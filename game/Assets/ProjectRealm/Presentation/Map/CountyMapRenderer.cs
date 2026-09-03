using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectRealm.Presentation.Map
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CountyMapRenderer : MonoBehaviour
    {
        private const string GeneratedRootName = "__GeneratedCountyMap";

        [SerializeField] private CountyMapDefinition definition;
        [SerializeField] private bool previewInEditMode = true;

        [NonSerialized] private readonly List<UnityEngine.Object> generatedAssets = new List<UnityEngine.Object>();
        [NonSerialized] private bool rebuildRequested;

        public CountyMapDefinition Definition => definition;

        public void SetDefinition(CountyMapDefinition mapDefinition)
        {
            definition = mapDefinition;
            Rebuild();
        }

        [ContextMenu("Rebuild County Map")]
        public void Rebuild()
        {
            ClearGeneratedContent();

            if (definition == null || (!UnityEngine.Application.isPlaying && !previewInEditMode))
            {
                return;
            }

            var generatedRoot = new GameObject(GeneratedRootName);
            generatedRoot.transform.SetParent(transform, false);
            if (!UnityEngine.Application.isPlaying)
            {
                generatedRoot.hideFlags = HideFlags.DontSaveInEditor;
            }

            CreateGround(generatedRoot.transform);
            CreateMountains(generatedRoot.transform);
            CreateLines(generatedRoot.transform);
            CreateSettlements(generatedRoot.transform);
        }

        private void OnEnable()
        {
            rebuildRequested = true;
        }

        private void OnDisable()
        {
            ClearGeneratedContent();
        }

        private void OnValidate()
        {
            rebuildRequested = true;
        }

        private void Update()
        {
            if (rebuildRequested ||
                (!UnityEngine.Application.isPlaying &&
                 previewInEditMode &&
                 definition != null &&
                 transform.Find(GeneratedRootName) == null))
            {
                rebuildRequested = false;
                Rebuild();
            }
        }

        private void CreateGround(Transform parent)
        {
            CreatePolygon(
                "Paper Background",
                parent,
                new[]
                {
                    new Vector2(-definition.Size.x * 0.62f, -definition.Size.y * 0.64f),
                    new Vector2(definition.Size.x * 0.62f, -definition.Size.y * 0.64f),
                    new Vector2(definition.Size.x * 0.62f, definition.Size.y * 0.64f),
                    new Vector2(-definition.Size.x * 0.62f, definition.Size.y * 0.64f)
                },
                -0.04f,
                new Color(0.72f, 0.67f, 0.54f));

            var regions = definition.TerrainRegions;
            if (regions == null)
            {
                return;
            }

            for (var index = 0; index < regions.Length; index++)
            {
                var region = regions[index];
                if (region == null)
                {
                    continue;
                }

                CreatePolygon(
                    $"Terrain {region.DisplayName}",
                    parent,
                    region.Polygon,
                    index * 0.012f,
                    TerrainColor(region.Kind));
            }
        }

        private void CreateMountains(Transform parent)
        {
            var mountains = definition.Mountains;
            if (mountains == null)
            {
                return;
            }

            var mountainMaterial = CreateMaterial("Mountain Ink", new Color(0.34f, 0.39f, 0.33f), true);
            for (var index = 0; index < mountains.Length; index++)
            {
                var mountain = mountains[index];
                if (mountain == null)
                {
                    continue;
                }

                var mountainObject = new GameObject($"Mountain {index + 1:00}");
                mountainObject.transform.SetParent(parent, false);
                mountainObject.transform.localPosition = new Vector3(mountain.Position.x, 0.07f, mountain.Position.y);
                mountainObject.transform.localRotation = Quaternion.Euler(0f, index * 23f, 0f);

                var mesh = CreateMountainMesh(mountain.Radius, mountain.Height, 8);
                var filter = mountainObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = mountainObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mountainMaterial;
            }
        }

        private void CreateLines(Transform parent)
        {
            var lines = definition.Lines;
            if (lines == null)
            {
                return;
            }

            foreach (var line in lines)
            {
                if (line == null || line.Points == null || line.Points.Length < 2)
                {
                    continue;
                }

                var style = LineStyle.For(line.Kind);
                var material = CreateMaterial($"{line.Kind} Line", style.Color, false);

                if (style.DashLength > 0f)
                {
                    CreateDashedLine(parent, line, style, material);
                }
                else
                {
                    CreateContinuousLine(parent, line, style, material);
                }
            }
        }

        private void CreateContinuousLine(
            Transform parent,
            MapLineDefinition line,
            LineStyle style,
            Material material)
        {
            var lineObject = new GameObject(line.DisplayName);
            lineObject.transform.SetParent(parent, false);
            var renderer = ConfigureLineRenderer(lineObject, style, material);
            renderer.loop = line.Closed;
            renderer.positionCount = line.Points.Length;

            for (var index = 0; index < line.Points.Length; index++)
            {
                renderer.SetPosition(index, ToWorldPoint(line.Points[index], style.Height));
            }
        }

        private void CreateDashedLine(
            Transform parent,
            MapLineDefinition line,
            LineStyle style,
            Material material)
        {
            var lineRoot = new GameObject(line.DisplayName);
            lineRoot.transform.SetParent(parent, false);
            var segmentCount = line.Closed ? line.Points.Length : line.Points.Length - 1;

            for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                var start = line.Points[segmentIndex];
                var end = line.Points[(segmentIndex + 1) % line.Points.Length];
                var distance = Vector2.Distance(start, end);
                var direction = (end - start).normalized;
                var cursor = 0f;
                var dashIndex = 0;

                while (cursor < distance)
                {
                    var dashEnd = Mathf.Min(cursor + style.DashLength, distance);
                    var dashObject = new GameObject($"Dash {segmentIndex:00}-{dashIndex:00}");
                    dashObject.transform.SetParent(lineRoot.transform, false);
                    var renderer = ConfigureLineRenderer(dashObject, style, material);
                    renderer.positionCount = 2;
                    renderer.SetPosition(0, ToWorldPoint(start + direction * cursor, style.Height));
                    renderer.SetPosition(1, ToWorldPoint(start + direction * dashEnd, style.Height));
                    cursor = dashEnd + style.GapLength;
                    dashIndex++;
                }
            }
        }

        private static LineRenderer ConfigureLineRenderer(
            GameObject owner,
            LineStyle style,
            Material material)
        {
            var renderer = owner.AddComponent<LineRenderer>();
            renderer.useWorldSpace = false;
            renderer.alignment = LineAlignment.View;
            renderer.widthMultiplier = style.Width;
            renderer.numCapVertices = 2;
            renderer.numCornerVertices = 2;
            renderer.sharedMaterial = material;
            renderer.textureMode = LineTextureMode.Stretch;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void CreateSettlements(Transform parent)
        {
            var settlements = definition.Settlements;
            if (settlements == null)
            {
                return;
            }

            foreach (var settlement in settlements)
            {
                if (settlement == null)
                {
                    continue;
                }

                var markerRoot = new GameObject($"Settlement {settlement.StableId}");
                markerRoot.transform.SetParent(parent, false);
                markerRoot.transform.localPosition = new Vector3(settlement.Position.x, 0.22f, settlement.Position.y);

                var primitive = settlement.Kind == SettlementKind.Village
                    ? PrimitiveType.Cylinder
                    : PrimitiveType.Cube;
                var marker = GameObject.CreatePrimitive(primitive);
                marker.name = "Marker";
                marker.transform.SetParent(markerRoot.transform, false);
                marker.transform.localScale = MarkerScale(settlement.Kind);
                marker.GetComponent<Renderer>().sharedMaterial = CreateMaterial(
                    $"{settlement.Kind} Marker",
                    SettlementColor(settlement.Kind),
                    true);

                var labelObject = new GameObject("Label");
                labelObject.transform.SetParent(markerRoot.transform, false);
                labelObject.transform.localPosition = new Vector3(0f, MarkerLabelHeight(settlement.Kind), 0f);
                var label = labelObject.AddComponent<TextMesh>();
                label.text = settlement.DisplayName;
                label.anchor = TextAnchor.LowerCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 64;
                label.characterSize = LabelCharacterSize(settlement.Kind);
                label.color = new Color(0.16f, 0.13f, 0.10f);

                var semanticLabel = labelObject.AddComponent<CountyMapSemanticLabel>();
                semanticLabel.Configure(MaximumLabelZoom(settlement.Kind));
            }
        }

        private void CreatePolygon(
            string objectName,
            Transform parent,
            Vector2[] polygon,
            float height,
            Color color)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return;
            }

            var polygonObject = new GameObject(objectName);
            polygonObject.transform.SetParent(parent, false);
            var mesh = new Mesh { name = $"{objectName} Mesh" };
            var vertices = new Vector3[polygon.Length];
            for (var index = 0; index < polygon.Length; index++)
            {
                vertices[index] = ToWorldPoint(polygon[index], height);
            }

            var triangles = new int[(polygon.Length - 2) * 3];
            for (var index = 0; index < polygon.Length - 2; index++)
            {
                var triangleIndex = index * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = index + 2;
                triangles[triangleIndex + 2] = index + 1;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            generatedAssets.Add(mesh);

            var filter = polygonObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = polygonObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial($"{objectName} Material", color, true);
        }

        private Mesh CreateMountainMesh(float radius, float height, int sides)
        {
            var vertices = new Vector3[1 + sides * 2];
            vertices[0] = new Vector3(radius * 0.08f, height, -radius * 0.06f);

            for (var index = 0; index < sides; index++)
            {
                var angle = Mathf.PI * 2f * index / sides;
                vertices[1 + index] = new Vector3(
                    Mathf.Cos(angle) * radius * 0.55f,
                    height * (0.35f + 0.08f * Mathf.Sin(index * 2.1f)),
                    Mathf.Sin(angle) * radius * 0.55f);
                vertices[1 + sides + index] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
            }

            var triangles = new int[sides * 9];
            var cursor = 0;
            for (var index = 0; index < sides; index++)
            {
                var next = (index + 1) % sides;
                triangles[cursor++] = 0;
                triangles[cursor++] = 1 + next;
                triangles[cursor++] = 1 + index;

                triangles[cursor++] = 1 + index;
                triangles[cursor++] = 1 + next;
                triangles[cursor++] = 1 + sides + next;

                triangles[cursor++] = 1 + index;
                triangles[cursor++] = 1 + sides + next;
                triangles[cursor++] = 1 + sides + index;
            }

            var mesh = new Mesh { name = "Low Poly Mountain Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            generatedAssets.Add(mesh);
            return mesh;
        }

        private Material CreateMaterial(string materialName, Color color, bool lit)
        {
            var shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0f);
            }

            generatedAssets.Add(material);
            return material;
        }

        private void ClearGeneratedContent()
        {
            var generatedRoot = transform.Find(GeneratedRootName);
            if (generatedRoot != null)
            {
                DestroyGeneratedObject(generatedRoot.gameObject);
            }

            foreach (var generatedAsset in generatedAssets)
            {
                if (generatedAsset != null)
                {
                    DestroyGeneratedObject(generatedAsset);
                }
            }

            generatedAssets.Clear();
        }

        private static void DestroyGeneratedObject(UnityEngine.Object target)
        {
            if (UnityEngine.Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static Vector3 ToWorldPoint(Vector2 point, float height)
        {
            return new Vector3(point.x, height, point.y);
        }

        private static Color TerrainColor(TerrainRegionKind kind)
        {
            switch (kind)
            {
                case TerrainRegionKind.CountyGround:
                    return new Color(0.88f, 0.83f, 0.70f);
                case TerrainRegionKind.Plain:
                    return new Color(0.79f, 0.78f, 0.60f);
                case TerrainRegionKind.Hills:
                    return new Color(0.61f, 0.67f, 0.49f);
                case TerrainRegionKind.Basin:
                    return new Color(0.68f, 0.74f, 0.57f);
                default:
                    return Color.gray;
            }
        }

        private static Vector3 MarkerScale(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.CountySeat:
                    return new Vector3(1.65f, 0.85f, 1.65f);
                case SettlementKind.Town:
                    return new Vector3(1.15f, 0.62f, 1.15f);
                default:
                    return new Vector3(0.68f, 0.28f, 0.68f);
            }
        }

        private static Color SettlementColor(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.CountySeat:
                    return new Color(0.55f, 0.20f, 0.15f);
                case SettlementKind.Town:
                    return new Color(0.43f, 0.30f, 0.18f);
                default:
                    return new Color(0.30f, 0.36f, 0.24f);
            }
        }

        private static float MarkerLabelHeight(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.CountySeat:
                    return 1.35f;
                case SettlementKind.Town:
                    return 0.95f;
                default:
                    return 0.55f;
            }
        }

        private static float LabelCharacterSize(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.CountySeat:
                    return 0.46f;
                case SettlementKind.Town:
                    return 0.36f;
                default:
                    return 0.28f;
            }
        }

        private static float MaximumLabelZoom(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.CountySeat:
                    return 100f;
                case SettlementKind.Town:
                    return 30f;
                default:
                    return 17f;
            }
        }

        private readonly struct LineStyle
        {
            public readonly Color Color;
            public readonly float Width;
            public readonly float Height;
            public readonly float DashLength;
            public readonly float GapLength;

            private LineStyle(Color color, float width, float height, float dashLength, float gapLength)
            {
                Color = color;
                Width = width;
                Height = height;
                DashLength = dashLength;
                GapLength = gapLength;
            }

            public static LineStyle For(MapLineKind kind)
            {
                switch (kind)
                {
                    case MapLineKind.MainRiver:
                        return new LineStyle(new Color(0.27f, 0.48f, 0.58f), 0.78f, 0.16f, 0f, 0f);
                    case MapLineKind.Tributary:
                        return new LineStyle(new Color(0.34f, 0.56f, 0.64f), 0.42f, 0.16f, 0f, 0f);
                    case MapLineKind.Road:
                        return new LineStyle(new Color(0.45f, 0.32f, 0.17f), 0.32f, 0.18f, 0f, 0f);
                    case MapLineKind.CountyBoundary:
                        return new LineStyle(new Color(0.64f, 0.16f, 0.12f), 0.42f, 0.25f, 0f, 0f);
                    case MapLineKind.TownshipBoundary:
                        return new LineStyle(new Color(0.20f, 0.35f, 0.60f), 0.25f, 0.24f, 1.8f, 1.0f);
                    case MapLineKind.VillageBoundary:
                        return new LineStyle(new Color(0.24f, 0.48f, 0.25f), 0.17f, 0.23f, 0.45f, 0.55f);
                    default:
                        return new LineStyle(Color.black, 0.2f, 0.2f, 0f, 0f);
                }
            }
        }
    }
}
