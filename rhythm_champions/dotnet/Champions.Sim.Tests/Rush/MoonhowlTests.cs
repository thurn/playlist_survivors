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
    /// Boss Rush boss 5, Moonhowl (boss-rush.md, The roster: "The moon cycle"; ps-8z4.14): its fairness at its slowest and
    /// fastest tempos, the cycle (the howl on a downbeat, the frenzy taught alone, then moon lunges among the lunges, the
    /// stalk's claws and leaps), the moon that times it, its phase, the beatless fallback, its replay and its slot in the run.
    /// </summary>
    public class MoonhowlTests
    {
        const double Bpm = 120, BeatS = 60 / Bpm;
        static GameContent Content => Fairness.Content;

        static FairnessOptions Options(double bpm, ulong seed = 1) => new FairnessOptions
        {
            Bpm = bpm, Act = 1, Seed = seed, FightS = 130, Arena = MoonhowlModule.ArenaId, ChaseShare = 0.5,
            // The glade is held whole by a fixed camera (TheCameraHoldsTheGladeWhole): all of it is on screen.
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

        /// <summary>The hero circling the glade's centre, 7 m out.</summary>
        static Action<Simulation> Circling(MoonhowlModule m) =>
            s => s.Target(m.OriginX + 7 * Math.Cos(s.Now * 0.6), m.OriginY + 7 * Math.Sin(s.Now * 0.6), s.Now);

        // ---- Fairness ----

        [TestCase(90)]
        [TestCase(180)]
        public void MoonhowlIsFair(double bpm)
        {
            var report = Fairness.Check(() => new MoonhowlModule(), Options(bpm));
            TestContext.Out.WriteLine(report);
            TestContext.Out.WriteLine(string.Join(" ", report.Hazards.GroupBy(h => h.Attack).Select(g => $"{g.First().Move}@{g.Min(h => h.Telegraph.Land):F1}")));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "a real dash wave: " + report);
            Assert.That(report.Hazards.Select(h => h.Move).Distinct(),
                Is.SupersetOf(new[] { BossMove.Tail, BossMove.Smash, BossMove.Stomp, BossMove.Fans, BossMove.Boulders }),
                "the howl, the lunges, the moon lunge, the claws and the leap all played");
        }

        [Test]
        public void MoonhowlIsFairOnOtherSeeds([Values(2UL, 3UL)] ulong seed)
        {
            Fairness.AssertFair(Fairness.Check(() => new MoonhowlModule(), Options(128, seed)));
        }

        // ---- The cycle ----

        [Test]
        public void ItHowlsOnADownbeatThenTeachesTheFrenzyAloneLungingOnEveryBeat()
        {
            var sim = Sim();
            var wolf = new MoonhowlModule();
            sim.Fight.Start(wolf, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = Play(sim, 16 * BeatS, Circling(wolf));
            Assert.AreEqual(BossMove.Tail, laid[0].Move, "it opens with the howl");
            Assert.AreEqual(HazardFlags.Signature, laid[0].Flags & HazardFlags.Signature, "the howl marks the turn (its bank's signature)");
            Assert.IsTrue(On(Downbeats(sim), laid[0].Telegraph.Land), "the howl lands on a downbeat");
            var lesson = laid.Skip(1).Take(8).ToList();
            Assert.That(lesson.Select(h => h.Move), Is.All.EqualTo(BossMove.Smash), "eight plain lunges, nothing else");
            for (int k = 0; k < lesson.Count; k++)
            {
                Assert.AreEqual(laid[0].Telegraph.Land + (k + 1) * BeatS, lesson[k].Telegraph.Land, 1e-6, $"lunge {k} on the beat after the last");
                Assert.That(lesson[k].Telegraph.Land - lesson[k].Telegraph.Start, Is.GreaterThanOrEqualTo(3 * BeatS - 1e-6), "three beats' warning");
                Assert.AreEqual(HazardBodyMove.AtLand, lesson[k].BodyMove, "it runs each lunge on its beat");
            }
            for (int k = 1; k < lesson.Count; k++)
            {
                Assert.AreEqual(lesson[k - 1].ToX, lesson[k].Shape.X, 1e-9, "each lunge starts where the last left it");
                Assert.AreEqual(lesson[k - 1].ToY, lesson[k].Shape.Y, 1e-9);
            }
            Assert.That(laid.Where(h => h.DashWave), Is.Empty, "no moon lunge in the lesson");
        }

        [Test]
        public void LaterFrenziesThrowAMoonLungeOnADownbeatAndTheStalkSwipesAndLeaps()
        {
            var sim = Sim();
            var wolf = new MoonhowlModule();
            sim.Fight.Start(wolf, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            // Two cycles and a half, the hero by turns close by (claws) and far away (leaps).
            var laid = Play(sim, 80 * BeatS, s =>
            {
                ref readonly var b = ref s.Fight.Bodies[0];
                bool close = ((int)(s.Now / 6)) % 2 == 0;
                if (close) s.Target(b.X + 2.5, b.Y, s.Now);
                else s.Target(wolf.OriginX - 0.8 * (b.X - wolf.OriginX), wolf.OriginY - 0.8 * (b.Y - wolf.OriginY) - 2, s.Now);
            });
            var howls = laid.Where(h => h.Move == BossMove.Tail).Select(h => h.Telegraph.Land).ToList();
            Assert.That(howls.Count, Is.GreaterThanOrEqualTo(3));
            for (int k = 1; k < howls.Count; k++)
                Assert.AreEqual(MoonhowlModule.CycleBeats[0] * BeatS, howls[k] - howls[k - 1], 1e-6, "a howl every eight bars");
            var moons = laid.Where(h => h.Move == BossMove.Stomp).ToList();
            Assert.That(moons, Is.Not.Empty, "moon lunges after the lesson");
            Assert.That(moons.Select(h => h.DashWave), Is.All.True, "each a dash wave");
            Assert.That(moons.Select(h => h.Telegraph.Land).Where(t => !On(Downbeats(sim), t)), Is.Empty, "on downbeats");
            Assert.That(moons.Select(h => h.Telegraph.Land), Is.All.GreaterThan(howls[1] - 1e-6), "none before the second cycle");
            Assert.That(moons, Has.All.Matches<Hazard>(h => Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy) > 3 * Content.Hero.SpeedMps), "its front outruns any walk");
            var claws = laid.Where(h => h.Move == BossMove.Fans).GroupBy(h => h.Attack).ToList();
            Assert.That(claws, Is.Not.Empty, "the claws in the stalk");
            var beats = Beats(sim);
            foreach (var combo in claws)
            {
                var hits = combo.OrderBy(h => h.Part).ToList();
                Assert.AreEqual(2, hits.Count, "two claws in phase 1");
                Assert.AreEqual(BeatS / 2, hits[1].Telegraph.Land - hits[0].Telegraph.Land, 1e-6, "the second on the half beat");
                Assert.IsTrue(On(beats, hits[0].Telegraph.Land));
            }
            Assert.That(laid.Where(h => h.Move == BossMove.Boulders), Is.Not.Empty, "the leap at a hero keeping away");
            // Stalk moves never land in a howl or a frenzy.
            foreach (var h in laid.Where(h => h.Move == BossMove.Fans || h.Move == BossMove.Boulders))
            {
                double since = howls.Where(t => t <= h.Telegraph.Land + 1e-6).DefaultIfEmpty(double.NaN).Max();
                Assert.That((h.Telegraph.Land - since) / BeatS, Is.GreaterThan(MoonhowlModule.FrenzyTo + 1e-6), "in the stalk");
            }
        }

        [Test]
        public void TheMoonIsFullThroughTheFrenzyAndNewHalfwayThroughTheStalk()
        {
            var sim = Sim();
            var wolf = new MoonhowlModule();
            sim.Fight.Start(wolf, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            var laid = Play(sim, 12 * BeatS, Circling(wolf));
            double howl = laid.First(h => h.Move == BossMove.Tail).Telegraph.Land;
            Until(sim, () => sim.Now >= howl + 0.05);
            Assert.AreEqual(1, wolf.Moon(sim.Fight, sim.Now), 1e-9, "full at the howl");
            Assert.IsTrue(wolf.FullMoon(sim.Fight));
            Until(sim, () => sim.Now >= howl + 5 * BeatS + 0.05);
            Assert.IsTrue(wolf.FullMoon(sim.Fight), "full through the frenzy");
            double newMoon = howl + (MoonhowlModule.FrenzyTo + 1 + (MoonhowlModule.CycleBeats[0] - MoonhowlModule.FrenzyTo - 1) / 2.0) * BeatS;
            Until(sim, () => sim.Now >= newMoon - 0.02);
            Assert.That(wolf.Moon(sim.Fight, sim.Now), Is.LessThan(0.1), "new halfway through the stalk");
            Assert.IsFalse(wolf.FullMoon(sim.Fight));
            Until(sim, () => sim.Now >= howl + (MoonhowlModule.CycleBeats[0] - 1) * BeatS + 0.02);
            Assert.That(wolf.Moon(sim.Fight, sim.Now), Is.GreaterThan(0.9), "waxing to full at the next howl");
        }

        // ---- Its phase ----

        [Test]
        public void BelowHalfTheHuntersMoonShortensTheCycleWithTwoMoonLungesAndAThirdClaw()
        {
            var sim = Sim();
            var wolf = new MoonhowlModule();
            sim.Fight.Start(wolf, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Strike(sim, 520);
            Assert.AreEqual(2, sim.Fight.Phase);
            Assert.IsTrue(sim.Fight.Untouchable, "four untouchable beats");
            Assert.IsTrue(wolf.FullMoon(sim.Fight), "the hunter's moon rises full");
            var laid = Play(sim, 60, s =>
            {
                ref readonly var b = ref s.Fight.Bodies[0];
                s.Target(b.X + 2.5, b.Y, s.Now);
            });
            double phase = Drive.Events(sim, SimEventKind.BossPhase).Single().T;
            Assert.That(laid.First().Telegraph.Start, Is.GreaterThanOrEqualTo(phase + 4 * BeatS - 1e-6), "nothing through the transition");
            Assert.AreEqual(BossMove.Tail, laid.First().Move, "a fresh cycle from its howl");
            var howls = laid.Where(h => h.Move == BossMove.Tail).Select(h => h.Telegraph.Land).ToList();
            Assert.AreEqual(MoonhowlModule.CycleBeats[1] * BeatS, howls[1] - howls[0], 1e-6, "a six-bar cycle");
            var firstFrenzy = laid.Where(h => h.Telegraph.Land > howls[0] && h.Telegraph.Land < howls[1]).ToList();
            Assert.AreEqual(2, firstFrenzy.Count(h => h.Move == BossMove.Stomp), "two moon lunges a frenzy");
            var moons = firstFrenzy.Where(h => h.Move == BossMove.Stomp).ToList();
            Assert.That(moons[1].Telegraph.Land - moons[0].Telegraph.Land, Is.GreaterThanOrEqualTo(Content.Dashes.CooldownS + BeatS - 1e-6), "a dash's cooldown and a beat apart");
            var claws = laid.Where(h => h.Move == BossMove.Fans).GroupBy(h => h.Attack).First().OrderBy(h => h.Part).ToList();
            Assert.AreEqual(3, claws.Count, "three claws");
            Assert.AreEqual(BeatS, claws[2].Telegraph.Land - claws[0].Telegraph.Land, 1e-6, "on the beat, the half beat and the next beat");
        }

        // ---- Its XP ----

        [Test]
        public void ItsBurstsFallInACrescentBehindIt()
        {
            var sim = Sim();
            var wolf = new MoonhowlModule();
            var bursts = new List<IReadOnlyList<GemSpot>>();
            sim.Fight.Shed += (_, _, _, spots) => bursts.Add(spots.ToList());
            sim.Fight.Start(wolf, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.Target(wolf.OriginX, wolf.OriginY - 10, sim.Now);
            Strike(sim, 120);
            Until(sim, () => bursts.Count > 0, 10);
            double bx = sim.Fight.Bodies[0].X, by = sim.Fight.Bodies[0].Y;
            sim.HeroWas(sim.Now, out double hx, out double hy);
            double ax = bx - hx, ay = by - hy, d = Math.Sqrt(ax * ax + ay * ay);
            Assert.That(bursts[0].Count, Is.EqualTo(7));
            Assert.AreEqual(1, bursts[0].Sum(s => s.Share), 1e-9);
            Assert.That(bursts[0], Has.All.Matches<GemSpot>(s => ((s.X - bx) * ax + (s.Y - by) * ay) / d > 0.5), "behind it, away from the hero");
        }

        // ---- Silence, determinism ----

        [Test]
        public void WithoutABeatItPlaysOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var wolf = new MoonhowlModule();
            sim.Fight.Start(wolf, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            var laid = Play(sim, 60, Circling(wolf));
            Assert.That(laid.Select(h => h.Move).Distinct(), Is.SupersetOf(new[] { BossMove.Tail, BossMove.Smash, BossMove.Stomp }),
                "its cycle turns: " + string.Join(",", laid.Select(h => h.Move).Distinct()));
            double beat = sim.Beats.Rules.TelegraphBeatlessBeatS;
            Assert.That(laid.Select(h => h.Telegraph.Land - h.Telegraph.Start), Is.All.GreaterThanOrEqualTo(3 * beat - 1e-6), "three fallback beats' warning");
        }

        [Test]
        public void ARecordedMoonhowlFightReplaysToItsHash()
        {
            var entries = new FixtureSong(5).Beats(0, 40, 128, jitter: 0.003).Gap(40, 46).Beats(46, 90, 150, jitter: 0.002).Entries();
            var run = new RunInputs { Seed = 5, Fight = MoonhowlModule.ModuleId, Start = RunStart.Immediately, SurvivorWeapons = true };
            var live = new Simulation(run.Build(Content));
            var rng = new Random(9);
            double next = 0;
            int fed = Drive.Feed(live, entries, 90, eachFrame: s =>
            {
                if (s.Now < next) return;
                next = s.Now + 1.5;
                s.Target(rng.NextDouble() * 24 - 12, rng.NextDouble() * 24 - 12, s.Now - 0.05);
                if (rng.NextDouble() < 0.3) s.Press(Key.Space, s.Now - 0.05);
            });
            Assert.That(live.Fight.Hazards.Length + Drive.Events(live, SimEventKind.BossMove).Count, Is.GreaterThan(10), "the fight played");
            string dir = Path.Combine(Path.GetTempPath(), "champions-moonhowl-" + Guid.NewGuid().ToString("N"));
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
        public void ItsSlotFightsInItsGladeArrivingAtTheBossStart()
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == MoonhowlModule.ModuleId);
            Assert.IsTrue(rules.Bosses[index].Fight, "landed: its module and cast row");
            Assert.AreEqual(MoonhowlModule.ArenaId, rules.Bosses[index].Arena);
            var config = new RunInputs { Seed = 4, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 500, Bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double stop = 480;
            while (!(sim.Fight.Module is MoonhowlModule && sim.Fight.Stage == FightStage.Fighting) && sim.Now < stop)
            {
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (RushTestKit.Fighting(sim) && sim.BossRush.Slot.Id != MoonhowlModule.ModuleId) RushTestKit.Strike(sim, 1e12);
                sim.StepTo(sim.Now + 1.0 / 60);
            }
            Assert.IsTrue(sim.Fight.Module is MoonhowlModule, "the run reached Moonhowl");
            Assert.AreEqual(MoonhowlModule.ArenaId, sim.Arena.Def.Id);
            var wolf = (MoonhowlModule)sim.Fight.Module;
            Assert.AreEqual((sim.Arena.OriginX, sim.Arena.OriginY), (wolf.OriginX, wolf.OriginY), "its glade is its arena");
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual(sim.Arena.BossStartX, b.X, 0.05, "it arrives at its arena's boss start");
            Assert.AreEqual(sim.Arena.BossStartY, b.Y, 0.05);
        }

        [Test]
        public void TheCameraHoldsTheGladeWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Content.Arenas.Find(MoonhowlModule.ArenaId), sim.Now);
            sim.StepTo(sim.Now + 3);
            var camera = new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 };
            var frame = new ArenaFraming(camera).Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(frame.Scrolls, "a fixed view: every lunge, front and the moon always in frame");
        }

        /// <summary>
        /// Moonhowl against a reference build at its place in the run (<c>--boss 5</c>), the scripted <c>positioning</c>
        /// player on the 128 BPM dance playlist, the hero unkillable: the fight's length and what the hero took.
        /// </summary>
        public static (double FightS, double Taken, int Hits) ReferenceRun(string build, ulong seed)
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == MoonhowlModule.ModuleId);
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
                if (double.IsNaN(from) && sim.Fight.Module is MoonhowlModule && sim.Fight.Stage == FightStage.Fighting) from = sim.State.RunClock;
                if (!double.IsNaN(from) && sim.Fight.Stage == FightStage.Dead) return (sim.State.RunClock - from, taken, hits);
                player.Frame(sim, frontier - lead);
            }
            Assert.Fail($"{build} seed {seed}: Moonhowl still stands ({sim.State.Phase} {sim.State.End} rush {sim.BossRush?.Stage} boss {sim.BossRush?.Boss} fight {sim.Fight.Module?.Id} {sim.Fight.Stage} now {sim.Now:F1} share {sim.Fight.HealthShare:F3} max {sim.Fight.MaxHealth:F0} taken {taken:F0} hits {hits})");
            return default;
        }

        [Test, Explicit("a minute or two: the reference builds against Moonhowl into evidence/boss-rush/moonhowl/reference-runs.tsv")]
        public void MeasureTheReferenceBuildsAgainstMoonhowl()
        {
            var tsv = new StringBuilder("build\tseed\tfightS\ttaken\thits\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                {
                    var (s, taken, hits) = ReferenceRun(build, seed);
                    tsv.Append(string.Join("\t", build, seed.ToString(CultureInfo.InvariantCulture), s.ToString("F1", CultureInfo.InvariantCulture),
                        taken.ToString("F0", CultureInfo.InvariantCulture), hits.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "moonhowl"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
        }
    }
}
