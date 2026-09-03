using System;
using UnityEngine;

namespace ProjectRealm.Presentation.Map.Water
{
    public enum WaterStudyKind { Stream = 1, Lake = 2, Pond = 3, Wetland = 4, Coast = 5 }

    [Serializable]
    public struct StudyBasin
    {
        public Vector2 center, radius;
        [Range(0, 0.25f)] public float irregularity;
        public StudyBasin(float x, float z, float rx, float rz, float irregularity = 0.12f)
        { center = new Vector2(x, z); radius = new Vector2(rx, rz); this.irregularity = irregularity; }
    }

    // Editable, synthetic presentation fixtures. Never loaded into the formal Definition DB or a save.
    [CreateAssetMenu(menuName = "Project Realm/Map/Water/Water Body Study")]
    public sealed class WaterBodyStudyDefinition : ScriptableObject
    {
        public WaterStudyKind kind = WaterStudyKind.Lake;
        public int seed = 1628;
        public Vector2 size = new Vector2(150, 110);
        [Range(48, 240)] public int resolution = 180;
        [Range(0.2f, 12)] public float depth = 5;
        [Range(0.2f, 4)] public float bankHeight = 1.6f;
        [Range(1, 15)] public float bankWidth = 5;
        [Range(0, 0.06f)] public float streamSlope = 0.025f;
        [Range(0, 1)] public float animationSpeed = 0.22f;
        public Texture2D waterTexture, groundTexture;
        public Color deepColor = new Color(0.27f, 0.43f, 0.46f);
        public Color shallowColor = new Color(0.52f, 0.64f, 0.59f);
        public StudyBasin[] basins = { new StudyBasin(-2, 0, 48, 32, 0.16f) };
        public StudyBasin[] islands = Array.Empty<StudyBasin>();
        public RiverStation[] stream = Array.Empty<RiverStation>();
        [Tooltip("可隐藏的石块/芦苇尺度参照，不是植被或资源数据。")]
        public bool referenceDetails = true;
        public string CaseId => "water/" + Folder + "/study-v1";
        public string Folder => ((int)kind + 1).ToString("D2") + "_" + kind;
        public string DisplayName => new[] { "溪流", "湖泊", "池塘", "湿地", "海岸" }[(int)kind - 1];
        public float ViewSize => size.y * 0.56f;

