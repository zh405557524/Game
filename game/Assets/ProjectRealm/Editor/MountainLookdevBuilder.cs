using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ProjectRealm.UnityPresentation.Map.Mountain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ProjectRealm.EditorTools
{
    /// <summary>Isolated mountain study. Opening does not rebuild; explicit rebuilds keep prior scene + generated revisions.</summary>
    public static class MountainLookdevBuilder
    {
        public const string CaseId = "lookdev/mountain-v1";
        public const string ScenePath = ProjectRealmWorkspaceLayout.DebugScenes + "/01_Terrain/03_Mountain/MountainLookdevV1/MountainLookdevV1.unity";
        public const string InputFolder = ProjectRealmWorkspaceLayout.TestData + "/Map/01_Terrain/03_Mountain/MountainLookdevV1";
        public const string ProfilePath = InputFolder + "/MountainLookdevV1.asset";
        public const string GeneratedFolder = ProjectRealmWorkspaceLayout.Generated + "/Map/01_Terrain/03_Mountain/MountainLookdevV1";
        public const string Sources = "Assets/ProjectRealm/Presentation/Map/Materials/Textures/Terrain/03_Mountain/OriginalReferenceV1";
        public const string EvidenceRelative = "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Mountain/MountainLookdevV1";
        public static readonly string[] SourceNames = { "mountain-wash-v1", "mountain-strokes-v3-seamless-local", "paper-grain-v1", "pine-clump-v4-alpha-local" };

        [MenuItem("Project Realm/Debug/Mountain/Open Mountain Lookdev V1")]
        public static void Open()
        {
            if (!CanChange()) return;
            if (!File.Exists(ScenePath)) { PrepareBuildHostScene(); Build(false); }
            EditorSceneManager.OpenScene(ScenePath);
            var type = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (type != null) EditorWindow.GetWindow(type).Show();
        }

        [MenuItem("Project Realm/Debug/Mountain/Rebuild Mountain V1 (Keep Previous Revision)")]
        public static void Rebuild()
        {
            if (!CanChange()) return;
            PrepareBuildHostScene();
            try { Build(true); }
            finally { if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath); }
        }

        private static bool CanChange()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first. Do not save play-mode navigation into the profile.");
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        /// <summary>Automation entry, never changes the existing learning scene or project build settings.</summary>
        public static void BatchBuildAndCapture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only; use the menu interactively.");
            PrepareBuildHostScene();
            Build(false);
            EditorSceneManager.OpenScene(ScenePath);
            ExportCurrent();
        }

        public static void BatchRebuildAndCapture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only; use the menu interactively.");
            PrepareBuildHostScene();
            Build(true);
            EditorSceneManager.OpenScene(ScenePath);
            ExportCurrent();
        }

        private static void PrepareBuildHostScene()
        {
            // Unity batch startup may contain an unsaved untitled default scene, which prevents additive creation.
            // Open an existing scene read-only in this separate batch process; never save or alter the old scene.
            // The target must also not be open: Unity refuses SaveScene over a different open scene's path.
            string host = MapDebugWorkbench.LoadCatalog().cases.Single(x => x.id == "terrain/03_Mountain").scenePath;
            if (!File.Exists(host)) throw new FileNotFoundException("A saved host scene is required for isolated batch generation.", host);
            EditorSceneManager.OpenScene(host);
        }

        private static void Build(bool rebuild)
        {
            if (File.Exists(ScenePath) && !rebuild) return;
            ValidateShaders();
            foreach (string name in SourceNames) ConfigureSource(name);
            ProjectRealmWorkspaceLayout.EnsureFolder(InputFolder);
            var profile = AssetDatabase.LoadAssetAtPath<MountainLookdevProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<MountainLookdevProfile>();
                profile.wash = Texture(SourceNames[0]); profile.strokes = Texture(SourceNames[1]);
                profile.paper = Texture(SourceNames[2]);
                var pineMetric = MeasureSource(Sources + "/" + SourceNames[3] + ".png");
                bool pineValid = pineMetric.transparentPixels > 0 && pineMetric.opaquePixels > 0;
                profile.pine = pineValid ? Texture(SourceNames[3]) : null;
                profile.showTrees = pineValid; profile.foliageStatus = pineValid ? "WaitingReview" : "BlockedInvalidAlpha";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            if (!profile.Validate(out string error)) throw new InvalidOperationException(error);
            string reference = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", profile.referencePath));
            if (!File.Exists(reference) || Sha(reference) != profile.referenceSha256) throw new InvalidOperationException("Approved reference changed or is missing. No geometry was rebuilt.");
            var inputMetrics = MeasureProfileSources(profile);
            if (profile.pine != null)
            {
                var assignedPine = MeasureSource(AssetDatabase.GetAssetPath(profile.pine));
                if (assignedPine.transparentPixels == 0 || assignedPine.opaquePixels == 0)
                    throw new InvalidOperationException("Pine source needs real transparent and visible pixels; a baked checkerboard is not alpha.");
            }
            if (profile.pine == null) Debug.LogWarning("Mountain foliage is incomplete: the selected source has no usable alpha. It is NOT assigned to a runtime material; the tree layer stays empty.");

            string revision = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string output = GeneratedFolder + "/" + revision;
            ProjectRealmWorkspaceLayout.EnsureFolder(output);
            ProjectRealmWorkspaceLayout.EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            if (File.Exists(ScenePath))
            {
                string archive = "Assets/Scenes/Archive/Map/MountainLookdevV1/" + revision;
                ProjectRealmWorkspaceLayout.EnsureFolder(archive);
                if (!AssetDatabase.CopyAsset(ScenePath, archive + "/MountainLookdevV1.unity")) throw new IOException("Could not preserve previous scene. Rebuild stopped.");
                BindArchivedProfile(archive + "/MountainLookdevV1.unity");
            }
            // Each built scene references its own immutable revision inputs, not the next editable authoring values.
            var snapshot = Object.Instantiate(profile); snapshot.name = "MountainLookdevV1 / " + revision;
            string snapshotPath = output + "/ProfileSnapshot.asset";
            AssetDatabase.CreateAsset(snapshot, snapshotPath);

            var mesh = MountainLookdevGeometry.Build(profile);
            AssetDatabase.CreateAsset(mesh, output + "/Mountain.asset");
            var quad = CreateQuad(); AssetDatabase.CreateAsset(quad, output + "/Billboard.asset");
            var surface = SurfaceMaterial(profile); AssetDatabase.CreateAsset(surface, output + "/MountainInk.mat");
            Material foliage = null;
            if (profile.pine != null)
            {
                foliage = new Material(Shader.Find("ProjectRealm/Map/MountainPine")) { name = "Independent ink pine / real alpha" };
                foliage.SetTexture("_BaseMap", profile.pine); SetShaderColor(foliage, "_PaperColor", profile.paperColor);
                foliage.SetFloat("_DepthWash", profile.depthWash); foliage.SetFloat("_DepthExtent", profile.size.y * .48f);
                AssetDatabase.CreateAsset(foliage, output + "/Pine.mat");
            }
            var haze = new Material(Shader.Find("ProjectRealm/Map/MountainMist")) { name = "Toggleable thin valley wash" };
            SetShaderColor(haze, "_PaperColor", profile.paperColor); haze.SetFloat("_Opacity", profile.mistOpacity);
            AssetDatabase.CreateAsset(haze, output + "/Mist.mat");

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            int treeCount = 0;
            try
            {
                RenderSettings.skybox = null; RenderSettings.fog = false;
                var terrain = RenderObject("01 山体 / peaks-ridges-valleys", mesh, surface, null);
                var trees = new GameObject("02 松树丛 / independent alpha cards");
                var fog = new GameObject("03 谷地薄雾 / independently toggleable");
                treeCount = profile.pine == null ? 0 : AddTrees(profile, trees.transform, quad, foliage);
                AddMist(profile, fog.transform, quad, haze);
                var go = new GameObject("Mountain Lookdev Camera", typeof(Camera), typeof(AudioListener), typeof(MountainLookdevView));
                go.tag = "MainCamera";
                var camera = go.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = profile.paperColor;
                camera.orthographic = true; camera.nearClipPlane = 0.3f; camera.farClipPlane = 700;
                camera.allowHDR = false; camera.allowMSAA = true;
                var view = go.GetComponent<MountainLookdevView>();
                view.profile = snapshot; view.terrain = terrain; view.treesRoot = trees; view.mistRoot = fog;
                view.trees = profile.showTrees; view.mist = profile.showMist;
                view.ResetView(); view.Apply();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Mountain scene could not be saved.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }

            var record = new BuildRecord
            {
                builtAt = DateTime.Now.ToString("O"), revision = revision, referencePath = reference,
                referenceSha256 = Sha(reference), profile = ProfilePath, profileSnapshot = snapshotPath, profileJson = JsonUtility.ToJson(profile, true),
                generatedFolder = output, vertexCount = mesh.vertexCount, triangleCount = (int)mesh.GetIndexCount(0) / 3,
                treeClumps = treeCount, sources = inputMetrics, unity = Application.unityVersion,
                os = SystemInfo.operatingSystem, graphicsDevice = SystemInfo.graphicsDeviceName,
                status = profile.visualStatus, note = "Technical build only. Foliage: " + profile.foliageStatus + ". No art approval, county-scale performance claim, or Windows runtime acceptance."
            };
            File.WriteAllText(output + "/build-record.json", JsonUtility.ToJson(record, true));
            var catalog = MapDebugWorkbench.LoadCatalog();
            var item = catalog.cases.FirstOrDefault(x => x.id == CaseId);
            if (item == null)
            {
                item = new MapDebugCase { id = CaseId, layer = 0, displayName = "山地原图复现 / MountainLookdevV1", scenePath = ScenePath, testDataPath = InputFolder, generatedPath = GeneratedFolder };
                catalog.cases.Add(item);
            }
            item.state = DebugReviewState.InProgress;
            item.findings = "原始高山图已获视觉目标确认；本地V3接缝修复皴擦与V4透明松树已独立接入，仍待美术确认。高度场和薄雾仍为NeedsRevision；不是完整插画贴到平面。";
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(profile); AssetDatabase.Refresh();
            Debug.Log($"MountainLookdevV1 built: {mesh.vertexCount} vertices, {record.triangleCount} triangles, {treeCount} independent tree clumps. Revision: {output}. Visual review pending.");
        }

        private static void BindArchivedProfile(string archivedScenePath)
        {
            var previous = SceneManager.GetActiveScene();
            var archived = EditorSceneManager.OpenScene(archivedScenePath, OpenSceneMode.Additive);
            try
            {
                var view = archived.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MountainLookdevView>(true)).Single();
                // Early study iterations referenced the authoring asset. Freeze those newly archived inputs too.
                if (AssetDatabase.GetAssetPath(view.profile) != ProfilePath) return;
                var mesh = view.terrain.GetComponent<MeshFilter>().sharedMesh;
                string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(mesh)).Replace('\\', '/') + "/ProfileSnapshot.asset";
                var originalSnapshot = AssetDatabase.LoadAssetAtPath<MountainLookdevProfile>(path);
                if (originalSnapshot == null) throw new IOException("Previous profile snapshot is missing; rebuild stopped to preserve its inputs.");
                view.profile = originalSnapshot;
                if (!EditorSceneManager.SaveScene(archived)) throw new IOException("Could not freeze the archived profile; rebuild stopped.");
            }
            finally
            {
                EditorSceneManager.CloseScene(archived, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        public static void ValidateShaders()
        {
            foreach (string name in new[] { "ProjectRealm/Map/MountainInkSurface", "ProjectRealm/Map/MountainPine", "ProjectRealm/Map/MountainMist", "Hidden/ProjectRealm/RawSourcePreview" })
            {
                var shader = Shader.Find(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Missing/invalid mountain shader: " + name);
            }
        }

        private static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Sources + "/" + name + ".png");
        private static void ConfigureSource(string name)
        {
            string path = Sources + "/" + name + ".png";
            if (!File.Exists(path)) throw new FileNotFoundException("Required native source has not arrived.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer missing: " + path);
            bool pine = name.StartsWith("pine-clump", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            // Stroke and paper masks are data; color wash and foliage use sRGB decoding.
            importer.sRGBTexture = name == "mountain-wash-v1" || pine;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = pine;
            importer.wrapMode = pine ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear; importer.mipmapEnabled = true;
            importer.anisoLevel = pine ? 1 : 4; importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false; importer.SaveAndReimport();
        }

        private static Material SurfaceMaterial(MountainLookdevProfile p)
        {
            var m = new Material(Shader.Find("ProjectRealm/Map/MountainInkSurface")) { name = "Layered mountain ink / no PBR specular" };
            m.SetTexture("_WashMap", p.wash); m.SetTexture("_StrokeMap", p.strokes); m.SetTexture("_PaperMap", p.paper);
            SetShaderColor(m, "_PaperColor", p.paperColor); SetShaderColor(m, "_RockColor", p.rockColor);
            SetShaderColor(m, "_InkColor", p.inkColor); SetShaderColor(m, "_MossColor", p.mossColor);
            m.SetFloat("_WashTile", p.washTileSize); m.SetFloat("_StrokeTile", p.strokeTileSize);
            m.SetFloat("_WashStrength", p.washStrength); m.SetFloat("_InkStrength", p.inkStrength);
            m.SetFloat("_PaperStrength", p.paperStrength); m.SetFloat("_DepthWash", p.depthWash);
            m.SetVector("_MapSize", new Vector4(p.size.x, p.size.y, 0, 0)); m.SetFloat("_Stage", 2);
            return m;
        }

        // Color shader properties are converted by Unity in a linear-color project. Do not pre-linearize them again.
        private static void SetShaderColor(Material m, string property, Color value) => m.SetColor(property, value);

        private static MeshRenderer RenderObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Bottom-center anchored independent card" };
            mesh.vertices = new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0), new Vector3(-.5f, 1, 0), new Vector3(.5f, 1, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private static int AddTrees(MountainLookdevProfile p, Transform root, Mesh quad, Material material)
        {
            var random = new System.Random(p.seed + 72);
            var occupied = new List<Vector2>();
            for (int attempt = 0; attempt < p.treeClumps * 110 && occupied.Count < p.treeClumps; attempt++)
            {
                float x = (float)(random.NextDouble() - .5) * p.size.x * .83f;
                float z = (float)(random.NextDouble() - .5) * p.size.y * .86f;
                float h = MountainLookdevGeometry.SampleHeight(p, x, z);
                var normal = MountainLookdevGeometry.SampleNormal(p, x, z);
                if (h < 6 || normal.y < .66f || occupied.Any(v => Vector2.SqrMagnitude(v - new Vector2(x, z)) < 10)) continue;
                var renderer = RenderObject("松丛 " + occupied.Count.ToString("D3"), quad, material, root);
                float height = Mathf.Lerp(p.treeHeight.x, p.treeHeight.y, (float)random.NextDouble());
                float widthVariation = Mathf.Lerp(.82f, 1.15f, (float)random.NextDouble());
                float mirror = random.NextDouble() < .5 ? -1 : 1;
                renderer.transform.localPosition = new Vector3(x, h + .05f, z);
                renderer.transform.localScale = new Vector3(height * (p.pine.width / (float)p.pine.height) * widthVariation * mirror, height, 1);
                occupied.Add(new Vector2(x, z));
            }
            return occupied.Count;
        }

        private static void AddMist(MountainLookdevProfile p, Transform root, Mesh quad, Material material)
        {
            var positions = new[] { new Vector3(-28, 6, 46), new Vector3(22, 5, -10), new Vector3(-27, 3, -39), new Vector3(47, 4, -70), new Vector3(-62, 5, 65), new Vector3(62, 5, 48) };
            for (int i = 0; i < positions.Length; i++)
            {
                var renderer = RenderObject("薄雾 " + i, quad, material, root);
                renderer.transform.localPosition = positions[i]; renderer.transform.localScale = new Vector3(47, 12, 1);
            }
        }

        [MenuItem("Project Realm/Debug/Mountain/Export Mountain Diagnostics")]
        public static void ExportCurrent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before deterministic captures.");
            var view = Object.FindFirstObjectByType<MountainLookdevView>();
            if (view == null) throw new InvalidOperationException("Open MountainLookdevV1 first.");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, EvidenceRelative, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(folder);
            string before = JsonUtility.ToJson(view); var profile = view.profile;
            var oldProperties = new MaterialPropertyBlock(); view.terrain.GetPropertyBlock(oldProperties);
            Vector3 oldPosition = view.transform.position; Quaternion oldRotation = view.transform.rotation;
            var oldCamera = view.GetComponent<Camera>(); float oldSize = oldCamera.orthographicSize;
            bool oldTrees = view.treesRoot.activeSelf, oldMist = view.mistRoot.activeSelf;
            var treeRotations = view.treesRoot.transform.Cast<Transform>().Select(t => t.rotation).ToArray();
            var mistRotations = view.mistRoot.transform.Cast<Transform>().Select(t => t.rotation).ToArray();
            var conditions = new List<string> { "capture,stage,trees,mist,paper,pitch,zoom,focusX,focusY,focusZ,width,height,strokeSource,inkStrength" };
            var go = new GameObject("Temporary mountain capture") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(oldCamera); camera.enabled = false;
            var mesh = view.terrain.GetComponent<MeshFilter>().sharedMesh;
            try
            {
                view.showHud = false; view.ResetView(); view.trees = view.mist = false; view.paper = false; view.surfaceStage = 0;
                Capture("01-clay-same-geometry");
                view.surfaceStage = 1; Capture("02-wash-no-dressing");
                view.surfaceStage = 2; view.paper = true; Capture("03-ink-no-dressing");
                var oldBrush = Texture("mountain-strokes-v1");
                if (oldBrush != null && oldBrush != profile.strokes)
                {
                    var sourceMaterial = view.terrain.sharedMaterial;
                    var comparison = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        comparison.SetTexture("_StrokeMap", oldBrush); view.terrain.sharedMaterial = comparison;
                        Capture("03b-brush-v1-same-geometry-comparison");
                    }
                    finally { view.terrain.sharedMaterial = sourceMaterial; Object.DestroyImmediate(comparison); }
                }
                if (profile.pine != null) { view.trees = true; Capture("04-trees-no-mist"); }
                view.mist = true; Capture("05-surface-and-mist-candidate");
                view.zoom = profile.minZoom * 1.35f; view.focus = new Vector3(0, 28, -2); Capture("06-near");
                view.ResetView(); view.zoom = profile.maxZoom; Capture("07-far");
                view.ResetView(); view.pitch = 35; Capture("08-pitch-35");
                view.pitch = 75; Capture("09-pitch-75");
                view.ResetView(); view.trees = view.mist = false; view.paper = false; Capture("10-no-paper-no-dressing");
                ExportTiles(camera, view, folder);
                File.WriteAllLines(Path.Combine(folder, "conditions.csv"), conditions);
                File.WriteAllText(Path.Combine(folder, "capture-record.json"), JsonUtility.ToJson(new CaptureRecord
                {
                    capturedAt = DateTime.Now.ToString("O"), unity = Application.unityVersion,
                    os = SystemInfo.operatingSystem, graphicsDevice = SystemInfo.graphicsDeviceName,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(), systemMemoryMb = SystemInfo.systemMemorySize,
                    graphicsMemoryReportedMb = SystemInfo.graphicsMemorySize, meshPath = AssetDatabase.GetAssetPath(mesh),
                    meshSha256 = Sha(AssetDatabase.GetAssetPath(mesh)), vertexCount = mesh.vertexCount,
                    triangleCount = (int)mesh.GetIndexCount(0) / 3, treeClumps = view.treesRoot.transform.childCount,
                    profileJson = JsonUtility.ToJson(profile, true), sources = MeasureProfileSources(profile),
                    visualStatus = profile.visualStatus, note = "Mac editor render captures; not art approval or county performance. On Apple silicon, reported graphics memory is not separate dedicated VRAM."
                }, true));
                var catalog = MapDebugWorkbench.LoadCatalog(); var item = catalog.cases.First(x => x.id == CaseId);
                item.evidencePath = folder;
                item.state = profile.visualStatus == "NeedsRevision" ? DebugReviewState.InProgress : DebugReviewState.WaitingReview;
                item.findings = "已导出同一网格白模/底纹/皴擦、树丛与薄雾开关、近中远、35/75度、独立纹理3×3及松树浅深底。视觉状态：" + profile.visualStatus + "；本地修复素材仍待美术确认，当前山形尚未达到原图。截图不等于视觉通过，窗口输入仍需另行实测。";
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                Debug.Log("MOUNTAIN_EVIDENCE=" + folder);
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(before, view);
                view.transform.SetPositionAndRotation(oldPosition, oldRotation); oldCamera.orthographicSize = oldSize;
                view.terrain.SetPropertyBlock(oldProperties); view.treesRoot.SetActive(oldTrees); view.mistRoot.SetActive(oldMist);
                int i = 0; foreach (Transform t in view.treesRoot.transform) t.rotation = treeRotations[i++];
                i = 0; foreach (Transform t in view.mistRoot.transform) t.rotation = mistRotations[i++];
                Object.DestroyImmediate(go);
            }

            void Capture(string name)
            {
                view.Apply(); camera.CopyFrom(view.GetComponent<Camera>()); camera.enabled = false;
                camera.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                Render(camera, 1600, 1200, Path.Combine(folder, name + ".png"));
                var material = view.terrain.sharedMaterial;
                string strokePath = AssetDatabase.GetAssetPath(material.GetTexture("_StrokeMap"));
                conditions.Add(FormattableString.Invariant($"{name},{view.surfaceStage},{view.trees},{view.mist},{view.paper},{view.pitch},{view.zoom},{view.focus.x},{view.focus.y},{view.focus.z},1600,1200,{strokePath},{material.GetFloat("_InkStrength")}"));
            }
        }

        private static void ExportTiles(Camera camera, MountainLookdevView view, string folder)
        {
            bool terrain = view.terrain.enabled, trees = view.treesRoot.activeSelf, mist = view.mistRoot.activeSelf;
            var go = new GameObject("Temporary raw tile card") { hideFlags = HideFlags.HideAndDontSave };
            var filter = go.AddComponent<MeshFilter>(); var renderer = go.AddComponent<MeshRenderer>();
            var mesh = CreateQuad(); var material = new Material(Shader.Find("Hidden/ProjectRealm/RawSourcePreview")) { hideFlags = HideFlags.HideAndDontSave };
            filter.sharedMesh = mesh; renderer.sharedMaterial = material;
            go.transform.position = new Vector3(0, -1, 0); go.transform.localScale = new Vector3(2, 2, 1);
            var cameraPosition = camera.transform.position; var cameraRotation = camera.transform.rotation;
            try
            {
                view.terrain.enabled = false; view.treesRoot.SetActive(false); view.mistRoot.SetActive(false);
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity); camera.orthographicSize = 1;
                var surfaceSources = new[] { view.profile.wash, view.profile.strokes, view.profile.paper };
                for (int i = 0; i < surfaceSources.Length; i++)
                {
                    mesh.uv = new[] { Vector2.zero, Vector2.right * 3, Vector2.up * 3, Vector2.one * 3 };
                    material.SetTexture("_BaseMap", surfaceSources[i]);
                    material.SetFloat("_DataTexture", i == 0 ? 0 : 1);
                    string sourceName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(surfaceSources[i]));
                    Render(camera, 1200, 1200, Path.Combine(folder, "11-3x3-" + sourceName + ".png"));
                }
                // Genuine source alpha inspected over two solid colors, not a baked checkerboard or keyed background.
                if (view.profile.pine != null)
                {
                    var pineMaterial = new Material(Shader.Find("ProjectRealm/Map/MountainPine")) { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        pineMaterial.SetTexture("_BaseMap", view.profile.pine); pineMaterial.SetFloat("_DepthWash", 0);
                        renderer.sharedMaterial = pineMaterial; mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                        camera.backgroundColor = new Color(.91f, .90f, .855f); Render(camera, 1000, 1000, Path.Combine(folder, "12-pine-on-paper.png"));
                        camera.backgroundColor = new Color(.2f, .28f, .31f); Render(camera, 1000, 1000, Path.Combine(folder, "13-pine-on-dark.png"));
                    }
                    finally { Object.DestroyImmediate(pineMaterial); }
                }
            }
            finally
            {
                view.terrain.enabled = terrain; view.treesRoot.SetActive(trees); view.mistRoot.SetActive(mist);
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            }
        }

        public static void Render(Camera camera, int width, int height, string path)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D capture = null;
            try
            {
                camera.aspect = width / (float)height;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render request unavailable; do not report a screenshot as captured.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
                capture = new Texture2D(width, height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0); capture.Apply();
                File.WriteAllBytes(path, capture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (capture != null) Object.DestroyImmediate(capture); RenderTexture.ReleaseTemporary(target); }
        }

        public static string Sha(string path)
        { using var hash = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }

        private static SourceMetric[] MeasureProfileSources(MountainLookdevProfile profile)
        {
            // Evidence follows the selected revision's actual texture references, not the latest default filenames.
            var paths = new[] { AssetDatabase.GetAssetPath(profile.wash), AssetDatabase.GetAssetPath(profile.strokes),
                AssetDatabase.GetAssetPath(profile.paper), profile.pine == null ? Sources + "/pine-clump-v1.png" : AssetDatabase.GetAssetPath(profile.pine) };
            return paths.Select(MeasureSource).ToArray();
        }

        public static SourceMetric MeasureSource(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)) throw new IOException("Cannot decode PNG: " + path);
                var pixels = texture.GetPixels32(); int w = texture.width, h = texture.height, transparent = 0, opaque = 0, partial = 0;
                double edge = 0, adjacent = 0;
                for (int y = 0; y < h; y++) edge += Difference(pixels[y * w], pixels[y * w + w - 1]);
                for (int x = 0; x < w; x++) edge += Difference(pixels[x], pixels[(h - 1) * w + x]);
                for (int y = 0; y < h; y += 11)
                    for (int x = 1; x < w; x++) adjacent += Difference(pixels[y * w + x], pixels[y * w + x - 1]);
                foreach (var pixel in pixels) { if (pixel.a == 0) transparent++; else if (pixel.a == 255) opaque++; else partial++; }
                int samples = ((h - 1) / 11 + 1) * (w - 1);
                return new SourceMetric { path = path, sha256 = Sha(path), bytes = new FileInfo(path).Length, width = w, height = h,
                    transparentPixels = transparent, opaquePixels = opaque, partialAlphaPixels = partial,
                    edgeMeanDifference = edge / (w + h), adjacentMeanDifference = adjacent / Math.Max(1, samples),
                    note = "Opposite-edge difference is a heuristic, not proof of seamless tiling. Native dimensions; no image resize or repair." };
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static double Difference(Color32 a, Color32 b) => (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / (3.0 * 255);
        [Serializable] public sealed class SourceMetric { public string path, sha256, note; public long bytes; public int width, height, transparentPixels, opaquePixels, partialAlphaPixels; public double edgeMeanDifference, adjacentMeanDifference; }
        [Serializable] private sealed class BuildRecord
        { public string builtAt, revision, referencePath, referenceSha256, profile, profileSnapshot, profileJson, generatedFolder, unity, os, graphicsDevice, status, note; public int vertexCount, triangleCount, treeClumps; public SourceMetric[] sources; }
        [Serializable] private sealed class CaptureRecord
        { public string capturedAt, unity, os, graphicsDevice, graphicsApi, meshPath, meshSha256, profileJson, visualStatus, note; public int systemMemoryMb, graphicsMemoryReportedMb, vertexCount, triangleCount, treeClumps; public SourceMetric[] sources; }
    }
}
