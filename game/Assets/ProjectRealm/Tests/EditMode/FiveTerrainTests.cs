using NUnit.Framework;
using ProjectRealm.Presentation.Map;
using UnityEngine;

namespace ProjectRealm.Tests.Integration
{
    public sealed class FiveTerrainTests
    {
        private FiveTerrainDefinition data;
        [SetUp] public void SetUp() => data = ScriptableObject.CreateInstance<FiveTerrainDefinition>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(data);

        [Test] public void SameSeedProducesIdenticalGeometryWithoutTouchingGlobalRandom()
        {
            var before = Random.state;
            var copy = ScriptableObject.CreateInstance<FiveTerrainDefinition>();
            try
            {
                for (int i = -100; i <= 100; i += 7) Assert.That(data.Height(i, i * 0.6f), Is.EqualTo(copy.Height(i, i * 0.6f)));
                Assert.That(Random.state, Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test] public void LandformsHaveDifferentRelief()
        {
            var plain = data.Focus(LandformKind.Plain); var mountain = data.Focus(LandformKind.Mountain);
            var plateau = data.Focus(LandformKind.Plateau); var basin = data.Focus(LandformKind.Basin);
            Assert.That(data.Height(mountain.x, mountain.y), Is.GreaterThan(data.Height(plain.x, plain.y) + 25));
            Assert.That(data.Height(plateau.x, plateau.y), Is.GreaterThan(data.Height(plain.x, plain.y) + 25));
            Assert.That(data.Height(basin.x + 53, basin.y), Is.GreaterThan(data.Height(basin.x, basin.y) + 7));
            Assert.That(data.Normal(plain.x, plain.y).y, Is.GreaterThan(0.97f));
            Assert.That(data.Normal(plateau.x, plateau.y).y, Is.GreaterThan(0.95f));
        }

        [Test] public void FiveRegionCentersHaveTheExpectedDominantWeight()
        {
            for (int i = 0; i < 5; i++)
            {
                Vector2 point = data.Focus((LandformKind)i); Color w = data.Weights(point.x, point.y);
                float[] values = { 1-w.r-w.g-w.b-w.a,w.r,w.g,w.b,w.a };
                for (int j = 0; j < 5; j++) if (i != j) Assert.That(values[i], Is.GreaterThan(values[j]), $"{i} vs {j}");
            }
        }

        [Test] public void ChunksShareExactEdgePositionNormalAndWeights()
        {
            var left = data.BuildChunk(2, 1); var right = data.BuildChunk(3, 1);
            try
            {
                int n = data.cellsPerChunk;
                for (int j = 0; j <= n; j++)
                {
                    int a = j*(n+1)+n, b=j*(n+1);
                    Assert.That(left.vertices[a], Is.EqualTo(right.vertices[b]));
                    Assert.That(left.normals[a], Is.EqualTo(right.normals[b]));
                    Assert.That(left.colors[a], Is.EqualTo(right.colors[b]));
                    Assert.That(left.uv2[a], Is.EqualTo(right.uv2[b]));
                }
                Assert.That(left.triangles.Length, Is.EqualTo(n*n*6));
                Assert.That(Vector3.Cross(left.vertices[left.triangles[1]]-left.vertices[left.triangles[0]],
                    left.vertices[left.triangles[2]]-left.vertices[left.triangles[0]]).y, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
        }

        [Test] public void WeightsAndHeightsStayFiniteAndNormalized()
        {
            for (int z = -120; z <= 120; z += 1)
            for (int x = -180; x <= 180; x += 1)
            {
                var weights = data.Weights(x, z); float sum = weights.r + weights.g + weights.b + weights.a;
                Assert.That(sum, Is.InRange(0f, 1.00001f));
                Assert.That(data.Height(x, z), Is.InRange(0f, 110f));
                Assert.That(data.Normal(x, z).magnitude, Is.EqualTo(1).Within(0.00001f));
            }
        }

        [Test] public void CameraFocusResetAndLimitsDoNotModifyDefinition()
        {
            var go = new GameObject("Test camera", typeof(Camera), typeof(FiveTerrainCamera));
            try
            {
                var controller = go.GetComponent<FiveTerrainCamera>(); controller.Configure(data);
                controller.FocusTerrain(2, true); Assert.That(controller.Selected, Is.EqualTo(2));
                Assert.That(controller.Zoom, Is.LessThan(70));
                controller.Home(true); Assert.That(controller.Selected, Is.EqualTo(-1));
                Assert.That(go.GetComponent<Camera>().orthographic, Is.True);
                Assert.That(controller.Pitch, Is.InRange(30, 75));
                Assert.That(data.seed, Is.EqualTo(1628));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(LandformKind.Plain)]
        [TestCase(LandformKind.Hills)]
        [TestCase(LandformKind.Mountain)]
        [TestCase(LandformKind.Plateau)]
        [TestCase(LandformKind.Basin)]
        public void IsolatedSamplesHaveFiniteGeometryAndUpwardNormals(LandformKind kind)
        {
            var mesh=data.BuildIsolated(kind,24);
            try
            {
                Assert.That(mesh.vertexCount,Is.EqualTo(625));
                foreach(var p in mesh.vertices)Assert.That(p.y,Is.InRange(0,110));
                foreach(var n in mesh.normals)Assert.That(n.y,Is.GreaterThan(0));
                Assert.That(mesh.triangles.Length,Is.EqualTo(24*24*6));
            }
            finally{Object.DestroyImmediate(mesh);}
        }

        [Test] public void IsolatedPlainDoesNotInheritNearbyMountainPlateauOrBasin()
        {
            for(int x=-75;x<=75;x+=5)
            for(int z=-60;z<=60;z+=5)
                Assert.That(data.IsolatedHeight(LandformKind.Plain,x,z),Is.InRange(2.7f,4.6f));
        }
    }
}
