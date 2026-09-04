using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    /// <summary>
    /// 一个完整日 Tick 成功后的候选提交包。失败时仍返回原时钟、原状态与原命令处理器。
    /// </summary>
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

    /// <summary>
    /// 按固定顺序调度 14 个世界执行阶段，并负责 Working State 的提交或整 Tick 回滚。
    /// </summary>
    public sealed class TickCoordinator
    {
        // 阶段枚举值就是协议顺序；排序后执行可避免注册顺序影响确定性。
        private static readonly IReadOnlyList<WorldExecutionStage> OrderedStages = new ReadOnlyCollection<WorldExecutionStage>(
            Enum.GetValues(typeof(WorldExecutionStage)).Cast<WorldExecutionStage>().OrderBy(stage => (int)stage).ToList());

        private readonly IModuleExecutorFactory _executorFactory;
        private readonly ISimulationDiagnosticsSink _diagnostics;

        public TickCoordinator(IModuleExecutorFactory executorFactory, ISimulationDiagnosticsSink diagnostics = null)
        {
            _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
            _diagnostics = diagnostics ?? new NullSimulationDiagnosticsSink();
        }

        /// <summary>
        /// 执行一个日 Tick。模块异常、失败结果或校验异常都会进入 catch 分支，
        /// 丢弃 Working State 与命令副本，并返回未提交结果。
        /// </summary>
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

            // 先计算候选时钟；真正提交前 currentClock 仍是调用方持有的旧对象。
            var advance = currentClock.NextDay();
            var tickId = new TickId(advance.Clock.TickSequence);
            var workingState = currentState.BeginWorkingState();
            // 命令状态机也参与事务：失败 Tick 不得留下已校验或已预留的命令。
            var transactionalCommands = currentCommands.Clone();
            var stages = new List<StageExecutionRecord>();
            var moduleResults = new List<ModuleResult>();
            // S00 的拓扑冻结结果在整个 Tick 内复用，模块不能边执行边改节点集合。
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

                // 14 个阶段全部成功后才把 Working State 固化并计算闭合散列。
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
                // 回滚只影响候选对象；返回的时钟、状态与命令仍指向调用前版本。
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
            // 命令协议固定占用 S80-S110；模块执行仍按同一阶段规则随后运行。
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

            // Registry 已按节点和定义 ID 排序，因此模块遍历顺序可复现。
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
