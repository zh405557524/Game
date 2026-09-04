using System;
using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map.Water
{
    [Serializable]
    public struct RiverStation
    {
        public Vector2 position;
        [Min(1)] public float width;
        public RiverStation(float x, float z, float width) { position = new Vector2(x, z); this.width = width; }
    }

    // Presentation-only study input, not the authoritative river graph or a save-game record.
    [CreateAssetMenu(menuName = "Project Realm/Map/Water/River Study")]
    public sealed class RiverStudyDefinition : ScriptableObject
    {
        public string caseId = "water/01_River/meander-v1";
        public int seed = 1628;
        public Vector2 groundSize = new Vector2(184, 126);
        [Range(8, 64)] public int samplesPerSpan = 28;
        [Range(0, 2)] public float flowSpeed = 0.9f;
        [Range(10, 80)] public float textureLength = 34;
        public Texture2D waterTexture;
        public Texture2D groundTexture;
        public RiverStation[] stations =
        {
            new RiverStation(-104, 35, 9), new RiverStation(-67, 32, 12),
            new RiverStation(-31, 7, 16), new RiverStation(0, 14, 18),
            new RiverStation(30, -4, 15), new RiverStation(46, -32, 21),
            new RiverStation(104, -39, 23)
        };

        public bool Validate(out string reason)
        {
            if (stations == null || stations.Length < 2 || stations.Length > 64) { reason = "Use 2–64 upstream-to-downstream stations."; return false; }
            if (!Finite(groundSize.x) || !Finite(groundSize.y) || groundSize.x < 40 || groundSize.y < 40 || groundSize.x > 500 || groundSize.y > 500)
            { reason = "Ground dimensions must be finite and between 40 and 500."; return false; }
            if (samplesPerSpan < 8 || samplesPerSpan > 64 || !Finite(flowSpeed) || flowSpeed < 0 || flowSpeed > 2 || !Finite(textureLength) || textureLength < 10 || textureLength > 80)
            { reason = "Invalid resolution, visual flow speed or texture scale."; return false; }
            for (int i = 0; i < stations.Length; i++)
            {
                var s = stations[i];
                if (!Finite(s.position.x) || !Finite(s.position.y) || !Finite(s.width) || s.width < 1 || s.width > 36)
                { reason = $"Station {i}: position/width invalid (width 1–36)."; return false; }
                if (i > 0 && Vector2.Distance(stations[i - 1].position, s.position) < 4)
                { reason = $"Station {i}: adjacent stations must be at least 4 units apart."; return false; }
            }
            reason = ""; return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
