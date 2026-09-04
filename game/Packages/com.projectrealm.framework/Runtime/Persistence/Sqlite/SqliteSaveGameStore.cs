using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ProjectRealm.World;
using ProjectRealm.Framework;
using SQLite;

namespace ProjectRealm.Persistence.Sqlite
{
    /// <summary>
    /// 每个存档独立一个 SQLite 数据库。写入使用 WAL、FULL synchronous 和单事务快照；
    /// Definition 数据库始终与 Save 数据库分离。
    /// </summary>
    public sealed class SqliteSaveGameStore : ISaveGameStore
    {
        public const int SaveSchemaVersion = 1;

        private readonly string _saveRoot;

        public SqliteSaveGameStore(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException("A persistent data path is required.", nameof(persistentDataPath));
            }

            _saveRoot = Path.Combine(persistentDataPath, "ProjectRealm", "Saves");
        }

        /// <summary>判断指定存档文件是否存在。</summary>
        public bool Exists(StableId saveId)
        {
            return File.Exists(GetSavePath(saveId));
        }

        /// <summary>枚举存档目录名；不会打开数据库或改变任何存档。</summary>
        public IReadOnlyList<StableId> ListSaveIds()
        {
            if (!Directory.Exists(_saveRoot))
            {
                return Array.Empty<StableId>();
            }

            var ids = Directory.EnumerateDirectories(_saveRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new StableId(name))
                .ToList();
            return new ReadOnlyCollection<StableId>(ids);
        }

        /// <summary>在单个 SQLite 事务中写入完整闭合世界快照。</summary>
        public void Save(WorldSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            var path = GetSavePath(saveData.Manifest.SaveId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var connection = OpenWritable(path))
            {
                InitializeSchema(connection);
                // 任一表写入失败都会由 SQLite 回滚整个事务。
                connection.RunInTransaction(() => WriteSnapshot(connection, saveData));
            }
        }

        /// <summary>校验 schema 与 integrity_check 后读取存档。</summary>
        public WorldSaveData Load(StableId saveId)
        {
            var path = GetSavePath(saveId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Save '{saveId}' does not exist.", path);
            }

            using (var connection = OpenWritable(path))
            {
                EnsureSchemaVersion(connection);
                if (!string.Equals(connection.ExecuteScalar<string>("PRAGMA integrity_check"), "ok", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Save '{saveId}' failed SQLite integrity_check.");
                }

                return ReadSnapshot(connection, saveId);
            }
        }

        /// <summary>使用 SQLite backup API 创建迁移前副本，不直接复制打开中的 WAL 文件。</summary>
        public void BackupBeforeMigration(StableId saveId, string migrationId)
        {
            if (string.IsNullOrWhiteSpace(migrationId))
            {
                throw new ArgumentException("A migration ID is required.", nameof(migrationId));
            }

            var source = GetSavePath(saveId);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Save '{saveId}' does not exist.", source);
            }

            var safeMigrationId = SanitizePathSegment(migrationId);
            var backupDirectory = Path.Combine(Path.GetDirectoryName(source), "Backups");
            Directory.CreateDirectory(backupDirectory);
            var destination = Path.Combine(
                backupDirectory,
                $"save_{SanitizePathSegment(saveId.Value)}.before_{safeMigrationId}.sqlite");
            using (var connection = OpenWritable(source))
            {
                connection.Backup(destination);
            }
        }

        /// <summary>生成经过路径片段清理的独立存档路径。</summary>
        public string GetSavePath(StableId saveId)
        {
            SimulationNode.RequireId(saveId, nameof(saveId));
            var safeId = SanitizePathSegment(saveId.Value);
            return Path.Combine(_saveRoot, safeId, $"save_{safeId}.sqlite");
        }

        private static SQLiteConnection OpenWritable(string path)
        {
            var connection = new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            connection.BusyTimeout = TimeSpan.FromSeconds(5);
            connection.Execute("PRAGMA foreign_keys=ON");
            connection.Execute("PRAGMA trusted_schema=OFF");
            connection.Execute("PRAGMA synchronous=FULL");
            connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
            return connection;
        }

        private static void InitializeSchema(SQLiteConnection connection)
        {
            var version = connection.ExecuteScalar<int>("PRAGMA user_version");
            if (version != 0 && version != SaveSchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported save schema version '{version}'.");
            }

            connection.ExecuteScript(SchemaSql);
            connection.Execute($"PRAGMA user_version={SaveSchemaVersion}");
            connection.Execute(
                "INSERT OR IGNORE INTO schema_history(schema_version, migration_id) VALUES (?,?)",
                SaveSchemaVersion,
                "create-save-schema-v1");
        }

