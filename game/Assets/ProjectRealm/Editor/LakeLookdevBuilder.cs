using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ProjectRealm.UnityPresentation.Map.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ProjectRealm.EditorTools
{
    public static class LakeLookdevBuilder
    {
        public const string ScenePath = "Assets/Scenes/Debug/Map/02_Water/03_Lake/LookdevV3/LakeLookdevV3.unity";
        public const string ProfilePath = "Assets/ProjectRealm/Development/TestData/Map/02_Water/03_Lake/LookdevV3/LakeLookdevV3.asset";
        public const string Sources = "Assets/ProjectRealm/Presentation/Map/Materials/Textures/Water/03_Lake/LookdevV3";
        public const string Generated = "Assets/ProjectRealm/Development/Generated/Map/02_Water/03_Lake/LookdevV3";
        public const string BaselineScene = "Assets/Scenes/Debug/Map/02_Water/03_Lake/LakeStudy.unity";
        private const string BaselineInput = "Assets/ProjectRealm/Development/TestData/Map/02_Water/03_Lake/LakeStudy.asset";
        private const string Evidence = "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Water/03_Lake/LookdevV3";

        [MenuItem("Project Realm/Debug/Water/Open Lake V3 Quality Study")]
        public static void Open()
        {
            if (!CanChange()) return;
            if (!File.Exists(ScenePath)) BuildNew();
            EditorSceneManager.OpenScene(ScenePath);
            var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type).Show();
        }

        // Deliberately create-once. Existing study scenes, source images, and saved lookdev edits are never overwritten.
        private static void BuildNew()
        {
            var definition = AssetDatabase.LoadAssetAtPath<WaterBodyStudyDefinition>(BaselineInput);
            if (definition == null || definition.kind != WaterStudyKind.Lake || !definition.Validate(out _)) throw new InvalidOperationException("Missing valid lake baseline.");
            var dependencies = AssetDatabase.GetDependencies(BaselineScene);
            T Dependency<T>(string name) where T : UnityEngine.Object => dependencies.Select(AssetDatabase.LoadAssetAtPath<T>).Single(x => x != null && x.name == name);
            var bed = Dependency<Mesh>("BedAndBanks"); var surface = Dependency<Mesh>("WaterSurface");
            var legacyWater = Dependency<Material>("Water"); var legacyBanks = Dependency<Material>("Banks");
            var waterShader = ValidShader("ProjectRealm/Map/LakeWaterLookdev"); var shoreShader = ValidShader("ProjectRealm/Map/LakeShoreLookdev");
            var rawShader = ValidShader("Universal Render Pipeline/Unlit");
            foreach (string file in new[] { "lake-water-ink-v3.png", "lake-shore-ink-v3.png" }) ConfigureSource(Sources + "/" + file);
            EnsureParent(ProfilePath); EnsureParent(ScenePath);
            var profile = AssetDatabase.LoadAssetAtPath<LakeLookdevProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<LakeLookdevProfile>(); profile.baseline = definition;
                profile.waterColor = AssetDatabase.LoadAssetAtPath<Texture2D>(Sources + "/lake-water-ink-v3.png");
                profile.shoreSediment = AssetDatabase.LoadAssetAtPath<Texture2D>(Sources + "/lake-shore-ink-v3.png");
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            if (!profile.Validate(out var reason)) throw new InvalidOperationException(reason);
            string output = Generated + "/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"); ProjectRealmWorkspaceLayout.EnsureFolder(output);
            var onlyTexture = new Material(legacyWater) { name = "02 New texture old shader" }; onlyTexture.SetTexture("_BaseMap", profile.waterColor);
            var newWater = new Material(waterShader) { name = "03 Water V3" }; newWater.SetTexture("_BaseMap", profile.waterColor); newWater.SetFloat("_TileSize", profile.waterTileSize);
            var newBanks = new Material(shoreShader) { name = "04 Shore V3" }; newBanks.SetTexture("_BaseMap", profile.shoreSediment); newBanks.SetTexture("_GrassMap", definition.groundTexture);
            newBanks.SetFloat("_TileSize", profile.shoreTileSize); newBanks.SetFloat("_ShoreWidth", profile.shoreWidth); newBanks.SetColor("_LandColor", profile.landColor);
            AssetDatabase.CreateAsset(onlyTexture, output + "/02_NewTextureOldShader.mat"); AssetDatabase.CreateAsset(newWater, output + "/03_WaterV3.mat"); AssetDatabase.CreateAsset(newBanks, output + "/04_ShoreV3.mat");
            var tileMesh = TileMesh(); AssetDatabase.CreateAsset(tileMesh, output + "/Raw3x3TileBoard.asset");
            var rawWater = new Material(rawShader) { name = "Raw water — no color correction" }; rawWater.SetTexture("_BaseMap", profile.waterColor);
            var rawSediment = new Material(rawShader) { name = "Raw sediment — no color correction" }; rawSediment.SetTexture("_BaseMap", profile.shoreSediment);
            AssetDatabase.CreateAsset(rawWater, output + "/RawWater.mat"); AssetDatabase.CreateAsset(rawSediment, output + "/RawSediment.mat");
            var previous = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.skybox = null; RenderSettings.fog = false;
                var root = new GameObject("湖泊对照 / unchanged baseline geometry");
                var banks = Add("原湖床网格 / 分阶段材质", root.transform, bed, newBanks); var water = Add("原水面网格 / 分阶段材质", root.transform, surface, newWater);
                var tiles = new GameObject("原图 3x3 / no seam repair");
                Add("左 水纹", tiles.transform, tileMesh, rawWater).transform.position = new Vector3(-37, 0, 0);
                Add("右 岸边底纹", tiles.transform, tileMesh, rawSediment).transform.position = new Vector3(37, 0, 0);
                var go = new GameObject("Lake V3 Quality Camera", typeof(Camera), typeof(AudioListener), typeof(LakeLookdevView)); go.tag = "MainCamera";
                var camera = go.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.92f, .91f, .85f);
                camera.orthographic = true; camera.allowHDR = false; camera.nearClipPlane = .3f; camera.farClipPlane = 600;
                var view = go.GetComponent<LakeLookdevView>(); view.profile = profile; view.water = water; view.banks = banks; view.lakeRoot = root; view.tileRoot = tiles;
                view.waterStages = new[] { legacyWater, onlyTexture, newWater, newWater }; view.bankStages = new[] { legacyBanks, legacyBanks, legacyBanks, newBanks };
                view.Frame(); view.Apply();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save lookdev scene.");
                var catalog = MapDebugWorkbench.LoadCatalog();
                var item = catalog.cases.Find(x => x.id == "lookdev/lake-v3");
                if (item == null)
                {
                    item = new MapDebugCase { id = "lookdev/lake-v3", displayName = "湖泊 V3 品质基准", layer = 1, scenePath = ScenePath, testDataPath = Path.GetDirectoryName(ProfilePath).Replace('\\', '/'), generatedPath = Generated, state = DebugReviewState.InProgress, findings = "候选：1—4 分阶段对照，5 检查原图平铺。待技术与视觉检查，非美术通过。" };
                    catalog.cases.Add(item); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                }
                File.WriteAllText(output + "/source-record.txt", "baseline_scene=" + BaselineScene + "\nbaseline_scene_sha256=" + Sha(BaselineScene) + "\nbaseline_input_sha256=" + Sha(BaselineInput) + "\nwater_mesh=" + AssetDatabase.GetAssetPath(surface) + "\nbed_mesh=" + AssetDatabase.GetAssetPath(bed) + "\nwater_source_sha256=" + Sha(AssetDatabase.GetAssetPath(profile.waterColor)) + "\nshore_source_sha256=" + Sha(AssetDatabase.GetAssetPath(profile.shoreSediment)) + "\nreferences=hidden identically in all stages\nstatus=candidate-not-approved\n");
                File.WriteAllText(output + "/profile-snapshot.json", JsonUtility.ToJson(profile, true));
                Debug.Log("Lake V3 quality study created without modifying baseline: " + output);
            }
            finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }

        private static bool CanChange() => !EditorApplication.isPlayingOrWillChangePlaymode && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        private static void EnsureParent(string path) => ProjectRealmWorkspaceLayout.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        private static Shader ValidShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Missing or invalid shader: " + name);
            return shader;
        }
        private static void ConfigureSource(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter; if (importer == null) throw new IOException("Missing new source: " + path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear; importer.mipmapEnabled = true; importer.anisoLevel = 4; importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false; importer.SaveAndReimport();
        }
        private static MeshRenderer Add(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        private static Mesh TileMesh()
        {
            var mesh = new Mesh { name = "Raw3x3TileBoard" };
            mesh.vertices = new[] { new Vector3(-33, 0, -33), new Vector3(-33, 0, 33), new Vector3(33, 0, -33), new Vector3(33, 0, 33) };
            mesh.uv = new[] { Vector2.zero, new Vector2(0, 3), new Vector2(3, 0), new Vector2(3, 3) };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static string Sha(string path)
        { using var algorithm = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }

        [MenuItem("Project Realm/Debug/Water/Export Lake V3 Quality Checks")]
        public static void Export()
        {
            if (!CanChange()) return;
            var view = UnityEngine.Object.FindAnyObjectByType<LakeLookdevView>();
            if (view == null) throw new InvalidOperationException("Open Lake V3 Quality Study first.");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, Evidence, DateTime.Now.ToString("yyyyMMdd-HHmmss"))); Directory.CreateDirectory(folder);
            int stage = view.stage; bool tiles = view.tiles, bed = view.bedOnly; float phase = view.phase, zoom = view.zoom, pitch = view.pitch; Vector3 focus = view.focus;
            var sourceCamera = view.GetComponent<Camera>();
            var temporary = new GameObject("Temporary lake quality camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = temporary.AddComponent<Camera>(); camera.enabled = false;
            var conditions = new List<string> { "image,stage,pitch,orthographic_size,focus_x,focus_z,phase,tiles,bed_only,reference_details" };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Capture("0" + (i + 1) + "-stage" + (i + 1) + "-overview", i, 1, 0);
                    Capture("0" + (i + 1) + "-stage" + (i + 1) + "-near", i, .45f, 0);
                }
                Capture("05-candidate-far", 3, 1.3f, 0);
                Capture("06-candidate-phase8", 3, 1, 8);
                Capture("07-raw-3x3", 3, 1, 0, true);
                Capture("08-bed-only", 3, .65f, 0, false, true);
                File.WriteAllLines(Path.Combine(folder, "conditions.csv"), conditions);
                File.WriteAllText(Path.Combine(folder, "texture-metrics.json"), "{\n\"water\":" + JsonUtility.ToJson(MeasureSource(AssetDatabase.GetAssetPath(view.profile.waterColor)), true) + ",\n\"sediment\":" + JsonUtility.ToJson(MeasureSource(AssetDatabase.GetAssetPath(view.profile.shoreSediment)), true) + "\n}");
                File.Copy(ProfilePath, Path.Combine(folder, "profile.asset.txt"));
                File.Copy(AssetDatabase.GetAssetPath(view.waterStages[3].shader), Path.Combine(folder, "water.shader.txt"));
                File.Copy(AssetDatabase.GetAssetPath(view.bankStages[3].shader), Path.Combine(folder, "shore.shader.txt"));
                File.WriteAllText(Path.Combine(folder, "reproducibility.txt"), "unity=" + Application.unityVersion + "\ngraphics=" + SystemInfo.graphicsDeviceType + "\ncolor_space=" + QualitySettings.activeColorSpace + "\nresolution=1600x1040\nscene=" + ScenePath + "\nprofile=" + ProfilePath + "\nprofile_sha256=" + Sha(ProfilePath) + "\nbaseline_scene_sha256=" + Sha(BaselineScene) + "\nbaseline_input_sha256=" + Sha(BaselineInput) + "\nwater_shader_sha256=" + Sha(AssetDatabase.GetAssetPath(view.waterStages[3].shader)) + "\nshore_shader_sha256=" + Sha(AssetDatabase.GetAssetPath(view.bankStages[3].shader)) + "\nraw_tiles=no seam correction; native source preserved\nstatus=technical evidence only; visual gate remains manual\n");
                Debug.Log("Lake V3 quality checks exported: " + folder);
            }
            finally
            {
                view.stage = stage; view.tiles = tiles; view.bedOnly = bed; view.phase = phase; view.zoom = zoom; view.pitch = pitch; view.focus = focus; view.Apply();
                UnityEngine.Object.DestroyImmediate(temporary);
            }
            void Capture(string name, int step, float size, float animation, bool raw = false, bool bedOnly = false)
            {
                view.stage = step; view.tiles = raw; view.bedOnly = bedOnly; view.phase = animation; view.Frame(size); view.Apply();
                camera.CopyFrom(sourceCamera); camera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation); camera.enabled = false;
                Render(camera, Path.Combine(folder, name + ".png"));
                string F(float value) => value.ToString(CultureInfo.InvariantCulture);
                conditions.Add(string.Join(",", name, step + 1, F(raw ? 90 : view.pitch), F(camera.orthographicSize), F(view.focus.x), F(view.focus.z), F(animation), raw, bedOnly, "false"));
            }
        }
        private static void Render(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1600, 1040, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                camera.aspect = 1600f / 1040; var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render request unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target; pixels = new Texture2D(1600, 1040, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1600, 1040), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
        }

        [Serializable] public sealed class SourceMetrics
        {
            public string path, sha256;
            public int width, height;
            public float horizontalEdgeMean, verticalEdgeMean, interiorMean, edgeRatio;
            public string interpretation = "0..1 sRGB channel error; compares opposite borders to sampled interior neighbors. A warning metric, NOT proof of seamlessness or art approval.";
        }
        public static SourceMetrics MeasureSource(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) throw new IOException("Invalid image: " + path);
                var pixels = texture.GetPixels32(); int width = texture.width, height = texture.height; double horizontal = 0, vertical = 0, interior = 0; int count = 0;
                float Difference(Color32 a, Color32 b) => (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) / (3f * 255);
                for (int y = 0; y < height; y++) horizontal += Difference(pixels[y * width], pixels[y * width + width - 1]);
                for (int x = 0; x < width; x++) vertical += Difference(pixels[x], pixels[(height - 1) * width + x]);
                for (int y = 0; y < height - 1; y += 4) for (int x = 0; x < width - 1; x += 4)
                { int p = y * width + x; interior += Difference(pixels[p], pixels[p + 1]) + Difference(pixels[p], pixels[p + width]); count += 2; }
                float inside = count > 0 ? (float)(interior / count) : 0;
                return new SourceMetrics { path = path, sha256 = Sha(path), width = width, height = height, horizontalEdgeMean = (float)(horizontal / height), verticalEdgeMean = (float)(vertical / width), interiorMean = inside, edgeRatio = Mathf.Max((float)(horizontal / height), (float)(vertical / width)) / Mathf.Max(inside, .000001f) };
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
