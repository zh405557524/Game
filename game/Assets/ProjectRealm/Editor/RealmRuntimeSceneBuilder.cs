using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectRealm.Bootstrap;
using ProjectRealm.UnityPresentation.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRealm.EditorTools
{
    /// <summary>创建只有代码依赖的三个运行时场景，并把 Bootstrap 固定为 Build Settings 第一项。</summary>
    public static class RealmRuntimeSceneBuilder
    {
        public const string BootstrapScene = "Assets/Scenes/Runtime/00_Bootstrap.unity";
        public const string MainMenuScene = "Assets/Scenes/Runtime/10_MainMenu.unity";
        public const string GameplayScene = "Assets/Scenes/Runtime/20_Gameplay.unity";
        public const string CountyMapScene = "Assets/Scenes/Debug/Map/90_Integration/CountyMapPrototype/CountyMapPrototype.unity";
        public const string RuntimeTheme = "Assets/ProjectRealm/Presentation/Resources/ProjectRealmRuntimeTheme.tss";
        public const string RuntimePanelSettings = "Assets/ProjectRealm/Presentation/Resources/ProjectRealmRuntimePanelSettings.asset";

        [MenuItem("Project Realm/Simulation/Rebuild Runtime Scenes")]
        public static void RebuildRuntimeScenes()
        {
            EnsureDirectory();
            EnsurePanelSettings();
            BuildScene(BootstrapScene, scene =>
            {
                var application = new GameObject("Realm Application");
                application.AddComponent<RealmApplication>();
                SceneManager.MoveGameObjectToScene(application, scene);

                var faultRoot = new GameObject("Framework Fault UI Root");
                SceneManager.MoveGameObjectToScene(faultRoot, scene);
            });
            BuildScene(MainMenuScene, scene =>
            {
                var screen = new GameObject("Main Menu Screen");
                screen.AddComponent<MainMenuScreenView>();
                SceneManager.MoveGameObjectToScene(screen, scene);
            });
            BuildScene(GameplayScene, scene =>
            {
                var screen = new GameObject("Gameplay Screen");
                screen.AddComponent<GameplayScreenView>();
                SceneManager.MoveGameObjectToScene(screen, scene);
            });

            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Realm runtime scenes rebuilt; 00_Bootstrap is the first enabled scene.");
        }

        private static void BuildScene(string path, Action<Scene> populate)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                scene.name = Path.GetFileNameWithoutExtension(path);
                populate(scene);
                if (!EditorSceneManager.SaveScene(scene, path))
                {
                    throw new InvalidOperationException($"Failed to save runtime scene '{path}'.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void UpdateBuildSettings()
        {
            var required = new[] { BootstrapScene, MainMenuScene, GameplayScene, CountyMapScene };
            var remaining = EditorBuildSettings.scenes
                .Select(item => item.path)
                .Where(path => !required.Contains(path, StringComparer.Ordinal))
                .ToList();
            var paths = new List<string>(required);
            paths.AddRange(remaining);
            EditorBuildSettings.scenes = paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.Ordinal)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToArray();
        }

        private static void EnsureDirectory()
        {
            var directory = Path.GetDirectoryName(BootstrapScene);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("The runtime scene directory is invalid.");
            }

            Directory.CreateDirectory(directory);
        }

        private static void EnsurePanelSettings()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(RuntimeTheme);
            if (theme == null)
            {
                throw new InvalidOperationException($"Runtime UI theme '{RuntimeTheme}' is missing.");
            }

            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(RuntimePanelSettings);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                settings.name = "ProjectRealmRuntimePanelSettings";
                AssetDatabase.CreateAsset(settings, RuntimePanelSettings);
            }

            settings.themeStyleSheet = theme;
            EditorUtility.SetDirty(settings);
        }
    }
}
