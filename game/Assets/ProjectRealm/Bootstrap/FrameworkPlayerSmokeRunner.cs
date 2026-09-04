using System;
using System.IO;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using UnityEngine;

namespace ProjectRealm.Bootstrap
{
    /// <summary>只在显式命令行参数存在时，通过公开 Manager 验证构建产物的 Tick 与存档续跑。</summary>
    public static class FrameworkPlayerSmokeRunner
    {
        private const string ResultArgument = "-projectRealmFrameworkSmokeResult";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunWhenExplicitlyRequested()
        {
            var resultPath = ReadArgument(ResultArgument);
            if (string.IsNullOrEmpty(resultPath))
            {
                return;
            }

            try
            {
                var application = UnityEngine.Object.FindAnyObjectByType<RealmApplication>(FindObjectsInactive.Include);
                if (application?.Context == null)
                {
                    throw new InvalidOperationException("The built player did not start RealmApplication.");
                }

                var context = application.Context;
                var create = context.World.Create(new NewRealmWorldRequest("built-player-smoke", "MING1628", 1628));
                Require(create.Succeeded, create.Error);
                var tick = context.Simulation.Advance(RealmAdvanceUnit.Day);
                Require(tick.Succeeded && tick.Value.Committed, tick.Error, tick.Value?.FailureReason);
                var expectedHash = tick.Value.StateHash;
                Require(context.Saves.Save().Succeeded, null, "Built-player save failed.");
                Require(context.World.Close().Succeeded, null, "Built-player close failed.");
                var loaded = context.Saves.Load("built-player-smoke");
                Require(loaded.Succeeded, loaded.Error);
                if (!string.Equals(expectedHash, loaded.Value.StateHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Built-player save/reload state hashes differ.");
                }

                var diagnostics = context.Diagnostics.Query(pageSize: 1);
                Require(diagnostics.Succeeded, diagnostics.Error);
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                File.WriteAllLines(resultPath, new[]
                {
                    "result=passed",
                    "nodes=" + diagnostics.Value.World.GeographicNodeCount,
                    "modules=" + diagnostics.Value.World.ModuleInstanceCount,
                    "tick=" + diagnostics.Value.World.Tick,
                    "state_sha256=" + diagnostics.Value.World.StateHash
                });
                UnityEngine.Application.Quit(0);
            }
            catch (Exception exception)
            {
                try
                {
                    var directory = Path.GetDirectoryName(resultPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(resultPath, "result=failed\nerror=" + exception);
                }
                catch
                {
                    // Preserve the original smoke failure.
                }

                Debug.LogException(exception);
                UnityEngine.Application.Quit(2);
            }
        }

        private static void Require(bool condition, RealmError error, string fallback = null)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error == null ? fallback : error.Code + ": " + error.Message);
            }
        }

        private static string ReadArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
            }
            return null;
        }
    }
}
