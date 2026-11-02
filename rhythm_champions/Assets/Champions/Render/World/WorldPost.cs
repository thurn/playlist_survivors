using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Champions.Render
{
    /// <summary>
    /// The world's post over the pipeline's profile (Settings/Post.asset: bloom and neutral tonemapping): a global volume
    /// of its own that sets the bloom per tier (<see cref="QualityTiers"/>) and flashes on special moments only
    /// (design.md, Restraint): an exposure lift of at most <see cref="FlashEv"/>, and on tiers with chromatic flashes an
    /// aberration of at most <see cref="ChromaticFlash"/>, both following <c>_RC_Special</c>, so a flash never whites
    /// out the frame and is gone within a quarter second. The peaks are the flash profile's
    /// (Resources/World/PostFlash.asset), which also keeps the flash's post variants in builds: URP strips the variants
    /// of post effects no profile under Assets/ has on.
    /// </summary>
    public sealed class WorldPost : IDisposable
    {
        /// <summary>The flash's peaks: its colour adjustments' exposure and its chromatic aberration's intensity.</summary>
        public const string FlashPath = "World/PostFlash";
        /// <summary>Above the pipeline's profile (priority 0).</summary>
        public const float Priority = 10f;

        /// <summary>The world's post, while one is up (the tiers apply to it).</summary>
        public static WorldPost Active { get; private set; }

        readonly GameObject _go;
        readonly VolumeProfile _profile;
        readonly Bloom _bloom, _basis;
        readonly ColorAdjustments _colour;
        readonly ChromaticAberration _chromatic;
        bool _chromaticFlashes;

        public WorldPost()
        {
            VolumeProfile basis = null;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) basis = urp.volumeProfile;
            if (basis != null) basis.TryGet(out _basis);
            var flash = Resources.Load<VolumeProfile>(FlashPath)
                        ?? throw new InvalidOperationException($"WorldPost: no flash profile at Resources/{FlashPath}");
            FlashEv = flash.TryGet<ColorAdjustments>(out var peakColour) ? peakColour.postExposure.value : 0f;
            ChromaticFlash = flash.TryGet<ChromaticAberration>(out var peakChromatic) ? peakChromatic.intensity.value : 0f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "World post";
            _bloom = _profile.Add<Bloom>();
            _colour = _profile.Add<ColorAdjustments>();
            _colour.postExposure.Override(0f);
            _chromatic = _profile.Add<ChromaticAberration>();
            _chromatic.intensity.Override(0f);
            _chromatic.active = false;
            _go = new GameObject("World post");
            var volume = _go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = _profile;
            Active = this;
            Apply(QualityTiers.Settings);
        }

        /// <summary>A special moment's exposure lift at its first frame (EV), and its chromatic aberration on tiers with chromatic flashes.</summary>
        public float FlashEv { get; }
        public float ChromaticFlash { get; }

        public VolumeProfile Profile => _profile;
        public Bloom Bloom => _bloom;

        /// <summary>The tier's bloom and whether special moments flash chromatically.</summary>
        public void Apply(in TierSettings s)
        {
            QualityTiers.ApplyTo(_bloom, _basis, s.Tier);
            _chromaticFlashes = s.ChromaticFlashes;
            _chromatic.active = _chromaticFlashes;
        }

        /// <summary>The frame's flash from the special-moment envelope (<see cref="ImpulseField.Special"/>, 0 to 1).</summary>
        public void Present(float special)
        {
            special = Mathf.Clamp01(special);
            _colour.postExposure.value = FlashEv * special;
            if (_chromaticFlashes) _chromatic.intensity.value = ChromaticFlash * special;
        }

        public void Dispose()
        {
            if (Active == this) Active = null;
            foreach (var o in new UnityEngine.Object[] { _go, _profile })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
