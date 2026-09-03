using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using UnityEditor;
using UnityEngine;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class WorkspaceLayoutTests
    {
        [TestCase(ProjectRealmWorkspaceLayout.LearningScene, "74ac1795408aa4784b578d45c4cec1e9")]
        [TestCase(ProjectRealmWorkspaceLayout.IntegrationScene, "515b9164c428c4ba38632b4ec85f6bc4")]
        [TestCase(ProjectRealmWorkspaceLayout.PrototypeScene, "abed279c2bc2f4b77b1ae8c02f785643")]
        [TestCase(ProjectRealmWorkspaceLayout.MaterialsScene, "358e1419c2c124daabd27be2ebccf885")]
        [TestCase(ProjectRealmWorkspaceLayout.TerrainData, "5015a5d6ebfdf45b08f93d1f27473170")]
        [TestCase(ProjectRealmWorkspaceLayout.StudyDefinition, "aa616109ec14b43699ac1e23b2bdee5c")]
        [TestCase(ProjectRealmWorkspaceLayout.PrototypeDefinition, "f46e183429b30413fb10096f40fab6bf")]
        public void KnownAssetsKeepTheirGuid(string path, string guid)
        {
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.Not.Null);
        }

        [Test] public void SceneFoldersDoNotContainLooseTestData()
        {
            foreach (string path in Directory.GetFiles("Assets/Scenes", "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(path);
                Assert.That(extension == ".unity" || extension == ".meta" || extension == ".md", Is.True, path);
            }
        }

        [Test] public void LearningTerrainKeepsAssignedPaintLayersAndTheirTextures()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(ProjectRealmWorkspaceLayout.TerrainData);
            Assert.That(terrain, Is.Not.Null);
            // The migration snapshot has two assigned layers; the third asset was unassigned.
            Assert.That(terrain.terrainLayers.Length, Is.EqualTo(2));
            foreach (var layer in terrain.terrainLayers)
            {
                Assert.That(layer, Is.Not.Null);
                Assert.That(layer.diffuseTexture, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(layer), Does.StartWith(ProjectRealmWorkspaceLayout.TerrainLayers));
            }
            Assert.That(AssetDatabase.GetDependencies(ProjectRealmWorkspaceLayout.LearningScene, true),
                Does.Contain(ProjectRealmWorkspaceLayout.TerrainData));
            foreach (string name in new[] { "PlainPractice", "HillsPractice", "BasinPractice" })
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{ProjectRealmWorkspaceLayout.TerrainLayers}/{name}.terrainlayer");
                Assert.That(layer, Is.Not.Null, name);
                Assert.That(layer.diffuseTexture, Is.Not.Null, name);
            }
        }

        [Test] public void AllBuildScenePathsResolveAfterMigration()
        {
            foreach (var scene in EditorBuildSettings.scenes) Assert.That(File.Exists(scene.path), Is.True, scene.path);
        }

        [Test] public void CatalogSeparatesSceneInputAndOutputForFiveTerrains()
        {
            var catalog = MapDebugWorkbench.LoadCatalog();
            Assert.That(catalog.cases.FindAll(TerrainDebugSceneBuilder.IsBaseTerrainCase).Count, Is.EqualTo(5));
            Assert.That(catalog.cases.Select(x => x.id).Distinct().Count(), Is.EqualTo(catalog.cases.Count));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 12), catalog.cases.Select(x => x.layer).Distinct());
            foreach (var entry in catalog.cases)
            {
                Assert.That(entry.testDataPath, Does.StartWith(ProjectRealmWorkspaceLayout.TestData));
                Assert.That(entry.generatedPath, Does.StartWith(ProjectRealmWorkspaceLayout.Generated));
                if (!string.IsNullOrEmpty(entry.scenePath)) Assert.That(entry.scenePath, Does.StartWith(ProjectRealmWorkspaceLayout.DebugScenes));
            }
        }
    }
}
