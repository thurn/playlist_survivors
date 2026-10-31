using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Look
{
    /// <summary>
    /// The night post stack moving with the live stream (unity-game.md, "Post-processing and
    /// Camera"), demo3d's PostFeel with the song map replaced by the fast lane and the sim's events:
    /// - every beat: an exposure and bloom pulse and a vignette breath, decaying over the beat
    ///   (<see cref="LaneFrame.Pulse"/>: full on a reliable beat, softer below the gate);
    /// - accents, heavy strikes, breaks and drops (<see cref="FeelMoments"/>): an exposure flash, a
    ///   chromatic-aberration burst and a saturation spike, gone within a beat;
    /// - a drop's build-up (<see cref="LaneFrame.Rise"/>): exposure, bloom and saturation rising
    ///   and the vignette closing in over the 1.6 s before it lands; the kick lifts the bloom a touch,
    ///   anticipated about 90 ms ahead;
    /// - per zone: a grade offset (exposure, saturation, contrast, white-balance temperature,
    ///   vignette) and a pulse scale, eased over <see cref="EaseBars"/> bars from the change
    ///   (<see cref="SetZone"/>, the hook for M3's zones).
    /// It writes offsets on top of the volume's base values (the night's, NightLook.CreateProfile),
    /// read when it first binds. Chromatic aberration is zero at rest.
    /// </summary>
    public sealed class PostFeel
    {
        /// <summary>The beat's pulse: exposure (stops), bloom intensity and vignette (demo3d's arena-layout postFeel).</summary>
        public const float BeatExposure = 0.12f, BeatBloom = 0.35f, BeatVignette = -0.03f;
        /// <summary>A full flash: exposure (stops), aberration and saturation.</summary>
        public const float FlashExposure = 0.7f, FlashChromatic = 0.5f, FlashSaturation = 25f;
        /// <summary>A drop's build-up at its peak: exposure, bloom, saturation and vignette; the kick's bloom.</summary>
        public const float RiseExposure = 0.15f, RiseBloom = 0.4f, RiseSaturation = 10f, RiseVignette = 0.06f, KickBloom = 0.15f;
        public const float EaseBars = 2f;

        /// <summary>A zone's grade offset, and its beat pulse as a multiple of the base.</summary>
        public struct ZoneGrade
        {
            public float Exposure, Saturation, Contrast, Temperature, Vignette, Pulse;

            public static readonly ZoneGrade None = new ZoneGrade { Pulse = 1 };

            public static ZoneGrade Lerp(in ZoneGrade a, in ZoneGrade b, float w) => new ZoneGrade
            {
                Exposure = a.Exposure + (b.Exposure - a.Exposure) * w,
                Saturation = a.Saturation + (b.Saturation - a.Saturation) * w,
                Contrast = a.Contrast + (b.Contrast - a.Contrast) * w,
                Temperature = a.Temperature + (b.Temperature - a.Temperature) * w,
                Vignette = a.Vignette + (b.Vignette - a.Vignette) * w,
                Pulse = a.Pulse + (b.Pulse - a.Pulse) * w,
            };
        }

        /// <summary>
        /// Each zone palette's grade (arena-layout.json's palettes): the neon night is the base; the
        /// others lean with their palette's light. M3's zones may retune them.
        /// </summary>
        public static ZoneGrade GradeOf(string zone)
        {
            switch (zone)
            {
                case "crypt": return new ZoneGrade { Exposure = -0.1f, Saturation = -10, Contrast = 8, Temperature = -10, Vignette = 0.05f, Pulse = 0.8f };
                case "lava": return new ZoneGrade { Exposure = 0.05f, Saturation = 10, Contrast = 6, Temperature = 12, Vignette = 0.04f, Pulse = 1.2f };
                case "blossom": return new ZoneGrade { Exposure = 0.08f, Saturation = 6, Temperature = 4, Pulse = 1f };
                case "snow": return new ZoneGrade { Exposure = 0.1f, Saturation = -6, Contrast = 4, Temperature = -8, Pulse = 0.9f };
                default: return ZoneGrade.None;
            }
        }

        /// <summary>Offsets from the volume's base values at one moment.</summary>
        public struct Offsets
        {
            public float Exposure, Bloom, Vignette, Saturation, Contrast, Temperature, Chromatic;
        }

        ZoneGrade _from = ZoneGrade.None, _to = ZoneGrade.None;
        double _zoneStart, _zoneEase;

        ColorAdjustments _color;
        Bloom _bloom;
        Vignette _vignette;
        WhiteBalance _white;
        ChromaticAberration _chromatic;
        float _baseExposure, _baseSaturation, _baseContrast, _baseBloom, _baseVignette, _baseTemperature, _baseChromatic;
        Volume _bound;

        /// <summary>
        /// Eases the grade to <paramref name="zone"/>'s from song time <paramref name="from"/> (the
        /// change's downbeat) over <see cref="EaseBars"/> bars of <paramref name="barS"/> seconds,
        /// starting from wherever the grade is then.
        /// </summary>
        public void SetZone(string zone, double from, double barS)
        {
            _from = Grade(from);
            _to = GradeOf(zone);
            _zoneStart = from;
            _zoneEase = Math.Max(0, EaseBars * barS);
        }

        /// <summary>The zone's grade at s, eased from the previous one (smoothstep).</summary>
        public ZoneGrade Grade(double s)
        {
            if (_zoneEase <= 0 || s >= _zoneStart + _zoneEase) return s < _zoneStart ? _from : _to;
            float x = (float)Math.Max(0, (s - _zoneStart) / _zoneEase);
            return ZoneGrade.Lerp(_from, _to, x * x * (3 - 2 * x));
        }

        /// <summary>The offsets at song time s, from the fast lane's frame and the moments.</summary>
        public Offsets At(double s, in LaneFrame lane, FeelMoments moments)
        {
            var g = Grade(s);
            float pulse = (float)lane.Pulse * g.Pulse;
            float flash = moments.Flash(s, lane.Period);
            float rise = (float)lane.Rise;
            float kick = (float)Math.Max(lane.Kick, lane.KickPre);
            return new Offsets
            {
                Exposure = g.Exposure + BeatExposure * pulse + FlashExposure * flash + RiseExposure * rise,
                Bloom = BeatBloom * pulse + RiseBloom * rise + KickBloom * kick,
                Vignette = g.Vignette + BeatVignette * pulse + RiseVignette * rise,
                Saturation = g.Saturation + FlashSaturation * flash + RiseSaturation * rise,
                Contrast = g.Contrast,
                Temperature = g.Temperature,
                Chromatic = FlashChromatic * flash,
            };
        }

        /// <summary>Sets <paramref name="volume"/>'s parameters to its base values plus <paramref name="o"/>.</summary>
        public void Apply(Volume volume, in Offsets o)
        {
            if (volume == null) return;
            if (volume != _bound) Bind(volume);
            if (_color != null)
            {
                _color.postExposure.value = _baseExposure + o.Exposure;
                _color.saturation.value = _baseSaturation + o.Saturation;
                _color.contrast.value = _baseContrast + o.Contrast;
            }
            if (_bloom != null) _bloom.intensity.value = Math.Max(0, _baseBloom + o.Bloom);
            if (_vignette != null) _vignette.intensity.value = Math.Max(0, _baseVignette + o.Vignette);
            if (_white != null) _white.temperature.value = _baseTemperature + o.Temperature;
            if (_chromatic != null) _chromatic.intensity.value = Math.Min(1, _baseChromatic + o.Chromatic);
        }

        void Bind(Volume volume)
        {
            _bound = volume;
            var p = volume.sharedProfile;
            if (p.TryGet(out _color)) { _baseExposure = _color.postExposure.value; _baseSaturation = _color.saturation.value; _baseContrast = _color.contrast.value; }
            if (p.TryGet(out _bloom)) _baseBloom = _bloom.intensity.value;
            if (p.TryGet(out _vignette)) _baseVignette = _vignette.intensity.value;
            if (p.TryGet(out _white)) _baseTemperature = _white.temperature.value;
            if (p.TryGet(out _chromatic)) _baseChromatic = _chromatic.intensity.value;
        }
    }
}
