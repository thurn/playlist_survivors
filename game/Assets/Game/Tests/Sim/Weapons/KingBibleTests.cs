using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// King Bible (unity-game.md, The Other Weapons): the orbit's beat lattice against native's
    /// table, a ring's numbers, its blades' hits, snapping on a real beat, its end and
    /// a freeze.
    /// </summary>
    public class KingBibleTests
    {
        static GameContent Content => HordeFixture.Content;
        static int Bible => Content.WeaponIndex("king-bible");
        static WeaponDef Def => Content.Weapon("king-bible");

        static Simulation Start(int rank, double bpm = 0, double might = 1, double seconds = 60)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            var stats = Content.Progression.Hero.Stats;
            stats.Might = might;
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("king-bible", rank) }, Stats = stats, Cards = false };
            var sim = new Simulation(config);
            if (bpm > 0)
                foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, seconds, bpm).Entries()
                             .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                    sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<OrbitEvent> Rings(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Orbit).Select(e => e.AsOrbit).ToList();

        static List<HitEvent> Hits(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target >= 0 && h.Weapon == Bible).ToList();

        [Test]
        public void TheOrbitSnapsToNativesBeatLatticeWithinFifteenPercent()
        {
            const double Raw = 35 * 0.0625;
            foreach (var (bpm, speed, blades, locked) in new[]
                     {
                         (70.0, 1.0, 1, false), (128.0, 1.0, 1, false), (174.0, 1.0, 1, true), (70.0, 1.6, 4, true),
                         (128.0, 1.6, 4, true), (174.0, 1.6, 4, false), (128.0, 1.6, 3, false), (326.0, 1.3, 3, true),
                     })
            {
                double period = 60 / bpm;
                double omega = KingBible.OrbitSpeed(Raw * speed, period, blades, out bool snapped);
                Assert.AreEqual(locked, snapped, $"{bpm} BPM, speed {speed}, {blades} blades");
                if (!snapped)
                {
                    Assert.AreEqual(Raw * speed, omega, 1e-12);
                    continue;
                }
                Assert.LessOrEqual(Math.Abs(omega / (Raw * speed) - 1), 0.15 + 1e-12);
                for (int k = 0; k < blades; k++)
                    Assert.IsTrue(Enumerable.Range(0, 64).Any(beat =>
                    {
                        double q = (omega * period * beat + 2 * Math.PI * k / blades) / (Math.PI / 2);
                        return Math.Abs(q - Math.Round(q)) < 1e-9;
                    }), $"{bpm} BPM: blade {k} stands on a quarter turn on a beat");
            }
            // Both sides of a reciprocal grid: rounding 3.48 down would miss the slower four-beat quarter.
            double p = Math.PI / 2 / (Raw * 3.48);
            Assert.AreEqual(Math.PI / 2 / (4 * p), KingBible.OrbitSpeed(Raw, p, 1, out bool yes), 1e-12);
            Assert.IsTrue(yes);
            foreach (var (ratio, locked) in new[] { (0.85, true), (1.15, true), (0.84999, false), (1.15001, false) })
            {
                KingBible.OrbitSpeed(Raw, Math.PI / 2 / (Raw * ratio), 4, out bool s);
                Assert.AreEqual(locked, s, $"the 15% boundary at {ratio}");
            }
            KingBible.OrbitSpeed(Raw, 0, 1, out bool none);
            Assert.IsFalse(none, "no beat");
        }

        [TestCase(1, 1.0)]
        [TestCase(5, 1.0)]
        [TestCase(8, 1.25)]
        public void ARingHasItsRanksBladesRadiusDurationAndPower(int rank, double might)
        {
            var sim = Start(rank, might: might);
            var r = Def.Rank(rank);
            double area = Math.Min(r.Area, Def.Shape.OrbitAreaCap);
            HordeFixture.Place(sim, "xlbat", sim.State.Hero.X + Def.Shape.OrbitRadiusM * area, sim.State.Hero.Y + 0.2);
            sim.StepTo(1);
            var ring = Rings(sim).Single();
            Assert.AreEqual(r.Amount, ring.Blades);
            Assert.AreEqual(r.DurationS, ring.End - ring.Born, 1e-12, "the rank's duration times Duration (1)");
            Assert.AreEqual(Def.Shape.OrbitRadiusM * area, ring.Radius, 1e-12);
            Assert.AreEqual(Def.Shape.RadiusM * (1 + Def.Shape.BladeAreaSlope * r.Area), ring.Blade, 1e-12);
            Assert.AreEqual(Def.Shape.RadPerS * r.Speed, ring.Omega, 1e-12, "no beat: its own speed");
            double[] power = { 1, 1, 1, 2, 2, 2, 3, 3 };
            Assert.AreEqual(power[rank - 1] * might, Hits(sim).First().Damage, 1e-12, "native's power times Might");
        }

        [Test]
        public void ABladeHitsWhatItSweepsPastOnTheTickItTouchesKnockingItAlong()
        {
            // One blade at 2.19 rad/s from angle 0: an enemy at a quarter turn is hit at ~0.72 s, and on the next lap.
            var sim = Start(1);
            double radius = Def.Shape.OrbitRadiusM;
            double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;
            int quarter = HordeFixture.Place(sim, "xlbat", hx, hy + radius);
            int far = HordeFixture.Place(sim, "xlbat", hx, hy + radius + 3);
            sim.StepTo(3);
            var hits = Hits(sim);
            Assert.IsFalse(hits.Any(h => h.Target == far), "out of the blade's reach");
            var first = hits.First(h => h.Target == quarter);
            double omega = Def.Shape.RadPerS;
            // The blade's body (grown in by then) and half the bat's reach it a little before the quarter turn.
            double reach = Def.Shape.RadiusM * 1.3 + 0.5 * Content.Roles[Content.RoleIndex("xlbat")].RadiusM;
            double contact = (Math.PI / 2 - 2 * Math.Asin(reach / (2 * radius))) / omega;
            Assert.AreEqual(contact, first.T, Simulation.TickSeconds + 1e-9, "on the tick its sweep touches the body");
            Assert.AreEqual(1, hits.Count(h => h.Target == quarter), "once a lap: the ring ends before the next");
            double a = omega * first.T;
            Assert.AreEqual((-Math.Sin(a), Math.Cos(a)), (first.DirX, first.DirY), "knocked along the blade's way (counterclockwise)");
        }

        [Test]
        public void ABladeHitsAgainOnTheNextLapOnceItsMemoryHasCleared()
        {
            // Duration 2: a 6 s ring, so the blade comes round again 2.87 s later, after its 1.7 s memory has cleared.
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            var stats = Content.Progression.Hero.Stats;
            stats.Duration = 2;
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("king-bible", 1) }, Stats = stats, Cards = false };
            var sim = new Simulation(config);
            int bat = HordeFixture.Place(sim, "xlbat", sim.State.Hero.X, sim.State.Hero.Y + Def.Shape.OrbitRadiusM);
            sim.StepTo(6);
            var times = Hits(sim).Where(h => h.Target == bat).Select(h => h.T).ToList();
            Assert.AreEqual(2, times.Count, "once a lap, though the blade touches it for several ticks each time");
            // A lap later, give or take the little the first hit knocked it outward.
            Assert.AreEqual(2 * Math.PI / Def.Shape.RadPerS, times[1] - times[0], 0.05);
        }

        [Test]
        public void ARingShotOnABeatHasItsBladesOnQuarterTurnsOnTheBeats()
        {
            // Rank 8: four blades at speed 1.6, which snaps at 128 BPM.
            var sim = Start(8, bpm: 128);
            sim.StepTo(20);
            var beats = sim.Stream.Beats.ToArray().Select(b => b.T).ToList();
            var rings = Rings(sim);
            Assert.Greater(rings.Count, 1);
            foreach (var ring in rings)
            {
                Assert.IsTrue(ring.Snapped, $"the ring at {ring.Born}");
                Assert.IsTrue(beats.Any(b => Math.Abs(b - ring.Born) < 1e-9), $"the ring at {ring.Born} starts on a beat");
                foreach (double b in beats.Where(b => b >= ring.Born && b <= ring.End))
                    for (int k = 0; k < ring.Blades; k++)
                    {
                        double q = (ring.Omega * (b - ring.Born) + 2 * Math.PI * k / ring.Blades) / (Math.PI / 2);
                        Assert.AreEqual(Math.Round(q), q, 1e-6, $"blade {k} at the beat {b}");
                    }
            }
        }

        [Test]
        public void AFreezeCutsTheRingShortAndItHitsNothingThen()
        {
            var sim = Start(1);
            HordeFixture.Place(sim, "xlbat", sim.State.Hero.X, sim.State.Hero.Y + Def.Shape.OrbitRadiusM);
            sim.StepTo(0.3);
            sim.Control(RunControl.Pause);
            double paused = sim.Now;
            sim.StepTo(3);
            var rings = Rings(sim);
            Assert.AreEqual(2, rings.Count, "the ring, and its new end");
            Assert.AreEqual(rings[0].Ring, rings[1].Ring);
            Assert.AreEqual(paused + Simulation.TickSeconds, rings[1].End, 1e-9, "cut short at the end of the tick it froze in");
            Assert.AreEqual(0, Hits(sim).Count);
        }
    }
}
