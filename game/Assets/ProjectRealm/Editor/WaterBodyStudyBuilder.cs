using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static class WaterBodyStudyBuilder
    {
        private const string Textures = "Assets/ProjectRealm/Presentation/Map/Materials/Textures/";
        public static string ScenePath(WaterStudyKind kind) => ProjectRealmWorkspaceLayout.DebugScenes + "/02_Water/" + Folder(kind) + "/" + kind + "Study.unity";
        private static string Folder(WaterStudyKind kind) => ((int)kind + 1).ToString("D2") + "_" + kind;
        private static string InputFolder(WaterStudyKind kind) => ProjectRealmWorkspaceLayout.TestData + "/Map/02_Water/" + Folder(kind);
        private static string OutputFolder(WaterStudyKind kind) => ProjectRealmWorkspaceLayout.Generated + "/Map/02_Water/" + Folder(kind);

        [MenuItem("Project Realm/Debug/Water/Create Missing Five Water Studies")]
        public static void CreateMissing()
        {
            if (!CanChange()) return;
            foreach (WaterStudyKind kind in Enum.GetValues(typeof(WaterStudyKind))) if (!File.Exists(ScenePath(kind))) Build(kind);
            OpenScene(WaterStudyKind.Lake);
        }
        [MenuItem("Project Realm/Debug/Water/Rebuild Five Water Body Studies")]
        public static void RebuildFive()
        {
            if (!CanChange()) return;
            foreach (WaterStudyKind kind in Enum.GetValues(typeof(WaterStudyKind))) Build(kind);
            OpenScene(WaterStudyKind.Lake);
        }
        [MenuItem("Project Realm/Debug/Water/Open Stream Study")] public static void Stream() => Open(WaterStudyKind.Stream);
        [MenuItem("Project Realm/Debug/Water/Open Lake Study")] public static void Lake() => Open(WaterStudyKind.Lake);
        [MenuItem("Project Realm/Debug/Water/Open Pond Study")] public static void Pond() => Open(WaterStudyKind.Pond);
        [MenuItem("Project Realm/Debug/Water/Open Wetland Study")] public static void Wetland() => Open(WaterStudyKind.Wetland);
        [MenuItem("Project Realm/Debug/Water/Open Coast Study")] public static void Coast() => Open(WaterStudyKind.Coast);
        private static void Open(WaterStudyKind kind) { if (!CanChange()) return; if (!File.Exists(ScenePath(kind))) Build(kind); OpenScene(kind); }
        private static bool CanChange()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode first."); return false; }
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }
        private static void OpenScene(WaterStudyKind kind)
        {
            EditorSceneManager.OpenScene(ScenePath(kind));
            var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type).Show();
        }
        [MenuItem("Project Realm/Debug/Water/Rebuild Current Water Body Study")]
        public static void RebuildCurrent()
        {
            if (!CanChange()) return;
            var view = UnityEngine.Object.FindAnyObjectByType<WaterBodyStudyView>();
            if (view == null) throw new InvalidOperationException("Open one of the five water body studies first (river has its own rebuild menu).");
            WaterStudyKind kind = view.definition.kind; Build(kind); OpenScene(kind);
        }

        private static void Build(WaterStudyKind kind)
        {
            MapDebugWorkbench.LoadCatalog();
            ProjectRealmWorkspaceLayout.EnsureFolder(InputFolder(kind)); ProjectRealmWorkspaceLayout.EnsureFolder(Path.GetDirectoryName(ScenePath(kind)).Replace('\\', '/'));
            string input = InputFolder(kind) + "/" + kind + "Study.asset";
            var definition = AssetDatabase.LoadAssetAtPath<WaterBodyStudyDefinition>(input);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WaterBodyStudyDefinition>(); definition.SetDefaults(kind);
                definition.waterTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + (kind == WaterStudyKind.Stream ? "water-river-v1.png" : "water-lake-v1.png"));
                definition.groundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "terrain-plain-v2.png");
                AssetDatabase.CreateAsset(definition, input);
            }
            if (definition.kind != kind || !definition.Validate(out _)) throw new InvalidOperationException("Invalid or mismatched water study input: " + input);
            if (definition.waterTexture == null || definition.groundTexture == null) throw new InvalidOperationException("Missing source texture.");
            var waterShader = Shader.Find("ProjectRealm/Map/WaterBodyStudy"); var landShader = Shader.Find("ProjectRealm/Map/WaterStudyGround");
            if (waterShader == null || landShader == null || ShaderUtil.ShaderHasError(waterShader) || ShaderUtil.ShaderHasError(landShader)) throw new InvalidOperationException("Study shader missing or invalid.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), output = OutputFolder(kind) + "/" + stamp;
            ProjectRealmWorkspaceLayout.EnsureFolder(output);
            var field = new WaterStudyField(definition);
            var land = WaterBodyStudyGeometry.Grid(field, false);
            var surface = kind == WaterStudyKind.Stream ? WaterBodyStudyGeometry.Stream(field) : WaterBodyStudyGeometry.Grid(field, true);
            var details = WaterBodyStudyGeometry.ReferenceDetails(field);
            int count = surface.vertexCount;
            AssetDatabase.CreateAsset(land, output + "/BedAndBanks.asset"); AssetDatabase.CreateAsset(surface, output + "/WaterSurface.asset"); AssetDatabase.CreateAsset(details, output + "/ReferenceDetails.asset");
            var water = new Material(waterShader) { name = definition.DisplayName + " / water" };
            water.SetTexture("_BaseMap", definition.waterTexture); water.SetColor("_DeepColor", definition.deepColor); water.SetColor("_ShallowColor", definition.shallowColor);
            water.SetFloat("_MaxDepth", definition.depth); water.SetFloat("_Directional", kind == WaterStudyKind.Stream ? 1 : 0); water.SetFloat("_Coast", kind == WaterStudyKind.Coast ? 1 : 0);
            water.SetVector("_MapSize", new Vector4(definition.size.x, definition.size.y, 0, 0));
            var ground = new Material(landShader) { name = definition.DisplayName + " / banks" }; ground.SetTexture("_BaseMap", definition.groundTexture);
            AssetDatabase.CreateAsset(water, output + "/Water.mat"); AssetDatabase.CreateAsset(ground, output + "/Banks.mat");
            File.WriteAllText(output + "/input-snapshot.json", JsonUtility.ToJson(definition, true));
            File.WriteAllText(output + "/build-record.txt", "input=" + input + "\nwaterTexture=" + AssetDatabase.GetAssetPath(definition.waterTexture) + "\nwaterGuid=" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.waterTexture)) + "\nwaterVertices=" + count + "\nstatus=generated, not visually approved\n");
            string scenePath = ScenePath(kind);
            if (File.Exists(scenePath))
            {
                string backup = Path.GetFullPath(Path.Combine(Application.dataPath, "../../builds/water-body-study-backups", Folder(kind), stamp)); Directory.CreateDirectory(backup);
                File.Copy(scenePath, Path.Combine(backup, kind + "Study.unity")); File.Copy(scenePath + ".meta", Path.Combine(backup, kind + "Study.unity.meta"));
            }
            var previous = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene); bool saved = false;
            try
            {
                RenderSettings.skybox = null; RenderSettings.fog = false;
                Add("水底与岸线 / generated", land, ground); var wet = Add("水面 / original texture", surface, water);
                var reference = Add("可隐藏尺度参照 / not vegetation data", details, ground); reference.gameObject.SetActive(definition.referenceDetails);
                var go = new GameObject(kind + " Study Camera", typeof(Camera), typeof(AudioListener), typeof(WaterBodyStudyView)); go.tag = "MainCamera";
                var camera = go.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.92f, 0.91f, 0.85f);
                camera.orthographic = true; camera.allowHDR = false; camera.nearClipPlane = 0.3f; camera.farClipPlane = 600; Position(camera, 55, definition.ViewSize);
                var controller = go.GetComponent<WaterBodyStudyView>(); controller.definition = definition; controller.surface = wet; controller.referenceDetails = reference.gameObject;
                var old = SceneManager.GetSceneByPath(scenePath); if (old.IsValid() && old != scene) EditorSceneManager.CloseScene(old, true);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save " + scenePath); saved = true;
            }
            finally
            {
                if (!saved)
                {
                    if (previous.IsValid()) { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
                    else if (File.Exists(scenePath)) EditorSceneManager.OpenScene(scenePath);
                }
            }
            OpenScene(kind);
            var catalog = MapDebugWorkbench.LoadCatalog(); var item = catalog.cases.First(x => x.id == "water/" + Folder(kind));
            item.scenePath = scenePath; item.state = DebugReviewState.InProgress;
            item.findings = "独立样板已建立，待逐项渲染与运行检查；不代表美术验收通过。";
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("Water body study saved: " + kind + ", output=" + output + ", waterVertices=" + count);
        }
        private static MeshRenderer Add(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        private static void Position(Camera camera, float pitch, float size)
        { var rotation = Quaternion.Euler(pitch, 0, 0); camera.transform.SetPositionAndRotation(-(rotation * Vector3.forward) * 250, rotation); camera.orthographicSize = size; }

        [MenuItem("Project Realm/Debug/Water/Export Five Water Body Diagnostics")]
        public static void ExportAll()
        {
            if (!CanChange()) return;
            foreach (WaterStudyKind kind in Enum.GetValues(typeof(WaterStudyKind))) if (!File.Exists(ScenePath(kind))) throw new InvalidOperationException("Create the missing studies first: " + kind);
            var setup = EditorSceneManager.GetSceneManagerSetup(); string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { foreach (WaterStudyKind kind in Enum.GetValues(typeof(WaterStudyKind))) { OpenScene(kind); ExportCurrent(stamp); } }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        private static void ExportCurrent(string stamp)
        {
            var source = UnityEngine.Object.FindAnyObjectByType<WaterBodyStudyView>(); var definition = source.definition;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Water", definition.Folder, stamp)); Directory.CreateDirectory(folder);
            var go = new GameObject("Temporary water QA camera") { hideFlags = HideFlags.HideAndDontSave }; var camera = go.AddComponent<Camera>(); camera.CopyFrom(source.GetComponent<Camera>()); camera.enabled = false;
            var original = new MaterialPropertyBlock(); source.surface.GetPropertyBlock(original); bool enabled = source.surface.enabled, details = source.referenceDetails.activeSelf;
            var conditions = new List<string> { "image,case,pitch,size,phase,mode,reference_details" };
            try
            {
                Capture("01-overview", 55, definition.ViewSize, 0, 0, true);
                Capture("02-near", 55, definition.ViewSize * 0.5f, 0, 0, true);
                Capture("03-top-depth", 90, definition.ViewSize * 1.05f, 0, 1, false);
                Capture("04-bed", 55, definition.ViewSize * 0.65f, 0, 2, false);
                Capture("05-phase", 55, definition.ViewSize, 8, 0, true);
                File.WriteAllLines(Path.Combine(folder, "conditions.csv"), conditions);
                var catalog = MapDebugWorkbench.LoadCatalog(); var item = catalog.cases.First(x => x.id == "water/" + definition.Folder); item.evidencePath = folder; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                Debug.Log("Water body diagnostics: " + folder);
            }
            finally { source.surface.enabled = enabled; source.surface.SetPropertyBlock(original); source.referenceDetails.SetActive(details); UnityEngine.Object.DestroyImmediate(go); }
            void Capture(string name, float pitch, float size, float phase, int mode, bool references)
            {
                Position(camera, pitch, size); var properties = new MaterialPropertyBlock(); properties.SetFloat("_Phase", phase); properties.SetFloat("_PreviewMode", mode == 1 ? 1 : 0);
                source.surface.SetPropertyBlock(properties); source.surface.enabled = mode != 2; source.referenceDetails.SetActive(references); Render(camera, Path.Combine(folder, name + ".png"));
                conditions.Add(string.Join(",", name, definition.CaseId, pitch.ToString(CultureInfo.InvariantCulture), size.ToString(CultureInfo.InvariantCulture), phase.ToString(CultureInfo.InvariantCulture), mode, references));
            }
        }
        private static void Render(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1600, 1040, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                camera.aspect = 1600f / 1040; var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render requests unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target; pixels = new Texture2D(1600, 1040, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1600, 1040), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
        }
    }
}
