using System.Linq;
using Champions.Client;
using Champions.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Champions.Tests
{
    public sealed class BootTests
    {
        [TearDown]
        public void CleanScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void TheBootSceneIsTheBuildsFirstAndDrawsTheBlackVoid()
        {
            Assert.That(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).FirstOrDefault(), Is.EqualTo(Tasks.BootScenePath));
            var scene = EditorSceneManager.OpenScene(Tasks.BootScenePath, OpenSceneMode.Single);
            var cameras = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).ToArray();
            Assert.That(cameras, Has.Length.EqualTo(1));
            var camera = cameras[0];
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(camera.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(camera.GetUniversalAdditionalCameraData().renderPostProcessing, "post (bloom, tonemapping)");
            var boot = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Boot>()).Single(b => b != null);
            Assert.That(new SerializedObject(boot).FindProperty("_camera").objectReferenceValue, Is.SameAs(camera));
        }

        [Test]
        public void LaunchOptionsReadTheGamesFlagsAndLeaveUnitysAlone()
        {
            var o = LaunchOptions.Parse(new[] { "player", "-batchmode", "-logFile", "out.log", "--fixed-time", "--seconds", "5",
                "--capture-stills", "dir", "--stills-at", "1,0.5" }, batchMode: true);
            Assert.That(o.Headless && o.FixedTime);
            Assert.That(o.Seconds, Is.EqualTo(5));
            Assert.That(o.StillsDir, Is.EqualTo("dir"));
            Assert.That(o.StillsAt, Is.EqualTo(new[] { 1.0, 0.5 }));
            Assert.That(o.Unrecognized, Is.Empty);
        }

        [Test]
        public void LaunchOptionsNoteWhatTheyDontKnow()
        {
            var o = LaunchOptions.Parse(new[] { "player", "--secs", "5", "--seconds" }, batchMode: false);
            Assert.That(o.Headless, Is.False);
            Assert.That(o.Unrecognized, Is.EquivalentTo(new[]
            {
                "--seconds: needs a value after it", "--secs: not a flag the game knows", "5: no flag takes this argument",
            }));
        }
    }
}
