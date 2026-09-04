using ProjectRealm.Foundation;
using System;
using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map
{
    public enum TerrainRegionKind
    {
        CountyGround,
        Plain,
        Hills,
        Basin
    }

    public enum MapLineKind
    {
        MainRiver,
        Tributary,
        Road,
        CountyBoundary,
        TownshipBoundary,
        VillageBoundary
    }

    public enum SettlementKind
    {
        Village,
        Town,
        CountySeat
    }

    [Serializable]
    public sealed class TerrainRegionDefinition
    {
        [SerializeField] private string displayName;
        [SerializeField] private TerrainRegionKind kind;
        [SerializeField] private Vector2[] polygon = Array.Empty<Vector2>();

        public string DisplayName => displayName;
        public TerrainRegionKind Kind => kind;
        public Vector2[] Polygon => polygon;

        public TerrainRegionDefinition(string displayName, TerrainRegionKind kind, Vector2[] polygon)
        {
            this.displayName = displayName;
            this.kind = kind;
            this.polygon = polygon;
        }
    }

    [Serializable]
    public sealed class MountainDefinition
    {
        [SerializeField] private Vector2 position;
        [SerializeField, Min(0.1f)] private float radius;
        [SerializeField, Min(0.1f)] private float height;

        public Vector2 Position => position;
        public float Radius => radius;
        public float Height => height;

        public MountainDefinition(Vector2 position, float radius, float height)
        {
            this.position = position;
            this.radius = radius;
            this.height = height;
        }
    }

    [Serializable]
    public sealed class MapLineDefinition
    {
        [SerializeField] private string displayName;
        [SerializeField] private MapLineKind kind;
        [SerializeField] private bool closed;
        [SerializeField] private Vector2[] points = Array.Empty<Vector2>();

        public string DisplayName => displayName;
        public MapLineKind Kind => kind;
        public bool Closed => closed;
        public Vector2[] Points => points;

        public MapLineDefinition(string displayName, MapLineKind kind, bool closed, Vector2[] points)
        {
            this.displayName = displayName;
            this.kind = kind;
            this.closed = closed;
            this.points = points;
        }
    }

    [Serializable]
    public sealed class SettlementDefinition
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private SettlementKind kind;
        [SerializeField] private Vector2 position;

        public string StableId => stableId;
        public string DisplayName => displayName;
        public SettlementKind Kind => kind;
        public Vector2 Position => position;

        public SettlementDefinition(string stableId, string displayName, SettlementKind kind, Vector2 position)
        {
            this.stableId = stableId;
            this.displayName = displayName;
            this.kind = kind;
            this.position = position;
        }
    }

    [CreateAssetMenu(fileName = "CountyMapDefinition", menuName = "Project Realm/Map/County Map Definition")]
    public sealed class CountyMapDefinition : ScriptableObject
    {
        [SerializeField] private string countyName = "南江桥县地图样板";
        [SerializeField] private Vector2 size = new Vector2(80f, 54f);
        [SerializeField] private TerrainRegionDefinition[] terrainRegions = Array.Empty<TerrainRegionDefinition>();
        [SerializeField] private MountainDefinition[] mountains = Array.Empty<MountainDefinition>();
        [SerializeField] private MapLineDefinition[] lines = Array.Empty<MapLineDefinition>();
        [SerializeField] private SettlementDefinition[] settlements = Array.Empty<SettlementDefinition>();

        public string CountyName => countyName;
        public Vector2 Size => size;
        public TerrainRegionDefinition[] TerrainRegions => terrainRegions;
        public MountainDefinition[] Mountains => mountains;
        public MapLineDefinition[] Lines => lines;
        public SettlementDefinition[] Settlements => settlements;

        public void ResetToPrototype()
        {
            countyName = "南江桥县地图样板";
            size = new Vector2(80f, 54f);

            var countyOutline = new[]
            {
                new Vector2(-38f, -11f), new Vector2(-31f, -22f), new Vector2(-12f, -27f),
                new Vector2(10f, -25f), new Vector2(29f, -19f), new Vector2(39f, -4f),
                new Vector2(36f, 13f), new Vector2(22f, 24f), new Vector2(2f, 27f),
                new Vector2(-22f, 23f), new Vector2(-37f, 11f)
            };

            terrainRegions = new[]
            {
                new TerrainRegionDefinition("县域底形", TerrainRegionKind.CountyGround, countyOutline),
                new TerrainRegionDefinition("中部平原", TerrainRegionKind.Plain, new[]
                {
                    new Vector2(-24f, -13f), new Vector2(4f, -20f), new Vector2(27f, -11f),
                    new Vector2(25f, 8f), new Vector2(5f, 16f), new Vector2(-22f, 10f)
                }),
                new TerrainRegionDefinition("西北丘陵", TerrainRegionKind.Hills, new[]
                {
                    new Vector2(-36f, 1f), new Vector2(-24f, -16f), new Vector2(-7f, -10f),
                    new Vector2(-3f, 9f), new Vector2(-21f, 23f), new Vector2(-34f, 12f)
                }),
                new TerrainRegionDefinition("东南盆地", TerrainRegionKind.Basin, new[]
                {
                    new Vector2(5f, -20f), new Vector2(27f, -15f), new Vector2(35f, -4f),
                    new Vector2(26f, 7f), new Vector2(9f, 4f)
                })
            };

            mountains = new[]
            {
                new MountainDefinition(new Vector2(-30f, 13f), 3.2f, 5.8f),
                new MountainDefinition(new Vector2(-25f, 18f), 3.7f, 7.2f),
                new MountainDefinition(new Vector2(-18f, 17f), 3.0f, 5.4f),
                new MountainDefinition(new Vector2(-13f, 22f), 3.6f, 6.7f),
                new MountainDefinition(new Vector2(-8f, 17f), 2.6f, 4.8f),
                new MountainDefinition(new Vector2(10f, 21f), 3.4f, 6.2f),
                new MountainDefinition(new Vector2(17f, 20f), 4.0f, 7.6f),
                new MountainDefinition(new Vector2(23f, 16f), 3.3f, 5.9f),
                new MountainDefinition(new Vector2(29f, 11f), 2.7f, 4.7f)
            };

            lines = new[]
            {
                new MapLineDefinition("南江", MapLineKind.MainRiver, false, new[]
                {
                    new Vector2(-34f, 8f), new Vector2(-22f, 5f), new Vector2(-10f, 2f),
                    new Vector2(1f, -2f), new Vector2(13f, -4f), new Vector2(25f, -9f),
                    new Vector2(36f, -8f)
                }),
                new MapLineDefinition("北溪", MapLineKind.Tributary, false, new[]
                {
                    new Vector2(-12f, 23f), new Vector2(-9f, 15f), new Vector2(-10f, 8f),
                    new Vector2(-10f, 2f)
                }),
                new MapLineDefinition("东溪", MapLineKind.Tributary, false, new[]
                {
                    new Vector2(24f, 18f), new Vector2(20f, 11f), new Vector2(18f, 3f),
                    new Vector2(13f, -4f)
                }),
                new MapLineDefinition("县城官道", MapLineKind.Road, false, new[]
                {
                    new Vector2(-31f, -8f), new Vector2(-18f, -5f), new Vector2(-5f, -7f),
                    new Vector2(6f, -4f), new Vector2(18f, 3f), new Vector2(30f, 7f)
                }),
                new MapLineDefinition("南北驿道", MapLineKind.Road, false, new[]
                {
                    new Vector2(-5f, -22f), new Vector2(-3f, -13f), new Vector2(-5f, -7f),
                    new Vector2(-2f, 3f), new Vector2(4f, 13f), new Vector2(10f, 21f)
                }),
                new MapLineDefinition("县界", MapLineKind.CountyBoundary, true, countyOutline),
                new MapLineDefinition("西乡界", MapLineKind.TownshipBoundary, false, new[]
                {
                    new Vector2(-19f, -22f), new Vector2(-17f, -12f), new Vector2(-18f, -5f),
                    new Vector2(-12f, 4f), new Vector2(-9f, 15f), new Vector2(-8f, 24f)
                }),
                new MapLineDefinition("东乡界", MapLineKind.TownshipBoundary, false, new[]
                {
                    new Vector2(8f, -24f), new Vector2(7f, -13f), new Vector2(9f, -3f),
                    new Vector2(14f, 7f), new Vector2(18f, 20f)
                }),
                new MapLineDefinition("西村界一", MapLineKind.VillageBoundary, false, new[]
                {
                    new Vector2(-34f, -4f), new Vector2(-25f, 0f), new Vector2(-18f, 7f)
                }),
                new MapLineDefinition("中村界一", MapLineKind.VillageBoundary, false, new[]
                {
                    new Vector2(-14f, -16f), new Vector2(-4f, -12f), new Vector2(7f, -13f)
                }),
                new MapLineDefinition("中村界二", MapLineKind.VillageBoundary, false, new[]
                {
                    new Vector2(-10f, 8f), new Vector2(0f, 7f), new Vector2(14f, 7f)
                }),
                new MapLineDefinition("东村界一", MapLineKind.VillageBoundary, false, new[]
                {
                    new Vector2(10f, -15f), new Vector2(21f, -12f), new Vector2(31f, -5f)
                })
            };

            settlements = new[]
            {
                new SettlementDefinition("SETTLEMENT_NANJIANG_COUNTY", "南江桥县城", SettlementKind.CountySeat, new Vector2(-5f, -7f)),
                new SettlementDefinition("SETTLEMENT_XIQIAO_TOWN", "西桥镇", SettlementKind.Town, new Vector2(-25f, -2f)),
                new SettlementDefinition("SETTLEMENT_DONGPU_TOWN", "东浦镇", SettlementKind.Town, new Vector2(20f, 4f)),
                new SettlementDefinition("SETTLEMENT_QINGHE_VILLAGE", "清河村", SettlementKind.Village, new Vector2(-30f, -9f)),
                new SettlementDefinition("SETTLEMENT_SHILIN_VILLAGE", "石林村", SettlementKind.Village, new Vector2(-18f, 9f)),
                new SettlementDefinition("SETTLEMENT_NANJIANGQIAO_VILLAGE", "南江桥村", SettlementKind.Village, new Vector2(-1f, -1f)),
                new SettlementDefinition("SETTLEMENT_HEWAN_VILLAGE", "河湾村", SettlementKind.Village, new Vector2(10f, -8f)),
                new SettlementDefinition("SETTLEMENT_DONGSHAN_VILLAGE", "东山村", SettlementKind.Village, new Vector2(27f, 10f)),
                new SettlementDefinition("SETTLEMENT_GUQIAO_VILLAGE", "古桥村", SettlementKind.Village, new Vector2(3f, 12f)),
                new SettlementDefinition("SETTLEMENT_NANPU_VILLAGE", "南浦村", SettlementKind.Village, new Vector2(17f, -15f))
            };
        }

        public bool HasRequiredPrototypeLayers(out string reason)
        {
            if (terrainRegions == null || terrainRegions.Length < 4)
            {
                reason = "Prototype needs county ground, plain, hills and basin terrain regions.";
                return false;
            }

            if (settlements == null || settlements.Length != 10)
            {
                reason = "Prototype needs exactly 10 settlement nodes.";
                return false;
            }

            var hasMainRiver = false;
            var hasRoad = false;
            var hasCountyBoundary = false;
            var hasTownshipBoundary = false;
            var hasVillageBoundary = false;

            if (lines != null)
            {
                foreach (var line in lines)
                {
                    if (line == null)
                    {
                        continue;
                    }

                    hasMainRiver |= line.Kind == MapLineKind.MainRiver;
                    hasRoad |= line.Kind == MapLineKind.Road;
                    hasCountyBoundary |= line.Kind == MapLineKind.CountyBoundary;
                    hasTownshipBoundary |= line.Kind == MapLineKind.TownshipBoundary;
                    hasVillageBoundary |= line.Kind == MapLineKind.VillageBoundary;
                }
            }

            if (!hasMainRiver || !hasRoad || !hasCountyBoundary || !hasTownshipBoundary || !hasVillageBoundary)
            {
                reason = "Prototype needs water, road and county/township/village boundary layers.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
