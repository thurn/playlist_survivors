using System;
using UnityEngine;

namespace Game.Client.Hero
{
    /// <summary>The hero's clips, by role: their order is the bake's clip array and the graph's inputs.</summary>
    public enum HeroClip
    {
        Idle, Run, SlashL, SlashR, Slash3, Spin, Slam,
    }

    /// <summary>
    /// The hero's clip table, game/Content/hero-clips.json (unity-game.md, The Hero): the knight, its
    /// shield and each clip's length, impact and end in seconds from its start (demo3d's measured
    /// annotations), and the retiming that lands an attack's impact on the sim's (demo3d's
    /// ClipLibrary.Fit, copied and owned).
    /// </summary>
    [Serializable]
    public sealed class HeroClips
    {
        public const float MinSpeed = 0.75f, MaxSpeed = 1.33f;
        public const string ContentPath = "Content/hero-clips.json";

        [Serializable]
        public struct Clip
        {
            public string role, guid, clip;
            public float length, impact, end;
        }

        public float heightM;
        public string prefab, shield;
        public Clip[] clips;

        public static HeroClips Parse(string json)
        {
            var c = JsonUtility.FromJson<HeroClips>(json);
            var roles = Enum.GetNames(typeof(HeroClip));
            if (c.clips == null || c.clips.Length != roles.Length)
                throw new FormatException($"hero clips: {roles.Length} clips expected, in the order {string.Join(", ", roles)}");
            for (int i = 0; i < roles.Length; i++)
            {
                if (!string.Equals(c.clips[i].role, roles[i], StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"hero clips: clip {i} is '{c.clips[i].role}', expected '{roles[i]}'");
                if (c.clips[i].length <= 0) throw new FormatException($"hero clips: '{roles[i]}' has no length");
                if (c.clips[i].end < c.clips[i].impact) throw new FormatException($"hero clips: '{roles[i]}' ends before its impact");
            }
            return c;
        }

        public Clip this[HeroClip role] => clips[(int)role];

        /// <summary>
        /// A clip retimed onto an attack (<see cref="Fit"/>): its clock, a function of song time. The clip's first
        /// frame shows at <see cref="Start"/>; a long wind-up's <b>coil</b> plays it slowly from there to
        /// <see cref="CoilClip"/> (s into the clip), reached at <see cref="CoilEnd"/>, and from then it runs at
        /// <see cref="Speed"/>. With no coil, CoilEnd is Start and CoilClip 0.
        /// </summary>
        public struct Retime
        {
            public double Start, CoilEnd;
            public float Speed, CoilClip;

            /// <summary>The time into the clip at song time t (before its start, negative).</summary>
            public double ClipTime(double t) =>
                CoilClip > 0 && t < CoilEnd ? (t - Start) * CoilClip / (CoilEnd - Start) : CoilClip + (t - CoilEnd) * Speed;

            /// <summary>The song time the clip reaches <paramref name="clipTime"/>, at or past its coil.</summary>
            public double TimeAt(float clipTime) => CoilEnd + (clipTime - CoilClip) / Speed;
        }

        /// <summary>The share of a clip's time to its impact that a long wind-up's coil covers: the blade drawn back, before the strike.</summary>
        public const float CoilShare = 0.6f;

        /// <summary>
        /// Retiming (choreography.md): speed = clamp(impact / (impact time - wind-up start), 0.75, 1.33),
        /// start = impact time - impact / speed, so the clip's impact frame lands on the impact time.
        /// Faster than 1.33 it starts before the wind-up, and is entered part-way at the wind-up (a follow-up
        /// continuing from the swing before). Slower than 0.75 it still starts at the wind-up, never late with
        /// nothing shown: it coils, playing its first <see cref="CoilShare"/> of the way to the impact slowly enough
        /// that the rest, the strike, runs at 0.75.
        /// </summary>
        public static Retime Fit(float clipImpact, double windupStart, double impactTime)
        {
            double avail = impactTime - windupStart;
            double s = avail <= 0 ? MaxSpeed : clipImpact / avail;
            if (s >= MinSpeed)
            {
                float speed = (float)Math.Min(MaxSpeed, s);
                double start = impactTime - clipImpact / speed;
                return new Retime { Start = start, CoilEnd = start, Speed = speed };
            }
            float coil = CoilShare * clipImpact;
            return new Retime { Start = windupStart, CoilEnd = impactTime - (clipImpact - coil) / MinSpeed, Speed = MinSpeed, CoilClip = coil };
        }
    }
}
