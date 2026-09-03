using System.IO;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using UnityEditor;
using UnityEngine;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class LakeLookdevWorkspaceTests
    {
        [Test] public void LakeLookdevDoesNotReplaceOriginalSceneOrInput()
        {
            Assert.That(File.Exists(LakeLookdevBuilder.ScenePath), Is.True);
            Assert.That(LakeLookdevBuilder.ScenePath, Is.Not.EqualTo(LakeLookdevBuilder.BaselineScene));
            Assert.That(File.Exists(LakeLookdevBuilder.ProfilePath), Is.True);
            var catalog = MapDebugWorkbench.LoadCatalog(); var item = catalog.cases.Find(x => x.id == "lookdev/lake-v3");
            Assert.That(item, Is.Not.Null); Assert.That(item.scenePath, Is.EqualTo(LakeLookdevBuilder.ScenePath));
        }
        [TestCase("lake-water-ink-v3.png", "348ef55bf7f29c20e68c4779782622c4905958a38119d0affa2b29312eaf56d3")]
        [TestCase("lake-shore-ink-v3.png", "85bd11c3a15ebbd5a36a0f7214b1a71ad6b14f21fee32c21b620fd69b8b9b779")]
        public void NativeCandidatePreservedAndImportedForRepeat(string filename, string hash)
        {
            string path = LakeLookdevBuilder.Sources + "/" + filename; var metrics = LakeLookdevBuilder.MeasureSource(path);
            Assert.That(metrics.width, Is.EqualTo(1254)); Assert.That(metrics.height, Is.EqualTo(1254)); Assert.That(metrics.sha256, Is.EqualTo(hash));
            Assert.That(float.IsNaN(metrics.edgeRatio) || float.IsInfinity(metrics.edgeRatio), Is.False);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.mipmapEnabled && importer.sRGBTexture, Is.True); Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.isReadable, Is.False);
        }
        [TestCase("ProjectRealm/Map/LakeWaterLookdev")]
        [TestCase("ProjectRealm/Map/LakeShoreLookdev")]
        public void CandidateShadersCompile(string name)
        { var shader = Shader.Find(name); Assert.That(shader, Is.Not.Null); Assert.That(ShaderUtil.ShaderHasError(shader), Is.False); }
        [Test] public void ComparisonReusesActualBaselineGeometry()
        {
            var baseline = AssetDatabase.GetDependencies(LakeLookdevBuilder.BaselineScene);
            var candidate = AssetDatabase.GetDependencies(LakeLookdevBuilder.ScenePath);
            foreach (string path in baseline)
                if (Path.GetFileName(path) == "WaterSurface.asset" || Path.GetFileName(path) == "BedAndBanks.asset")
                    CollectionAssert.Contains(candidate, path);
        }
    }
}
