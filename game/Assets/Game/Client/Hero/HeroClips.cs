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
        /// Retiming (choreography.md): speed = clamp(impact / (impact time - wind-up start), 0.75, 1.33),
        /// start = impact time - impact / speed, so the clip's impact frame lands on the impact time.
        /// Slower than 0.75 the clip starts after the wind-up; faster than 1.33 it starts before it,
        /// and is entered part-way at the wind-up (a follow-up continuing from the swing before).
        /// </summary>
        public static void Fit(float clipImpact, double windupStart, double impactTime, out double start, out float speed)
        {
            double avail = impactTime - windupStart;
            double s = avail <= 0 ? MaxSpeed : clipImpact / avail;
            speed = (float)Math.Max(MinSpeed, Math.Min(MaxSpeed, s));
            start = impactTime - clipImpact / speed;
        }
    }
}
