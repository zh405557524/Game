using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using UnityEditor;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class WaterBodyWorkspaceTests
    {
        [TestCase("02_Stream")] [TestCase("03_Lake")] [TestCase("04_Pond")] [TestCase("05_Wetland")] [TestCase("06_Coast")]
        public void EachWaterBodyHasARealSceneAndSeparateDependencies(string folder)
        {
            var item = MapDebugWorkbench.LoadCatalog().cases.Single(x => x.id == "water/" + folder);
            Assert.That(File.Exists(item.scenePath), Is.True, item.scenePath);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(item.scenePath), Is.Not.Null);
            string input = item.testDataPath + "/" + folder.Substring(3) + "Study.asset";
            Assert.That(File.Exists(input), Is.True, input);
            var dependencies = AssetDatabase.GetDependencies(item.scenePath);
            CollectionAssert.Contains(dependencies, input);
            Assert.That(dependencies.Any(x => x.StartsWith(item.generatedPath + "/") && x.EndsWith("WaterSurface.asset")), Is.True);
            Assert.That(dependencies.Any(x => x.Contains("/TestData/Learning/")), Is.False);
            Assert.That(item.state, Is.Not.EqualTo(DebugReviewState.Approved), "Automated creation must not approve art.");
        }
    }
}
