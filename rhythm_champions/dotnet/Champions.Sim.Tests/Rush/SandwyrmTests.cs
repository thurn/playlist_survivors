using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss Rush boss 6, the Sandwyrm (ps-8z4.17; boss-rush.md, The roster: Ripples): fair at the slowest and fastest
    /// tempos in its basin, the hunt taught alone first and erupting on a downbeat where the hero stood when it dived, the
    /// tunnel's wake collapsing behind it, the breach's arc as its dash wave, its phase two chaining the hunt, the beatless
    /// fallback, determinism, its arena and its slot.
    /// </summary>
    public class SandwyrmTests
    {
        static GameContent Content => Fairness.Content;
        static ArenaDef Basin => Content.Arenas.Find(SandwyrmModule.ArenaId);
        static int SlotHealth => (int)Content.BossRush.Bosses.First(b => b.Id == SandwyrmModule.ModuleId).Health;

        /// <summary>Where the hero may stand in its basin (from its centre): inside the wall, off the props.</summary>
        static bool InBasin(double x, double y)
        {
            var def = Basin;
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
            Bpm = bpm, Act = 2, FightS = 130, Arena = SandwyrmModule.ArenaId, Inside = InBasin, ArenaRadiusM = SandwyrmModule.BasinM,
            ExtentM = SandwyrmModule.BasinM + 1,
            // The camera holds its whole basin still (TheCameraHoldsItsBasinWhole), so nothing in it is off screen.
            ScreenRadiusM = 2 * SandwyrmModule.BasinM,
            Setup = new FightSetup { Health = SlotHealth, Act = 2 },
        };

        static Simulation Sim(double bpm = 120, double songS = 240, bool beats = true)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            if (beats)
                foreach (var k in Drive.Known(new FixtureSong().Beats(0, songS, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            return sim;
        }

        /// <summary>The basin's centre in a test sim (no arena): where the arena would stand round the hero's start.</summary>
        static (double X, double Y) Centre(Simulation sim) =>
            (sim.Fight.CentreX - SandwyrmModule.HeroStartX, sim.Fight.CentreY - SandwyrmModule.HeroStartY);

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

        /// <summary>A hero walking a slow loop round the basin's middle, retargeted every half second.</summary>
        static Action Wander(Simulation sim)
        {
            int s = 0;
            return () =>
            {
                if (++s % 30 != 0) return;
                var c = Centre(sim);
                sim.Target(c.X + 7 * Math.Cos(s / 90.0), c.Y + 6 * Math.Sin(s / 90.0), sim.Now);
            };
        }

        /// <summary>Its moves in order, as the fight made them, until <paramref name="count"/> or the time runs out.</summary>
        static List<SandwyrmModule.Kind> Moves(Simulation sim, SandwyrmModule m, int count, double maxS = 180, Action each = null)
        {
            var moves = new List<SandwyrmModule.Kind>();
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

        /// <summary>Every hazard laid from now until <paramref name="untilS"/> later.</summary>
        static List<Hazard> Laid(Simulation sim, double untilS, Action each = null, Func<List<Hazard>, bool> done = null)
        {
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            double stop = sim.Now + untilS;
            while (sim.Now < stop && (done == null || !done(laid)))
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                each?.Invoke();
                foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) laid.Add(h);
            }
            return laid;
        }

        static int Hurts(Simulation sim, double from) =>
            Drive.Events(sim, SimEventKind.HeroHit).Count(e => e.T > from && e.I0 == (int)HitOutcome.Hurt);

        [TestCase(90)]
        [TestCase(180)]
        public void TheSandwyrmIsFairInItsBasin(double bpm)
        {
            var report = Fairness.Check(() => new SandwyrmModule(), Options(bpm));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "its breach is a real dash wave");
        }

        [Test]
        public void ItTeachesTheHuntAloneThenEveryMoveInTurn()
        {
            var sim = Sim();
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            var moves = Moves(sim, m, 3 + 7, each: Wander(sim));
            CollectionAssert.AreEqual(SandwyrmModule.Teach, moves.Take(SandwyrmModule.Teach.Length).ToArray(), "the hunt alone, three times, first");
            CollectionAssert.AreEqual(SandwyrmModule.PhaseOne.Take(7).ToArray(), moves.Skip(3).ToArray(), "then its moves in turn");
            Assert.AreEqual(1, sim.Fight.Phase);
            var tells = Drive.Events(sim, SimEventKind.BossTelegraph).ToList();
            Assert.That(tells.Select(e => e.MarkMove).Distinct(), Is.SupersetOf(new[] { BossMove.Dive, BossMove.Smash, BossMove.Boulders }));
        }

        [Test]
        public void EachHuntTravelsUnderTheSandAndEruptsOnADownbeatWhereTheHeroStoodAsItDived()
        {
            var sim = Sim();
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var wander = Wander(sim);
            var stood = new Dictionary<int, (double X, double Y)>();
            var hunts = Laid(sim, 40, () =>
            {
                wander();
                foreach (var h in sim.Fight.Hazards)
                    if (!stood.ContainsKey(h.Id))
                    {
                        sim.HeroWas(h.Telegraph.Start, out double hx, out double hy);
                        stood[h.Id] = (hx, hy);
                    }
            }, l => l.Count >= 3);
            Assert.AreEqual(3, hunts.Count, "three hunts, one at a time");
            foreach (var h in hunts)
            {
                Assert.AreEqual(BossMove.Dive, h.Move);
                Assert.IsTrue((h.Flags & HazardFlags.Signature) != 0);
                Assert.GreaterOrEqual(h.Telegraph.Land - h.Telegraph.Start, SandwyrmModule.HuntBeats * 0.5 - 1e-6, "three beats at 120 BPM at least");
                Assert.AreEqual(stood[h.Id].X, h.Shape.X, 0.05, "on the hero's spot as it dived");
                Assert.AreEqual(stood[h.Id].Y, h.Shape.Y, 0.05);
                Assert.AreEqual((h.Shape.X, h.Shape.Y), (h.ToX, h.ToY), "and it surfaces there");
            }
            // Under the sand while its ripple runs, standing once it has burst.
            var last = hunts[hunts.Count - 1];
            Until(sim, () => sim.Now >= (last.Telegraph.Start + last.Telegraph.Land) / 2);
            Assert.IsTrue(m.Under(sim.Now));
            Assert.AreEqual(1, m.Sunk(sim.Fight, 0, sim.Now), 1e-9, "buried as it travels");
            Until(sim, () => sim.Now >= last.Telegraph.Land + 0.2);
            Assert.IsFalse(m.Under(sim.Now));
            Assert.AreEqual(0, m.Sunk(sim.Fight, 0, sim.Now), 1e-9);
            Assert.AreEqual((last.ToX, last.ToY), (sim.Fight.Bodies[0].X, sim.Fight.Bodies[0].Y));
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.Downbeat).Select(e => e.T).ToList();
            foreach (var h in hunts) Assert.IsTrue(downbeats.Any(t => Math.Abs(t - h.Telegraph.Land) < 1e-6), "it erupts on a downbeat");
        }

        [Test]
        public void StandingStillIsFoundAndWalkingIsNot()
        {
            int During(bool walk)
            {
                var sim = Sim();
                sim.Fight.Start(new SandwyrmModule(), sim.Now, new FightSetup { Health = 1e6, Act = 2 });
                Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
                double from = sim.Now;
                // The three taught hunts: a hero standing still, or walking a wide loop.
                int s = 0;
                while (sim.Now < from + 12)
                {
                    sim.StepTo(sim.Now + 1.0 / 60);
                    double a = ++s / 60.0 * 0.6;
                    var c = Centre(sim);
                    if (walk) sim.Target(c.X + 9 * Math.Cos(a), c.Y + 9 * Math.Sin(a), sim.Now);
                }
                return Hurts(sim, from);
            }
            Assert.That(During(walk: false), Is.GreaterThan(0), "it finds a hero who stands still");
            Assert.AreEqual(0, During(walk: true), "and never one who keeps moving");
        }

        [Test]
        public void ItsTunnelCollapsesBehindItThroughTheHerosSpotOneHalfBeatAtATime()
        {
            var sim = Sim();
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Moves(sim, m, SandwyrmModule.Teach.Length + Array.IndexOf(SandwyrmModule.PhaseOne, SandwyrmModule.Kind.Tunnel) + 1, each: Wander(sim));
            Assert.AreEqual(SandwyrmModule.Kind.Tunnel, m.Last);
            var tunnel = sim.Fight.Hazards.ToArray();
            Assert.That(tunnel.Select(h => h.Attack).Distinct().Count(), Is.EqualTo(1), "one attack");
            var end = tunnel.Single(h => h.Move == BossMove.Dive);
            var wake = tunnel.Where(h => h.Move != BossMove.Dive).OrderBy(h => h.Telegraph.Land).ToList();
            Assert.That(wake.Count, Is.GreaterThanOrEqualTo(3));
            for (int i = 1; i < wake.Count; i++) Assert.AreEqual(0.25, wake[i].Telegraph.Land - wake[i - 1].Telegraph.Land, 1e-6, "a half beat apart");
            Assert.GreaterOrEqual(end.Telegraph.Land, wake[wake.Count - 1].Telegraph.Land, "it surfaces at its end last");
            // All on one line from where it dived through the hero's spot to the far end, marching away from it.
            sim.HeroWas(end.Telegraph.Start, out double hx, out double hy);
            ref readonly var b = ref sim.Fight.Bodies[0];
            double sx = wake[0].Shape.X, sy = wake[0].Shape.Y, ux = end.ToX - sx, uy = end.ToY - sy, len = Math.Sqrt(ux * ux + uy * uy);
            foreach (var h in wake) Assert.Less(Math.Abs((h.Shape.X - sx) * uy - (h.Shape.Y - sy) * ux) / len, 1e-6, "on the line");
            Assert.Less(Math.Abs((hx - sx) * uy - (hy - sy) * ux) / len, SandwyrmModule.TunnelRadiusM, "through the hero's spot");
            var c = Centre(sim);
            Assert.LessOrEqual(Math.Sqrt((end.ToX - c.X) * (end.ToX - c.X) + (end.ToY - c.Y) * (end.ToY - c.Y)), SandwyrmModule.InnerM + 1e-6, "inside the basin");
            Assert.IsTrue(m.Under(sim.Now), "under the sand as it runs");
        }

        [Test]
        public void ItsBreachSendsAnArcOfSandAcrossTheBasinThatOnlyADashCrosses()
        {
            var sim = Sim();
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Moves(sim, m, SandwyrmModule.Teach.Length + Array.IndexOf(SandwyrmModule.PhaseOne, SandwyrmModule.Kind.Breach) + 1, each: Wander(sim));
            Assert.AreEqual(SandwyrmModule.Kind.Breach, m.Last);
            var breach = sim.Fight.Hazards.ToArray();
            var dive = breach.Single(h => h.Move == BossMove.Dive);
            var wave = breach.Single(h => h.DashWave);
            Assert.AreEqual(dive.Telegraph.Land, wave.Telegraph.Land, 1e-9, "the wave rolls out as it breaches");
            Assert.AreEqual((dive.ToX, dive.ToY), (wave.Shape.X, wave.Shape.Y));
            Assert.Greater(wave.GrowMps, sim.Fight.HeroSpeedMps * 2, "it outruns any walk");
            Assert.GreaterOrEqual(wave.GrowMps * wave.ActiveS, 2 * SandwyrmModule.BasinM, "across the whole basin");
            // At the rim across the basin from the hero.
            var c = Centre(sim);
            sim.HeroWas(dive.Telegraph.Start, out double hx, out double hy);
            Assert.AreEqual(SandwyrmModule.InnerM, Math.Sqrt((dive.ToX - c.X) * (dive.ToX - c.X) + (dive.ToY - c.Y) * (dive.ToY - c.Y)), 1e-6);
            Assert.Less((dive.ToX - c.X) * (hx - c.X) + (dive.ToY - c.Y) * (hy - c.Y), 0, "across from the hero");
        }

        [Test]
        public void BelowHalfHealthItDivesUntouchableForFourBeatsThenChainsItsHunts()
        {
            var sim = Sim();
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1000, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.StepTo(sim.Now + 10);
            Strike(sim, 510);
            Assert.AreEqual((FightStage.Transition, 2), (sim.Fight.Stage, sim.Fight.Phase));
            double health = sim.Fight.Health;
            Strike(sim, 100);
            Assert.AreEqual(health, sim.Fight.Health, 1e-9, "untouchable through its dive");
            Assert.IsTrue(m.Under(sim.Now + 0.6), "under the sand");
            double from = sim.Now;
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(4 * 0.5, sim.Now - from, 0.1, "four beats at 120 BPM");
            var c = Centre(sim);
            Assert.AreEqual(c.X, sim.Fight.Bodies[0].X, 1e-6, "it rose at the basin's heart");
            Assert.AreEqual(c.Y, sim.Fight.Bodies[0].Y, 1e-6);
            var wander = Wander(sim);
            var moves = Moves(sim, m, 1, each: wander);
            Assert.AreEqual(SandwyrmModule.Kind.Chain, moves[0]);
            // The chain's first hunt is laid already; Laid picks it up on its first step.
            var hunts = Laid(sim, 20, wander, l => l.Count >= SandwyrmModule.ChainHunts);
            Assert.AreEqual(SandwyrmModule.ChainHunts, hunts.Count);
            Assert.That(hunts.All(h => h.Move == BossMove.Dive));
            for (int i = 1; i < hunts.Count; i++)
            {
                Assert.AreEqual(4 * 0.5, hunts[i].Telegraph.Land - hunts[i - 1].Telegraph.Land, 1e-6, "one a bar");
                Assert.GreaterOrEqual(hunts[i].Telegraph.Land - hunts[i].Telegraph.Start, 3 * 0.5 - 1e-6, "each warned three beats");
            }
            var more = Moves(sim, m, 4, each: wander);
            CollectionAssert.AreEqual(SandwyrmModule.PhaseTwo.Skip(1).Take(4).ToArray(), more);
        }

        [Test]
        public void WithoutABeatItFightsOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 400, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            double from = sim.Now;
            var wander = Wander(sim);
            var tells = Laid(sim, 50, () =>
            {
                wander();
                if (sim.Now > from + 40 && sim.Fight.Stage == FightStage.Fighting) Strike(sim, 50);
            });
            double beat = Content.Beat.TelegraphBeatlessBeatS;
            Assert.That(m.Moves, Is.GreaterThanOrEqualTo(5), "it keeps attacking");
            Assert.That(tells.All(h => h.Telegraph.Land - h.Telegraph.Start >= 3 * beat - 1e-6), "three fallback beats of warning");
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage, "and it can be beaten");
        }

        [Test]
        public void TheSameSeedPlaysTheSameFight()
        {
            ulong Run()
            {
                var sim = Sim(128);
                sim.Fight.Start(new SandwyrmModule(), sim.Now, new FightSetup { Health = 600, Act = 2 });
                for (int s = 0; s < 90; s++)
                {
                    sim.StepTo(sim.Now + 1);
                    if (s % 3 == 0 && sim.Fight.InFight && !sim.Fight.Untouchable) Strike(sim, 20);
                    sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s), sim.Fight.CentreY + 8 + 5 * Math.Cos(s * 0.7), sim.Now);
                }
                return sim.Hash ^ (ulong)Drive.Events(sim, SimEventKind.BossTelegraph).Count();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TheCameraHoldsItsBasinWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Basin, sim.Now);
            sim.StepTo(sim.Now + 3);
            var framing = new ArenaFraming(new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 });
            var f = framing.Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(f.Scrolls, "the whole basin in a still view");
            var o = Basin.Outline;
            for (int i = 0; i < o.Count; i++)
                Assert.AreEqual(SandwyrmModule.BasinM, Math.Sqrt(o.X[i] * o.X[i] + o.Y[i] * o.Y[i]), 0.01, "the module's geometry is its arena's");
            Assert.AreEqual((SandwyrmModule.HeroStartX, SandwyrmModule.HeroStartY), (Basin.HeroStartX, Basin.HeroStartY));
            Assert.AreEqual((SandwyrmModule.BossStartX, SandwyrmModule.BossStartY), (Basin.BossStartX, Basin.BossStartY));
            Assert.IsTrue(InBasin(Basin.HeroStartX, Basin.HeroStartY) && InBasin(Basin.BossStartX, Basin.BossStartY));
            // Its middle is open sand for the hunts and tunnels: every prop stands out past where it travels.
            foreach (var p in Basin.Props) Assert.Greater(Math.Sqrt(p.X * p.X + p.Y * p.Y), SandwyrmModule.InnerM - 0.5, $"prop {p.Kind}");
        }

        [Test]
        public void ItsSlotFightsInItsBasinArrivingAtTheBossStart()
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == SandwyrmModule.ModuleId);
            Assert.AreEqual(5, index, "boss 6");
            Assert.IsTrue(rules.Bosses[index].Fight, "landed: its module and cast row");
            Assert.AreEqual(SandwyrmModule.ArenaId, rules.Bosses[index].Arena);
            var config = new RunInputs { Seed = 4, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 600, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double stop = 580;
            while (!(sim.Fight.Module is SandwyrmModule && sim.Fight.Stage == FightStage.Fighting) && sim.Now < stop)
            {
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (RushTestKit.Fighting(sim) && sim.BossRush.Slot.Id != SandwyrmModule.ModuleId) RushTestKit.Strike(sim, 1e12);
                sim.StepTo(sim.Now + 1.0 / 60);
            }
            Assert.IsTrue(sim.Fight.Module is SandwyrmModule, "the run reached the Sandwyrm");
            Assert.AreEqual(SandwyrmModule.ArenaId, sim.Arena.Def.Id);
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual(sim.Arena.BossStartX, b.X, 0.05, "it arrives at its arena's boss start");
            Assert.AreEqual(sim.Arena.BossStartY, b.Y, 0.05);
        }

        [Test]
        public void ARecordedSandwyrmFightReplaysToItsHash()
        {
            var entries = new FixtureSong(6).Beats(0, 40, 128, jitter: 0.003).Gap(40, 46).Beats(46, 90, 150, jitter: 0.002).Entries();
            var run = new RunInputs { Seed = 6, Fight = SandwyrmModule.ModuleId, Start = RunStart.Immediately, SurvivorWeapons = true };
            var live = new Simulation(run.Build(Content));
            var rng = new Random(11);
            double next = 0;
            int fed = Drive.Feed(live, entries, 90, eachFrame: s =>
            {
                if (s.Now < next) return;
                next = s.Now + 1.5;
                s.Target(rng.NextDouble() * 24 - 12, rng.NextDouble() * 24 - 12, s.Now - 0.05);
                if (rng.NextDouble() < 0.3) s.Press(Key.Space, s.Now - 0.05);
            });
            Assert.That(live.Fight.Hazards.Length + Drive.Events(live, SimEventKind.BossMove).Count, Is.GreaterThan(10), "the fight played");
            string dir = Path.Combine(Path.GetTempPath(), "champions-sandwyrm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, entries.GetRange(0, fed));
                var read = SessionRecord.Read(dir);
                var replay = new SessionReplay(read);
                foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
                var sim = new Simulation(read.Run.Build(Content));
                Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16}, recorded {record.Hash:X16}");
                Assert.AreEqual(live.Hash, sim.Hash);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ItsBurstsLieAlongItsLastRunUnderTheSand()
        {
            var sim = Sim();
            var bursts = new List<List<GemSpot>>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add(spots.ToList());
            var m = new SandwyrmModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1000, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var c = Centre(sim);
            sim.Target(c.X + 8, c.Y - 6, sim.Now);
            var hunt = Laid(sim, 10, null, l => l.Count >= 1)[0];
            double fromX = sim.Fight.Bodies[0].X, fromY = sim.Fight.Bodies[0].Y;
            sim.Fight.At(0, hunt.Telegraph.Start, out fromX, out fromY, out _);
            Until(sim, () => sim.Now >= hunt.Telegraph.Land + 0.1);
            Strike(sim, 120);
            sim.StepTo(sim.Now + 1.1);
            Assert.AreEqual(1, bursts.Count);
            Assert.AreEqual(1, bursts[0].Sum(g => g.Share), 1e-9);
            // Every gem lies within a couple of metres of the line it ran.
            double ux = hunt.ToX - fromX, uy = hunt.ToY - fromY, len = Math.Sqrt(ux * ux + uy * uy);
            foreach (var g in bursts[0]) Assert.Less(Math.Abs((g.X - fromX) * uy - (g.Y - fromY) * ux) / len, 2);
        }

        /// <summary>
        /// The Sandwyrm against a reference build at its place in the run (<c>--boss 6</c>), the scripted <c>positioning</c>
        /// player on the 128 BPM dance playlist, the hero unkillable: the fight's length and what the hero took.
        /// </summary>
        public static (double FightS, double Taken, int Hits) ReferenceRun(string build, ulong seed)
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == SandwyrmModule.ModuleId);
            var run = new RunInputs
            {
                Seed = seed, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], Start = RunStart.Immediately,
                RushStart = rules.ReferenceCheckpoint(index, Content.Combat, build),
            };
            var config = run.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            var player = ScriptedPlayer.Parse("positioning", "reference", Content.Dashes);
            var entries = LevelRunTests.Playlist(500, seed).Entries();
            int next = 0, cursor = 0;
            double frontier = 0, from = double.NaN, taken = 0;
            int hits = 0;
            const double frame = 1.0 / 60, lead = 0.05;
            while (frontier < 490)
            {
                frontier += frame;
                while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                sim.StepTo(frontier);
                foreach (var e in sim.EventsSince(cursor))
                {
                    player.Take(sim, e);
                    if (e.Kind == SimEventKind.HeroHit && e.D0 > 0 && !double.IsNaN(from)) { hits++; taken += e.D0; }
                }
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (double.IsNaN(from) && sim.Fight.Module is SandwyrmModule && sim.Fight.Stage == FightStage.Fighting) from = sim.State.RunClock;
                if (!double.IsNaN(from) && sim.Fight.Stage == FightStage.Dead) return (sim.State.RunClock - from, taken, hits);
                player.Frame(sim, frontier - lead);
            }
            Assert.Fail($"{build} seed {seed}: the Sandwyrm still stands (rush {sim.BossRush?.Stage} fight {sim.Fight.Module?.Id} {sim.Fight.Stage} now {sim.Now:F1} share {sim.Fight.HealthShare:F3} taken {taken:F0} hits {hits})");
            return default;
        }

        [Test, Explicit("a minute or two: the reference builds against the Sandwyrm into evidence/boss-rush/sandwyrm/reference-runs.tsv")]
        public void MeasureTheReferenceBuildsAgainstTheSandwyrm()
        {
            var tsv = new StringBuilder("build\tseed\tfightS\ttaken\thits\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                {
                    var (s, taken, hits) = ReferenceRun(build, seed);
                    tsv.Append(string.Join("\t", build, seed.ToString(CultureInfo.InvariantCulture), s.ToString("F1", CultureInfo.InvariantCulture),
                        taken.ToString("F0", CultureInfo.InvariantCulture), hits.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "sandwyrm"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
        }
    }
}
