using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRealm.Presentation.Map.Mountain
{
    /// <summary>Deterministic, editable height field. All geometry comes from controls, never an illustration's luminance.</summary>
    public static class MountainLookdevGeometry
    {
        public static Mesh Build(MountainLookdevProfile profile)
        {
            if (profile == null) throw new ArgumentException("Profile is missing.");
            if (!profile.Validate(out var error, false)) throw new ArgumentException(error);
            int n = profile.cells, stride = n + 1;
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var diagnostics = new Vector2[vertices.Length];
            var heights = new float[stride * stride];
            float dx = profile.size.x / n, dz = profile.size.y / n;
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    int k = z * stride + x;
                    float wx = x * dx - profile.size.x * 0.5f, wz = z * dz - profile.size.y * 0.5f;
                    heights[k] = SampleHeight(profile, wx, wz);
                    vertices[k] = new Vector3(wx, heights[k], wz);
                    uv[k] = new Vector2(x / (float)n, z / (float)n);
                }
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    int k = z * stride + x;
                    float l = heights[z * stride + Mathf.Max(0, x - 1)], r = heights[z * stride + Mathf.Min(n, x + 1)];
                    float b = heights[Mathf.Max(0, z - 1) * stride + x], f = heights[Mathf.Min(n, z + 1) * stride + x];
                    normals[k] = new Vector3((l - r) / ((x == 0 || x == n) ? dx : 2 * dx), 1, (b - f) / ((z == 0 || z == n) ? dz : 2 * dz)).normalized;
                    float curvature = (l + r + b + f - 4 * heights[k]) / (dx + dz);
                    diagnostics[k] = new Vector2(curvature, heights[k]);
                }
            var triangles = new int[n * n * 6];
            int t = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int k = z * stride + x;
                    triangles[t++] = k; triangles[t++] = k + stride; triangles[t++] = k + 1;
                    triangles[t++] = k + 1; triangles[t++] = k + stride; triangles[t++] = k + stride + 1;
                }
            var mesh = new Mesh { name = "Mountain / editable peaks-ridges-valleys", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.uv2 = diagnostics; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static float SampleHeight(MountainLookdevProfile p, float x, float z)
        {
            float result = 0;
            for (int i = 0; i < p.peaks.Length; i++)
            {
                var peak = p.peaks[i];
                float a = peak.heading * Mathf.Deg2Rad, cx = x - peak.center.x, cz = z - peak.center.y;
                float u = (cx * Mathf.Cos(a) - cz * Mathf.Sin(a)) / (peak.radius.x * 1.08f);
                float v = (cx * Mathf.Sin(a) + cz * Mathf.Cos(a)) / (peak.radius.y * 1.08f);
                float angle = Mathf.Atan2(v, u), radial = Mathf.Sqrt(u * u + v * v);
                float phase = i * 1.71f + p.seed * 0.019f;
                // Unequal buttresses broaden down-slope; they are not high-frequency noise painted onto cones.
                float buttress = Mathf.Sin(angle * 3 + phase + radial * 0.4f) * 0.16f + Mathf.Sin(angle * 7 - phase) * 0.05f;
                radial *= 1 + buttress * peak.folds * 3f;
                // Broad lower shoulders connect neighbouring masses. Steeper upper walls have an unequal crown.
                float mass = 0.24f * Mathf.Exp(-0.72f * radial * radial) +
                    0.64f * Mathf.Exp(-4f * Mathf.Pow(radial, 5.4f)) +
                    0.12f * Mathf.Exp(-3.6f * Mathf.Pow(radial, 1.5f));
                float value = peak.height * mass;
                result = SmoothMax(result, value, 1.3f);
            }
            if (p.ridges != null)
                foreach (var ridge in p.ridges)
                    for (int i = 1; i < ridge.points.Length; i++)
                    {
                        var a = ridge.points[i - 1]; var b = ridge.points[i];
                        float t = SegmentFraction(new Vector2(x, z), new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                        float distance = Vector2.Distance(new Vector2(x, z), Vector2.Lerp(new Vector2(a.x, a.z), new Vector2(b.x, b.z), t));
                        float value = Mathf.Lerp(a.y, b.y, t) * Mathf.Exp(-2.2f * Mathf.Pow(distance / ridge.width, 2.4f));
                        result = SmoothMax(result, value, 1.1f);
                    }
            if (p.valleys != null)
                foreach (var valley in p.valleys)
                {
                    float t = SegmentFraction(new Vector2(x, z), valley.start, valley.end);
                    float d = Vector2.Distance(new Vector2(x, z), Vector2.Lerp(valley.start, valley.end, t));
                    result -= valley.depth * Mathf.Exp(-2 * d * d / (valley.width * valley.width));
                }
            float broad = (Mathf.PerlinNoise(x * 0.065f + p.seed * 0.017f, z * 0.06f + 79) - 0.5f) * 2;
            float fractures = 1 - Mathf.Abs(Mathf.PerlinNoise(x * 0.22f + 53, z * 0.115f + p.seed * 0.012f) * 2 - 1);
            float relief = (fractures - .7f) * 1.45f + (Mathf.PerlinNoise(x * .46f + 31, z * .39f + 16) - .5f) * .18f;
            result += (broad + relief) * (1.2f + Mathf.Sqrt(Mathf.Max(0, result)) * .40f) * p.rockRelief * Mathf.SmoothStep(0, 1, result / 9);
            float edge = Mathf.Min(p.size.x * 0.5f - Mathf.Abs(x), p.size.y * 0.5f - Mathf.Abs(z));
            return Mathf.Max(0, result) * Mathf.SmoothStep(0, 1, edge / 14);
        }

        public static Vector3 SampleNormal(MountainLookdevProfile p, float x, float z)
        {
            const float step = 0.5f;
            return new Vector3(SampleHeight(p, x - step, z) - SampleHeight(p, x + step, z), 2 * step, SampleHeight(p, x, z - step) - SampleHeight(p, x, z + step)).normalized;
        }

        private static float SegmentFraction(Vector2 p, Vector2 a, Vector2 b)
        { Vector2 ab = b - a; return ab.sqrMagnitude < 0.0001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); }
        private static float SmoothMax(float a, float b, float k)
        { float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k; return Mathf.Max(a, b) + h * h * k * 0.25f; }
    }
}