        private static void EnsureSchemaVersion(SQLiteConnection connection)
        {
            var version = connection.ExecuteScalar<int>("PRAGMA user_version");
            if (version != SaveSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Save schema version '{version}' requires an explicit backed-up migration to '{SaveSchemaVersion}'.");
            }
        }

        private static void WriteSnapshot(SQLiteConnection connection, WorldSaveData save)
        {
            foreach (var table in DeleteOrder)
            {
                connection.Execute("DELETE FROM " + table);
            }

            WriteClock(connection, save);
            WriteTopology(connection, save.Topology);
            WriteModulesAndState(connection, save);
            WriteCommandsAndEvents(connection, save);
            WriteResults(connection, save);
            WriteCheckpoints(connection, save);

            // manifest（含当前检查点指针）最后写入；事务外永远看不到半套新快照。
            var ruleset = save.Manifest.Ruleset;
            connection.Execute(
                "INSERT INTO save_manifest VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                save.Manifest.SaveId.Value,
                save.Manifest.WorldId.Value,
                save.WorldSeed.Value,
                ruleset.RulesetVersion,
                ruleset.ModuleCatalogVersion,
                ruleset.StateSchemaVersion,
                ruleset.DefinitionContentHash,
                ruleset.InitializationAlgorithmVersion,
                ruleset.RandomAlgorithmVersion,
                ruleset.CommercialReleaseReady ? 1 : 0,
                save.Manifest.CurrentCheckpointId.Value,
                save.Manifest.UpdatedUtc.ToString("O"),
                SaveSchemaVersion);
        }

        private static void WriteClock(SQLiteConnection connection, WorldSaveData save)
        {
            var clock = save.Clock;
            connection.Execute(
                "INSERT INTO world_clock VALUES (?,?,?,?,?,?,?,?,?,?)",
                save.Manifest.SaveId.Value,
                clock.DayIndex,
                clock.TickSequence,
                clock.EconomicYear,
                clock.Month,
                clock.Day,
                clock.CalendarDefinitionId.Value,
                clock.LastMonthCloseTick,
                clock.LastSeasonCloseTick,
                clock.LastYearCloseTick);
        }

        private static void WriteTopology(SQLiteConnection connection, WorldTopology topology)
        {
            foreach (var node in topology.Geography.Nodes)
            {
                connection.Execute(
                    "INSERT INTO node_instance VALUES (?,?,?,?,?)",
                    node.NodeId.Value,
                    (int)node.Kind,
                    node.DisplayName,
                    node.GeographicParentId?.Value ?? string.Empty,
                    node.HistoricalClaim ? 1 : 0);
            }

            foreach (var faction in topology.Factions.Nodes)
            {
                connection.Execute(
                    "INSERT INTO faction_node VALUES (?,?,?)",
                    faction.NodeId.Value,
                    faction.DisplayName,
                    faction.HistoricalClaim ? 1 : 0);
            }

            foreach (var relation in topology.Factions.Relations)
            {
                connection.Execute(
                    "INSERT INTO faction_relation VALUES (?,?,?)",
                    relation.FromFactionId.Value,
                    relation.ToFactionId.Value,
                    relation.RelationKind);
            }

            foreach (var relation in topology.Jurisdictions.Relations)
            {
                connection.Execute(
                    "INSERT INTO jurisdiction_relation VALUES (?,?,?,?,?)",
                    relation.JurisdictionId.Value,
                    relation.FactionId.Value,
                    relation.RegionId.Value,
                    relation.AuthorityKind,
                    relation.HistoricalClaim ? 1 : 0);
            }

            foreach (var owner in topology.SettlementOwners)
            {
                connection.Execute(
                    "INSERT INTO settlement_owner VALUES (?,?,?)",
                    owner.SettlementId.Value,
                    owner.OwnerId.Value,
                    owner.OwnershipKind);
            }
        }

        private static void WriteModulesAndState(SQLiteConnection connection, WorldSaveData save)
        {
            foreach (var instance in save.ModuleInstances)
            {
                connection.Execute(
                    "INSERT INTO module_instance VALUES (?,?,?,?)",
                    instance.InstanceId.Value,
                    instance.DefinitionId.Value,
                    instance.NodeId.Value,
                    (int)instance.LifecycleState);
            }

            foreach (var record in save.CommittedState.Records)
            {
                connection.Execute(
                    "INSERT INTO module_state_snapshot VALUES (?,?,?,?,?)",
                    record.Key,
                    (int)record.Category,
                    record.CodecId,
                    (int)record.DataQuality,
                    record.Payload);
            }
        }

