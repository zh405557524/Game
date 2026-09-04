using System.Linq;
using NUnit.Framework;
using ProjectRealm.Bootstrap;
using ProjectRealm.EditorTools;
using ProjectRealm.UnityPresentation.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class RealmRuntimeSceneTests
    {
        [Test]
        public void RuntimeScenesAreTheFirstEnabledBuildScenes()
        {
            var paths = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();

            Assert.That(paths.Take(4), Is.EqualTo(new[]
            {
                RealmRuntimeSceneBuilder.BootstrapScene,
                RealmRuntimeSceneBuilder.MainMenuScene,
                RealmRuntimeSceneBuilder.GameplayScene,
                RealmRuntimeSceneBuilder.CountyMapScene
            }));
        }

        [Test]
        public void BootstrapSceneContainsExactlyOneRealmApplication()
        {
            var scene = EditorSceneManager.OpenScene(RealmRuntimeSceneBuilder.BootstrapScene, OpenSceneMode.Additive);
            try
            {
                var applications = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RealmApplication>(true))
                    .ToArray();
                Assert.That(applications, Has.Length.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [TestCase(RealmRuntimeSceneBuilder.MainMenuScene, typeof(MainMenuScreenView))]
        [TestCase(RealmRuntimeSceneBuilder.GameplayScene, typeof(GameplayScreenView))]
        public void ScreenSceneContainsItsSingleView(string path, System.Type viewType)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var views = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren(viewType, true));
                Assert.That(views.Count(), Is.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
