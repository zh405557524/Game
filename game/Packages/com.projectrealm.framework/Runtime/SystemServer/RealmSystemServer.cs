using System;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>
    /// 类似 Android system_server 的纯 C# 进程内服务宿主。它是唯一 Composition Graph，
    /// 负责服务顺序、Context/Manager 代理以及应用状态；不绘制地图、不读取输入。
    /// </summary>
    public sealed class RealmSystemServer
    {
        private readonly RealmEventStream _events;
        private readonly RealmApplicationStateMachine _applicationState;
        private readonly RealmServiceRegistry _services;
        private bool _started;

        public RealmSystemServer(
            IWorldDefinitionStore definitions,
            ISaveGameStore saves,
            IRealmSceneNavigator sceneNavigator,
            IModuleExecutorFactory executorFactory = null,
            ISimulationDiagnosticsSink diagnostics = null)
        {
            _events = new RealmEventStream();
            _applicationState = new RealmApplicationStateMachine(_events);

            var world = new WorldService(definitions, saves, _applicationState, _events, executorFactory, diagnostics);
            var simulation = new SimulationService(world, _applicationState, _events);
            var save = new SaveService(world, _applicationState, _events);
            var diagnostic = new DiagnosticsService(world);
            var navigation = new NavigationService(sceneNavigator);
            _services = new RealmServiceRegistry(new IRealmService[]
            {
                world,
                simulation,
                save,
                diagnostic,
                navigation
            });

            Context = new RealmContext(
                new WorldManager(world),
                new SimulationManager(simulation),
                new SaveManager(save),
                new NavigationManager(navigation),
                new DiagnosticsManager(diagnostic),
                _events);
        }

        public RealmApplicationState State => _applicationState.State;

        public IRealmContext Context { get; }

        /// <summary>按固定依赖顺序启动服务；失败时 Registry 会逆序停止已启动服务。</summary>
        public void Start()
        {
            if (_started)
            {
                throw new InvalidOperationException("RealmSystemServer has already started.");
            }

            _applicationState.Transition(RealmApplicationState.Booting, "system_server_start");
            try
            {
                _services.StartAll();
                _started = true;
                _applicationState.Transition(RealmApplicationState.MainMenu, "system_server_ready");
            }
            catch (Exception exception)
            {
                var error = RealmErrorMapper.FromException(exception);
                _applicationState.Fault(error.Code, error.Message);
                throw;
            }
        }

        /// <summary>逆序停止所有服务；不保存或隐式推进世界。</summary>
        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            if (_applicationState.State != RealmApplicationState.ShuttingDown)
            {
                _applicationState.Transition(RealmApplicationState.ShuttingDown, "system_server_stop");
            }

            _services.StopAll();
            _started = false;
        }
    }
}
