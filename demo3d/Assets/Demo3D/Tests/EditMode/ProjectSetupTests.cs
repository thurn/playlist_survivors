using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Tests
{
    /// <summary>The settings every measurement assumes (performance.md, Engine and Rendering Setup).</summary>
    public class ProjectSetupTests
    {
        [Test]
        public void PlayerIsDirect3D12Only()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            Assert.That(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64), Is.False);
            Assert.That(apis, Is.EqualTo(new[] { GraphicsDeviceType.Direct3D12 }));
        }

        [Test]
        public void PlayerUsesIl2cppAndLinearColor()
        {
            Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone), Is.EqualTo(ScriptingImplementation.IL2CPP));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(PlayerSettings.enableFrameTimingStats, Is.True);
        }

        [Test]
        public void UrpIsTheActivePipeline()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Not.Null);
            Assert.That(GraphicsSettings.defaultRenderPipeline.GetType().Name, Is.EqualTo("UniversalRenderPipelineAsset"));
        }
    }
}
