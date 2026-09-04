using System;
using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map.Mountain
{
    [Serializable]
    public sealed class MountainPeak
    {
        public string label;
        public Vector2 center;
        public Vector2 radius = new Vector2(16, 22);
        [Min(1)] public float height = 40;
        public float heading;
        [Range(0, 1)] public float folds = 0.35f;
        public MountainPeak(string label, float x, float z, float h, float rx, float rz, float heading = 0)
        { this.label = label; center = new Vector2(x, z); height = h; radius = new Vector2(rx, rz); this.heading = heading; }
    }

    [Serializable]
    public sealed class MountainRidge
    {
        public string label;
        // x,z in map space; y is an explicit absolute ridge height, not a sampled image value.
        public Vector3[] points;
        [Min(1)] public float width = 8;
        public MountainRidge(string label, float width, params Vector3[] points)
        { this.label = label; this.width = width; this.points = points; }
    }

    [Serializable]
    public sealed class MountainValley
    {
        public Vector2 start, end;
        [Min(1)] public float width = 8;
        [Min(0)] public float depth = 6;
        public MountainValley(Vector2 start, Vector2 end, float width, float depth)
        { this.start = start; this.end = end; this.width = width; this.depth = depth; }
    }

    /// <summary>Look-development input only. This is not a world Definition, terrain enum, or save-game contract.</summary>
    [CreateAssetMenu(menuName = "Project Realm/Debug/Mountain Lookdev Profile")]
    public sealed class MountainLookdevProfile : ScriptableObject
    {
        [Header("Approved visual reference — never a surface or height map")]
        public string referenceVersion = "OriginalReferenceV1";
        public string referencePath = "docs/03_美术风格/02_地图设计稿/01_地形/高山.png";
        public string referenceSha256 = "8b49fbff7c3de999f46562bd70b960be80a99e250f99410210c666e124871d4f";
        public string visualStatus = "NeedsRevision";

        [Header("Editable height field (explicit rebuild only)")]
        public int seed = 1628;
        public Vector2 size = new Vector2(200, 220);
        [Range(32, 384)] public int cells = 240;
        [Range(0, 3)] public float rockRelief = 0.9f;
        public MountainPeak[] peaks =
        {
            new MountainPeak("主峰 / rear crown", -5, 39, 66, 18, 25, -12),
            new MountainPeak("前峰 / descending spine", 0, 1, 53, 18, 28, -16),
            new MountainPeak("前脊肩", 5, -31, 36, 15, 22, 12),
            new MountainPeak("近景前峰", -4, -66, 29, 14, 19, -10),
            new MountainPeak("西侧峰", -47, -13, 37, 14, 19, 18),
            new MountainPeak("西侧后峰", -52, 42, 30, 16, 20, -22),
            new MountainPeak("西近景支峰", -70, -69, 28, 19, 22, -15),
            new MountainPeak("西中景支峰", -74, 1, 18, 16, 22, 28),
            new MountainPeak("东侧峰", 44, 9, 35, 16, 20, 24),
            new MountainPeak("东侧后峰", 34, 52, 42, 14, 22, -17),
            new MountainPeak("东前峰", 43, -46, 28, 15, 23, -8),
            new MountainPeak("东近景支峰", 65, -81, 18, 20, 19, 18),
            new MountainPeak("东中景支峰", 78, 27, 22, 15, 20, 30),
            new MountainPeak("远山西", -51, 86, 17, 26, 13, 8),
            new MountainPeak("远山中", 3, 92, 14, 25, 15, -25),
            new MountainPeak("远山东", 59, 86, 19, 23, 14, 18)
        };
        public MountainRidge[] ridges =
        {
            new MountainRidge("主山连续脊", 8, new Vector3(-5, 48, 36), new Vector3(-2, 30, 18), new Vector3(0, 41, 0), new Vector3(5, 21, -26), new Vector3(-3, 16, -62)),
            new MountainRidge("东支脊", 8, new Vector3(0, 36, 30), new Vector3(16, 18, 37), new Vector3(34, 27, 50)),
            new MountainRidge("东侧连续脊", 7, new Vector3(34, 28, 50), new Vector3(39, 15, 28), new Vector3(44, 23, 9), new Vector3(45, 13, -18), new Vector3(43, 19, -46)),
            new MountainRidge("西侧支脊", 9, new Vector3(-52, 20, 42), new Vector3(-44, 12, 16), new Vector3(-47, 24, -13), new Vector3(-59, 10, -40), new Vector3(-70, 16, -69)),
            new MountainRidge("前部低鞍", 7, new Vector3(-4, 16, -66), new Vector3(16, 7, -78), new Vector3(42, 11, -85), new Vector3(65, 12, -81))
        };
        public MountainValley[] valleys =
        {
            new MountainValley(new Vector2(-30, 75), new Vector2(-25, -82), 9, 5),
            new MountainValley(new Vector2(23, 20), new Vector2(23, -78), 7, 4)
        };

        [Header("Independent raster layers")]
        public Texture2D wash;
        public Texture2D strokes;
        public Texture2D paper;
        public Texture2D pine;
        public Color paperColor = new Color(0.91f, 0.88f, 0.80f, 1);
        public Color rockColor = new Color(0.71f, 0.685f, 0.595f, 1);
        public Color inkColor = new Color(0.20f, 0.22f, 0.20f, 1);
        public Color mossColor = new Color(0.43f, 0.455f, 0.35f, 1);
        [Min(1)] public float washTileSize = 34;
        [Min(1)] public float strokeTileSize = 38;
        [Range(0, 1)] public float washStrength = 0.55f;
        [Range(0, 1)] public float inkStrength = 0.64f;
        [Range(0, 0.15f)] public float paperStrength = 0.025f;
        [Range(0, 1)] public float depthWash = 0.65f;

        [Header("Independent set dressing")]
        [Range(0, 400)] public int treeClumps = 32;
        public Vector2 treeHeight = new Vector2(6.0f, 11.0f);
        public bool showTrees = false;
        public string foliageStatus = "BlockedInvalidAlpha";
        public bool showMist = true;
        [Range(0, 0.6f)] public float mistOpacity = 0.16f;

        [Header("Locked heading camera")]
        public Vector3 defaultFocus = new Vector3(0, 10, 3);
        [Min(1)] public float defaultZoom = 82;
        [Min(1)] public float minZoom = 24;
        [Min(1)] public float maxZoom = 115;
        [Range(35, 75)] public float defaultPitch = 48;
        [Min(1)] public float cameraDistance = 340;

        public bool Validate(out string error, bool requireTextures = true)
        {
            error = null;
            if (!Finite(size.x) || !Finite(size.y) || size.x < 50 || size.y < 50 || cells < 32 || cells > 384)
                error = "Map size must be finite and >=50; cells must be 32..384.";
            else if (!Finite(rockRelief) || rockRelief < 0 || rockRelief > 3 || peaks == null || peaks.Length == 0)
                error = "Explicit mountain peaks and bounded relief are required.";
            else
            {
                foreach (var p in peaks)
                    if (p == null || !Finite(p.center) || !Finite(p.radius) || p.radius.x < 2 || p.radius.y < 2 || !Finite(p.height) || p.height < 1 || p.height > 150 || !Finite(p.heading) || !Finite(p.folds) || p.folds < 0 || p.folds > 1)
                    { error = "Every peak needs a finite position, radius>=2, height 1..150 and folds 0..1."; break; }
                if (error == null && ridges != null)
                    foreach (var r in ridges)
                    {
                        if (r == null || !Finite(r.width) || r.width < 1 || r.points == null || r.points.Length < 2)
                        { error = "Every ridge needs a width>=1 and at least two points."; break; }
                        foreach (var point in r.points)
                            if (!Finite(point) || point.y < 0 || point.y > 150) { error = "Invalid ridge point."; break; }
                    }
                if (error == null && valleys != null)
                    foreach (var v in valleys)
                        if (v == null || !Finite(v.start) || !Finite(v.end) || !Finite(v.width) || v.width < 1 || !Finite(v.depth) || v.depth < 0)
                        { error = "Invalid valley control."; break; }
            }
            if (error == null && (!Finite(defaultFocus) || !Finite(defaultZoom) || !Finite(minZoom) || !Finite(maxZoom) || minZoom < 1 || minZoom > defaultZoom || defaultZoom > maxZoom || !Finite(defaultPitch) || defaultPitch < 35 || defaultPitch > 75 || !Finite(cameraDistance) || cameraDistance < 180))
                error = "Invalid camera limits.";
            if (error == null && (!Finite(washTileSize) || washTileSize < 1 || !Finite(strokeTileSize) || strokeTileSize < 1 || !Unit(washStrength) || !Unit(inkStrength) || !Unit(depthWash) || !Finite(paperStrength) || paperStrength < 0 || paperStrength > 0.15f || !Unit(mistOpacity) || !Finite(treeHeight) || treeHeight.x <= 0 || treeHeight.y < treeHeight.x || treeClumps < 0 || treeClumps > 400 || !Finite(paperColor) || !Finite(rockColor) || !Finite(inkColor) || !Finite(mossColor)))
                error = "Invalid material or set-dressing values.";
            if (error == null && requireTextures && (wash == null || strokes == null || paper == null))
                error = "The three independent surface sources are required. Never substitute the full concept painting.";
            if (error == null && requireTextures && showTrees && pine == null)
                error = "Trees cannot be enabled without a valid transparent pine source.";
            return error == null;
        }

        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Color v) => Finite(v.r) && Finite(v.g) && Finite(v.b) && Finite(v.a);
        private static bool Unit(float v) => Finite(v) && v >= 0 && v <= 1;
    }
}
