using System.Collections.Generic;
using NUnit.Framework;
using PS.Look.Effects;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The death flourish and the spawn warp (neon-look.md, The Axes &gt; VFX; ps-wnj.16).</summary>
    public class FlourishTests
    {
        static Flourishes.Death D(double t, float x, float scale = 1f) =>
            new Flourishes.Death { Time = t, At = new Vector2(x, 0), Scale = scale, Tint = new Color(x, 0, 0, 1) };

        [Test]
        public void AnInstantsDeathsNearEachOtherShareOneLargerRing()
        {
            var rings = new List<Flourishes.Death>();
            // A clump of four killed at once, a lone death at the same instant 10 m away, and one a beat later.
            Flourishes.Rings(new[] { D(2, 0), D(2, 1), D(2, 2), D(2, 0.5f, 2), D(2, 10), D(2.5, 0) }, rings);
            Assert.That(rings.Count, Is.EqualTo(3));
            var clump = rings[0];
            Assert.That(clump.Time, Is.EqualTo(2));
            Assert.That(clump.At.x, Is.EqualTo(0.875f).Within(1e-5f), "at the clump's centre");
            Assert.That(clump.Scale, Is.EqualTo(1.25f * 2f).Within(1e-5f), "their mean scale times the square root of four");
            Assert.That(clump.Tint.r, Is.EqualTo(0), "in the first one's gem");
            Assert.That(rings[1].At.x, Is.EqualTo(10), "a death beyond the merge distance keeps its own ring");
            Assert.That(rings[1].Scale, Is.EqualTo(1));
            Assert.That(rings[2].Time, Is.EqualTo(2.5), "another instant's death is its own");
        }

        [Test]
        public void AMassKillsRingGrowsNoFurtherThanTheCap()
        {
            var deaths = new List<Flourishes.Death>();
            for (int i = 0; i < 100; i++) deaths.Add(D(1, i * 0.02f));
            var rings = new List<Flourishes.Death>();
            Flourishes.Rings(deaths, rings);
            Assert.That(rings.Count, Is.EqualTo(1));
            Assert.That(rings[0].Scale, Is.EqualTo(Flourishes.RingGrowthCap));
        }

        [Test]
        public void NoVfxOptionDrawsNoFlourish()
        {
            var library = ScriptableObject.CreateInstance<EffectLibrary>();
            try { Assert.That(Flourishes.IndexOf(library, Flourishes.Pop, null), Is.EqualTo(-1)); }
            finally { Object.DestroyImmediate(library); }
        }
    }
}
