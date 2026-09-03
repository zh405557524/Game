using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Ports;
using SQLite;

namespace ProjectRealm.Infrastructure.Sqlite
{
    public sealed class SqliteWorldDefinitionStore : IWorldDefinitionStore
    {
        private readonly SQLiteAsset _definitionAsset;

        public SqliteWorldDefinitionStore(SQLiteAsset definitionAsset)
        {
            _definitionAsset = definitionAsset ?? throw new ArgumentNullException(nameof(definitionAsset));
        }

        public bool ContainsWorld(StableId worldId)
        {
            using (var connection = Open())
            {
                return connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM definition_manifest WHERE world_id=?", worldId.Value) == 1;
            }
        }

        public WorldDefinition LoadWorld(StableId worldId)
        {
            using (var connection = Open())
            {
                var manifestRow = connection.Query<DefinitionManifestRow>(
                    "SELECT * FROM definition_manifest WHERE world_id=?", worldId.Value).SingleOrDefault();
                if (manifestRow == null)
                {
                    throw new KeyNotFoundException($"Definition world '{worldId}' is unavailable.");
                }

                if (connection.ExecuteScalar<int>("PRAGMA user_version") != 1)
                {
                    throw new InvalidOperationException("Unsupported Definition database schema version.");
                }

                ValidateModuleCatalog(connection);

                var nodes = connection.Query<SimulationNodeRow>(
                        "SELECT * FROM simulation_node ORDER BY node_id")
                    .Select(ToRegionNode)
                    .ToList();
                var factions = connection.Query<FactionNodeRow>(
                        "SELECT * FROM faction_node ORDER BY faction_id")
                    .Select(row => new FactionNode(new StableId(row.FactionId), row.DisplayName, IsYes(row.HistoricalClaim)))
                    .ToList();
                var jurisdictions = connection.Query<JurisdictionRow>(
                        "SELECT * FROM jurisdiction_relation ORDER BY jurisdiction_id")
                    .Select(row => new JurisdictionRelation(
                        new StableId(row.JurisdictionId),
                        new StableId(row.FactionId),
                        new StableId(row.RegionId),
                        row.AuthorityKind,
                        IsYes(row.HistoricalClaim)))
                    .ToList();
                var owners = connection.Query<SettlementOwnerRow>(
                        "SELECT * FROM settlement_owner ORDER BY settlement_id")
                    .Select(row => new SettlementOwner(
                        new StableId(row.SettlementId),
                        new StableId(row.OwnerId),
                        row.OwnershipKind))
                    .ToList();
                var compositions = connection.Query<ModuleCompositionRow>(
                        "SELECT node_id, definition_id FROM node_module_composition ORDER BY node_id, definition_id")
                    .Select(row => new NodeModuleComposition(new StableId(row.NodeId), new StableId(row.DefinitionId)))
                    .ToList();

                var manifest = new RulesetManifest(
                    manifestRow.RulesetVersion,
                    manifestRow.ModuleCatalogVersion,
                    manifestRow.StateSchemaVersion,
                    manifestRow.ContentHash,
                    manifestRow.InitializationAlgorithmVersion,
                    manifestRow.RandomAlgorithmVersion,
                    IsYes(manifestRow.CommercialReleaseReady));
                var topology = new WorldTopology(
                    new GeographicTree(nodes),
                    new FactionGraph(factions),
                    new JurisdictionGraph(jurisdictions),
                    owners);
                return new WorldDefinition(worldId, manifest, topology, compositions);
            }
        }

        private SQLiteConnection Open()
        {
            var connection = _definitionAsset.CreateConnection();
            connection.Execute("PRAGMA query_only=ON");
            connection.Execute("PRAGMA trusted_schema=OFF");
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.BusyTimeout = TimeSpan.FromSeconds(5);
            return connection;
        }

