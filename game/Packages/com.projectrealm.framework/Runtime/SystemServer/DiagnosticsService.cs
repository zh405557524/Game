using System;
using System.Linq;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>从活动运行时构建只读诊断 DTO，不把 WorldRuntime 泄露到 UI。</summary>
    internal sealed class DiagnosticsService : RealmServiceBase, IDiagnosticsManagerGateway
    {
        private readonly WorldService _world;
        private readonly SimulationDiagnosticsQuery _query = new SimulationDiagnosticsQuery();

        public DiagnosticsService(WorldService world)
            : base("DiagnosticsService")
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public RealmResult<RealmDiagnosticsSnapshot> Query(string search, int page, int pageSize)
        {
            if (!_world.HasActiveWorld)
            {
                return RealmResult<RealmDiagnosticsSnapshot>.Success(RealmDiagnosticsSnapshot.Empty());
            }

            try
            {
                var runtime = _world.RequireRuntime();
                var source = _query.Query(runtime, search, page, pageSize);
                var latest = source.LatestTick;
                var stages = latest == null
                    ? Array.Empty<RealmStageSnapshot>()
                    : latest.Stages.Select(stage => new RealmStageSnapshot(
                        stage.Stage.ToString(),
                        stage.ModuleExecutionCount,
                        stage.Succeeded,
                        stage.ReasonCode)).ToArray();
                var nodes = source.Nodes.Select(node =>
                    new RealmNodeSummary(node.NodeId.Value, node.DisplayName, node.Kind.ToString()));
                var modules = source.Modules.Select(module => new RealmModuleSummary(
                    module.InstanceId.Value,
                    module.DefinitionId.Value,
                    module.NodeId.Value,
                    module.LifecycleState.ToString(),
                    runtime.ModuleCatalog.GetRequired(module.DefinitionId).ImplementationTier.ToString()));
                var snapshot = new RealmDiagnosticsSnapshot(
                    WorldService.BuildSnapshot(runtime),
                    source.FactionCount,
                    source.JurisdictionCount,
                    runtime.Commands.Count,
                    runtime.Reservations.Count,
                    runtime.Events.Count,
                    runtime.Checkpoints.Count,
                    stages,
                    nodes,
                    modules,
                    latest?.FailureReason);
                return RealmResult<RealmDiagnosticsSnapshot>.Success(snapshot);
            }
            catch (Exception exception)
            {
                var error = RealmErrorMapper.FromException(exception);
                return RealmResult<RealmDiagnosticsSnapshot>.Failure(error.Code, error.Message, error.Kind);
            }
        }
    }
}
