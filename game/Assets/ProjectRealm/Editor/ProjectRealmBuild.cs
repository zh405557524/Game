using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    public static class ProjectRealmBuild
    {
        private const string BuildOutputArgument = "-buildOutput";

        [MenuItem("Project Realm/Build/macOS Development")]
        public static void BuildMacOSDevelopment()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("At least one enabled scene is required for a player build.");
            }

            var outputPath = ReadArgument(BuildOutputArgument) ?? Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../builds/ProjectRealm-macOS/Project Realm.app"));
            var outputDirectory = Path.GetDirectoryName(outputPath);

            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException($"Invalid build output path: {outputPath}");
            }

            Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"macOS build failed with result {report.summary.result} and {report.summary.totalErrors} error(s).");
            }

            Debug.Log($"Project Realm macOS development build created at {outputPath}");
        }

        private static string ReadArgument(string argumentName)
        {
            var arguments = Environment.GetCommandLineArgs();

            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], argumentName, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
