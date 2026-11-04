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
    /// Boss Rush progression (boss-rush.md, Progression; ps-8z4.4): the boss sheds its fixed XP budget in gem bursts at each
    /// 10% of health lost, on the next beat, in its authored geometry, the kill dropping the rest; the mode's own XP curve
    /// (about 2–3 levels a boss, the 77-step build complete at bosses 23–25); the reference builds; and the health table,
    /// each slot's target fight length × the reference builds' measured sustained damage there.
    /// </summary>
    public class BossRushProgressionTests
    {
        static readonly GameContent Content = GameContent.Load(Checkout.In("Content"));
        const int FullBuild = 77;

        static RunInputs Rush(BossRushCheckpoint start = null) =>
            new RunInputs { Seed = 3, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], RushStart = start };

        static SimConfig Config(RunInputs run, Action<BossRushRules> tweak = null)
        {
            var c = run.Build(Content);
            c.Hero.Health = 1e9;
            // The stand-ins' shedding (bossrush.json's shapes); the landed fights' is the framework's (BossFightTests).
            RushTestKit.StandIns(c.BossRush, c.Enemies);
            tweak?.Invoke(c.BossRush);
            return c;
        }

        static Simulation Make(SimConfig config, double bpm = 120, double seconds = 600)
        {
            var s = new Simulation(config);
            if (bpm > 0) foreach (var e in Drive.Known(new FixtureSong().Beats(0, seconds, bpm).Entries())) s.AddKnown(e.Entry, e.KnownAt);
            return s;
        }

        static void Until(Simulation s, Func<bool> ready, double seconds = 60)
        {
            double until = s.Now + seconds;
            while (!ready() && s.Now < until) s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.IsTrue(ready(), "scripted condition within the simulated horizon");
        }

        static void Hit(Simulation s, double damage)
        {
            ref readonly var b = ref s.Enemies[s.Bosses.Id];
            s.Strikes.Land(s, new Blow { Shape = HitShape.Circle(b.X, b.Y, 0.1), T = s.Now, Damage = damage,
                Tags = HitTags.Strike, Source = BlowSource.SwordSlash, Owner = -1, Part = -1 });
        }

        /// <summary>Takes the boss to <paramref name="share"/> of its health (the stagger's damage scale aside).</summary>
        static void DownTo(Simulation s, double share)
        {
            for (int guard = 0; guard < 50 && s.Bosses.Health > share * s.Bosses.MaxHealth + 1e-6; guard++)
                Hit(s, (s.Bosses.Health - share * s.Bosses.MaxHealth) / s.Bosses.DamageScale + 1e-6);
        }

        static List<SimEvent> Since(Simulation s, int from, SimEventKind kind) => s.EventsSince(from).ToArray().Where(e => e.Kind == kind).ToList();

        static Simulation Fighting(SimConfig config, double bpm = 120)
        {
            var s = Make(config, bpm);
            Until(s, () => s.Bosses.InDuel && s.BossRush.Stage == BossRushStage.Fighting && !s.State.Held);
            return s;
        }

        // ---- The curve ----

        [Test]
        public void TheModeHasItsOwnCurveAndTheMainGamesIsUntouched()
        {
            var rush = Rush().Build(Content).Progression;
            var main = new RunInputs { Seed = 3, SurvivorWeapons = true }.Build(Content).Progression;
            Assert.AreEqual(Content.BossRush.Needed(1), rush.Needed(1));
            Assert.AreEqual(Content.BossRush.Needed(40), rush.Needed(40), 1e-9);
            Assert.AreEqual(Content.Progression.Needed(1), main.Needed(1), "progression.json stays the main game's");
            Assert.AreNotEqual(main.Needed(1), rush.Needed(1));
        }

        [Test]
        public void EachBudgetIsItsLevelsOnTheCurveAboutTwoOrThreeABossTheFullBuildLandingAtBosses23To25()
        {
            var r = Content.BossRush;
            int fullAfter = -1;
            for (int i = 0; i < r.Count; i++)
            {
                var b = r.Bosses[i];
                double exact = r.ExperienceFor(r.LevelAt(i), b.Levels);
                Assert.That(b.Experience, Is.InRange(exact, exact + 0.01), $"{b.Id}: its {b.Levels} levels' XP (never short of them)");
                Assert.That(b.Levels, Is.InRange(2, 3), $"{b.Id}: about two or three levels");
                if (i > 0) Assert.LessOrEqual(b.Levels, r.Bosses[i - 1].Levels, "a little more early on, never more later");
                // After boss i: its own and earlier levels, and its reward (the last boss's kill wins, no reward).
                int steps = r.LevelAt(i + 1) - 1 + Math.Min(i + 1, r.Count - 1);
                if (fullAfter < 0 && steps >= FullBuild) fullAfter = i;
            }
            Assert.That(fullAfter + 1, Is.InRange(23, 25), "the 77-step build completes around bosses 23-25");
            Assert.AreEqual(3, r.Bosses[0].Levels, "act 1 gives a little more");
        }

        [Test]
        public void TheReferenceBuildsFollowTheCurveFocusedDeepAndBroadWide()
        {
            var r = Content.BossRush;
            CollectionAssert.AreEquivalent(new[] { "focused", "broad" }, r.ReferenceBuilds.Keys);
            foreach (string build in r.ReferenceBuilds.Keys)
            {
                var last = r.ReferenceCheckpoint(r.Count - 1, Content.Combat, build);
                Assert.AreEqual(r.LevelAt(r.Count - 1), last.Level);
                Assert.AreEqual(Math.Min(FullBuild, r.StepsAt(r.Count - 1)), last.Steps, $"{build}: the whole build by the finale");
                Assert.AreEqual(Math.Max(0, r.StepsAt(r.Count - 1) - FullBuild), last.Fallbacks);
                var five = r.ReferenceCheckpoint(5, Content.Combat, build);
                Assert.AreEqual(r.StepsAt(5), five.Steps);
            }
            var focused = r.ReferenceCheckpoint(5, Content.Combat, "focused").Build;
            var broad = r.ReferenceCheckpoint(5, Content.Combat, "broad").Build;
            Assert.AreEqual(8, focused.First(c => c.Kind == CardKind.Weapon && c.Weapon == WeaponKind.Sword).Rank, "focused: the sword first, to its top");
            Assert.Less(focused.Count(c => c.Kind == CardKind.Weapon), broad.Count(c => c.Kind == CardKind.Weapon), "broad: more weapons at once");
            Assert.AreEqual(r.ReferenceCheckpoint(7, Content.Combat).Steps, r.ReferenceCheckpoint(7, Content.Combat, r.ReferenceBuild).Steps, "--boss N's default");
        }

        // ---- Shedding ----

        [Test]
        public void EachTenthOfHealthBurstsATenthOfTheBudgetOnTheNextBeatAndTheKillDropsTheRest()
        {
            var s = Fighting(Config(Rush()));
            var slot = s.BossRush.Slot;
            double dropped0 = s.Pickups.ExperienceDropped;
            for (int k = 1; k <= 9; k++)
            {
                int from = s.EventCount;
                DownTo(s, 1 - k * 0.1);
                s.StepTo(s.Now + Simulation.TickSeconds);
                Assert.AreEqual(0, Since(s, from, SimEventKind.RushShed).Count, $"threshold {k}: nothing before the beat");
                int beat = s.State.LastBeat;
                Until(s, () => Since(s, from, SimEventKind.RushShed).Count > 0, 2);
                var shed = Since(s, from, SimEventKind.RushShed).Single();
                Assert.AreEqual(k, shed.I0);
                Assert.AreEqual(slot.Experience * 0.1, shed.D0, 1e-9);
                Assert.Greater(s.State.LastBeat, beat, "on the next beat");
                Assert.AreEqual(s.Beats.BeatTime(s.State.LastBeat), shed.T, 1e-9, "at the beat's own time");
                var drops = Since(s, from, SimEventKind.PickupDrop);
                Assert.AreEqual(slot.Shed.Gems, drops.Count, "a cluster of the shape's gems");
                if (s.State.Offer.Up) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }
                Until(s, () => !s.State.Held, 5);
            }
            Assert.AreEqual(9, s.BossRush.Bursts);
            Assert.AreEqual(slot.Experience * 0.9, s.BossRush.Shed, 1e-6);
            Hit(s, 1e12);
            Assert.AreEqual(slot.Experience, s.Pickups.ExperienceDropped - dropped0, 1e-6, "the whole budget, no more");
            Until(s, () =>
            {
                if (s.State.Offer.Up) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }
                return s.BossRush.Boss == 1;
            });
            Assert.AreEqual(1 + slot.Levels, s.State.Level, "every gem collected: the slot's levels");
        }

        [Test]
        public void SeveralThresholdsInOneBlowBurstTogetherAndStallingEarnsNothing()
        {
            var s = Fighting(Config(Rush()));
            int from = s.EventCount;
            DownTo(s, 0.62);
            Until(s, () => Since(s, from, SimEventKind.RushShed).Count > 0, 2);
            var bursts = Since(s, from, SimEventKind.RushShed);
            Assert.AreEqual(3, bursts.Count, "90%, 80% and 70% passed");
            Assert.IsTrue(bursts.All(b => b.T == bursts[0].T), "all on the same beat");
            // However long the fight runs, only the health lost sheds: the bursts are the thresholds passed, each a fixed share.
            s.StepTo(s.Now + 30);
            s.StepTo(s.Now + 1);
            var slot = s.BossRush.Slot;
            int passed = Math.Min(9, (int)Math.Floor((1 - s.Bosses.Health / s.Bosses.MaxHealth) / 0.1 + 1e-9));
            Assert.AreEqual(passed, s.BossRush.Bursts);
            Assert.AreEqual(passed * 0.1 * slot.Experience, s.BossRush.Shed, 1e-6);
        }

        [Test]
        public void WithoutABeatAThresholdBurstsOneFallbackBeatLater()
        {
            var s = Fighting(Config(Rush()), bpm: 0);
            int from = s.EventCount;
            double at = s.State.RunClock;
            DownTo(s, 0.85);
            Until(s, () => Since(s, from, SimEventKind.RushShed).Count > 0, 3);
            Assert.AreEqual(at + Content.Beat.TelegraphBeatlessBeatS, s.State.RunClock, 0.03);
        }

        [Test]
        public void AddsDrawFromTheSameBudget()
        {
            var s = Fighting(Config(Rush()));
            var slot = s.BossRush.Slot;
            double dropped0 = s.Pickups.ExperienceDropped;
            Assert.AreEqual(10, s.BossRush.Draw(10));
            Assert.AreEqual(slot.Experience - 10, s.BossRush.Draw(1e9), 1e-9, "no more than what's left");
            Assert.AreEqual(0, s.BossRush.Draw(5));
            Hit(s, 1e12);
            Assert.AreEqual(0, s.Pickups.ExperienceDropped - dropped0, 1e-9, "the kill drops nothing more");
        }

        [Test]
        public void TheShapesLieRoundAheadAndAcross()
        {
            double facing = Math.PI / 3, fx = Math.Cos(facing), fy = Math.Sin(facing);
            var ring = new BossShedShape { Kind = BossShedKind.Ring, Gems = 6, RadiusM = 5 };
            var spray = new BossShedShape { Kind = BossShedKind.Spray, Gems = 5, RadiusM = 6, SpreadDeg = 80 };
            var lane = new BossShedShape { Kind = BossShedKind.Lane, Gems = 7, RadiusM = 4, LengthM = 18 };
            foreach (var (shape, check) in new (BossShedShape, Action<double, double>)[]
            {
                (ring, (dx, dy) => Assert.AreEqual(5, Math.Sqrt(dx * dx + dy * dy), 1e-9)),
                (spray, (dx, dy) => Assert.Greater((dx * fx + dy * fy) / Math.Sqrt(dx * dx + dy * dy), Math.Cos(41 * Math.PI / 180), "within the fan")),
                (lane, (dx, dy) => Assert.AreEqual(4, dx * fx + dy * fy, 1e-9, "on the line ahead")),
            })
            {
                var spots = new List<(double, double)>();
                for (int i = 0; i < shape.Gems; i++)
                {
                    BossRush.Spot(shape, 2, i, facing, out double dx, out double dy);
                    check(dx, dy);
                    spots.Add((Math.Floor(dx + 100.3), Math.Floor(dy + 100.7)));
                }
                Assert.AreEqual(shape.Gems, spots.Distinct().Count(), $"{shape.Kind}: no two gems merge into one pickup cell");
            }
        }

        [Test]
        public void ASlotsAuthoredShapeReplacesTheDefault()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-shed-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyContent(dir);
                string path = Path.Combine(dir, BossRushRules.File);
                string json = File.ReadAllText(path).Replace("\"levels\": 3, \"targetS\": 120.0,", "\"levels\": 3, \"targetS\": 120.0, \"shed\": {\"shape\": \"lane\", \"gems\": 8},");
                File.WriteAllText(path, json);
                var r = GameContent.Load(dir).BossRush;
                Assert.AreEqual(BossShedKind.Lane, r.Bosses[0].Shed.Kind);
                Assert.AreEqual(8, r.Bosses[0].Shed.Gems);
                Assert.AreEqual(r.Bosses[1].Shed.LengthM, r.Bosses[0].Shed.LengthM, "the rest from the default");
                Assert.AreEqual(BossShedKind.Ring, r.Bosses[1].Shed.Kind);
            }
            finally { Directory.Delete(dir, true); }
        }

        static void CopyContent(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(Checkout.In("Content"), "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        }

        // ---- Health scaling ----

        [Test]
        public void EverySlotsHealthIsItsTargetTimesTheMeasuredDamage()
        {
            var r = Content.BossRush;
            var measured = Measured();
            Assert.AreEqual(r.Count, measured.Count, $"evidence/boss-rush/progression/{MeasuredFile} has every slot");
            for (int i = 0; i < r.Count; i++)
            {
                var b = r.Bosses[i];
                Assert.IsFalse(double.IsNaN(b.Health), $"{b.Id}: health scaled");
                Assert.AreEqual(Math.Round(b.TargetS * measured[i]), b.Health, 1, $"{b.Id}: {b.TargetS} s × {measured[i]:F1} damage/s");
                if (i > 0) Assert.Greater(b.TargetS, r.Bosses[i - 1].TargetS, "fights lengthen through the run");
            }
            Assert.That(r.Bosses[0].TargetS, Is.InRange(105, 135), "about 2 minutes in act 1");
            Assert.That(r.Bosses[r.Count - 1].TargetS, Is.InRange(165, 195), "about 3 in act 5");
        }

        const string MeasuredFile = "sustained-damage.tsv";

        /// <summary>The measured table's mean of the builds' sustained damage per slot (the columns after slot and id).</summary>
        static List<double> Measured()
        {
            var lines = File.ReadAllLines(Checkout.In(Path.Combine("evidence", "boss-rush", "progression", MeasuredFile)));
            var mean = new List<double>();
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split('\t');
                mean.Add(double.Parse(cols[cols.Length - 1], CultureInfo.InvariantCulture));
            }
            return mean;
        }

        /// <summary>
        /// The reference player (`--walk positioning`, its telegraph dodges) with reference build <paramref name="build"/>
        /// at slot <paramref name="index"/>, against that slot's fighter made unkillable, no XP: the boss health it takes
        /// per second over <paramref name="window"/> s of the duel, a 128 BPM dance playlist.
        /// </summary>
        public static double SustainedDamage(int index, string build, double window = 60, ulong seed = 31)
        {
            var rules = Content.BossRush;
            var run = Rush(rules.ReferenceCheckpoint(index, Content.Combat, build));
            run.Seed = seed;
            run.Start = RunStart.Immediately;
            var config = run.Build(Content);
            config.Hero.Health = 1e9;
            foreach (var b in config.BossRush.Bosses) { b.Health = 1e9; b.Experience = 0; }
            var sim = new Simulation(config);
            var player = ScriptedPlayer.Parse("positioning", "reference", Content.Dashes);
            var entries = LevelRunTests.Playlist(window + 60, seed).Entries();
            int next = 0, cursor = 0;
            double frontier = 0, from = double.NaN, health0 = 0;
            const double frame = 1.0 / 60, lead = 0.05;
            while (sim.State.Phase != RunPhase.Ended)
            {
                frontier += frame;
                while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                sim.StepTo(frontier);
                foreach (var e in sim.EventsSince(cursor)) player.Take(sim, e);
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                if (double.IsNaN(from) && RushTestKit.Fighting(sim))
                {
                    from = sim.State.RunClock;
                    health0 = BossHealth(sim);
                }
                if (!double.IsNaN(from) && sim.State.RunClock - from >= window) break;
                if (frontier > window + 50) Assert.Fail($"slot {index + 1} {build}: no duel within the horizon");
                player.Frame(sim, frontier - lead);
            }
            return (health0 - BossHealth(sim)) / (sim.State.RunClock - from);
        }

        /// <summary>The run's boss's health: its landed fight's, or its stand-in duel's.</summary>
        static double BossHealth(Simulation sim) => sim.BossRush.Slot.Fight ? sim.Fight.Health : sim.Bosses.Health;

        [Test, Explicit("a few minutes: measures the reference builds' sustained damage per slot into evidence/boss-rush/progression/ and prints the health table")]
        public void MeasureTheReferenceBuildsAndPrintTheHealthTable()
        {
            var r = Content.BossRush;
            var names = r.ReferenceBuilds.Keys.OrderBy(n => n == "focused" ? 0 : 1).ToArray();
            var tsv = new StringBuilder("slot\tid\tfighter\tlevel\tsteps\t" + string.Join("\t", names) + "\tmean\n");
            var table = new StringBuilder();
            for (int i = 0; i < r.Count; i++)
            {
                var b = r.Bosses[i];
                var dps = names.Select(n => SustainedDamage(i, n)).ToArray();
                double mean = Math.Round(dps.Average(), 1);
                tsv.Append(string.Join("\t", new[] { (i + 1).ToString(CultureInfo.InvariantCulture), b.Id, Content.Enemies.Cast[b.Cast].Id,
                    r.LevelAt(i).ToString(CultureInfo.InvariantCulture), r.StepsAt(i).ToString(CultureInfo.InvariantCulture) }
                    .Concat(dps.Select(d => d.ToString("F1", CultureInfo.InvariantCulture))).Concat(new[] { mean.ToString("F1", CultureInfo.InvariantCulture) })) + "\n");
                table.AppendLine($"{b.Id}\t{Math.Round(b.TargetS * mean).ToString(CultureInfo.InvariantCulture)}");
            }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "progression"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MeasuredFile), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
            TestContext.Out.WriteLine(table.ToString());
        }
    }
}
