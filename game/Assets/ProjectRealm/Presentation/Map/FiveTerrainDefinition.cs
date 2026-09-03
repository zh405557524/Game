using System;
using UnityEngine;

namespace ProjectRealm.Presentation.Map
{
    // Visual study only. These samples do not own simulation or save-game terrain data.
    public enum LandformKind { Plain, Hills, Mountain, Plateau, Basin }

    [CreateAssetMenu(menuName = "Project Realm/Map/Five Terrain Study")]
    public sealed class FiveTerrainDefinition : ScriptableObject
    {
        public int seed = 1628;
        [Min(100)] public float width = 360f;
        [Min(100)] public float depth = 240f;
        [Range(16, 96)] public int cellsPerChunk = 64;
        [Range(1, 12)] public int columns = 6;
        [Range(1, 12)] public int rows = 4;
        [Range(0.4f, 2f)] public float reliefScale = 1f;

        public static readonly string[] Names = { "平原", "丘陵", "山地", "高原", "盆地" };
        public static readonly string[] Descriptions =
        {
            "低平开阔 · 细微起伏\n观察大面积连续的平缓地表。",
            "坡缓顶圆 · 连续起伏\n观察山坡怎样自然接入平原。",
            "山脊相连 · 高差显著\n观察主峰、支脊与山间沟壑。",
            "整体抬升 · 顶部宽平\n观察台面与周围低地的高差。",
            "四周较高 · 中部低平\n观察环绕低地的坡地与出口。"
        };

        public Vector2 Focus(LandformKind kind)
        {
            var point = kind switch
            {
                LandformKind.Plain => new Vector2(-65f, -81f),
                LandformKind.Hills => new Vector2(101f, -48f),
                LandformKind.Mountain => new Vector2(-89f, 66f),
                LandformKind.Plateau => new Vector2(108f, 57f),
                _ => new Vector2(-17f, -11f)
            };
            return new Vector2(point.x * width / 360f, point.y * depth / 240f);
        }

        private struct ReliefFields
        {
            public float ground, mountain, hills, plateau, basinRim, basinFloor, floorHeight, edge;
        }

        public float Height(float worldX, float worldZ)
        {
            var f = EvaluateRelief(worldX, worldZ);
            float land = f.ground + f.mountain + f.hills + f.plateau + f.basinRim;
            land = Mathf.Lerp(land, f.floorHeight, f.basinFloor);
            return (2.8f + (land - 2.8f) * f.edge) * reliefScale;
        }

        // Reuses the same landform calculation, but excludes every other terrain's contribution.
        public float IsolatedHeight(LandformKind kind, float localX, float localZ)
        {
            Vector2 origin = Focus(kind);
            var f = EvaluateRelief(localX + origin.x, localZ + origin.y);
            float land = f.ground;
            switch (kind)
            {
                case LandformKind.Mountain: land += f.mountain; break;
                case LandformKind.Hills: land += f.hills; break;
                case LandformKind.Plateau: land += f.plateau; break;
                case LandformKind.Basin: land = Mathf.Lerp(land + f.basinRim, f.floorHeight, f.basinFloor); break;
            }
            float edge = (1f-Smooth(68,80,Mathf.Abs(localX))) * (1f-Smooth(54,64,Mathf.Abs(localZ)));
            return (2.8f + (land-2.8f)*edge)*reliefScale;
        }

        public Mesh BuildIsolated(LandformKind kind, int cells = 160)
        {
            if (cells < 8 || cells > 240) throw new ArgumentOutOfRangeException(nameof(cells));
            int stride = cells + 1;
            var vertices = new Vector3[stride*stride];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[cells*cells*6];
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                int i=z*stride+x;
                float px=-80f+x*160f/cells, pz=-64f+z*128f/cells;
                vertices[i]=new Vector3(px,IsolatedHeight(kind,px,pz),pz);
                const float e=0.8f;
                normals[i]=new Vector3(IsolatedHeight(kind,px-e,pz)-IsolatedHeight(kind,px+e,pz),2*e,
                    IsolatedHeight(kind,px,pz-e)-IsolatedHeight(kind,px,pz+e)).normalized;
                uv[i]=new Vector2((float)x/cells,(float)z/cells);
                if(x==cells||z==cells)continue;
                int t=(z*cells+x)*6;
                triangles[t]=i;triangles[t+1]=i+stride;triangles[t+2]=i+1;
                triangles[t+3]=i+1;triangles[t+4]=i+stride;triangles[t+5]=i+stride+1;
            }
            var mesh=new Mesh {name=kind+" isolated terrain"};
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();
            return mesh;
        }

