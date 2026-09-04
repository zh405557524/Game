using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRealm.UnityPresentation.Map.Water
{
    public sealed class WaterStudyField
    {
        public readonly WaterBodyStudyDefinition definition;
        public readonly RiverSample[] stream;
        public WaterStudyField(WaterBodyStudyDefinition definition)
        {
            if (!definition.Validate(out var reason)) throw new ArgumentException(reason);
            this.definition = definition;
            if (definition.kind != WaterStudyKind.Stream) return;
            var river = ScriptableObject.CreateInstance<RiverStudyDefinition>();
            try { river.stations = definition.stream; river.groundSize = definition.size; river.samplesPerSpan = 28; stream = RiverStudyGeometry.Sample(river); }
            finally { UnityEngine.Object.DestroyImmediate(river); }
            for (int i = 1; i < stream.Length; i++)
                if (stream[i].position.x < stream[i - 1].position.x) throw new ArgumentException("The sampled stream turns uphill; spread the control points.");
        }
        public float Level(Vector2 p) => 0.2f + (definition.kind == WaterStudyKind.Stream ? (definition.size.x * 0.5f - p.x) * definition.streamSlope : 0);
        // An approximate signed shore distance, consistently shared by ground and surface.
        public float Shore(Vector2 p)
        {
            float d = float.MaxValue;
            if (stream != null) d = RiverStudyGeometry.ShoreDistance(p, stream);
            else if (definition.kind == WaterStudyKind.Coast) d = -12 + 12 * Mathf.Sin(p.y * 0.06f) + 9 * Mathf.Cos(p.y * 0.115f) + 4 * Mathf.Sin(p.y * 0.2f) - p.x;
            else foreach (var basin in definition.basins) d = Mathf.Min(d, BasinDistance(p, basin));
            foreach (var island in definition.islands) d = Mathf.Max(d, -BasinDistance(p, island));
            return d;
        }
        public static float BasinDistance(Vector2 p, StudyBasin basin)
        {
            var q = p - basin.center; float angle = Mathf.Atan2(q.y / basin.radius.y, q.x / basin.radius.x);
            float rim = 1 + basin.irregularity * (0.6f * Mathf.Sin(angle * 3 + 0.6f) + 0.28f * Mathf.Cos(angle * 5) + 0.12f * Mathf.Sin(angle * 9));
            return (new Vector2(q.x / basin.radius.x, q.y / basin.radius.y).magnitude - rim) * Mathf.Min(basin.radius.x, basin.radius.y);
        }
        public float Depth(float d) => definition.depth * Mathf.SmoothStep(0, 1, Mathf.Clamp01(-d / (definition.kind == WaterStudyKind.Stream ? 2.5f : definition.kind == WaterStudyKind.Coast ? 28 : 12))) + 0.08f;
        public float GroundHeight(Vector2 p, float d)
        {
            float level = Level(p);
            if (d < 0) return level - Depth(d);
            float n = Noise(p, 0.045f);
            return level - 0.08f + definition.bankHeight * Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / definition.bankWidth))
                + (n - 0.5f) * definition.bankHeight * 0.5f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / 12));
        }
        public float Noise(Vector2 p, float scale) => Mathf.Clamp01(Mathf.PerlinNoise(p.x * scale + definition.seed * 0.017f, p.y * scale + 91));
        public Color GroundColor(Vector2 p, float d)
        {
            bool marsh = definition.kind == WaterStudyKind.Wetland, coast = definition.kind == WaterStudyKind.Coast;
            var soil = coast ? new Color(0.79f, 0.76f, 0.64f) : new Color(0.72f, 0.71f, 0.58f);
            var wet = marsh ? new Color(0.43f, 0.48f, 0.30f) : new Color(0.47f, 0.50f, 0.40f);
            var meadow = Color.Lerp(new Color(0.51f, 0.58f, 0.40f), new Color(0.72f, 0.72f, 0.54f), Noise(p, 0.07f));
            // Both sides converge to the same wet colour at d=0. A discontinuity here
            // produces grid-shaped bright steps through the translucent water edge.
            Color c = d < 0 ? Color.Lerp(wet * 0.8f, wet, Mathf.Clamp01((d + 15) / 15))
                : Color.Lerp(wet, soil, Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / (coast ? 4 : 2))));
            if (d > 2) c = Color.Lerp(c, meadow, Mathf.SmoothStep(0, marsh ? 1 : 0.85f, Mathf.Clamp01((d - 2) / (coast ? 22 : 9))));
            return (c * (0.96f + 0.08f * Noise(p, 0.5f))).linear;
        }
    }

    public static class WaterBodyStudyGeometry
    {
        public static Mesh Grid(WaterStudyField field, bool water, int overrideResolution = 0)
        {
            var d = field.definition; int nx = overrideResolution == 0 ? d.resolution : overrideResolution;
            if (nx < 8 || nx > 240) throw new ArgumentOutOfRangeException(nameof(overrideResolution));
            int nz = Mathf.Clamp(Mathf.RoundToInt(nx * d.size.y / d.size.x), 8, 240), stride = nx + 1;
            var vertices = new Vector3[stride * (nz + 1)]; var uv = new Vector2[vertices.Length]; var diagnostic = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length]; var triangles = new int[nx * nz * 6];
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                int i = z * stride + x; var p = new Vector2((x / (float)nx - 0.5f) * d.size.x, (z / (float)nz - 0.5f) * d.size.y);
                float shore = field.Shore(p); vertices[i] = new Vector3(p.x, water ? field.Level(p) : field.GroundHeight(p, shore), p.y);
                uv[i] = p / 28; diagnostic[i] = new Vector2(shore, field.Depth(shore)); colors[i] = field.GroundColor(p, shore);
                if (x == nx || z == nz) continue;
                int t = (z * nx + x) * 6; triangles[t] = i; triangles[t + 1] = i + stride; triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1; triangles[t + 4] = i + stride; triangles[t + 5] = i + stride + 1;
            }
            return Mesh(water ? "Closed water surface" : "Water bed and shoreline", vertices, triangles, uv, diagnostic, colors);
        }
        public static Mesh Stream(WaterStudyField field)
        {
            if (field.stream == null) throw new ArgumentException("Not a stream study.");
            var mesh = RiverStudyGeometry.Water(field.stream); var vertices = mesh.vertices; var uv = mesh.uv; var diagnostics = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = new Vector2(vertices[i].x, vertices[i].z); vertices[i].y = field.Level(p);
                float shore = (Mathf.Abs(uv[i].x - 0.5f) - 0.5f) * field.stream[i / 13].width;
                diagnostics[i] = new Vector2(shore, field.Depth(shore));
            }
            mesh.name = "Shallow downhill stream"; mesh.vertices = vertices; mesh.uv2 = diagnostics; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        // Separate, optional reference geometry. No vegetation, resource or navigation records are emitted.
        public static Mesh ReferenceDetails(WaterStudyField field)
        {
            var random = new System.Random(field.definition.seed); var vertices = new List<Vector3>(); var triangles = new List<int>(); var colors = new List<Color>();
            bool reeds = field.definition.kind == WaterStudyKind.Wetland || field.definition.kind == WaterStudyKind.Pond;
            int wanted = reeds ? (field.definition.kind == WaterStudyKind.Wetland ? 145 : 36) : 22, placed = 0;
            for (int attempt = 0; attempt < 12000 && placed < wanted; attempt++)
            {
                var p = new Vector2(((float)random.NextDouble() - 0.5f) * field.definition.size.x * 0.92f, ((float)random.NextDouble() - 0.5f) * field.definition.size.y * 0.92f);
                float shore = field.Shore(p); if (shore < 0.6f || shore > (reeds ? 4 : 8)) continue; placed++;
                var anchor = new Vector3(p.x, field.GroundHeight(p, shore), p.y);
                if (reeds)
                {
                    for (int blade = 0; blade < 5; blade++)
                    {
                        float a = (float)random.NextDouble() * Mathf.PI * 2, height = 0.9f + (float)random.NextDouble() * 1.6f;
                        var offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); int i = vertices.Count;
                        vertices.Add(anchor - offset * 0.12f); vertices.Add(anchor + offset * 0.12f); vertices.Add(anchor + Vector3.up * height + offset * 0.45f);
                        triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                        var c = Color.Lerp(new Color(0.38f, 0.43f, 0.24f), new Color(0.69f, 0.62f, 0.38f), (float)random.NextDouble()).linear;
                        colors.Add(c); colors.Add(c); colors.Add(c * 1.12f);
                    }
                }
                else
                {
                    float radius = 0.5f + (float)random.NextDouble() * (field.definition.kind == WaterStudyKind.Coast ? 2 : 0.8f);
                    int i = vertices.Count; vertices.Add(anchor + Vector3.up * radius * 0.65f); colors.Add(new Color(0.55f, 0.56f, 0.48f).linear);
                    for (int ring = 0; ring < 8; ring++)
                    {
                        float a = ring * Mathf.PI / 4; vertices.Add(anchor + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius * 0.72f));
                        colors.Add(Color.Lerp(new Color(0.40f, 0.44f, 0.37f), new Color(0.64f, 0.63f, 0.54f), ring / 7f).linear);
                        triangles.Add(i); triangles.Add(i + 1 + (ring + 1) % 8); triangles.Add(i + 1 + ring);
                    }
                }
            }
            return Mesh("Optional scale reference", vertices.ToArray(), triangles.ToArray(), new Vector2[vertices.Count], new Vector2[vertices.Count], colors.ToArray());
        }
        private static Mesh Mesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uv, Vector2[] diagnostics, Color[] colors)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.vertices = vertices; mesh.triangles = triangles;
            mesh.uv = uv; mesh.uv2 = diagnostics; mesh.colors = colors; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