        private static void WriteCommandsAndEvents(SQLiteConnection connection, WorldSaveData save)
        {
            foreach (var command in save.Commands)
            {
                var envelope = command.Envelope;
                connection.Execute(
                    "INSERT INTO command_record VALUES (?,?,?,?,?,?,?,?,?)",
                    envelope.CommandInstanceId.Value,
                    envelope.CommandDefinitionId.Value,
                    envelope.ActorId.Value,
                    envelope.TargetId.Value,
                    envelope.AuthorityScopeId.Value,
                    envelope.IdempotencyKey,
                    envelope.Payload,
                    envelope.SubmittedTick.Value,
                    (int)command.Status);
                connection.Execute(
                    "INSERT INTO idempotency_key VALUES (?,?,?)",
                    envelope.AuthorityScopeId.Value,
                    envelope.IdempotencyKey,
                    envelope.CommandInstanceId.Value);
                for (var sequence = 0; sequence < command.StatusEvents.Count; sequence++)
                {
                    var statusEvent = command.StatusEvents[sequence];
                    connection.Execute(
                        "INSERT INTO command_status_event VALUES (?,?,?,?,?,?,?)",
                        statusEvent.EventId.Value,
                        statusEvent.CommandInstanceId.Value,
                        (int)statusEvent.PreviousStatus,
                        (int)statusEvent.CurrentStatus,
                        statusEvent.TickId.Value,
                        statusEvent.ReasonCode,
                        sequence);
                }
            }

            foreach (var reservation in save.Reservations)
            {
                connection.Execute(
                    "INSERT INTO reservation VALUES (?,?,?,?,?,?)",
                    reservation.ReservationId.Value,
                    reservation.CommandInstanceId.Value,
                    reservation.AuthorityScopeId.Value,
                    reservation.ResourceKey,
                    reservation.Amount,
                    reservation.Committed ? 1 : 0);
            }

            foreach (var item in save.Events)
            {
                connection.Execute(
                    "INSERT INTO event_log VALUES (?,?,?,?,?)",
                    item.EventId.Value,
                    item.EventDefinitionId.Value,
                    item.SourceNodeId.Value,
                    item.CommittedTick.Value,
                    item.Payload);
            }
        }

        private static void WriteResults(SQLiteConnection connection, WorldSaveData save)
        {
            foreach (var result in save.ModuleResults)
            {
                connection.Execute(
                    "INSERT INTO module_result VALUES (?,?,?,?,?,?,?,?,?)",
                    result.TickId.Value,
                    result.ModuleInstanceId.Value,
                    result.NodeId.Value,
                    (int)result.Stage,
                    (int)result.ImplementationTier,
                    (int)result.DataQuality,
                    result.Succeeded ? 1 : 0,
                    result.ReasonCode,
                    result.Deltas.Count);
            }

            foreach (var result in save.NodePeriodResults)
            {
                connection.Execute(
                    "INSERT INTO node_period_result VALUES (?,?,?,?,?,?,?)",
                    result.TickId.Value,
                    result.NodeId.Value,
                    (int)result.PeriodCloseFlags,
                    (int)result.DataQuality,
                    result.ResidualLedger.LedgerId.Value,
                    string.Join("\u001e", result.ResidualLedger.ResidualKeys),
                    result.ModuleResults.Count);
            }

            foreach (var snapshot in save.NodeSnapshots)
            {
                connection.Execute(
                    "INSERT INTO node_snapshot VALUES (?,?,?,?,?)",
                    snapshot.TickId.Value,
                    snapshot.NodeId.Value,
                    snapshot.StateHash.Sha256,
                    (int)snapshot.DataQuality,
                    1);
            }
        }

        private static void WriteCheckpoints(SQLiteConnection connection, WorldSaveData save)
        {
            foreach (var checkpoint in save.Checkpoints)
            {
                connection.Execute(
                    "INSERT INTO checkpoint VALUES (?,?,?,?)",
                    checkpoint.CheckpointId.Value,
                    checkpoint.TickId.Value,
                    checkpoint.StateHash.Sha256,
                    checkpoint.CreatedUtc.ToString("O"));
            }

            var current = save.Manifest.CurrentCheckpointId.Value;
            foreach (var record in save.CommittedState.Records)
            {
                connection.Execute(
                    "INSERT INTO checkpoint_item VALUES (?,?,?,?,?,?)",
                    current,
                    record.Key,
                    (int)record.Category,
                    record.CodecId,
                    (int)record.DataQuality,
                    record.Payload);
            }
        }

