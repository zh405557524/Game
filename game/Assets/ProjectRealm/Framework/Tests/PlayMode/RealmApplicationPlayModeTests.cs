using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ProjectRealm.Bootstrap;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.Persistence.Sqlite;
using ProjectRealm.SystemServer;
using ProjectRealm.UnityAdapter;
using SQLite;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectRealm.Framework.PlayModeTests
{
    public sealed class RealmApplicationPlayModeTests
    {
        [UnityTest]
        public IEnumerator BootstrapEntersMainMenuWithoutCreatingOrAdvancingAWorld()
        {
            SceneManager.LoadScene(UnityRealmSceneNavigator.BootstrapScene, LoadSceneMode.Single);
            yield return null;

            var application = UnityEngine.Object.FindAnyObjectByType<RealmApplication>();
            Assert.That(application, Is.Not.Null);
            Assert.That(application.State, Is.EqualTo(RealmApplicationState.MainMenu));
            Assert.That(application.Context.World.HasActiveWorld, Is.False);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(UnityRealmSceneNavigator.MainMenuScene));
        }

        [UnityTest]
        public IEnumerator ManagersSaveReloadAndContinueFromTheSameClosedTick()
        {
            var temporaryRoot = Path.Combine(Path.GetTempPath(), "project-realm-playmode", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            var server = CreateServer(temporaryRoot);
            try
            {
                server.Start();
                var created = server.Context.World.Create(
                    new NewRealmWorldRequest("playmode-save", "MING1628", 1628));
                Assert.That(created.Succeeded, Is.True, created.Error?.Message);

                var advanced = server.Context.Simulation.Advance(RealmAdvanceUnit.Day);
                Assert.That(advanced.Succeeded && advanced.Value.Committed, Is.True, advanced.Error?.Message);
                Assert.That(server.Context.Saves.Save().Succeeded, Is.True);
                var before = server.Context.World.GetCurrent().Value;

                Assert.That(server.Context.World.Close().Succeeded, Is.True);
                var loaded = server.Context.Saves.Load("playmode-save");
                Assert.That(loaded.Succeeded, Is.True, loaded.Error?.Message);
                Assert.That(loaded.Value.Tick, Is.EqualTo(before.Tick));
                Assert.That(loaded.Value.StateHash, Is.EqualTo(before.StateHash));

                var continued = server.Context.Simulation.Advance(RealmAdvanceUnit.Day);
                Assert.That(continued.Succeeded && continued.Value.Committed, Is.True, continued.Error?.Message);
                yield return null;
            }
            finally
            {
                server.Stop();
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDownApplication()
        {
            var application = UnityEngine.Object.FindAnyObjectByType<RealmApplication>();
            if (application != null)
            {
                UnityEngine.Object.Destroy(application.gameObject);
                yield return null;
            }
        }

        private static RealmSystemServer CreateServer(string saveRoot)
        {
            var definitionAsset = Resources.Load<SQLiteAsset>("realm_definition_ming1628_dev_v1");
            Assert.That(definitionAsset, Is.Not.Null,
                "Run python3 tools/framework/build_runtime_definition.py before PlayMode tests.");
            return new RealmSystemServer(
                new SqliteWorldDefinitionStore(definitionAsset),
                new SqliteSaveGameStore(saveRoot),
                new TestSceneNavigator());
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
