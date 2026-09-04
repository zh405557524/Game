using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using ProjectRealm.UnityPresentation.Map.Mountain;
using UnityEditor;
using UnityEngine;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class MountainLookdevWorkspaceTests
    {
        [Test] public void NewLookdevDoesNotJoinOriginalFiveTerrainBuilder()
        {
            Assert.That(TerrainDebugSceneBuilder.IsBaseTerrainCase(new MapDebugCase { id = "lookdev/mountain-v1", layer = 0 }), Is.False);
            Assert.That(TerrainDebugSceneBuilder.IsBaseTerrainCase(new MapDebugCase { id = "terrain/unknown", layer = 0 }), Is.False);
            Assert.That(TerrainDebugSceneBuilder.IsBaseTerrainCase(new MapDebugCase { id = null, layer = 0 }), Is.False);
            Assert.That(TerrainDebugSceneBuilder.IsBaseTerrainCase(new MapDebugCase { id = "terrain/03_Mountain", layer = 0 }), Is.True);
            Assert.That(MapDebugWorkbench.LoadCatalog().cases.Count(TerrainDebugSceneBuilder.IsBaseTerrainCase), Is.EqualTo(5));
        }

        [Test] public void CandidateSeparatesSceneProfileGeneratedAssetsAndEvidence()
        {
            Assert.That(File.Exists(MountainLookdevBuilder.ScenePath), Is.True);
            var profile = AssetDatabase.LoadAssetAtPath<MountainLookdevProfile>(MountainLookdevBuilder.ProfilePath);
            Assert.That(profile, Is.Not.Null); Assert.That(profile.Validate(out var error), Is.True, error);
            var item = MapDebugWorkbench.LoadCatalog().cases.Single(x => x.id == MountainLookdevBuilder.CaseId);
            Assert.That(item.scenePath, Is.EqualTo(MountainLookdevBuilder.ScenePath));
            Assert.That(item.testDataPath, Is.EqualTo(MountainLookdevBuilder.InputFolder));
            Assert.That(item.generatedPath, Is.EqualTo(MountainLookdevBuilder.GeneratedFolder));
            Assert.That(item.evidencePath.Replace('\\', '/'), Does.Contain("/docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Mountain/"));
            var dependencies = AssetDatabase.GetDependencies(MountainLookdevBuilder.ScenePath, true);
            Assert.That(dependencies.Any(p => p.StartsWith(MountainLookdevBuilder.GeneratedFolder + "/") && p.EndsWith("/Mountain.asset")), Is.True);
            Assert.That(dependencies, Does.Not.Contain(MountainLookdevBuilder.ProfilePath), "A built revision must not read future authoring edits.");
            Assert.That(dependencies.Any(p => p.StartsWith(MountainLookdevBuilder.GeneratedFolder + "/") && p.EndsWith("/ProfileSnapshot.asset")), Is.True);
            Assert.That(dependencies, Does.Not.Contain(ProjectRealmWorkspaceLayout.LearningScene));
            Assert.That(dependencies.Any(p => p.Contains("DesignGuidedV1") || p.Contains("高山.png")), Is.False, "A full concept painting must not become a mountain material.");
        }

        [Test] public void OriginalVisualReferenceKeepsItsHashAndIsNotATerrainTexture()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MountainLookdevProfile>(MountainLookdevBuilder.ProfilePath);
            Assert.That(profile.referenceVersion, Is.EqualTo("OriginalReferenceV1"));
            string original = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", profile.referencePath));
            Assert.That(MountainLookdevBuilder.Sha(original), Is.EqualTo("8b49fbff7c3de999f46562bd70b960be80a99e250f99410210c666e124871d4f"));
            Assert.That(AssetDatabase.GetAssetPath(profile.wash), Does.EndWith("/mountain-wash-v1.png"));
        }

        [Test] public void SourceImportKeepsNativeSizeAndCorrectColorSpace()
        {
            foreach (string name in MountainLookdevBuilder.SourceNames.Take(3))
            {
                string path = MountainLookdevBuilder.Sources + "/" + name + ".png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var native = MountainLookdevBuilder.MeasureSource(path);
                Assert.That(texture.width, Is.EqualTo(native.width)); Assert.That(texture.height, Is.EqualTo(native.height));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
                Assert.That(importer.mipmapEnabled, Is.True); Assert.That(importer.isReadable, Is.False);
                Assert.That(importer.sRGBTexture, Is.EqualTo(name == "mountain-wash-v1"));
            }
        }

        [Test] public void InvalidAlphaCannotLeakIntoSceneMaterials()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MountainLookdevProfile>(MountainLookdevBuilder.ProfilePath);
            var metric = MountainLookdevBuilder.MeasureSource(MountainLookdevBuilder.Sources + "/pine-clump-v1.png");
            if (metric.transparentPixels == 0 && profile.pine == null)
            {
                Assert.That(profile.pine, Is.Null); Assert.That(profile.showTrees, Is.False);
                Assert.That(profile.foliageStatus, Is.EqualTo("BlockedInvalidAlpha"));
                Assert.That(AssetDatabase.GetDependencies(MountainLookdevBuilder.ScenePath, true), Does.Not.Contain(MountainLookdevBuilder.Sources + "/pine-clump-v1.png"));
            }
            else if (profile.pine != null)
                Assert.That(MountainLookdevBuilder.MeasureSource(AssetDatabase.GetAssetPath(profile.pine)).transparentPixels, Is.GreaterThan(0));
        }

        [Test] public void LocalRepairsAreVersionedAndKeepNativeSources()
        {
            string root = MountainLookdevBuilder.Sources + "/";
            var nativeStrokes = MountainLookdevBuilder.MeasureSource(root + "mountain-strokes-v2.png");
            var repairedStrokes = MountainLookdevBuilder.MeasureSource(root + "mountain-strokes-v3-seamless-local.png");
            var nativePine = MountainLookdevBuilder.MeasureSource(root + "pine-clump-v1.png");
            var repairedPine = MountainLookdevBuilder.MeasureSource(root + "pine-clump-v4-alpha-local.png");

            Assert.That(nativeStrokes.sha256, Is.EqualTo("bac6ec27f2ce2340d930f8c94d5882a4928576ea44501d5aa85f4d9e120d2576"));
            Assert.That(repairedStrokes.sha256, Is.EqualTo("973f76b59bd2dff191ebccbabb7a5561e25da6c74256212901079e5d0374fa88"));
            Assert.That(repairedStrokes.width, Is.EqualTo(nativeStrokes.width));
            Assert.That(repairedStrokes.height, Is.EqualTo(nativeStrokes.height));
            Assert.That(repairedStrokes.edgeMeanDifference, Is.LessThan(0.00001), "Opposite edges should match after the local periodic repair.");
            Assert.That(repairedStrokes.edgeMeanDifference, Is.LessThan(nativeStrokes.edgeMeanDifference));

            Assert.That(nativePine.sha256, Is.EqualTo("3ec91703dfd092e9c778ad432d32afe6964467394883dc21ace7e895fc2e614c"));
            Assert.That(repairedPine.sha256, Is.EqualTo("1418f6521329b76c6fc7346aca7a77f096432ae00a16a697579869750d6136fe"));
            Assert.That(nativePine.transparentPixels, Is.Zero, "The native RGB checkerboard remains an unmodified failed source.");
            Assert.That(repairedPine.width, Is.EqualTo(nativePine.width));
            Assert.That(repairedPine.height, Is.EqualTo(nativePine.height));
            Assert.That(repairedPine.transparentPixels, Is.GreaterThan(0));
            Assert.That(repairedPine.opaquePixels, Is.GreaterThan(0));
        }

        [Test] public void MountainShadersCompile() => Assert.DoesNotThrow(MountainLookdevBuilder.ValidateShaders);
    }
}