        public void SetDefaults(WaterStudyKind value)
        {
            kind = value; seed = 1628 + (int)value * 31; resolution = 180;
            size = new Vector2(150, 110); depth = 5; bankHeight = 1.6f; bankWidth = 5; animationSpeed = 0.22f;
            deepColor = new Color(0.27f, 0.43f, 0.46f); shallowColor = new Color(0.52f, 0.64f, 0.59f);
            basins = new[] { new StudyBasin(-2, 0, 48, 32, 0.16f) };
            islands = Array.Empty<StudyBasin>(); stream = Array.Empty<RiverStation>();
            if (kind == WaterStudyKind.Stream)
            {
                size = new Vector2(128, 90); depth = 0.85f; bankHeight = 2; bankWidth = 6; animationSpeed = 0.7f;
                deepColor = new Color(0.36f, 0.52f, 0.51f); shallowColor = new Color(0.62f, 0.69f, 0.59f);
                basins = Array.Empty<StudyBasin>();
                stream = new[] { new RiverStation(-76, 26, 3), new RiverStation(-45, 22, 3.8f), new RiverStation(-21, 0, 4.7f),
                    new RiverStation(4, 9, 4), new RiverStation(24, -9, 5), new RiverStation(46, -23, 5.6f), new RiverStation(76, -21, 6) };
            }
            else if (kind == WaterStudyKind.Lake) islands = new[] { new StudyBasin(14, 4, 6.8f, 4.5f, 0.15f) };
            else if (kind == WaterStudyKind.Pond)
            {
                size = new Vector2(64, 48); depth = 1.6f; bankHeight = 0.9f; bankWidth = 2.8f; animationSpeed = 0.12f;
                basins = new[] { new StudyBasin(-1, 0, 18, 12.5f, 0.04f) };
                deepColor = new Color(0.32f, 0.46f, 0.40f); shallowColor = new Color(0.58f, 0.65f, 0.49f);
            }
            else if (kind == WaterStudyKind.Wetland)
            {
                size = new Vector2(116, 86); depth = 0.48f; bankHeight = 0.38f; bankWidth = 6; animationSpeed = 0.07f;
                basins = new[] { new StudyBasin(-32, 19, 16, 9, 0.2f), new StudyBasin(-9, 12, 15, 12, 0.2f),
                    new StudyBasin(10, -2, 21, 8, 0.22f), new StudyBasin(30, -17, 17, 11, 0.18f),
                    new StudyBasin(-27, -17, 9, 6, 0.22f), new StudyBasin(26, 22, 9, 6, 0.2f) };
                deepColor = new Color(0.41f, 0.52f, 0.46f); shallowColor = new Color(0.61f, 0.65f, 0.49f);
            }
            else if (kind == WaterStudyKind.Coast)
            {
                size = new Vector2(160, 120); depth = 9; bankHeight = 2.4f; bankWidth = 9; animationSpeed = 0.4f;
                basins = Array.Empty<StudyBasin>(); islands = new[] { new StudyBasin(38, 28, 9, 6, 0.2f), new StudyBasin(54, -24, 5, 3.5f, 0.18f) };
                deepColor = new Color(0.23f, 0.39f, 0.46f); shallowColor = new Color(0.52f, 0.65f, 0.62f);
            }
        }

        public bool Validate(out string reason)
        {
            if (!Enum.IsDefined(typeof(WaterStudyKind), kind)) return Fail("Unknown water category.", out reason);
            if (!Finite(size.x) || !Finite(size.y) || size.x < 40 || size.y < 40 || size.x > 300 || size.y > 300) return Fail("Invalid study size (40–300).", out reason);
            if (resolution < 48 || resolution > 240 || !In(depth, 0.2f, 12) || !In(bankHeight, 0.2f, 4) || !In(bankWidth, 1, 15)
                || !In(streamSlope, 0, 0.06f) || !In(animationSpeed, 0, 1)) return Fail("Invalid geometry or animation settings.", out reason);
            if (basins == null || islands == null || basins.Length > 16 || islands.Length > 16) return Fail("Invalid basin list.", out reason);
            if (kind != WaterStudyKind.Stream && kind != WaterStudyKind.Coast && basins.Length == 0) return Fail("Closed water requires a basin.", out reason);
            foreach (var basin in basins) if (!ValidBasin(basin)) return Fail("Invalid basin radius/position.", out reason);
            foreach (var basin in islands) if (!ValidBasin(basin)) return Fail("Invalid island radius/position.", out reason);
            if (kind == WaterStudyKind.Stream)
            {
                if (stream == null || stream.Length < 2 || stream.Length > 32) return Fail("Stream requires 2–32 stations.", out reason);
                for (int i = 0; i < stream.Length; i++)
                {
                    if (!Finite(stream[i].position.x) || !Finite(stream[i].position.y) || !In(stream[i].width, 1, 10)) return Fail("Invalid stream station.", out reason);
                    // This simple sloping fixture runs toward +X; reject upstream rises instead of faking hydrology.
                    if (i > 0 && stream[i].position.x - stream[i - 1].position.x < 4) return Fail("Stream stations must advance at least 4 units toward +X.", out reason);
                }
            }
            reason = ""; return true;
        }
        private static bool ValidBasin(StudyBasin b) => Finite(b.center.x) && Finite(b.center.y) && In(b.radius.x, 2, 100) && In(b.radius.y, 2, 100) && In(b.irregularity, 0, 0.25f);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool In(float v, float min, float max) => Finite(v) && v >= min && v <= max;
        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
