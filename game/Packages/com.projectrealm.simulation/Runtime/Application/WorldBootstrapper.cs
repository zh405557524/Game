using System;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    /// <summary>
    /// 新建和读取世界的应用服务。旧构造函数与旧 StartNewWorld API 被保留，
    /// 新调用方应提供 Definition Store 与 Save Store。
    /// </summary>
    public sealed class WorldBootstrapper
    {
        private readonly IWorldDefinitionReader _worldDefinitions;
        private readonly IWorldDefinitionStore _definitionStore;
        private readonly ISaveGameStore _saveGameStore;
        private readonly IModuleExecutorFactory _executorFactory;
        private readonly ISimulationDiagnosticsSink _diagnostics;

        public WorldBootstrapper(IWorldDefinitionReader worldDefinitions)
        {
            _worldDefinitions = worldDefinitions ?? throw new ArgumentNullException(nameof(worldDefinitions));
            _definitionStore = worldDefinitions as IWorldDefinitionStore;
            _executorFactory = new DefaultModuleExecutorFactory();
            _diagnostics = new NullSimulationDiagnosticsSink();
        }

        public WorldBootstrapper(
            IWorldDefinitionStore worldDefinitions,
            ISaveGameStore saveGameStore,
            IModuleExecutorFactory executorFactory = null,
            ISimulationDiagnosticsSink diagnostics = null)
        {
            _worldDefinitions = worldDefinitions ?? throw new ArgumentNullException(nameof(worldDefinitions));
            _definitionStore = worldDefinitions;
            _saveGameStore = saveGameStore ?? throw new ArgumentNullException(nameof(saveGameStore));
            _executorFactory = executorFactory ?? new DefaultModuleExecutorFactory();
            _diagnostics = diagnostics ?? new NullSimulationDiagnosticsSink();
        }

        /// <summary>兼容旧公开 API，并把实际推进委托给新的 WorldRuntime。</summary>
        public SimulationSession StartNewWorld(StableId worldId, WorldSeed worldSeed)
        {
            if (!_worldDefinitions.ContainsWorld(worldId))
            {
                throw new InvalidOperationException($"World definition '{worldId}' is unavailable.");
            }

            var definition = _definitionStore == null
                ? WorldRuntimeFactory.CreateMinimalDefinition(worldId)
                : _definitionStore.LoadWorld(worldId);
            var runtime = WorldRuntimeFactory.CreateNew(
                new WorldBootstrapRequest(new StableId("save.legacy." + worldId.Value), worldId, worldSeed),
                definition,
                _saveGameStore,
                _executorFactory,
                _diagnostics);
            return new SimulationSession(worldId, worldSeed, runtime);
        }

        /// <summary>从只读 Definition 创建尚未保存的新世界。</summary>
        public WorldRuntime StartNewWorld(WorldBootstrapRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_definitionStore == null)
            {
                throw new InvalidOperationException("A full Definition store is required by the framework bootstrap API.");
            }

            if (!_definitionStore.ContainsWorld(request.WorldId))
            {
                throw new InvalidOperationException($"World definition '{request.WorldId}' is unavailable.");
            }

            if (_saveGameStore != null && _saveGameStore.Exists(request.SaveId))
            {
                throw new InvalidOperationException($"Save '{request.SaveId}' already exists.");
            }

            return WorldRuntimeFactory.CreateNew(
                request,
                _definitionStore.LoadWorld(request.WorldId),
                _saveGameStore,
                _executorFactory,
                _diagnostics);
        }

        /// <summary>读取 Save，并用匹配的 Definition 验证后恢复运行时。</summary>
        public WorldRuntime LoadWorld(LoadWorldRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_definitionStore == null || _saveGameStore == null)
            {
                throw new InvalidOperationException("Definition and save-game stores are required to load a world.");
            }

            var save = _saveGameStore.Load(request.SaveId);
            var definition = _definitionStore.LoadWorld(save.Manifest.WorldId);
            return WorldRuntimeFactory.Restore(save, definition, _saveGameStore, _executorFactory, _diagnostics);
        }
    }
}
