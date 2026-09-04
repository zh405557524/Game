using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map
{
    public sealed class TerrainDebugSettings : ScriptableObject
    {
        public LandformKind kind;
        public FiveTerrainDefinition geometry;
        public Texture2D paintedTexture;
        public Texture2D microTexture;
        [Range(10,160)] public float textureWorldSize = 40;
        [Range(30,75)] public float pitch = 48;
        [Range(15,100)] public float cameraSize = 68;
    }
}
