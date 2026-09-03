using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    public sealed class TickExecutionCommit
    {
        public TickExecutionCommit(
            WorldClock clock,
            CommittedState state,
            CommandProcessor commandProcessor,
            WorldCheckpoint checkpoint,
            WorldTickResult result)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            State = state ?? throw new ArgumentNullException(nameof(state));
            CommandProcessor = commandProcessor ?? throw new ArgumentNullException(nameof(commandProcessor));
            Checkpoint = checkpoint;
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public WorldClock Clock { get; }
        public CommittedState State { get; }
        public CommandProcessor CommandProcessor { get; }
        public WorldCheckpoint Checkpoint { get; }
        public WorldTickResult Result { get; }
    }

    public sealed class TickCoordinator
    {
        private static readonly IReadOnlyList<WorldExecutionStage> OrderedStages = new ReadOnlyCollection<WorldExecutionStage>(
            Enum.GetValues(typeof(WorldExecutionStage)).Cast<WorldExecutionStage>().OrderBy(stage => (int)stage).ToList());

        private readonly IModuleExecutorFactory _executorFactory;
        private readonly ISimulationDiagnosticsSink _diagnostics;

        public TickCoordinator(IModuleExecutorFactory executorFactory, ISimulationDiagnosticsSink diagnostics = null)
        {
            _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
            _diagnostics = diagnostics ?? new NullSimulationDiagnosticsSink();
        }

        public TickExecutionCommit ExecuteDay(
            StableId worldId,
            WorldSeed worldSeed,
            WorldTopology topology,
            ModuleCatalog catalog,
            ModuleRegistry registry,
            WorldClock currentClock,
            CommittedState currentState,
            CommandProcessor currentCommands)
        {
            if (topology == null || catalog == null || registry == null || currentClock == null || currentState == null || currentCommands == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }

            var advance = currentClock.NextDay();
            var tickId = new TickId(advance.Clock.TickSequence);
            var workingState = currentState.BeginWorkingState();
            var transactionalCommands = currentCommands.Clone();
            var stages = new List<StageExecutionRecord>();
            var moduleResults = new List<ModuleResult>();
            var snapshot = new TickTopologySnapshot(
                tickId,
                topology.Geography.Nodes.Select(node => node.NodeId).Concat(topology.Factions.Nodes.Select(node => node.NodeId)),
                registry.Instances.Select(instance => instance.InstanceId));

            try
            {
                foreach (var stage in OrderedStages)
                {
                    var executionCount = ExecuteStage(
                        stage,
                        tickId,
                        advance.Clock,
                        snapshot,
                        catalog,
                        registry,
                        workingState,
                        transactionalCommands,
                        moduleResults);
                    var stageRecord = new StageExecutionRecord(stage, executionCount, true);
                    stages.Add(stageRecord);
                    _diagnostics.RecordStage(tickId, stageRecord);
                }

                var committedState = workingState.Commit();
                var stateHash = DeterministicStateHasher.Compute(worldId, worldSeed, advance.Clock, topology, registry, committedState);
                var nodeResults = BuildNodeResults(topology, tickId, advance.CloseFlags, stateHash, moduleResults);
                var nodeSnapshots = topology.Geography.Nodes.Select(node => new NodeSnapshot(
                    node.NodeId,
                    tickId,
                    stateHash,
                    moduleResults.Any(result => result.NodeId.Equals(node.NodeId)) ? DataQuality.Unavailable : DataQuality.Unknown)).ToList();
                var checkpoint = new WorldCheckpoint(
                    new StableId($"checkpoint.{tickId.Value:D20}"),
                    tickId,
                    stateHash,
                    DeterministicTimestamp(tickId));
                var result = new WorldTickResult(
                    tickId,
                    true,
                    advance.CloseFlags,
                    stateHash,
                    stages,
                    moduleResults,
                    nodeResults,
                    nodeSnapshots);
                return new TickExecutionCommit(advance.Clock, committedState, transactionalCommands, checkpoint, result);
            }
            catch (Exception exception)
            {
                workingState.Rollback();
                var stateHash = DeterministicStateHasher.Compute(worldId, worldSeed, currentClock, topology, registry, currentState);
                var failedStage = OrderedStages.FirstOrDefault(stage => stages.All(record => record.Stage != stage));
                var failedRecord = new StageExecutionRecord(failedStage, 0, false, exception.GetType().Name);
                stages.Add(failedRecord);
                _diagnostics.RecordStage(tickId, failedRecord);
                var failure = new WorldTickResult(
                    tickId,
                    false,
                    PeriodCloseFlags.None,
                    stateHash,
                    stages,
                    moduleResults,
                    Array.Empty<NodePeriodResult>(),
                    Array.Empty<NodeSnapshot>(),
                    exception.GetType().Name + ": " + exception.Message);
                return new TickExecutionCommit(currentClock, currentState, currentCommands, null, failure);
            }
        }

        private int ExecuteStage(
            WorldExecutionStage stage,
            TickId tickId,
            WorldClock clock,
            TickTopologySnapshot topologySnapshot,
            ModuleCatalog catalog,
            ModuleRegistry registry,
            WorkingState workingState,
            CommandProcessor commands,
            ICollection<ModuleResult> moduleResults)
        {
            var executionCount = 0;
            if (stage == WorldExecutionStage.S80CommandValidation)
            {
                executionCount += commands.ValidatePending(tickId);
            }
            else if (stage == WorldExecutionStage.S90ReservationCommit)
            {
                executionCount += commands.ReserveAccepted(tickId);
            }
            else if (stage == WorldExecutionStage.S100CommandDispatch)
            {
                executionCount += commands.DispatchReserved(tickId);
            }
            else if (stage == WorldExecutionStage.S110ImmediateExecution)
            {
                executionCount += commands.ExecuteDispatched(tickId);
            }

            foreach (var instance in registry.Instances)
            {
                if (instance.LifecycleState != ModuleLifecycleState.Active && instance.LifecycleState != ModuleLifecycleState.Degraded)
                {
                    continue;
                }

                var definition = catalog.GetRequired(instance.DefinitionId);
                if (!definition.Stages.Contains(stage))
                {
                    continue;
                }

                var executor = _executorFactory.Create(definition);
                var context = new ModuleExecutionContext(tickId, stage, clock, definition, instance, workingState, topologySnapshot);
                var result = executor.Execute(context);
                if (result == null || !result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Module '{instance.InstanceId}' failed during stage '{stage}' with reason '{result?.ReasonCode ?? "missing_result"}'.");
                }

                moduleResults.Add(result);
                _diagnostics.RecordModuleResult(result);
                executionCount++;
            }

            return executionCount;
        }

        private static IReadOnlyList<NodePeriodResult> BuildNodeResults(
            WorldTopology topology,
            TickId tickId,
            PeriodCloseFlags closeFlags,
            StateHash stateHash,
            IEnumerable<ModuleResult> moduleResults)
        {
            var byNode = moduleResults.GroupBy(result => result.NodeId).ToDictionary(group => group.Key, group => group.ToList());
            var results = new List<NodePeriodResult>();
            foreach (var node in topology.Geography.Nodes)
            {
                var nodeModules = byNode.TryGetValue(node.NodeId, out var values) ? values : new List<ModuleResult>();
                results.Add(new NodePeriodResult(
                    node.NodeId,
                    tickId,
                    closeFlags,
                    nodeModules.Count > 0 ? DataQuality.Unavailable : DataQuality.Unknown,
                    nodeModules,
                    new ResidualLedger(new StableId($"residual.{node.NodeId.Value}.{tickId.Value:D20}"), tickId)));
            }

            return new ReadOnlyCollection<NodePeriodResult>(results);
        }

        private static DateTime DeterministicTimestamp(TickId tickId)
        {
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(tickId.Value);
        }
    }
}
