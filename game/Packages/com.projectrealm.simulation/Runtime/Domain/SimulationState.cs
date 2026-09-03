using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ProjectRealm.Domain
{
    public enum DataQuality
    {
        Exact,
        Aggregated,
        Estimated,
        Stale,
        Partial,
        Unknown,
        Unavailable,
        Blocked
    }

    public enum StateCategory
    {
        DirectState,
        AggregateState,
        PeriodFlow,
        DerivedIndicator,
        HardState,
        SoftState,
        ObligationState,
        TransitState,
        ProjectState
    }

    public sealed class StateRecord
    {
        public StateRecord(
            string key,
            StateCategory category,
            byte[] payload,
            string codecId,
            DataQuality dataQuality)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A state record key is required.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(codecId))
            {
                throw new ArgumentException("A state codec ID is required.", nameof(codecId));
            }

            Key = key;
            Category = category;
            Payload = (byte[])(payload ?? throw new ArgumentNullException(nameof(payload))).Clone();
            CodecId = codecId;
            DataQuality = dataQuality;
        }

        public string Key { get; }
        public StateCategory Category { get; }
        public byte[] Payload { get; }
        public string CodecId { get; }
        public DataQuality DataQuality { get; }

        public StateRecord Clone()
        {
            return new StateRecord(Key, Category, Payload, CodecId, DataQuality);
        }
    }

    public sealed class CommittedState
    {
        private readonly Dictionary<string, StateRecord> _records;

        public CommittedState(IEnumerable<StateRecord> records = null)
        {
            _records = new Dictionary<string, StateRecord>(StringComparer.Ordinal);
            foreach (var record in records ?? Array.Empty<StateRecord>())
            {
                if (record == null || _records.ContainsKey(record.Key))
                {
                    throw new InvalidOperationException("Committed state record keys must be unique.");
                }

                _records.Add(record.Key, record.Clone());
            }

            Records = new ReadOnlyCollection<StateRecord>(_records.Values
                .OrderBy(record => record.Key, StringComparer.Ordinal)
                .Select(record => record.Clone())
                .ToList());
        }

        public IReadOnlyList<StateRecord> Records { get; }

        public bool TryGet(string key, out StateRecord record)
        {
            if (_records.TryGetValue(key, out var stored))
            {
                record = stored.Clone();
                return true;
            }

            record = null;
            return false;
        }

        public WorkingState BeginWorkingState()
        {
            return new WorkingState(this);
        }
    }

    public sealed class StateDelta
    {
        public StateDelta(string key, StateRecord replacement)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A state delta key is required.", nameof(key));
            }

            if (replacement != null && !string.Equals(replacement.Key, key, StringComparison.Ordinal))
            {
                throw new ArgumentException("The replacement record must use the delta key.", nameof(replacement));
            }

            Key = key;
            Replacement = replacement?.Clone();
        }

        public string Key { get; }
        public StateRecord Replacement { get; }
        public bool RemovesRecord => Replacement == null;
    }

    public sealed class WorkingState
    {
        private readonly Dictionary<string, StateRecord> _records;
        private readonly List<StateDelta> _deltas;
        private bool _closed;

        internal WorkingState(CommittedState source)
        {
            _records = source.Records.ToDictionary(record => record.Key, record => record.Clone(), StringComparer.Ordinal);
            _deltas = new List<StateDelta>();
        }

        public IReadOnlyList<StateDelta> Deltas => new ReadOnlyCollection<StateDelta>(_deltas.ToList());

        public void Set(StateRecord record)
        {
            EnsureOpen();
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            _records[record.Key] = record.Clone();
            _deltas.Add(new StateDelta(record.Key, record));
        }

        public void Remove(string key)
        {
            EnsureOpen();
            if (_records.Remove(key))
            {
                _deltas.Add(new StateDelta(key, null));
            }
        }

        public bool TryGet(string key, out StateRecord record)
        {
            if (_records.TryGetValue(key, out var stored))
            {
                record = stored.Clone();
                return true;
            }

            record = null;
            return false;
        }

        public CommittedState Commit()
        {
            EnsureOpen();
            _closed = true;
            return new CommittedState(_records.Values);
        }

        public void Rollback()
        {
            EnsureOpen();
            _closed = true;
        }

        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new InvalidOperationException("The working state is already closed.");
            }
        }
    }

    public sealed class ModuleResult
    {
        public ModuleResult(
            TickId tickId,
            StableId moduleInstanceId,
            StableId nodeId,
            WorldExecutionStage stage,
            ModuleImplementationTier implementationTier,
            DataQuality dataQuality,
            bool succeeded,
            string reasonCode,
            IEnumerable<StateDelta> deltas = null)
        {
            SimulationNode.RequireId(moduleInstanceId, nameof(moduleInstanceId));
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            TickId = tickId;
            ModuleInstanceId = moduleInstanceId;
            NodeId = nodeId;
            Stage = stage;
            ImplementationTier = implementationTier;
            DataQuality = dataQuality;
            Succeeded = succeeded;
            ReasonCode = reasonCode ?? string.Empty;
            Deltas = new ReadOnlyCollection<StateDelta>((deltas ?? Array.Empty<StateDelta>()).ToList());
        }

        public TickId TickId { get; }
        public StableId ModuleInstanceId { get; }
        public StableId NodeId { get; }
        public WorldExecutionStage Stage { get; }
        public ModuleImplementationTier ImplementationTier { get; }
        public DataQuality DataQuality { get; }
        public bool Succeeded { get; }
        public string ReasonCode { get; }
        public IReadOnlyList<StateDelta> Deltas { get; }
    }

    public sealed class ResidualLedger
    {
        public ResidualLedger(StableId ledgerId, TickId tickId, IEnumerable<string> residualKeys = null)
        {
            SimulationNode.RequireId(ledgerId, nameof(ledgerId));
            LedgerId = ledgerId;
            TickId = tickId;
            ResidualKeys = new ReadOnlyCollection<string>((residualKeys ?? Array.Empty<string>())
                .OrderBy(value => value, StringComparer.Ordinal).ToList());
        }

        public StableId LedgerId { get; }
        public TickId TickId { get; }
        public IReadOnlyList<string> ResidualKeys { get; }
    }

    public sealed class NodePeriodResult
    {
        public NodePeriodResult(
            StableId nodeId,
            TickId tickId,
            PeriodCloseFlags periodCloseFlags,
            DataQuality dataQuality,
            IEnumerable<ModuleResult> moduleResults,
            ResidualLedger residualLedger)
        {
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            NodeId = nodeId;
            TickId = tickId;
            PeriodCloseFlags = periodCloseFlags;
            DataQuality = dataQuality;
            ModuleResults = new ReadOnlyCollection<ModuleResult>((moduleResults ?? throw new ArgumentNullException(nameof(moduleResults)))
                .OrderBy(result => result.ModuleInstanceId.Value, StringComparer.Ordinal).ToList());
            ResidualLedger = residualLedger ?? throw new ArgumentNullException(nameof(residualLedger));
        }

        public StableId NodeId { get; }
        public TickId TickId { get; }
        public PeriodCloseFlags PeriodCloseFlags { get; }
        public DataQuality DataQuality { get; }
        public IReadOnlyList<ModuleResult> ModuleResults { get; }
        public ResidualLedger ResidualLedger { get; }
    }

    public sealed class NodeSnapshot
    {
        public NodeSnapshot(StableId nodeId, TickId tickId, StateHash stateHash, DataQuality dataQuality)
        {
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            NodeId = nodeId;
            TickId = tickId;
            StateHash = stateHash;
            DataQuality = dataQuality;
        }

        public StableId NodeId { get; }
        public TickId TickId { get; }
        public StateHash StateHash { get; }
        public DataQuality DataQuality { get; }
    }

    public sealed class StageExecutionRecord
    {
        public StageExecutionRecord(WorldExecutionStage stage, int moduleExecutionCount, bool succeeded, string reasonCode = null)
        {
            if (moduleExecutionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moduleExecutionCount));
            }

            Stage = stage;
            ModuleExecutionCount = moduleExecutionCount;
            Succeeded = succeeded;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public WorldExecutionStage Stage { get; }
        public int ModuleExecutionCount { get; }
        public bool Succeeded { get; }
        public string ReasonCode { get; }
    }

    public sealed class WorldTickResult
    {
        public WorldTickResult(
            TickId tickId,
            bool committed,
            PeriodCloseFlags periodCloseFlags,
            StateHash stateHash,
            IEnumerable<StageExecutionRecord> stages,
            IEnumerable<ModuleResult> moduleResults,
            IEnumerable<NodePeriodResult> nodeResults,
            IEnumerable<NodeSnapshot> nodeSnapshots,
            string failureReason = null)
        {
            TickId = tickId;
            Committed = committed;
            PeriodCloseFlags = periodCloseFlags;
            StateHash = stateHash;
            Stages = new ReadOnlyCollection<StageExecutionRecord>((stages ?? throw new ArgumentNullException(nameof(stages))).ToList());
            ModuleResults = new ReadOnlyCollection<ModuleResult>((moduleResults ?? throw new ArgumentNullException(nameof(moduleResults))).ToList());
            NodeResults = new ReadOnlyCollection<NodePeriodResult>((nodeResults ?? throw new ArgumentNullException(nameof(nodeResults))).ToList());
            NodeSnapshots = new ReadOnlyCollection<NodeSnapshot>((nodeSnapshots ?? throw new ArgumentNullException(nameof(nodeSnapshots))).ToList());
            FailureReason = failureReason ?? string.Empty;
        }

        public TickId TickId { get; }
        public bool Committed { get; }
        public PeriodCloseFlags PeriodCloseFlags { get; }
        public StateHash StateHash { get; }
        public IReadOnlyList<StageExecutionRecord> Stages { get; }
        public IReadOnlyList<ModuleResult> ModuleResults { get; }
        public IReadOnlyList<NodePeriodResult> NodeResults { get; }
        public IReadOnlyList<NodeSnapshot> NodeSnapshots { get; }
        public string FailureReason { get; }
    }

    public static class DeterministicStateHasher
    {
        public static StateHash Compute(
            StableId worldId,
            WorldSeed worldSeed,
            WorldClock clock,
            WorldTopology topology,
            ModuleRegistry modules,
            CommittedState state)
        {
            if (clock == null || topology == null || modules == null || state == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                WriteString(writer, worldId.Value);
                writer.Write(worldSeed.Value);
                writer.Write(clock.DayIndex);
                writer.Write(clock.TickSequence);
                writer.Write(clock.EconomicYear);
                writer.Write(clock.Month);
                writer.Write(clock.Day);
                WriteString(writer, clock.CalendarDefinitionId.Value);

                foreach (var node in topology.Geography.Nodes)
                {
                    WriteString(writer, node.NodeId.Value);
                    writer.Write((int)node.Kind);
                    WriteString(writer, node.DisplayName);
                    WriteString(writer, node.GeographicParentId?.Value ?? string.Empty);
                    writer.Write(node.HistoricalClaim);
                }

                foreach (var faction in topology.Factions.Nodes)
                {
                    WriteString(writer, faction.NodeId.Value);
                    WriteString(writer, faction.DisplayName);
                }

                foreach (var relation in topology.Factions.Relations)
                {
                    WriteString(writer, relation.FromFactionId.Value);
                    WriteString(writer, relation.ToFactionId.Value);
                    WriteString(writer, relation.RelationKind);
                }

                foreach (var relation in topology.Jurisdictions.Relations)
                {
                    WriteString(writer, relation.JurisdictionId.Value);
                    WriteString(writer, relation.FactionId.Value);
                    WriteString(writer, relation.RegionId.Value);
                    WriteString(writer, relation.AuthorityKind);
                    writer.Write(relation.HistoricalClaim);
                }

                foreach (var owner in topology.SettlementOwners)
                {
                    WriteString(writer, owner.SettlementId.Value);
                    WriteString(writer, owner.OwnerId.Value);
                    WriteString(writer, owner.OwnershipKind);
                }

                foreach (var instance in modules.Instances)
                {
                    WriteString(writer, instance.InstanceId.Value);
                    WriteString(writer, instance.DefinitionId.Value);
                    WriteString(writer, instance.NodeId.Value);
                    writer.Write((int)instance.LifecycleState);
                }

                foreach (var record in state.Records)
                {
                    WriteString(writer, record.Key);
                    writer.Write((int)record.Category);
                    WriteString(writer, record.CodecId);
                    writer.Write((int)record.DataQuality);
                    writer.Write(record.Payload.Length);
                    writer.Write(record.Payload);
                }

                writer.Flush();
                using (var sha256 = SHA256.Create())
                {
                    return new StateHash(ToHex(sha256.ComputeHash(stream.ToArray())));
                }
            }
        }

        public static string HashBytes(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes ?? throw new ArgumentNullException(nameof(bytes))));
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }

    public sealed class Pcg32
    {
        private ulong _state;
        private readonly ulong _increment;

        public Pcg32(ulong seed, ulong sequence)
        {
            _increment = (sequence << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        public uint NextUInt()
        {
            var previous = _state;
            _state = unchecked(previous * 6364136223846793005UL + _increment);
            var xorShifted = (uint)(((previous >> 18) ^ previous) >> 27);
            var rotation = (int)(previous >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        public static Pcg32 FromDescriptor(RandomStreamDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            var material = string.Join("\u001f", new[]
            {
                descriptor.WorldSeed.Value.ToString(),
                descriptor.TickId.Value.ToString(),
                descriptor.NodeId.Value,
                descriptor.ModuleId.Value,
                descriptor.Purpose,
                descriptor.EntityId.Value,
                descriptor.AlgorithmVersion
            });
            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
                return new Pcg32(ReadUInt64LittleEndian(digest, 0), ReadUInt64LittleEndian(digest, 8));
            }
        }

        private static ulong ReadUInt64LittleEndian(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++)
            {
                value |= (ulong)bytes[offset + index] << (index * 8);
            }

            return value;
        }
    }
}
