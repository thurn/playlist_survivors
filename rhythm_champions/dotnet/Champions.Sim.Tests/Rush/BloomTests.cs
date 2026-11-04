using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss Rush boss 3, the Bloom (boss-rush.md, The roster: "Rotating spirals"; ps-8z4.10): its fairness at its slowest
    /// and fastest tempos, the arms taught alone and then turning on the bar, the bite punishing the close, its phase,
    /// the beatless fallback, its replay, and its slot in the run (its arena, its root).
    /// </summary>
    public class BloomTests
    {
        const double Bpm = 120, BeatS = 60 / Bpm;
        static GameContent Content => Fairness.Content;

        /// <summary>The bloom arena's walkable disc and extent round its root, for the harness.</summary>
        static FairnessOptions Options(double bpm, ulong seed = 1) => new FairnessOptions
        {
            Bpm = bpm, Act = 1, Seed = seed, FightS = 125,
            Inside = (x, y) => x * x + y * y <= 16.5 * 16.5, ArenaRadiusM = 16.5, ExtentM = 17,
            // The bloom arena is held whole by a fixed camera (TheCameraHoldsTheBloomsArenaWhole): all of it is on screen.
            ScreenRadiusM = 40,
        };

        static Simulation Sim(Action<SimConfig> tweak = null, double songS = 240, double bpm = Bpm, bool beats = true)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            tweak?.Invoke(config);
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

        /// <summary>Plays a Bloom fight for <paramref name="seconds"/>, recording every hazard it lays (in order) and when the hero first saw it.</summary>
        static List<Hazard> Play(Simulation sim, BloomModule bloom, double seconds, Action<Simulation> eachFrame = null,
            Dictionary<int, (double X, double Y)> heroAt = null)
        {
            var seen = new HashSet<int>();
            var laid = new List<Hazard>();
            double stop = sim.Now + seconds;
            while (sim.Now < stop && sim.Fight.Stage != FightStage.Dead)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                eachFrame?.Invoke(sim);
                foreach (var h in sim.Fight.Hazards)
                    if (seen.Add(h.Id))
                    {
                        laid.Add(h);
                        if (heroAt != null) { sim.HeroWas(sim.Now, out double x, out double y); heroAt[h.Id] = (x, y); }
                    }
            }
            return laid;
        }

        static double Angle(in HitShape s) => Math.Atan2(s.Y2 - s.Y, s.X2 - s.X);

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        // ---- Fairness ----

        [TestCase(90)]
        [TestCase(180)]
        public void TheBloomIsFair(double bpm)
        {
            var report = Fairness.Check(() => new BloomModule(), Options(bpm));
            TestContext.Out.WriteLine(report);
            TestContext.Out.WriteLine(string.Join(" ", report.Hazards.GroupBy(h => h.Attack).Select(g => $"{g.First().Move}@{g.Min(h => h.Telegraph.Land):F1}")));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "a real dash wave: " + report);
            Assert.That(report.Hazards.Select(h => h.Move).Distinct(), Is.SupersetOf(new[] { BossMove.Breath, BossMove.Smash, BossMove.Stomp, BossMove.Fireballs }),
                "the arms, the bite, the pulse and the spiral all played");
        }

        [Test]
        public void TheBloomIsFairOnOtherSeeds([Values(2UL, 3UL)] ulong seed)
        {
            Fairness.AssertFair(Fairness.Check(() => new BloomModule(), Options(128, seed)));
        }

        // ---- The arms ----

        [Test]
        public void ItsArmsAreTaughtAloneThenTurnOnTheBarChangingDirection()
        {
            var sim = Sim();
            var bloom = new BloomModule();
            sim.Fight.Start(bloom, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            // The hero hugs the root through the lesson: no bite over the arms' first lesson.
            sim.Target(bloom.RootX + 2, bloom.RootY, sim.Now);
            var laid = Play(sim, bloom, 9 * 4 * BeatS);
            var first = laid.Where(h => h.Attack == laid[0].Attack).ToList();
            Assert.AreEqual(3, first.Count, "three arms first");
            Assert.That(first.Select(h => h.Move), Is.All.EqualTo(BossMove.Breath));
            Assert.That(first.Select(h => h.Flags & HazardFlags.Signature), Is.All.EqualTo(HazardFlags.Signature), "its signature");
            var lesson = laid.TakeWhile(h => h.Move == BossMove.Breath).ToList();
            Assert.AreEqual(9, lesson.Count, "three bars of three arms, and nothing else");
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.I2 != 0).Select(e => e.T).ToArray();
            Assert.That(lesson.Select(h => h.Telegraph.Land), Is.All.Matches<double>(t => downbeats.Any(d => Math.Abs(d - t) < 1e-6)), "landing on downbeats");
            // Bar by bar, each set lands where the last has turned to: one set of arms, steady and one way in the lesson.
            var bars = lesson.GroupBy(h => h.Attack).Select(g => g.OrderBy(h => h.Part).ToList()).ToList();
            for (int i = 1; i < bars.Count; i++)
            {
                var a = bars[i - 1][0];
                double turned = Angle(a.Shape) + a.SpinRadPerS * (bars[i][0].Telegraph.Land - a.Telegraph.Land);
                Assert.AreEqual(0, Wrap(turned - Angle(bars[i][0].Shape)), 1e-6, $"bar {i} continues bar {i - 1}");
                Assert.AreEqual(4 * BeatS, bars[i][0].Telegraph.Land - a.Telegraph.Land, 1e-6, "a bar apart");
                Assert.AreEqual(a.SpinRadPerS, bars[i][0].SpinRadPerS, 1e-12, "the lesson turns steadily");
            }
            for (int k = 1; k < 3; k++) Assert.AreEqual(0, Wrap(Angle(bars[0][k].Shape) - Angle(bars[0][0].Shape) - 2 * Math.PI * k / 3), 1e-6, "evenly spaced");
            // Later arms change direction on the bar.
            laid = Play(sim, bloom, 40);
            var spins = laid.Where(h => h.Move == BossMove.Breath && h.Part == 0).Select(h => h.SpinRadPerS).ToList();
            Assert.That(spins, Has.Some.Negative.And.Some.Positive, "the arms reverse");
            Assert.That(spins.Select(Math.Abs).Max(), Is.LessThanOrEqualTo(BloomModule.MaxSpinRadPerS + 1e-12), "never faster than its tip can be walked with");
        }

        // ---- The bite ----

        [Test]
        public void TheBiteSnapsAtAHeroStandingCloseOnTheHalfBeatAndNeverAtOneFarAway()
        {
            var sim = Sim();
            var bloom = new BloomModule();
            sim.Fight.Start(bloom, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.Target(bloom.RootX, bloom.RootY - 14, sim.Now);
            var far = Play(sim, bloom, 50);
            Assert.That(far.Select(h => h.Move), Has.None.EqualTo(BossMove.Smash), "no bite 14 m out");
            sim.Target(bloom.RootX + 3, bloom.RootY, sim.Now);
            var heroAt = new Dictionary<int, (double X, double Y)>();
            var near = Play(sim, bloom, 30, heroAt: heroAt);
            var bites = near.Where(h => h.Move == BossMove.Smash).ToList();
            Assert.That(bites, Is.Not.Empty, "a hero beside its root is bitten at");
            var beats = Drive.Events(sim, SimEventKind.Beat).Select(e => e.T).ToArray();
            foreach (var b in bites)
            {
                Assert.AreEqual(ShapeKind.Cone, b.Shape.Kind);
                var (hx, hy) = heroAt[b.Id];
                double d = Math.Sqrt((hx - bloom.RootX) * (hx - bloom.RootX) + (hy - bloom.RootY) * (hy - bloom.RootY));
                Assert.Greater((b.Shape.DirX * (hx - bloom.RootX) + b.Shape.DirY * (hy - bloom.RootY)) / d, 0.99, "toward the hero");
                Assert.That(d, Is.LessThanOrEqualTo(BloomModule.BiteTriggerM + 0.2), "only when close");
                Assert.That(b.Telegraph.Land - b.Telegraph.Start, Is.GreaterThanOrEqualTo(3 * BeatS - 1e-6), "three beats' warning in act 1");
                Assert.IsTrue(beats.Zip(beats.Skip(1), (x, y) => (x + y) / 2).Any(m => Math.Abs(m - b.Telegraph.Land) < 1e-6), "on a half beat");
            }
            Assert.That(bites.Zip(bites.Skip(1), (x, y) => y.Telegraph.Land - x.Telegraph.Land), Is.All.GreaterThanOrEqualTo(BloomModule.BiteEveryBars * 4 * BeatS - 1e-6));
            Assert.That(Drive.Events(sim, SimEventKind.HeroHit), Is.Not.Empty, "standing still beside it hurts");
        }

        // ---- Its phase ----

        [Test]
        public void BelowHalfItBlossomsIntoFourArmsAndATwoFrontPulse()
        {
            var sim = Sim();
            var bloom = new BloomModule();
            sim.Fight.Start(bloom, sim.Now, new FightSetup { Health = 1000 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Strike(sim, 520);
            Assert.AreEqual(2, sim.Fight.Phase);
            Assert.IsTrue(sim.Fight.Untouchable, "four untouchable beats");
            var laid = Play(sim, bloom, 60, s => s.Target(bloom.RootX, bloom.RootY - 12, s.Now));
            Assert.That(laid.First().Telegraph.Start, Is.GreaterThanOrEqualTo(Drive.Events(sim, SimEventKind.BossPhase).Single().T + 4 * BeatS - 1e-6));
            Assert.AreEqual(4, laid.Count(h => h.Attack == laid[0].Attack), "four arms");
            var pulse = laid.Where(h => h.Move == BossMove.Stomp).GroupBy(h => h.Attack).First().ToList();
            Assert.AreEqual(2, pulse.Count, "two fronts");
            Assert.That(pulse.Select(h => h.DashWave), Is.All.True);
        }

        // ---- Silence, determinism ----

        [Test]
        public void WithoutABeatItPlaysOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var bloom = new BloomModule();
            sim.Fight.Start(bloom, sim.Now);
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            var laid = Play(sim, bloom, 60, s => s.Target(bloom.RootX + 9 * Math.Cos(s.Now), bloom.RootY + 9 * Math.Sin(s.Now), s.Now));
            Assert.That(laid.Select(h => h.Move).Distinct().Count(), Is.GreaterThanOrEqualTo(3), "it keeps fighting: " + string.Join(",", laid.Select(h => h.Move).Distinct()));
            double beat = sim.Beats.Rules.TelegraphBeatlessBeatS;
            Assert.That(laid.Select(h => h.Telegraph.Land - h.Telegraph.Start), Is.All.GreaterThanOrEqualTo(3 * beat - 1e-6), "three fallback beats' warning");
        }

        [Test]
        public void ARecordedBloomFightReplaysToItsHash()
        {
            var entries = new FixtureSong(5).Beats(0, 40, 128, jitter: 0.003).Gap(40, 46).Beats(46, 90, 150, jitter: 0.002).Entries();
            var run = new RunInputs { Seed = 5, Fight = BloomModule.ModuleId, Start = RunStart.Immediately, SurvivorWeapons = true };
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
            string dir = Path.Combine(Path.GetTempPath(), "champions-bloom-" + Guid.NewGuid().ToString("N"));
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
        public void ItsSlotFightsInItsOwnArenaRootedAtTheBossStart()
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == BloomModule.ModuleId);
            Assert.IsTrue(rules.Bosses[index].Fight, "landed: its module and cast row");
            Assert.AreEqual("bloom", rules.Bosses[index].Arena);
            var config = new RunInputs { Seed = 4, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 300, Bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double stop = 280;
            while (!(sim.Fight.Module is BloomModule && sim.Fight.Stage == FightStage.Fighting) && sim.Now < stop)
            {
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (RushTestKit.Fighting(sim) && sim.BossRush.Slot.Id != BloomModule.ModuleId) RushTestKit.Strike(sim, 1e12);
                sim.StepTo(sim.Now + 1.0 / 60);
            }
            Assert.IsTrue(sim.Fight.Module is BloomModule, "the run reached the Bloom");
            Assert.AreEqual("bloom", sim.Arena.Def.Id);
            var bloom = (BloomModule)sim.Fight.Module;
            Assert.AreEqual((sim.Arena.BossStartX, sim.Arena.BossStartY), (bloom.RootX, bloom.RootY));
            Assert.AreEqual(bloom.RootX, sim.Fight.Bodies[0].X, 1e-6, "rooted at its arena's centre");
            Assert.AreEqual(bloom.RootY, sim.Fight.Bodies[0].Y, 1e-6);
        }

        /// <summary>
        /// The Bloom against a reference build at its place in the run (<c>--boss 3</c>), the scripted <c>positioning</c>
        /// player on the 128 BPM dance playlist, the hero unkillable: the fight's length and what the hero took.
        /// </summary>
        public static (double FightS, double Taken, int Hits) ReferenceRun(string build, ulong seed)
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == BloomModule.ModuleId);
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
                if (double.IsNaN(from) && sim.Fight.Module is BloomModule && sim.Fight.Stage == FightStage.Fighting) from = sim.State.RunClock;
                if (!double.IsNaN(from) && sim.Fight.Stage == FightStage.Dead) return (sim.State.RunClock - from, taken, hits);
                player.Frame(sim, frontier - lead);
            }
            Assert.Fail($"{build} seed {seed}: the Bloom still stands ({sim.State.Phase} {sim.State.End} rush {sim.BossRush?.Stage} boss {sim.BossRush?.Boss} fight {sim.Fight.Module?.Id} {sim.Fight.Stage} now {sim.Now:F1} share {sim.Fight.HealthShare:F3} max {sim.Fight.MaxHealth:F0} taken {taken:F0} hits {hits})");
            return default;
        }

        [Test, Explicit("a minute or two: the reference builds against the Bloom into evidence/boss-rush/bloom/reference-runs.tsv")]
        public void MeasureTheReferenceBuildsAgainstTheBloom()
        {
            var tsv = new StringBuilder("build\tseed\tfightS\ttaken\thits\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                {
                    var (s, taken, hits) = ReferenceRun(build, seed);
                    tsv.Append(string.Join("\t", build, seed.ToString(CultureInfo.InvariantCulture), s.ToString("F1", CultureInfo.InvariantCulture),
                        taken.ToString("F0", CultureInfo.InvariantCulture), hits.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "bloom"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
        }

        [Test]
        public void TheCameraHoldsTheBloomsArenaWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Content.Arenas.Find("bloom"), sim.Now);
            sim.StepTo(sim.Now + 3);
            var camera = new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 };
            var frame = new ArenaFraming(camera).Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(frame.Scrolls, "a fixed view: every hazard and the root always in frame");
        }
    }
}
