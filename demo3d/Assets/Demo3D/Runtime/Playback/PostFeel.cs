using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The post stack moving with the music (ps-e8r.18), beside the camera's punch and shake:
    /// - every beat of a pulsed section: an exposure and bloom pulse and a vignette breath, decaying over the beat;
    /// - accents, free sections' anchors, the hero's big hits and structure breaks (BreakMoments): a flash (exposure
    ///   spike), a chromatic-aberration burst and a saturation spike, scaled by their strength and gone within a beat;
    /// - per section: a grade offset (exposure, saturation, contrast, white-balance temperature, vignette), eased in over
    ///   <see cref="Amounts.EaseBars"/> bars from the section's first downbeat, as the camera's zoom eases.
    /// It writes offsets on top of the night volume's base values (NightLook.CreateProfile, the hero pool's vignette
    /// included), read once when it first binds, and is a function of song time alone, so fixed-time stills reproduce.
    /// </summary>
    public sealed class PostFeel
    {
        /// <summary>The post feel's amounts, the <c>lighting.postFeel</c> object of arena-layout.json.</summary>
        public sealed class Amounts
        {
            public float BeatExposure, BeatBloom, BeatVignette;
            public float FlashExposure, FlashChromatic, FlashSaturation;
            /// <summary>The hero's attacks at or above this strength flash as hits (above 1: none).</summary>
            public float HitStrength = 2f, HitFlash;
            public float EaseBars = 2f;
            public readonly List<SectionGrade> Sections = new List<SectionGrade>();

            public static Amounts Parse(JsonValue v)
            {
                var a = new Amounts();
                if (v.Type != JsonType.Object) return a;
                JsonValue beat = v["beat"], flash = v["flash"], hit = v["hit"];
                a.BeatExposure = beat["exposure"].AsFloat(0f);
                a.BeatBloom = beat["bloom"].AsFloat(0f);
                a.BeatVignette = beat["vignette"].AsFloat(0f);
                a.FlashExposure = flash["exposure"].AsFloat(0f);
                a.FlashChromatic = flash["chromatic"].AsFloat(0f);
                a.FlashSaturation = flash["saturation"].AsFloat(0f);
                a.HitStrength = hit["minStrength"].AsFloat(a.HitStrength);
                a.HitFlash = hit["flash"].AsFloat(0f);
                a.EaseBars = v["easeBars"].AsFloat(a.EaseBars);
                JsonValue sections = v["sections"];
                for (int i = 0; i < sections.Count; i++)
                {
                    JsonValue s = sections[i];
                    a.Sections.Add(new SectionGrade
                    {
                        Match = s["match"].AsString(""),
                        Exposure = s["exposure"].AsFloat(0f),
                        Saturation = s["saturation"].AsFloat(0f),
                        Contrast = s["contrast"].AsFloat(0f),
                        Temperature = s["temperature"].AsFloat(0f),
                        Vignette = s["vignette"].AsFloat(0f),
                        Pulse = s["pulse"].AsFloat(1f),
                    });
                }
                return a;
            }

            /// <summary>The first grade whose <see cref="SectionGrade.Match"/> starts the label, or none.</summary>
            public SectionGrade For(string label)
            {
                foreach (var s in Sections) if (label != null && label.StartsWith(s.Match, StringComparison.Ordinal)) return s;
                return SectionGrade.None;
            }
        }

        /// <summary>A section's grade offset, and its beat pulse as a multiple of the base.</summary>
        public sealed class SectionGrade
        {
            public static readonly SectionGrade None = new SectionGrade();
            public string Match = "";
            public float Exposure, Saturation, Contrast, Temperature, Vignette, Pulse = 1f;
        }

        /// <summary>Offsets from the volume's base values at one moment.</summary>
        public struct Offsets
        {
            public float Exposure, Bloom, Vignette, Saturation, Contrast, Temperature, Chromatic;
        }

        readonly SongMap _map;
        readonly Amounts _amounts;
        readonly SectionGrade[] _grades;   // per map section
        readonly double[] _flashAt;        // sorted
        readonly float[] _flashStrength;
        public readonly bool Enabled;

        ColorAdjustments _color;
        Bloom _bloom;
        Vignette _vignette;
        WhiteBalance _white;
        ChromaticAberration _chromatic;
        float _baseExposure, _baseSaturation, _baseContrast, _baseBloom, _baseVignette, _baseTemperature, _baseChromatic;
        Volume _bound;

        /// <param name="breaks">Structure breaks' flashes (BreakMoments.Flashes), at their strengths; none without `--structure-breaks`.</param>
        public PostFeel(Timeline tl, Amounts amounts, bool enabled = true, IEnumerable<(double time, float strength)> breaks = null)
        {
            _map = tl.Map;
            _amounts = amounts;
            Enabled = enabled;
            _grades = new SectionGrade[_map.Sections.Length];
            for (int i = 0; i < _grades.Length; i++) _grades[i] = amounts.For(_map.Sections[i].Label);

            var flashes = new List<(double t, float s)>();
            foreach (var a in _map.Accents) flashes.Add((a.Time, a.Strength));
            foreach (var s in _map.Sections)
                if (s.Feel == SectionFeel.Free)
                    foreach (var a in s.Anchors) flashes.Add((a.Time, a.Strength));
            foreach (var a in tl.Actions)
                if (a.Kind == ActionKind.Attack && a.Entity == 0 && a.Strength >= amounts.HitStrength)
                    flashes.Add((a.Impact, a.Strength * amounts.HitFlash));
            if (breaks != null) flashes.AddRange(breaks);
            flashes.Sort((x, y) => x.t.CompareTo(y.t));
            _flashAt = new double[flashes.Count];
            _flashStrength = new float[flashes.Count];
            for (int i = 0; i < flashes.Count; i++) { _flashAt[i] = flashes[i].t; _flashStrength[i] = flashes[i].s; }
        }

        /// <summary>The beat pulse at t: 1 on a beat of a pulsed section, decaying to 0 over the beat.</summary>
        public float Pulse(double t)
        {
            var sec = _map.Sections[_map.SectionIndexAt(t)];
            if (sec.Feel == SectionFeel.Free || t < _map.Beats[0]) return 0f;
            double x = _map.BeatIndexAt(t);
            float u = (float)(x - Math.Floor(x + 1e-6));
            if (u < 0) u = 0;
            return (1 - u) * (1 - u);
        }

        /// <summary>The flash at t: the strongest accent, anchor or big hit still decaying (each over its beat).</summary>
        public float Flash(double t)
        {
            int i = Array.BinarySearch(_flashAt, t);
            if (i < 0) i = ~i - 1;
            float flash = 0;
            for (; i >= 0 && t - _flashAt[i] < 2.0; i--)
            {
                double u = (t - _flashAt[i]) / BeatLength(_flashAt[i]);
                if (u >= 1) continue;
                float k = (float)(1 - u);
                flash = Math.Max(flash, _flashStrength[i] * k * k * k);
            }
            return flash;
        }

        /// <summary>The section's grade at t, eased from the previous section's over the first bars.</summary>
        public SectionGrade Grade(double t, out float pulseScale)
        {
            int i = _map.SectionIndexAt(t);
            var cur = _grades[i];
            var prev = i > 0 ? _grades[i - 1] : SectionGrade.None;
            var sec = _map.Sections[i];
            double bar = BeatLength(sec.Start) * _map.BeatsPerBarAt(sec.FirstBar);
            double ease = Math.Min(_amounts.EaseBars * bar, 0.5 * (sec.End - sec.Start));
            float w = ease > 0 ? Smooth((float)((t - sec.Start) / ease)) : 1f;
            pulseScale = Mathf.Lerp(prev.Pulse, cur.Pulse, w);
            return new SectionGrade
            {
                Exposure = Mathf.Lerp(prev.Exposure, cur.Exposure, w),
                Saturation = Mathf.Lerp(prev.Saturation, cur.Saturation, w),
                Contrast = Mathf.Lerp(prev.Contrast, cur.Contrast, w),
                Temperature = Mathf.Lerp(prev.Temperature, cur.Temperature, w),
                Vignette = Mathf.Lerp(prev.Vignette, cur.Vignette, w),
            };
        }

        /// <summary>The offsets at song time t (all zero when disabled).</summary>
        public Offsets At(double t)
        {
            if (!Enabled) return default;
            var g = Grade(t, out float pulseScale);
            float pulse = Pulse(t) * pulseScale, flash = Flash(t);
            var a = _amounts;
            return new Offsets
            {
                Exposure = g.Exposure + a.BeatExposure * pulse + a.FlashExposure * flash,
                Bloom = a.BeatBloom * pulse,
                Vignette = g.Vignette + a.BeatVignette * pulse,
                Saturation = g.Saturation + a.FlashSaturation * flash,
                Contrast = g.Contrast,
                Temperature = g.Temperature,
                Chromatic = a.FlashChromatic * flash,
            };
        }

        /// <summary>Sets <paramref name="volume"/>'s parameters to its base values plus the offsets at t.</summary>
        public void Apply(Volume volume, double t)
        {
            if (!Enabled || volume == null) return;
            if (volume != _bound) Bind(volume);
            var o = At(t);
            if (_color != null)
            {
                _color.postExposure.value = _baseExposure + o.Exposure;
                _color.saturation.value = _baseSaturation + o.Saturation;
                _color.contrast.value = _baseContrast + o.Contrast;
            }
            if (_bloom != null) _bloom.intensity.value = Math.Max(0, _baseBloom + o.Bloom);
            if (_vignette != null) _vignette.intensity.value = _baseVignette + o.Vignette;
            if (_white != null) _white.temperature.value = _baseTemperature + o.Temperature;
            if (_chromatic != null) _chromatic.intensity.value = _baseChromatic + o.Chromatic;
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

        double BeatLength(double t)
        {
            double i = Math.Floor(_map.BeatIndexAt(t));
            return _map.TimeOfBeatIndex(i + 1) - _map.TimeOfBeatIndex(i);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3 - 2 * x);
        }
    }
}
