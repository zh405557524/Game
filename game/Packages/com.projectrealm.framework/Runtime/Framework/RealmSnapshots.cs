using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.Framework
{
    /// <summary>Presentation 可读取的当前世界摘要，不暴露 WorldRuntime。</summary>
    public sealed class WorldSessionSnapshot
    {
        public WorldSessionSnapshot(
            bool hasActiveWorld,
            string saveId,
            string worldId,
            long tick,
            int year,
            int month,
            int day,
            string stateHash,
            int geographicNodeCount,
            int moduleInstanceCount,
            int scaffoldModuleCount,
            string dataQuality,
            bool commercialReleaseReady)
        {
            HasActiveWorld = hasActiveWorld;
            SaveId = saveId ?? string.Empty;
            WorldId = worldId ?? string.Empty;
            Tick = tick;
            Year = year;
            Month = month;
            Day = day;
            StateHash = stateHash ?? string.Empty;
            GeographicNodeCount = geographicNodeCount;
            ModuleInstanceCount = moduleInstanceCount;
            ScaffoldModuleCount = scaffoldModuleCount;
            DataQuality = dataQuality ?? string.Empty;
            CommercialReleaseReady = commercialReleaseReady;
        }

        public bool HasActiveWorld { get; }
        public string SaveId { get; }
        public string WorldId { get; }
        public long Tick { get; }
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }
        public string StateHash { get; }
        public int GeographicNodeCount { get; }
        public int ModuleInstanceCount { get; }
        public int ScaffoldModuleCount { get; }
        public string DataQuality { get; }
        public bool CommercialReleaseReady { get; }

        public static WorldSessionSnapshot Empty() =>
            new WorldSessionSnapshot(false, string.Empty, string.Empty, 0, 0, 0, 0, string.Empty, 0, 0, 0, "Unknown", false);
    }

    /// <summary>一次显式日/月/季/年推进完成后的只读结果。</summary>
    public sealed class SimulationStepSnapshot
    {
        public SimulationStepSnapshot(
            RealmAdvanceUnit unit,
            bool committed,
            long tick,
            string stateHash,
            string failureReason,
            int executedStageCount,
            int moduleResultCount,
            string dataQuality)
        {
            Unit = unit;
            Committed = committed;
            Tick = tick;
            StateHash = stateHash ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            ExecutedStageCount = executedStageCount;
            ModuleResultCount = moduleResultCount;
            DataQuality = dataQuality ?? string.Empty;
        }

        public RealmAdvanceUnit Unit { get; }
        public bool Committed { get; }
        public long Tick { get; }
        public string StateHash { get; }
        public string FailureReason { get; }
        public int ExecutedStageCount { get; }
        public int ModuleResultCount { get; }
        public string DataQuality { get; }
    }

    /// <summary>命令被队列接收时返回的票据；接收不代表业务执行成功。</summary>
    public sealed class CommandTicketSnapshot
    {
        public CommandTicketSnapshot(string commandInstanceId, string status, string reasonCode)
        {
            CommandInstanceId = commandInstanceId ?? string.Empty;
            Status = status ?? string.Empty;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public string CommandInstanceId { get; }
        public string Status { get; }
        public string ReasonCode { get; }
    }

    /// <summary>主菜单显示的存档槽位。</summary>
    public sealed class SaveSlotSnapshot
    {
        public SaveSlotSnapshot(string saveId)
        {
            SaveId = saveId ?? string.Empty;
        }

        public string SaveId { get; }
    }

    public sealed class RealmStageSnapshot
    {
        public RealmStageSnapshot(string stage, int executionCount, bool succeeded, string failureCode)
        {
            Stage = stage ?? string.Empty;
            ExecutionCount = executionCount;
            Succeeded = succeeded;
            FailureCode = failureCode ?? string.Empty;
        }

        public string Stage { get; }
        public int ExecutionCount { get; }
        public bool Succeeded { get; }
        public string FailureCode { get; }
    }

    public sealed class RealmNodeSummary
    {
        public RealmNodeSummary(string id, string displayName, string kind)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Kind { get; }
    }

    public sealed class RealmModuleSummary
    {
        public RealmModuleSummary(string instanceId, string definitionId, string nodeId, string lifecycle, string implementationTier)
        {
            InstanceId = instanceId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            Lifecycle = lifecycle ?? string.Empty;
            ImplementationTier = implementationTier ?? string.Empty;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string NodeId { get; }
        public string Lifecycle { get; }
        public string ImplementationTier { get; }
    }

    /// <summary>Framework Inspector 与开发 UI 共用的不可变诊断投影。</summary>
    public sealed class RealmDiagnosticsSnapshot
    {
        public RealmDiagnosticsSnapshot(
            WorldSessionSnapshot world,
            int factionCount,
            int jurisdictionCount,
            int commandCount,
            int reservationCount,
            int eventCount,
            int checkpointCount,
            IEnumerable<RealmStageSnapshot> stages,
            IEnumerable<RealmNodeSummary> nodes,
            IEnumerable<RealmModuleSummary> modules,
            string latestFailure)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            FactionCount = factionCount;
            JurisdictionCount = jurisdictionCount;
            CommandCount = commandCount;
            ReservationCount = reservationCount;
            EventCount = eventCount;
            CheckpointCount = checkpointCount;
            Stages = new ReadOnlyCollection<RealmStageSnapshot>((stages ?? Array.Empty<RealmStageSnapshot>()).ToList());
            Nodes = new ReadOnlyCollection<RealmNodeSummary>((nodes ?? Array.Empty<RealmNodeSummary>()).ToList());
            Modules = new ReadOnlyCollection<RealmModuleSummary>((modules ?? Array.Empty<RealmModuleSummary>()).ToList());
            LatestFailure = latestFailure ?? string.Empty;
        }

        public WorldSessionSnapshot World { get; }
        public int FactionCount { get; }
        public int JurisdictionCount { get; }
        public int CommandCount { get; }
        public int ReservationCount { get; }
        public int EventCount { get; }
        public int CheckpointCount { get; }
        public IReadOnlyList<RealmStageSnapshot> Stages { get; }
        public IReadOnlyList<RealmNodeSummary> Nodes { get; }
        public IReadOnlyList<RealmModuleSummary> Modules { get; }
        public string LatestFailure { get; }

        public static RealmDiagnosticsSnapshot Empty() =>
            new RealmDiagnosticsSnapshot(WorldSessionSnapshot.Empty(), 0, 0, 0, 0, 0, 0, null, null, null, string.Empty);
    }
}
