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
    /// Boss Rush boss 7, the Bandaged King (boss-rush.md, The roster: "Sandstorm"; ps-8z4.19): its fairness at its slowest
    /// and fastest tempos, the storm cycle (the first storm's lashes taught alone, later storms rolling in behind a sand
    /// front, the calm's whip combos and reaching lashes), the storm that closes the client's sight, its phase, its XP, the
    /// beatless fallback, its replay and its slot in the run.
    /// </summary>
    public class BandagedKingTests
    {
        const double Bpm = 120, BeatS = 60 / Bpm;
        static GameContent Content => Fairness.Content;

        static FairnessOptions Options(double bpm, ulong seed = 1) => new FairnessOptions
        {
            Bpm = bpm, Act = 2, Seed = seed, FightS = 135, Arena = BandagedKingModule.ArenaId, ChaseShare = 0.5,
            // The court is held whole by a fixed camera (TheCameraHoldsTheCourtWhole): all of it is on screen.
            ScreenRadiusM = 40,
        };

        static Simulation Sim(double songS = 300, double bpm = Bpm, bool beats = true)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            if (beats)
                foreach (var k in Drive.Known(new FixtureSong().Beats(0, songS, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
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

        /// <summary>Plays the fight for <paramref name="seconds"/>, recording every hazard it lays, in order.</summary>
        static List<Hazard> Play(Simulation sim, double seconds, Action<Simulation> eachFrame = null)
        {
            var seen = new HashSet<int>();
            var laid = new List<Hazard>();
            double stop = sim.Now + seconds;
            while (sim.Now < stop && sim.Fight.Stage != FightStage.Dead)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                eachFrame?.Invoke(sim);
                foreach (var h in sim.Fight.Hazards)
                    if (seen.Add(h.Id)) laid.Add(h);
            }
            return laid;
        }

        static double[] Downbeats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Where(e => e.I2 != 0).Select(e => e.T).ToArray();
        static double[] Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Select(e => e.T).ToArray();
        static bool On(double[] times, double t) => times.Any(d => Math.Abs(d - t) < 1e-6);

        /// <summary>The hero circling the court's centre, 6 m out.</summary>
        static Action<Simulation> Circling(BandagedKingModule m) =>
            s => s.Target(m.OriginX + 6 * Math.Cos(s.Now * 0.6), m.OriginY + 6 * Math.Sin(s.Now * 0.6), s.Now);

        /// <summary>The hero by turns close to the King (combos) and across the court (reaching lashes).</summary>
        static Action<Simulation> NearAndFar(BandagedKingModule m) => s =>
        {
            ref readonly var b = ref s.Fight.Bodies[0];
            bool close = ((int)(s.Now / 5)) % 2 == 0;
            if (close) s.Target(b.X + 2.5, b.Y, s.Now);
            else s.Target(m.OriginX - 0.7 * (b.X - m.OriginX), m.OriginY - 0.7 * (b.Y - m.OriginY) - 2, s.Now);
        };

        /// <summary>Whether (x, y) lies within the court's outline (grown by <paramref name="slackM"/>).</summary>
        static bool InCourt(BandagedKingModule m, double x, double y, double slackM)
        {
            var c = BandagedKingModule.Court;
            for (int i = 0; i < c.Length; i++)
            {
                var (ax, ay) = c[i];
                var (bx, by) = c[(i + 1) % c.Length];
                double nx = by - ay, ny = ax - bx, n = Math.Sqrt(nx * nx + ny * ny);
                if ((nx * (x - m.OriginX - ax) + ny * (y - m.OriginY - ay)) / n > slackM) return false;
            }
            return true;
        }

        static bool Front(in Hazard h) => h.Move == BossMove.Stomp;
        static bool Lash(in Hazard h) => h.Move == BossMove.Smash || h.Move == BossMove.Boulders;

        // ---- Fairness ----

        [TestCase(90)]
        [TestCase(180)]
        public void TheBandagedKingIsFair(double bpm)
        {
            var report = Fairness.Check(() => new BandagedKingModule(), Options(bpm));
            TestContext.Out.WriteLine(report);
            TestContext.Out.WriteLine(string.Join(" ", report.Hazards.GroupBy(h => h.Attack).Select(g => $"{g.First().Move}@{g.Min(h => h.Telegraph.Land):F1}")));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "a real dash wave: " + report);
            Assert.That(report.Hazards.Select(h => h.Move).Distinct(),
                Is.SupersetOf(new[] { BossMove.Stomp, BossMove.Smash, BossMove.Fans, BossMove.Boulders }),
                "the front, the lashes, the combo and the crossed lash all played");
        }

        [Test]
        public void TheBandagedKingIsFairOnOtherSeeds([Values(2UL, 3UL)] ulong seed)
        {
            Fairness.AssertFair(Fairness.Check(() => new BandagedKingModule(), Options(128, seed)));
        }

        // ---- The storm cycle ----

        [Test]
        public void TheFirstStormTeachesItsLashesAloneOnEveryOtherBeat()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = Play(sim, 24 * BeatS, Circling(king));
            var storm = laid.Where(h => h.Telegraph.Land < laid[0].Telegraph.Land + (BandagedKingModule.StormBeats[0] - BandagedKingModule.FirstLashBeat) * BeatS).ToList();
            Assert.That(storm.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(storm.Select(h => h.Move), Is.All.EqualTo(BossMove.Smash), "lashes and nothing else: no front, no combo");
            var downbeats = Downbeats(sim);
            double cycle0 = laid[0].Telegraph.Land - BandagedKingModule.FirstLashBeat * BeatS;
            Assert.IsTrue(On(downbeats, cycle0), "the storm's cycle starts on a downbeat");
            for (int k = 0; k < storm.Count; k++)
            {
                Assert.AreEqual(cycle0 + (BandagedKingModule.FirstLashBeat + k * BandagedKingModule.LashEveryBeats) * BeatS, storm[k].Telegraph.Land, 1e-6, $"lash {k} every other beat");
                Assert.That(storm[k].Telegraph.Land - storm[k].Telegraph.Start, Is.GreaterThanOrEqualTo(3 * BeatS - 1e-6), "three beats' warning");
                Assert.That(storm[k].Flags & HazardFlags.Signature, Is.EqualTo(HazardFlags.None), "a whip crack, not the storm's swell");
                // A long lash reaching across the court, to its wall: its end on the outline, or its whole length short of it.
                var s = storm[k].Shape;
                double len = Math.Sqrt((s.X2 - s.X) * (s.X2 - s.X) + (s.Y2 - s.Y) * (s.Y2 - s.Y)) - Content.Enemies.Cast[Content.Enemies.IndexOf(BandagedKingModule.ModuleId)].RadiusM * 0.5;
                Assert.That(len, Is.GreaterThan(8).And.LessThanOrEqualTo(BandagedKingModule.LashLengthM + 1e-6), "a long lash");
                Assert.IsTrue(InCourt(king, s.X2, s.Y2, 0.05), "it stops at the court's wall");
            }
            Assert.That(laid.Where(h => h.DashWave), Is.Empty, "the first storm drifts in with no front");
        }

        [Test]
        public void LaterStormsRollInBehindAFrontAndTheCalmSwipesAndReaches()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = Play(sim, 100 * BeatS, NearAndFar(king));
            var fronts = laid.Where(h => Front(h)).ToList();
            Assert.That(fronts.Count, Is.GreaterThanOrEqualTo(2));
            var downbeats = Downbeats(sim);
            Assert.That(fronts.Select(h => h.Telegraph.Land).Where(t => !On(downbeats, t)), Is.Empty, "fronts on downbeats");
            for (int k = 1; k < fronts.Count; k++)
                Assert.AreEqual(BandagedKingModule.CycleBeats[0] * BeatS, fronts[k].Telegraph.Land - fronts[k - 1].Telegraph.Land, 1e-6, "a storm every eight bars");
            Assert.That(fronts, Has.All.Matches<Hazard>(h => h.DashWave && (h.Flags & HazardFlags.Signature) != 0), "each a dash wave, the bank's swell");
            Assert.That(fronts, Has.All.Matches<Hazard>(h => Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy) > 3 * Content.Hero.SpeedMps), "its front outruns any walk");
            var combos = laid.Where(h => h.Move == BossMove.Fans).GroupBy(h => h.Attack).ToList();
            Assert.That(combos, Is.Not.Empty, "the whip combo in the calm");
            var beats = Beats(sim);
            foreach (var combo in combos)
            {
                var hits = combo.OrderBy(h => h.Part).ToList();
                Assert.AreEqual(3, hits.Count, "three lashes");
                Assert.IsTrue(On(beats, hits[0].Telegraph.Land));
                Assert.AreEqual(BeatS / 2, hits[1].Telegraph.Land - hits[0].Telegraph.Land, 1e-6, "the second on the half beat");
                Assert.AreEqual(BeatS, hits[2].Telegraph.Land - hits[0].Telegraph.Land, 1e-6, "the third on the next beat");
            }
            // Calm moves land only in the calm: after the storm's last lash, before the next storm's withdrawal.
            foreach (var h in laid.Where(h => h.Move == BossMove.Fans))
            {
                double since = fronts.Select(f => f.Telegraph.Land).Where(t => t <= h.Telegraph.Land + 1e-6).DefaultIfEmpty(fronts[0].Telegraph.Land - 32 * BeatS).Max();
                double c = (h.Telegraph.Land - since) / BeatS;
                Assert.That(c, Is.GreaterThanOrEqualTo(BandagedKingModule.StormBeats[0] - 1e-6), "in the calm");
                Assert.That(c, Is.LessThan(BandagedKingModule.CycleBeats[0] - BandagedKingModule.LeadBeats), "done before the next storm");
            }
            Assert.That(laid.Count(h => h.Move == BossMove.Smash), Is.GreaterThan(fronts.Count * 6), "the storms' lashes");
        }

        [Test]
        public void TheStormRisesOnItsCycleHoldsThroughItsBarsAndSettles()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(0, king.Storm(sim.Fight, sim.Now), 1e-9, "still air at his arrival");
            Until(sim, () => sim.Fight.Hazards.Length > 0, 20);
            double cycle0 = sim.Fight.Hazards[0].Telegraph.Land - BandagedKingModule.FirstLashBeat * BeatS;
            int storm = BandagedKingModule.StormBeats[0];
            void At(double beats) => Until(sim, () => sim.Now >= cycle0 + beats * BeatS + 0.01);
            At(BandagedKingModule.RiseBeats / 2);
            Assert.AreEqual(1, king.Storm(sim.Fight, sim.Now), 1e-9, "full a beat after cycle beat 0");
            At(storm - 1);
            Assert.AreEqual(1, king.Storm(sim.Fight, sim.Now), 1e-9, "full through its bars");
            At(storm + BandagedKingModule.SettleBeats / 2);
            Assert.That(king.Storm(sim.Fight, sim.Now), Is.InRange(0.4, 0.6), "settling");
            At(storm + BandagedKingModule.SettleBeats + 1);
            Assert.AreEqual(0, king.Storm(sim.Fight, sim.Now), 1e-9, "still air in the calm");
            At(BandagedKingModule.CycleBeats[0]);
            Assert.That(king.Storm(sim.Fight, sim.Now), Is.InRange(0.45, 0.6), "rising again on the next cycle's downbeat");
        }

        [Test]
        public void HeWithdrawsIntoTheDarkAsTheStormRises()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            // The hero comes up to him in the calm and holds its ground once a storm is coming; the storm sends him back.
            double holdX = 0, holdY = 0;
            bool holding = false;
            var seen = new HashSet<int>();
            var firstLashGaps = new List<double>();
            bool afterFront = false;
            Play(sim, 70 * BeatS, s =>
            {
                ref readonly var b = ref s.Fight.Bodies[0];
                bool coming = s.Fight.Moving(0) || king.Storm(s.Fight, s.Now) > 0;
                if (coming && !holding) s.HeroWas(s.Now, out holdX, out holdY);
                holding = coming;
                if (holding) s.Target(holdX, holdY, s.Now);
                else s.Target(b.X, b.Y - 3, s.Now);
                foreach (var h in s.Fight.Hazards)
                {
                    if (!seen.Add(h.Id)) continue;
                    if (Front(h)) afterFront = true;
                    else if (afterFront && h.Move == BossMove.Smash)
                    {
                        // Its line starts at his body: how far that is from the hero as it is planned.
                        s.HeroWas(s.Now, out double hx, out double hy);
                        firstLashGaps.Add(Math.Sqrt((h.Shape.X - hx) * (h.Shape.X - hx) + (h.Shape.Y - hy) * (h.Shape.Y - hy)));
                        afterFront = false;
                    }
                }
            });
            Assert.That(firstLashGaps, Is.Not.Empty);
            Assert.That(firstLashGaps, Is.All.GreaterThan(BandagedKingModule.SightM + 1), "each storm's first lash comes from the dark, beyond the sight");
        }

        // ---- Its phase ----

        [Test]
        public void BelowHalfTheStormHoldsLongerWithTwoFrontsAndCrossedLashes()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Play(sim, 10 * BeatS, Circling(king));
            double before = king.Storm(sim.Fight, sim.Now);
            Strike(sim, 520);
            Assert.AreEqual(2, sim.Fight.Phase);
            Assert.IsTrue(sim.Fight.Untouchable, "four untouchable beats");
            double phase = Drive.Events(sim, SimEventKind.BossPhase).Single().T;
            var laid = Play(sim, 40 * BeatS, Circling(king));
            Assert.That(before, Is.GreaterThan(0.9), "it woke in a storm");
            Assert.That(laid.First().Telegraph.Start, Is.GreaterThanOrEqualTo(phase + 4 * BeatS - 1e-6), "nothing through the transition");
            Assert.IsTrue(Front(laid.First()), "a fresh storm from its front");
            double cycle0 = laid.First().Telegraph.Land;
            var storm = laid.Where(h => h.Telegraph.Land < cycle0 + BandagedKingModule.StormBeats[1] * BeatS - 1e-6).ToList();
            var fronts = storm.Where(h => Front(h)).ToList();
            Assert.AreEqual(2, fronts.Count, "two fronts a storm");
            Assert.AreEqual(BandagedKingModule.SecondFrontBeat * BeatS, fronts[1].Telegraph.Land - fronts[0].Telegraph.Land, 1e-6);
            var crosses = storm.Where(h => h.Move == BossMove.Boulders).GroupBy(h => h.Attack).ToList();
            Assert.That(crosses, Is.Not.Empty, "crossed lashes");
            foreach (var cross in crosses)
            {
                var pair = cross.OrderBy(h => h.Part).ToList();
                Assert.AreEqual(2, pair.Count);
                Assert.AreEqual(BeatS / 2, pair[1].Telegraph.Land - pair[0].Telegraph.Land, 1e-6, "the second on the half beat");
            }
            Assert.That(storm.Last().Telegraph.Land - cycle0, Is.GreaterThan(BandagedKingModule.StormBeats[0] * BeatS), "the storm holds three bars");
        }

        [Test]
        public void ItsTransitionClearsTheStormOverTwoBeats()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Play(sim, 10 * BeatS, Circling(king));
            Strike(sim, 520);
            double t = sim.Now;
            Assert.That(king.Storm(sim.Fight, t), Is.GreaterThan(0.9));
            Assert.That(king.Storm(sim.Fight, t + BeatS), Is.InRange(0.4, 0.6));
            Assert.AreEqual(0, king.Storm(sim.Fight, t + 2 * BeatS + 0.01), 1e-9);
        }

        // ---- Its XP ----

        [Test]
        public void ItsBurstsUnravelInALineBehindHim()
        {
            var sim = Sim();
            var king = new BandagedKingModule();
            var bursts = new List<IReadOnlyList<GemSpot>>();
            sim.Fight.Shed += (_, _, _, spots) => bursts.Add(spots.ToList());
            sim.Fight.Start(king, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.Target(king.OriginX, king.OriginY - 9, sim.Now);
            Strike(sim, 120);
            Until(sim, () => bursts.Count > 0, 10);
            double bx = sim.Fight.Bodies[0].X, by = sim.Fight.Bodies[0].Y;
            sim.HeroWas(sim.Now, out double hx, out double hy);
            double ax = bx - hx, ay = by - hy, d = Math.Sqrt(ax * ax + ay * ay);
            Assert.That(bursts[0].Count, Is.EqualTo(7));
            Assert.AreEqual(1, bursts[0].Sum(s => s.Share), 1e-9);
            Assert.That(bursts[0], Has.All.Matches<GemSpot>(s => ((s.X - bx) * ax + (s.Y - by) * ay) / d > 1.5), "behind him, away from the hero");
            Assert.That(bursts[0], Has.All.Matches<GemSpot>(s => Math.Abs(((s.X - bx) * ay - (s.Y - by) * ax) / d) < 1.5), "in a line");
        }

        // ---- Silence, determinism ----

        [Test]
        public void WithoutABeatItPlaysOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var king = new BandagedKingModule();
            sim.Fight.Start(king, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            var laid = Play(sim, 60, NearAndFar(king));
            Assert.That(laid.Select(h => h.Move).Distinct(), Is.SupersetOf(new[] { BossMove.Smash, BossMove.Stomp, BossMove.Fans }),
                "its cycle turns: " + string.Join(",", laid.Select(h => h.Move).Distinct()));
            double beat = sim.Beats.Rules.TelegraphBeatlessBeatS;
            Assert.That(laid.Select(h => h.Telegraph.Land - h.Telegraph.Start), Is.All.GreaterThanOrEqualTo(3 * beat - 1e-6), "three fallback beats' warning");
        }

        [Test]
        public void ARecordedBandagedKingFightReplaysToItsHash()
        {
            var entries = new FixtureSong(5).Beats(0, 40, 128, jitter: 0.003).Gap(40, 46).Beats(46, 90, 150, jitter: 0.002).Entries();
            var run = new RunInputs { Seed = 5, Fight = BandagedKingModule.ModuleId, Start = RunStart.Immediately, SurvivorWeapons = true };
            var live = new Simulation(run.Build(Content));
            var rng = new Random(9);
            double next = 0;
            int fed = Drive.Feed(live, entries, 90, eachFrame: s =>
            {
                if (s.Now < next) return;
                next = s.Now + 1.5;
                s.Target(rng.NextDouble() * 24 - 12, rng.NextDouble() * 20 - 10, s.Now - 0.05);
                if (rng.NextDouble() < 0.3) s.Press(Key.Space, s.Now - 0.05);
            });
            Assert.That(live.Fight.Hazards.Length + Drive.Events(live, SimEventKind.BossMove).Count, Is.GreaterThan(10), "the fight played");
            string dir = Path.Combine(Path.GetTempPath(), "champions-bandagedking-" + Guid.NewGuid().ToString("N"));
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

        // ---- In the run ----

        [Test]
        public void ItsSlotFightsInItsTombArrivingAtTheBossStart()
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == BandagedKingModule.ModuleId);
            Assert.IsTrue(rules.Bosses[index].Fight, "landed: its module and cast row");
            Assert.AreEqual(BandagedKingModule.ArenaId, rules.Bosses[index].Arena);
            var config = new RunInputs { Seed = 4, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], RushStart = rules.ReferenceCheckpoint(index, Content.Combat) }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 200, Bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double stop = 120;
            while (!(sim.Fight.Module is BandagedKingModule && sim.Fight.Stage == FightStage.Fighting) && sim.Now < stop)
            {
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                sim.StepTo(sim.Now + 1.0 / 60);
            }
            Assert.IsTrue(sim.Fight.Module is BandagedKingModule, "the run reached the Bandaged King");
            Assert.AreEqual(BandagedKingModule.ArenaId, sim.Arena.Def.Id);
            var king = (BandagedKingModule)sim.Fight.Module;
            Assert.AreEqual((sim.Arena.OriginX, sim.Arena.OriginY), (king.OriginX, king.OriginY), "its court is its arena");
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual(sim.Arena.BossStartX, b.X, 0.05, "it arrives at its arena's boss start");
            Assert.AreEqual(sim.Arena.BossStartY, b.Y, 0.05);
        }

        [Test]
        public void ItsCourtIsItsArenasOutline()
        {
            var outline = Content.Arenas.Find(BandagedKingModule.ArenaId).Outline;
            Assert.AreEqual(BandagedKingModule.Court.Length, outline.X.Length);
            for (int i = 0; i < outline.X.Length; i++)
                Assert.AreEqual(BandagedKingModule.Court[i], (outline.X[i], outline.Y[i]), $"corner {i}");
        }

        [Test]
        public void TheCameraHoldsTheCourtWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Content.Arenas.Find(BandagedKingModule.ArenaId), sim.Now);
            sim.StepTo(sim.Now + 3);
            var camera = new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 };
            var frame = new ArenaFraming(camera).Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(frame.Scrolls, "a fixed view: every lash, front and the King always in frame");
        }

        /// <summary>
        /// The Bandaged King against a reference build at its place in the run (<c>--boss 7</c>), the scripted
        /// <c>positioning</c> player on the 128 BPM dance playlist, the hero unkillable: the fight's length and what the hero took.
        /// </summary>
        public static (double FightS, double Taken, int Hits) ReferenceRun(string build, ulong seed)
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == BandagedKingModule.ModuleId);
            var run = new RunInputs
            {
                Seed = seed, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], Start = RunStart.Immediately,
                RushStart = rules.ReferenceCheckpoint(index, Content.Combat, build),
            };
            var config = run.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            var player = ScriptedPlayer.Parse("positioning", "reference", Content.Dashes);
            var entries = LevelRunTests.Playlist(400, seed).Entries();
            int next = 0, cursor = 0;
            double frontier = 0, from = double.NaN, taken = 0;
            int hits = 0;
            const double frame = 1.0 / 60, lead = 0.05;
            while (frontier < 390)
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
                if (double.IsNaN(from) && sim.Fight.Module is BandagedKingModule && sim.Fight.Stage == FightStage.Fighting) from = sim.State.RunClock;
                if (!double.IsNaN(from) && sim.Fight.Stage == FightStage.Dead) return (sim.State.RunClock - from, taken, hits);
                player.Frame(sim, frontier - lead);
            }
            Assert.Fail($"{build} seed {seed}: the Bandaged King still stands ({sim.State.Phase} {sim.State.End} rush {sim.BossRush?.Stage} boss {sim.BossRush?.Boss} fight {sim.Fight.Module?.Id} {sim.Fight.Stage} now {sim.Now:F1} share {sim.Fight.HealthShare:F3} max {sim.Fight.MaxHealth:F0} taken {taken:F0} hits {hits})");
            return default;
        }

        [Test, Explicit("a minute or two: the reference builds against the Bandaged King into evidence/boss-rush/bandagedKing/reference-runs.tsv")]
        public void MeasureTheReferenceBuildsAgainstTheBandagedKing()
        {
            var tsv = new StringBuilder("build\tseed\tfightS\ttaken\thits\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                {
                    var (s, taken, hits) = ReferenceRun(build, seed);
                    tsv.Append(string.Join("\t", build, seed.ToString(CultureInfo.InvariantCulture), s.ToString("F1", CultureInfo.InvariantCulture),
                        taken.ToString("F0", CultureInfo.InvariantCulture), hits.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "bandagedKing"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
        }
    }
}
