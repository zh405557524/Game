using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.Domain
{
    /// <summary>模块实例从注册到移除的受控生命周期。</summary>
    public enum ModuleLifecycleState
    {
        Registered,
        Initializing,
        Active,
        Degraded,
        Suspended,
        Retiring,
        Removed,
        Failed
    }

    /// <summary>区分脚手架、试验实现和生产实现，避免把空跑模块误认为玩法完成。</summary>
    public enum ModuleImplementationTier
    {
        Scaffold,
        Experimental,
        Production
    }

    /// <summary>模块对某能力是权威提供者、贡献者还是只读观察者。</summary>
    public enum CapabilityAuthorityMode
    {
        Authoritative,
        Contributor,
        Observer
    }

    /// <summary>模块能力及其 authority key 的声明。</summary>
    public sealed class CapabilityContract
    {
        public CapabilityContract(
            StableId capabilityId,
            StableId authorityKey,
            CapabilityAuthorityMode authorityMode,
            bool required)
        {
            SimulationNode.RequireId(capabilityId, nameof(capabilityId));
            SimulationNode.RequireId(authorityKey, nameof(authorityKey));
            CapabilityId = capabilityId;
            AuthorityKey = authorityKey;
            AuthorityMode = authorityMode;
            Required = required;
        }

        public StableId CapabilityId { get; }
        public StableId AuthorityKey { get; }
        public CapabilityAuthorityMode AuthorityMode { get; }
        public bool Required { get; }
    }

    /// <summary>
    /// 目录层模块定义，包含版本、能力、阶段和依赖；不保存节点运行状态。
    /// </summary>
    public sealed class ModuleDefinition
    {
        public ModuleDefinition(
            StableId definitionId,
            string sourceName,
            string implementationVersion,
            ModuleImplementationTier implementationTier,
            IEnumerable<CapabilityContract> capabilities,
            IEnumerable<WorldExecutionStage> stages,
            IEnumerable<StableId> hardDependencyIds = null,
            IEnumerable<StableId> optionalDependencyIds = null,
            string sourceDocument = null)
        {
            SimulationNode.RequireId(definitionId, nameof(definitionId));
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException("A module definition requires its source name.", nameof(sourceName));
            }

            if (string.IsNullOrWhiteSpace(implementationVersion))
            {
                throw new ArgumentException("A module implementation version is required.", nameof(implementationVersion));
            }

            DefinitionId = definitionId;
            SourceName = sourceName;
            ImplementationVersion = implementationVersion;
            ImplementationTier = implementationTier;
            Capabilities = CopyUniqueCapabilities(capabilities);
            Stages = CopyUniqueStages(stages);
            HardDependencyIds = CopyUniqueIds(hardDependencyIds);
            OptionalDependencyIds = CopyUniqueIds(optionalDependencyIds);
            SourceDocument = sourceDocument ?? string.Empty;
        }

        public StableId DefinitionId { get; }
        public string SourceName { get; }
        public string ImplementationVersion { get; }
        public ModuleImplementationTier ImplementationTier { get; }
        public IReadOnlyList<CapabilityContract> Capabilities { get; }
        public IReadOnlyList<WorldExecutionStage> Stages { get; }
        public IReadOnlyList<StableId> HardDependencyIds { get; }
        public IReadOnlyList<StableId> OptionalDependencyIds { get; }
        public string SourceDocument { get; }

        private static IReadOnlyList<CapabilityContract> CopyUniqueCapabilities(IEnumerable<CapabilityContract> source)
        {
            var result = (source ?? throw new ArgumentNullException(nameof(source))).ToList();
            if (result.Any(item => item == null) || result.Select(item => item.CapabilityId).Distinct().Count() != result.Count)
            {
                throw new InvalidOperationException("Module capability IDs must be non-null and unique.");
            }

            return new ReadOnlyCollection<CapabilityContract>(result
                .OrderBy(item => item.CapabilityId.Value, StringComparer.Ordinal)
                .ToList());
        }

        private static IReadOnlyList<WorldExecutionStage> CopyUniqueStages(IEnumerable<WorldExecutionStage> source)
        {
            var result = (source ?? throw new ArgumentNullException(nameof(source))).Distinct().OrderBy(stage => (int)stage).ToList();
            if (result.Count == 0)
            {
                throw new InvalidOperationException("A module must participate in at least one execution stage.");
            }

            return new ReadOnlyCollection<WorldExecutionStage>(result);
        }

        private static IReadOnlyList<StableId> CopyUniqueIds(IEnumerable<StableId> source)
        {
            var result = (source ?? Array.Empty<StableId>()).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToList();
            foreach (var id in result)
            {
                SimulationNode.RequireId(id, nameof(source));
            }

            return new ReadOnlyCollection<StableId>(result);
        }
    }

    /// <summary>Definition 数据库中“节点加载哪个模块定义”的组合行。</summary>
    public sealed class NodeModuleComposition
    {
        public NodeModuleComposition(StableId nodeId, StableId moduleDefinitionId)
        {
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            SimulationNode.RequireId(moduleDefinitionId, nameof(moduleDefinitionId));
            NodeId = nodeId;
            ModuleDefinitionId = moduleDefinitionId;
        }

        public StableId NodeId { get; }
        public StableId ModuleDefinitionId { get; }
    }

    /// <summary>某个模块定义在具体世界节点上的运行实例。</summary>
    public sealed class ModuleInstance
    {
        public ModuleInstance(
            StableId instanceId,
            StableId definitionId,
            StableId nodeId,
            ModuleLifecycleState lifecycleState = ModuleLifecycleState.Registered)
        {
            SimulationNode.RequireId(instanceId, nameof(instanceId));
            SimulationNode.RequireId(definitionId, nameof(definitionId));
            SimulationNode.RequireId(nodeId, nameof(nodeId));
            InstanceId = instanceId;
            DefinitionId = definitionId;
            NodeId = nodeId;
            LifecycleState = lifecycleState;
        }

        public StableId InstanceId { get; }
        public StableId DefinitionId { get; }
        public StableId NodeId { get; }
        public ModuleLifecycleState LifecycleState { get; private set; }

        /// <summary>只允许通过安全状态边迁移，非法跳转立即失败。</summary>
        public void TransitionTo(ModuleLifecycleState next)
        {
            if (!IsValidTransition(LifecycleState, next))
            {
                throw new InvalidOperationException($"Module '{InstanceId}' cannot transition from {LifecycleState} to {next}.");
            }

            LifecycleState = next;
        }

        private static bool IsValidTransition(ModuleLifecycleState current, ModuleLifecycleState next)
        {
            if (next == ModuleLifecycleState.Failed && current != ModuleLifecycleState.Removed)
            {
                return true;
            }

            switch (current)
            {
                case ModuleLifecycleState.Registered:
                    return next == ModuleLifecycleState.Initializing || next == ModuleLifecycleState.Removed;
                case ModuleLifecycleState.Initializing:
                    return next == ModuleLifecycleState.Active || next == ModuleLifecycleState.Degraded;
                case ModuleLifecycleState.Active:
                    return next == ModuleLifecycleState.Degraded || next == ModuleLifecycleState.Suspended || next == ModuleLifecycleState.Retiring;
                case ModuleLifecycleState.Degraded:
                    return next == ModuleLifecycleState.Active || next == ModuleLifecycleState.Suspended || next == ModuleLifecycleState.Retiring;
                case ModuleLifecycleState.Suspended:
                    return next == ModuleLifecycleState.Active || next == ModuleLifecycleState.Retiring;
                case ModuleLifecycleState.Retiring:
                    return next == ModuleLifecycleState.Removed;
                default:
                    return false;
            }
        }
    }

    /// <summary>模块定义与兼容别名的只读目录，并校验依赖存在性和环。</summary>
    public sealed class ModuleCatalog
    {
        private readonly Dictionary<StableId, ModuleDefinition> _definitions;
        private readonly Dictionary<string, StableId> _aliases;

        public ModuleCatalog(IEnumerable<ModuleDefinition> definitions, IDictionary<string, StableId> aliases = null)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _definitions = new Dictionary<StableId, ModuleDefinition>();
            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || _definitions.ContainsKey(definition.DefinitionId) || !sourceNames.Add(definition.SourceName))
                {
                    throw new InvalidOperationException("Module definition IDs and source names must be unique.");
                }

                _definitions.Add(definition.DefinitionId, definition);
            }

            _aliases = new Dictionary<string, StableId>(StringComparer.Ordinal);
            foreach (var pair in aliases ?? new Dictionary<string, StableId>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !_definitions.ContainsKey(pair.Value) || !_aliases.TryAdd(pair.Key, pair.Value))
                {
                    throw new InvalidOperationException($"Invalid module alias '{pair.Key}'.");
                }
            }

            ValidateDependencyGraph();
            Definitions = new ReadOnlyCollection<ModuleDefinition>(_definitions.Values
                .OrderBy(definition => definition.DefinitionId.Value, StringComparer.Ordinal)
                .ToList());
            Aliases = new ReadOnlyDictionary<string, StableId>(_aliases);
        }

        public IReadOnlyList<ModuleDefinition> Definitions { get; }
        public IReadOnlyDictionary<string, StableId> Aliases { get; }

        public ModuleDefinition GetRequired(StableId definitionId)
        {
            if (!_definitions.TryGetValue(definitionId, out var definition))
            {
                throw new KeyNotFoundException($"Module definition '{definitionId}' does not exist.");
            }

            return definition;
        }

        public ModuleDefinition ResolveSourceName(string sourceName)
        {
            var direct = _definitions.Values.FirstOrDefault(definition => string.Equals(definition.SourceName, sourceName, StringComparison.Ordinal));
            if (direct != null)
            {
                return direct;
            }

            if (_aliases.TryGetValue(sourceName, out var definitionId))
            {
                return _definitions[definitionId];
            }

            throw new KeyNotFoundException($"Module source name or alias '{sourceName}' does not exist.");
        }

        private void ValidateDependencyGraph()
        {
            foreach (var definition in _definitions.Values)
            {
                foreach (var dependencyId in definition.HardDependencyIds.Concat(definition.OptionalDependencyIds))
                {
                    if (!_definitions.ContainsKey(dependencyId))
                    {
                        throw new InvalidOperationException($"Module '{definition.DefinitionId}' refers to missing dependency '{dependencyId}'.");
                    }
                }
            }

            var visiting = new HashSet<StableId>();
            var visited = new HashSet<StableId>();
            foreach (var definition in _definitions.Values)
            {
                Visit(definition, visiting, visited);
            }
        }

        private void Visit(ModuleDefinition definition, ISet<StableId> visiting, ISet<StableId> visited)
        {
            if (visited.Contains(definition.DefinitionId))
            {
                return;
            }

            if (!visiting.Add(definition.DefinitionId))
            {
                throw new InvalidOperationException($"Module dependency cycle detected at '{definition.DefinitionId}'.");
            }

            foreach (var dependencyId in definition.HardDependencyIds)
            {
                Visit(_definitions[dependencyId], visiting, visited);
            }

            visiting.Remove(definition.DefinitionId);
            visited.Add(definition.DefinitionId);
        }
    }

    /// <summary>
    /// 当前世界的模块实例表。构造时校验节点内硬依赖和唯一权威提供者约束。
    /// </summary>
    public sealed class ModuleRegistry
    {
        private readonly ModuleCatalog _catalog;
        private readonly Dictionary<StableId, ModuleInstance> _instances;

        public ModuleRegistry(ModuleCatalog catalog, IEnumerable<ModuleInstance> instances)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _instances = new Dictionary<StableId, ModuleInstance>();
            foreach (var instance in instances ?? throw new ArgumentNullException(nameof(instances)))
            {
                if (instance == null || _instances.ContainsKey(instance.InstanceId))
                {
                    throw new InvalidOperationException("Module instances must be non-null and uniquely identified.");
                }

                _catalog.GetRequired(instance.DefinitionId);
                _instances.Add(instance.InstanceId, instance);
            }

            ValidateNodeDependenciesAndAuthority();
            Instances = new ReadOnlyCollection<ModuleInstance>(_instances.Values
                .OrderBy(instance => instance.NodeId.Value, StringComparer.Ordinal)
                .ThenBy(instance => instance.DefinitionId.Value, StringComparer.Ordinal)
                .ToList());
        }

        public IReadOnlyList<ModuleInstance> Instances { get; }

        public IReadOnlyList<ModuleInstance> GetForNode(StableId nodeId)
        {
            return new ReadOnlyCollection<ModuleInstance>(Instances.Where(instance => instance.NodeId.Equals(nodeId)).ToList());
        }

        private void ValidateNodeDependenciesAndAuthority()
        {
            foreach (var nodeGroup in _instances.Values.GroupBy(instance => instance.NodeId))
            {
                var definitionIds = new HashSet<StableId>(nodeGroup.Select(instance => instance.DefinitionId));
                var authorityKeys = new Dictionary<StableId, StableId>();
                foreach (var instance in nodeGroup)
                {
                    var definition = _catalog.GetRequired(instance.DefinitionId);
                    foreach (var dependencyId in definition.HardDependencyIds)
                    {
                        if (!definitionIds.Contains(dependencyId))
                        {
                            throw new InvalidOperationException(
                                $"Module '{instance.InstanceId}' is missing hard dependency '{dependencyId}' on node '{nodeGroup.Key}'.");
                        }
                    }

                    foreach (var capability in definition.Capabilities.Where(item => item.AuthorityMode == CapabilityAuthorityMode.Authoritative))
                    {
                        if (authorityKeys.TryGetValue(capability.AuthorityKey, out var existing))
                        {
                            throw new InvalidOperationException(
                                $"Modules '{existing}' and '{instance.InstanceId}' both authoritatively provide '{capability.AuthorityKey}' on node '{nodeGroup.Key}'.");
                        }

                        authorityKeys.Add(capability.AuthorityKey, instance.InstanceId);
                    }
                }
            }
        }
    }

    /// <summary>S00 冻结后的节点和模块 ID 快照。</summary>
    public sealed class TickTopologySnapshot
    {
        public TickTopologySnapshot(TickId tickId, IEnumerable<StableId> nodeIds, IEnumerable<StableId> moduleInstanceIds)
        {
            TickId = tickId;
            NodeIds = new ReadOnlyCollection<StableId>((nodeIds ?? throw new ArgumentNullException(nameof(nodeIds)))
                .OrderBy(id => id.Value, StringComparer.Ordinal).ToList());
            ModuleInstanceIds = new ReadOnlyCollection<StableId>((moduleInstanceIds ?? throw new ArgumentNullException(nameof(moduleInstanceIds)))
                .OrderBy(id => id.Value, StringComparer.Ordinal).ToList());
        }

        public TickId TickId { get; }
        public IReadOnlyList<StableId> NodeIds { get; }
        public IReadOnlyList<StableId> ModuleInstanceIds { get; }
    }

    /// <summary>传给模块执行器的单阶段上下文；唯一可写入口是 WorkingState。</summary>
    public sealed class ModuleExecutionContext
    {
        public ModuleExecutionContext(
            TickId tickId,
            WorldExecutionStage stage,
            WorldClock clock,
            ModuleDefinition definition,
            ModuleInstance instance,
            WorkingState workingState,
            TickTopologySnapshot topologySnapshot)
        {
            TickId = tickId;
            Stage = stage;
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            TopologySnapshot = topologySnapshot ?? throw new ArgumentNullException(nameof(topologySnapshot));
        }

        public TickId TickId { get; }
        public WorldExecutionStage Stage { get; }
        public WorldClock Clock { get; }
        public ModuleDefinition Definition { get; }
        public ModuleInstance Instance { get; }
        public WorkingState WorkingState { get; }
        public TickTopologySnapshot TopologySnapshot { get; }
    }

    /// <summary>模块执行器契约。失败结果会触发整 Tick 回滚。</summary>
    public interface IModuleExecutor
    {
        ModuleResult Execute(ModuleExecutionContext context);
    }
}