        private ReliefFields EvaluateRelief(float worldX, float worldZ)
        {
            float x = worldX * 360f / width, z = worldZ * 240f / depth;
            float ground = 3f + Noise(x, z, 0.025f) * 1.2f + Noise(x, z, 0.095f) * 0.18f;
            float warpX = (Noise(x, z, 0.035f) - 0.5f) * 23f;
            float warpZ = (Noise(x + 170, z - 85, 0.035f) - 0.5f) * 19f;
            float a = x + warpX, b = z + warpZ;

            // Overlapping elongated peaks and spurs, not isolated cone objects.
            float mountains = Peak(a, b, -127, 60, 24, 20, 33) + Peak(a, b, -99, 79, 22, 21, 57)
                + Peak(a, b, -73, 53, 22, 26, 48) + Peak(a, b, -44, 79, 26, 18, 43)
                + Peak(a, b, -109, 31, 19, 30, 24) + Peak(a, b, -52, 32, 19, 22, 24)
                + Peak(a, b, -145, 39, 19, 18, 17) + Peak(a, b, -13, 87, 24, 20, 23);
            float ridgeSignal = Noise(a, b, 0.075f) * 2f - 1f;
            float ridges = 1f - Mathf.Sqrt(ridgeSignal * ridgeSignal + 0.045f);
            mountains *= 0.42f + 0.75f * ridges * ridges;
            mountains += mountains * (Noise(a, b, 0.17f) - 0.5f) * 0.08f;

            float hillMask = Mathf.Exp(-Sq((x - 104f) / 72f) - Sq((z + 46f) / 52f));
            float hills = hillMask * (5f + 20f * Mathf.Pow(Noise(x + warpX, z + warpZ, 0.047f), 2f)
                + 3f * Noise(x, z, 0.11f));
            hills += Peak(a, b, 63, -39, 16, 19, 7) + Peak(a, b, 117, -70, 17, 13, 6);

            float px = (a - 107f) / 53f, pz = (b - 59f) / 38f;
            float angle = Mathf.Atan2(pz, px);
            float plateauRadius = Mathf.Sqrt(px * px + pz * pz)
                + 0.12f * Mathf.Sin(angle * 3f + 0.7f) + 0.065f * Mathf.Cos(angle * 7f);
            float plateau = (1f - Smooth(0.82f, 1.16f, plateauRadius)) * 34f;
            plateau += (1f - Smooth(0.4f, 0.82f, plateauRadius)) * Noise(x, z, 0.022f) * 5f;
            plateau += (1f - Smooth(0.65f, 1.08f, plateauRadius)) * Noise(x, z, 0.065f) * 1.5f;

            float bx = (a + 17f) / 55f, bz = (b + 11f) / 39f;
            float basinAngle = Mathf.Atan2(bz, bx);
            float radius = Mathf.Sqrt(bx * bx + bz * bz) + 0.13f * Mathf.Sin(basinAngle * 3f) + 0.06f * Mathf.Cos(basinAngle * 5f);
            float outlet = 1f - 0.92f * Mathf.Exp(-Sq((x + 3f) / 18f)) * (1f - Smooth(-45, -19, z));
            float rim = Mathf.Exp(-Sq((radius - 1.03f) / 0.27f)) * (9f + 17f * Mathf.Pow(Noise(x, z, 0.072f), 1.4f)) * outlet;
            float floorMask = 1f - Smooth(0.5f, 0.86f, radius);
            // Roll off the outside to a quiet paper margin; no abrupt vertical cut face.
            float edge = (1f - Smooth(157f, 180f, Mathf.Abs(x))) * (1f - Smooth(102f, 120f, Mathf.Abs(z)));
            return new ReliefFields { ground=ground, mountain=mountains, hills=hills, plateau=plateau,
                basinRim=rim, basinFloor=floorMask, floorHeight=3.2f+Noise(x,z,0.035f)*0.65f, edge=edge };
        }

