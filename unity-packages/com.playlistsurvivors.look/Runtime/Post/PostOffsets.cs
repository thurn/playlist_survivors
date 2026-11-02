using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PS.Look
{
    /// <summary>
    /// The post feel's offsets at one moment (demo3d's and game's PostFeel compute them from the beat and their
    /// events): added to a volume's base values by <see cref="PostBinding"/>. <see cref="Pulse"/> and
    /// <see cref="Flash"/> are the beat's pulse and the flash themselves (0 to 1), which tint the bloom toward the
    /// palette's colours when the binding has them. <see cref="Filter"/> is a colour the colour filter leans toward by
    /// <see cref="FilterShare"/> (0: the volume's own filter), such as the crystal cut's palette flash at a cut.
    /// </summary>
    public struct PostOffsets
    {
        public float Exposure, Bloom, Vignette, Saturation, Contrast, Temperature, Chromatic;
        public float Pulse, Flash;
        public Color Filter;
        public float FilterShare;
    }

    /// <summary>
    /// A volume's base values, read when it first binds, and the post feel's offsets written over them each frame.
    /// With <see cref="Tints"/> (a look's post stack, <see cref="PostStack.Tints"/>) the bloom's tint also leans
    /// toward the beat's colour on a pulse and the flash's on a flash; without, the tint is left alone.
    /// </summary>
    public sealed class PostBinding
    {
        /// <summary>The bloom's tints: the beat's colour and its share at a full pulse, and the flash's colour.</summary>
        public struct BloomTints
        {
            public Color Beat, Flash;
            public float BeatShare;
        }

        public BloomTints? Tints;

        ColorAdjustments _color;
        Bloom _bloom;
        Vignette _vignette;
        WhiteBalance _white;
        ChromaticAberration _chromatic;
        float _baseExposure, _baseSaturation, _baseContrast, _baseBloom, _baseVignette, _baseTemperature, _baseChromatic;
        Color _baseTint, _baseFilter = Color.white;
        bool _filtered; // whether a filter offset has turned the colour filter's override on
        Volume _bound;

        /// <summary>Sets <paramref name="volume"/>'s parameters to its base values plus <paramref name="o"/>.</summary>
        public void Apply(Volume volume, in PostOffsets o)
        {
            if (volume == null) return;
            if (volume != _bound) Bind(volume);
            if (_color != null)
            {
                _color.postExposure.value = _baseExposure + o.Exposure;
                _color.saturation.value = _baseSaturation + o.Saturation;
                _color.contrast.value = _baseContrast + o.Contrast;
                if (o.FilterShare > 0 || _filtered)
                {
                    // The filter's override goes on with the first filter offset (at the volume's own colour, a no-op)
                    // and stays, so a volume that never takes one is left as it was.
                    if (!_color.colorFilter.overrideState) _color.colorFilter.Override(_baseFilter);
                    _filtered = true;
                    _color.colorFilter.value = Color.Lerp(_baseFilter, o.Filter, Mathf.Clamp01(o.FilterShare));
                }
            }
            if (_bloom != null)
            {
                _bloom.intensity.value = Math.Max(0, _baseBloom + o.Bloom);
                if (Tints is BloomTints t)
                {
                    var tint = Color.Lerp(_baseTint, t.Beat, Mathf.Clamp01(o.Pulse * t.BeatShare));
                    _bloom.tint.value = Color.Lerp(tint, t.Flash, Mathf.Clamp01(o.Flash));
                }
            }
            if (_vignette != null) _vignette.intensity.value = Math.Max(0, _baseVignette + o.Vignette);
            if (_white != null) _white.temperature.value = _baseTemperature + o.Temperature;
            if (_chromatic != null) _chromatic.intensity.value = Math.Min(1, _baseChromatic + o.Chromatic);
        }

        void Bind(Volume volume)
        {
            _bound = volume;
            var p = volume.sharedProfile;
            if (p.TryGet(out _color))
            {
                _baseExposure = _color.postExposure.value; _baseSaturation = _color.saturation.value; _baseContrast = _color.contrast.value;
                _baseFilter = _color.colorFilter.overrideState ? _color.colorFilter.value : Color.white;
                _filtered = false;
            }
            if (p.TryGet(out _bloom)) { _baseBloom = _bloom.intensity.value; _baseTint = _bloom.tint.value; }
            if (p.TryGet(out _vignette)) _baseVignette = _vignette.intensity.value;
            if (p.TryGet(out _white)) _baseTemperature = _white.temperature.value;
            if (p.TryGet(out _chromatic)) _baseChromatic = _chromatic.intensity.value;
        }
    }
}
