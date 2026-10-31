using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Magic Wand (unity-game.md, The Other Weapons): shots on eighths in bursts spaced as native's,
    /// crystal bolts at the nearest enemy arriving on sixteenths, damage against native's rank table,
    /// pierce, turning from a dead enemy, an aimless bolt and a freeze.
    /// </summary>
    public class MagicWandTests
    {
        const double Bpm = 128;

        static GameContent Content => HordeFixture.Content;
        static int Wand => Content.WeaponIndex("magic-wand");

        /// <summary>A standing hero with a wand at <paramref name="rank"/> and no spawns, on beats at <paramref name="bpm"/> (none: no beat).</summary>
        static Simulation Start(int rank, double bpm = Bpm, double might = 1)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            var stats = Content.Progression.Hero.Stats;
            stats.Might = might;
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("magic-wand", rank) }, Stats = stats, Cards = false };
            var sim = new Simulation(config);
            if (bpm > 0)
                foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 60, bpm).Entries()
                             .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                    sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static int Place(Simulation sim, string role, double dx, double dy) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        static List<ProjectileEvent> Legs(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Projectile).Select(e => e.AsProjectile).Where(p => p.Weapon == Wand).ToList();

        static List<HitEvent> Hits(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target >= 0 && h.Weapon == Wand).ToList();

        static double Phase(Simulation sim, double t, double per)
        {
            var beats = sim.Stream.Beats.ToArray().Select(b => b.T).ToList();
            int b = beats.FindLastIndex(x => x <= t + 1e-9);
            return (t - beats[b]) / (beats[b + 1] - beats[b]) * per;
        }

        [Test]
        public void ShotsLandOnEighthsAndEachFiresABurstSpacedByNativesRepeat()
        {
            // Rank 4: three bolts every 1 s, 0.1 s apart at speed 1 (native's repeat / projectile speed).
            var sim = Start(4);
            Place(sim, "xlbat", 30, 0);
            sim.StepTo(10);
            var shots = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.ByHero && a.Weapon == Wand)
                .Select(a => a.T).ToList();
            Assert.AreEqual(10, shots.Count, 1, "one shot a second");
            foreach (double t in shots) Assert.AreEqual(Math.Round(Phase(sim, t, 2)), Phase(sim, t, 2), 1e-6, $"shot at {t} on an eighth");
            var launches = Legs(sim).Where(p => p.Phase == ProjectilePhase.Launch).Select(p => p.T).ToList();
            foreach (double t in shots.Take(shots.Count - 1))
                CollectionAssert.AreEqual(new[] { t, t + 0.1, t + 0.2 }.Select(x => Math.Round(x, 9)),
                    launches.Where(x => x >= t - 1e-9 && x < t + 0.5).Select(x => Math.Round(x, 9)), $"the burst at {t}");
        }

        [TestCase(1, 1.0)]
        [TestCase(5, 1.0)]
        [TestCase(8, 1.5)]
        public void ABoltHitsTheNearestEnemyOnASixteenthForPowerTimesMight(int rank, double might)
        {
            var sim = Start(rank, might: might);
            int near = Place(sim, "xlbat", 4, 1);
            Place(sim, "xlbat", -6, 3);
            sim.StepTo(1.2);
            var launch = Legs(sim).First(p => p.Phase == ProjectilePhase.Launch);
            Assert.AreEqual(near, launch.Target, "the nearest enemy");
            double flight = Math.Sqrt(17) / (Content.Weapon("magic-wand").Shape.SpeedMps * Content.Weapon("magic-wand").Rank(rank).Speed);
            Assert.GreaterOrEqual(launch.Arrive, launch.T + flight - 1e-6, "never faster than the bolt's speed");
            Assert.Less(launch.Arrive, launch.T + flight + 60 / Bpm / 4, "the first sixteenth after its flight");
            Assert.AreEqual(Math.Round(Phase(sim, launch.Arrive, 4)), Phase(sim, launch.Arrive, 4), 1e-6, "arrives on a sixteenth");
            var hit = Hits(sim).First();
            Assert.AreEqual((near, launch.Arrive), (hit.Target, hit.T));
            double[] power = { 1, 1, 1, 1, 2, 2, 2, 3 };
            Assert.AreEqual(power[rank - 1] * might, hit.Damage, 1e-12, "native's power times Might");
            var wand = Content.Weapon("magic-wand");
            Assert.AreEqual(wand.Shape.RadiusM, launch.Radius, 1e-12);
        }

        [Test]
        public void AKillCostsNoPierceSoTheBoltFliesOnUntilAHitThatDoesntKill()
        {
            // Rank 1, pierce 1: it kills the bat, turns for the far brute and ends on it.
            var sim = Start(1);
            int bat = Place(sim, "bat1", 3, 0);
            int brute = Place(sim, "xlbat", 0, 7);
            Place(sim, "xlbat", -20, 0);
            sim.StepTo(2);
            var legs = Legs(sim).TakeWhile(p => p.Phase != ProjectilePhase.End).ToList();
            var end = Legs(sim).First(p => p.Phase == ProjectilePhase.End);
            Assert.AreEqual(new[] { (ProjectilePhase.Launch, bat), (ProjectilePhase.Turn, brute) },
                legs.Take(2).Select(p => (p.Phase, p.Target)).ToArray());
            var deaths = Drive.Events(sim, SimEventKind.Death).Select(e => e.AsDeath).ToList();
            Assert.AreEqual((bat, legs[0].Arrive), (deaths[0].Enemy, deaths[0].T), "the bat dies on the arrival");
            Assert.AreEqual(legs[0].Arrive, legs[1].T, "it turns where the bat fell, then");
            Assert.AreEqual(legs[1].Arrive, end.T, "a hit that doesn't kill uses its one pierce");
            Assert.AreEqual(end.Projectile, legs[0].Projectile);
        }

        [Test]
        public void PierceTwoHitsTwoSurvivorsAndNeverTheSameTwiceWithinItsHitDelay()
        {
            // Rank 7: pierce 2, four bolts a shot. Two survivors near each other: each bolt hits both, then ends.
            var sim = Start(7);
            int a = Place(sim, "xlbat", 3, 0);
            int b = Place(sim, "xlbat", 3, 3);
            sim.StepTo(0.9);
            var first = Legs(sim).Where(p => p.Projectile == 0).ToList();
            Assert.AreEqual(new[] { ProjectilePhase.Launch, ProjectilePhase.Turn, ProjectilePhase.End }, first.Select(p => p.Phase).ToArray());
            Assert.AreEqual(new[] { a, b }, first.Take(2).Select(p => p.Target).ToArray());
            var hits = Hits(sim).Where(h => h.T <= first[2].T + 1e-9).Select(h => h.Target).ToList();
            CollectionAssert.IsSubsetOf(new[] { a, b }, hits);
        }

        [Test]
        public void ABoltWhoseEnemyDiesInFlightTurnsAtTheTicksEnd()
        {
            var sim = Start(1);
            int bat = Place(sim, "xlbat", 8, 0);
            int other = Place(sim, "xlbat", 0, -9);
            sim.StepTo(0.05);
            var launch = Legs(sim).Single();
            Assert.AreEqual(bat, launch.Target);
            sim.StepTo(launch.T + 0.3);
            double killed = sim.Now;
            sim.Horde.Kill(sim, bat, killed, 1, 0, 0);
            sim.StepTo(killed + 2 * Simulation.TickSeconds);
            var turn = Legs(sim).Single(p => p.Phase == ProjectilePhase.Turn);
            Assert.AreEqual(killed + Simulation.TickSeconds, turn.T, 1e-9, "at the end of the tick it died in");
            Assert.AreEqual(other, turn.Target);
            double u = (turn.T - launch.T) / (launch.Arrive - launch.T);
            Assert.AreEqual(launch.FromX + (launch.ToX - launch.FromX) * u, turn.FromX, 1e-9, "from where it was on its leg");
            Assert.AreEqual(launch.FromY + (launch.ToY - launch.FromY) * u, turn.FromY, 1e-9);
        }

        [Test]
        public void WithNoEnemyABoltFliesAlongTheFacingAndFades()
        {
            var sim = Start(1, bpm: 0);
            sim.StepTo(0.9);
            var legs = Legs(sim);
            Assert.AreEqual(new[] { ProjectilePhase.Launch, ProjectilePhase.End }, legs.Select(p => p.Phase).ToArray());
            Assert.AreEqual(-1, legs[0].Target);
            Assert.AreEqual(MagicWand.AimlessS, legs[1].T - legs[0].T, 1e-9);
        }

        [Test]
        public void AFreezeEndsTheBoltsInFlightAndHoldsTheBurst()
        {
            var sim = Start(4);
            Place(sim, "xlbat", 12, 0);
            sim.StepTo(0.05);
            sim.Control(RunControl.Pause);
            sim.StepTo(3);
            var legs = Legs(sim);
            Assert.AreEqual(1, legs.Count(p => p.Phase == ProjectilePhase.Launch), "the burst's later bolts never leave");
            Assert.AreEqual(ProjectilePhase.End, legs.Last().Phase);
            Assert.AreEqual(0, Hits(sim).Count);
        }

        [Test]
        public void TheSameRunGivesTheSameBolts()
        {
            ulong Run()
            {
                var config = HordeFixture.Config(minimum: 60, heroStands: true);
                config.Arsenal = new ArsenalConfig { Weapons = new[] { ("magic-wand", 8) }, Cards = false };
                var sim = new Simulation(config);
                foreach (var k in Drive.Known(new FixtureSong(4).Beats(0, 30, 140).Entries()
                             .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                    sim.AddKnown(k.Entry, k.KnownAt);
                sim.StepTo(25);
                Assert.Greater(Hits(sim).Count, 50, "the wand works the horde");
                return sim.Hash;
            }
            Assert.AreEqual(Run(), Run());
        }

#if !UNITY_5_3_OR_NEWER
        [Test]
        public void TheWandAndTheBibleAllocateNothingWhileStepping()
        {
            Simulation sim = null;
            long allocated = Drive.LeastAllocated(() =>
            {
                var config = HordeFixture.Config(heroStands: true);
                config.Arsenal = new ArsenalConfig { Weapons = new[] { ("magic-wand", 8), ("king-bible", 8) }, Cards = false };
                sim = new Simulation(config);
                foreach (var k in Drive.Known(new FixtureSong(4).Beats(0, 60, 128).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
                sim.StepTo(10); // warm up (JIT, the rings' pool)
                return () => sim.StepTo(60);
            });
            Assert.Greater(Hits(sim).Count, 100);
            Assert.AreEqual(0, allocated, "bytes allocated while stepping 50 s");
        }
#endif
    }
}
