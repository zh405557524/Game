using System;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.Framework.Testing;
using ProjectRealm.SystemServer;
using ProjectRealm.World;

namespace ProjectRealm.Tests.Unit
{
    public sealed class SimulationCoreTests
    {
        [Test]
        public void SystemServerStartsAtMainMenuWithoutCreatingOrAdvancingAWorld()
        {
            var server = CreateServer();

            server.Start();

            Assert.That(server.State, Is.EqualTo(RealmApplicationState.MainMenu));
            Assert.That(server.Context.World.HasActiveWorld, Is.False);
            Assert.That(server.Context.World.GetCurrent().Error.Code, Is.EqualTo("no_active_world"));
            server.Stop();
        }

        [Test]
        public void NewWorldUsesStableIdentityAndDoesNotImplicitlyAdvance()
        {
            var server = CreateServer();
            server.Start();

            var result = server.Context.World.Create(new NewRealmWorldRequest("save.test", "MING1628", 1628));

            Assert.That(result.Succeeded, Is.True, result.Error?.Message);
            Assert.That(result.Value.WorldId, Is.EqualTo("MING1628"));
            Assert.That(result.Value.SaveId, Is.EqualTo("save.test"));
            Assert.That(result.Value.Tick, Is.Zero);
            Assert.That(server.State, Is.EqualTo(RealmApplicationState.Running));
            server.Stop();
        }

        [Test]
        public void StableIdRejectsWhitespace()
        {
            Assert.Throws<ArgumentException>(() => new StableId("MING 1628"));
        }

        [TestCase(typeof(StableId))]
        [TestCase(typeof(IRealmContext))]
        [TestCase(typeof(RealmSystemServer))]
        [TestCase(typeof(WorldClock))]
        public void PureFrameworkAssembliesDoNotReferenceUnityEngine(Type markerType)
        {
            var referencesUnityEngine = markerType.Assembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal));

            Assert.That(referencesUnityEngine, Is.False, $"{markerType.Assembly.GetName().Name} must remain independent of UnityEngine.");
        }

        private static RealmSystemServer CreateServer()
        {
            return new RealmSystemServer(
                new InMemoryWorldDefinitionStore(new[] { CreateDefinition() }),
                new InMemorySaveGameStore(),
                new TestSceneNavigator());
        }

        private static WorldDefinition CreateDefinition()
        {
            var worldId = new StableId("MING1628");
            var topology = new WorldTopology(
                new GeographicTree(new[] { new RegionNode(worldId, SimulationNodeKind.World, "Ming 1628") }),
                new FactionGraph(Array.Empty<FactionNode>()),
                new JurisdictionGraph(Array.Empty<JurisdictionRelation>()));
            var manifest = new RulesetManifest(
                "framework-ruleset-v1",
                FrameworkModuleCatalog.Version,
                "save-schema-v1",
                "test-definition-v1",
                "framework-empty-v1",
                "pcg32-v1",
                false);
            return new WorldDefinition(worldId, manifest, topology, new[]
            {
                new NodeModuleComposition(worldId, FrameworkModuleCatalog.DefinitionIdFor("PopulationModule"))
            });
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
