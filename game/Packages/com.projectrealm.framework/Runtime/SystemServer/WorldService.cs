using System;
using System.Linq;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.World;

namespace ProjectRealm.SystemServer
{
    /// <summary>
    /// System Server 中唯一拥有活动 WorldRuntime 的服务。新建、读取和关闭都在这里完成，
    /// Scene、Presenter 和 Manager 永远拿不到可写运行时对象。
    /// </summary>
    internal sealed class WorldService : RealmServiceBase, IWorldManagerGateway
    {
        private readonly IWorldDefinitionStore _definitions;
        private readonly ISaveGameStore _saves;
        private readonly IModuleExecutorFactory _executors;
        private readonly ISimulationDiagnosticsSink _diagnostics;
        private readonly RealmApplicationStateMachine _applicationState;
        private readonly RealmEventStream _events;

        public WorldService(
            IWorldDefinitionStore definitions,
            ISaveGameStore saves,
            RealmApplicationStateMachine applicationState,
            RealmEventStream events,
            IModuleExecutorFactory executors = null,
            ISimulationDiagnosticsSink diagnostics = null)
            : base("WorldService")
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _executors = executors ?? new DefaultModuleExecutorFactory();
            _diagnostics = diagnostics ?? new NullSimulationDiagnosticsSink();
        }

        public bool HasActiveWorld => Runtime != null;
        internal WorldRuntime Runtime { get; private set; }
        internal ISaveGameStore SaveStore => _saves;

        public RealmResult<WorldSessionSnapshot> GetCurrent()
        {
            if (Runtime == null)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("no_active_world", "No world is currently open.", RealmErrorKind.Conflict);
            }

            return RealmResult<WorldSessionSnapshot>.Success(BuildSnapshot(Runtime));
        }

        public RealmResult<WorldSessionSnapshot> Create(NewRealmWorldRequest request)
        {
            if (request == null)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("invalid_request", "A new-world request is required.", RealmErrorKind.Validation);
            }

            if (HasActiveWorld)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("world_already_open", "Close the active world before creating another.", RealmErrorKind.Conflict);
            }

            if (_applicationState.State != RealmApplicationState.MainMenu)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("invalid_application_state", "A world can only be created from the main menu.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.LoadingWorld, "create_world");
            try
            {
                var saveId = new StableId(request.SaveId);
                var worldId = new StableId(request.WorldId);
                if (!_definitions.ContainsWorld(worldId))
                {
                    _applicationState.Transition(RealmApplicationState.MainMenu, "definition_unavailable");
                    return RealmResult<WorldSessionSnapshot>.Failure(
                        "definition_unavailable",
                        $"World definition '{request.WorldId}' is unavailable. Run the Definition database builder first.",
                        RealmErrorKind.Unavailable);
                }

                if (_saves.Exists(saveId))
                {
                    _applicationState.Transition(RealmApplicationState.MainMenu, "save_already_exists");
                    return RealmResult<WorldSessionSnapshot>.Failure("save_already_exists", $"Save '{request.SaveId}' already exists.", RealmErrorKind.Conflict);
                }

                Runtime = WorldRuntimeFactory.CreateNew(
                    new WorldBootstrapRequest(saveId, worldId, new WorldSeed(request.WorldSeed)),
                    _definitions.LoadWorld(worldId),
                    _saves,
                    _executors,
                    _diagnostics);
                var snapshot = BuildSnapshot(Runtime);
                _applicationState.Transition(RealmApplicationState.Running, "world_created");
                _events.Publish(new RealmWorldOpenedEvent(snapshot, false));
                return RealmResult<WorldSessionSnapshot>.Success(snapshot);
            }
            catch (Exception exception)
            {
                Runtime = null;
                return FailLoading(exception);
            }
        }

        public RealmResult Close()
        {
            if (Runtime == null)
            {
                return RealmResult.Failure("no_active_world", "No world is currently open.", RealmErrorKind.Conflict);
            }

            if (_applicationState.State != RealmApplicationState.Running && _applicationState.State != RealmApplicationState.Paused)
            {
                return RealmResult.Failure("invalid_application_state", "The active world cannot be closed in the current state.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.UnloadingWorld, "close_world");
            var saveId = Runtime.SaveId.Value;
            Runtime = null;
            _applicationState.Transition(RealmApplicationState.MainMenu, "world_closed");
            _events.Publish(new RealmWorldClosedEvent(saveId));
            return RealmResult.Success();
        }

        internal RealmResult<WorldSessionSnapshot> Load(string saveIdValue)
        {
            if (string.IsNullOrWhiteSpace(saveIdValue))
            {
                return RealmResult<WorldSessionSnapshot>.Failure("invalid_save_id", "A save ID is required.", RealmErrorKind.Validation);
            }

            if (HasActiveWorld)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("world_already_open", "Close the active world before loading another.", RealmErrorKind.Conflict);
            }

            if (_applicationState.State != RealmApplicationState.MainMenu)
            {
                return RealmResult<WorldSessionSnapshot>.Failure("invalid_application_state", "A world can only be loaded from the main menu.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.LoadingWorld, "load_world");
            try
            {
                var save = _saves.Load(new StableId(saveIdValue));
                var definition = _definitions.LoadWorld(save.Manifest.WorldId);
                Runtime = WorldRuntimeFactory.Restore(save, definition, _saves, _executors, _diagnostics);
                var snapshot = BuildSnapshot(Runtime);
                _applicationState.Transition(RealmApplicationState.Running, "world_loaded");
                _events.Publish(new RealmWorldOpenedEvent(snapshot, true));
                return RealmResult<WorldSessionSnapshot>.Success(snapshot);
            }
            catch (Exception exception)
            {
                Runtime = null;
                return FailLoading(exception);
            }
        }

        internal WorldRuntime RequireRuntime()
        {
            return Runtime ?? throw new InvalidOperationException("No active WorldRuntime exists.");
        }

        internal static WorldSessionSnapshot BuildSnapshot(WorldRuntime runtime)
        {
            var scaffoldCount = runtime.ModuleRegistry.Instances.Count(instance =>
                runtime.ModuleCatalog.GetRequired(instance.DefinitionId).ImplementationTier == ModuleImplementationTier.Scaffold);
            return new WorldSessionSnapshot(
                true,
                runtime.SaveId.Value,
                runtime.WorldId.Value,
                runtime.Clock.TickSequence,
                runtime.Clock.EconomicYear,
                runtime.Clock.Month,
                runtime.Clock.Day,
                runtime.CurrentStateHash.Sha256,
                runtime.Topology.Geography.Nodes.Count,
                runtime.ModuleRegistry.Instances.Count,
                scaffoldCount,
                scaffoldCount > 0 ? DataQuality.Unavailable.ToString() : DataQuality.Unknown.ToString(),
                runtime.Ruleset.CommercialReleaseReady);
        }

        protected override void OnStop()
        {
            Runtime = null;
        }

        private RealmResult<WorldSessionSnapshot> FailLoading(Exception exception)
        {
            var error = RealmErrorMapper.FromException(exception);
            if (error.Kind == RealmErrorKind.Fatal)
            {
                _applicationState.Fault(error.Code, error.Message);
            }
            else
            {
                _applicationState.Transition(RealmApplicationState.MainMenu, error.Code);
            }

            return RealmResult<WorldSessionSnapshot>.Failure(error.Code, error.Message, error.Kind);
        }
    }
}
