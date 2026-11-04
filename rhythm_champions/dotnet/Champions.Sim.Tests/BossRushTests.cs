using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The Boss Rush shell (boss-rush.md, The Run; ps-8z4.1): the acts and bosses in order with stand-ins for unlanded
    /// fights, each boss's forming, fight and reward, the act cards, act checkpoints and their round trip, victory, and
    /// replay determinism.
    /// </summary>
    public class BossRushTests
    {
        static GameContent Content => GameContent.Load(Checkout.In("Content"));
        static readonly GameContent Loaded = Content;

        static RunInputs Rush(BossRushCheckpoint start = null) =>
            new RunInputs { Seed = 3, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], RushStart = start };

        /// <summary>The run's config, every slot fought by its stand-in (the shell's own rules; <see cref="RushTestKit.StandIns"/>).</summary>
        static SimConfig Config(RunInputs run, GameContent content = null, Action<BossRushRules> tweak = null)
        {
            var c = run.Build(content ?? Content);
            c.Hero.Health = 1e9;
            RushTestKit.StandIns(c.BossRush, c.Enemies);
            tweak?.Invoke(c.BossRush);
            return c;
        }

        static Simulation Make(SimConfig config, double bpm = 120, double seconds = 900)
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

        static void Pick(Simulation s) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }

        static void Kill(Simulation s)
        {
            ref readonly var b = ref s.Enemies[s.Bosses.Id];
            s.Strikes.Land(s, new Blow { Shape = HitShape.Circle(b.X, b.Y, 0.1), T = s.Now, Damage = 1e12,
                Tags = HitTags.Strike, Source = BlowSource.SwordSlash, Owner = -1, Part = -1 });
        }

        /// <summary>Fights the boss now to its death and takes every card, until the rush moves on.</summary>
        static void Clear(Simulation s)
        {
            int boss = s.BossRush.Boss;
            Until(s, () => s.Bosses.InDuel && s.BossRush.Stage == BossRushStage.Fighting);
            Kill(s);
            Until(s, () =>
            {
                if (s.State.Offer.Up) Pick(s);
                return s.BossRush.Boss != boss || s.State.Phase == RunPhase.Ended;
            });
        }

        static List<SimEvent> Stages(Simulation s) => Drive.Events(s, SimEventKind.RushStage).ToList();

        [Test]
        public void TheContentHasFiveActsOfFiveEachSlotFoughtByAMainGameBossUntilItsFightLands()
        {
            var content = Content;
            var rules = content.BossRush;
            Assert.AreEqual(5, rules.Acts.Length);
            Assert.AreEqual(25, rules.Count);
            for (int i = 0; i < rules.Count; i++)
            {
                var b = rules.Bosses[i];
                Assert.AreEqual(i / 5, b.Act, $"{b.Id} in act {i / 5 + 1}");
                if (b.Fight) Assert.IsTrue(BossModules.Has(b.Id) && content.Enemies.Cast[b.Cast].Id == b.Id, $"{b.Id} fights as its own module");
                else Assert.IsNotNull(content.Bosses.Of(b.Cast), $"{b.Id} has a fighter with a duel");
                if (!b.Landed) Assert.AreEqual(b.StandIn, content.Enemies.Cast[b.Cast].Id);
            }
            Assert.AreEqual("treasureGoblin", rules.Bosses[0].Id);
            Assert.AreEqual("titan", rules.Bosses[24].Id);
        }

        [Test]
        public void EachBossFormsArrivesAndIsRewardedThenTheActCardLeadsToTheNextAct()
        {
            var s = Make(Config(Rush()));
            var first = Stages(s);
            Assert.AreEqual(BossRushStage.Act, (BossRushStage)first[0].I0);
            Assert.AreEqual(BossRushStage.Forming, (BossRushStage)first[1].I0);
            Assert.AreEqual(0, s.BossRush.Boss);
            Assert.AreEqual(0, s.Bosses.Called, "the arena forms before the boss is called");
            Assert.AreEqual(Loaded.BossRush.Acts[0].Zone, s.Zones.Key, "the act's floor key");
            Until(s, () => s.Bosses.Called == 1, 10);
            Assert.AreEqual(s.BossRush.Slot.Cast, s.Bosses.Cast, "the stand-in fights the unlanded slot");

            Until(s, () => s.Bosses.InDuel);
            s.State.Hero.Health = 1;
            double budget = s.BossRush.Slot.Experience;
            Kill(s);
            Assert.AreEqual(BossRushStage.Reward, s.BossRush.Stage);
            Assert.AreEqual(s.State.Hero.MaxHealth, s.State.Hero.Health, "full heal at the kill");
            Assert.AreEqual(OfferKind.Treasure, s.State.Offer.Kind, "the reward card first");
            Assert.AreEqual(0, s.Pickups.StoredCount, "every gem is magnetised");
            Assert.AreEqual(budget, s.Pickups.ExperienceDropped, 1e-9, "the unshed budget drops at the kill");
            Until(s, () =>
            {
                if (s.State.Offer.Up) Pick(s);
                return s.BossRush.Stage == BossRushStage.Forming;
            });
            Assert.AreEqual(budget, s.Pickups.ExperienceCollected, 1e-9, "the gems come in before the next arena");
            Assert.Greater(s.State.Level, 1, "the queued level-ups were offered");
            Assert.AreEqual(1, s.BossRush.Boss);
            var forming = Stages(s).Last();
            Until(s, () => s.Bosses.Called == 2, 10);
            var called = Stages(s).Last();
            Assert.AreEqual(BossRushStage.Fighting, (BossRushStage)called.I0);
            var beats = Drive.Events(s, SimEventKind.Beat).Where(e => e.T > forming.T + 1e-9 && e.T <= called.T + 1e-9).ToList();
            Assert.AreEqual(4, beats.Count, "the arena forms over four beats");
            Assert.AreEqual(beats.Last().T, called.T, 1e-9, "the boss is called on the fourth");

            for (int i = 1; i < 5; i++) Clear(s);
            Assert.AreEqual(BossRushStage.ActCard, s.BossRush.Stage, "an act card after the fifth boss");
            Assert.AreEqual(5, s.BossRush.Boss);
            Assert.AreEqual(1, s.BossRush.Act);
            double card = s.State.RunClock;
            s.StepTo(s.Now + 1);
            s.Press(Key.Space, s.Now);
            s.Release(Key.Space, s.Now);
            s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.AreEqual(BossRushStage.ActCard, s.BossRush.Stage, "a dash never skips the act card");
            s.Press(Key.Enter, s.Now);
            s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.AreEqual(BossRushStage.Forming, s.BossRush.Stage, "Enter skips the act card");
            Assert.Less(s.State.RunClock - card, Loaded.BossRush.ActCardS);
            Assert.AreEqual(Loaded.BossRush.Acts[1].Zone, s.Zones.Key);

            var cp = s.BossRush.Checkpoint;
            Assert.AreEqual(5, cp.Boss, "act 2's checkpoint");
            Assert.AreEqual(s.State.Level, cp.Level);
            CollectionAssert.AreEqual(BossRushCheckpoint.CardsOf(s.State.Build).Select(c => c.Code), cp.Build.Select(c => c.Code));
            Assert.Greater(cp.Steps, 5, "the build grew through act 1");
            Assert.AreEqual(s.State.RunClock, cp.TimeBefore, 1e-9);
            var stages = Stages(s).Select(e => (BossRushStage)e.I0).ToList();
            Assert.AreEqual(2, stages.Count(x => x == BossRushStage.Act));
            Assert.AreEqual(5, stages.Count(x => x == BossRushStage.Reward));
            Assert.AreEqual(0, Drive.Events(s, SimEventKind.EnemySpawn).Count(e => e.I2 == 0), "no horde, no reinforcements");
        }

        [Test]
        public void TheLastBossWinsTheRunAndUnlandedSlotsCanBeSkipped()
        {
            var config = Config(Rush(), tweak: r =>
            {
                r.Unlanded = BossRushUnlanded.Skip;
                r.Bosses[1].Landed = true;
                r.Bosses[6].Landed = true;
            });
            var s = Make(config);
            Assert.AreEqual(1, s.BossRush.Boss, "the first unlanded slot is passed over");
            Clear(s);
            Assert.AreEqual(BossRushStage.ActCard, s.BossRush.Stage);
            Assert.AreEqual(6, s.BossRush.Boss);
            Clear(s);
            Assert.AreEqual(RunEnd.Victory, s.State.End);
            Assert.AreEqual(BossRushStage.Won, s.BossRush.Stage);
            Assert.AreEqual(2, s.BossRush.Killed);
        }

        [Test]
        public void ADeathEndsTheAttemptAndKeepsTheActsCheckpoint()
        {
            var c = Config(Rush());
            c.Hero.Health = 1;
            var s = Make(c);
            Until(s, () => s.State.Phase == RunPhase.Ended, 120);
            Assert.AreEqual(RunEnd.Death, s.State.End);
            Assert.AreEqual(0, s.BossRush.Checkpoint.Boss);
            Assert.AreEqual(1, s.BossRush.Checkpoint.Level);
            var retry = s.BossRush.Retry(death: true);
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 0, 0 }, retry.Deaths);
            Assert.AreEqual(s.State.RunClock, retry.TimeBefore, 1e-9, "the failed attempt's time counts");
            Assert.AreEqual(0, retry.Boss);
            var again = Make(Config(Rush(retry)));
            CollectionAssert.AreEqual(retry.Deaths, again.BossRush.Deaths, "the retry carries the deaths");
            Assert.AreEqual(retry.TimeBefore, again.BossRush.TotalTime, 1e-9);
        }

        [Test]
        public void ACheckpointRoundTripsAndARetryStartsFromIt()
        {
            var start = new BossRushCheckpoint
            {
                Boss = 5,
                Build = new[] { Card.Of(WeaponKind.Sword, 3, 0), Card.Of(WeaponKind.Bow, 2, 0), Card.Of(SupportKind.Might, 2, 0) },
                Level = 9, Experience = 4.5, HeldLevels = 2, Fallbacks = 1, TimeBefore = 321.25, Deaths = new[] { 2, 1, 0, 0, 0 },
            };
            var bytes = new MemoryStream();
            using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) start.Write(w);
            bytes.Position = 0;
            var read = BossRushCheckpoint.Read(new BinaryReader(bytes), "test");
            Assert.AreEqual(start.Boss, read.Boss);
            CollectionAssert.AreEqual(start.Build.Select(c => c.Code), read.Build.Select(c => c.Code));
            Assert.AreEqual(start.Level, read.Level);
            Assert.AreEqual(start.Experience, read.Experience);
            Assert.AreEqual(start.HeldLevels, read.HeldLevels);
            Assert.AreEqual(start.Fallbacks, read.Fallbacks);
            Assert.AreEqual(start.TimeBefore, read.TimeBefore);
            CollectionAssert.AreEqual(start.Deaths, read.Deaths);
            Assert.Throws<FormatException>(() => BossRushCheckpoint.Read(new BinaryReader(new MemoryStream(new byte[] { 1, 2, 3 })), "junk"));

            string dir = Path.Combine(Path.GetTempPath(), "champions-rush-" + Guid.NewGuid().ToString("N"));
            try
            {
                string file = Path.Combine(dir, "bossrush.checkpoint");
                start.Save(file);
                Assert.AreEqual(start.TimeBefore, BossRushCheckpoint.Load(file, out _).TimeBefore);
                Assert.IsNull(BossRushCheckpoint.Load(Path.Combine(dir, "none"), out string none));
                Assert.IsNull(none);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }

            var s = Make(Config(Rush(read)));
            Assert.AreEqual(5, s.BossRush.Boss);
            Assert.AreEqual(1, s.BossRush.Act);
            Assert.AreEqual(3, s.State.Build.Level(WeaponKind.Sword));
            Assert.AreEqual(2, s.State.Build.Level(WeaponKind.Bow));
            Assert.AreEqual(2, s.State.Build.Level(SupportKind.Might));
            Assert.AreEqual(9, s.State.Level);
            Assert.AreEqual(4.5, s.State.Experience);
            Assert.AreEqual(1, s.Progression.Fallbacks);
            s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.IsTrue(s.State.Offer.Up, "the pending choices open at once");
            Assert.AreEqual(1, s.State.HeldLevels);
            var cp = s.BossRush.Checkpoint;
            Assert.AreEqual(5, cp.Boss);
            CollectionAssert.AreEqual(start.Deaths, cp.Deaths, "the run's totals carry on");
            Assert.AreEqual(start.TimeBefore, cp.TimeBefore);
        }

        [Test]
        public void BossNStartsFromTheReferenceBuildForThatIndex()
        {
            var content = Content;
            var rules = content.BossRush;
            var zero = rules.ReferenceCheckpoint(0, content.Combat);
            Assert.AreEqual(0, zero.Steps);
            Assert.AreEqual(1, zero.Level);
            var later = rules.ReferenceCheckpoint(12, content.Combat);
            Assert.AreEqual(12, later.Boss);
            Assert.AreEqual(rules.StepsAt(12), later.Steps);
            Assert.AreEqual(rules.LevelAt(12), later.Level);
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.ReferenceCheckpoint(25, content.Combat));
            var s = Make(Config(Rush(later), content));
            Assert.AreEqual(12, s.BossRush.Boss);
            Assert.AreEqual(2, s.BossRush.Act);
            Until(s, () => s.Bosses.InDuel);
        }

        [Test]
        public void ASlotsHealthReplacesTheStandInsAndBeatlessFormingFallsBackToTheClock()
        {
            var s = Make(Config(Rush(), tweak: r => r.Bosses[0].Health = 1234), bpm: 0);
            Until(s, () => s.Bosses.Called == 1, 10);
            Assert.AreEqual(4 * Loaded.Beat.TelegraphBeatlessBeatS, s.State.RunClock, 0.02, "four fallback beats");
            Until(s, () => s.Bosses.InDuel, 10);
            Assert.AreEqual(1234, s.Bosses.MaxHealth);
        }

        [Test]
        public void ARushSessionReplaysToItsHash()
        {
            // A strong build that kills by its own weapons (every input recorded), from the first boss (the health table's
            // lightest; the driver's stand-close walk may die to the second, which ends the attempt the same way).
            var start = new BossRushCheckpoint
            {
                Boss = 0, Level = 4, Deaths = new[] { 1, 0, 0, 0, 0 },
                Build = new[] { Card.Of(WeaponKind.Sword, 8, 0), Card.Of(WeaponKind.Lightning, 8, 0), Card.Of(WeaponKind.OrbitBlades, 8, 0),
                    Card.Of(WeaponKind.EmberField, 8, 0), Card.Of(SupportKind.Might, 5, 0), Card.Of(SupportKind.Armor, 5, 0) },
            };
            var run = Rush(start);
            var entries = new FixtureSong(9).Beats(0, 300, 124).Entries();
            var live = new Simulation(run.Build(Content));
            int fed = Drive.Feed(live, entries, 240, eachFrame: s =>
            {
                if (s.State.Phase != RunPhase.Playing) return;
                if (s.State.Offer.Up) { Pick(s); return; }
                if (s.BossRush.Killed >= 2 && s.BossRush.Stage == BossRushStage.Forming) { s.Control(RunControl.Quit, s.Now); return; }
                if (!RushTestKit.Fighting(s) || s.Now % 0.5 > 1.0 / 60) return;
                ref readonly var b = ref s.Enemies[RushTestKit.Boss(s)];
                double tx = b.X, ty = b.Y - 3;
                s.HeroWas(s.Now, out double hx, out double hy);
                ScriptedPlayer.Steer(s, hx, hy, ref tx, ref ty);
                s.Target(tx, ty, s.Now);
            });
            Assert.GreaterOrEqual(live.BossRush.Killed, 1, "the build's weapons killed a boss");
            string dir = Path.Combine(Path.GetTempPath(), "champions-rush-" + Guid.NewGuid().ToString("N"));
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, entries.GetRange(0, fed));
                var read = SessionRecord.Read(dir);
                Assert.IsTrue(read.Run.BossRush);
                Assert.AreEqual(0, read.Run.RushStart.Boss);
                Assert.AreEqual(4, read.Run.RushStart.Level);
                var replay = new SessionReplay(read);
                foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
                var sim = new Simulation(read.Run.Build(Content));
                Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16}, recorded {record.Hash:X16}");
                Assert.AreEqual(live.BossRush.Boss, sim.BossRush.Boss);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
