using System.Collections.Generic;
using UnityEngine;

namespace PS.Look.Effects
{
    /// <summary>
    /// The death flourish and the spawn warp (neon-look.md, The Axes &gt; VFX; ps-wnj.16), the rules both players share. They
    /// belong to the look's vfx option: without one (`--vfx classic`, the action cut) neither draws nor sounds.
    /// - **The death flourish**: on a death's impact frame, a light pop on the body (<see cref="Pop"/>) and a ring of
    ///   light racing out on the ground (<see cref="Ring"/>), on top of the shatter; the deaths of one instant within
    ///   <see cref="ImpulseField.MergeM"/> share one ring (<see cref="Rings"/>), as their impulse rings merge, so a mass
    ///   kill is a few larger rings, not a ring per body. Each player's impulse ring is the death's own.
    /// - **The spawn warp** (<see cref="Warp"/>): from a monster's spawn in view, rings on the ground collapse onto it
    ///   and a glow pops where they meet, before its first step; a spawn's own time, so it never builds up to a death.
    /// </summary>
    public static class Flourishes
    {
        /// <summary>The selections' effects (game/Content/effects.json, demo3d's Data/effects.json).</summary>
        public const string Pop = "death-pop", Ring = "death-ring", Warp = "spawn-warp";
        /// <summary>A shared ring's size: its deaths' mean scale times the square root of their count, up to this.</summary>
        public const float RingGrowthCap = 2.5f;
        /// <summary>Deaths this close in song time are one instant.</summary>
        public const double SameInstant = 1e-3;

        /// <summary>One death, or one shared ring: where on the ground, its body's scale and its gem.</summary>
        public struct Death
        {
            public double Time;
            public Vector2 At;
            public float Scale;
            public Color Tint;
        }

        /// <summary>
        /// Effect <paramref name="name"/>'s index under vfx option <paramref name="vfx"/>: -1 without one, else the effect
        /// (warned when the library lacks it: rebake the effects).
        /// </summary>
        public static int IndexOf(EffectLibrary library, string name, string vfx)
        {
            if (vfx == null) return -1;
            int i = library.IndexOf(name, vfx);
            if (i < 0) Debug.LogWarning($"Flourishes: the effect library has no '{name}' (rebake the effects)");
            return i;
        }

        /// <summary>
        /// The rings for <paramref name="deaths"/> (sorted by time) into <paramref name="rings"/>: each instant's deaths
        /// within <see cref="ImpulseField.MergeM"/> of the first one not yet taken share one ring at their centre, in the
        /// first one's gem, its scale their mean times the square root of their count (up to <see cref="RingGrowthCap"/>).
        /// </summary>
        public static void Rings(IReadOnlyList<Death> deaths, List<Death> rings)
        {
            rings.Clear();
            var taken = new bool[deaths.Count];
            for (int i = 0, end; i < deaths.Count; i = end)
            {
                for (end = i + 1; end < deaths.Count && deaths[end].Time - deaths[i].Time < SameInstant; end++) { }
                for (int a = i; a < end; a++)
                {
                    if (taken[a]) continue;
                    var first = deaths[a];
                    Vector2 sum = Vector2.zero;
                    float scale = 0;
                    int n = 0;
                    for (int b = a; b < end; b++)
                    {
                        if (taken[b] || (deaths[b].At - first.At).sqrMagnitude > ImpulseField.MergeM * ImpulseField.MergeM) continue;
                        taken[b] = true;
                        sum += deaths[b].At;
                        scale += deaths[b].Scale;
                        n++;
                    }
                    rings.Add(new Death
                    {
                        Time = first.Time, At = sum / n, Tint = first.Tint,
                        Scale = scale / n * Mathf.Min(RingGrowthCap, Mathf.Sqrt(n)),
                    });
                }
            }
        }
    }
}
