using System;
using NUnit.Framework;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.Framework.Testing;
using ProjectRealm.SystemServer;
using ProjectRealm.World;

namespace ProjectRealm.Tests.Integration
{
    public sealed class WorldBootstrapIntegrationTests
    {
        [Test]
        public void PublicManagerCanCreateAWorldThroughFrameworkPorts()
        {
            var worldId = new StableId("MING1628");
            var server = new RealmSystemServer(
                new InMemoryWorldDefinitionStore(new[] { CreateDefinition(worldId) }),
                new InMemorySaveGameStore(),
                new TestSceneNavigator());
            server.Start();

            var result = server.Context.World.Create(new NewRealmWorldRequest("integration.save", worldId.Value, 1628));

            Assert.That(result.Succeeded, Is.True, result.Error?.Message);
            Assert.That(result.Value.WorldId, Is.EqualTo(worldId.Value));
            Assert.That(result.Value.Tick, Is.Zero);
            server.Stop();
        }

        private static WorldDefinition CreateDefinition(StableId worldId)
        {
            return new WorldDefinition(
                worldId,
                new RulesetManifest(
                    "framework-ruleset-v1",
                    FrameworkModuleCatalog.Version,
                    "save-schema-v1",
                    "test-definition-v1",
                    "framework-empty-v1",
                    "pcg32-v1",
                    false),
                new WorldTopology(
                    new GeographicTree(new[] { new RegionNode(worldId, SimulationNodeKind.World, "Ming 1628") }),
                    new FactionGraph(Array.Empty<FactionNode>()),
                    new JurisdictionGraph(Array.Empty<JurisdictionRelation>())),
                new[] { new NodeModuleComposition(worldId, FrameworkModuleCatalog.DefinitionIdFor("PopulationModule")) });
        }

        private sealed class TestSceneNavigator : IRealmSceneNavigator
        {
            public RealmResult ShowMainMenu() => RealmResult.Success();
            public RealmResult ShowGameplay() => RealmResult.Success();
            public RealmResult ShowFault(string message) => RealmResult.Success();
            public RealmResult ExitApplication() => RealmResult.Success();
        }
    }
}
