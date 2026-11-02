using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Champions.Render
{
    /// <summary>The quality tiers (design.md, The Look &gt; Quality tiers; plan.md, Rendering &gt; Quality tiers).</summary>
    public enum Tier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// One tier's settings (plan.md's table). The core look (edge cuts, bodies, beat wave, grid, shatter, bloom) is on
    /// every tier; a tier sheds only extras. URP's bloom always starts at half resolution or below, so "half resolution"
    /// is its quarter-resolution start and "full" its half-resolution one; "wider" is a wider scatter over more
    /// iterations.
    /// </summary>
    public readonly struct TierSettings
    {
        public readonly Tier Tier;
        public readonly BloomDownscaleMode BloomDownscale;
        public readonly float BloomScatter;
        public readonly int BloomIterations;
        /// <summary>MSAA samples (1: off).</summary>
        public readonly int Msaa;
        /// <summary>Monster and structure shard pieces alive at once (the shatter's budget).</summary>
        public readonly int ShardPieces;
        /// <summary>The extras: distortion in slashes, glow halos on lines, chromatic flashes on special moments.</summary>
        public readonly bool SlashDistortion, LineHalos, ChromaticFlashes;

        public TierSettings(Tier tier, BloomDownscaleMode downscale, float scatter, int iterations, int msaa, int shards, bool extras)
        {
            Tier = tier;
            BloomDownscale = downscale;
            BloomScatter = scatter;
            BloomIterations = iterations;
            Msaa = msaa;
            ShardPieces = shards;
            SlashDistortion = LineHalos = ChromaticFlashes = extras;
        }

        public override string ToString() =>
            $"{Tier}: bloom from {(BloomDownscale == BloomDownscaleMode.Half ? "half" : "quarter")} resolution, scatter {BloomScatter:0.00}, " +
            $"{BloomIterations} iterations; MSAA {(Msaa > 1 ? Msaa + "x" : "off")}; {ShardPieces} shard pieces; " +
            $"slash distortion, line halos and chromatic flashes {(ChromaticFlashes ? "on" : "off")}";
    }

    /// <summary>
    /// Picks and applies the quality tier: `--tier low|medium|high` at launch, the pause menu's Quality row after
    /// (<see cref="Apply"/> with remember, which keeps the choice for the next launch). <see cref="Changed"/> tells the
    /// systems that read a tier's settings (the shatter's budget, the slashes, the lines' halos). Applying sets the
    /// pipeline's MSAA in a player (on a runtime copy of the pipeline asset, never the asset itself; the editor keeps
    /// the project's quality settings untouched) and the world's post (<see cref="WorldPost"/>). Without a choice, the GPU picks: an M-series Max or Ultra plays High, a Pro Medium,
    /// anything else Low.
    /// </summary>
    public static class QualityTiers
    {
        public const string PrefKey = "champions.quality-tier";

        static readonly TierSettings[] All =
        {
            new TierSettings(Tier.Low, BloomDownscaleMode.Quarter, 0.7f, 6, 1, 600, false),
            new TierSettings(Tier.Medium, BloomDownscaleMode.Half, 0.7f, 6, 2, 1500, false),
            new TierSettings(Tier.High, BloomDownscaleMode.Half, 0.8f, 8, 4, 3000, true),
        };

        static UniversalRenderPipelineAsset _runtimePipeline;

        /// <summary>The tier applied last (High until one is).</summary>
        public static Tier Current { get; private set; } = Tier.High;

        public static TierSettings Settings => Of(Current);

        /// <summary>After a tier is applied: the systems re-read <see cref="Settings"/>.</summary>
        public static event Action<Tier> Changed;

        public static TierSettings Of(Tier tier) => All[(int)tier];

        public static bool TryParse(string s, out Tier tier) => Enum.TryParse(s, true, out tier) && Enum.IsDefined(typeof(Tier), tier);

        /// <summary>The tier kept from the pause menu, or the GPU's guess.</summary>
        public static Tier Default()
        {
            try
            {
                if (PlayerPrefs.HasKey(PrefKey) && TryParse(PlayerPrefs.GetString(PrefKey), out var kept)) return kept;
            }
            catch (UnityException)
            {
                // PlayerPrefs off the main thread or before the engine is up: the GPU decides.
            }
            return ForGpu(SystemInfo.graphicsDeviceName);
        }

        /// <summary>The tier a GPU named <paramref name="device"/> plays at without a choice.</summary>
        public static Tier ForGpu(string device)
        {
            device ??= "";
            if (device.Contains(" Max") || device.Contains(" Ultra")) return Tier.High;
            return device.Contains(" Pro") ? Tier.Medium : Tier.Low;
        }

        /// <summary>
        /// Applies <paramref name="tier"/>: the pipeline's MSAA in a player, the world's post, then <see cref="Changed"/>;
        /// <paramref name="remember"/> keeps it for the next launch (the pause menu's choice).
        /// </summary>
        public static void Apply(Tier tier, bool remember = false)
        {
            var s = Of(tier);
            if (Application.isPlaying && !Application.isEditor && RuntimePipeline() is UniversalRenderPipelineAsset urp) ApplyTo(urp, tier);
            WorldPost.Active?.Apply(s);
            Current = tier;
            if (remember)
            {
                PlayerPrefs.SetString(PrefKey, tier.ToString());
                PlayerPrefs.Save();
            }
            Debug.Log("Quality: " + s);
            Changed?.Invoke(tier);
        }

        /// <summary>Sets <paramref name="pipeline"/>'s MSAA to <paramref name="tier"/>'s.</summary>
        public static void ApplyTo(UniversalRenderPipelineAsset pipeline, Tier tier) => pipeline.msaaSampleCount = Of(tier).Msaa;

        /// <summary>Sets <paramref name="bloom"/>'s tiered values to <paramref name="tier"/>'s and the rest to <paramref name="basis"/>'s (the pipeline's profile).</summary>
        public static void ApplyTo(Bloom bloom, Bloom basis, Tier tier)
        {
            var s = Of(tier);
            if (basis != null)
            {
                bloom.threshold.Override(basis.threshold.value);
                bloom.intensity.Override(basis.intensity.value);
                bloom.clamp.Override(basis.clamp.value);
                bloom.tint.Override(basis.tint.value);
                bloom.filter.Override(basis.filter.value);
                bloom.highQualityFiltering.Override(basis.highQualityFiltering.value);
            }
            bloom.active = true;
            bloom.downscale.Override(s.BloomDownscale);
            bloom.scatter.Override(s.BloomScatter);
            bloom.maxIterations.Override(s.BloomIterations);
        }

        /// <summary>A copy of the active pipeline asset, made once and put in its place, so the tiers never change the asset itself.</summary>
        static UniversalRenderPipelineAsset RuntimePipeline()
        {
            if (_runtimePipeline != null && GraphicsSettings.currentRenderPipeline == _runtimePipeline) return _runtimePipeline;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return null;
            _runtimePipeline = UnityEngine.Object.Instantiate(asset);
            _runtimePipeline.name = asset.name + " (runtime)";
            QualitySettings.renderPipeline = _runtimePipeline;
            return _runtimePipeline;
        }
    }
}
