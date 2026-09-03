using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    public sealed class AdvanceRequest
    {
        public AdvanceRequest(AdvanceUnit unit, int count = 1)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Unit = unit;
            Count = count;
        }

        public AdvanceUnit Unit { get; }
        public int Count { get; }
    }

    public sealed class WorldRuntime : ISimulationSessionRuntime
    {
        private const int MaximumTickHistory = 32;

        private readonly TickCoordinator _tickCoordinator;
        private readonly ISaveGameStore _saveStore;
        private readonly List<WorldTickResult> _tickHistory;
        private readonly List<WorldCheckpoint> _checkpoints;
        private readonly List<EventEnvelope> _events;
        private IReadOnlyList<ModuleResult> _latestModuleResults;
        private IReadOnlyList<NodePeriodResult> _latestNodeResults;
        private IReadOnlyList<NodeSnapshot> _latestNodeSnapshots;
        private CommandProcessor _commandProcessor;
        private CommittedState _committedState;

        public WorldRuntime(
            StableId saveId,
            StableId worldId,
            WorldSeed worldSeed,
            RulesetManifest ruleset,
            WorldTopology topology,
            ModuleCatalog moduleCatalog,
            ModuleRegistry moduleRegistry,
            TickCoordinator tickCoordinator,
            ISaveGameStore saveStore = null,
            WorldClock clock = null,
            CommittedState committedState = null,
            CommandProcessor commandProcessor = null,
            IEnumerable<EventEnvelope> events = null,
            IEnumerable<WorldCheckpoint> checkpoints = null,
            IEnumerable<ModuleResult> latestModuleResults = null,
            IEnumerable<NodePeriodResult> latestNodeResults = null,
            IEnumerable<NodeSnapshot> latestNodeSnapshots = null)
        {
            SimulationNode.RequireId(saveId, nameof(saveId));
            SimulationNode.RequireId(worldId, nameof(worldId));
            SaveId = saveId;
            WorldId = worldId;
            WorldSeed = worldSeed;
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            ModuleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
            ModuleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
            _tickCoordinator = tickCoordinator ?? throw new ArgumentNullException(nameof(tickCoordinator));
            _saveStore = saveStore;
            Clock = clock ?? new WorldClock(0, 0, 1, 1, 1, new StableId("calendar.economic-12x30.v1"));
            _committedState = committedState ?? new CommittedState();
            _commandProcessor = commandProcessor ?? new CommandProcessor(true);
            _events = (events ?? Array.Empty<EventEnvelope>()).ToList();
            _checkpoints = (checkpoints ?? Array.Empty<WorldCheckpoint>()).OrderBy(item => item.TickId).ToList();
            _tickHistory = new List<WorldTickResult>();
            _latestModuleResults = new ReadOnlyCollection<ModuleResult>((latestModuleResults ?? Array.Empty<ModuleResult>()).ToList());
            _latestNodeResults = new ReadOnlyCollection<NodePeriodResult>((latestNodeResults ?? Array.Empty<NodePeriodResult>()).ToList());
            _latestNodeSnapshots = new ReadOnlyCollection<NodeSnapshot>((latestNodeSnapshots ?? Array.Empty<NodeSnapshot>()).ToList());

            if (_checkpoints.Count == 0)
            {
                _checkpoints.Add(CheckpointCoordinator.CreateInitial(WorldId, WorldSeed, Clock, Topology, ModuleRegistry, _committedState));
            }
        }

        public StableId SaveId { get; }
        public StableId WorldId { get; }
        public WorldSeed WorldSeed { get; }
        public RulesetManifest Ruleset { get; }
        public WorldTopology Topology { get; }
        public ModuleCatalog ModuleCatalog { get; }
        public ModuleRegistry ModuleRegistry { get; }
        public WorldClock Clock { get; private set; }
        public long ElapsedDays => Clock.DayIndex;
        public StateHash CurrentStateHash => DeterministicStateHasher.Compute(WorldId, WorldSeed, Clock, Topology, ModuleRegistry, _committedState);
        public IReadOnlyList<WorldTickResult> TickHistory => new ReadOnlyCollection<WorldTickResult>(_tickHistory.ToList());
        public IReadOnlyList<WorldCheckpoint> Checkpoints => new ReadOnlyCollection<WorldCheckpoint>(_checkpoints.ToList());
        public IReadOnlyList<CommandRecord> Commands => _commandProcessor.Commands;
        public IReadOnlyList<ResourceReservation> Reservations => _commandProcessor.Reservations;
        public IReadOnlyList<EventEnvelope> Events => new ReadOnlyCollection<EventEnvelope>(_events.ToList());
        public IReadOnlyList<ModuleResult> LatestModuleResults => _latestModuleResults;
        public IReadOnlyList<NodePeriodResult> LatestNodeResults => _latestNodeResults;
        public IReadOnlyList<NodeSnapshot> LatestNodeSnapshots => _latestNodeSnapshots;
        public CommittedState CommittedState => _committedState;

        public WorldTickResult Advance(AdvanceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            WorldTickResult lastResult = null;
            var completed = 0;
            var safetyLimit = checked(request.Count * WorldClock.DefaultMonthCount * WorldClock.DefaultDaysPerMonth + WorldClock.DefaultDaysPerMonth);
            for (var executed = 0; completed < request.Count && executed < safetyLimit; executed++)
            {
                lastResult = AdvanceDayInternal();
                if (!lastResult.Committed)
                {
                    return lastResult;
                }

                if (CompletesRequestedUnit(request.Unit, lastResult.PeriodCloseFlags))
                {
                    completed++;
                }
            }

            if (completed != request.Count || lastResult == null)
            {
                throw new InvalidOperationException("The requested calendar advance did not reach its deterministic boundary.");
            }

            return lastResult;
        }

        public void AdvanceOneDay()
        {
            var result = Advance(new AdvanceRequest(AdvanceUnit.Day));
            if (!result.Committed)
            {
                throw new InvalidOperationException("The day tick rolled back: " + result.FailureReason);
            }
        }

        public CommandRecord SubmitCommand(CommandEnvelope envelope)
        {
            return _commandProcessor.Submit(envelope, new TickId(Clock.TickSequence));
        }

        public void Save()
        {
            if (_saveStore == null)
            {
                throw new InvalidOperationException("This runtime has no save-game store.");
            }

            _saveStore.Save(ExportSaveData());
        }

        public WorldSaveData ExportSaveData()
        {
            var currentCheckpoint = _checkpoints[_checkpoints.Count - 1];
            var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Clock.TickSequence);
            var manifest = new WorldSaveManifest(SaveId, WorldId, Ruleset, currentCheckpoint.CheckpointId, timestamp);
            return new WorldSaveData(
                manifest,
                WorldSeed,
                Clock,
                Topology,
                ModuleRegistry.Instances,
                _committedState,
                _commandProcessor.Commands,
                _commandProcessor.Reservations,
                _events,
                _latestModuleResults,
                _latestNodeResults,
                _latestNodeSnapshots,
                _checkpoints);
        }

        private WorldTickResult AdvanceDayInternal()
        {
            var commit = _tickCoordinator.ExecuteDay(
                WorldId,
                WorldSeed,
                Topology,
                ModuleCatalog,
                ModuleRegistry,
                Clock,
                _committedState,
                _commandProcessor);
            var result = commit.Result;
            AddTickHistory(result);
            if (!result.Committed)
            {
                return result;
            }

            Clock = commit.Clock;
            _committedState = commit.State;
            _commandProcessor = commit.CommandProcessor;
            _checkpoints.Add(commit.Checkpoint);
            _latestModuleResults = new ReadOnlyCollection<ModuleResult>(result.ModuleResults.ToList());
            _latestNodeResults = new ReadOnlyCollection<NodePeriodResult>(result.NodeResults.ToList());
            _latestNodeSnapshots = new ReadOnlyCollection<NodeSnapshot>(result.NodeSnapshots.ToList());
            return result;
        }

        private void AddTickHistory(WorldTickResult result)
        {
            _tickHistory.Add(result);
            if (_tickHistory.Count > MaximumTickHistory)
            {
                _tickHistory.RemoveAt(0);
            }
        }

        private static bool CompletesRequestedUnit(AdvanceUnit unit, PeriodCloseFlags flags)
        {
            switch (unit)
            {
                case AdvanceUnit.Day: return (flags & PeriodCloseFlags.Day) != 0;
                case AdvanceUnit.Month: return (flags & PeriodCloseFlags.Month) != 0;
                case AdvanceUnit.Season: return (flags & PeriodCloseFlags.Season) != 0;
                case AdvanceUnit.Year: return (flags & PeriodCloseFlags.Year) != 0;
                default: throw new ArgumentOutOfRangeException(nameof(unit));
            }
        }
    }

    public sealed class SnapshotAssembler
    {
        public IReadOnlyList<NodeSnapshot> GetLatest(WorldRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return runtime.LatestNodeSnapshots;
        }
    }

    public static class CheckpointCoordinator
    {
        public static WorldCheckpoint CreateInitial(
            StableId worldId,
            WorldSeed worldSeed,
            WorldClock clock,
            WorldTopology topology,
            ModuleRegistry registry,
            CommittedState state)
        {
            var hash = DeterministicStateHasher.Compute(worldId, worldSeed, clock, topology, registry, state);
            return new WorldCheckpoint(
                new StableId("checkpoint.00000000000000000000"),
                new TickId(0),
                hash,
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
