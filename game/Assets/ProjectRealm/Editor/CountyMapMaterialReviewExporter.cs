using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ProjectRealm.EditorTools
{
    public static class CountyMapMaterialReviewExporter
    {
        private static readonly string[] Names = { "Plain", "Hills", "Mountain", "Plateau", "Basin" };

        [MenuItem("Project Realm/Map Materials/Rebuild and Export Review")]
        public static void RebuildAndExportReview()
        {
            CountyMapMaterialReviewBuilder.CreateReviewScene();
            ExportCurrentReviewRenders();
        }

        public static void ExportSavedReviewRenders()
        {
            EditorSceneManager.OpenScene(CountyMapMaterialReviewBuilder.ScenePath, OpenSceneMode.Single);
            ExportCurrentReviewRenders();
        }

        [MenuItem("Project Realm/Map Materials/Export Current Review Renders")]
        public static void ExportCurrentReviewRenders()
        {
            if (EditorSceneManager.GetActiveScene().path != CountyMapMaterialReviewBuilder.ScenePath)
                throw new InvalidOperationException("Open the five-material review scene before exporting.");

            var source = GameObject.Find("Material Review Camera").GetComponent<Camera>();
            var output = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,
                "../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/03_材质验收/renders", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(output);
            var metrics = new StringBuilder("image,mean_luma,coarse_contrast,horizontal_correlation,vertical_correlation,diagonal_correlation,max_abs_correlation\n");
            var cameraObject = new GameObject("Temporary Material QA Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var hiddenLabels = new List<Renderer>();

            try
            {
                Render(camera, 2560, 1440, Path.Combine(output, "overview.png"));
                // Closeups and their measurements must contain only the material, not review labels.
                foreach (var text in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
                {
                    var labelRenderer = text.GetComponent<Renderer>();
                    if (labelRenderer != null && labelRenderer.enabled)
                    {
                        hiddenLabels.Add(labelRenderer);
                        labelRenderer.enabled = false;
                    }
                }
                foreach (var name in Names)
                {
                    var panel = GameObject.Find($"{name} 3x3 Tile Review");
                    camera.transform.position = new Vector3(panel.transform.position.x, panel.transform.position.y, -30f);
                    camera.orthographicSize = 4f;
                    Record(metrics, $"{name.ToLowerInvariant()}-material-3x3", Render(camera, 1024, 1024,
                        Path.Combine(output, $"{name.ToLowerInvariant()}-material-3x3.png")));

                    var renderer = panel.GetComponent<MeshRenderer>();
                    var originalMaterial = renderer.sharedMaterial;
                    var directRepeat = new Material(originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        renderer.sharedMaterial = directRepeat;
                        directRepeat.SetFloat("_DetailScale", 6f);
                        Record(metrics, $"{name.ToLowerInvariant()}-material-6x6", Render(camera, 1024, 1024,
                            Path.Combine(output, $"{name.ToLowerInvariant()}-material-6x6.png"), 6));

                        directRepeat.SetFloat("_DetailScale", 3f);
                        directRepeat.SetFloat("_AntiTileStrength", 0f);
                        Record(metrics, $"{name.ToLowerInvariant()}-direct-repeat", Render(camera, 1024, 1024,
                            Path.Combine(output, $"{name.ToLowerInvariant()}-direct-repeat.png")));

                        if (originalMaterial.shader.name.EndsWith("InkTerrainStochastic", StringComparison.Ordinal))
                        {
                            var previous = AssetDatabase.LoadAssetAtPath<Material>(
                                $"Assets/ProjectRealm/Presentation/Map/Materials/Generated/{name.ToLowerInvariant()}-ink-terrain.mat");
                            if (previous != null)
                            {
                                renderer.sharedMaterial = previous;
                                Record(metrics, $"{name.ToLowerInvariant()}-previous-material", Render(camera, 1024, 1024,
                                    Path.Combine(output, $"{name.ToLowerInvariant()}-previous-material.png")));
                            }
                        }
                    }
                    finally
                    {
                        renderer.sharedMaterial = originalMaterial;
                        UnityEngine.Object.DestroyImmediate(directRepeat);
                    }
                }
                File.WriteAllText(Path.Combine(output, "quality-metrics.csv"), metrics.ToString());
                Debug.Log($"Map material QA render export: {output}");
            }
            finally
            {
                foreach (var label in hiddenLabels)
                    if (label != null) label.enabled = true;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Record(StringBuilder csv, string name, MaterialRenderQualityAnalyzer.Result result)
        {
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6}",
                name, result.Mean, result.CoarseContrast, result.HorizontalCorrelation, result.VerticalCorrelation,
                result.DiagonalCorrelation, result.MaxAbsoluteCorrelation));
            Debug.Log($"Material periodicity [{name}]: maxAbsCorrelation={result.MaxAbsoluteCorrelation:F4}, coarseContrast={result.CoarseContrast:F4}");
        }

        private static MaterialRenderQualityAnalyzer.Result Render(Camera camera, int width, int height, string path, int tileCount = 3)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                camera.aspect = (float)width / height;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("The active render pipeline does not support QA render requests.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return MaterialRenderQualityAnalyzer.Measure(image, tileCount);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
