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
    /// Boss Rush boss 1, the Treasure Goblin (boss-rush.md, The roster, "The Chase"; ps-8z4.6): fair at its tempos in its
    /// grove, the bomb trail taught alone first, its flight round the loop, the cornered ring of bombs as a real dash wave,
    /// its panic, its shedding behind it, the beatless fallback, its grove's content and framing, and its place in the run.
    /// </summary>
    public class TreasureGoblinTests
    {
        const string Goblin = TreasureGoblinModule.ModuleId;
        static GameContent Content => Fairness.Content;

        /// <summary>A sim of the goblin's fight in its grove (as `--fight treasureGoblin` plays it), on a fixed-tempo song (0: none).</summary>
        static Simulation Fight(double bpm = 120, double health = 400, ulong seed = 1)
        {
            var config = new RunInputs { Seed = seed, Loadout = new Upgrade[0], Fight = Goblin }.Build(Content);
            config.Hero.Health = 1e9;
            config.FightHealth = health;
            var sim = new Simulation(config);
            if (bpm > 0) foreach (var k in Drive.Known(new FixtureSong(seed).Beats(0, 400, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            return sim;
        }

        static void Until(Simulation sim, Func<bool> done, double maxS = 60)
        {
            double stop = sim.Now + maxS;
            while (!done() && sim.Now < stop) sim.StepTo(sim.Now + 1.0 / 60);
            Assert.IsTrue(done(), "timed out");
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

        /// <summary>The hero walks at the goblin, a step short of it, for <paramref name="seconds"/>.</summary>
        static void Chase(Simulation sim, double seconds, double shortM = 2)
        {
            double stop = sim.Now + seconds, next = 0;
            while (sim.Now < stop)
            {
                if (sim.Now >= next)
                {
                    ref readonly var b = ref sim.Fight.Bodies[0];
                    sim.HeroWas(sim.Now, out double hx, out double hy);
                    double dx = b.X - hx, dy = b.Y - hy, d = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
                    sim.Target(b.X - dx / d * shortM, b.Y - dy / d * shortM, sim.Now);
                    next = sim.Now + 0.25;
                }
                sim.StepTo(sim.Now + 1.0 / 60);
            }
        }

        static List<Hazard> Laid(Simulation sim, List<Hazard> into, HashSet<int> seen)
        {
            foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) into.Add(h);
            return into;
        }

        static bool Bomb(in Hazard h) => (h.Flags & HazardFlags.Signature) != 0;

        static double[] Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Select(e => e.T).ToArray();

        // ---- Fair (boss-rush.md, Fair fights) ----

        /// <summary>
        /// The grove frames whole (<see cref="TheGroveFramesWholeAndItsLoopClearsEveryProp"/>), so every hazard in it is on
        /// screen: the harness's screen is the arena.
        /// </summary>
        static FairnessOptions Options(double bpm, ulong seed) => new FairnessOptions
        {
            Bpm = bpm, Act = 1, Seed = seed, Arena = TreasureGoblinModule.ArenaId, ScreenRadiusM = 40, FightS = 100, ChaseShare = 0.6,
            Setup = new FightSetup { Health = 400 },
        };

        [TestCase(90, 1UL)]
        [TestCase(120, 2UL)]
        [TestCase(180, 3UL)]
        public void TheGoblinIsFairInItsGrove(double bpm, ulong seed)
        {
            var report = Fairness.Check(() => new TreasureGoblinModule(), Options(bpm, seed));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "its ring of bombs is a real dash wave: " + report);
            Assert.That(report.Hazards.Count(h => Bomb(h)), Is.GreaterThan(20), "its bomb trail");
            Assert.That(report.Hazards.Count(h => (h.Flags & HazardFlags.Projectile) != 0), Is.GreaterThan(0), "its coins");
            Assert.That(report.Hazards.Count(h => h.Move == BossMove.Smash), Is.GreaterThan(0), "its stab");
        }

        // ---- The signature, taught alone ----

        [Test]
        public void TheBombTrailIsTaughtAloneAndPopsBehindItInTimeWithTheBeat()
        {
            var sim = Fight();
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            double stop = sim.Now + 40, next = 0;
            while (sim.Now < stop && laid.Count(h => Bomb(h)) < TreasureGoblinModule.TaughtBombs + 4)
            {
                if (sim.Now >= next) { Chase(sim, 0.25); next = sim.Now; }
                Laid(sim, laid, seen);
            }
            var bombs = laid.Where(h => Bomb(h)).ToList();
            Assert.That(bombs.Count, Is.GreaterThanOrEqualTo(TreasureGoblinModule.TaughtBombs), "chased, it lays its trail");
            sim.StepTo(sim.Now + TreasureGoblinModule.FuseBeats * 0.5 + 0.1);
            int firstOther = laid.FindIndex(h => !Bomb(h));
            Assert.That(firstOther < 0 || laid.Take(firstOther).Count(h => Bomb(h)) >= TreasureGoblinModule.TaughtBombs,
                "nothing but bombs until the trail is taught");
            var beats = Beats(sim);
            double beatS = 60.0 / 120;
            foreach (var h in bombs)
            {
                Assert.AreEqual(TreasureGoblinModule.FuseBeats * beatS, h.Telegraph.Land - h.Telegraph.Start, 0.02, "a four-beat fuse");
                Assert.IsTrue(beats.Any(b => Math.Abs(b - h.Telegraph.Land) < 0.002), "it pops on its beat");
                Assert.AreEqual(ShapeKind.Circle, h.Shape.Kind);
                Assert.AreEqual(TreasureGoblinModule.BombRadiusM, h.Shape.Radius, 1e-9);
            }
            var gaps = bombs.Zip(bombs.Skip(1), (a, b) => b.Telegraph.Land - a.Telegraph.Land).Where(g => g < 4 * beatS).ToList();
            Assert.That(gaps, Has.All.EqualTo(2 * beatS).Within(0.02), "dropped every other beat while it runs");
        }

        // ---- The chase ----

        [Test]
        public void ItRunsItsLoopAwayFromTheHeroClearOfThePropsAndReversesWhenCutOff()
        {
            var sim = Fight();
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var arena = sim.Arena;
            Assert.AreEqual(TreasureGoblinModule.ArenaId, arena.Def.Id, "`--fight` forms its grove");
            double radius = sim.Fight.TypeOf(0).RadiusM;
            double minFled = double.MaxValue;
            var dirs = new List<double>();
            for (int k = 0; k < 60 * 20; k++)
            {
                if (k % 15 == 0)
                {
                    // The hero walks at it, then (from 10 s) cuts across the copse to meet it head-on.
                    ref readonly var b0 = ref sim.Fight.Bodies[0];
                    if (k < 600) sim.Target(b0.X, b0.Y, sim.Now);
                    else sim.Target(arena.OriginX - (b0.X - arena.OriginX), arena.OriginY - (b0.Y - arena.OriginY), sim.Now);
                }
                double was = Angle(sim);
                sim.StepTo(sim.Now + 1.0 / 60);
                ref readonly var b = ref sim.Fight.Bodies[0];
                Assert.IsTrue(arena.Walkable(b.X, b.Y, radius), $"its body stands clear at {sim.Now:F2}");
                double turn = Math.IEEERemainder(Angle(sim) - was, 2 * Math.PI);
                if (Math.Abs(turn) > 1e-6) dirs.Add(Math.Sign(turn));
                sim.HeroWas(sim.Now, out double hx, out double hy);
                if (b.Walking) minFled = Math.Min(minFled, Math.Sqrt((hx - b.X) * (hx - b.X) + (hy - b.Y) * (hy - b.Y)));
            }
            Assert.That(dirs.Count, Is.GreaterThan(100), "it ran");
            Assert.That(dirs.Zip(dirs.Skip(1), (a, b) => a != b).Count(c => c), Is.GreaterThan(0), "cut off, it turns back");
            Assert.That(minFled, Is.LessThan(TreasureGoblinModule.CorneredM), "the hero catches it: it runs slower than the walk");
        }

        static double Angle(Simulation sim)
        {
            ref readonly var b = ref sim.Fight.Bodies[0];
            return Math.Atan2(b.Y - sim.Arena.OriginY, b.X - sim.Arena.OriginX);
        }

        [Test]
        public void LeftAloneItComesBackToTauntAndSprayCoinsInArcsWithWalkingGaps()
        {
            var sim = Fight();
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            // The hero stays by its start, at the grove's south edge.
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            Until(sim, () => Laid(sim, laid, seen).Any(h => (h.Flags & HazardFlags.Projectile) != 0), 60);
            var coins = laid.Where(h => (h.Flags & HazardFlags.Projectile) != 0).ToList();
            var first = coins.Where(h => h.Attack == coins[0].Attack && h.Telegraph.Land == coins[0].Telegraph.Land).ToList();
            double launched = sim.Now;
            sim.StepTo(first[0].Telegraph.Land + 0.1);
            Assert.AreEqual(TreasureGoblinModule.SprayCoins, first.Count);
            sim.HeroWas(launched, out double hx, out double hy);
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.That(Math.Sqrt((hx - b.X) * (hx - b.X) + (hy - b.Y) * (hy - b.Y)), Is.LessThan(TreasureGoblinModule.SprayM), "it came to taunt");
            // Neighbouring coins a body and more apart where they reach the hero: lanes to walk through.
            var angles = first.Select(h => Math.Atan2(h.Vy, h.Vx)).ToList();
            double step = angles[1] - angles[0], reachM = TreasureGoblinModule.FleeM;
            Assert.That(reachM * step - 2 * TreasureGoblinModule.CoinRadiusM, Is.GreaterThan(2 * Content.Hero.RadiusM + 0.5));
            Assert.IsTrue(Beats(sim).Any(t => Math.Abs(t - first[0].Telegraph.Land) < 0.002), "launched on the beat");
        }

        [Test]
        public void CorneredItRingsItselfWithBombsAndBlowsThemOnADownbeatAsADashWave()
        {
            var sim = Fight();
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            double stop = sim.Now + 90;
            while (sim.Now < stop && !laid.Any(h => h.DashWave))
            {
                Chase(sim, 0.25, shortM: 1);
                Laid(sim, laid, seen);
            }
            var wave = laid.First(h => h.DashWave);
            sim.StepTo(wave.Telegraph.Land + 0.1);
            Laid(sim, laid, seen);
            var ring = laid.Where(h => h.Attack == wave.Attack).ToList();
            Assert.AreEqual(1 + TreasureGoblinModule.RingBombs, ring.Count, "one front and its ring of bombs");
            Assert.That(ring.Select(h => h.Telegraph.Land).Distinct().Count(), Is.EqualTo(1), "they go off together");
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => (e.I2 & 1) != 0).Select(e => e.T);
            Assert.IsTrue(downbeats.Any(t => Math.Abs(t - wave.Telegraph.Land) < 0.002), "on a downbeat");
            Assert.That(TreasureGoblinModule.WaveSpeedMps, Is.GreaterThan(Content.Hero.SpeedMps * 2), "walking can't outrun it");
            Assert.That(TreasureGoblinModule.WaveRadiusM, Is.GreaterThanOrEqualTo(2 * 18 - TreasureGoblinModule.TrackInM), "nor get past it: it sweeps the grove");
        }

        // ---- Panic ----

        [Test]
        public void BelowFortyPercentItPanicsUntouchableForTwoBeatsThenDropsABombEveryBeat()
        {
            var sim = Fight(health: 1000);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Chase(sim, 20);
            Strike(sim, 610);
            Assert.AreEqual(FightStage.Transition, sim.Fight.Stage);
            double health = sim.Fight.Health;
            Strike(sim, 100);
            Assert.AreEqual(health, sim.Fight.Health, 1e-9, "untouchable in its cackle");
            var phase = Drive.Events(sim, SimEventKind.BossPhase).Single();
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 5);
            Assert.AreEqual(2 * 0.5, sim.Now - phase.T, 0.05, "two beats");
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            double stop = sim.Now + 30;
            while (sim.Now < stop && laid.Count(h => Bomb(h)) < 12)
            {
                Chase(sim, 0.25, shortM: 7);
                Laid(sim, laid, seen);
            }
            var lands = laid.Where(h => Bomb(h)).Select(h => h.Telegraph.Land).ToList();
            Assert.That(lands.Zip(lands.Skip(1), (a, b) => b - a), Has.Some.EqualTo(0.5).Within(0.02), "a bomb every beat");
        }

        // ---- Shedding ----

        [Test]
        public void ItsBurstsSpillBehindItAlongItsTrail()
        {
            var sim = Fight(health: 1000);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Chase(sim, 6, shortM: 6);
            var bursts = new List<(double Facing, double X, double Y, GemSpot[] Spots)>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add((f.Lead.Facing, f.Lead.X, f.Lead.Y, spots.ToArray()));
            Strike(sim, 120);
            sim.StepTo(sim.Now + 0.6);
            Assert.AreEqual(1, bursts.Count);
            var (facing, x, y, gems) = bursts[0];
            Assert.AreEqual(1, gems.Sum(g => g.Share), 1e-9);
            foreach (var g in gems)
            {
                double ahead = (g.X - x) * Math.Cos(facing) + (g.Y - y) * Math.Sin(facing);
                Assert.That(ahead, Is.LessThan(0), "behind it");
            }
        }

        // ---- Silence, determinism ----

        [Test]
        public void WithNoBeatItFightsOnTheHalfSecondFallback()
        {
            var sim = Fight(bpm: 0);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 20);
            var laid = new List<Hazard>();
            var seen = new HashSet<int>();
            double stop = sim.Now + 20;
            while (sim.Now < stop && laid.Count(h => Bomb(h)) < 3)
            {
                Chase(sim, 0.25);
                Laid(sim, laid, seen);
            }
            var bombs = laid.Where(h => Bomb(h)).ToList();
            Assert.That(bombs.Count, Is.GreaterThanOrEqualTo(3), "its trail without a beat");
            double fallback = Content.Beat.TelegraphBeatlessBeatS;
            foreach (var h in bombs)
                Assert.AreEqual(TreasureGoblinModule.FuseBeats * fallback, h.Telegraph.Land - h.Telegraph.Start, 0.02, "fused by the fallback beat");
            Strike(sim, 1e9);
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage);
        }

        [Test]
        public void TheSameRunPlaysTheSameFight()
        {
            ulong Play()
            {
                var sim = Fight(seed: 5);
                Until(sim, () => sim.Fight.InFight);
                Chase(sim, 40, shortM: 1.5);
                return sim.Hash;
            }
            Assert.AreEqual(Play(), Play());
        }

        // ---- The grove ----

        [Test]
        public void TheGroveFramesWholeAndItsLoopClearsEveryProp()
        {
            var def = Content.Arenas.Find(TreasureGoblinModule.ArenaId);
            Assert.IsNotNull(def);
            Assert.AreEqual((TreasureGoblinModule.HeroStartX, TreasureGoblinModule.HeroStartY), (def.HeroStartX, def.HeroStartY), "the module's starts are its arena's");
            Assert.AreEqual((TreasureGoblinModule.BossStartX, TreasureGoblinModule.BossStartY), (def.BossStartX, def.BossStartY));
            TreasureGoblinModule.TrackPoint(0, out double x0, out double y0);
            Assert.AreEqual(def.BossStartX, x0, 1e-9, "it arrives on its loop");
            Assert.AreEqual(def.BossStartY, y0, 1e-9);
            var sim = Fight();
            var arena = sim.Arena;
            sim.StepTo(sim.Now + 3);
            Assert.AreEqual(ArenaPhase.Standing, arena.Phase);
            double clear = Content.Enemies.Cast[Content.Enemies.IndexOf(Goblin)].RadiusM + 0.3;
            for (int k = 0; k < TreasureGoblinModule.TrackPoints; k++)
            {
                TreasureGoblinModule.TrackPoint(k, out double ax, out double ay);
                TreasureGoblinModule.TrackPoint((k + 1) % TreasureGoblinModule.TrackPoints, out double bx, out double by);
                for (double u = 0; u <= 1; u += 0.05)
                    Assert.IsTrue(arena.Walkable(arena.OriginX + ax + (bx - ax) * u, arena.OriginY + ay + (by - ay) * u, clear), $"the loop clear between points {k} and {k + 1}");
            }
            Assert.IsFalse(arena.Walkable(arena.OriginX, arena.OriginY, 0.4), "the copse is walled");
            var framing = new ArenaFraming(new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 });
            var f = framing.Frame(arena, arena.HeroStartX, arena.HeroStartY, new List<(double, double, double)>());
            Assert.IsFalse(f.Scrolls, "the whole grove in view: every hazard in it is on screen");
            var o = def.Outline;
            for (int i = 0; i < o.Count; i++)
                Assert.IsTrue(framing.InFrame(f.FocusX, f.FocusY, f.Distance, arena.OriginX + o.X[i], arena.OriginY + o.Y[i]), $"wall vertex {i} in frame");
        }

        // ---- Evidence: the reference builds ----

        /// <summary>One reference run of the goblin's fight (the run's first boss, the reference player).</summary>
        public sealed class ReferenceRun
        {
            public string Build;
            public ulong Seed;
            public bool Killed, Died;
            public double FightS, Taken;
            public int Hits, Dodged, Bursts, LevelsGained;
        }

        /// <summary>
        /// The run's first boss with reference build <paramref name="build"/> at its slot (`--boss 1`), played by the
        /// reference player (`--walk positioning`: it walks at the boss round the props and dashes each landing and wave) to the
        /// 128 BPM dance playlist, as the progression measures it, but with the slot's real health (and the hero's, unless
        /// <paramref name="immortal"/>: the fight's length whatever the player's dodging).
        /// </summary>
        public static ReferenceRun PlayReference(string build, ulong seed, bool immortal = false, double horizonS = 400)
        {
            var rules = Content.BossRush;
            var run = new RunInputs { Seed = seed, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0],
                RushStart = rules.ReferenceCheckpoint(0, Content.Combat, build), Start = RunStart.Immediately };
            var config = run.Build(Content);
            if (immortal) config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            var player = ScriptedPlayer.Parse("positioning", "reference", Content.Dashes);
            var entries = LevelRunTests.Playlist(horizonS + 60, seed).Entries();
            var result = new ReferenceRun { Build = build, Seed = seed };
            int next = 0, cursor = 0, level0 = sim.State.Level;
            double frontier = 0, from = double.NaN;
            const double frame = 1.0 / 60, lead = 0.05;
            while (sim.State.Phase != RunPhase.Ended && frontier < horizonS)
            {
                frontier += frame;
                while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                sim.StepTo(frontier);
                foreach (var e in sim.EventsSince(cursor))
                {
                    player.Take(sim, e);
                    if (e.Kind == SimEventKind.HeroHit && !double.IsNaN(from))
                    {
                        var outcome = (HitOutcome)e.I0;
                        if (outcome == HitOutcome.Hurt || outcome == HitOutcome.Killed)
                        {
                            result.Hits++; result.Taken += e.D0;
                        }
                        else if (outcome == HitOutcome.Dodged || outcome == HitOutcome.PerfectDodge) result.Dodged++;
                    }
                }
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                if (double.IsNaN(from) && sim.Fight.InFight) from = sim.State.RunClock;
                if (sim.BossRush.Killed > 0)
                {
                    result.Killed = true;
                    break;
                }
                player.Frame(sim, frontier - lead);
            }
            result.Died = sim.State.End == RunEnd.Death;
            result.FightS = double.IsNaN(from) ? 0 : sim.State.RunClock - from;
            result.Bursts = sim.BossRush.Bursts;
            result.LevelsGained = sim.State.Level - level0;
            return result;
        }

        [Test, Explicit("about a minute: the reference builds' runs into evidence/boss-rush/treasureGoblin/reference-runs.tsv")]
        public void RecordTheReferenceBuildsRuns()
        {
            var tsv = new StringBuilder("build\tseed\thero\tkilled\tdied\tfightS\thits\ttaken\tdodged\tbursts\tlevels\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                foreach (bool immortal in new[] { false, true })
                {
                    var r = PlayReference(build, seed, immortal);
                    tsv.Append(string.Join("\t", r.Build, r.Seed.ToString(CultureInfo.InvariantCulture), immortal ? "immortal" : "100",
                        r.Killed ? "yes" : "no", r.Died ? "yes" : "no",
                        r.FightS.ToString("F1", CultureInfo.InvariantCulture), r.Hits.ToString(CultureInfo.InvariantCulture),
                        r.Taken.ToString("F0", CultureInfo.InvariantCulture), r.Dodged.ToString(CultureInfo.InvariantCulture),
                        r.Bursts.ToString(CultureInfo.InvariantCulture), r.LevelsGained.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", Goblin));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
            TestContext.Out.WriteLine($"sustained damage: focused {BossRushProgressionTests.SustainedDamage(0, "focused"):F2}, broad {BossRushProgressionTests.SustainedDamage(0, "broad"):F2}");
        }

        [Test]
        public void TheReferenceBuildKillsItInAboutTwoMinutesSheddingEveryBurst()
        {
            // The slot's health is its target time at the reference builds' measured damage against this fight (the
            // progression's table): chased round its grove by the reference player, it lasts about that long.
            var r = PlayReference("focused", 31, immortal: true);
            Assert.IsTrue(r.Killed, $"killed after {r.FightS:F0} s");
            Assert.That(r.FightS, Is.InRange(90.0, 150.0), "about two minutes (act 1)");
            Assert.AreEqual(9, r.Bursts, "it shed all nine bursts on the way");
            Assert.That(r.LevelsGained, Is.InRange(2, 3), "about two level-ups a boss");
        }

        // ---- In the run ----

        [Test]
        public void TheRunsFirstBossIsTheGoblinsOwnFightInItsGrove()
        {
            var config = new RunInputs { Seed = 3, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var slot = config.BossRush.Bosses[0];
            Assert.AreEqual((Goblin, true, TreasureGoblinModule.ArenaId), (slot.Id, slot.Fight, slot.Arena));
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 200, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            Until(sim, () => sim.Fight.InFight, 30);
            Assert.AreEqual(Goblin, sim.Fight.Module.Id);
            Assert.AreEqual(BossStage.None, sim.Bosses.Stage, "no stand-in");
            Assert.AreEqual(TreasureGoblinModule.ArenaId, sim.Arena.Def.Id);
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual(sim.Arena.BossStartX, b.X, 0.05, "it arrives at its arena's boss start");
            Assert.AreEqual(sim.Arena.BossStartY, b.Y, 0.05);
            Assert.AreEqual(slot.Health, sim.Fight.MaxHealth, 1e-9, "the slot's health");
        }
    }
}
