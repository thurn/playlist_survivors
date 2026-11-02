using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PS.Look
{
    /// <summary>
    /// The look's own passes round URP's post (<see cref="PostStack"/>; Resources/Look/Shaders/LookPost.shader):
    /// - <see cref="Stage.Scene"/>, before URP's post, on the scene's light: the exposure (the option's and the
    ///   scene's), the void's toe (light under its knee sinks toward black by a power of the brightest channel, the
    ///   others scaled alike), the wide soft halo (what is over the halo's threshold, its glow saturated, blurred down a pyramid
    ///   from about 540 rows to about 17 and summed back up, added at the halo's intensity, so emitters lift their
    ///   surroundings) and, for a filmic option, the filmic tonemap: the brightest channel rolls off past the
    ///   shoulder with the others scaled with it, so a saturated emitter keeps its hue, and only a core run far past
    ///   white goes white. URP then grades the tonemapped frame as it always has (LDR grading, its own tonemapper
    ///   off), its bloom on it as the core layer;
    /// - <see cref="Stage.Retro"/>, after URP's post: scanlines (360 to a frame's height, whatever its resolution)
    ///   and a soft CRT mask, for an option that has them.
    /// The scene stage costs the halo's pyramid (when the option has one) and one full-screen pass; the retro stage
    /// one full-screen pass.
    /// </summary>
    sealed class LookPostPass : ScriptableRenderPass
    {
        public enum Stage { Scene, Retro }

        public const string ShaderPath = "Look/Shaders/LookPost";
        /// <summary>The halo pyramid's top: about this many rows, whatever the render resolution (a supersampled frame's too).</summary>
        public const int HaloTopRows = 540;
        public const int HaloLevels = 6;
        /// <summary>The scanlines to a frame's height.</summary>
        public const float ScanlineRows = 360f;
        /// <summary>Filmic: an input this many times white is as white as <see cref="PostOption.Whiten"/> takes it.</summary>
        public const float WhitePoint = 8f;

        const int Prefilter = 0, Down = 1, Up = 2, Scene = 3, Retro = 4;
        static readonly int IdSpecial = Shader.PropertyToID("_LookSpecial");
        static readonly int IdHalo = Shader.PropertyToID("_LookHalo"), IdTone = Shader.PropertyToID("_LookTone"),
            IdRetro = Shader.PropertyToID("_LookRetro"), IdVoid = Shader.PropertyToID("_LookVoid"), IdTexel = Shader.PropertyToID("_LookTexel"), IdExtra = Shader.PropertyToID("_LookExtra");

        readonly Material _material;
        readonly Stage _stage;
        public PostOption Option;
        /// <summary>The scene's own exposure in stops, added to the option's (<see cref="PostStack.SceneExposure"/>).</summary>
        public float SceneExposure;

        public LookPostPass(Material material, Stage stage)
        {
            _material = material;
            _stage = stage;
            renderPassEvent = stage == Stage.Scene ? RenderPassEvent.BeforeRenderingPostProcessing : RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public void Dispose() => CoreUtils.Destroy(_material);

        /// <summary>Whether this stage draws anything for <paramref name="o"/>.</summary>
        public bool Draws(PostOption o) => _stage == Stage.Retro
            ? o.Scanlines > 0 || o.Mask > 0
            : o.HaloIntensity > 0 || o.Tonemap == PostOption.Tonemaps.Filmic || o.VoidKnee > 0 || o.Exposure + SceneExposure != 0;

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
            var o = Option;
            if (resources.isActiveTargetBackBuffer || !camera.postProcessEnabled || o == null || !Draws(o)) return;
            bool halo = o.HaloIntensity > 0;
            float exposure = Mathf.Pow(2f, o.Exposure + SceneExposure);
            // A special moment lifts the halo: the impulse field's envelope, 1 on the moment's first frame (ImpulseField.Publish).
            float intensity = o.HaloIntensity * (1 + o.HaloSpecial * Mathf.Clamp01(Shader.GetGlobalFloat(IdSpecial)));
            _material.SetVector(IdHalo, new Vector4(o.HaloThreshold, halo ? intensity / HaloLevels : 0, o.HaloSaturation, exposure));
            _material.SetVector(IdTone, new Vector4(o.Tonemap == PostOption.Tonemaps.Filmic ? 1 : 0, o.Shoulder, o.Whiten, WhitePoint));
            _material.SetVector(IdRetro, new Vector4(o.Scanlines, ScanlineRows, o.Mask, 0));
            _material.SetVector(IdVoid, new Vector4(o.VoidKnee, o.VoidPower, 0, 0));

            var source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            var size = new Vector2Int(desc.width, desc.height);
            var glow = TextureHandle.nullHandle;
            if (_stage == Stage.Scene && halo)
            {
                var levels = new TextureHandle[HaloLevels];
                var sizes = new Vector2Int[HaloLevels];
                int div = Mathf.Max(2, Mathf.RoundToInt(desc.height / (float)HaloTopRows));
                var level = new Vector2Int(Mathf.Max(1, desc.width / div), Mathf.Max(1, desc.height / div));
                for (int i = 0; i < HaloLevels; i++)
                {
                    sizes[i] = level;
                    levels[i] = renderGraph.CreateTexture(new TextureDesc(level.x, level.y)
                    {
                        name = "_LookHalo" + i, format = GraphicsFormat.B10G11R11_UFloatPack32, filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp, clearBuffer = false,
                    });
                    level = new Vector2Int(Mathf.Max(1, level.x / 2), Mathf.Max(1, level.y / 2));
                }
                Blit(renderGraph, "Look halo prefilter", source, size, TextureHandle.nullHandle, levels[0], Prefilter);
                for (int i = 1; i < HaloLevels; i++)
                    Blit(renderGraph, "Look halo down", levels[i - 1], sizes[i - 1], TextureHandle.nullHandle, levels[i], Down);
                // Back up: each level the one below it upsampled plus its own, into a fresh texture of its size.
                var below = levels[HaloLevels - 1];
                for (int i = HaloLevels - 2; i >= 0; i--)
                {
                    var sum = renderGraph.CreateTexture(renderGraph.GetTextureDesc(levels[i]));
                    Blit(renderGraph, "Look halo up", below, sizes[i + 1], levels[i], sum, Up);
                    below = sum;
                }
                glow = below;
            }

            var finalDesc = desc;
            finalDesc.name = _stage == Stage.Scene ? "_LookScene" : "_LookRetro";
            finalDesc.clearBuffer = false;
            finalDesc.msaaSamples = MSAASamples.None;
            var destination = renderGraph.CreateTexture(finalDesc);
            Blit(renderGraph, _stage == Stage.Scene ? "Look scene" : "Look retro", source, size, glow, destination, _stage == Stage.Scene ? Scene : Retro);
            resources.cameraColor = destination;
        }

        /// <summary>
        /// One full-screen draw of <paramref name="pass"/> from <paramref name="source"/> (of <paramref name="sourceSize"/>)
        /// to <paramref name="destination"/>, with <paramref name="extra"/> bound as _LookExtra when valid (the scene pass
        /// reads no halo without one: its intensity is 0). The texel size and the extra texture are set as globals in the
        /// command stream, so each draw sees its own.
        /// </summary>
        void Blit(RenderGraph renderGraph, string name, TextureHandle source, Vector2Int sourceSize, TextureHandle extra, TextureHandle destination, int pass)
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
