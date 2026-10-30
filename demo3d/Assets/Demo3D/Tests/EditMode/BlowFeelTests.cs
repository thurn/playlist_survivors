using System;
using System.Collections.Generic;
using System.IO;
using PS.Choreography;
using Demo3D.Runtime.Playback;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>The blow feel (camera-and-sound.md, Blow weight): blow weights, the hit-stop and the camera's response.</summary>
    public class BlowFeelTests
    {
        const string Overdrive = "Assets/Demo3D/Choreography/Data/intensity-overdrive.json";
        static SongMap _map;

        [OneTimeSetUp]
        public void Load() => _map = SongMap.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));

        static BlowFeel.Amounts Feel() => BlowFeel.Amounts.Parse(JsonValue.Parse(
            "{ \"crispKills\": 8, \"heavyKills\": 60, \"punch\": 0.05, \"shake\": 0.3, \"shakeCap\": 0.5, \"hitStop\": 0.06, \"hitStopCatchUp\": 0.12 }"));

        /// <summary>A timeline with a hero blow on each of <paramref name="beats"/>, killing <paramref name="kills"/> each.</summary>
        static Timeline Blows(int[] beats, int[] kills)
        {
            var actions = new List<ActionEvent>();
            int monster = 1;
            for (int i = 0; i < beats.Length; i++)
            {
                double t = _map.Beats[beats[i]];
                actions.Add(new ActionEvent(0, ActionKind.Attack, t, -1, -1, 0.5f));
                for (int k = 0; k < kills[i]; k++) actions.Add(new ActionEvent(monster++, ActionKind.Death, t, -1, -1, 0.5f));
            }
            return new Timeline { Map = _map, Actions = actions.ToArray() };
        }

        [Test]
        public void WeightGrowsWithKillsFromCrispToHeavy()
        {
            var feel = Feel();
            Assert.That(BlowFeel.Weight(5, feel), Is.EqualTo(0f));
            Assert.That(BlowFeel.Weight(8, feel), Is.EqualTo(0f));
            Assert.That(BlowFeel.Weight(20, feel), Is.InRange(0.3f, 0.7f));
            Assert.That(BlowFeel.Weight(60, feel), Is.EqualTo(1f).Within(1e-6));
            Assert.That(BlowFeel.Weight(250, feel), Is.EqualTo(1f));
            var blows = new BlowFeel(Blows(new[] { 40, 41 }, new[] { 60, 5 }), feel);
            Assert.That(blows.WeightAt(_map.Beats[40]), Is.EqualTo(1f).Within(1e-6));
            Assert.That(blows.WeightAt(_map.Beats[41]), Is.EqualTo(0f));
            Assert.That(blows.WeightAt(_map.Beats[42]), Is.EqualTo(-1f));
        }

        /// <summary>A heavy blow lands on time, the world holds through its hit-stop and is back on song time before the next eighth; a crisp one doesn't stop.</summary>
        [Test]
        public void HitStopHoldsAHeavyBlowAndCatchesUpBeforeTheNextEighth()
        {
            var blows = new BlowFeel(Blows(new[] { 40, 41 }, new[] { 60, 5 }), Feel());
            double heavy = _map.Beats[40], crisp = _map.Beats[41];
            Assert.That(blows.WorldTime(heavy), Is.EqualTo(heavy));
            Assert.That(blows.WorldTime(heavy + 0.05), Is.EqualTo(heavy).Within(1e-9));
            Assert.That(blows.WorldTime(heavy + 0.1), Is.InRange(heavy, heavy + 0.1));
            Assert.That(blows.WorldTime(heavy + 0.18), Is.EqualTo(heavy + 0.18));
            Assert.That(blows.WorldTime(crisp + 0.03), Is.EqualTo(crisp + 0.03));
            double last = double.NegativeInfinity;
            for (double t = heavy - 0.1; t < crisp + 0.3; t += 1.0 / 240)
            {
                double w = blows.WorldTime(t);
                Assert.That(w, Is.GreaterThanOrEqualTo(last), "the world ran backwards at " + t);
                Assert.That(w, Is.LessThanOrEqualTo(t));
                last = w;
            }
        }

        /// <summary>A heavy blow punches deeper and shakes harder than the plain feel, within the feel's cap; without a feel the camera is as before.</summary>
        [Test]
        public void HeavyBlowsPunchAndShakeHarder()
        {
            var tl = Blows(new[] { 40, 44 }, new[] { 60, 5 });
            var plain = new CameraFeel(tl);
            var heavy = new CameraFeel(tl, new BlowFeel(tl, Feel()));
            double hit = _map.Beats[40] + (_map.Beats[41] - _map.Beats[40]) / 4, crisp = _map.Beats[44] + (_map.Beats[45] - _map.Beats[44]) / 4;
            Assert.That(heavy.Punch(hit), Is.LessThan(1f - CameraFeel.PunchAmount - 0.04f));
            Assert.That(heavy.Punch(crisp), Is.EqualTo(plain.Punch(crisp)));
            float shake = 0, plainShake = 0;
            for (double t = _map.Beats[40]; t < _map.Beats[40] + 0.1; t += 0.005)
            {
                shake = Math.Max(shake, heavy.Shake(t).magnitude);
                plainShake = Math.Max(plainShake, plain.Shake(t).magnitude);
            }
            Assert.That(shake, Is.GreaterThan(plainShake + 0.1f));
            Assert.That(shake, Is.LessThanOrEqualTo(0.5f + 1e-4f));
        }

        [Test]
        public void OnlyAnOverlayWithAFeelHasOne()
        {
            Assert.That(BlowFeel.Amounts.FromOverlay(null), Is.Null);
            Assert.That(BlowFeel.Amounts.FromOverlay("{ \"name\": \"x\" }"), Is.Null);
            var od = BlowFeel.Amounts.FromOverlay(File.ReadAllText(Overdrive));
            Assert.That(od, Is.Not.Null);
            Assert.That(od.HeavyKills, Is.GreaterThan(od.CrispKills));
            Assert.That(BlowFeel.Amounts.Parse(JsonValue.Parse("{}")).HeroScale, Is.EqualTo(1f));
            Assert.That(od.HeroScale, Is.GreaterThan(1f));
            Assert.Throws<FormatException>(() => BlowFeel.Amounts.Parse(JsonValue.Parse("{ \"crispKills\": 60, \"heavyKills\": 8 }")));
            Assert.Throws<FormatException>(() => BlowFeel.Amounts.Parse(JsonValue.Parse("{ \"hitStop\": 0.2, \"hitStopCatchUp\": 0.1 }")));
            Assert.Throws<FormatException>(() => BlowFeel.Amounts.Parse(JsonValue.Parse("{ \"heroScale\": 0 }")));
        }
    }
}
