using System.Linq;
using System.Text;
using Game.Client;
using Game.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Tests
{
    /// <summary>The project's foundations: its render settings, its boot scene, and that it draws.</summary>
    public class ProjectTests
    {
        [Test]
        public void EveryQualityLevelDrawsWithTheGamesForwardPlusPipelineAtMsaa4()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Tasks.PipelinePath);
            Assert.That(urp, Is.Not.Null);
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(urp));
            for (int i = 0; i < QualitySettings.count; i++)
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.SameAs(urp), QualitySettings.names[i]);
            Assert.That(urp.msaaSampleCount, Is.EqualTo(4));
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Tasks.RendererPath);
            Assert.That(renderer.renderingMode, Is.EqualTo(RenderingMode.ForwardPlus));
            Assert.That(renderer.rendererFeatures.All(f => f != null), "a renderer feature's script is missing");
        }

        [Test]
        public void TheNightStackHasSsaoAndTheCameraNoTemporalAntialiasing()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Tasks.RendererPath);
            Assert.That(renderer.rendererFeatures.Any(f => f is ScreenSpaceAmbientOcclusion && f.isActive), "no active SSAO feature");
            var scene = EditorSceneManager.OpenScene(Tasks.BootScenePath, OpenSceneMode.Single);
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).Single(c => c != null);
            Assert.That(camera.GetUniversalAdditionalCameraData().antialiasing, Is.EqualTo(AntialiasingMode.None));
        }

        [Test]
        public void TheBootSceneIsTheFirstBuildSceneAndHasACamera()
        {
            var first = EditorBuildSettings.scenes.First(s => s.enabled);
            Assert.That(first.path, Is.EqualTo(Tasks.BootScenePath));
            var scene = EditorSceneManager.OpenScene(Tasks.BootScenePath, OpenSceneMode.Single);
            var boot = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Boot>()).Single(b => b != null);
            var camera = new SerializedObject(boot).FindProperty("_camera").objectReferenceValue as Camera;
            Assert.That(camera, Is.Not.Null, "Boot has no camera");
        }

        [Test]
        public void TheBootCameraDrawsThroughUrp()
        {
            var scene = EditorSceneManager.OpenScene(Tasks.BootScenePath, OpenSceneMode.Single);
            var camera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).Single(c => c != null);
            var target = new RenderTexture(64, 36, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(64, 36, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                tex.ReadPixels(new Rect(0, 0, 64, 36), 0, 0);
                tex.Apply();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                target.Release();
            }
            // The clear colour, through the pipeline's post stack: dark, and the same everywhere.
            var pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            Assert.That(pixels.All(p => p.Equals(pixels[0])), "an empty scene drew something");
            Assert.That(pixels[0].r + pixels[0].g + pixels[0].b, Is.LessThan(3 * 64), $"too bright: {pixels[0]}");
        }

        [Test]
        public void RenderTagMarksAJpegAsTheVendorGuardRequires()
        {
            var tex = new Texture2D(8, 8);
            var jpeg = RenderTag.TagJpeg(tex.EncodeToJPG(), "test");
            Object.DestroyImmediate(tex);
            Assert.That(jpeg.Take(4), Is.EqualTo(new byte[] { 0xFF, 0xD8, 0xFF, 0xFE }));
            Assert.That(Encoding.ASCII.GetString(jpeg, 6, 9), Is.EqualTo("ps-render"));
            Assert.That(jpeg.Skip(jpeg.Length - 2), Is.EqualTo(new byte[] { 0xFF, 0xD9 }));
        }
    }
}
