using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectRealm.Presentation.Map.Water
{
    public readonly struct RiverSample
    {
        public readonly Vector2 position, tangent;
        public readonly float width, distance;
        public RiverSample(Vector2 position, Vector2 tangent, float width, float distance)
        { this.position = position; this.tangent = tangent; this.width = width; this.distance = distance; }
    }

    public static class RiverStudyGeometry
    {
        public const float WaterHeight = 0.16f;
        public const int CrossSegments = 12;

        public static RiverSample[] Sample(RiverStudyDefinition definition)
        {
            if (!definition.Validate(out string reason)) throw new ArgumentException(reason);
            var controls = definition.stations;
            int n = (controls.Length - 1) * definition.samplesPerSpan + 1;
            var positions = new Vector2[n]; var widths = new float[n]; var result = new RiverSample[n];
            for (int i = 0; i < n; i++)
            {
                int span = Mathf.Min(i / definition.samplesPerSpan, controls.Length - 2);
                float t = (i - span * definition.samplesPerSpan) / (float)definition.samplesPerSpan;
                Vector2 p0 = controls[Mathf.Max(0, span - 1)].position, p1 = controls[span].position;
                Vector2 p2 = controls[span + 1].position, p3 = controls[Mathf.Min(controls.Length - 1, span + 2)].position;
                positions[i] = 0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                widths[i] = Mathf.Lerp(controls[span].width, controls[span + 1].width, Mathf.SmoothStep(0, 1, t));
            }
            float distance = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) distance += Vector2.Distance(positions[i - 1], positions[i]);
                var tangent = (positions[Mathf.Min(i + 1, n - 1)] - positions[Mathf.Max(i - 1, 0)]).normalized;
                if (tangent.sqrMagnitude < 0.9f) throw new ArgumentException("Degenerate river tangent; spread the control points.");
                result[i] = new RiverSample(positions[i], tangent, widths[i], distance);
            }
            return result;
        }

        public static Mesh Water(RiverSample[] samples)
        {
            int stride = CrossSegments + 1;
            var vertices = new Vector3[samples.Length * stride]; var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length]; var triangles = new int[(samples.Length - 1) * CrossSegments * 6];
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i]; var left = new Vector2(-s.tangent.y, s.tangent.x);
                for (int j = 0; j <= CrossSegments; j++)
                {
                    int index = i * stride + j; float u = j / (float)CrossSegments;
                    Vector2 p = s.position + left * ((u - 0.5f) * s.width);
                    vertices[index] = new Vector3(p.x, WaterHeight, p.y); uv[index] = new Vector2(u, s.distance); normals[index] = Vector3.up;
                    if (i == samples.Length - 1 || j == CrossSegments) continue;
                    int t = (i * CrossSegments + j) * 6;
                    triangles[t] = index; triangles[t + 1] = index + 1; triangles[t + 2] = index + stride;
                    triangles[t + 3] = index + 1; triangles[t + 4] = index + stride + 1; triangles[t + 5] = index + stride;
                }
            }
            var mesh = new Mesh { name = "River surface • arc-length UV", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds();
            var points = mesh.vertices;
            for (int t = 0; t < triangles.Length; t += 3)
                if (Vector3.Cross(points[triangles[t + 1]] - points[triangles[t]], points[triangles[t + 2]] - points[triangles[t]]).y <= 0)
                { UnityEngine.Object.DestroyImmediate(mesh); throw new ArgumentException("River banks fold at a bend. Widen the bend or reduce channel width."); }
            return mesh;
        }

        public static float ShoreDistance(Vector2 point, RiverSample[] samples)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < samples.Length - 1; i++)
            {
                Vector2 ab = samples[i + 1].position - samples[i].position;
                float t = Mathf.Clamp01(Vector2.Dot(point - samples[i].position, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                float d = Vector2.Distance(point, samples[i].position + ab * t) - Mathf.Lerp(samples[i].width, samples[i + 1].width, t) * 0.5f;
                nearest = Mathf.Min(nearest, d);
            }
            return nearest;
        }

        public static float BedHeight(float shoreDistance, float noise)
        {
            if (shoreDistance < 0) return 0.02f - 1.65f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(-shoreDistance / 7));
            return 0.02f + Mathf.SmoothStep(0, 0.85f, Mathf.Clamp01(shoreDistance / 3))
                + (noise - 0.5f) * 0.8f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(shoreDistance / 8));
        }

        public static Mesh Ground(RiverStudyDefinition definition, RiverSample[] samples, int cellsX = 184, int cellsZ = 126)
        {
            if (cellsX < 8 || cellsX > 256 || cellsZ < 8 || cellsZ > 256) throw new ArgumentOutOfRangeException(nameof(cellsX));
            int stride = cellsX + 1; var vertices = new Vector3[stride * (cellsZ + 1)];
            var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length]; var triangles = new int[cellsX * cellsZ * 6];
            for (int z = 0; z <= cellsZ; z++)
            for (int x = 0; x <= cellsX; x++)
            {
                int index = z * stride + x;
                var p = new Vector2((x / (float)cellsX - 0.5f) * definition.groundSize.x, (z / (float)cellsZ - 0.5f) * definition.groundSize.y);
                float n = Mathf.Clamp01(Mathf.PerlinNoise(p.x * 0.038f + definition.seed * 0.07f, p.y * 0.042f + 33));
                float d = ShoreDistance(p, samples);
                vertices[index] = new Vector3(p.x, BedHeight(d, n), p.y); uv[index] = p / 45;
                Color c = d < 0 ? Color.Lerp(new Color(0.43f, 0.48f, 0.43f), new Color(0.50f, 0.54f, 0.44f), Mathf.Clamp01((d + 7) / 7))
                    : Color.Lerp(new Color(0.50f, 0.54f, 0.44f), new Color(0.73f, 0.72f, 0.61f), Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / (1.5f + 2 * n))));
                Color meadow = Color.Lerp(new Color(0.60f, 0.65f, 0.53f), new Color(0.78f, 0.77f, 0.65f), n);
                if (d > 3) c = Color.Lerp(c, meadow, Mathf.SmoothStep(0, 0.84f, Mathf.Clamp01((d - 3) / 11)));
                float fleck = Mathf.Clamp01(Mathf.PerlinNoise(p.x * 0.39f + 17, p.y * 0.39f + definition.seed * 0.04f));
                c *= 0.95f + 0.10f * fleck;
                colors[index] = c.linear;
                if (x == cellsX || z == cellsZ) continue;
                int t = (z * cellsX + x) * 6;
                triangles[t] = index; triangles[t + 1] = index + stride; triangles[t + 2] = index + 1;
                triangles[t + 3] = index + 1; triangles[t + 4] = index + stride; triangles[t + 5] = index + stride + 1;
            }
            var mesh = new Mesh { name = "River channel and floodplain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh FlowArrows(RiverSample[] samples, Vector2 groundSize)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); float next = 18;
            foreach (var s in samples)
            {
                if (s.distance < next) continue; next += 18;
                // Keep the whole arrow inside the visible board, not only its center.
                if (Mathf.Abs(s.position.x) > groundSize.x * 0.5f - 3 || Mathf.Abs(s.position.y) > groundSize.y * 0.5f - 3) continue;
                var center = new Vector3(s.position.x, WaterHeight + 0.15f, s.position.y);
                var forward = new Vector3(s.tangent.x, 0, s.tangent.y); var left = new Vector3(-s.tangent.y, 0, s.tangent.x);
                int i = vertices.Count;
                vertices.Add(center + forward * 2.4f); vertices.Add(center - forward * 1.5f + left * 1.1f); vertices.Add(center - forward * 1.5f - left * 1.1f);
                triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 1);
            }
            var mesh = new Mesh { name = "Downstream diagnostic arrows" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
