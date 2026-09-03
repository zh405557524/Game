using System;
using System.IO;
using ProjectRealm.Presentation.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRealm.EditorTools
{
    public static class FiveTerrainMapBuilder
    {
        public const string ScenePath = ProjectRealmWorkspaceLayout.IntegrationScene;
        public const string DefinitionPath = ProjectRealmWorkspaceLayout.StudyDefinition;
        public const string OutputRoot = ProjectRealmWorkspaceLayout.CombinedGenerated;
        private const string TextureRoot = "Assets/ProjectRealm/Presentation/Map/Materials/Textures";

        [MenuItem("Project Realm/Map/Create or Open Five Terrain Map")]
        public static void Open()
        {
            if (!CanSwitchScene()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Build();
            ShowGame();
        }

        [MenuItem("Project Realm/Map/Rebuild Five Terrain Study")]
        public static void Rebuild()
        {
            if (!CanSwitchScene()) return;
            // New revision assets preserve the last scene's mesh/material files for recovery.
            if (File.Exists(ScenePath))
            {
                EnsureFolder(ProjectRealmWorkspaceLayout.ArchiveScenes);
                AssetDatabase.CopyAsset(ScenePath, AssetDatabase.GenerateUniqueAssetPath(
                    $"{ProjectRealmWorkspaceLayout.ArchiveScenes}/FiveTerrain-{DateTime.Now:yyyyMMdd-HHmmss}.unity"));
            }
            Build();
            ShowGame();
        }

        private static bool CanSwitchScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before opening/rebuilding the terrain study.");
                return false;
            }
            return UnityEngine.Application.isBatchMode || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        public static void Build()
        {
            EnsureFolder(Path.GetDirectoryName(DefinitionPath).Replace('\\', '/'));
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            var data = AssetDatabase.LoadAssetAtPath<FiveTerrainDefinition>(DefinitionPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<FiveTerrainDefinition>();
                AssetDatabase.CreateAsset(data, DefinitionPath);
            }
            string revision = $"{OutputRoot}/r{DateTime.Now:yyyyMMdd-HHmmss}";
            EnsureFolder(revision);
            var shader = Shader.Find("ProjectRealm/Map/FiveTerrainLandscape");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Terrain shader is missing or has errors.");
            var material = new Material(shader) { name = "Five Terrain / blended ink landscape" };
            material.SetColor("_PlainColor", new Color(0.77f, 0.77f, 0.59f).linear);
            material.SetColor("_HillsColor", new Color(0.57f, 0.65f, 0.48f).linear);
            material.SetColor("_MountainColor", new Color(0.48f, 0.56f, 0.50f).linear);
            material.SetColor("_PlateauColor", new Color(0.74f, 0.67f, 0.48f).linear);
            material.SetColor("_BasinColor", new Color(0.64f, 0.73f, 0.53f).linear);
            string[] textures = { "terrain-plain-v2", "terrain-hills-v4", "terrain-mountain-v4", "terrain-plateau-v1", "terrain-basin-v2" };
            string[] slots = { "_PlainTex", "_HillsTex", "_MountainTex", "_PlateauTex", "_BasinTex" };
            for (int i = 0; i < slots.Length; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureRoot}/{textures[i]}.png");
                if (texture == null) throw new FileNotFoundException(textures[i]);
                material.SetTexture(slots[i], texture);
            }
            material.SetVector("_MapExtent", new Vector4(data.width * 0.5f, data.depth * 0.5f, 0, 0));
            AssetDatabase.CreateAsset(material, $"{revision}/Landscape.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "FiveTerrainMap";
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.70f, 0.72f, 0.65f);
            var root = new GameObject("Five Terrain / continuous landforms");
            for (int row = 0; row < data.rows; row++)
            for (int column = 0; column < data.columns; column++)
            {
                var mesh = data.BuildChunk(column, row);
                AssetDatabase.CreateAsset(mesh, $"{revision}/Chunk-{column:D2}-{row:D2}.asset");
                var chunk = new GameObject($"Terrain chunk {column:D2}-{row:D2}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                chunk.transform.SetParent(root.transform, false);
                chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
                chunk.GetComponent<MeshCollider>().sharedMesh = mesh;
                var renderer = chunk.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            }
            var sun = new GameObject("Soft northwest light").AddComponent<Light>();
            sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(48, 35, 0);
            sun.color = new Color(1f, 0.96f, 0.87f); sun.intensity = 1;
            sun.shadows = LightShadows.Soft; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.3f;
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(FiveTerrainCamera), typeof(FiveTerrainHud));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.89f, 0.88f, 0.81f);
            camera.nearClipPlane = 0.3f; camera.farClipPlane = 1000f; camera.allowHDR = false;
            cameraObject.GetComponent<FiveTerrainCamera>().Configure(data);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Five terrain map built: {ScenePath}; {data.columns * data.rows} chunks; {data.columns * data.rows * data.cellsPerChunk * data.cellsPerChunk * 2} triangles. Learning scene unchanged.");
        }

        [MenuItem("Project Realm/Map/Export Five Terrain Views")]
        public static void ExportViews()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open FiveTerrainMap outside Play Mode before exporting.");
            var source = Camera.main;
            string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,
                "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/04_五地形样板/renders", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(directory);
            var go = new GameObject("Temporary landscape QA") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
            var controller = go.AddComponent<FiveTerrainCamera>(); controller.Configure(AssetDatabase.LoadAssetAtPath<FiveTerrainDefinition>(DefinitionPath));
            try
            {
                controller.Home(true); Render(camera, 1920, 1080, Path.Combine(directory, "overview.png"));
                for (int i = 0; i < 5; i++)
                {
                    controller.FocusTerrain(i, true);
                    Render(camera, 1440, 1080, Path.Combine(directory, $"{i + 1}-{(LandformKind)i}.png"));
                }
                Debug.Log($"Five terrain render export: {directory}");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void Render(Camera camera, int width, int height, string path)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                camera.aspect = (float)width / height;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render requests unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void ShowGame()
        {
            var type = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (type != null) EditorWindow.GetWindow(type).Show();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