        private static WorldSaveData ReadSnapshot(SQLiteConnection connection, StableId requestedSaveId)
        {
            var manifestRow = connection.Query<SaveManifestRow>("SELECT * FROM save_manifest").SingleOrDefault();
            if (manifestRow == null || !string.Equals(manifestRow.SaveId, requestedSaveId.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Save manifest for '{requestedSaveId}' is missing or mismatched.");
            }

            var clockRow = connection.Query<WorldClockRow>("SELECT * FROM world_clock").Single();
            var clock = new WorldClock(
                clockRow.DayIndex,
                clockRow.TickSequence,
                clockRow.EconomicYear,
                clockRow.Month,
                clockRow.Day,
                new StableId(clockRow.CalendarDefinitionId),
                clockRow.LastMonthCloseTick,
                clockRow.LastSeasonCloseTick,
                clockRow.LastYearCloseTick);
            var topology = ReadTopology(connection);
            var modules = connection.Query<ModuleInstanceRow>("SELECT * FROM module_instance ORDER BY instance_id")
                .Select(row => new ModuleInstance(
                    new StableId(row.InstanceId),
                    new StableId(row.DefinitionId),
                    new StableId(row.NodeId),
                    (ModuleLifecycleState)row.LifecycleState))
                .ToList();
            var state = new CommittedState(connection.Query<StateRow>("SELECT * FROM module_state_snapshot ORDER BY state_key")
                .Select(row => new StateRecord(
                    row.StateKey,
                    (StateCategory)row.StateCategory,
                    row.Payload ?? Array.Empty<byte>(),
                    row.CodecId,
                    (DataQuality)row.DataQuality)));
            var commands = ReadCommands(connection);
            var reservations = connection.Query<ReservationRow>("SELECT * FROM reservation ORDER BY reservation_id")
                .Select(row => new ResourceReservation(
                    new StableId(row.ReservationId),
                    new StableId(row.CommandInstanceId),
                    new StableId(row.AuthorityScopeId),
                    row.ResourceKey,
                    row.Amount,
                    row.Committed != 0))
                .ToList();
            var events = connection.Query<EventRow>("SELECT * FROM event_log ORDER BY committed_tick, event_id")
                .Select(row => new EventEnvelope(
                    new StableId(row.EventId),
                    new StableId(row.EventDefinitionId),
                    new StableId(row.SourceNodeId),
                    new TickId(row.CommittedTick),
                    row.Payload ?? Array.Empty<byte>()))
                .ToList();
            var moduleResults = ReadModuleResults(connection);
            var nodeResults = ReadNodeResults(connection, moduleResults);
            var snapshots = connection.Query<NodeSnapshotRow>("SELECT * FROM node_snapshot ORDER BY tick_id, node_id")
                .Select(row => new NodeSnapshot(
                    new StableId(row.NodeId),
                    new TickId(row.TickId),
                    new StateHash(row.StateHash),
                    (DataQuality)row.DataQuality))
                .ToList();
            var checkpoints = connection.Query<CheckpointRow>("SELECT * FROM checkpoint ORDER BY tick_id")
                .Select(row => new WorldCheckpoint(
                    new StableId(row.CheckpointId),
                    new TickId(row.TickId),
                    new StateHash(row.StateHash),
                    DateTime.Parse(row.CreatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime()))
                .ToList();
            var currentCheckpointId = ResolveCurrentCheckpoint(manifestRow.CurrentCheckpointId, checkpoints);
            var ruleset = new RulesetManifest(
                manifestRow.RulesetVersion,
                manifestRow.ModuleCatalogVersion,
                manifestRow.StateSchemaVersion,
                manifestRow.DefinitionContentHash,
                manifestRow.InitializationAlgorithmVersion,
                manifestRow.RandomAlgorithmVersion,
                manifestRow.CommercialReleaseReady != 0);
            var manifest = new WorldSaveManifest(
                requestedSaveId,
                new StableId(manifestRow.WorldId),
                ruleset,
                currentCheckpointId,
                DateTime.Parse(manifestRow.UpdatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
            return new WorldSaveData(
                manifest,
                new WorldSeed(manifestRow.WorldSeed),
                clock,
                topology,
                modules,
                state,
                commands,
                reservations,
                events,
                moduleResults,
                nodeResults,
                snapshots,
                checkpoints);
        }

        private static WorldTopology ReadTopology(SQLiteConnection connection)
        {
            var nodes = connection.Query<NodeRow>("SELECT * FROM node_instance ORDER BY node_id")
                .Select(row => new RegionNode(
                    new StableId(row.NodeId),
                    (SimulationNodeKind)row.NodeKind,
                    row.DisplayName,
                    string.IsNullOrEmpty(row.GeographicParentId) ? (StableId?)null : new StableId(row.GeographicParentId),
                    row.HistoricalClaim != 0))
                .ToList();
            var factions = connection.Query<FactionRow>("SELECT * FROM faction_node ORDER BY faction_id")
                .Select(row => new FactionNode(new StableId(row.FactionId), row.DisplayName, row.HistoricalClaim != 0))
                .ToList();
            var factionRelations = connection.Query<FactionRelationRow>("SELECT * FROM faction_relation ORDER BY from_faction_id, to_faction_id")
                .Select(row => new FactionRelation(
                    new StableId(row.FromFactionId),
                    new StableId(row.ToFactionId),
                    row.RelationKind))
                .ToList();
            var jurisdictions = connection.Query<JurisdictionRow>("SELECT * FROM jurisdiction_relation ORDER BY jurisdiction_id")
                .Select(row => new JurisdictionRelation(
                    new StableId(row.JurisdictionId),
                    new StableId(row.FactionId),
                    new StableId(row.RegionId),
                    row.AuthorityKind,
                    row.HistoricalClaim != 0))
                .ToList();
            var owners = connection.Query<OwnerRow>("SELECT * FROM settlement_owner ORDER BY settlement_id")
                .Select(row => new SettlementOwner(
                    new StableId(row.SettlementId),
                    new StableId(row.OwnerId),
                    row.OwnershipKind))
                .ToList();
            return new WorldTopology(
                new GeographicTree(nodes),
                new FactionGraph(factions, factionRelations),
                new JurisdictionGraph(jurisdictions),
                owners);
        }

        private static List<CommandRecord> ReadCommands(SQLiteConnection connection)
        {
            var statusRows = connection.Query<CommandStatusRow>(
                    "SELECT * FROM command_status_event ORDER BY command_instance_id, sequence")
                .GroupBy(row => row.CommandInstanceId)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var result = new List<CommandRecord>();
            foreach (var row in connection.Query<CommandRow>("SELECT * FROM command_record ORDER BY command_instance_id"))
            {
                var envelope = new CommandEnvelope(
                    new StableId(row.CommandInstanceId),
                    new StableId(row.CommandDefinitionId),
                    new StableId(row.ActorId),
                    new StableId(row.TargetId),
                    new StableId(row.AuthorityScopeId),
                    row.IdempotencyKey,
                    row.Payload ?? Array.Empty<byte>(),
                    new TickId(row.SubmittedTick));
                var events = statusRows.TryGetValue(row.CommandInstanceId, out var rows)
                    ? rows.Select(status => new CommandStatusEvent(
                        new StableId(status.EventId),
                        new StableId(status.CommandInstanceId),
                        (CommandStatus)status.PreviousStatus,
                        (CommandStatus)status.CurrentStatus,
                        new TickId(status.TickId),
                        status.ReasonCode)).ToList()
                    : new List<CommandStatusEvent>();
                result.Add(new CommandRecord(envelope, (CommandStatus)row.CurrentStatus, events));
            }

            return result;
        }

        private static List<ModuleResult> ReadModuleResults(SQLiteConnection connection)
        {
            return connection.Query<ModuleResultRow>("SELECT * FROM module_result ORDER BY tick_id, module_instance_id, stage")
                .Select(row => new ModuleResult(
                    new TickId(row.TickId),
                    new StableId(row.ModuleInstanceId),
                    new StableId(row.NodeId),
                    (WorldExecutionStage)row.Stage,
                    (ModuleImplementationTier)row.ImplementationTier,
                    (DataQuality)row.DataQuality,
                    row.Succeeded != 0,
                    row.ReasonCode))
                .ToList();
        }

        private static List<NodePeriodResult> ReadNodeResults(SQLiteConnection connection, IEnumerable<ModuleResult> moduleResults)
        {
            var lookup = moduleResults.GroupBy(result => $"{result.TickId.Value}\u001f{result.NodeId.Value}")
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            return connection.Query<NodePeriodRow>("SELECT * FROM node_period_result ORDER BY tick_id, node_id")
                .Select(row =>
                {
                    var key = $"{row.TickId}\u001f{row.NodeId}";
                    var modules = lookup.TryGetValue(key, out var values) ? values : new List<ModuleResult>();
                    var residualKeys = string.IsNullOrEmpty(row.ResidualKeys)
                        ? Array.Empty<string>()
                        : row.ResidualKeys.Split(new[] { '\u001e' }, StringSplitOptions.RemoveEmptyEntries);
                    return new NodePeriodResult(
                        new StableId(row.NodeId),
                        new TickId(row.TickId),
                        (PeriodCloseFlags)row.PeriodCloseFlags,
                        (DataQuality)row.DataQuality,
                        modules,
                        new ResidualLedger(new StableId(row.ResidualLedgerId), new TickId(row.TickId), residualKeys));
                })
                .ToList();
        }

        private static StableId ResolveCurrentCheckpoint(string requestedId, IReadOnlyList<WorldCheckpoint> checkpoints)
        {
            var requested = checkpoints.FirstOrDefault(item => string.Equals(item.CheckpointId.Value, requestedId, StringComparison.Ordinal));
            if (requested != null)
            {
                return requested.CheckpointId;
            }

            // 指针缺失时只回退到最近的完整闭合检查点，不猜测或合成状态。
            var fallback = checkpoints.OrderByDescending(item => item.TickId).FirstOrDefault();
            if (fallback == null)
            {
                throw new InvalidDataException("The save contains no valid closed-tick checkpoint.");
            }

            return fallback.CheckpointId;
        }

        private static string SanitizePathSegment(string value)
        {
            var characters = value.Select(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'
                    ? character
                    : '_').ToArray();
            var result = new string(characters).Trim('.');
            if (string.IsNullOrEmpty(result) || result == "..")
            {
                throw new ArgumentException("The identifier cannot be represented as a safe save path.", nameof(value));
            }

            return result;
        }

        private static readonly string[] DeleteOrder =
        {
            "checkpoint_item", "idempotency_key", "command_status_event", "reservation", "ongoing_operation", "command_record",
            "event_log", "module_result", "node_period_result", "node_snapshot", "settlement_ledger",
            "module_state_snapshot", "module_instance", "settlement_owner", "jurisdiction_relation",
            "faction_relation", "faction_node", "node_instance", "world_clock", "checkpoint", "save_manifest"
        };

        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS schema_history(schema_version INTEGER PRIMARY KEY, migration_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS save_manifest(
  save_id TEXT PRIMARY KEY, world_id TEXT NOT NULL, world_seed INTEGER NOT NULL,
  ruleset_version TEXT NOT NULL, module_catalog_version TEXT NOT NULL, state_schema_version TEXT NOT NULL,
  definition_content_hash TEXT NOT NULL, initialization_algorithm_version TEXT NOT NULL,
  random_algorithm_version TEXT NOT NULL, commercial_release_ready INTEGER NOT NULL,
  current_checkpoint_id TEXT NOT NULL, updated_utc TEXT NOT NULL, save_schema_version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS world_clock(
  save_id TEXT PRIMARY KEY, day_index INTEGER NOT NULL, tick_sequence INTEGER NOT NULL,
  economic_year INTEGER NOT NULL, month INTEGER NOT NULL, day INTEGER NOT NULL,
  calendar_definition_id TEXT NOT NULL, last_month_close_tick INTEGER NOT NULL,
  last_season_close_tick INTEGER NOT NULL, last_year_close_tick INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS node_instance(
  node_id TEXT PRIMARY KEY, node_kind INTEGER NOT NULL, display_name TEXT NOT NULL,
  geographic_parent_id TEXT NOT NULL, historical_claim INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS faction_node(
  faction_id TEXT PRIMARY KEY, display_name TEXT NOT NULL, historical_claim INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS faction_relation(
  from_faction_id TEXT NOT NULL, to_faction_id TEXT NOT NULL, relation_kind TEXT NOT NULL,
  PRIMARY KEY(from_faction_id,to_faction_id,relation_kind));
CREATE TABLE IF NOT EXISTS jurisdiction_relation(
  jurisdiction_id TEXT PRIMARY KEY, faction_id TEXT NOT NULL, region_id TEXT NOT NULL,
  authority_kind TEXT NOT NULL, historical_claim INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS settlement_owner(
  settlement_id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, ownership_kind TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS module_instance(
  instance_id TEXT PRIMARY KEY, definition_id TEXT NOT NULL, node_id TEXT NOT NULL, lifecycle_state INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS module_state_snapshot(
  state_key TEXT PRIMARY KEY, state_category INTEGER NOT NULL, codec_id TEXT NOT NULL,
  data_quality INTEGER NOT NULL, payload BLOB NOT NULL);
CREATE TABLE IF NOT EXISTS command_record(
  command_instance_id TEXT PRIMARY KEY, command_definition_id TEXT NOT NULL, actor_id TEXT NOT NULL,
  target_id TEXT NOT NULL, authority_scope_id TEXT NOT NULL, idempotency_key TEXT NOT NULL,
  payload BLOB NOT NULL, submitted_tick INTEGER NOT NULL, current_status INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS command_status_event(
  event_id TEXT PRIMARY KEY, command_instance_id TEXT NOT NULL, previous_status INTEGER NOT NULL,
  current_status INTEGER NOT NULL, tick_id INTEGER NOT NULL, reason_code TEXT NOT NULL, sequence INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS idempotency_key(
  authority_scope_id TEXT NOT NULL, idempotency_key TEXT NOT NULL, command_instance_id TEXT NOT NULL,
  PRIMARY KEY(authority_scope_id,idempotency_key));
CREATE TABLE IF NOT EXISTS reservation(
  reservation_id TEXT PRIMARY KEY, command_instance_id TEXT NOT NULL, authority_scope_id TEXT NOT NULL,
  resource_key TEXT NOT NULL, amount INTEGER NOT NULL, committed INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS ongoing_operation(
  operation_id TEXT PRIMARY KEY, command_instance_id TEXT NOT NULL, payload BLOB NOT NULL);
CREATE TABLE IF NOT EXISTS event_log(
  event_id TEXT PRIMARY KEY, event_definition_id TEXT NOT NULL, source_node_id TEXT NOT NULL,
  committed_tick INTEGER NOT NULL, payload BLOB NOT NULL);
CREATE TABLE IF NOT EXISTS module_result(
  tick_id INTEGER NOT NULL, module_instance_id TEXT NOT NULL, node_id TEXT NOT NULL, stage INTEGER NOT NULL,
  implementation_tier INTEGER NOT NULL, data_quality INTEGER NOT NULL, succeeded INTEGER NOT NULL,
  reason_code TEXT NOT NULL, delta_count INTEGER NOT NULL,
  PRIMARY KEY(tick_id,module_instance_id,stage));
CREATE TABLE IF NOT EXISTS node_period_result(
  tick_id INTEGER NOT NULL, node_id TEXT NOT NULL, period_close_flags INTEGER NOT NULL,
  data_quality INTEGER NOT NULL, residual_ledger_id TEXT NOT NULL, residual_keys TEXT NOT NULL,
  module_result_count INTEGER NOT NULL, PRIMARY KEY(tick_id,node_id));
CREATE TABLE IF NOT EXISTS node_snapshot(
  tick_id INTEGER NOT NULL, node_id TEXT NOT NULL, state_hash TEXT NOT NULL,
  data_quality INTEGER NOT NULL, closed INTEGER NOT NULL, PRIMARY KEY(tick_id,node_id));
CREATE TABLE IF NOT EXISTS settlement_ledger(
  ledger_id TEXT PRIMARY KEY, tick_id INTEGER NOT NULL, node_id TEXT NOT NULL, payload BLOB NOT NULL);
CREATE TABLE IF NOT EXISTS checkpoint(
  checkpoint_id TEXT PRIMARY KEY, tick_id INTEGER NOT NULL UNIQUE, state_hash TEXT NOT NULL, created_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS checkpoint_item(
  checkpoint_id TEXT NOT NULL, state_key TEXT NOT NULL, state_category INTEGER NOT NULL,
  codec_id TEXT NOT NULL, data_quality INTEGER NOT NULL, payload BLOB NOT NULL,
  PRIMARY KEY(checkpoint_id,state_key));";

        private sealed class SaveManifestRow
        {
            [Column("save_id")] public string SaveId { get; set; }
            [Column("world_id")] public string WorldId { get; set; }
            [Column("world_seed")] public long WorldSeed { get; set; }
            [Column("ruleset_version")] public string RulesetVersion { get; set; }
            [Column("module_catalog_version")] public string ModuleCatalogVersion { get; set; }
            [Column("state_schema_version")] public string StateSchemaVersion { get; set; }
            [Column("definition_content_hash")] public string DefinitionContentHash { get; set; }
            [Column("initialization_algorithm_version")] public string InitializationAlgorithmVersion { get; set; }
            [Column("random_algorithm_version")] public string RandomAlgorithmVersion { get; set; }
            [Column("commercial_release_ready")] public int CommercialReleaseReady { get; set; }
            [Column("current_checkpoint_id")] public string CurrentCheckpointId { get; set; }
            [Column("updated_utc")] public string UpdatedUtc { get; set; }
        }

        private sealed class WorldClockRow
        {
            [Column("day_index")] public long DayIndex { get; set; }
            [Column("tick_sequence")] public long TickSequence { get; set; }
            [Column("economic_year")] public int EconomicYear { get; set; }
            [Column("month")] public int Month { get; set; }
            [Column("day")] public int Day { get; set; }
            [Column("calendar_definition_id")] public string CalendarDefinitionId { get; set; }
            [Column("last_month_close_tick")] public long LastMonthCloseTick { get; set; }
            [Column("last_season_close_tick")] public long LastSeasonCloseTick { get; set; }
            [Column("last_year_close_tick")] public long LastYearCloseTick { get; set; }
        }

        private sealed class NodeRow
        {
            [Column("node_id")] public string NodeId { get; set; }
            [Column("node_kind")] public int NodeKind { get; set; }
            [Column("display_name")] public string DisplayName { get; set; }
            [Column("geographic_parent_id")] public string GeographicParentId { get; set; }
            [Column("historical_claim")] public int HistoricalClaim { get; set; }
        }

        private sealed class FactionRow
        {
            [Column("faction_id")] public string FactionId { get; set; }
            [Column("display_name")] public string DisplayName { get; set; }
            [Column("historical_claim")] public int HistoricalClaim { get; set; }
        }

        private sealed class FactionRelationRow
        {
            [Column("from_faction_id")] public string FromFactionId { get; set; }
            [Column("to_faction_id")] public string ToFactionId { get; set; }
            [Column("relation_kind")] public string RelationKind { get; set; }
        }

        private sealed class JurisdictionRow
        {
            [Column("jurisdiction_id")] public string JurisdictionId { get; set; }
            [Column("faction_id")] public string FactionId { get; set; }
            [Column("region_id")] public string RegionId { get; set; }
            [Column("authority_kind")] public string AuthorityKind { get; set; }
            [Column("historical_claim")] public int HistoricalClaim { get; set; }
        }

        private sealed class OwnerRow
        {
            [Column("settlement_id")] public string SettlementId { get; set; }
            [Column("owner_id")] public string OwnerId { get; set; }
            [Column("ownership_kind")] public string OwnershipKind { get; set; }
        }

        private sealed class ModuleInstanceRow
        {
            [Column("instance_id")] public string InstanceId { get; set; }
            [Column("definition_id")] public string DefinitionId { get; set; }
            [Column("node_id")] public string NodeId { get; set; }
            [Column("lifecycle_state")] public int LifecycleState { get; set; }
        }

        private sealed class StateRow
        {
            [Column("state_key")] public string StateKey { get; set; }
            [Column("state_category")] public int StateCategory { get; set; }
            [Column("codec_id")] public string CodecId { get; set; }
            [Column("data_quality")] public int DataQuality { get; set; }
            [Column("payload")] public byte[] Payload { get; set; }
        }

        private sealed class CommandRow
        {
            [Column("command_instance_id")] public string CommandInstanceId { get; set; }
            [Column("command_definition_id")] public string CommandDefinitionId { get; set; }
            [Column("actor_id")] public string ActorId { get; set; }
            [Column("target_id")] public string TargetId { get; set; }
            [Column("authority_scope_id")] public string AuthorityScopeId { get; set; }
            [Column("idempotency_key")] public string IdempotencyKey { get; set; }
            [Column("payload")] public byte[] Payload { get; set; }
            [Column("submitted_tick")] public long SubmittedTick { get; set; }
            [Column("current_status")] public int CurrentStatus { get; set; }
        }

        private sealed class CommandStatusRow
        {
            [Column("event_id")] public string EventId { get; set; }
            [Column("command_instance_id")] public string CommandInstanceId { get; set; }
            [Column("previous_status")] public int PreviousStatus { get; set; }
            [Column("current_status")] public int CurrentStatus { get; set; }
            [Column("tick_id")] public long TickId { get; set; }
            [Column("reason_code")] public string ReasonCode { get; set; }
        }

        private sealed class ReservationRow
        {
            [Column("reservation_id")] public string ReservationId { get; set; }
            [Column("command_instance_id")] public string CommandInstanceId { get; set; }
            [Column("authority_scope_id")] public string AuthorityScopeId { get; set; }
            [Column("resource_key")] public string ResourceKey { get; set; }
            [Column("amount")] public long Amount { get; set; }
            [Column("committed")] public int Committed { get; set; }
        }

        private sealed class EventRow
        {
            [Column("event_id")] public string EventId { get; set; }
            [Column("event_definition_id")] public string EventDefinitionId { get; set; }
            [Column("source_node_id")] public string SourceNodeId { get; set; }
            [Column("committed_tick")] public long CommittedTick { get; set; }
            [Column("payload")] public byte[] Payload { get; set; }
        }

        private sealed class ModuleResultRow
        {
            [Column("tick_id")] public long TickId { get; set; }
            [Column("module_instance_id")] public string ModuleInstanceId { get; set; }
            [Column("node_id")] public string NodeId { get; set; }
            [Column("stage")] public int Stage { get; set; }
            [Column("implementation_tier")] public int ImplementationTier { get; set; }
            [Column("data_quality")] public int DataQuality { get; set; }
            [Column("succeeded")] public int Succeeded { get; set; }
            [Column("reason_code")] public string ReasonCode { get; set; }
        }

        private sealed class NodePeriodRow
        {
            [Column("tick_id")] public long TickId { get; set; }
            [Column("node_id")] public string NodeId { get; set; }
            [Column("period_close_flags")] public int PeriodCloseFlags { get; set; }
            [Column("data_quality")] public int DataQuality { get; set; }
            [Column("residual_ledger_id")] public string ResidualLedgerId { get; set; }
            [Column("residual_keys")] public string ResidualKeys { get; set; }
        }

        private sealed class NodeSnapshotRow
        {
            [Column("tick_id")] public long TickId { get; set; }
            [Column("node_id")] public string NodeId { get; set; }
            [Column("state_hash")] public string StateHash { get; set; }
            [Column("data_quality")] public int DataQuality { get; set; }
        }

        private sealed class CheckpointRow
        {
            [Column("checkpoint_id")] public string CheckpointId { get; set; }
            [Column("tick_id")] public long TickId { get; set; }
            [Column("state_hash")] public string StateHash { get; set; }
            [Column("created_utc")] public string CreatedUtc { get; set; }
        }
    }
}
