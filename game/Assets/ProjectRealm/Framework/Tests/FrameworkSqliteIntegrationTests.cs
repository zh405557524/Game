using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Infrastructure.Sqlite;
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
            var saveStore = new SqliteSaveGameStore(_temporaryRoot);
            var bootstrapper = new WorldBootstrapper(definitionStore, saveStore);
            var saveId = new StableId("integration-save");
            var runtime = bootstrapper.StartNewWorld(new WorldBootstrapRequest(
                saveId,
                new StableId("MING1628"),
                new WorldSeed(1628)));

            var firstTick = runtime.Advance(new AdvanceRequest(AdvanceUnit.Day));
            Assert.That(firstTick.Committed, Is.True, firstTick.FailureReason);
            Assert.That(runtime.ModuleRegistry.Instances, Has.Count.EqualTo(14307));
            Assert.That(firstTick.ModuleResults, Has.Count.EqualTo(14307));
            Assert.That(firstTick.ModuleResults.All(result =>
                result.ImplementationTier == ModuleImplementationTier.Scaffold &&
                result.DataQuality == DataQuality.Unavailable), Is.True);
            Assert.That(runtime.CommittedState.Records, Is.Empty);

            runtime.Save();
            Assert.That(File.Exists(saveStore.GetSavePath(saveId)), Is.True);
            var reloaded = bootstrapper.LoadWorld(new LoadWorldRequest(saveId));
            Assert.That(reloaded.CurrentStateHash, Is.EqualTo(runtime.CurrentStateHash));
            Assert.That(reloaded.Clock.DayIndex, Is.EqualTo(runtime.Clock.DayIndex));
            Assert.That(reloaded.ModuleRegistry.Instances, Has.Count.EqualTo(runtime.ModuleRegistry.Instances.Count));

            runtime.AdvanceOneDay();
            reloaded.AdvanceOneDay();
            Assert.That(reloaded.CurrentStateHash, Is.EqualTo(runtime.CurrentStateHash));
        }

        [Test]
        public void DiagnosticsQueryCannotAdvanceClockOrChangeHash()
        {
            var definitionStore = LoadDefinitionStore();
            var saveStore = new SqliteSaveGameStore(_temporaryRoot);
            var runtime = new WorldBootstrapper(definitionStore, saveStore).StartNewWorld(
                new WorldBootstrapRequest(new StableId("diagnostics-save"), new StableId("MING1628"), new WorldSeed(1628)));
            var beforeClock = runtime.Clock.TickSequence;
            var beforeHash = runtime.CurrentStateHash;

            var query = new SimulationDiagnosticsQuery();
            for (var index = 0; index < 20; index++)
            {
                var snapshot = query.Query(runtime, "MING1628", index % 3, 25);
                Assert.That(snapshot.GeographicNodeCount, Is.GreaterThan(1168));
            }

            Assert.That(runtime.Clock.TickSequence, Is.EqualTo(beforeClock));
            Assert.That(runtime.CurrentStateHash, Is.EqualTo(beforeHash));
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
    }
}
