using UnityEngine;

namespace ProjectRealm.Presentation.Map.Water
{
    // Presentation-only test input. Never a production Definition or save-game record.
    public sealed class LakeLookdevProfile : ScriptableObject
    {
        public WaterBodyStudyDefinition baseline;
        public Texture2D waterColor;
        public Texture2D shoreSediment;
        [Min(1)] public float waterTileSize = 48;
        [Min(1)] public float shoreTileSize = 14;
        [Min(0.1f)] public float shoreWidth = 1.8f;
        public Color landColor = new Color(0.79f, 0.80f, 0.69f);
        [TextArea] public string reviewStatus = "Candidate — technical checks do not imply art approval.";

        public bool Validate(out string reason)
        {
            reason = "";
            if (baseline == null || baseline.kind != WaterStudyKind.Lake || !baseline.Validate(out reason))
            { reason = "A valid lake baseline is required. " + reason; return false; }
            if (waterColor == null || shoreSediment == null) { reason = "Both source images are required."; return false; }
            if (!Positive(waterTileSize) || !Positive(shoreTileSize) || !Positive(shoreWidth))
            { reason = "Texture scales and shore width must be finite and positive."; return false; }
            for (int channel = 0; channel < 4; channel++)
                if (float.IsNaN(landColor[channel]) || float.IsInfinity(landColor[channel]))
                { reason = "Land ink color must be finite."; return false; }
            return true;
        }
        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