        // RGB = hills, mountain, plateau; A = basin. Plain is the remaining weight.
        public Color Weights(float worldX, float worldZ)
        {
            float x = worldX * 360f / width, z = worldZ * 240f / depth;
            float hills = 1.6f * Mathf.Exp(-Sq((x - 100f) / 76f) - Sq((z + 47f) / 57f));
            float mountains = 3f * Mathf.Exp(-Sq((x + 87f) / 71f) - Sq((z - 65f) / 45f));
            float plateau = 4f * Mathf.Exp(-Mathf.Pow((x - 107f) / 55f, 4f) - Mathf.Pow((z - 59f) / 46f, 4f));
            float basin = 2.7f * Mathf.Exp(-Mathf.Pow((x + 17f) / 55f, 4f) - Mathf.Pow((z + 11f) / 41f, 4f));
            float plain = 0.35f;
            float total = plain + hills + mountains + plateau + basin;
            return new Color(hills / total, mountains / total, plateau / total, basin / total);
        }

        public Vector3 Normal(float x, float z)
        {
            const float e = 0.8f;
            return new Vector3(Height(x - e, z) - Height(x + e, z), 2f * e,
                Height(x, z - e) - Height(x, z + e)).normalized;
        }

        public Mesh BuildChunk(int column, int row)
        {
            if (column < 0 || column >= columns || row < 0 || row >= rows)
                throw new ArgumentOutOfRangeException(nameof(column));
            int n = cellsPerChunk, stride = n + 1;
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var shading = new Vector2[vertices.Length];
            var triangles = new int[n * n * 6];
            for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                int v = j * stride + i;
                float x = -width * 0.5f + (column * n + i) * width / (columns * n);
                float z = -depth * 0.5f + (row * n + j) * depth / (rows * n);
                vertices[v] = new Vector3(x, Height(x, z), z);
                normals[v] = Normal(x, z);
                colors[v] = Weights(x, z);
                uv[v] = new Vector2(x / 20f, z / 20f);
                shading[v] = BakedShade(x, z, vertices[v].y);
                if (i == n || j == n) continue;
                int t = (j * n + i) * 6;
                triangles[t] = v; triangles[t + 1] = v + stride; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + stride; triangles[t + 5] = v + stride + 1;
            }
            var mesh = new Mesh { name = $"Terrain {column:D2}-{row:D2}" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors; mesh.uv = uv; mesh.uv2 = shading;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // Static relief shading for this study only; never used as gameplay visibility.
        // Same world-space sample positions on both sides of every chunk boundary.
        private Vector2 BakedShade(float x, float z, float height)
        {
            float shadow = 0;
            for (int i = 1; i <= 12; i++)
            {
                float distance = i * 3.5f;
                float blocker = Height(x - distance * 0.384f, z - distance * 0.548f) - (height + distance * 0.743f);
                shadow = Mathf.Max(shadow, Smooth(-0.8f, 2.4f, blocker));
            }
            float average = (Height(x - 4, z) + Height(x + 4, z) + Height(x, z - 4) + Height(x, z + 4)) * 0.25f;
            float cavity = Mathf.Clamp01((average - height) * 0.45f);
            return new Vector2(1f - shadow * 0.32f, 1f - cavity * 0.24f);
        }

        // Unity PerlinNoise can slightly exceed [0,1]; fractional powers require clamping.
        private float Noise(float x, float z, float scale) => Mathf.Clamp01(Mathf.PerlinNoise(x * scale + seed * 0.0137f, z * scale + seed * 0.0291f));
        private static float Sq(float v) => v * v;
        private static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, v));
        private static float Peak(float x, float z, float cx, float cz, float rx, float rz, float height)
            => Mathf.Exp(-(Sq((x - cx) / rx) + Sq((z - cz) / rz)) * 1.45f) * height;
    }
}
