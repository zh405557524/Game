using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.World
{
    /// <summary>存档身份、规则兼容信息和当前检查点指针。</summary>
    public sealed class WorldSaveManifest
    {
        public WorldSaveManifest(
            StableId saveId,
            StableId worldId,
            RulesetManifest ruleset,
            StableId currentCheckpointId,
            DateTime updatedUtc)
        {
            SimulationNode.RequireId(saveId, nameof(saveId));
            SimulationNode.RequireId(worldId, nameof(worldId));
            SimulationNode.RequireId(currentCheckpointId, nameof(currentCheckpointId));
            if (updatedUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Save timestamps must use UTC.", nameof(updatedUtc));
            }

            SaveId = saveId;
            WorldId = worldId;
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
            CurrentCheckpointId = currentCheckpointId;
            UpdatedUtc = updatedUtc;
        }

        public StableId SaveId { get; }
        public StableId WorldId { get; }
        public RulesetManifest Ruleset { get; }
        public StableId CurrentCheckpointId { get; }
        public DateTime UpdatedUtc { get; }
    }

    /// <summary>
    /// Application 与持久化适配器之间的完整存档 DTO；不包含 SQLite 或 Unity 类型。
    /// </summary>
    public sealed class WorldSaveData
    {
        public WorldSaveData(
            WorldSaveManifest manifest,
            WorldSeed worldSeed,
            WorldClock clock,
            WorldTopology topology,
            IEnumerable<ModuleInstance> moduleInstances,
            CommittedState committedState,
            IEnumerable<CommandRecord> commands,
            IEnumerable<ResourceReservation> reservations,
            IEnumerable<EventEnvelope> events,
            IEnumerable<ModuleResult> moduleResults,
            IEnumerable<NodePeriodResult> nodePeriodResults,
            IEnumerable<NodeSnapshot> nodeSnapshots,
            IEnumerable<WorldCheckpoint> checkpoints)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            WorldSeed = worldSeed;
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            ModuleInstances = Copy(moduleInstances, instance => instance.InstanceId.Value);
            CommittedState = committedState ?? throw new ArgumentNullException(nameof(committedState));
            Commands = Copy(commands, command => command.Envelope.CommandInstanceId.Value);
            Reservations = Copy(reservations, reservation => reservation.ReservationId.Value);
            Events = Copy(events, item => item.EventId.Value);
            ModuleResults = Copy(moduleResults, result => $"{result.TickId.Value:D20}:{result.ModuleInstanceId.Value}:{(int)result.Stage:D3}");
            NodePeriodResults = Copy(nodePeriodResults, result => $"{result.TickId.Value:D20}:{result.NodeId.Value}");
            NodeSnapshots = Copy(nodeSnapshots, snapshot => $"{snapshot.TickId.Value:D20}:{snapshot.NodeId.Value}");
            Checkpoints = Copy(checkpoints, checkpoint => checkpoint.CheckpointId.Value);

            if (!Checkpoints.Any(checkpoint => checkpoint.CheckpointId.Equals(Manifest.CurrentCheckpointId)))
            {
                throw new InvalidOperationException("The save manifest must reference a stored checkpoint.");
            }
        }

        public WorldSaveManifest Manifest { get; }
        public WorldSeed WorldSeed { get; }
        public WorldClock Clock { get; }
        public WorldTopology Topology { get; }
        public IReadOnlyList<ModuleInstance> ModuleInstances { get; }
        public CommittedState CommittedState { get; }
        public IReadOnlyList<CommandRecord> Commands { get; }
        public IReadOnlyList<ResourceReservation> Reservations { get; }
        public IReadOnlyList<EventEnvelope> Events { get; }
        public IReadOnlyList<ModuleResult> ModuleResults { get; }
        public IReadOnlyList<NodePeriodResult> NodePeriodResults { get; }
        public IReadOnlyList<NodeSnapshot> NodeSnapshots { get; }
        public IReadOnlyList<WorldCheckpoint> Checkpoints { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, Func<T, string> orderKey)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var values = source.ToList();
            if (values.Any(value => value == null))
            {
                throw new InvalidOperationException("Save collections cannot contain null values.");
            }

            return new ReadOnlyCollection<T>(values.OrderBy(orderKey, StringComparer.Ordinal).ToList());
        }
    }
}
