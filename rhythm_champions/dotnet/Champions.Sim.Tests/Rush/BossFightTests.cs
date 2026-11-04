using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The Boss Rush framework (boss-rush.md, "Fights"; ps-8z4.3): a module's fight arriving on a downbeat, hazards on the
    /// beat grid drawn as boss marks, phases with untouchable transitions, several bodies with their own health, XP
    /// bursts on the beat, the Ogre's port onto the building blocks, and the fairness harness catching unfair attacks.
    /// </summary>
    public class BossFightTests
    {
        const double Bpm = 120, BeatS = 60 / Bpm;

        static Simulation Sim(Action<SimConfig> tweak = null, double songS = 200, double bpm = Bpm)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Fairness.Content);
            config.Hero.Health = 1e9;
            tweak?.Invoke(config);
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, songS, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            return sim;
        }

        static double[] Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Select(e => e.T).ToArray();

        static void Strike(Simulation sim, int body, double damage)
        {
            ref readonly var b = ref sim.Fight.Bodies[body];
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

        static bool OnGrid(double[] beats, double t) =>
            beats.Any(b => Math.Abs(b - t) < 0.002) || beats.Zip(beats.Skip(1), (a, b) => (a + b) / 2).Any(m => Math.Abs(m - t) < 0.002);

        /// <summary>A test boss: lays what <see cref="Lay"/> says on every downbeat, or on bodies' cue.</summary>
        sealed class Probe : BossModule
        {
            public Action<BossFight, double> Lay;
            public BodySpec[] Spec = { new BodySpec("fatOgre") };
            public double[] Phases = Array.Empty<double>();
            public readonly List<(int Body, double T)> Fell = new List<(int, double)>();
            public readonly List<(int Phase, double T, double Until)> PhaseStarts = new List<(int, double, double)>();
            public int Every = 4;
            int beats;

            public override string Id => "probe";
            public override BodySpec[] Bodies => Spec;
            public override double[] PhasesBelow => Phases;

            protected internal override void Beat(BossFight f, double t, int beat, bool downbeat)
            {
                if (f.Stage == FightStage.Fighting && beats++ % Every == 0) Lay?.Invoke(f, t);
            }

            protected internal override void BodyDied(BossFight f, int body, double t) => Fell.Add((body, t));

            protected internal override void PhaseBegins(BossFight f, int phase, double t, double until) => PhaseStarts.Add((phase, t, until));
        }

        // ---- Arrival, hazards, drawing ----

        [Test]
        public void AFightCallsThenArrivesOnADownbeatAndItsHazardsDrawAsBossMarksOnTheBeat()
        {
            var sim = Sim();
            var probe = new Probe { Lay = (f, t) => { f.HeroAt(t, out double x, out double y); f.Add(new Hazard { Body = 0, Shape = HitShape.Circle(x, y, 1.5), Telegraph = f.Plan(3), Damage = 10, Attack = f.NextAttack() }); } };
            sim.Fight.Start(probe, sim.Now);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.FightCall).Count, "the call warms the looks before the arrival");
            Until(sim, () => sim.Fight.InFight);
            var arrive = Drive.Events(sim, SimEventKind.BossArrive).Single();
            Assert.AreEqual(sim.Fight.Bodies[0].Enemy, arrive.I0);
            Assert.IsTrue(sim.Enemies[arrive.I0].Boss);
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.I2 != 0).Select(e => e.T);
            Assert.That(downbeats, Has.Some.EqualTo(arrive.T).Within(1e-9), "on a downbeat");
            sim.StepTo(sim.Now + 20);
            var marks = Drive.Events(sim, SimEventKind.BossTelegraph);
            var lands = Drive.Events(sim, SimEventKind.BossMove).Where(e => e.I2 == 0).ToList();
            Assert.That(marks.Count, Is.GreaterThan(3));
            Assert.That(marks.Select(e => e.I0), Is.All.GreaterThanOrEqualTo(BossFight.FirstMarkId), "numbered clear of the main game's marks");
            Assert.That(marks.Select(e => e.MarkMove), Is.All.EqualTo(BossMove.Hazard));
            var beats = Beats(sim);
            foreach (var m in marks) Assert.That(m.D0 - m.T, Is.GreaterThanOrEqualTo(3 * BeatS - 1e-6), "act 1 warns three beats");
            Assert.That(lands.Select(e => e.T), Is.All.Matches<double>(t => OnGrid(beats, t)));
            Assert.That(Drive.Events(sim, SimEventKind.HeroHit), Is.Not.Empty, "aimed at a hero standing still, they hit");
        }

        [Test]
        public void PlanNeverWarnsUnderTheActsBeatsOrSixTenthsOfASecondAndLandsOnHalfBeatsWhenAsked()
        {
            var sim = Sim(bpm: 200);
            Telegraph one = default, half = default, act3 = default, fast = default;
            var probe = new Probe { Lay = (f, t) => { if (one.Land == 0) { one = f.Plan(1); half = f.Plan(1, Landing.HalfBeat); } } };
            sim.Fight.Start(probe, sim.Now, new FightSetup { Act = 1 });
            Until(sim, () => one.Land > 0);
            double beat = 60 / 200.0;
            Assert.AreEqual(3 * beat, one.Land - one.Start, 0.01, "act 1: three beats");
            Assert.AreEqual(one.Land + beat / 2, half.Land, 0.01, "half a beat after");
            var late = Sim(bpm: 200);
            var probe3 = new Probe { Lay = (f, t) => { if (act3.Land == 0) act3 = f.Plan(1); } };
            late.Fight.Start(probe3, late.Now, new FightSetup { Act = 4 });
            Until(late, () => act3.Land > 0);
            Assert.AreEqual(2 * beat, act3.Land - act3.Start, 0.01, "later acts: two beats (0.6 s at 200 BPM)");
            var faster = Sim(bpm: 240);
            var probeFast = new Probe { Lay = (f, t) => { if (fast.Land == 0) fast = f.Plan(1); } };
            faster.Fight.Start(probeFast, faster.Now, new FightSetup { Act = 5 });
            Until(faster, () => fast.Land > 0);
            Assert.AreEqual(3 * 0.25, fast.Land - fast.Start, 0.01, "at 240 BPM two beats are under 0.6 s: three");
        }

        [Test]
        public void ProjectilesLaunchOnTheBeatAndTravelLingeringHazardsHitOnceAndSpinningBeamsTurn()
        {
            var sim = Sim();
            int shot = -1;
            var probe = new Probe
            {
                Every = 1000,
                Lay = (f, t) =>
                {
                    f.HeroAt(t, out double hx, out double hy);
                    // A shot from 10 m left of the hero, aimed at it at 10 m/s, and a burning pool on the hero.
                    shot = BossKit.Shot(f, 0, hx - 10, hy, 0, 10, 0.5, 20, 7, f.Plan(3), f.NextAttack());
                    f.Add(new Hazard { Body = 0, Shape = HitShape.Circle(hx, hy + 30, 1), Telegraph = f.Plan(3), ActiveS = 2, Damage = 3, Attack = f.NextAttack() });
                },
            };
            sim.Fight.Start(probe, sim.Now);
            Until(sim, () => shot >= 0);
            double launch = sim.Fight.Hazards.ToArray().First(h => h.Attack == shot).Telegraph.Land;
            Until(sim, () => sim.Fight.Hazards.Length == 0, 10);
            sim.StepTo(sim.Now + 1);
            var hits = Drive.Events(sim, SimEventKind.HeroHit).Where(e => e.T >= launch - 1e-9).ToList();
            Assert.AreEqual(1, hits.Count, "the shot hits once; the pool is far away");
            Assert.AreEqual(launch + (10 - 0.5 - 0.4) / 10, hits[0].T, 0.02, "when it reaches the hero, not at its launch");

            var beam = new Hazard
            {
                Telegraph = new Telegraph(0, 1, -1, -1, true), Shape = HitShape.Path(0, 0, 10, 0, 0.5), ActiveS = 2, SpinRadPerS = Math.PI / 2,
            };
            var turned = beam.ShapeAt(2);
            Assert.AreEqual(0, turned.X2, 1e-9);
            Assert.AreEqual(10, turned.Y2, 1e-9, "a quarter turn after a second");
            var ring = new Hazard { Telegraph = new Telegraph(0, 1, -1, -1, true), Shape = HitShape.Ring(0, 0, 0, 1), GrowMps = 5, ActiveS = 2 };
            Assert.AreEqual((5.0, 6.0), (ring.ShapeAt(2).Inner, ring.ShapeAt(2).Radius), "a ring grows keeping its thickness");
        }

        [Test]
        public void AHoldKeepsPendingLandingsOnTheGrid()
        {
            var sim = Sim();
            var probe = new Probe { Lay = (f, t) => BossKit.Stomp(f, 0, 3, 5, 3) };
            sim.Fight.Start(probe, sim.Now);
            Until(sim, () => sim.Fight.Hazards.Length > 0);
            sim.Control(RunControl.Pause, sim.Now);
            sim.StepTo(sim.Now + 0.77);
            sim.Control(RunControl.Resume, sim.Now);
            sim.StepTo(sim.Now + 15);
            var beats = Beats(sim);
            var lands = Drive.Events(sim, SimEventKind.BossMove).Where(e => e.I2 == 0).Select(e => e.T).ToList();
            Assert.That(lands, Is.Not.Empty);
            Assert.That(lands, Is.All.Matches<double>(t => OnGrid(beats, t)));
        }

        // ---- Phases, bodies, shedding, death ----

        [Test]
        public void BelowEachThresholdAPhaseBeginsCallingOffItsHazardsAndTheBossIsUntouchableThroughItsTransition()
        {
            var sim = Sim();
            var probe = new Probe { Phases = new[] { 0.6, 0.3 }, Every = 1, Lay = (f, t) => BossKit.Stomp(f, 0, 3, 5, 4) };
            sim.Fight.Start(probe, sim.Now, new FightSetup { Health = 100 });
            Until(sim, () => sim.Fight.Hazards.Length > 0);
            Strike(sim, 0, 45);
            Assert.AreEqual((FightStage.Transition, 2), (sim.Fight.Stage, sim.Fight.Phase));
            Assert.AreEqual(0, sim.Fight.Hazards.Length, "its hazards called off");
            Assert.That(Drive.Events(sim, SimEventKind.BossMove).Where(e => e.I2 != 0), Is.Not.Empty);
            Strike(sim, 0, 20);
            Assert.AreEqual(55, sim.Fight.Health, 1e-9, "untouchable: the blow takes nothing");
            var (phase, from, until) = probe.PhaseStarts.Single();
            Assert.That(until - from, Is.LessThanOrEqualTo(4 * BeatS + BeatS + 1e-6), "four beats at most (from the next beat)");
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(until, sim.Now, 1.0 / 60 + 1e-6);
            Strike(sim, 0, 30);
            Assert.AreEqual(3, sim.Fight.Phase, "below 30%: the third phase");
            Assert.AreEqual(2, Drive.Events(sim, SimEventKind.BossPhase).Count);
        }

        [Test]
        public void SeveralBodiesHaveTheirOwnHealthAndTheFightEndsWhenTheLastFalls()
        {
            var sim = Sim();
            var probe = new Probe { Spec = new[] { new BodySpec("fatOgre", -5), new BodySpec("fatOgre", 0), new BodySpec("dragon", 5, share: 2) } };
            int died = 0;
            sim.Fight.Died += (s, f) => died++;
            sim.Fight.Start(probe, sim.Now, new FightSetup { Health = 400 });
            Until(sim, () => sim.Fight.InFight);
            var bodies = sim.Fight.Bodies.ToArray();
            CollectionAssert.AreEqual(new[] { 100.0, 100.0, 200.0 }, bodies.Select(b => b.MaxHealth), "health split by the shares");
            Assert.AreEqual(3, Drive.Events(sim, SimEventKind.BossArrive).Count);
            Strike(sim, 1, 150);
            Assert.AreEqual((1, false), (probe.Fell.Single().Body, sim.Fight.Bodies[1].Alive));
            Assert.AreEqual(FightStage.Entrance, sim.Fight.Stage, "the others fight on");
            Assert.AreEqual(300, sim.Fight.Health, 1e-9);
            Strike(sim, 0, 150);
            Strike(sim, 2, 250);
            Assert.AreEqual((FightStage.Dead, 1), (sim.Fight.Stage, died));
            Assert.AreEqual(3, Drive.Events(sim, SimEventKind.BossDeath).Count, "each body shatters as it falls");
        }

        [Test]
        public void EachTenthOfHealthLostShedsABurstOnTheNextBeatAndTheKillShedsTheRest()
        {
            var sim = Sim();
            var bursts = new List<(int Burst, double T, int Spots)>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add((burst, s.Now, spots.Count));
            sim.Fight.Start(new Probe(), sim.Now, new FightSetup { Health = 100 });
            Until(sim, () => sim.Fight.InFight);
            Strike(sim, 0, 25);
            Assert.IsEmpty(bursts, "on the next beat");
            sim.StepTo(sim.Now + BeatS + 0.01);
            CollectionAssert.AreEqual(new[] { 0, 1 }, bursts.Select(b => b.Burst), "90% and 80% passed");
            Assert.That(bursts.Select(b => b.Spots), Is.All.EqualTo(6), "the default ring");
            Strike(sim, 0, 100);
            CollectionAssert.AreEqual(Enumerable.Range(0, 9), bursts.Select(b => b.Burst), "the kill sheds the rest at once");
        }

        // ---- The Ogre's port ----

        [Test]
        public void TheOgrePortedOntoTheBuildingBlocksUsesTheMainGamesMovesAndPhase()
        {
            var sim = Sim();
            sim.Fight.Start(OgreModule.ModuleId, sim.Now, new FightSetup { Health = 200 });
            Until(sim, () => sim.Fight.InFight);
            sim.StepTo(sim.Now + 40);
            var moves = Drive.Events(sim, SimEventKind.BossTelegraph).Select(e => e.MarkMove).Distinct().ToList();
            CollectionAssert.IsSubsetOf(new[] { BossMove.Smash, BossMove.Boulders, BossMove.Stomp }, moves, "its moves in turn");
            Strike(sim, 0, 110);
            Assert.AreEqual(FightStage.Transition, sim.Fight.Stage);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Strike(sim, 0, 200);
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage);
        }

        [Test]
        public void TheMainGamesSmashAndTheKitsLandAlike()
        {
            // The main game's duel draws its smash with the kit's shape: the same line from the same aim.
            var shape = BossKit.SmashLine(1, 2, 0, 1, 2, 7, 1.25);
            Assert.AreEqual((1.0, 3.0, 1.0, 11.0, 1.25), (shape.X, shape.Y, shape.X2, shape.Y2, shape.Radius));
        }

        [TestCase(90)]
        [TestCase(180)]
        public void TheOgreIsFair(double bpm)
        {
            var report = Fairness.Check(() => new OgreModule(), new FairnessOptions { Bpm = bpm, Setup = new FightSetup { Health = 300 } });
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
        }

        // ---- The harness catches unfair fights ----

        [Test]
        public void TheHarnessFindsAnAttackWithNoWayOut()
        {
            // A disc of 30 m on the hero in three beats, burning two seconds: walking 6 m/s gets nowhere near its edge, and
            // the dash's i-frames (which beat any instant impact) don't last the burn.
            var report = Fairness.Check(() => new Probe
            {
                Every = 8,
                Lay = (f, t) => { f.HeroAt(t, out double x, out double y); f.Add(new Hazard { Body = 0, Shape = HitShape.Circle(x, y, 30), Telegraph = f.Plan(3), ActiveS = 2, Damage = 1, Attack = f.NextAttack() }); },
            }, new FairnessOptions { FightS = 20 });
            Assert.That(report.Problems, Has.Some.Contains("no hit-free path"), report.ToString());
        }

        [Test]
        public void TheHarnessFindsShortOffGridAndOffScreenTelegraphsAndCrowdedDashWaves()
        {
            var report = Fairness.Check(() => new Probe
            {
                Every = 4,
                Lay = (f, t) =>
                {
                    f.HeroAt(t, out double x, out double y);
                    // Too short, off the grid, off screen, and two dash waves a beat apart.
                    f.Add(new Hazard { Body = 0, Shape = HitShape.Circle(x + 3, y, 1), Telegraph = new Telegraph(t, t + 0.4, -1, -1, true), Attack = f.NextAttack() });
                    f.Add(new Hazard { Body = 0, Shape = HitShape.Circle(x - 3, y, 1), Telegraph = new Telegraph(t, t + 3 * BeatS + 0.13, -1, -1, true), Attack = f.NextAttack() });
                    f.Add(new Hazard { Body = -1, Shape = HitShape.Circle(x + 40, y, 1), Telegraph = f.Plan(3), Attack = f.NextAttack() });
                    BossKit.Wave(f, 0, x, y + 30, 5, 10, 1, 1, 3);
                    f.Add(new Hazard { Body = 0, Shape = HitShape.Ring(x, y + 30, 0, 1), Telegraph = f.Plan(4), Flags = HazardFlags.DashWave, Attack = f.NextAttack() });
                },
            }, new FairnessOptions { FightS = 10, ArenaRadiusM = 60 });
            Assert.That(report.Problems, Has.Some.Contains("warns 0.40 s"), report.ToString());
            Assert.That(report.Problems, Has.Some.Contains("off the beat"));
            Assert.That(report.Problems, Has.Some.Contains("off screen"));
            Assert.That(report.Problems, Has.Some.Contains("dash waves at"));
        }

        [Test]
        public void ARealDashWaveNeedsTheDashAndTheHarnessCountsIt()
        {
            // A ring outrunning the walk, from right beside the hero: only the dash's i-frames cross it.
            var report = Fairness.Check(() => new Probe
            {
                Every = 8,
                Lay = (f, t) => { f.HeroAt(t, out double x, out double y); BossKit.Wave(f, 0, x + 1, y, 18, 14, 0.8, 10, 3); },
            }, new FairnessOptions { FightS = 30, WanderS = 0 });
            Fairness.AssertFair(report);
            Assert.That(report.DashWaves, Is.GreaterThan(0));
            Assert.AreEqual(report.DashWaves, report.DashWavesNeedingDash, report.ToString());
        }

        // ---- In the Boss Rush run ----

        sealed class Shedder : BossModule
        {
            public override string Id => "fightProbe";
            public override BodySpec[] Bodies => new[] { new BodySpec("fatOgre") };
            protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
            {
                into.Add(new GemSpot(f.CentreX + 10, f.CentreY, 1));
                into.Add(new GemSpot(f.CentreX - 10, f.CentreY, 3));
            }
        }

        [Test]
        public void ALandedSlotRunsItsFightInTheRunAndShedsItsBudgetAtTheModulesSpots()
        {
            BossModules.Register("fightProbe", () => new Shedder());
            var config = new RunInputs { Seed = 3, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Fairness.Content);
            config.Hero.Health = 1e9;
            var slot = config.BossRush.Bosses[config.BossRush.NextPlayed(0)];
            slot.Id = "fightProbe";
            slot.Fight = slot.Landed = true;
            slot.Health = 100;
            slot.Experience = 50;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 200, Bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            Until(sim, () => sim.Fight.InFight, 60);
            Assert.AreEqual((BossStage.None, BossRushStage.Fighting), (sim.Bosses.Stage, sim.BossRush.Stage), "the fight, not the stand-in");
            Strike(sim, 0, 25);
            sim.StepTo(sim.Now + BeatS + 0.01);
            Assert.AreEqual(2, sim.BossRush.Bursts, "90% and 80% passed");
            Assert.AreEqual(10, sim.BossRush.Shed, 1e-9, "a tenth of the budget each");
            var gems = Drive.Events(sim, SimEventKind.PickupDrop).Select(e => (e.D0 - sim.Fight.CentreX, e.D2)).ToList();
            Assert.That(gems, Has.Some.Matches<(double X, double V)>(g => Math.Abs(g.X - 10) < 1e-6 && Math.Abs(g.V - 1.25) < 1e-6), "a quarter of a burst at the first spot");
            Assert.That(gems, Has.Some.Matches<(double X, double V)>(g => Math.Abs(g.X + 10) < 1e-6 && Math.Abs(g.V - 3.75) < 1e-6));
            Strike(sim, 0, 100);
            Assert.AreEqual(BossRushStage.Reward, sim.BossRush.Stage, "the fight's death is the run's kill");
            Assert.AreEqual(50, sim.BossRush.Shed, 1e-9, "the kill drops the rest");
        }

        // ---- Sessions ----

        [Test]
        public void ARunsFightIsRecordedInItsInputs()
        {
            var run = new RunInputs { Fight = OgreModule.ModuleId, FightAct = 3 };
            var bytes = new MemoryStream();
            using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) run.Write(w);
            bytes.Position = 0;
            var back = RunInputs.Read(new BinaryReader(bytes), 8, "inputs.bin");
            Assert.AreEqual((OgreModule.ModuleId, 3), (back.Fight, back.FightAct));
            var config = back.Build(Fairness.Content);
            var sim = new Simulation(config);
            Assert.AreEqual(FightStage.Coming, sim.Fight.Stage, "a scripted fight starts with the run");
            Assert.Throws<ArgumentException>(() => new RunInputs { Fight = "noSuchBoss" }.Build(Fairness.Content));
        }
    }
}
