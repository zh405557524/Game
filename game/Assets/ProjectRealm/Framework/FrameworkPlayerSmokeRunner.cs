using System;
using System.IO;
using System.Linq;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Infrastructure.Sqlite;
using SQLite;
using UnityEngine;

namespace ProjectRealm.UnityFramework
{
    /// <summary>
    /// 构建产物中的显式冒烟入口。只有传入专用命令行参数时才运行，
    /// 用隔离目录验证 Definition 加载、Tick、保存、重载与散列一致性。
    /// </summary>
    public static class FrameworkPlayerSmokeRunner
    {
        private const string ResultArgument = "-projectRealmFrameworkSmokeResult";
        private const string RootArgument = "-projectRealmFrameworkSmokeRoot";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunWhenExplicitlyRequested()
        {
            var resultPath = ReadArgument(ResultArgument);
            if (string.IsNullOrEmpty(resultPath))
            {
                return;
            }

            var rootPath = ReadArgument(RootArgument);
            try
            {
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    throw new InvalidOperationException($"{RootArgument} is required for an isolated smoke save.");
                }

                var asset = Resources.Load<SQLiteAsset>("realm_definition_ming1628_dev_v1");
                if (asset == null)
                {
                    throw new InvalidOperationException("The built player cannot load the Definition SQLiteAsset.");
                }

                var definitions = new SqliteWorldDefinitionStore(asset);
                var saves = new SqliteSaveGameStore(rootPath);
                var bootstrapper = new WorldBootstrapper(definitions, saves);
                var saveId = new StableId("built-player-smoke");
                var runtime = bootstrapper.StartNewWorld(new WorldBootstrapRequest(
                    saveId,
                    new StableId("MING1628"),
                    new WorldSeed(1628)));
                var tick = runtime.Advance(new AdvanceRequest(AdvanceUnit.Day));
                if (!tick.Committed)
                {
                    throw new InvalidOperationException("Built-player framework tick rolled back: " + tick.FailureReason);
                }

                runtime.Save();
                var loaded = bootstrapper.LoadWorld(new LoadWorldRequest(saveId));
                if (!loaded.CurrentStateHash.Equals(runtime.CurrentStateHash))
                {
                    throw new InvalidOperationException("Built-player save/reload state hashes differ.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                File.WriteAllLines(resultPath, new[]
                {
                    "result=passed",
                    "counties=" + loaded.Topology.Geography.Nodes.Count(node => node.Kind == SimulationNodeKind.County),
                    "modules=" + loaded.ModuleRegistry.Instances.Count,
                    "tick=" + loaded.Clock.TickSequence,
                    "state_sha256=" + loaded.CurrentStateHash.Sha256,
                    "definition_sha256=" + loaded.Ruleset.DefinitionContentHash,
                    "save_path=" + saves.GetSavePath(saveId)
                });
                UnityEngine.Application.Quit(0);
            }
            catch (Exception exception)
            {
                try
                {
                    var directory = Path.GetDirectoryName(resultPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
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

        private static string ReadArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
