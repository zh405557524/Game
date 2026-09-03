using System;
using NUnit.Framework;
using ProjectRealm.Presentation.Map.Mountain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ProjectRealm.Tests.Integration
{
    public sealed class MountainLookdevTests
    {
        private MountainLookdevProfile profile;
        [SetUp] public void SetUp() { profile = ScriptableObject.CreateInstance<MountainLookdevProfile>(); profile.cells = 48; }
        [TearDown] public void TearDown() { Object.DestroyImmediate(profile); }

        [Test] public void GeometryHasFiniteUpwardNormalsValidIndicesAndBounds()
        {
            var mesh = MountainLookdevGeometry.Build(profile);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(49 * 49));
                Assert.That(mesh.triangles.Length, Is.EqualTo(48 * 48 * 6));
                Assert.That(mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    var v = mesh.vertices[i]; var n = mesh.normals[i];
                    Assert.That(MountainLookdevProfile.Finite(v) && MountainLookdevProfile.Finite(n), Is.True);
                    Assert.That(v.y, Is.GreaterThanOrEqualTo(0)); Assert.That(n.y, Is.GreaterThan(0));
                    Assert.That(n.magnitude, Is.EqualTo(1).Within(.0001));
                }
                foreach (int index in mesh.triangles) Assert.That(index, Is.InRange(0, mesh.vertexCount - 1));
                Assert.That(mesh.bounds.size.x, Is.EqualTo(profile.size.x).Within(.001));
                Assert.That(mesh.bounds.size.z, Is.EqualTo(profile.size.y).Within(.001));
                Assert.That(mesh.bounds.max.y, Is.InRange(50, 80));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test] public void GeometryIsDeterministicAndDoesNotDependOnImagePixels()
        {
            var a = MountainLookdevGeometry.Build(profile);
            profile.wash = Texture2D.blackTexture; profile.strokes = Texture2D.whiteTexture;
            var b = MountainLookdevGeometry.Build(profile);
            try
            { CollectionAssert.AreEqual(a.vertices, b.vertices); CollectionAssert.AreEqual(a.normals, b.normals); CollectionAssert.AreEqual(a.triangles, b.triangles); }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test] public void PeakRidgeAndValleyControlsActuallyAffectHeightField()
        {
            float peak = MountainLookdevGeometry.SampleHeight(profile, -5, 39);
            profile.peaks[0].height += 10;
            Assert.That(MountainLookdevGeometry.SampleHeight(profile, -5, 39), Is.GreaterThan(peak + 9));
            profile.peaks = new[] { new MountainPeak("separate", -70, -70, 15, 5, 5) }; profile.rockRelief = 0;
            profile.ridges = new[] { new MountainRidge("test", 8, new Vector3(-10, 12, 0), new Vector3(10, 12, 0)) };
            profile.valleys = Array.Empty<MountainValley>();
            float ridge = MountainLookdevGeometry.SampleHeight(profile, 0, 0);
            Assert.That(ridge, Is.EqualTo(12).Within(.02));
            profile.valleys = new[] { new MountainValley(new Vector2(0, -10), new Vector2(0, 10), 5, 4) };
            Assert.That(MountainLookdevGeometry.SampleHeight(profile, 0, 0), Is.EqualTo(ridge - 4).Within(.02));
        }

        [Test] public void LargeGridsUse32BitIndices()
        {
            profile.cells = 256;
            var mesh = MountainLookdevGeometry.Build(profile);
            try { Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32)); }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test] public void InvalidGeometryAndCameraSettingsFailBeforeAllocation()
        {
            profile.peaks[0].height = float.NaN;
            Assert.That(profile.Validate(out _, false), Is.False);
            Assert.Throws<ArgumentException>(() => MountainLookdevGeometry.Build(profile));
            profile.peaks[0].height = 60; profile.defaultPitch = 80;
            Assert.That(profile.Validate(out _, false), Is.False);
        }

        [Test] public void CameraPanZoomPitchResetNeverMutateProfileOrMesh()
        {
            var go = new GameObject("camera test", typeof(Camera), typeof(MountainLookdevView));
            var terrain = new GameObject("terrain", typeof(MeshFilter), typeof(MeshRenderer));
            var mesh = MountainLookdevGeometry.Build(profile);
            try
            {
                var view = go.GetComponent<MountainLookdevView>(); view.profile = profile;
                view.terrain = terrain.GetComponent<MeshRenderer>(); terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
                view.ResetView(); string before = JsonUtility.ToJson(profile); var vertices = mesh.vertices;
                view.Navigate(Vector2.up, 0, false, .1f); Assert.That(view.focus.z, Is.GreaterThan(profile.defaultFocus.z));
                float pitch = view.pitch;
                view.Navigate(Vector2.zero, 120, false, 0); Assert.That(view.zoom, Is.LessThan(profile.defaultZoom)); Assert.That(view.pitch, Is.EqualTo(pitch));
                float zoom = view.zoom;
                view.Navigate(Vector2.zero, 120, true, 0); Assert.That(view.pitch, Is.GreaterThan(pitch)); Assert.That(view.zoom, Is.EqualTo(zoom));
                view.Navigate(Vector2.zero, 100000, true, 0); Assert.That(view.pitch, Is.EqualTo(75));
                view.Navigate(Vector2.zero, -100000, true, 0); Assert.That(view.pitch, Is.EqualTo(35));
                view.Navigate(Vector2.zero, 100000, false, 0); Assert.That(view.zoom, Is.EqualTo(profile.minZoom));
                view.Navigate(Vector2.zero, -100000, false, 0); Assert.That(view.zoom, Is.EqualTo(profile.maxZoom));
                view.Navigate(Vector2.zero, 0, false, 0, true); view.Apply();
                Assert.That(view.focus, Is.EqualTo(profile.defaultFocus)); Assert.That(view.pitch, Is.EqualTo(48)); Assert.That(view.zoom, Is.EqualTo(profile.defaultZoom));
                Assert.That(go.transform.eulerAngles.y, Is.EqualTo(0).Within(.0001));
                Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(before));
                Assert.That(terrain.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                CollectionAssert.AreEqual(vertices, mesh.vertices);
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(terrain); Object.DestroyImmediate(go); }
        }

        [Test] public void MissingPineIsExplicitAndCannotAppearAsAnEnabledTreeLayer()
        {
            profile.wash = Texture2D.whiteTexture; profile.strokes = Texture2D.whiteTexture; profile.paper = Texture2D.whiteTexture;
            profile.pine = null; profile.showTrees = false;
            Assert.That(profile.Validate(out _), Is.True);
            profile.showTrees = true;
            Assert.That(profile.Validate(out var error), Is.False); StringAssert.Contains("transparent", error);
        }

        [Test] public void ActualInputSystemEventsDrivePanZoomPitchLayerTogglesAndReset()
        {
            // The official fixture isolates update counters and restores all real editor input devices/settings.
            var inputFixture = new InputTestFixture(); inputFixture.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>(); var mouse = InputSystem.AddDevice<Mouse>();
            var go = new GameObject("input camera", typeof(Camera), typeof(MountainLookdevView));
            var ground = new GameObject("input ground", typeof(MeshRenderer));
            try
            {
                keyboard.MakeCurrent(); mouse.MakeCurrent();
                var view = go.GetComponent<MountainLookdevView>(); view.profile = profile; view.terrain = ground.GetComponent<MeshRenderer>();
                view.showHud = false; view.ResetView(); string before = JsonUtility.ToJson(profile);
                Send(new KeyboardState(Key.W), Vector2.zero); Assert.That(view.focus.z, Is.GreaterThan(profile.defaultFocus.z));
                float z = view.focus.z;
                Send(new KeyboardState(Key.S), Vector2.zero); Assert.That(view.focus.z, Is.LessThan(z));
                Send(new KeyboardState(Key.D), Vector2.zero); Assert.That(view.focus.x, Is.GreaterThan(0));
                float x = view.focus.x;
                Send(new KeyboardState(Key.A), Vector2.zero); Assert.That(view.focus.x, Is.LessThan(x));
                Send(new KeyboardState(), new Vector2(0, 120)); Assert.That(view.zoom, Is.LessThan(profile.defaultZoom));
                float zoom = view.zoom;
                Send(new KeyboardState(Key.LeftShift), new Vector2(0, 120)); Assert.That(view.pitch, Is.GreaterThan(48)); Assert.That(view.zoom, Is.EqualTo(zoom));
                Send(new KeyboardState(Key.Digit1), Vector2.zero); Assert.That(view.surfaceStage, Is.EqualTo(0));
                bool mist = view.mist; Send(new KeyboardState(Key.G), Vector2.zero); Assert.That(view.mist, Is.Not.EqualTo(mist));
                Send(new KeyboardState(Key.F), Vector2.zero); Assert.That(view.zoom, Is.EqualTo(profile.defaultZoom)); Assert.That(view.pitch, Is.EqualTo(48)); Assert.That(view.focus, Is.EqualTo(profile.defaultFocus));
                Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(before));

                void Send(KeyboardState state, Vector2 scroll)
                {
                    InputSystem.QueueStateEvent(keyboard, state); InputSystem.QueueStateEvent(mouse, new MouseState { scroll = scroll });
                    InputSystem.Update(); view.PollInput(.05f);
                }
            }
            finally
            { InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); Object.DestroyImmediate(go); Object.DestroyImmediate(ground); inputFixture.TearDown(); }
        }
    }
}
