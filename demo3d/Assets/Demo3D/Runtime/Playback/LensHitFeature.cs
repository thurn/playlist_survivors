using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The trailer's impact lens hits on the frame (trailer.md, "Look"): after the post stack, a radial blur toward the
    /// impact's screen position and a chromatic-aberration spike across it (Shaders/LensHit.shader). The renderer's
    /// feature (ProjectSetup adds it); it draws only on frames with a hit (<see cref="Strength"/> above 0), so a run
    /// without one, every normal run, pays nothing.
    /// </summary>
    public sealed class LensHitFeature : ScriptableRendererFeature
    {
        /// <summary>The blur's longest streak at full strength, as a share of the screen's width (the cap).</summary>
        public const float MaxBlur = 0.03f;
        /// <summary>The aberration's split at full strength, as a share of the screen's width.</summary>
        public const float MaxAberration = 0.002f;

        /// <summary>The frame's hit: its centre in viewport coordinates (0 to 1, from the bottom left) and strength (0 to 1, capped).</summary>
        public static Vector2 Centre;
        public static float Strength;

        [SerializeField] Shader shader;
        Material _material;
        Pass _pass;

        public override void Create()
        {
            _pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing, requiresIntermediateTexture = true };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!(Strength > 0f) || shader == null || renderingData.cameraData.cameraType != CameraType.Game) return;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            _pass.Material = _material;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(_material);

        sealed class Pass : ScriptableRenderPass
        {
            static readonly int HitId = Shader.PropertyToID("_LensHit");
            public Material Material;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var camera = frameData.Get<UniversalCameraData>();
                float s = Mathf.Clamp01(Strength);
                float aspect = camera.cameraTargetDescriptor.width / (float)Mathf.Max(1, camera.cameraTargetDescriptor.height);
                Material.SetVector(HitId, new Vector4(Centre.x, Centre.y, MaxBlur * s, MaxAberration * s));
                Material.SetFloat("_LensHitAspect", aspect);
                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_LensHit";
                desc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, 0), "Lens hit");
                resources.cameraColor = destination;
            }
        }
    }
}
