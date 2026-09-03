using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectRealm.Presentation.Map.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ProjectRealm.EditorTools
{
    public static class RiverStudyBuilder
    {
        private const string TextureRoot = "Assets/ProjectRealm/Presentation/Map/Materials/Textures/";

        [MenuItem("Project Realm/Debug/Water/Create or Open River Study")]
        public static void Open()
        {
            if (!CanChangeScene()) return;
            if (!File.Exists(WaterDebugCases.RiverScene)) Build();
            else ShowScene();
        }

        [MenuItem("Project Realm/Debug/Water/Rebuild River Study From Input")]
        public static void Rebuild()
        {
            if (!CanChangeScene()) return;
            Build();
        }

        private static bool CanChangeScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode before creating or opening a study."); return false; }
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        private static void Build()
        {
            MapDebugWorkbench.LoadCatalog();
            ProjectRealmWorkspaceLayout.EnsureFolder(WaterDebugCases.RiverData);
            var definition = AssetDatabase.LoadAssetAtPath<RiverStudyDefinition>(WaterDebugCases.RiverData + "/RiverStudy.asset");
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<RiverStudyDefinition>();
                definition.waterTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "water-river-v1.png");
                definition.groundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "terrain-plain-v2.png");
                AssetDatabase.CreateAsset(definition, WaterDebugCases.RiverData + "/RiverStudy.asset");
            }
            if (!definition.Validate(out string reason)) throw new InvalidOperationException(reason);
            if (definition.waterTexture == null || definition.groundTexture == null) throw new InvalidOperationException("Assign both source textures before building.");
            Shader waterShader = RequireShader("ProjectRealm/Map/InkRiverStudy"), groundShader = RequireShader("ProjectRealm/Map/RiverStudyGround");
            var samples = RiverStudyGeometry.Sample(definition);
            var surface = RiverStudyGeometry.Water(samples);
            int waterVertices = surface.vertexCount, waterTriangles = surface.triangles.Length / 3;
            var ground = RiverStudyGeometry.Ground(definition, samples);
            var arrows = RiverStudyGeometry.FlowArrows(samples, definition.groundSize);
            string revision = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string output = WaterDebugCases.RiverOutput + "/" + revision;
            ProjectRealmWorkspaceLayout.EnsureFolder(output);
            AssetDatabase.CreateAsset(surface, output + "/WaterSurface.asset");
            AssetDatabase.CreateAsset(ground, output + "/RiverBed.asset");
            AssetDatabase.CreateAsset(arrows, output + "/FlowArrows.asset");
            var wet = new Material(waterShader) { name = "River • blue-grey ink" };
            wet.SetTexture("_BaseMap", definition.waterTexture); wet.SetFloat("_TextureLength", definition.textureLength);
            wet.SetVector("_MapSize", new Vector4(definition.groundSize.x, definition.groundSize.y, 0, 0));
            var dry = new Material(groundShader) { name = "Banks • damp edge and floodplain" }; dry.SetTexture("_BaseMap", definition.groundTexture);
            var arrowMaterial = new Material(RequireShader("Universal Render Pipeline/Unlit")) { name = "Diagnostic arrows • not gameplay flow" };
            arrowMaterial.SetColor("_BaseColor", new Color(0.77f, 0.38f, 0.13f));
            AssetDatabase.CreateAsset(wet, output + "/Water.mat"); AssetDatabase.CreateAsset(dry, output + "/Banks.mat"); AssetDatabase.CreateAsset(arrowMaterial, output + "/Arrows.mat");
            if (File.Exists(WaterDebugCases.RiverScene))
            {
                string backup = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../builds/water-study-backups", revision));
                Directory.CreateDirectory(backup);
                File.Copy(WaterDebugCases.RiverScene, Path.Combine(backup, "RiverStudy.unity"));
                File.Copy(WaterDebugCases.RiverScene + ".meta", Path.Combine(backup, "RiverStudy.unity.meta"));
                Debug.Log("Previous river scene preserved at " + backup);
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            bool saved = false;
            try
            {
                RenderSettings.skybox = null; RenderSettings.fog = false;
                MeshRenderer bed = Object("河床与河岸 / generated", ground, dry), water = Object("河流水面 / original texture", surface, wet);
                MeshRenderer vectors = Object("下游方向 / diagnostics only", arrows, arrowMaterial); vectors.gameObject.SetActive(false);
                var cameraObject = new GameObject("River Study Camera", typeof(Camera), typeof(AudioListener), typeof(RiverStudyView)); cameraObject.tag = "MainCamera";
                var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.92f, 0.91f, 0.85f); camera.orthographic = true; camera.orthographicSize = 66;
                camera.allowHDR = false; camera.nearClipPlane = 0.3f; camera.farClipPlane = 500;
                SetCamera(camera, Vector3.zero, 55, 66);
                var controller = cameraObject.GetComponent<RiverStudyView>(); controller.definition = definition; controller.water = water; controller.flowArrows = vectors.gameObject;
                // A different loaded scene cannot be overwritten by SaveScene.
                // Its saved copy was backed up above; close it only once the replacement is ready.
                var oldTarget = SceneManager.GetSceneByPath(WaterDebugCases.RiverScene);
                if (oldTarget.IsValid() && oldTarget != scene) EditorSceneManager.CloseScene(oldTarget, true);
                if (!EditorSceneManager.SaveScene(scene, WaterDebugCases.RiverScene))
                    throw new IOException("Could not save the rebuilt river study scene.");
                saved = true;
            }
            finally
            {
                if (!saved)
                {
                    if (previous.IsValid())
                    {
                        SceneManager.SetActiveScene(previous);
                        EditorSceneManager.CloseScene(scene, true);
                    }
                    else if (File.Exists(WaterDebugCases.RiverScene)) EditorSceneManager.OpenScene(WaterDebugCases.RiverScene);
                }
            }
            // Opening a scene unloads unused assets, including an unreferenced catalogue.
            ShowScene();
            var catalog = MapDebugWorkbench.LoadCatalog();
            var item = catalog.cases.First(x => x.id == "water/01_River"); item.state = DebugReviewState.InProgress;
            item.findings = "河流独立样板已建立：路径、宽度、河床、岸线、沿程流动。待检查近远景、弯道与水面衔接；未人工验收。";
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log($"River study created: {samples.Length} sections, {waterVertices} water vertices, {waterTriangles} water triangles; generated assets: {output}");
        }

        private static MeshRenderer Object(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        private static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name); if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Missing or invalid shader: " + name); return shader;
        }
        private static void ShowScene()
        {
            EditorSceneManager.OpenScene(WaterDebugCases.RiverScene);
            var gameType = Type.GetType("UnityEditor.GameView,UnityEditor"); if (gameType != null) EditorWindow.GetWindow(gameType).Show();
        }
        private static void SetCamera(Camera camera, Vector3 target, float pitch, float size)
        {
            var rotation = Quaternion.Euler(pitch, 0, 0); camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * 240, rotation); camera.orthographicSize = size;
        }

        [MenuItem("Project Realm/Debug/Water/Export River Diagnostics")]
        public static void Export()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var source = UnityEngine.Object.FindAnyObjectByType<RiverStudyView>();
            if (source == null) throw new InvalidOperationException("Open RiverStudy first.");
            string folder = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Water/01_River", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(folder);
            var oldBlock = new MaterialPropertyBlock(); source.water.GetPropertyBlock(oldBlock);
            bool waterActive = source.water.enabled, arrowsActive = source.flowArrows.activeSelf;
            var go = new GameObject("Temporary water QA camera") { hideFlags = HideFlags.HideAndDontSave }; var camera = go.AddComponent<Camera>();
            camera.CopyFrom(source.GetComponent<Camera>()); camera.enabled = false;
            var records = new List<string> { "image,case_id,pitch,orthographic_size,flow_distance,water_visible,flow_arrows" };
            try
            {
                Capture("01-overview", Vector3.zero, 55, 66, 0, true, false);
                Capture("02-flow-phase", Vector3.zero, 55, 66, 6, true, false);
                Capture("03-near-bank", new Vector3(4, 0, 10), 55, 32, 0, true, false);
                Capture("04-topology", Vector3.zero, 90, 72, 0, true, false);
                Capture("05-river-bed", new Vector3(4, 0, 10), 55, 32, 0, false, false);
                Capture("06-downstream", Vector3.zero, 90, 72, 0, true, true);
                File.WriteAllLines(Path.Combine(folder, "conditions.csv"), records);
                var catalog = MapDebugWorkbench.LoadCatalog(); var item = catalog.cases.First(x => x.id == "water/01_River");
                item.evidencePath = folder; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                Debug.Log("River diagnostics: " + folder);
            }
            finally { source.water.enabled = waterActive; source.flowArrows.SetActive(arrowsActive); source.water.SetPropertyBlock(oldBlock); UnityEngine.Object.DestroyImmediate(go); }

            void Capture(string name, Vector3 focus, float pitch, float size, float flow, bool showWater, bool showArrows)
            {
                SetCamera(camera, focus, pitch, size);
                var block = new MaterialPropertyBlock(); block.SetFloat("_FlowDistance", flow); block.SetFloat("_TextureLength", source.definition.textureLength);
                source.water.SetPropertyBlock(block); source.water.enabled = showWater; source.flowArrows.SetActive(showArrows);
                Render(camera, Path.Combine(folder, name + ".png"));
                records.Add($"{name},{source.definition.caseId},{pitch},{size},{flow},{showWater},{showArrows}");
            }
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1600, height = 1040;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D image = null;
            try
            {
                camera.aspect = (float)width / height;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render request unsupported.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (image != null) UnityEngine.Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(target); }
        }
    }
}
