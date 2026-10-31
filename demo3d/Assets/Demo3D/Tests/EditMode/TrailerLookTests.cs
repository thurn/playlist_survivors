using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Tests
{
    /// <summary>The trailer's look (trailer.md, "Look"): its impact lens hits and its lens dirt.</summary>
    public class TrailerLookTests
    {
        static SongMap _map;
        static BlowFeel.Amounts _feel;

        [OneTimeSetUp]
        public void Load()
        {
            _map = SongMap.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));
            _feel = BlowFeel.Amounts.FromOverlay(File.ReadAllText("Assets/Demo3D/Choreography/Data/intensity-overdrive.json"));
        }

        static double Eighth(double t)
        {
            double i = System.Math.Floor(_map.BeatIndexAt(t));
            return 0.5 * (_map.TimeOfBeatIndex(i + 1) - _map.TimeOfBeatIndex(i));
        }

        /// <summary>A timeline of the hero's blows at <paramref name="blows"/> (time, kills), with a slam's shock at each of <paramref name="slams"/>.</summary>
        static Timeline Blows(IEnumerable<(double t, int kills)> blows, IEnumerable<double> slams = null)
        {
            var actions = new List<ActionEvent>();
            foreach (var (t, kills) in blows)
            {
                actions.Add(new ActionEvent(0, ActionKind.Attack, t, -1, -1, 1f));
                for (int k = 0; k < kills; k++) actions.Add(new ActionEvent(1 + k, ActionKind.Death, t, -1, -1, 1f));
            }
            var effects = new List<EffectEvent>();
            foreach (var t in slams ?? new double[0])
                effects.Add(new EffectEvent(System.Array.IndexOf(Catalog.Effects, "slam-shock"), 0, 1, t, new System.Numerics.Vector3(0, 0.1f, 0), 0, 1));
            return new Timeline
            {
                Map = _map, Actions = actions.ToArray(), Effects = effects.ToArray(), EffectNames = Catalog.Effects,
                Segments = new MotionSegment[0], SegmentStart = new int[2],
            };
        }

        [Test]
        public void SlamsAndMassKillsHitTheLensWithTheirWeight()
        {
            double a = _map.TimeOfBarBeat("90.1"), b = _map.TimeOfBarBeat("90.3"), c = _map.TimeOfBarBeat("91.1"), d = _map.TimeOfBarBeat("91.3");
            var tl = Blows(new[] { (a, 5), (b, 60), (c, 30), (d, 3) }, new[] { a });
            var hits = TrailerLook.HitsOf(tl, _feel, 0, 1000);
            Assert.That(hits.Select(h => h.Time), Is.EqualTo(new[] { a, b, c }), "the slam, and the blows of 60 and 30; not the crisp blow");
            Assert.That(hits[0].Strength, Is.EqualTo(TrailerLook.ImpactFloor).Within(1e-5f), "a slam of a few kills: the floor");
            Assert.That(hits[1].Strength, Is.EqualTo(1f).Within(1e-5f), "60 kills: full weight");
            Assert.That(hits[2].Strength, Is.GreaterThan(TrailerLook.ImpactFloor).And.LessThan(1f), "30 kills: between");
            Assert.That(TrailerLook.HitsOf(tl, _feel, b, c).Select(h => h.Time), Is.EqualTo(new[] { b }), "only its shot's span");
        }

        [Test]
        public void AHitPeaksOnItsImpactAndIsGoneWithinAnEighthWithoutFlicker()
        {
            double t0 = _map.TimeOfBarBeat("90.1"), eighth = Eighth(t0);
            var look = new TrailerLook(_map, new[] { new TrailerLook.Hit { Time = t0, End = t0 + 10, Strength = 0.8f } }, 1f);
            Assert.That(look.StrengthAt(t0 - 1e-4, out _), Is.EqualTo(0f), "nothing before the impact");
            Assert.That(look.StrengthAt(t0, out _), Is.EqualTo(0.8f).Within(1e-5f), "its peak on the impact");
            float last = float.MaxValue;
            for (double t = t0; t < t0 + eighth; t += 1.0 / 240)
            {
                float s = look.StrengthAt(t, out _);
                Assert.That(s, Is.LessThanOrEqualTo(last), $"decaying smoothly at {t - t0:F4} s");
                last = s;
            }
            Assert.That(look.StrengthAt(t0 + eighth - 1.0 / 60, out _), Is.LessThan(0.01f), "near nothing a frame before the eighth");
            Assert.That(look.StrengthAt(t0 + eighth + 1e-6, out _), Is.EqualTo(0f), "gone within an eighth");
        }

        [Test]
        public void OverlappingHitsAddUpToTheSameCapAndACutClearsThem()
        {
            double t0 = _map.TimeOfBarBeat("90.1");
            var a = new TrailerLook.Hit { Time = t0, End = t0 + 0.05, Strength = 0.7f, Position = new UnityEngine.Vector3(-4, 0, 0) };
            var b = new TrailerLook.Hit { Time = t0 + 0.02, End = t0 + 10, Strength = 0.7f, Position = new UnityEngine.Vector3(4, 0, 0) };
            var look = new TrailerLook(_map, new[] { b, a }, 1f);
            Assert.That(look.StrengthAt(t0 + 0.02, out var p), Is.EqualTo(1f), "0.7 + about 0.6, capped");
            Assert.That(p.x, Is.GreaterThan(0f).And.LessThan(4f), "between them, nearer the stronger");
            float s = look.StrengthAt(t0 + 0.05, out p);
            Assert.That(s, Is.LessThan(0.7f), "the first one's shot has ended");
            Assert.That(p.x, Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void AnImpactReTriggersAtMostOnceAnEighth()
        {
            double t0 = _map.TimeOfBarBeat("90.1"), eighth = Eighth(t0);
            var tl = Blows(new[] { (t0, 40), (t0 + 0.4 * eighth, 60), (t0 + 1.5 * eighth, 40) }, new[] { t0 + 0.2 * eighth });
            var hits = TrailerLook.HitsOf(tl, _feel, 0, 1000);
            Assert.That(hits.Select(h => h.Time), Is.EqualTo(new[] { t0, t0 + 1.5 * eighth }), "the three within an eighth are one");
            Assert.That(hits[0].Strength, Is.EqualTo(1f).Within(1e-5f), "at the strongest's strength");
        }

        [Test]
        public void TheLensDirtIsGeneratedMostlyDarkWithSpecksAndSmudges()
        {
            var px = LensDirt.Pixels();
            Assert.That(px, Has.Length.EqualTo(LensDirt.Width * LensDirt.Height));
            Assert.That(LensDirt.Pixels(), Is.EqualTo(px), "deterministic");
            var lum = px.Select(c => (c.r + c.g + c.b) / 3f).OrderBy(v => v).ToArray();
            Assert.That(lum[lum.Length / 2], Is.LessThan(0.15f), "mostly dark, so a frame without bloom shows none of it");
            Assert.That(lum[lum.Length - 1], Is.GreaterThan(0.3f), "bright specks");
            Assert.That(lum[(int)(lum.Length * 0.95)], Is.GreaterThan(1.5f * lum[lum.Length / 2]), "smudges stand out from the haze");
        }
    }
}
