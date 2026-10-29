using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Crowd.Bench
{
    /// <summary>
    /// Shows one of URP's intermediate buffers over the frame, so a still proves the crowd's
    /// passes run: `--show-motion` (the motion vector texture, from the MotionVectors pass) or
    /// `--show-normals` (the camera normals texture, from the DepthNormals pass SSAO uses). It
    /// asks URP for that input, so the pass runs even when nothing else needs it, and blits it as
    /// colour after post-processing. Enqueued per camera from beginCameraRendering, so the
    /// renderer asset is unchanged.
    /// </summary>
    public sealed class BufferDebugPass : ScriptableRenderPass
    {
        public enum Buffer { Motion = 0, Normals = 1 }

        readonly Material _material;
        readonly Buffer _buffer;

        public BufferDebugPass(Shader shader, Buffer buffer)
        {
            _material = CoreUtils.CreateEngineMaterial(shader);
            _buffer = buffer;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(buffer == Buffer.Motion ? ScriptableRenderPassInput.Motion : ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var source = _buffer == Buffer.Motion ? resources.motionVectorColor : resources.cameraNormalsTexture;
            if (!source.IsValid()) return;
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, resources.activeColorTexture, _material, (int)_buffer),
                "Crowd buffer debug: " + _buffer);
        }

        public void Dispose() => CoreUtils.Destroy(_material);
    }
}
