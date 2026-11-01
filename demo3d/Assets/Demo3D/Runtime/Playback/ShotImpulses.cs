using System;
using System.Collections.Generic;
using Demo3D.Runtime.Crowd;
using PS.Choreography;
using PS.Look;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// A crystal-cut shot's rings on the look package's impulse field (neon-look.md, The impulse field), all known at
    /// load from its timeline, so fixed-time captures reproduce them: each of the hero's blows from the hero at its
    /// impact, reaching its farthest kill, weighted by its kills as the blow feel weighs it (a formation's smash the
    /// heaviest); each death that shatters (LookCrowd's breaks) a small ring at the body in its gem, which the field
    /// merges in a mass kill; and each structure break or formation smash (BreakMoments) a big ring at the bolt or the
    /// blow, in the palette's shatter gem. The trailer loads a shot's rings at its cut.
    /// </summary>
    public static class ShotImpulses
    {
        /// <summary>A blow's least reach (m): a crisp slash still shoves the grid round the hero.</summary>
        public const float MinBlowReachM = 3f;

        public static Impulse[] Of(Timeline tl, LookCrowd.BreakEvent[] breaks, BreakMoments moments, BlowFeel blows, Palette palette)
        {
            var rings = new List<Impulse>(breaks.Length + 64);
            // The deaths a blow makes land on its impact (Sim.Strike): its kills and the farthest of them from the hero.
            var kills = new Dictionary<double, (int Count, float FarM)>();
            foreach (var b in breaks)
            {
                var hero = tl.PositionAt(0, b.Time);
                float d = Vector2.Distance(new Vector2(hero.X, hero.Y), new Vector2(b.Hit.x, b.Hit.z));
                kills[b.Time] = kills.TryGetValue(b.Time, out var k) ? (k.Count + 1, Mathf.Max(k.FarM, d)) : (1, d);
                rings.Add(ImpulseField.Death(new Vector2(b.Hit.x, b.Hit.z), b.Time, b.Gem.gamma));
            }
            var feel = blows?.Feel ?? new BlowFeel.Amounts();
            foreach (var a in tl.ActionsOf(0))
            {
                if (a.Kind != ActionKind.Attack) continue;
                kills.TryGetValue(a.Impact, out var k);
                float weight = blows != null && blows.WeightAt(a.Impact) is float w && w >= 0 ? w : BlowFeel.Weight(k.Count, feel);
                var hero = tl.PositionAt(0, a.Impact);
                rings.Add(ImpulseField.Blow(new Vector2(hero.X, hero.Y), a.Impact, Mathf.Max(MinBlowReachM, k.FarM), weight, palette.HeroTrail));
            }
            foreach (var m in moments.Moments)
                rings.Add(ImpulseField.Shatter(new Vector2(m.Hit.x, m.Hit.z), m.Time, m.Crystal ? Mathf.Max(4f, m.SizeM) : m.SizeM, palette.ShatterGem));
            // In time order, so the field adds each at its end (a cut loads thousands).
            rings.Sort((x, y) => x.Start.CompareTo(y.Start));
            return rings.ToArray();
        }
    }
}