        private static void ValidateModuleCatalog(SQLiteConnection connection)
        {
            var expected = FrameworkModuleCatalog.Create();
            var stored = connection.Query<StoredModuleDefinitionRow>(
                "SELECT definition_id, source_name, implementation_tier FROM module_definition ORDER BY definition_id");
            if (stored.Count != expected.Definitions.Count)
            {
                throw new InvalidOperationException(
                    $"Definition module catalog contains {stored.Count} entries; expected {expected.Definitions.Count}.");
            }

            foreach (var row in stored)
            {
                var definition = expected.GetRequired(new StableId(row.DefinitionId));
                if (!string.Equals(row.SourceName, definition.SourceName, StringComparison.Ordinal) ||
                    !string.Equals(row.ImplementationTier, definition.ImplementationTier.ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Definition module catalog drift detected at '{row.DefinitionId}'.");
                }

                var stages = connection.QueryScalars<int>(
                    "SELECT stage FROM module_stage WHERE definition_id=? ORDER BY stage", row.DefinitionId);
                if (!stages.SequenceEqual(definition.Stages.Select(stage => (int)stage)))
                {
                    throw new InvalidOperationException($"Definition module stage drift detected at '{row.DefinitionId}'.");
                }
            }

            var authoritativeAliases = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM module_compatibility_alias WHERE authoritative_provider<>0");
            if (authoritativeAliases != 0)
            {
                throw new InvalidOperationException("Compatibility module aliases cannot be authoritative providers.");
            }
        }

        private static RegionNode ToRegionNode(SimulationNodeRow row)
        {
            if (!Enum.TryParse(row.NodeKind, true, out SimulationNodeKind kind))
            {
                throw new InvalidOperationException($"Unknown simulation node kind '{row.NodeKind}'.");
            }

            StableId? parentId = string.IsNullOrEmpty(row.GeographicParentId)
                ? (StableId?)null
                : new StableId(row.GeographicParentId);
            return new RegionNode(new StableId(row.NodeId), kind, row.DisplayName, parentId, IsYes(row.HistoricalClaim));
        }

        private static bool IsYes(string value)
        {
            return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class DefinitionManifestRow
        {
            [Column("world_id")] public string WorldId { get; set; }
            [Column("ruleset_version")] public string RulesetVersion { get; set; }
            [Column("module_catalog_version")] public string ModuleCatalogVersion { get; set; }
            [Column("state_schema_version")] public string StateSchemaVersion { get; set; }
            [Column("content_hash")] public string ContentHash { get; set; }
            [Column("initialization_algorithm_version")] public string InitializationAlgorithmVersion { get; set; }
            [Column("random_algorithm_version")] public string RandomAlgorithmVersion { get; set; }
            [Column("commercial_release_ready")] public string CommercialReleaseReady { get; set; }
        }

        private sealed class SimulationNodeRow
        {
            [Column("node_id")] public string NodeId { get; set; }
            [Column("node_kind")] public string NodeKind { get; set; }
            [Column("display_name")] public string DisplayName { get; set; }
            [Column("geographic_parent_id")] public string GeographicParentId { get; set; }
            [Column("historical_claim")] public string HistoricalClaim { get; set; }
        }

        private sealed class FactionNodeRow
        {
            [Column("faction_id")] public string FactionId { get; set; }
            [Column("display_name")] public string DisplayName { get; set; }
            [Column("historical_claim")] public string HistoricalClaim { get; set; }
        }

        private sealed class JurisdictionRow
        {
            [Column("jurisdiction_id")] public string JurisdictionId { get; set; }
            [Column("faction_id")] public string FactionId { get; set; }
            [Column("region_id")] public string RegionId { get; set; }
            [Column("authority_kind")] public string AuthorityKind { get; set; }
            [Column("historical_claim")] public string HistoricalClaim { get; set; }
        }

        private sealed class SettlementOwnerRow
        {
            [Column("settlement_id")] public string SettlementId { get; set; }
            [Column("owner_id")] public string OwnerId { get; set; }
            [Column("ownership_kind")] public string OwnershipKind { get; set; }
        }

        private sealed class ModuleCompositionRow
        {
            [Column("node_id")] public string NodeId { get; set; }
            [Column("definition_id")] public string DefinitionId { get; set; }
        }

        private sealed class StoredModuleDefinitionRow
        {
            [Column("definition_id")] public string DefinitionId { get; set; }
            [Column("source_name")] public string SourceName { get; set; }
            [Column("implementation_tier")] public string ImplementationTier { get; set; }
        }
    }
}
