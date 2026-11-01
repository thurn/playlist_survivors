using System;
using PS.Look;
using UnityEngine.Rendering;

namespace Game.Client.Look
{
    /// <summary>
    /// The look's post stack moving with the live stream (unity-game.md, "Post-processing and
    /// Camera"), demo3d's PostFeel with the song map replaced by the fast lane and the sim's events,
    /// at the look's post option's beat and flash amounts (<see cref="PostOption.Feel"/>; under the classic
    /// look, the night post's, <see cref="NightFeel"/>):
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
    /// It writes offsets on top of the volume's base values (the look's stack, PostStack, or the
    /// classic look's night, NightLook.CreateProfile) through the
    /// package's <see cref="PostBinding"/>, read when it first binds; on a beat and a flash the bloom
    /// leans toward the palette's colours when the binding has its tints.
    /// </summary>
    public sealed class PostFeel
    {
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
        /// others lean with their palette's light. Blossom and snow are nights too, so they lift no exposure.
        /// </summary>
        public static ZoneGrade GradeOf(string zone)
        {
            switch (zone)
            {
                case "crypt": return new ZoneGrade { Exposure = -0.1f, Saturation = -10, Contrast = 8, Temperature = -10, Vignette = 0.05f, Pulse = 0.8f };
                case "lava": return new ZoneGrade { Exposure = 0.05f, Saturation = 10, Contrast = 6, Temperature = 12, Vignette = 0.04f, Pulse = 1.2f };
                case "blossom": return new ZoneGrade { Saturation = 6, Temperature = 4, Pulse = 1f };
                case "snow": return new ZoneGrade { Saturation = -6, Contrast = 4, Temperature = -8, Pulse = 0.9f };
                default: return ZoneGrade.None;
            }
        }

        /// <summary>The look the game draws: Boot's, else the fallback preset's (a test that runs no boot).</summary>
        public static ResolvedLook Look => ResolvedLook.Active ?? ResolvedLook.Resolve(new LookFlags());

        /// <summary>The post option <paramref name="look"/> draws, or null for the night post (NightLook): classic, or one not built.</summary>
        public static PostOption OptionOf(ResolvedLook look) => PostStack.OptionOf(look);

        /// <summary>The night post's beat pulse and full flash, from before the look's post stacks (demo3d's arena-layout postFeel).</summary>
        public static PostOption.FeelAmounts NightFeel => new PostOption.FeelAmounts
        {
            BeatExposure = 0.12f, BeatBloom = 0.35f, BeatVignette = -0.03f,
            FlashExposure = 0.35f, FlashChromatic = 0.5f, FlashSaturation = 25f,
        };

        /// <summary>The feel of <paramref name="option"/> (<see cref="OptionOf"/>): its own, or the night's.</summary>
        public static PostOption.FeelAmounts FeelOf(PostOption option) => option?.Feel ?? NightFeel;

        /// <summary>The beat's pulse and a full flash (the look's post option's, or the night's).</summary>
        public readonly PostOption.FeelAmounts Feel;
        /// <summary>The volume's base values and the offsets over them, with the look's bloom tints.</summary>
        public readonly PostBinding Binding = new PostBinding();

        ZoneGrade _from = ZoneGrade.None, _to = ZoneGrade.None;
        double _zoneStart, _zoneEase;

        public PostFeel(PostOption.FeelAmounts feel) => Feel = feel;

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
        public PostOffsets At(double s, in LaneFrame lane, FeelMoments moments)
        {
            var g = Grade(s);
            float pulse = (float)lane.Pulse * g.Pulse;
            float flash = moments.Flash(s, lane.Period);
            float rise = (float)lane.Rise;
            float kick = (float)Math.Max(lane.Kick, lane.KickPre);
            var f = Feel;
            return new PostOffsets
            {
                Exposure = g.Exposure + f.BeatExposure * pulse + f.FlashExposure * flash + RiseExposure * rise,
                Bloom = f.BeatBloom * pulse + RiseBloom * rise + KickBloom * kick,
                Vignette = g.Vignette + f.BeatVignette * pulse + RiseVignette * rise,
                Saturation = g.Saturation + f.FlashSaturation * flash + RiseSaturation * rise,
                Contrast = g.Contrast,
                Temperature = g.Temperature,
                Chromatic = f.FlashChromatic * flash,
                Pulse = pulse,
                Flash = flash,
            };
        }

        /// <summary>Sets <paramref name="volume"/>'s parameters to its base values plus <paramref name="o"/>.</summary>
        public void Apply(Volume volume, in PostOffsets o) => Binding.Apply(volume, o);
    }
}
