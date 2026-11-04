using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss Rush boss 2, the Hive Queen (ps-8z4.8; boss-rush.md, The roster: Swarm formations): fair at the slowest and
    /// fastest tempos in her clearing, the sweep taught alone before anything joins it, walking-width gaps in every row but
    /// the dash wave's, her phase two combining moves, the beatless fallback, determinism, her arena and her run slot.
    /// </summary>
    public class HiveQueenTests
    {
        static GameContent Content => Fairness.Content;
        static ArenaDef Clearing => Content.Arenas.Find(HiveQueenModule.ArenaId);
        static int SlotHealth => (int)Content.BossRush.Bosses.First(b => b.Id == HiveQueenModule.ModuleId).Health;

        /// <summary>Where the hero may stand in her clearing (from its centre): inside the walls, off the corner props.</summary>
        static bool InClearing(double x, double y)
        {
            var def = Clearing;
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
            Bpm = bpm, Act = 1, FightS = 120, Inside = InClearing, ArenaRadiusM = HiveQueenModule.CircumM, ExtentM = HiveQueenModule.CircumM + 1,
            // The camera holds her whole clearing still (TheCameraHoldsHerClearingWhole), so nothing in it is off screen.
            ScreenRadiusM = 2 * HiveQueenModule.CircumM,
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

        /// <summary>Her moves in order, as the fight made them, until <paramref name="count"/> or the time runs out.</summary>
        static List<HiveQueenModule.Kind> Moves(Simulation sim, HiveQueenModule q, int count, double maxS = 120)
        {
            var moves = new List<HiveQueenModule.Kind>();
            int seen = 0;
            double stop = sim.Now + maxS;
            while (moves.Count < count && sim.Now < stop)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                if (q.Moves > seen)
                {
                    seen = q.Moves;
                    moves.Add(q.Last);
                }
            }
            return moves;
        }

        [TestCase(90)]
        [TestCase(180)]
        public void TheHiveQueenIsFairInHerClearing(double bpm)
        {
            var report = Fairness.Check(() => new HiveQueenModule(), Options(bpm));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "her wall is a real dash wave");
        }

        [Test]
        public void SheTeachesTheSweepAloneThenTheChevronThenTheDiveThenEveryMoveInTurn()
        {
            var sim = Sim();
            var q = new HiveQueenModule();
            sim.Fight.Start(q, sim.Now, new FightSetup { Health = 1e6 });
            var moves = Moves(sim, q, 12);
            CollectionAssert.AreEqual(HiveQueenModule.Teach, moves.Take(4).ToArray(), "the sweep alone, twice, first");
            CollectionAssert.IsSupersetOf(moves, new[]
            {
                HiveQueenModule.Kind.Spiral, HiveQueenModule.Kind.Wall, HiveQueenModule.Kind.Net, HiveQueenModule.Kind.Chevron,
            }, "every phase-one move by the twelfth");
            Assert.AreEqual(1, sim.Fight.Phase);
            // Every move lands on the beat grid; the rows launch from her side and carry the signature.
            var tells = Drive.Events(sim, SimEventKind.BossTelegraph).ToList();
            Assert.That(tells.Select(e => e.MarkMove).Distinct(), Is.SupersetOf(new[] { BossMove.Fans, BossMove.Smash, BossMove.Wall }));
        }

        [Test]
        public void EverySweepLeavesWalkingWidthGapsAndTheWallNone()
        {
            double half = HiveQueenModule.RowHalfM;
            for (double offset = -4; offset <= 4; offset += 0.5)
            {
                var spans = HiveQueenModule.Stretches(half, offset);
                Assert.That(spans.Count, Is.GreaterThanOrEqualTo(3));
                for (int i = 1; i < spans.Count; i++)
                    Assert.AreEqual(HiveQueenModule.GapM, spans[i].From - spans[i - 1].To, 1e-9, "a gap the hero walks through");
                // Nowhere along the row is farther than half a stretch from a gap.
                Assert.That(spans.Max(s => s.To - s.From), Is.LessThanOrEqualTo(HiveQueenModule.GapEveryM - HiveQueenModule.GapM + 1e-9));
            }
            var wall = HiveQueenModule.Stretches(half, double.NaN);
            Assert.AreEqual(1, wall.Count, "the dash wave is solid");
            Assert.AreEqual((-half, half), (wall[0].From, wall[0].To));
            // The gaps a row leaves are clear of its drones' reach: the hero's body fits with room to spare.
            Assert.Greater(HiveQueenModule.GapM, 2 * 0.4 + 1.5);
        }

        [Test]
        public void BelowHalfHealthSheSwarmsUntouchableForFourBeatsThenCombinesHerMoves()
        {
            var sim = Sim();
            var q = new HiveQueenModule();
            sim.Fight.Start(q, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.StepTo(sim.Now + 10);
            Strike(sim, 510);
            Assert.AreEqual((FightStage.Transition, 2), (sim.Fight.Stage, sim.Fight.Phase));
            double health = sim.Fight.Health;
            Strike(sim, 100);
            Assert.AreEqual(health, sim.Fight.Health, 1e-9, "untouchable through the swarm");
            double from = sim.Now;
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(4 * 0.5, sim.Now - from, 0.1, "four beats at 120 BPM");
            Assert.AreEqual((sim.Fight.CentreX, sim.Fight.CentreY), (sim.Fight.Bodies[0].X, sim.Fight.Bodies[0].Y), "she swarmed to the centre");
            var moves = Moves(sim, q, 5);
            CollectionAssert.AreEqual(HiveQueenModule.PhaseTwo.Take(5).ToArray(), moves);
            // A combination lays two attacks at once.
            Assert.That(Drive.Events(sim, SimEventKind.BossTelegraph).Where(e => e.T > from).Select(e => e.T).GroupBy(t => Math.Round(t, 6)).Any(g => g.Count() > 6));
        }

        [Test]
        public void WithoutABeatSheFightsOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var q = new HiveQueenModule();
            sim.Fight.Start(q, sim.Now, new FightSetup { Health = 400 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            double from = sim.Now;
            var tells = new List<Hazard>();
            var seen = new HashSet<int>();
            while (sim.Now < from + 40)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) tells.Add(h);
                if (sim.Now > from + 30 && sim.Fight.Stage == FightStage.Fighting) Strike(sim, 50);
            }
            double beat = Content.Beat.TelegraphBeatlessBeatS;
            Assert.That(q.Moves, Is.GreaterThanOrEqualTo(4), "she keeps attacking");
            Assert.That(tells.All(h => h.Telegraph.Land - h.Telegraph.Start >= 3 * beat - 1e-6), "three fallback beats of warning");
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage, "and she can be beaten");
        }

        [Test]
        public void TheSameSeedPlaysTheSameFight()
        {
            ulong Run()
            {
                var sim = Sim(128);
                sim.Fight.Start(new HiveQueenModule(), sim.Now, new FightSetup { Health = 600 });
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
        public void TheCameraHoldsHerClearingWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Clearing, sim.Now);
            sim.StepTo(sim.Now + 3);
            var framing = new ArenaFraming(new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 });
            var f = framing.Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(f.Scrolls, "the whole clearing in a still view");
            // The module's geometry is her arena's: a flat-topped hexagon of its circumradius.
            var o = Clearing.Outline;
            Assert.AreEqual(6, o.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(HiveQueenModule.CircumM, Math.Sqrt(o.X[i] * o.X[i] + o.Y[i] * o.Y[i]), 0.01);
                Assert.AreEqual(0, Math.IEEERemainder(Math.Atan2(o.Y[i], o.X[i]), Math.PI / 3), 1e-3);
            }
            Assert.IsTrue(InClearing(Clearing.HeroStartX, Clearing.HeroStartY) && InClearing(Clearing.BossStartX, Clearing.BossStartY));
        }

        [Test]
        public void HerSlotIsFoughtByHerInHerClearing()
        {
            var config = new RunInputs { Seed = 2, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            int index = Array.FindIndex(config.BossRush.Bosses, b => b.Id == HiveQueenModule.ModuleId);
            var slot = config.BossRush.Bosses[index];
            Assert.IsTrue(slot.Fight, "a landed fight: her module and her cast row");
            Assert.AreEqual(HiveQueenModule.ArenaId, slot.Arena);
            Assert.AreEqual(1, index, "boss 2");
        }

        [Test]
        public void HerBurstsLieBetweenHerAndTheHero()
        {
            var sim = Sim();
            var bursts = new List<List<GemSpot>>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add(spots.ToList());
            sim.Fight.Start(new HiveQueenModule(), sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Strike(sim, 120);
            sim.StepTo(sim.Now + 1.1);
            Assert.AreEqual(1, bursts.Count);
            ref readonly var b = ref sim.Fight.Bodies[0];
            sim.HeroWas(sim.Now, out double hx, out double hy);
            double mx = bursts[0].Average(g => g.X), my = bursts[0].Average(g => g.Y);
            Assert.AreEqual(6, bursts[0].Count);
            Assert.AreEqual(1, bursts[0].Sum(g => g.Share), 1e-9);
            Assert.Less(Math.Sqrt((mx - (b.X + hx) / 2) * (mx - (b.X + hx) / 2) + (my - (b.Y + hy) / 2) * (my - (b.Y + hy) / 2)), 1.0);
        }
    }
}
