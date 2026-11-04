using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss Rush boss 4, the Rootwarden (ps-8z4.12; boss-rush.md, The roster: Echo eruptions): fair at the slowest and
    /// fastest tempos in its hollow, the echo taught alone first and punishing only a hero who stands still, its burrow,
    /// its phase two chaining the echo along the hero's path, the beatless fallback, determinism, its arena and its slot.
    /// </summary>
    public class RootwardenTests
    {
        static GameContent Content => Fairness.Content;
        static ArenaDef Hollow => Content.Arenas.Find(RootwardenModule.ArenaId);
        static int SlotHealth => (int)Content.BossRush.Bosses.First(b => b.Id == RootwardenModule.ModuleId).Health;

        /// <summary>Where the hero may stand in its hollow (from its centre): inside the wall, off the props.</summary>
        static bool InHollow(double x, double y)
        {
            var def = Hollow;
            if (!def.Outline.Contains(x, y, 0, 0) || def.Outline.DistanceToOutline(x, y) < 0.4) return false;
            foreach (var p in def.Props)
            {
                double r = Content.Arenas.Props[p.Kind].Bound + 0.4;
                if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) < r * r) return false;
            }
            return true;
        }

        static FairnessOptions Options(double bpm) => new FairnessOptions
        {
            Bpm = bpm, Act = 1, FightS = 120, Arena = RootwardenModule.ArenaId, Inside = InHollow, ArenaRadiusM = RootwardenModule.HollowM, ExtentM = RootwardenModule.HollowM + 1,
            // The camera holds its whole hollow still (TheCameraHoldsItsHollowWhole), so nothing in it is off screen.
            ScreenRadiusM = 2 * RootwardenModule.HollowM,
            Setup = new FightSetup { Health = SlotHealth },
        };

        static Simulation Sim(double bpm = 120, double songS = 220, bool beats = true)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            if (beats)
                foreach (var k in Drive.Known(new FixtureSong().Beats(0, songS, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            return sim;
        }

        static void Strike(Simulation sim, double damage)
        {
            ref readonly var b = ref sim.Fight.Bodies[0];
            sim.Strikes.Land(sim, new Blow
            {
                Shape = HitShape.Circle(b.X, b.Y, 0.1), T = sim.Now, Damage = damage, Tags = HitTags.Strike,
                Source = BlowSource.SwordSlash, Owner = -1, Part = -1,
            });
        }

        static void Until(Simulation sim, Func<bool> done, double maxS = 60)
        {
            double stop = sim.Now + maxS;
            while (!done() && sim.Now < stop) sim.StepTo(sim.Now + 1.0 / 60);
            Assert.IsTrue(done(), "timed out");
        }

        /// <summary>Its moves in order, as the fight made them, until <paramref name="count"/> or the time runs out.</summary>
        static List<RootwardenModule.Kind> Moves(Simulation sim, RootwardenModule m, int count, double maxS = 150, Action each = null)
        {
            var moves = new List<RootwardenModule.Kind>();
            int seen = m.Moves;
            double stop = sim.Now + maxS;
            while (moves.Count < count && sim.Now < stop)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                each?.Invoke();
                if (m.Moves > seen)
                {
                    seen = m.Moves;
                    moves.Add(m.Last);
                }
            }
            return moves;
        }

        static int Hurts(Simulation sim, double from) =>
            Drive.Events(sim, SimEventKind.HeroHit).Count(e => e.T > from && e.I0 == (int)HitOutcome.Hurt);

        [TestCase(90)]
        [TestCase(180)]
        public void TheRootwardenIsFairInItsHollow(double bpm)
        {
            var report = Fairness.Check(() => new RootwardenModule(), Options(bpm));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "its quake is a real dash wave");
        }

        [Test]
        public void ItTeachesTheEchoAloneThenEveryMoveInTurn()
        {
            var sim = Sim();
            var m = new RootwardenModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6 });
            int s = 0;
            var moves = Moves(sim, m, 9, each: () =>
            {
                // A hero walking a slow circle, so the echoes have somewhere to trail.
                if (++s % 30 == 0) sim.Target(sim.Fight.CentreX + 7 * Math.Cos(s / 90.0), sim.Fight.CentreY + 7 * Math.Sin(s / 90.0), sim.Now);
            });
            CollectionAssert.AreEqual(RootwardenModule.Teach, moves.Take(RootwardenModule.Teach.Length).ToArray(), "the echo alone, twice, first");
            CollectionAssert.IsSupersetOf(moves, new[]
            {
                RootwardenModule.Kind.Line, RootwardenModule.Kind.Seeds, RootwardenModule.Kind.Burrow, RootwardenModule.Kind.Quake,
            }, "every phase-one move by the ninth");
            Assert.AreEqual(1, sim.Fight.Phase);
            var tells = Drive.Events(sim, SimEventKind.BossTelegraph).ToList();
            Assert.That(tells.Select(e => e.MarkMove).Distinct(), Is.SupersetOf(new[] { BossMove.Smash, BossMove.Boulders, BossMove.Dive, BossMove.Wave }));
        }

        [Test]
        public void EachEchoEruptsWhereTheHeroStoodThreeBeatsBefore()
        {
            var sim = Sim();
            var m = new RootwardenModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var echoes = new List<Hazard>();
            var stood = new List<(double X, double Y)>();
            var seen = new HashSet<int>();
            int s = 0;
            while (echoes.Count < RootwardenModule.EchoMarks && sim.Now < 60)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                if (++s % 20 == 0) sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s / 50.0), sim.Fight.CentreY + 4 * Math.Cos(s / 70.0), sim.Now);
                foreach (var h in sim.Fight.Hazards)
                    if (seen.Add(h.Id))
                    {
                        echoes.Add(h);
                        sim.HeroWas(h.Telegraph.Start, out double x, out double y);
                        stood.Add((x, y));
                    }
            }
            Assert.AreEqual(RootwardenModule.EchoMarks, echoes.Count);
            Assert.That(echoes.Select(h => h.Attack).Distinct().Count(), Is.EqualTo(1), "one echo, one attack");
            for (int i = 0; i < echoes.Count; i++)
            {
                var h = echoes[i];
                Assert.AreEqual(3 * 0.5, h.Telegraph.Land - h.Telegraph.Start, 1e-6, "three beats at 120 BPM");
                Assert.AreEqual(ShapeKind.Circle, h.Shape.Kind);
                Assert.IsTrue((h.Flags & HazardFlags.Signature) != 0);
                Assert.AreEqual(stood[i].X, h.Shape.X, 0.05, "on the hero's spot as it was marked");
                Assert.AreEqual(stood[i].Y, h.Shape.Y, 0.05);
            }
            // One a beat.
            for (int i = 1; i < echoes.Count; i++) Assert.AreEqual(0.5, echoes[i].Telegraph.Land - echoes[i - 1].Telegraph.Land, 1e-6);
        }

        [Test]
        public void StandingStillIsHitAndWalkingOnTheBeatIsNot()
        {
            int During(bool walk)
            {
                var sim = Sim();
                sim.Fight.Start(new RootwardenModule(), sim.Now, new FightSetup { Health = 1e6 });
                Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
                double from = sim.Now;
                // The two taught echoes: a hero standing still, or walking a wide circle at full speed.
                int s = 0;
                while (sim.Now < from + 12)
                {
                    sim.StepTo(sim.Now + 1.0 / 60);
                    double a = ++s / 60.0 * 0.5;
                    if (walk) sim.Target(sim.Fight.CentreX + 9 * Math.Cos(a), sim.Fight.CentreY - 2 + 9 * Math.Sin(a), sim.Now);
                }
                return Hurts(sim, from);
            }
            Assert.That(During(walk: false), Is.GreaterThan(0), "its roots find a hero who stands still");
            Assert.AreEqual(0, During(walk: true), "and never one who keeps moving");
        }

        [Test]
        public void ItBurrowsUnderTheFloorAndBurstsUpOnTheHerosMarkedSpot()
        {
            var sim = Sim();
            var m = new RootwardenModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6 });
            Moves(sim, m, 1 + Array.IndexOf(RootwardenModule.PhaseOne, RootwardenModule.Kind.Burrow) + RootwardenModule.Teach.Length);
            Assert.AreEqual(RootwardenModule.Kind.Burrow, m.Last);
            var burst = sim.Fight.Hazards.ToArray().First(h => h.Move == BossMove.Dive);
            Assert.AreEqual(0, m.Sunk(sim.Fight, 0, burst.Telegraph.Start), 1e-9);
            double mid = (burst.Telegraph.Start + burst.Telegraph.Land) / 2;
            Until(sim, () => sim.Now >= mid);
            Assert.AreEqual(1, m.Sunk(sim.Fight, 0, sim.Now), 1e-9, "buried as it travels");
            Until(sim, () => sim.Now >= burst.Telegraph.Land + 0.1);
            Assert.AreEqual(0, m.Sunk(sim.Fight, 0, sim.Now), 1e-9, "risen at its burst");
            Assert.AreEqual((burst.ToX, burst.ToY), (sim.Fight.Bodies[0].X, sim.Fight.Bodies[0].Y));
            Assert.AreEqual((burst.Shape.X, burst.Shape.Y), (burst.ToX, burst.ToY), "it bursts on the marked spot");
            // Its ring of roots spreads round the burst a beat later.
            var ring = Drive.Events(sim, SimEventKind.BossTelegraph).Where(e => e.MarkShape == ShapeKind.Ring).ToList();
            Assert.That(ring.Count, Is.GreaterThan(0));
        }

        [Test]
        public void BelowHalfHealthItRetreatsUntouchableForFourBeatsThenChainsTheEchoAlongThePath()
        {
            var sim = Sim();
            var m = new RootwardenModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.StepTo(sim.Now + 10);
            Strike(sim, 510);
            Assert.AreEqual((FightStage.Transition, 2), (sim.Fight.Stage, sim.Fight.Phase));
            double health = sim.Fight.Health;
            Strike(sim, 100);
            Assert.AreEqual(health, sim.Fight.Health, 1e-9, "untouchable through its retreat");
            double from = sim.Now;
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(4 * 0.5, sim.Now - from, 0.1, "four beats at 120 BPM");
            Assert.AreEqual((sim.Fight.CentreX, sim.Fight.CentreY), (sim.Fight.Bodies[0].X, sim.Fight.Bodies[0].Y), "it drew back to the centre");
            var chain = new List<Hazard>();
            var seen = new HashSet<int>();
            int s = 0;
            var moves = Moves(sim, m, 1, each: () =>
            {
                if (++s % 15 == 0) sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s / 40.0), sim.Fight.CentreY - 5 + 3 * Math.Cos(s / 40.0), sim.Now);
            });
            Assert.AreEqual(RootwardenModule.PhaseTwo[0], moves[0]);
            while (chain.Count < RootwardenModule.ChainMarks && sim.Now < from + 30)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                if (++s % 15 == 0) sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s / 40.0), sim.Fight.CentreY - 5 + 3 * Math.Cos(s / 40.0), sim.Now);
                foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) chain.Add(h);
            }
            Assert.AreEqual(RootwardenModule.ChainMarks, chain.Count);
            // After the first, each eruption is a root along the hero's path from the last marked spot to this one.
            Assert.AreEqual(ShapeKind.Circle, chain[0].Shape.Kind);
            for (int i = 1; i < chain.Count; i++)
            {
                Assert.AreEqual(ShapeKind.Path, chain[i].Shape.Kind);
                double px = chain[i - 1].Shape.Kind == ShapeKind.Path ? chain[i - 1].Shape.X2 : chain[i - 1].Shape.X;
                double py = chain[i - 1].Shape.Kind == ShapeKind.Path ? chain[i - 1].Shape.Y2 : chain[i - 1].Shape.Y;
                Assert.AreEqual((px, py), (chain[i].Shape.X, chain[i].Shape.Y), "joined end to end");
            }
            var more = Moves(sim, m, 4);
            CollectionAssert.AreEqual(RootwardenModule.PhaseTwo.Skip(1).Take(4).ToArray(), more);
        }

        [Test]
        public void WithoutABeatItFightsOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var m = new RootwardenModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 400 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            double from = sim.Now;
            var tells = new List<Hazard>();
            var seen = new HashSet<int>();
            while (sim.Now < from + 45)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) tells.Add(h);
                if (sim.Now > from + 35 && sim.Fight.Stage == FightStage.Fighting) Strike(sim, 50);
            }
            double beat = Content.Beat.TelegraphBeatlessBeatS;
            Assert.That(m.Moves, Is.GreaterThanOrEqualTo(4), "it keeps attacking");
            Assert.That(tells.All(h => h.Telegraph.Land - h.Telegraph.Start >= 3 * beat - 1e-6), "three fallback beats of warning");
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage, "and it can be beaten");
        }

        [Test]
        public void TheSameSeedPlaysTheSameFight()
        {
            ulong Run()
            {
                var sim = Sim(128);
                sim.Fight.Start(new RootwardenModule(), sim.Now, new FightSetup { Health = 600 });
                for (int s = 0; s < 90; s++)
                {
                    sim.StepTo(sim.Now + 1);
                    if (s % 3 == 0 && sim.Fight.InFight && !sim.Fight.Untouchable) Strike(sim, 20);
                    sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s), sim.Fight.CentreY + 5 * Math.Cos(s * 0.7), sim.Now);
                }
                return sim.Hash ^ (ulong)Drive.Events(sim, SimEventKind.BossTelegraph).Count();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TheCameraHoldsItsHollowWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Hollow, sim.Now);
            sim.StepTo(sim.Now + 3);
            var framing = new ArenaFraming(new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 });
            var f = framing.Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(f.Scrolls, "the whole hollow in a still view");
            var o = Hollow.Outline;
            for (int i = 0; i < o.Count; i++)
                Assert.AreEqual(RootwardenModule.HollowM, Math.Sqrt(o.X[i] * o.X[i] + o.Y[i] * o.Y[i]), 0.01, "the module's geometry is its arena's");
            Assert.IsTrue(InHollow(Hollow.HeroStartX, Hollow.HeroStartY) && InHollow(Hollow.BossStartX, Hollow.BossStartY));
            // Its middle is open floor, for the echoes and the lines: every prop stands back by the wall.
            foreach (var p in Hollow.Props) Assert.Greater(Math.Sqrt(p.X * p.X + p.Y * p.Y), 0.7 * RootwardenModule.HollowM, $"prop {p.Kind}");
        }

        [Test]
        public void ItsSlotIsFoughtByItInItsHollow()
        {
            var config = new RunInputs { Seed = 2, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            int index = Array.FindIndex(config.BossRush.Bosses, b => b.Id == RootwardenModule.ModuleId);
            var slot = config.BossRush.Bosses[index];
            Assert.IsTrue(slot.Fight, "a landed fight: its module and its cast row");
            Assert.AreEqual(RootwardenModule.ArenaId, slot.Arena);
            Assert.AreEqual(3, index, "boss 4");
        }

        [Test]
        public void ItsBurstsLieAlongTheHerosTrail()
        {
            var sim = Sim();
            var bursts = new List<List<GemSpot>>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add(spots.ToList());
            sim.Fight.Start(new RootwardenModule(), sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var trail = new List<(double X, double Y)>();
            for (int s = 0; s < 6; s++)
            {
                sim.Target(sim.Fight.CentreX - 6 + 2.4 * s, sim.Fight.CentreY - 3, sim.Now);
                sim.StepTo(sim.Now + 0.5);
                sim.HeroWas(sim.Now, out double x, out double y);
                trail.Add((x, y));
            }
            Strike(sim, 120);
            sim.StepTo(sim.Now + 1.1);
            Assert.AreEqual(1, bursts.Count);
            Assert.AreEqual(1, bursts[0].Sum(g => g.Share), 1e-9);
            // Every gem lies within a couple of metres of where the hero walked.
            foreach (var g in bursts[0])
                Assert.Less(trail.Min(p => Math.Sqrt((p.X - g.X) * (p.X - g.X) + (p.Y - g.Y) * (p.Y - g.Y))), 3.5);
        }
    }
}
