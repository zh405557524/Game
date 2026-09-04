using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.World
{
    /// <summary>世界 Tick 的固定执行协议；数值用于稳定排序，不应随意重排。</summary>
    public enum WorldExecutionStage
    {
        S00FreezeTopology = 0,
        S10CollectDueWork = 10,
        S20PrepareInputs = 20,
        S30LocalFactSettlement = 30,
        S40UpwardAggregation = 40,
        S50SnapshotClose = 50,
        S60PerceptionBuild = 60,
        S70DecisionPlanning = 70,
        S80CommandValidation = 80,
        S90ReservationCommit = 90,
        S100CommandDispatch = 100,
        S110ImmediateExecution = 110,
        S120EventCommit = 120,
        S130AuditAndCheckpoint = 130
    }

    /// <summary>一个日 Tick 同时闭合了哪些经济历周期。</summary>
    [Flags]
    public enum PeriodCloseFlags
    {
        None = 0,
        Day = 1,
        Month = 2,
        Season = 4,
        Year = 8
    }

    /// <summary>应用层可请求的显式推进单位。</summary>
    public enum AdvanceUnit
    {
        Day,
        Month,
        Season,
        Year
    }

    /// <summary>世界内单调递增的 Tick 序号。</summary>
    public readonly struct TickId : IEquatable<TickId>, IComparable<TickId>
    {
        public TickId(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
        }

        public long Value { get; }

        public bool Equals(TickId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is TickId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public int CompareTo(TickId other) => Value.CompareTo(other.Value);

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// 不可变世界时钟。首版采用 12 月、每月 30 日的经济历，后续历法通过版本 ID 扩展。
    /// </summary>
    public sealed class WorldClock
    {
        public const int DefaultMonthCount = 12;
        public const int DefaultDaysPerMonth = 30;

        public WorldClock(
            long dayIndex,
            long tickSequence,
            int economicYear,
            int month,
            int day,
            StableId calendarDefinitionId,
            long lastMonthCloseTick = 0,
            long lastSeasonCloseTick = 0,
            long lastYearCloseTick = 0)
        {
            if (dayIndex < 0 || tickSequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dayIndex));
            }

            if (economicYear < 1 || month < 1 || month > DefaultMonthCount || day < 1 || day > DefaultDaysPerMonth)
            {
                throw new ArgumentOutOfRangeException(nameof(economicYear), "The development calendar requires a valid year, month and day.");
            }

            SimulationNode.RequireId(calendarDefinitionId, nameof(calendarDefinitionId));
            DayIndex = dayIndex;
            TickSequence = tickSequence;
            EconomicYear = economicYear;
            Month = month;
            Day = day;
            CalendarDefinitionId = calendarDefinitionId;
            LastMonthCloseTick = lastMonthCloseTick;
            LastSeasonCloseTick = lastSeasonCloseTick;
            LastYearCloseTick = lastYearCloseTick;
        }

        public long DayIndex { get; }

        public long TickSequence { get; }

        public int EconomicYear { get; }

        public int Month { get; }

        public int Day { get; }

        public StableId CalendarDefinitionId { get; }

        public long LastMonthCloseTick { get; }

        public long LastSeasonCloseTick { get; }

        public long LastYearCloseTick { get; }

        /// <summary>计算下一日候选时钟及周期闭合标记，不修改当前实例。</summary>
        public WorldClockAdvance NextDay()
        {
            var nextTick = checked(TickSequence + 1);
            var nextDayIndex = checked(DayIndex + 1);
            var nextDay = Day + 1;
            var nextMonth = Month;
            var nextYear = EconomicYear;
            var closeFlags = PeriodCloseFlags.Day;

            if (nextDay > DefaultDaysPerMonth)
            {
                nextDay = 1;
                nextMonth++;
                closeFlags |= PeriodCloseFlags.Month;
                if (Month % 3 == 0)
                {
                    closeFlags |= PeriodCloseFlags.Season;
                }

                if (nextMonth > DefaultMonthCount)
                {
                    nextMonth = 1;
                    nextYear++;
                    closeFlags |= PeriodCloseFlags.Year;
                }
            }

            var next = new WorldClock(
                nextDayIndex,
                nextTick,
                nextYear,
                nextMonth,
                nextDay,
                CalendarDefinitionId,
                (closeFlags & PeriodCloseFlags.Month) != 0 ? nextTick : LastMonthCloseTick,
                (closeFlags & PeriodCloseFlags.Season) != 0 ? nextTick : LastSeasonCloseTick,
                (closeFlags & PeriodCloseFlags.Year) != 0 ? nextTick : LastYearCloseTick);
            return new WorldClockAdvance(next, closeFlags);
        }
    }

    /// <summary>一次时钟计算的不可变返回值。</summary>
    public sealed class WorldClockAdvance
    {
        public WorldClockAdvance(WorldClock clock, PeriodCloseFlags closeFlags)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            CloseFlags = closeFlags;
        }

        public WorldClock Clock { get; }

        public PeriodCloseFlags CloseFlags { get; }
    }

    /// <summary>
    /// 约束 Definition、模块目录、状态载荷、初始化算法和随机算法的兼容性清单。
    /// </summary>
    public sealed class RulesetManifest
    {
        public RulesetManifest(
            string rulesetVersion,
            string moduleCatalogVersion,
            string stateSchemaVersion,
            string definitionContentHash,
            string initializationAlgorithmVersion,
            string randomAlgorithmVersion,
            bool commercialReleaseReady)
        {
            RulesetVersion = RequireText(rulesetVersion, nameof(rulesetVersion));
            ModuleCatalogVersion = RequireText(moduleCatalogVersion, nameof(moduleCatalogVersion));
            StateSchemaVersion = RequireText(stateSchemaVersion, nameof(stateSchemaVersion));
            DefinitionContentHash = RequireText(definitionContentHash, nameof(definitionContentHash));
            InitializationAlgorithmVersion = RequireText(initializationAlgorithmVersion, nameof(initializationAlgorithmVersion));
            RandomAlgorithmVersion = RequireText(randomAlgorithmVersion, nameof(randomAlgorithmVersion));
            CommercialReleaseReady = commercialReleaseReady;
        }

        public string RulesetVersion { get; }

        public string ModuleCatalogVersion { get; }

        public string StateSchemaVersion { get; }

        public string DefinitionContentHash { get; }

        public string InitializationAlgorithmVersion { get; }

        public string RandomAlgorithmVersion { get; }

        public bool CommercialReleaseReady { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A manifest value is required.", parameterName);
            }

            return value;
        }
    }

    /// <summary>规范序列化后的 SHA-256 世界状态指纹。</summary>
    public readonly struct StateHash : IEquatable<StateHash>
    {
        public StateHash(string sha256)
        {
            if (sha256 == null || sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException("A state hash must be a 64-character SHA-256 value.", nameof(sha256));
            }

            Sha256 = sha256.ToLowerInvariant();
        }

        public string Sha256 { get; }

        public bool Equals(StateHash other) => string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is StateHash other && Equals(other);

        public override int GetHashCode() => Sha256 == null ? 0 : StringComparer.Ordinal.GetHashCode(Sha256);

        public override string ToString() => Sha256 ?? string.Empty;
    }

    /// <summary>
    /// 确定性随机流的完整寻址键。相同世界、Tick、节点、模块、用途和实体得到相同随机序列。
    /// </summary>
    public sealed class RandomStreamDescriptor
    {
        public RandomStreamDescriptor(
            WorldSeed worldSeed,
            TickId tickId,
            StableId nodeId,
            StableId moduleId,
            string purpose,
            StableId entityId,
            string algorithmVersion = "pcg32-v1")
        {
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            SimulationNode.RequireId(moduleId, nameof(moduleId));
            SimulationNode.RequireId(entityId, nameof(entityId));
            if (string.IsNullOrWhiteSpace(purpose))
            {
                throw new ArgumentException("A random stream purpose is required.", nameof(purpose));
            }

            WorldSeed = worldSeed;
            TickId = tickId;
            NodeId = nodeId;
            ModuleId = moduleId;
            Purpose = purpose;
            EntityId = entityId;
            AlgorithmVersion = algorithmVersion;
        }

        public WorldSeed WorldSeed { get; }
        public TickId TickId { get; }
        public StableId NodeId { get; }
        public StableId ModuleId { get; }
        public string Purpose { get; }
        public StableId EntityId { get; }
        public string AlgorithmVersion { get; }
    }

    /// <summary>只指向闭合 Tick 的可验证检查点。</summary>
    public sealed class WorldCheckpoint
    {
        public WorldCheckpoint(StableId checkpointId, TickId tickId, StateHash stateHash, DateTime createdUtc)
        {
            SimulationNode.RequireId(checkpointId, nameof(checkpointId));
            if (createdUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Checkpoint timestamps must use UTC.", nameof(createdUtc));
            }

            CheckpointId = checkpointId;
            TickId = tickId;
            StateHash = stateHash;
            CreatedUtc = createdUtc;
        }

        public StableId CheckpointId { get; }
        public TickId TickId { get; }
        public StateHash StateHash { get; }
        public DateTime CreatedUtc { get; }
    }

    /// <summary>从只读 Definition 数据库加载的世界拓扑、规则清单和模块组合。</summary>
    public sealed class WorldDefinition
    {
        public WorldDefinition(
            StableId worldId,
            RulesetManifest manifest,
            WorldTopology topology,
            IEnumerable<NodeModuleComposition> moduleCompositions)
        {
            SimulationNode.RequireId(worldId, nameof(worldId));
            WorldId = worldId;
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            ModuleCompositions = new ReadOnlyCollection<NodeModuleComposition>((moduleCompositions ?? throw new ArgumentNullException(nameof(moduleCompositions)))
                .OrderBy(composition => composition.NodeId.Value, StringComparer.Ordinal)
                .ThenBy(composition => composition.ModuleDefinitionId.Value, StringComparer.Ordinal)
                .ToList());
        }

        public StableId WorldId { get; }
        public RulesetManifest Manifest { get; }
        public WorldTopology Topology { get; }
        public IReadOnlyList<NodeModuleComposition> ModuleCompositions { get; }
    }

}
