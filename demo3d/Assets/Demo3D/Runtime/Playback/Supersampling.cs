using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// `--supersample <1–2>` (trailer.md, "Look"): the frame renders at that multiple of its output resolution (URP's
    /// render scale) and is downsampled to it with linear filtering, a 2×2 box at 2×, for clean edges on blades,
    /// monsters and particles. A trailer's capture renders at 2×, its live run at <see cref="TrailerLive"/>
    /// (performance.md); a normal run at 1×, as before.
    /// </summary>
    public static class Supersampling
    {
        public const string Flag = "--supersample";
        /// <summary>A trailer capture's factor (video or stills).</summary>
        public const float TrailerCapture = 2f;
        /// <summary>
        /// A live trailer's factor: the highest at which the climax holds 60 fps on the Mac (performance.md, The trailer's
        /// look). Even 1× takes about 16 ms on the final slam's frames there, so it is 1×.
        /// </summary>
        public const float TrailerLive = 1f;
        /// <summary>From this factor bloom runs at a quarter of the render resolution rather than half, so its glow keeps its size on screen.</summary>
        const float QuarterBloomFrom = 1.414f;

        /// <summary>The run's factor: the flag's (1 to 2), else a trailer's capture or live default, else 1.</summary>
        public static float Factor(DevFlags flags)
        {
            if (Args.Has(Flag))
            {
                float f = Args.GetFloat(Flag, 1f);
                if (!(f >= 1f && f <= 2f)) throw new ArgumentException($"{Flag}: takes 1 to 2, not {Args.Get(Flag)}");
                return f;
            }
            if (flags.Trailer == null) return 1f;
            return flags.CaptureVideo != null || flags.CaptureStills != null ? TrailerCapture : TrailerLive;
        }

        /// <summary>Sets the pipeline's render scale (a player's own copy of the asset; Downscaling above 1).</summary>
        public static void Apply(float factor)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = factor;
            else if (factor != 1f) throw new InvalidOperationException($"{Flag}: needs URP");
        }

        /// <summary>
        /// Keeps an environment's bloom the same size on screen: URP's bloom pyramid starts at half the render
        /// resolution and stops at its iteration cap, so at 2× its glow would halve. At 2× a quarter matches 1×'s half.
        /// </summary>
        public static void FitBloom(VolumeProfile profile, float factor)
        {
            if (factor >= QuarterBloomFrom && profile.TryGet(out Bloom bloom)) bloom.downscale.Override(BloomDownscaleMode.Quarter);
        }
    }
}
