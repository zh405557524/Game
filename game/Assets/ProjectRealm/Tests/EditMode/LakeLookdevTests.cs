using NUnit.Framework;
using ProjectRealm.Presentation.Map.Water;
using UnityEngine;

namespace ProjectRealm.Tests.Integration
{
    public sealed class LakeLookdevTests
    {
        private LakeLookdevProfile profile;
        private WaterBodyStudyDefinition definition;
        private Texture2D water, shore;
        [SetUp] public void Setup()
        {
            definition = ScriptableObject.CreateInstance<WaterBodyStudyDefinition>(); definition.SetDefaults(WaterStudyKind.Lake);
            profile = ScriptableObject.CreateInstance<LakeLookdevProfile>(); profile.baseline = definition;
            profile.waterColor = water = new Texture2D(2, 2); profile.shoreSediment = shore = new Texture2D(2, 2);
        }
        [TearDown] public void Teardown()
        { Object.DestroyImmediate(profile); Object.DestroyImmediate(definition); Object.DestroyImmediate(water); Object.DestroyImmediate(shore); }
        [Test] public void ProfileReferencesButDoesNotMutateBaseline()
        {
            string before = JsonUtility.ToJson(definition); Assert.That(profile.Validate(out var reason), Is.True, reason);
            profile.waterTileSize = 48; profile.shoreWidth = 2; Assert.That(profile.Validate(out _), Is.True);
            Assert.That(JsonUtility.ToJson(definition), Is.EqualTo(before));
        }
        [TestCase(0f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidTextureScaleRejected(float value)
        { profile.waterTileSize = value; Assert.That(profile.Validate(out _), Is.False); }
        [Test] public void NonLakeAndMissingSourceRejected()
        {
            definition.SetDefaults(WaterStudyKind.Pond); Assert.That(profile.Validate(out _), Is.False);
            definition.SetDefaults(WaterStudyKind.Lake); profile.shoreSediment = null; Assert.That(profile.Validate(out _), Is.False);
        }
        [Test] public void StageComparisonDoesNotMoveCameraOrChangeMeshAndInput()
        {
            var go = new GameObject("test camera", typeof(Camera), typeof(LakeLookdevView));
            var root = new GameObject("test lake"); var tiles = new GameObject("test tiles");
            var wet = new GameObject("water", typeof(MeshFilter), typeof(MeshRenderer)); wet.transform.SetParent(root.transform);
            var bank = new GameObject("banks", typeof(MeshFilter), typeof(MeshRenderer)); bank.transform.SetParent(root.transform);
            var mesh = new Mesh(); var material = new Material(Shader.Find("ProjectRealm/Map/WaterBodyStudy"));
            try
            {
                wet.GetComponent<MeshFilter>().sharedMesh = mesh;
                var view = go.GetComponent<LakeLookdevView>(); view.profile = profile; view.water = wet.GetComponent<MeshRenderer>(); view.banks = bank.GetComponent<MeshRenderer>();
                view.lakeRoot = root; view.tileRoot = tiles; view.waterStages = new[] { material, material, material, material }; view.bankStages = new[] { material, material, material, material };
                view.focus = new Vector3(7, 0, -9); view.zoom = 31; view.pitch = 60; view.phase = 8; view.Apply();
                var pose = go.transform.localToWorldMatrix; string input = JsonUtility.ToJson(definition), baselineMaterial = UnityEditor.EditorJsonUtility.ToJson(material);
                for (int stage = 0; stage < 4; stage++)
                {
                    view.stage = stage; view.Apply(); Assert.That(go.transform.localToWorldMatrix, Is.EqualTo(pose)); Assert.That(view.phase, Is.EqualTo(8));
                    Assert.That(wet.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                    Assert.That(JsonUtility.ToJson(definition), Is.EqualTo(input)); Assert.That(UnityEditor.EditorJsonUtility.ToJson(material), Is.EqualTo(baselineMaterial));
                }
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(root); Object.DestroyImmediate(tiles); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
    }
}
