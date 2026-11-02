using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Champions.Render
{
    /// <summary>
    /// The trailer's grade (design.md, The Look &gt; The reference: the trailer): the frozen look package's LookPostPass,
    /// its scene stage with the neon edge trailer's neon-bloom values, ported by review. Before URP's post, on the scene's
    /// light of every camera that post-processes: the exposure, a void toe that sinks what lies under its knee toward
    /// black (a monster's glass core, the faint rim), a wide soft halo of what is over its threshold, saturated, and a
    /// filmic tonemap that rolls the brightest channel off and scales the others with it, so a saturated emitter keeps
    /// its hue where a per-channel tonemap washes a levelled blue or magenta toward white. URP's own tonemapper is off
    /// (Settings/Post.asset); its bloom, colour adjustments and vignette follow on the graded frame, as the trailer's did.
    /// Resources/World/Grade.shader. The stills' type-ID pass draws without post, so without the grade.
    /// </summary>
    public static class Grade
    {
        /// <summary>The neon-bloom preset's scene stage: exposure (stops), the toe, the halo, the filmic curve.</summary>
        public const float Exposure = -0.1f, VoidKnee = 0.6f, VoidPower = 3.2f;
        public const float HaloThreshold = 0.45f, HaloIntensity = 0.63f, HaloSaturation = 1.5f;
        public const float Shoulder = 0.55f, Whiten = 0.3f;
        /// <summary>Filmic: an input this many times white is as white as <see cref="Whiten"/> takes it.</summary>
        public const float WhitePoint = 8f;
        /// <summary>The halo pyramid's top: about this many rows, whatever the render resolution; its levels.</summary>
        public const int HaloTopRows = 540, HaloLevels = 6;
        public const string ShaderPath = "World/Grade";

        static Pass _pass;
        static readonly int IdGrade = Shader.PropertyToID("_RC_Grade");

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            RenderPipelineManager.beginCameraRendering += Enqueue;
            // For what is calibrated as drawn after the grade (World.hlsl's Ungraded: the floor).
            Shader.SetGlobalVector(IdGrade, new Vector4(Mathf.Pow(2f, Exposure), VoidKnee, VoidPower, 0));
        }

        static void Enqueue(ScriptableRenderContext context, Camera camera)
        {
            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) || !data.renderPostProcessing) return;
            if (_pass == null)
            {
                var shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null) return; // a domain reload before the asset database is up
                _pass = new Pass(CoreUtils.CreateEngineMaterial(shader));
            }
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        sealed class Pass : ScriptableRenderPass
        {
            const int Prefilter = 0, Down = 1, Up = 2, Scene = 3;
            static readonly int IdHalo = Shader.PropertyToID("_GradeHalo"), IdTone = Shader.PropertyToID("_GradeTone"),
                IdVoid = Shader.PropertyToID("_GradeVoid"), IdTexel = Shader.PropertyToID("_GradeTexel"),
                IdExtra = Shader.PropertyToID("_GradeExtra");

            readonly Material _material;

            public Pass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            sealed class PassData
            {
                public TextureHandle Source, Extra;
                public Vector4 Texel;
                public Material Material;
                public int Pass;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !camera.postProcessEnabled) return;
                _material.SetVector(IdHalo, new Vector4(HaloThreshold, HaloIntensity / HaloLevels, HaloSaturation, Mathf.Pow(2f, Exposure)));
                _material.SetVector(IdTone, new Vector4(1, Shoulder, Whiten, WhitePoint));
                _material.SetVector(IdVoid, new Vector4(VoidKnee, VoidPower, 0, 0));

                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                var size = new Vector2Int(desc.width, desc.height);
                var levels = new TextureHandle[HaloLevels];
                var sizes = new Vector2Int[HaloLevels];
                int div = Mathf.Max(2, Mathf.RoundToInt(desc.height / (float)HaloTopRows));
                var level = new Vector2Int(Mathf.Max(1, desc.width / div), Mathf.Max(1, desc.height / div));
                for (int i = 0; i < HaloLevels; i++)
                {
                    sizes[i] = level;
                    levels[i] = renderGraph.CreateTexture(new TextureDesc(level.x, level.y)
                    {
                        name = "_GradeHalo" + i, format = GraphicsFormat.B10G11R11_UFloatPack32, filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp, clearBuffer = false,
                    });
                    level = new Vector2Int(Mathf.Max(1, level.x / 2), Mathf.Max(1, level.y / 2));
                }
                Blit(renderGraph, "Grade halo prefilter", source, size, TextureHandle.nullHandle, levels[0], Prefilter);
                for (int i = 1; i < HaloLevels; i++)
                    Blit(renderGraph, "Grade halo down", levels[i - 1], sizes[i - 1], TextureHandle.nullHandle, levels[i], Down);
                // Back up: each level the one below it upsampled plus its own, into a fresh texture of its size.
                var below = levels[HaloLevels - 1];
                for (int i = HaloLevels - 2; i >= 0; i--)
                {
                    var sum = renderGraph.CreateTexture(renderGraph.GetTextureDesc(levels[i]));
                    Blit(renderGraph, "Grade halo up", below, sizes[i + 1], levels[i], sum, Up);
                    below = sum;
                }

                var finalDesc = desc;
                finalDesc.name = "_GradeScene";
                finalDesc.clearBuffer = false;
                finalDesc.msaaSamples = MSAASamples.None;
                var destination = renderGraph.CreateTexture(finalDesc);
                Blit(renderGraph, "Grade scene", source, size, below, destination, Scene);
                resources.cameraColor = destination;
            }

            /// <summary>
            /// One full-screen draw of <paramref name="pass"/> from <paramref name="source"/> (of <paramref name="sourceSize"/>)
            /// to <paramref name="destination"/>, with <paramref name="extra"/> bound as _GradeExtra when valid. The texel size
            /// and the extra texture are set as globals in the command stream, so each draw sees its own.
            /// </summary>
            void Blit(RenderGraph renderGraph, string name, TextureHandle source, Vector2Int sourceSize, TextureHandle extra,
                      TextureHandle destination, int pass)
            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data);
                data.Source = source;
                data.Extra = extra;
                data.Texel = new Vector4(1f / sourceSize.x, 1f / sourceSize.y, sourceSize.x, sourceSize.y);
                data.Material = _material;
                data.Pass = pass;
                builder.UseTexture(source);
                if (extra.IsValid()) builder.UseTexture(extra);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(IdTexel, d.Texel);
                    if (d.Extra.IsValid()) context.cmd.SetGlobalTexture(IdExtra, d.Extra);
                    Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1, 1, 0, 0), d.Material, d.Pass);
                });
            }
        }
    }
}
