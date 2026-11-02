using System.Linq;
using Champions.EditorTools;
using Champions.Render;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Champions.Tests
{
    /// <summary>The lean pipeline (plan.md Decision 4, Rendering > Pipeline) stays lean.</summary>
    public sealed class PipelineTests
    {
        static UniversalRenderPipelineAsset Pipeline => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Tasks.PipelinePath);

        [Test]
        public void EveryQualityLevelDrawsWithTheProjectsOnePipeline()
        {
            Assert.That(Pipeline, Is.Not.Null, Tasks.PipelinePath);
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(Pipeline));
            for (int i = 0; i < QualitySettings.count; i++)
            {
                var level = QualitySettings.GetRenderPipelineAssetAt(i);
                Assert.That(level == null || level == Pipeline, $"quality level {QualitySettings.names[i]} uses {level}");
            }
        }

        [Test]
        public void ThePipelineIsForwardAndHdrWithEverythingElseOff()
        {
            var p = Pipeline;
            Assert.That(p.supportsHDR, "HDR");
            Assert.That(p.supportsMainLightShadows, Is.False, "main light shadows");
            Assert.That(p.supportsAdditionalLightShadows, Is.False, "additional light shadows");
            Assert.That(p.supportsSoftShadows, Is.False, "soft shadows");
            Assert.That(p.supportsMixedLighting, Is.False, "mixed lighting (lightmaps)");
            Assert.That(p.supportsLightCookies, Is.False, "light cookies");
            Assert.That(p.useRenderingLayers, Is.False, "rendering (light) layers");
            Assert.That(p.supportDataDrivenLensFlare || p.supportScreenSpaceLensFlare, Is.False, "lens flares");
            Assert.That(p.reflectionProbeBlending || p.reflectionProbeBoxProjection, Is.False, "reflection probes");
            Assert.That(p.additionalLightsRenderingMode, Is.EqualTo(LightRenderingMode.PerPixel));
            Assert.That(p.maxAdditionalLightsCount, Is.LessThanOrEqualTo(4), "additional lights per object");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Tasks.RendererPath);
            Assert.That(renderer.renderingMode, Is.EqualTo(RenderingMode.Forward));
            Assert.That(renderer.rendererFeatures.Where(f => f != null).Select(f => f.GetType().Name), Is.Empty,
                "renderer features (SSAO, decals, ...)");
        }

        [Test]
        public void PostIsTheTrailersGradeThenBloom()
        {
            var profile = Pipeline.volumeProfile;
            Assert.That(profile, Is.Not.Null, "the pipeline's volume profile");
            Assert.That(profile.TryGet<Bloom>(out var bloom) && bloom.active && bloom.intensity.overrideState && bloom.intensity.value > 0, "bloom");
            // The trailer's grade tonemaps before URP's post (Grade.cs), so URP's own tonemapper stays off.
            Assert.That(profile.TryGet<Tonemapping>(out var tonemap) && tonemap.mode.value == TonemappingMode.None, "URP's tonemapping off");
            Assert.That(Resources.Load<Shader>(Grade.ShaderPath), Is.Not.Null, "the grade's shader");
        }

        [Test]
        public void NoShaderIsAlwaysIncluded()
        {
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var always = graphics.FindProperty("m_AlwaysIncludedShaders");
            var names = Enumerable.Range(0, always.arraySize)
                .Select(i => always.GetArrayElementAtIndex(i).objectReferenceValue)
                .Select(s => s != null ? ((Shader)s).name : "(missing)");
            Assert.That(names, Is.Empty, "always-included shaders count against the variant budget in every build");
        }
    }
}
