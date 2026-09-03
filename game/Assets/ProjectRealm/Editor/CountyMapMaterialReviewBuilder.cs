using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    public static class CountyMapMaterialReviewBuilder
    {
        public const string ScenePath = ProjectRealmWorkspaceLayout.MaterialsScene;

        private const string TextureRoot = "Assets/ProjectRealm/Presentation/Map/Materials/Textures";
        private const string MaterialRoot = "Assets/ProjectRealm/Presentation/Map/Materials/Generated";

        private static readonly MaterialSpec[] MaterialSpecs =
        {
            new MaterialSpec("Plain", $"{TextureRoot}/terrain-plain-v2.png", new Vector2(-9f, 5.6f),
                new Color(1.00f, 0.99f, 0.91f), 0.88f, 0.12f),
            new MaterialSpec("Hills", $"{TextureRoot}/terrain-hills-v4.png", new Vector2(0f, 5.6f),
                new Color(0.96f, 0.98f, 0.92f), 0.82f, 0.12f, true),
            new MaterialSpec("Mountain", $"{TextureRoot}/terrain-mountain-v4.png", new Vector2(9f, 5.6f),
                new Color(0.88f, 0.90f, 0.86f), 1.05f, 0.24f, true),
            new MaterialSpec("Plateau", $"{TextureRoot}/terrain-plateau-v1.png", new Vector2(-4.5f, -5.4f),
                new Color(0.98f, 0.93f, 0.82f), 1.03f, 0.22f),
            new MaterialSpec("Basin", $"{TextureRoot}/terrain-basin-v2.png", new Vector2(4.5f, -5.4f),
                new Color(0.93f, 0.98f, 0.89f), 0.88f, 0.14f, true)
        };

        [MenuItem("Project Realm/Map Materials/Create Five-Material Review Scene")]
        public static void CreateReviewScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding the review scene.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            ProjectRealmWorkspaceLayout.EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EnsureFolder("Assets/ProjectRealm/Presentation/Map/Materials", "Generated");
            var results = ImportAndValidateTextures();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "CountyMapMaterialReview";

            CreateCamera();
            CreateBackdrop();
            CreateTitle();

            for (var index = 0; index < MaterialSpecs.Length; index++)
            {
                var spec = MaterialSpecs[index];
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TexturePath);
                var material = LoadOrCreateMaterial(spec, texture);
                CreateReviewPanel(spec, material, results[index]);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Five-material review scene created: {ScenePath}");
        }

        [MenuItem("Project Realm/Map Materials/Validate Five Source Textures")]
        public static void ValidateSourceTextures()
        {
            ImportAndValidateTextures();
        }

        private static MaterialQualityResult[] ImportAndValidateTextures()
        {
            var results = new MaterialQualityResult[MaterialSpecs.Length];
            for (var index = 0; index < MaterialSpecs.Length; index++)
            {
                var spec = MaterialSpecs[index];
                ConfigureTextureImporter(spec.TexturePath);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TexturePath);
                results[index] = Analyze(texture);
                LogResult(spec, results[index]);
            }

            return results;
        }

        private static void ConfigureTextureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException($"Map material texture not found: {path}");
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static MaterialQualityResult Analyze(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var width = texture.width;
            var height = texture.height;
            var edgeDifference = 0d;
            var edgeSamples = 0;

            for (var y = 0; y < height; y += 2)
            {
                edgeDifference += Difference(pixels[y * width], pixels[y * width + width - 1]);
                edgeSamples++;
            }

            for (var x = 0; x < width; x += 2)
            {
                edgeDifference += Difference(pixels[x], pixels[(height - 1) * width + x]);
                edgeSamples++;
            }

            var mean = 0d;
            var squareMean = 0d;
            var luminanceSamples = 0;
            for (var index = 0; index < pixels.Length; index += 4)
            {
                var color = pixels[index];
                var luminance = (0.2126d * color.r + 0.7152d * color.g + 0.0722d * color.b) / 255d;
                mean += luminance;
                squareMean += luminance * luminance;
                luminanceSamples++;
            }

            mean /= luminanceSamples;
            squareMean /= luminanceSamples;
            var standardDeviation = Math.Sqrt(Math.Max(0d, squareMean - mean * mean));
            var seamMismatch = edgeDifference / Math.Max(1, edgeSamples);
            var passesSeam = seamMismatch <= 0.085d;
            var passesContrast = standardDeviation >= 0.025d && standardDeviation <= 0.22d;

            return new MaterialQualityResult(seamMismatch, mean, standardDeviation, passesSeam && passesContrast);
        }

        private static double Difference(Color32 first, Color32 second)
        {
            return (Math.Abs(first.r - second.r) +
                    Math.Abs(first.g - second.g) +
                    Math.Abs(first.b - second.b)) / (255d * 3d);
        }

        private static Material LoadOrCreateMaterial(MaterialSpec spec, Texture2D texture)
        {
            var suffix = spec.Stochastic ? "-v2" : string.Empty;
            var path = $"{MaterialRoot}/{spec.Name.ToLowerInvariant()}-ink-terrain{suffix}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(spec.Stochastic ? "ProjectRealm/Map/InkTerrainStochastic" : "ProjectRealm/Map/InkTerrainMaterial");
            if (shader == null)
            {
                throw new InvalidOperationException("Ink terrain shader did not compile or could not be found.");
            }

            if (material == null)
            {
                material = new Material(shader) { name = $"{spec.Name} Ink Terrain" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", spec.Tint);
            material.SetFloat("_DetailScale", 3f);
            material.SetFloat("_AntiTileStrength", spec.Stochastic ? 1f : 0.86f);
            material.SetFloat("_Contrast", spec.Contrast);
            material.SetFloat("_InkStrength", spec.InkStrength);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateReviewPanel(
            MaterialSpec spec,
            Material material,
            MaterialQualityResult result)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = $"{spec.Name} 3x3 Tile Review";
            panel.transform.position = new Vector3(spec.Position.x, spec.Position.y, 0f);
            panel.transform.localScale = new Vector3(8f, 8f, 1f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(panel.GetComponent<Collider>());

            var labelObject = new GameObject($"{spec.Name} Review Label");
            labelObject.transform.position = new Vector3(spec.Position.x, spec.Position.y - 4.8f, -0.1f);
            var label = labelObject.AddComponent<TextMesh>();
            label.text = $"{spec.Name}{(spec.Stochastic ? " / v2" : "")}\nsource edge {result.SeamMismatch:0.000}";
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 52;
            label.characterSize = 0.11f;
            label.color = new Color(0.76f, 0.73f, 0.64f);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Material Review Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -30f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.09f, 0.075f);
            camera.orthographic = true;
            camera.orthographicSize = 12.6f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
        }

        private static void CreateBackdrop()
        {
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Neutral Review Backdrop";
            backdrop.transform.position = new Vector3(0f, 0f, 1f);
            backdrop.transform.localScale = new Vector3(48f, 28f, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { color = new Color(0.13f, 0.12f, 0.10f) };
            backdrop.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
        }

        private static void CreateTitle()
        {
            var titleObject = new GameObject("Review Title");
            titleObject.transform.position = new Vector3(0f, 11.7f, -0.1f);
            var title = titleObject.AddComponent<TextMesh>();
            title.text = "FIVE INK MAP MATERIALS  /  3 x 3 SEAM REVIEW";
            title.anchor = TextAnchor.MiddleCenter;
            title.alignment = TextAlignment.Center;
            title.fontSize = 72;
            title.characterSize = 0.15f;
            title.color = new Color(0.84f, 0.79f, 0.68f);
        }

        private static void LogResult(MaterialSpec spec, MaterialQualityResult result)
        {
            Debug.Log(
                $"Map material QA [{spec.Name}]: seam={result.SeamMismatch:0.0000}, " +
                $"meanLuma={result.MeanLuminance:0.0000}, contrast={result.Contrast:0.0000}, " +
                $"automatic={(result.AutomaticPass ? "PASS" : "REVIEW")}");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private readonly struct MaterialSpec
        {
            public readonly string Name;
            public readonly string TexturePath;
            public readonly Vector2 Position;
            public readonly Color Tint;
            public readonly float Contrast;
            public readonly float InkStrength;
            public readonly bool Stochastic;

            public MaterialSpec(
                string name,
                string texturePath,
                Vector2 position,
                Color tint,
                float contrast,
                float inkStrength,
                bool stochastic = false)
            {
                Name = name;
                TexturePath = texturePath;
                Position = position;
                Tint = tint;
                Contrast = contrast;
                InkStrength = inkStrength;
                Stochastic = stochastic;
            }
        }

        private readonly struct MaterialQualityResult
        {
            public readonly double SeamMismatch;
            public readonly double MeanLuminance;
            public readonly double Contrast;
            public readonly bool AutomaticPass;

            public MaterialQualityResult(
                double seamMismatch,
                double meanLuminance,
                double contrast,
                bool automaticPass)
            {
                SeamMismatch = seamMismatch;
                MeanLuminance = meanLuminance;
                Contrast = contrast;
                AutomaticPass = automaticPass;
            }
        }
    }
}
