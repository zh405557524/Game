using System;
using NUnit.Framework;
using ProjectRealm.Presentation.Map.Water;
using UnityEngine;

namespace ProjectRealm.Tests.Integration
{
    public sealed class RiverStudyTests
    {
        private RiverStudyDefinition definition;
        [SetUp] public void SetUp() => definition = ScriptableObject.CreateInstance<RiverStudyDefinition>();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(definition);

        [Test] public void DefaultProfileHasValidUpstreamToDownstreamStations()
        {
            Assert.That(definition.Validate(out string reason), Is.True, reason);
            var samples = RiverStudyGeometry.Sample(definition);
            Assert.That(samples.Length, Is.EqualTo(169));
            Assert.That(Vector2.Distance(samples[0].position, definition.stations[0].position), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(samples[samples.Length - 1].position, definition.stations[6].position), Is.LessThan(0.001f));
            for (int i = 1; i < samples.Length; i++)
            {
                Assert.That(samples[i].distance, Is.GreaterThan(samples[i - 1].distance));
                Assert.That(samples[i].tangent.magnitude, Is.EqualTo(1).Within(0.001f));
                Assert.That(samples[i].width, Is.InRange(9, 23));
            }
        }

        [Test] public void WaterUvUsesArcLengthAndTrianglesFaceUp()
        {
            var samples = RiverStudyGeometry.Sample(definition); var mesh = RiverStudyGeometry.Water(samples);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(2197));
                Assert.That(mesh.triangles.Length / 3, Is.EqualTo(4032));
                for (int i = 0; i < samples.Length; i++)
                {
                    Assert.That(mesh.uv[i * 13].x, Is.EqualTo(0)); Assert.That(mesh.uv[i * 13 + 12].x, Is.EqualTo(1));
                    Assert.That(mesh.uv[i * 13].y, Is.EqualTo(samples[i].distance));
                }
                AssertFiniteAndUp(mesh);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void SamplingAndMeshesAreDeterministic()
        {
            var a = RiverStudyGeometry.Water(RiverStudyGeometry.Sample(definition));
            var b = RiverStudyGeometry.Water(RiverStudyGeometry.Sample(definition));
            try { CollectionAssert.AreEqual(a.vertices, b.vertices); CollectionAssert.AreEqual(a.uv, b.uv); }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }

        [Test] public void GeneratedBedIsBelowWaterAtChannelSamples()
        {
            var samples = RiverStudyGeometry.Sample(definition);
            foreach (var sample in samples)
            {
                var left = new Vector2(-sample.tangent.y, sample.tangent.x);
                foreach (float side in new[] { -0.4f, 0f, 0.4f })
                {
                    float d = RiverStudyGeometry.ShoreDistance(sample.position + left * (sample.width * side), samples);
                    Assert.That(RiverStudyGeometry.BedHeight(d, 0.5f), Is.LessThan(RiverStudyGeometry.WaterHeight));
                }
            }
        }

        [Test] public void GroundMeshIsFiniteAndFacesUp()
        {
            var mesh = RiverStudyGeometry.Ground(definition, RiverStudyGeometry.Sample(definition), 32, 24);
            try { AssertFiniteAndUp(mesh); Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount)); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void DiagnosticArrowHeadsPointDownstreamAndFaceUp()
        {
            var samples = RiverStudyGeometry.Sample(definition);
            var mesh = RiverStudyGeometry.FlowArrows(samples, definition.groundSize);
            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(15)); AssertFiniteAndUp(mesh);
                foreach (var p in mesh.vertices)
                {
                    Assert.That(Mathf.Abs(p.x), Is.LessThan(definition.groundSize.x * 0.5f));
                    Assert.That(Mathf.Abs(p.z), Is.LessThan(definition.groundSize.y * 0.5f));
                }
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i += 3)
                {
                    Vector3 tail = (vertices[i + 1] + vertices[i + 2]) * 0.5f;
                    Vector3 center = (vertices[i] * 1.5f + tail * 2.4f) / 3.9f;
                    RiverSample nearest = samples[0]; float distance = float.MaxValue;
                    foreach (var sample in samples)
                    {
                        float d = Vector2.Distance(sample.position, new Vector2(center.x, center.z));
                        if (d < distance) { distance = d; nearest = sample; }
                    }
                    var direction = (vertices[i] - tail).normalized;
                    Assert.That(Vector2.Dot(new Vector2(direction.x, direction.z), nearest.tangent), Is.GreaterThan(0.999f));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [TestCase(-2f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void RejectsInvalidWidth(float width)
        {
            definition.stations[2].width = width;
            Assert.That(definition.Validate(out _), Is.False);
            Assert.Throws<ArgumentException>(() => RiverStudyGeometry.Sample(definition));
        }

        [Test] public void RejectsRepeatedAdjacentStations()
        {
            definition.stations[1].position = definition.stations[0].position;
            Assert.That(definition.Validate(out _), Is.False);
        }

        [Test] public void RejectsUnsafeGroundResolution()
            => Assert.Throws<ArgumentOutOfRangeException>(() => RiverStudyGeometry.Ground(definition, RiverStudyGeometry.Sample(definition), 0, 24));

        private static void AssertFiniteAndUp(Mesh mesh)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            foreach (var p in vertices)
                Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z), Is.False);
            for (int i = 0; i < triangles.Length; i += 3)
                Assert.That(Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]).y, Is.GreaterThan(0));
        }
    }
}
