using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Tests.Unit
{
    public sealed class FrameworkKernelTests
    {
        [Test]
        public void FrameworkCatalogContainsAllCanonicalModulesAndOnlyCompatibilityAliases()
        {
            var catalog = FrameworkModuleCatalog.Create();

            Assert.That(catalog.Definitions.Count, Is.EqualTo(101));
            Assert.That(catalog.Aliases.Keys, Is.EquivalentTo(new[] { "PersonModule", "TaxModule" }));
            Assert.That(catalog.ResolveSourceName("PersonModule").SourceName, Is.EqualTo("PersonIdentityModule"));
            Assert.That(catalog.ResolveSourceName("TaxModule").SourceName, Is.EqualTo("TaxPolicyModule"));
            Assert.That(catalog.Definitions.All(definition => definition.ImplementationTier == ModuleImplementationTier.Scaffold), Is.True);
        }

        [Test]
        public void ModuleRegistryRejectsTwoAuthoritativeProvidersForOneNodeAndAuthorityKey()
        {
            var authorityKey = new StableId("authority.shared");
            var first = Definition("module.first.v1", authorityKey);
            var second = Definition("module.second.v1", authorityKey);
            var catalog = new ModuleCatalog(new[] { first, second });

            Assert.Throws<InvalidOperationException>(() => new ModuleRegistry(catalog, new[]
            {
                ActiveInstance("instance.first", first.DefinitionId),
                ActiveInstance("instance.second", second.DefinitionId)
            }));
        }

        [Test]
        public void ModuleCatalogRejectsHardDependencyCycles()
        {
            var firstId = new StableId("module.first.v1");
            var secondId = new StableId("module.second.v1");
            var first = Definition(firstId.Value, new StableId("authority.first"), secondId);
            var second = Definition(secondId.Value, new StableId("authority.second"), firstId);

            Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(new[] { first, second }));
        }

        [Test]
        public void WorkingStateRollbackNeverChangesCommittedState()
        {
            var original = new CommittedState(new[]
            {
                new StateRecord("state.a", StateCategory.DirectState, new byte[] { 1 }, "bytes-v1", DataQuality.Exact)
            });
            var working = original.BeginWorkingState();
            working.Set(new StateRecord("state.a", StateCategory.DirectState, new byte[] { 2 }, "bytes-v1", DataQuality.Exact));
            working.Rollback();

            Assert.That(original.TryGet("state.a", out var record), Is.True);
            Assert.That(record.Payload, Is.EqualTo(new byte[] { 1 }));
            Assert.Throws<InvalidOperationException>(() => working.Commit());
        }

        [Test]
        public void TickRunsEveryStageInStableOrderAndClosesUnavailableScaffoldResult()
        {
            var runtime = CreateRuntime(new DefaultModuleExecutorFactory());

            var result = runtime.Advance(new AdvanceRequest(AdvanceUnit.Day));

            Assert.That(result.Committed, Is.True);
            Assert.That(result.Stages.Select(stage => (int)stage.Stage), Is.Ordered);
            Assert.That(result.Stages.Count, Is.EqualTo(14));
            Assert.That(result.ModuleResults, Has.Count.EqualTo(1));
            Assert.That(result.ModuleResults[0].ImplementationTier, Is.EqualTo(ModuleImplementationTier.Scaffold));
            Assert.That(result.ModuleResults[0].DataQuality, Is.EqualTo(DataQuality.Unavailable));
            Assert.That(result.ModuleResults[0].ReasonCode, Is.EqualTo(ScaffoldModuleExecutor.UnavailableReason));
            Assert.That(runtime.ElapsedDays, Is.EqualTo(1));
        }

        [Test]
        public void FailedModuleRollsBackClockStateAndCommands()
        {
            var runtime = CreateRuntime(new FailingExecutorFactory());
            var before = runtime.CurrentStateHash;

            var result = runtime.Advance(new AdvanceRequest(AdvanceUnit.Day));

            Assert.That(result.Committed, Is.False);
            Assert.That(runtime.ElapsedDays, Is.Zero);
            Assert.That(runtime.CurrentStateHash, Is.EqualTo(before));
            Assert.That(runtime.Checkpoints, Has.Count.EqualTo(1));
        }

        [Test]
        public void ScaffoldCommandIsIdempotentAndRejectedWithStatusEvents()
        {
            var runtime = CreateRuntime(new DefaultModuleExecutorFactory());
            var envelope = new CommandEnvelope(
                new StableId("command.instance.1"),
                new StableId("command.domain.test.v1"),
                new StableId("actor.test"),
                new StableId("node.test"),
                new StableId("scope.test"),
                "same-request",
                Array.Empty<byte>(),
                new TickId(0));

            var first = runtime.SubmitCommand(envelope);
            var duplicate = runtime.SubmitCommand(new CommandEnvelope(
                new StableId("command.instance.2"),
                envelope.CommandDefinitionId,
                envelope.ActorId,
                envelope.TargetId,
                envelope.AuthorityScopeId,
                envelope.IdempotencyKey,
                Array.Empty<byte>(),
                new TickId(0)));
            runtime.AdvanceOneDay();

            Assert.That(duplicate, Is.SameAs(first));
            var committed = runtime.Commands.Single();
            Assert.That(committed.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(committed.StatusEvents.Select(item => item.CurrentStatus), Is.EqualTo(new[]
            {
                CommandStatus.Submitted,
                CommandStatus.Validating,
                CommandStatus.Rejected
            }));
            Assert.That(committed.StatusEvents.Last().ReasonCode, Is.EqualTo("implementation_unavailable"));
        }

        [Test]
        public void AvailableCommandTraversesReservationDispatchExecutionAndSettlement()
        {
            var processor = new CommandProcessor(false);
            var command = processor.Submit(new CommandEnvelope(
                new StableId("command.available.1"),
                new StableId("command.test.v1"),
                new StableId("actor.test"),
                new StableId("node.test"),
                new StableId("scope.test"),
                "available-request",
                Array.Empty<byte>(),
                new TickId(0)), new TickId(0));

            processor.ValidatePending(new TickId(1));
            processor.ReserveAccepted(new TickId(1));
            processor.DispatchReserved(new TickId(1));
            processor.ExecuteDispatched(new TickId(1));

            Assert.That(command.Status, Is.EqualTo(CommandStatus.Settled));
            Assert.That(command.StatusEvents.Select(item => item.CurrentStatus), Is.EqualTo(new[]
            {
                CommandStatus.Submitted,
                CommandStatus.Validating,
                CommandStatus.Accepted,
                CommandStatus.Reserving,
                CommandStatus.Reserved,
                CommandStatus.Dispatched,
                CommandStatus.Executing,
                CommandStatus.Completed,
                CommandStatus.Settled
            }));
            Assert.That(processor.Reservations.Single().Committed, Is.True);
        }

        [Test]
        public void CalendarClosesMonthSeasonAndYearAtDeterministicBoundaries()
        {
            var runtime = CreateRuntime(new DefaultModuleExecutorFactory());

            var month = runtime.Advance(new AdvanceRequest(AdvanceUnit.Month));
            var season = runtime.Advance(new AdvanceRequest(AdvanceUnit.Season));
            var year = runtime.Advance(new AdvanceRequest(AdvanceUnit.Year));

            Assert.That(month.PeriodCloseFlags.HasFlag(PeriodCloseFlags.Month), Is.True);
            Assert.That(season.PeriodCloseFlags.HasFlag(PeriodCloseFlags.Season), Is.True);
            Assert.That(year.PeriodCloseFlags.HasFlag(PeriodCloseFlags.Year), Is.True);
            Assert.That(runtime.Clock.EconomicYear, Is.EqualTo(2));
            Assert.That(runtime.Clock.Month, Is.EqualTo(1));
            Assert.That(runtime.Clock.Day, Is.EqualTo(1));
        }

        [Test]
        public void Pcg32MatchesReferenceVector()
        {
            var random = new Pcg32(42, 54);

            Assert.That(random.NextUInt(), Is.EqualTo(0xa15c02b7u));
            Assert.That(random.NextUInt(), Is.EqualTo(0x7b47f409u));
            Assert.That(random.NextUInt(), Is.EqualTo(0xba1d3330u));
            Assert.That(random.NextUInt(), Is.EqualTo(0x83d2f293u));
            Assert.That(random.NextUInt(), Is.EqualTo(0xbfa4784bu));
        }

        [Test]
        public void IdenticalWorldsProduceTheSameHashAcrossOneHundredRuns()
        {
            var hashes = Enumerable.Range(0, 100).Select(_ =>
            {
                var runtime = CreateRuntime(new DefaultModuleExecutorFactory());
                runtime.AdvanceOneDay();
                return runtime.CurrentStateHash.Sha256;
            }).Distinct().ToList();

            Assert.That(hashes, Has.Count.EqualTo(1));
        }

        private static WorldRuntime CreateRuntime(IModuleExecutorFactory executorFactory)
        {
            var nodeId = new StableId("node.test");
            var factionId = new StableId("faction.test");
            var topology = new WorldTopology(
                new GeographicTree(new[] { new RegionNode(nodeId, SimulationNodeKind.World, "Test") }),
                new FactionGraph(new[] { new FactionNode(factionId, "Test faction") }),
                new JurisdictionGraph(new[]
                {
                    new JurisdictionRelation(new StableId("jurisdiction.test"), factionId, nodeId, "test")
                }));
            var catalog = FrameworkModuleCatalog.Create();
            var definition = catalog.ResolveSourceName("PopulationModule");
            var instance = ActiveInstance("instance.population", definition.DefinitionId);
            var registry = new ModuleRegistry(catalog, new[] { instance });
            var ruleset = new RulesetManifest(
                "framework-ruleset-v1",
                FrameworkModuleCatalog.Version,
                "save-schema-v1",
                "test-definition-v1",
                "framework-empty-v1",
                "pcg32-v1",
                false);
            return new WorldRuntime(
                new StableId("save.test"),
                new StableId("world.test"),
                new WorldSeed(1628),
                ruleset,
                topology,
                catalog,
                registry,
                new TickCoordinator(executorFactory));
        }

        private static ModuleDefinition Definition(string id, StableId authorityKey, params StableId[] hardDependencies)
        {
            return new ModuleDefinition(
                new StableId(id),
                id,
                "test-v1",
                ModuleImplementationTier.Scaffold,
                new[]
                {
                    new CapabilityContract(new StableId("capability." + id), authorityKey, CapabilityAuthorityMode.Authoritative, false)
                },
                new[] { WorldExecutionStage.S30LocalFactSettlement },
                hardDependencies);
        }

        private static ModuleInstance ActiveInstance(string instanceId, StableId definitionId)
        {
            var instance = new ModuleInstance(new StableId(instanceId), definitionId, new StableId("node.test"));
            instance.TransitionTo(ModuleLifecycleState.Initializing);
            instance.TransitionTo(ModuleLifecycleState.Active);
            return instance;
        }

        private sealed class FailingExecutorFactory : IModuleExecutorFactory
        {
            public IModuleExecutor Create(ModuleDefinition definition) => new FailingExecutor();
        }

        private sealed class FailingExecutor : IModuleExecutor
        {
            public ModuleResult Execute(ModuleExecutionContext context)
            {
                return new ModuleResult(
                    context.TickId,
                    context.Instance.InstanceId,
                    context.Instance.NodeId,
                    context.Stage,
                    ModuleImplementationTier.Scaffold,
                    DataQuality.Blocked,
                    false,
                    "forced_failure");
            }
        }
    }
}
