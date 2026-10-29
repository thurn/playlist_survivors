using System.Linq;
using Demo2D.EditorTools;
using Demo2D.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Tests
{
    /// <summary>The committed project settings are what ProjectSetup.Configure writes.</summary>
    public class ProjectSetupTests
    {
        [Test]
        public void TheMacPlayerDrawsWithMetalThroughIL2CPP()
        {
            Assert.That(PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX), Is.EqualTo(new[] { GraphicsDeviceType.Metal }));
            Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone), Is.EqualTo(ScriptingImplementation.IL2CPP));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(PlayerSettings.enableFrameTimingStats, Is.True);
        }

        [Test]
        public void ThePipelineIsUrpWithThe2DRendererSortingByFeet()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(urp, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(urp), Is.EqualTo(ProjectSetup.PipelinePath));
            var renderer = new SerializedObject(urp).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            Assert.That(renderer, Is.InstanceOf<Renderer2DData>());
            var r = new SerializedObject(renderer);
            Assert.That(r.FindProperty("m_TransparencySortMode").intValue, Is.EqualTo((int)TransparencySortMode.CustomAxis));
            Assert.That(r.FindProperty("m_TransparencySortAxis").vector3Value, Is.EqualTo(Vector3.up));
            Assert.That(r.FindProperty("m_PostProcessData").objectReferenceValue, Is.Not.Null);
            Assert.That(urp.msaaSampleCount, Is.EqualTo(1));
        }

        [Test]
        public void TheBuildStartsFromTheEmptySceneAtTheBaseView()
        {
            Assert.That(EditorBuildSettings.scenes.First().path, Is.EqualTo(ProjectSetup.EmptyScenePath));
            var scene = EditorSceneManager.OpenScene(ProjectSetup.EmptyScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var camera = roots.Select(g => g.GetComponent<Camera>()).Single(c => c != null);
                Assert.That(camera.CompareTag("MainCamera"));
                Assert.That(camera.orthographic);
                Assert.That(camera.orthographicSize * 2 * TestCard.PixelsPerMetre, Is.EqualTo(540f));
                var lights = roots.SelectMany(g => g.GetComponents<Light2D>()).ToList();
                Assert.That(lights.Count(l => l.lightType == Light2D.LightType.Global), Is.EqualTo(1));
                var card = roots.Select(g => g.GetComponent<TestCard>()).Single(c => c != null);
                Assert.That(AssetDatabase.GetAssetPath(card.litMaterial), Is.EqualTo(ProjectSetup.SpriteLitPath));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
