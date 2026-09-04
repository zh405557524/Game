using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.World;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>
    /// 描述一次显式时间推进请求。月、季、年并不是直接跳日期，而是连续执行日 Tick，
    /// 直到命中相应的闭合边界。
    /// </summary>
    internal sealed class AdvanceRequest
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

    /// <summary>
    /// System Server 内部唯一可写的世界运行时。它集中管理时钟、已提交状态、模块实例、
    /// 命令、检查点与存档；Unity、Presenter 和 Manager 都不能直接持有它。
    /// </summary>
    /// <remarks>
    /// 每个 Tick 先在 Working State 和命令副本上执行；只有完整成功后，本对象才替换当前状态。
    /// 因而读取诊断信息、刷新窗口或计算状态散列都不会推进世界。
    /// </remarks>
    internal sealed class WorldRuntime
    {
        // 调试历史只保留最近若干 Tick；权威历史由检查点和存档承载。
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

        /// <summary>
        /// 按日、月、季或年推进世界，并返回最后一个日 Tick 的闭合结果。
        /// 任一日失败时立即停止，且失败 Tick 不会改变已提交状态。
        /// </summary>
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

        /// <summary>把命令加入当前权威命令队列；实际校验和执行发生在后续 Tick 阶段。</summary>
        public CommandRecord SubmitCommand(CommandEnvelope envelope)
        {
            return _commandProcessor.Submit(envelope, new TickId(Clock.TickSequence));
        }

        /// <summary>把最近闭合 Tick 的完整世界快照写入配置的存档仓储。</summary>
        public void Save()
        {
            if (_saveStore == null)
            {
                throw new InvalidOperationException("This runtime has no save-game store.");
            }

            _saveStore.Save(ExportSaveData());
        }

        /// <summary>组装持久化 DTO，不直接执行文件或数据库写入。</summary>
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
            // Coordinator 返回一个候选提交包；在 Committed=true 之前不替换任何权威字段。
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

            // 统一交换时钟、状态、命令和检查点，避免调用方观察到半个 Tick。
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

    /// <summary>为展示层提供只读节点快照，避免展示层直接读取 Working State。</summary>
    internal sealed class SnapshotAssembler
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

    /// <summary>创建新世界的确定性初始检查点。</summary>
    internal static class CheckpointCoordinator
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
