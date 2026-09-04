using System;
using NUnit.Framework;
using ProjectRealm.UnityPresentation.Map.Water;
using UnityEngine;

namespace ProjectRealm.Tests.Integration
{
    public sealed class WaterBodyStudyTests
    {
        private WaterBodyStudyDefinition definition;
        [SetUp] public void Setup() => definition = ScriptableObject.CreateInstance<WaterBodyStudyDefinition>();
        [TearDown] public void Teardown() => UnityEngine.Object.DestroyImmediate(definition);

        [TestCase(WaterStudyKind.Stream)] [TestCase(WaterStudyKind.Lake)] [TestCase(WaterStudyKind.Pond)] [TestCase(WaterStudyKind.Wetland)] [TestCase(WaterStudyKind.Coast)]
        public void DefaultFieldsContainBothWaterAndDryLand(WaterStudyKind kind)
        {
            definition.SetDefaults(kind); Assert.That(definition.Validate(out var reason), Is.True, reason);
            var field = new WaterStudyField(definition); int wet = 0, dry = 0;
            for (int x = 0; x <= 50; x++) for (int z = 0; z <= 50; z++)
            {
                var p = new Vector2((x / 50f - 0.5f) * definition.size.x, (z / 50f - 0.5f) * definition.size.y); float shore = field.Shore(p);
                Assert.That(float.IsNaN(shore) || float.IsInfinity(shore), Is.False);
                if (shore < -0.3f) { wet++; Assert.That(field.GroundHeight(p, shore), Is.LessThan(field.Level(p))); }
                if (shore > definition.bankWidth) { dry++; Assert.That(field.GroundHeight(p, shore), Is.GreaterThan(field.Level(p))); }
            }
            Assert.That(wet, Is.GreaterThan(20)); Assert.That(dry, Is.GreaterThan(20));
        }
        [TestCase(WaterStudyKind.Stream)] [TestCase(WaterStudyKind.Lake)] [TestCase(WaterStudyKind.Pond)] [TestCase(WaterStudyKind.Wetland)] [TestCase(WaterStudyKind.Coast)]
        public void SurfaceAndBedAreFiniteDeterministicAndFaceUp(WaterStudyKind kind)
        {
            definition.SetDefaults(kind); var field = new WaterStudyField(definition);
            var a = kind == WaterStudyKind.Stream ? WaterBodyStudyGeometry.Stream(field) : WaterBodyStudyGeometry.Grid(field, true, 48);
            var b = kind == WaterStudyKind.Stream ? WaterBodyStudyGeometry.Stream(field) : WaterBodyStudyGeometry.Grid(field, true, 48);
            var bed = WaterBodyStudyGeometry.Grid(field, false, 48);
            try
            {
                Check(a); Check(bed); CollectionAssert.AreEqual(a.vertices, b.vertices); CollectionAssert.AreEqual(a.uv2, b.uv2);
                Assert.That(a.uv2.Length, Is.EqualTo(a.vertexCount));
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(bed); }
        }
        [Test] public void StreamRunsDownhillWithoutChangingRiverInput()
        {
            definition.SetDefaults(WaterStudyKind.Stream); var field = new WaterStudyField(definition);
            for (int i = 1; i < field.stream.Length; i++) Assert.That(field.Level(field.stream[i].position), Is.LessThan(field.Level(field.stream[i - 1].position)));
            Assert.That(definition.depth, Is.LessThan(1)); Assert.That(definition.stream[0].width, Is.LessThan(5));
        }
        [TestCase(WaterStudyKind.Lake)] [TestCase(WaterStudyKind.Coast)]
        public void IslandsRemainDryAndSurroundingWaterStaysWet(WaterStudyKind kind)
        {
            definition.SetDefaults(kind); var field = new WaterStudyField(definition);
            foreach (var island in definition.islands)
            {
                Assert.That(field.Shore(island.center), Is.GreaterThan(0));
                Assert.That(field.Shore(island.center + new Vector2(island.radius.x * 1.8f, 0)), Is.LessThan(0));
            }
        }
        [Test] public void WetlandHasSeparatedPoolsAndSmallDepth()
        {
            definition.SetDefaults(WaterStudyKind.Wetland); var field = new WaterStudyField(definition);
            Assert.That(definition.basins.Length, Is.EqualTo(6)); Assert.That(definition.depth, Is.LessThan(0.6f));
            Assert.That(field.Shore(new Vector2(-27, -17)), Is.LessThan(0)); Assert.That(field.Shore(new Vector2(-42, -17)), Is.GreaterThan(0));
        }
        [Test] public void PondHasSmallerScaleThanLake()
        {
            definition.SetDefaults(WaterStudyKind.Lake); float lake = definition.basins[0].radius.x;
            definition.SetDefaults(WaterStudyKind.Pond); Assert.That(definition.basins[0].radius.x, Is.LessThan(lake * 0.5f)); Assert.That(definition.size.x, Is.LessThan(70));
        }
        [Test] public void CoastHasLandOnLeftAndOpenSeaOnRight()
        {
            definition.SetDefaults(WaterStudyKind.Coast); var field = new WaterStudyField(definition);
            for (int z = -50; z <= 50; z += 10) { Assert.That(field.Shore(new Vector2(-70, z)), Is.GreaterThan(0)); Assert.That(field.Shore(new Vector2(75, z)), Is.LessThan(0)); }
        }
        [TestCase(WaterStudyKind.Stream)] [TestCase(WaterStudyKind.Lake)] [TestCase(WaterStudyKind.Pond)] [TestCase(WaterStudyKind.Wetland)] [TestCase(WaterStudyKind.Coast)]
        public void ReferenceDetailsAreSeparateAndDeterministic(WaterStudyKind kind)
        {
            definition.SetDefaults(kind); var field = new WaterStudyField(definition); var a = WaterBodyStudyGeometry.ReferenceDetails(field); var b = WaterBodyStudyGeometry.ReferenceDetails(field);
            try { Assert.That(a.vertexCount, Is.GreaterThan(0)); CollectionAssert.AreEqual(a.vertices, b.vertices); CollectionAssert.AreEqual(a.colors, b.colors); }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }
        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidDepthCannotGenerate(float value)
        {
            definition.SetDefaults(WaterStudyKind.Lake); definition.depth = value;
            Assert.That(definition.Validate(out _), Is.False); Assert.Throws<ArgumentException>(() => new WaterStudyField(definition));
        }
        [Test] public void MissingBasinAndUpstreamReversalAreRejected()
        {
            definition.SetDefaults(WaterStudyKind.Lake); definition.basins = Array.Empty<StudyBasin>(); Assert.That(definition.Validate(out _), Is.False);
            definition.SetDefaults(WaterStudyKind.Stream); definition.stream[2].position.x = -50; Assert.That(definition.Validate(out _), Is.False);
        }
        [TestCase(WaterStudyKind.Stream)] [TestCase(WaterStudyKind.Lake)] [TestCase(WaterStudyKind.Pond)] [TestCase(WaterStudyKind.Wetland)] [TestCase(WaterStudyKind.Coast)]
        public void GroundColorIsContinuousAcrossShore(WaterStudyKind kind)
        {
            definition.SetDefaults(kind); var field = new WaterStudyField(definition);
            Color a = field.GroundColor(Vector2.zero, -0.0001f), b = field.GroundColor(Vector2.zero, 0.0001f);
            Assert.That(Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b), Is.LessThan(0.001f));
        }
        private static void Check(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            foreach (var p in v) Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z), Is.False);
            for (int i = 0; i < t.Length; i += 3) Assert.That(Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y, Is.GreaterThan(0));
        }
    }
}
