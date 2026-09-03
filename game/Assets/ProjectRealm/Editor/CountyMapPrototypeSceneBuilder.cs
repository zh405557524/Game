using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Presentation;
using ProjectRealm.Presentation.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ProjectRealm.EditorTools
{
    public static class CountyMapPrototypeSceneBuilder
    {
        public const string ScenePath = ProjectRealmWorkspaceLayout.PrototypeScene;
        public const string DefinitionPath = ProjectRealmWorkspaceLayout.PrototypeDefinition;

        [MenuItem("Project Realm/Map/Create or Open County Map Prototype")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ProjectRealmWorkspaceLayout.EnsureFolder(System.IO.Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            if (System.IO.File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var definition = LoadOrCreateDefinition();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "CountyMapPrototype";

            ConfigureEnvironment();
            var mapRenderer = CreateMapRoot(definition);
            CreateCamera(definition);
            CreateLighting();

            EditorSceneManager.SaveScene(scene, ScenePath);
            mapRenderer.Rebuild();
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"County map prototype created and opened: {ScenePath}");
        }

        private static CountyMapDefinition LoadOrCreateDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CountyMapDefinition>(DefinitionPath);
            if (definition != null)
            {
                return definition;
            }

            ProjectRealmWorkspaceLayout.EnsureFolder(System.IO.Path.GetDirectoryName(DefinitionPath).Replace('\\', '/'));
            definition = ScriptableObject.CreateInstance<CountyMapDefinition>();
            definition.ResetToPrototype();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            return definition;
        }

        private static CountyMapRenderer CreateMapRoot(CountyMapDefinition definition)
        {
            var mapRoot = new GameObject("Project Realm County Map");
            mapRoot.AddComponent<ProjectRealmViewRoot>();
            var mapRenderer = mapRoot.AddComponent<CountyMapRenderer>();
            mapRenderer.SetDefinition(definition);
            return mapRenderer;
        }

        private static void CreateCamera(CountyMapDefinition definition)
        {
            var cameraObject = new GameObject("County Map Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 46f, -43f);
            cameraObject.transform.LookAt(Vector3.zero);

            var mapCamera = cameraObject.AddComponent<Camera>();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(0.58f, 0.54f, 0.44f);
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = 31f;
            mapCamera.nearClipPlane = 0.3f;
            mapCamera.farClipPlane = 180f;
            mapCamera.allowHDR = true;

            cameraObject.AddComponent<AudioListener>();
            var controller = cameraObject.AddComponent<CountyMapCameraController>();
            controller.Configure(definition.Size);
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Ink Wash Sun");
            lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var mapLight = lightObject.AddComponent<Light>();
            mapLight.type = LightType.Directional;
            mapLight.color = new Color(1f, 0.94f, 0.80f);
            mapLight.intensity = 1.15f;
            mapLight.shadows = LightShadows.Soft;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.55f, 0.48f);
            RenderSettings.reflectionIntensity = 0.25f;
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            scenes.AddRange(EditorBuildSettings.scenes.Where(scene => scene.path != ScenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
