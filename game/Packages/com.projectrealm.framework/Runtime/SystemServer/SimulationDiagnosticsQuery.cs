using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.World;

namespace ProjectRealm.SystemServer
{
    /// <summary>Framework Inspector 使用的不可变诊断投影。</summary>
    internal sealed class SimulationDiagnosticsSnapshot
    {
        public SimulationDiagnosticsSnapshot(
            WorldClock clock,
            StateHash stateHash,
            int geographicNodeCount,
            int factionCount,
            int jurisdictionCount,
            int moduleInstanceCount,
            int scaffoldModuleCount,
            IReadOnlyList<RegionNode> nodes,
            IReadOnlyList<ModuleInstance> modules,
            WorldTickResult latestTick)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            StateHash = stateHash;
            GeographicNodeCount = geographicNodeCount;
            FactionCount = factionCount;
            JurisdictionCount = jurisdictionCount;
            ModuleInstanceCount = moduleInstanceCount;
            ScaffoldModuleCount = scaffoldModuleCount;
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            LatestTick = latestTick;
        }

        public WorldClock Clock { get; }
        public StateHash StateHash { get; }
        public int GeographicNodeCount { get; }
        public int FactionCount { get; }
        public int JurisdictionCount { get; }
        public int ModuleInstanceCount { get; }
        public int ScaffoldModuleCount { get; }
        public IReadOnlyList<RegionNode> Nodes { get; }
        public IReadOnlyList<ModuleInstance> Modules { get; }
        public WorldTickResult LatestTick { get; }
    }

    /// <summary>
    /// 只读查询世界运行状态。查询不会推进时间、修改状态或消耗随机流。
    /// </summary>
    internal sealed class SimulationDiagnosticsQuery
    {
        /// <summary>按同一搜索词分别分页节点和模块，单页最多返回 500 项。</summary>
        public SimulationDiagnosticsSnapshot Query(WorldRuntime runtime, string search = null, int page = 0, int pageSize = 50)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (page < 0 || pageSize < 1 || pageSize > 500)
            {
                throw new ArgumentOutOfRangeException(nameof(page));
            }

            var normalizedSearch = search ?? string.Empty;
            var nodes = runtime.Topology.Geography.Nodes
                .Where(node => Matches(node.NodeId.Value, node.DisplayName, normalizedSearch))
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();
            var modules = runtime.ModuleRegistry.Instances
                .Where(instance => Matches(instance.InstanceId.Value, instance.DefinitionId.Value, normalizedSearch))
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();
            var scaffoldCount = runtime.ModuleRegistry.Instances.Count(instance =>
                runtime.ModuleCatalog.GetRequired(instance.DefinitionId).ImplementationTier == ModuleImplementationTier.Scaffold);
            return new SimulationDiagnosticsSnapshot(
                runtime.Clock,
                runtime.CurrentStateHash,
                runtime.Topology.Geography.Nodes.Count,
                runtime.Topology.Factions.Nodes.Count,
                runtime.Topology.Jurisdictions.Relations.Count,
                runtime.ModuleRegistry.Instances.Count,
                scaffoldCount,
                new ReadOnlyCollection<RegionNode>(nodes),
                new ReadOnlyCollection<ModuleInstance>(modules),
                runtime.TickHistory.LastOrDefault());
        }

        private static bool Matches(string first, string second, string search)
        {
            return string.IsNullOrEmpty(search) ||
                   first.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   second.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
