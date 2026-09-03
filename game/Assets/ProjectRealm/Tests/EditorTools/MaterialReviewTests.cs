using System;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using UnityEditor;
using UnityEngine;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class MaterialReviewTests
    {
        [Test]
        public void RepeatedPatternHasHighCorrelation()
        {
            var random = new System.Random(1628);
            var tile = new double[32 * 32];
            for (var i = 0; i < tile.Length; i++) tile[i] = random.NextDouble();
            var image = new double[96 * 96];
            for (var y = 0; y < 96; y++)
            for (var x = 0; x < 96; x++) image[y * 96 + x] = tile[(y % 32) * 32 + x % 32];
            var result = MaterialRenderQualityAnalyzer.Measure(image, 96, 96, 3);
            Assert.That(result.HorizontalCorrelation, Is.GreaterThan(0.99));
            Assert.That(result.VerticalCorrelation, Is.GreaterThan(0.99));
        }

        [Test]
        public void IndependentNoiseDoesNotLookPeriodic()
        {
            var random = new System.Random(1628);
            var image = new double[96 * 96];
            for (var i = 0; i < image.Length; i++) image[i] = random.NextDouble();
            Assert.That(MaterialRenderQualityAnalyzer.Measure(image, 96, 96, 3).MaxAbsoluteCorrelation, Is.LessThan(0.08));
        }

        [Test]
        public void FlatImageIsReportedAsZeroContrastNotFalseTextureDetail()
        {
            var image = new double[96 * 96];
            for (var i = 0; i < image.Length; i++) image[i] = 0.5;
            var result = MaterialRenderQualityAnalyzer.Measure(image, 96, 96, 3);
            Assert.That(result.CoarseContrast, Is.Zero.Within(1e-8));
            Assert.That(result.MaxAbsoluteCorrelation, Is.Zero);
        }

        [TestCase("plain", "terrain-plain-v2", false)]
        [TestCase("hills", "terrain-hills-v4", true)]
        [TestCase("mountain", "terrain-mountain-v4", true)]
        [TestCase("plateau", "terrain-plateau-v1", false)]
        [TestCase("basin", "terrain-basin-v2", true)]
        public void SelectedMaterialsKeepExpectedTextureAndShader(string name, string texture, bool stochastic)
        {
            const string root = "Assets/ProjectRealm/Presentation/Map/Materials/";
            var suffix = stochastic ? "-v2" : string.Empty;
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{root}Generated/{name}-ink-terrain{suffix}.mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo($"{root}Textures/{texture}.png"));
            Assert.That(material.shader.name, Is.EqualTo(stochastic ? "ProjectRealm/Map/InkTerrainStochastic" : "ProjectRealm/Map/InkTerrainMaterial"));
            Assert.That(material.shader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
            if (stochastic) Assert.That(material.GetFloat("_AntiTileStrength"), Is.EqualTo(1f));
        }
    }
}
