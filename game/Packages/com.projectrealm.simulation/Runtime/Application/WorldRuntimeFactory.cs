using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    public sealed class WorldBootstrapRequest
    {
        public WorldBootstrapRequest(StableId saveId, StableId worldId, WorldSeed worldSeed)
        {
            SimulationNode.RequireId(saveId, nameof(saveId));
            SimulationNode.RequireId(worldId, nameof(worldId));
            SaveId = saveId;
            WorldId = worldId;
            WorldSeed = worldSeed;
        }

        public StableId SaveId { get; }
        public StableId WorldId { get; }
        public WorldSeed WorldSeed { get; }
    }

    public sealed class LoadWorldRequest
    {
        public LoadWorldRequest(StableId saveId)
        {
            SimulationNode.RequireId(saveId, nameof(saveId));
            SaveId = saveId;
        }

        public StableId SaveId { get; }
    }

    internal static class WorldRuntimeFactory
    {
        public static WorldRuntime CreateNew(
            WorldBootstrapRequest request,
            WorldDefinition definition,
            ISaveGameStore saveStore,
            IModuleExecutorFactory executorFactory,
            ISimulationDiagnosticsSink diagnostics)
        {
            if (request == null || definition == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.WorldId.Equals(definition.WorldId))
            {
                throw new InvalidOperationException("The requested world does not match the loaded Definition database.");
            }

            var catalog = FrameworkModuleCatalog.Create();
            ValidateCatalogVersion(definition.Manifest, catalog);
            var instances = BuildInstances(definition, catalog);
            var registry = new ModuleRegistry(catalog, instances);
            return new WorldRuntime(
                request.SaveId,
                request.WorldId,
                request.WorldSeed,
                definition.Manifest,
                definition.Topology,
                catalog,
                registry,
                new TickCoordinator(executorFactory ?? new DefaultModuleExecutorFactory(), diagnostics),
                saveStore,
                commandProcessor: new CommandProcessor(true, diagnostics));
        }

        public static WorldRuntime Restore(
            WorldSaveData save,
            WorldDefinition definition,
            ISaveGameStore saveStore,
            IModuleExecutorFactory executorFactory,
            ISimulationDiagnosticsSink diagnostics)
        {
            if (save == null || definition == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            if (!save.Manifest.WorldId.Equals(definition.WorldId) ||
                !string.Equals(save.Manifest.Ruleset.DefinitionContentHash, definition.Manifest.DefinitionContentHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The save requires a different Definition database content hash.");
            }

            var catalog = FrameworkModuleCatalog.Create();
            ValidateCatalogVersion(save.Manifest.Ruleset, catalog);
            var registry = new ModuleRegistry(catalog, save.ModuleInstances);
            var currentCheckpoint = save.Checkpoints.Single(checkpoint =>
                checkpoint.CheckpointId.Equals(save.Manifest.CurrentCheckpointId));
            var actualStateHash = DeterministicStateHasher.Compute(
                save.Manifest.WorldId,
                save.WorldSeed,
                save.Clock,
                save.Topology,
                registry,
                save.CommittedState);
            if (!actualStateHash.Equals(currentCheckpoint.StateHash))
            {
                throw new InvalidOperationException("The save state does not match its current closed-tick checkpoint hash.");
            }

            var commands = new CommandProcessor(true, diagnostics, save.Commands, save.Reservations);
            return new WorldRuntime(
                save.Manifest.SaveId,
                save.Manifest.WorldId,
                save.WorldSeed,
                save.Manifest.Ruleset,
                save.Topology,
                catalog,
                registry,
                new TickCoordinator(executorFactory ?? new DefaultModuleExecutorFactory(), diagnostics),
                saveStore,
                save.Clock,
                save.CommittedState,
                commands,
                save.Events,
                save.Checkpoints,
                save.ModuleResults,
                save.NodePeriodResults,
                save.NodeSnapshots);
        }

        public static WorldDefinition CreateMinimalDefinition(StableId worldId)
        {
            var worldNode = new RegionNode(worldId, SimulationNodeKind.World, worldId.Value);
            var faction = new FactionNode(new StableId("faction.ming.dev"), "Ming development authority", false);
            var topology = new WorldTopology(
                new GeographicTree(new[] { worldNode }),
                new FactionGraph(new[] { faction }),
                new JurisdictionGraph(new[]
                {
                    new JurisdictionRelation(
                        new StableId("jurisdiction.ming.dev.world"),
                        faction.NodeId,
                        worldId,
                        "development-only",
                        false)
                }));
            var manifest = new RulesetManifest(
                "framework-ruleset-v1",
                FrameworkModuleCatalog.Version,
                "save-schema-v1",
                "minimal-definition-v1",
                "framework-empty-v1",
                "pcg32-v1",
                false);
            return new WorldDefinition(worldId, manifest, topology, Array.Empty<NodeModuleComposition>());
        }

        private static IEnumerable<ModuleInstance> BuildInstances(WorldDefinition definition, ModuleCatalog catalog)
        {
            var nodeIds = new HashSet<StableId>(definition.Topology.Geography.Nodes.Select(node => node.NodeId)
                .Concat(definition.Topology.Factions.Nodes.Select(node => node.NodeId)));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var composition in definition.ModuleCompositions)
            {
                if (!nodeIds.Contains(composition.NodeId))
                {
                    throw new InvalidOperationException($"Module composition refers to missing node '{composition.NodeId}'.");
                }

                catalog.GetRequired(composition.ModuleDefinitionId);
                var pairKey = composition.NodeId.Value + "\u001f" + composition.ModuleDefinitionId.Value;
                if (!seen.Add(pairKey))
                {
                    throw new InvalidOperationException($"Duplicate module composition '{pairKey}'.");
                }

                var instance = new ModuleInstance(
                    new StableId("instance." + composition.NodeId.Value + "." + composition.ModuleDefinitionId.Value),
                    composition.ModuleDefinitionId,
                    composition.NodeId);
                instance.TransitionTo(ModuleLifecycleState.Initializing);
                instance.TransitionTo(ModuleLifecycleState.Active);
                yield return instance;
            }
        }

        private static void ValidateCatalogVersion(RulesetManifest manifest, ModuleCatalog catalog)
        {
            if (!string.Equals(manifest.ModuleCatalogVersion, FrameworkModuleCatalog.Version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Definition module catalog '{manifest.ModuleCatalogVersion}' is incompatible with '{FrameworkModuleCatalog.Version}'.");
            }

            if (catalog.Definitions.Count != FrameworkModuleCatalog.CanonicalNames.Count)
            {
                throw new InvalidOperationException("The framework module catalog is incomplete.");
            }
        }
    }
}
