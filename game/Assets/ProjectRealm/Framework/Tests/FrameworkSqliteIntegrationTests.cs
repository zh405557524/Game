using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.Persistence.Sqlite;
using ProjectRealm.SystemServer;
using ProjectRealm.World;
using SQLite;
using UnityEngine;

namespace ProjectRealm.Framework.Tests
{
    public sealed class FrameworkSqliteIntegrationTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), "project-realm-framework-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        [Test]
        public void DevelopmentDefinitionLoadsNationwideCountiesAndFullSampleComposition()
        {
            var definition = LoadDefinition();

            Assert.That(definition.Manifest.CommercialReleaseReady, Is.False);
            Assert.That(definition.Manifest.ModuleCatalogVersion, Is.EqualTo(FrameworkModuleCatalog.Version));
            Assert.That(definition.Topology.Geography.Nodes.Count(node => node.Kind == SimulationNodeKind.County), Is.EqualTo(1168));
            Assert.That(definition.Topology.Geography.GetRequired(new StableId("MING1628-0205")).DisplayName, Is.EqualTo("萧县"));
            Assert.That(definition.Topology.Geography.GetRequired(new StableId("MING1628-0205-LD033")).DisplayName, Is.EqualTo("南江桥乡"));
            Assert.That(definition.Topology.Geography.GetRequired(new StableId("MING1628-0205-V2080")).DisplayName, Is.EqualTo("七里村"));
            Assert.That(definition.ModuleCompositions.Count(item => item.NodeId.Equals(new StableId("MING1628-0205"))), Is.EqualTo(101));
            Assert.That(definition.ModuleCompositions.Count(item => item.NodeId.Equals(new StableId("MING1628-0205-LD033"))), Is.EqualTo(101));
            Assert.That(definition.ModuleCompositions.Count(item => item.NodeId.Equals(new StableId("MING1628-0205-V2080"))), Is.EqualTo(101));
            Assert.That(definition.ModuleCompositions.Count(item => item.NodeId.Equals(new StableId("MING1628-0001"))), Is.EqualTo(12));
        }

        [Test]
        public void NationwideScaffoldTickPersistsReloadsAndContinuesDeterministically()
        {
            var definitionStore = LoadDefinitionStore();
            var reloadRoot = Path.Combine(_temporaryRoot, "reload");
            var reloadStore = new SqliteSaveGameStore(reloadRoot);
            var reloadedServer = CreateServer(definitionStore, reloadStore);
            reloadedServer.Start();

            var created = reloadedServer.Context.World.Create(
                new NewRealmWorldRequest("integration-save", "MING1628", 1628));
            Assert.That(created.Succeeded, Is.True, created.Error?.Message);
            Assert.That(created.Value.ModuleInstanceCount, Is.EqualTo(14307));
            Assert.That(created.Value.ScaffoldModuleCount, Is.EqualTo(14307));

            var firstTick = reloadedServer.Context.Simulation.Advance(RealmAdvanceUnit.Day);
            Assert.That(firstTick.Succeeded, Is.True, firstTick.Error?.Message);
            Assert.That(firstTick.Value.Committed, Is.True, firstTick.Value.FailureReason);
            Assert.That(firstTick.Value.ModuleResultCount, Is.EqualTo(14307));
            Assert.That(firstTick.Value.DataQuality, Is.EqualTo(DataQuality.Unavailable.ToString()));
            Assert.That(reloadedServer.Context.Saves.Save().Succeeded, Is.True);
            Assert.That(reloadStore.Exists(new StableId("integration-save")), Is.True);

            var saved = reloadedServer.Context.World.GetCurrent().Value;
            Assert.That(reloadedServer.Context.World.Close().Succeeded, Is.True);
            var loaded = reloadedServer.Context.Saves.Load("integration-save");
            Assert.That(loaded.Succeeded, Is.True, loaded.Error?.Message);
            Assert.That(loaded.Value.StateHash, Is.EqualTo(saved.StateHash));
            Assert.That(loaded.Value.Tick, Is.EqualTo(saved.Tick));
            var afterReload = reloadedServer.Context.Simulation.Advance(RealmAdvanceUnit.Day);
            Assert.That(afterReload.Succeeded && afterReload.Value.Committed, Is.True, afterReload.Error?.Message);

            var continuousStore = new SqliteSaveGameStore(Path.Combine(_temporaryRoot, "continuous"));
            var continuousServer = CreateServer(definitionStore, continuousStore);
            continuousServer.Start();
            var continuousCreated = continuousServer.Context.World.Create(
                new NewRealmWorldRequest("integration-save", "MING1628", 1628));
            Assert.That(continuousCreated.Succeeded, Is.True, continuousCreated.Error?.Message);
            Assert.That(continuousServer.Context.Simulation.Advance(RealmAdvanceUnit.Day).Value.Committed, Is.True);
            var uninterrupted = continuousServer.Context.Simulation.Advance(RealmAdvanceUnit.Day);

            Assert.That(uninterrupted.Succeeded && uninterrupted.Value.Committed, Is.True, uninterrupted.Error?.Message);
            Assert.That(afterReload.Value.StateHash, Is.EqualTo(uninterrupted.Value.StateHash));
            reloadedServer.Stop();
            continuousServer.Stop();
        }

        [Test]
        public void DiagnosticsManagerCannotAdvanceClockOrChangeHash()
        {
            var server = CreateServer(
                LoadDefinitionStore(),
                new SqliteSaveGameStore(Path.Combine(_temporaryRoot, "diagnostics")));
            server.Start();
            var created = server.Context.World.Create(
                new NewRealmWorldRequest("diagnostics-save", "MING1628", 1628));
            Assert.That(created.Succeeded, Is.True, created.Error?.Message);
            var before = server.Context.World.GetCurrent().Value;

            for (var index = 0; index < 20; index++)
            {
                var diagnostics = server.Context.Diagnostics.Query("MING1628", index % 3, 25);
                Assert.That(diagnostics.Succeeded, Is.True, diagnostics.Error?.Message);
                Assert.That(diagnostics.Value.World.GeographicNodeCount, Is.GreaterThan(1168));
            }

            var after = server.Context.World.GetCurrent().Value;
            Assert.That(after.Tick, Is.EqualTo(before.Tick));
            Assert.That(after.StateHash, Is.EqualTo(before.StateHash));
            server.Stop();
        }

        private static RealmSystemServer CreateServer(IWorldDefinitionStore definitions, ISaveGameStore saves)
        {
            return new RealmSystemServer(definitions, saves, new TestSceneNavigator());
        }

        private static SqliteWorldDefinitionStore LoadDefinitionStore()
        {
            var asset = Resources.Load<SQLiteAsset>("realm_definition_ming1628_dev_v1");
            Assert.That(asset, Is.Not.Null,
                "Run python3 tools/framework/build_runtime_definition.py before the framework tests.");
            return new SqliteWorldDefinitionStore(asset);
        }

        private static WorldDefinition LoadDefinition()
        {
            return LoadDefinitionStore().LoadWorld(new StableId("MING1628"));
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
