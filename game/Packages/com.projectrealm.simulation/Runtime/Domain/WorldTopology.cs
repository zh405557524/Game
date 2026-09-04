using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.Domain
{
    /// <summary>模拟节点的语义类型；地理、势力和组织关系由不同图表达。</summary>
    public enum SimulationNodeKind
    {
        World,
        Region,
        County,
        LocalDivision,
        Settlement,
        Faction,
        Organization
    }

    /// <summary>所有拓扑节点共享的稳定标识和可审计元数据。</summary>
    public class SimulationNode
    {
        public SimulationNode(
            StableId nodeId,
            SimulationNodeKind kind,
            string displayName,
            StableId? geographicParentId = null,
            bool historicalClaim = false)
        {
            RequireId(nodeId, nameof(nodeId));
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A simulation node requires a display name.", nameof(displayName));
            }

            NodeId = nodeId;
            Kind = kind;
            DisplayName = displayName;
            GeographicParentId = geographicParentId;
            HistoricalClaim = historicalClaim;
        }

        public StableId NodeId { get; }

        public SimulationNodeKind Kind { get; }

        public string DisplayName { get; }

        public StableId? GeographicParentId { get; }

        public bool HistoricalClaim { get; }

        public static void RequireId(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("A stable ID is required.", parameterName);
            }
        }
    }

    /// <summary>地理树中的世界、区域、县、基层区划或聚落节点。</summary>
    public sealed class RegionNode : SimulationNode
    {
        public RegionNode(
            StableId nodeId,
            SimulationNodeKind kind,
            string displayName,
            StableId? geographicParentId = null,
            bool historicalClaim = false)
            : base(nodeId, kind, displayName, geographicParentId, historicalClaim)
        {
            if (kind != SimulationNodeKind.World &&
                kind != SimulationNodeKind.Region &&
                kind != SimulationNodeKind.County &&
                kind != SimulationNodeKind.LocalDivision &&
                kind != SimulationNodeKind.Settlement)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), "A region node must be geographic.");
            }
        }
    }

    /// <summary>势力关系图中的政治或组织主体。</summary>
    public sealed class FactionNode : SimulationNode
    {
        public FactionNode(StableId nodeId, string displayName, bool historicalClaim = false)
            : base(nodeId, SimulationNodeKind.Faction, displayName, null, historicalClaim)
        {
        }
    }

    /// <summary>按父子关系组织的地理树，构造时拒绝缺失父节点和循环。</summary>
    public sealed class GeographicTree
    {
        private readonly Dictionary<StableId, RegionNode> _nodes;

        public GeographicTree(IEnumerable<RegionNode> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            _nodes = new Dictionary<StableId, RegionNode>();
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    throw new ArgumentException("A geographic tree cannot contain null nodes.", nameof(nodes));
                }

                if (_nodes.ContainsKey(node.NodeId))
                {
                    throw new InvalidOperationException($"Duplicate geographic node '{node.NodeId}'.");
                }

                _nodes.Add(node.NodeId, node);
            }

            ValidateParentsAndCycles();
            Nodes = new ReadOnlyCollection<RegionNode>(_nodes.Values.OrderBy(node => node.NodeId.Value, StringComparer.Ordinal).ToList());
        }

        public IReadOnlyList<RegionNode> Nodes { get; }

        public RegionNode GetRequired(StableId nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
            {
                throw new KeyNotFoundException($"Geographic node '{nodeId}' does not exist.");
            }

            return node;
        }

        public IReadOnlyList<RegionNode> GetChildren(StableId parentId)
        {
            return new ReadOnlyCollection<RegionNode>(_nodes.Values
                .Where(node => node.GeographicParentId.HasValue && node.GeographicParentId.Value.Equals(parentId))
                .OrderBy(node => node.NodeId.Value, StringComparer.Ordinal)
                .ToList());
        }

        private void ValidateParentsAndCycles()
        {
            foreach (var node in _nodes.Values)
            {
                if (node.GeographicParentId.HasValue && !_nodes.ContainsKey(node.GeographicParentId.Value))
                {
                    throw new InvalidOperationException(
                        $"Geographic node '{node.NodeId}' refers to missing parent '{node.GeographicParentId.Value}'.");
                }

                var visited = new HashSet<StableId>();
                var current = node;
                while (current.GeographicParentId.HasValue)
                {
                    if (!visited.Add(current.NodeId))
                    {
                        throw new InvalidOperationException($"Geographic cycle detected at '{current.NodeId}'.");
                    }

                    current = _nodes[current.GeographicParentId.Value];
                }
            }
        }
    }

    /// <summary>两个势力之间的有向关系。</summary>
    public sealed class FactionRelation
    {
        public FactionRelation(StableId fromFactionId, StableId toFactionId, string relationKind)
        {
            SimulationNode.RequireId(fromFactionId, nameof(fromFactionId));
            SimulationNode.RequireId(toFactionId, nameof(toFactionId));
            if (string.IsNullOrWhiteSpace(relationKind))
            {
                throw new ArgumentException("A faction relation requires a kind.", nameof(relationKind));
            }

            FromFactionId = fromFactionId;
            ToFactionId = toFactionId;
            RelationKind = relationKind;
        }

        public StableId FromFactionId { get; }

        public StableId ToFactionId { get; }

        public string RelationKind { get; }
    }

    /// <summary>势力节点与有向关系图。</summary>
    public sealed class FactionGraph
    {
        public FactionGraph(IEnumerable<FactionNode> nodes, IEnumerable<FactionRelation> relations = null)
        {
            var nodeList = (nodes ?? throw new ArgumentNullException(nameof(nodes)))
                .OrderBy(node => node.NodeId.Value, StringComparer.Ordinal)
                .ToList();
            var ids = new HashSet<StableId>();
            foreach (var node in nodeList)
            {
                if (node == null || !ids.Add(node.NodeId))
                {
                    throw new InvalidOperationException("Faction nodes must be non-null and unique.");
                }
            }

            var relationList = (relations ?? Array.Empty<FactionRelation>())
                .OrderBy(relation => relation.FromFactionId.Value, StringComparer.Ordinal)
                .ThenBy(relation => relation.ToFactionId.Value, StringComparer.Ordinal)
                .ToList();
            foreach (var relation in relationList)
            {
                if (!ids.Contains(relation.FromFactionId) || !ids.Contains(relation.ToFactionId))
                {
                    throw new InvalidOperationException("Faction relations must reference registered factions.");
                }
            }

            Nodes = new ReadOnlyCollection<FactionNode>(nodeList);
            Relations = new ReadOnlyCollection<FactionRelation>(relationList);
        }

        public IReadOnlyList<FactionNode> Nodes { get; }

        public IReadOnlyList<FactionRelation> Relations { get; }
    }

    /// <summary>势力对地理区域的管辖关系，不与地理父子关系混用。</summary>
    public sealed class JurisdictionRelation
    {
        public JurisdictionRelation(
            StableId jurisdictionId,
            StableId factionId,
            StableId regionId,
            string authorityKind,
            bool historicalClaim = false)
        {
            SimulationNode.RequireId(jurisdictionId, nameof(jurisdictionId));
            SimulationNode.RequireId(factionId, nameof(factionId));
            SimulationNode.RequireId(regionId, nameof(regionId));
            if (string.IsNullOrWhiteSpace(authorityKind))
            {
                throw new ArgumentException("A jurisdiction relation requires an authority kind.", nameof(authorityKind));
            }

            JurisdictionId = jurisdictionId;
            FactionId = factionId;
            RegionId = regionId;
            AuthorityKind = authorityKind;
            HistoricalClaim = historicalClaim;
        }

        public StableId JurisdictionId { get; }

        public StableId FactionId { get; }

        public StableId RegionId { get; }

        public string AuthorityKind { get; }

        public bool HistoricalClaim { get; }
    }

    /// <summary>经过唯一性校验的管辖关系集合。</summary>
    public sealed class JurisdictionGraph
    {
        public JurisdictionGraph(IEnumerable<JurisdictionRelation> relations)
        {
            if (relations == null)
            {
                throw new ArgumentNullException(nameof(relations));
            }

            var ids = new HashSet<StableId>();
            var ordered = relations.OrderBy(relation => relation.JurisdictionId.Value, StringComparer.Ordinal).ToList();
            foreach (var relation in ordered)
            {
                if (relation == null || !ids.Add(relation.JurisdictionId))
                {
                    throw new InvalidOperationException("Jurisdiction relations must be non-null and uniquely identified.");
                }
            }

            Relations = new ReadOnlyCollection<JurisdictionRelation>(ordered);
        }

        public IReadOnlyList<JurisdictionRelation> Relations { get; }
    }

    /// <summary>聚落或设施的所有权关系。</summary>
    public sealed class SettlementOwner
    {
        public SettlementOwner(StableId settlementId, StableId ownerId, string ownershipKind)
        {
            SimulationNode.RequireId(settlementId, nameof(settlementId));
            SimulationNode.RequireId(ownerId, nameof(ownerId));
            if (string.IsNullOrWhiteSpace(ownershipKind))
            {
                throw new ArgumentException("Settlement ownership requires a kind.", nameof(ownershipKind));
            }

            SettlementId = settlementId;
            OwnerId = ownerId;
            OwnershipKind = ownershipKind;
        }

        public StableId SettlementId { get; }

        public StableId OwnerId { get; }

        public string OwnershipKind { get; }
    }

    /// <summary>地理树、势力图、管辖图和聚落所有权的聚合根。</summary>
    public sealed class WorldTopology
    {
        public WorldTopology(
            GeographicTree geography,
            FactionGraph factions,
            JurisdictionGraph jurisdictions,
            IEnumerable<SettlementOwner> settlementOwners = null)
        {
            Geography = geography ?? throw new ArgumentNullException(nameof(geography));
            Factions = factions ?? throw new ArgumentNullException(nameof(factions));
            Jurisdictions = jurisdictions ?? throw new ArgumentNullException(nameof(jurisdictions));
            SettlementOwners = new ReadOnlyCollection<SettlementOwner>((settlementOwners ?? Array.Empty<SettlementOwner>())
                .OrderBy(owner => owner.SettlementId.Value, StringComparer.Ordinal)
                .ToList());
        }

        public GeographicTree Geography { get; }

        public FactionGraph Factions { get; }

        public JurisdictionGraph Jurisdictions { get; }

        public IReadOnlyList<SettlementOwner> SettlementOwners { get; }
    }
}
