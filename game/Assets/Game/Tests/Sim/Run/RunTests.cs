using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The run (unity-game.md, The Run): its start, the run clock, freezes and the music stopping,
    /// victory, the ends and Easy's chickens.
    /// </summary>
    public class RunTests
    {
        const double Eps = 1e-9;

        /// <summary>Beats, ticks and ends only, at their normal known-at times.</summary>
        static List<KnownEntry> Known(FixtureSong song) =>
            Drive.Known(song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick || e.Kind == EntryKind.End));

        static Simulation Start(SimConfig config, IEnumerable<KnownEntry> known)
        {
            var sim = new Simulation(config);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<FreezeEvent> Freezes(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Freeze).Select(e => e.AsFreeze).ToList();

        static List<BeatEvent> Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Select(e => e.AsBeat).ToList();

        static List<double> StrideSteps(Simulation sim) => Drive.Events(sim, SimEventKind.StrideStep).Select(e => e.T).ToList();

        static List<HeroStepEvent> Moves(Simulation sim) => Drive.Events(sim, SimEventKind.HeroStep).Select(e => e.AsHeroStep).ToList();

        /// <summary>The second downbeat after the first reliable beat at or after <paramref name="from"/>.</summary>
        static double SecondDownbeatAfterLock(Simulation sim, double from)
        {
            var beats = Beats(sim).Where(b => b.T >= from - Eps).ToList();
            int lockAt = beats.FindIndex(b => b.Reliability == Reliability.Reliable);
            return beats.Skip(lockAt + 1).Where(b => b.Downbeat).ElementAt(1).T;
        }

        // ---------------------------------------------------------------- freezes

        [Test]
        public void APauseStopsTheRunClockAndPlayResumesOnTheNextBeat()
        {
            var sim = Start(new SimConfig(), Known(new FixtureSong(3).Beats(0, 30, 120)));
            sim.StepTo(10.03);
            double paused = sim.Now;
            sim.Control(RunControl.Pause);
            Assert.IsTrue(sim.State.Frozen);
            Assert.IsTrue(sim.State.FrozenFor(FreezeReason.Pause));
            sim.StepTo(15);
            Assert.AreEqual(15, sim.Now, 1e-9, "song time goes on");
            Assert.AreEqual(paused, sim.State.RunClock, Eps, "the run clock doesn't");
            sim.Control(RunControl.Resume);
            Assert.IsFalse(sim.State.FrozenFor(FreezeReason.Pause), "the banner goes");
            Assert.IsTrue(sim.State.Frozen, "the world waits for the beat");
            sim.StepTo(20);
            double beat = sim.Stream.Beats.ToArray().Select(b => b.T).First(t => t > 15 + Eps);
            var freezes = Freezes(sim);
            Assert.AreEqual(2, freezes.Count);
            Assert.AreEqual(FreezeReason.Pause, freezes[0].Reason);
            Assert.IsTrue(freezes[0].Starts);
            Assert.AreEqual(paused, freezes[0].T, Eps);
            Assert.IsFalse(freezes[1].Starts);
            Assert.AreEqual(beat, freezes[1].T, Eps, "on the next beat");
            Assert.IsFalse(sim.State.Frozen);
            Assert.AreEqual(paused + (20 - beat), sim.State.RunClock, 1e-6);
        }

        [Test]
        public void APauseAgainBeforeTheResumesBeatContinuesTheSameFreeze()
        {
            var sim = Start(new SimConfig(), Known(new FixtureSong(3).Beats(0, 30, 120)));
            sim.StepTo(10.03);
            double paused = sim.Now;
            sim.Control(RunControl.Pause);
            sim.StepTo(12.03);
            sim.Control(RunControl.Resume);
            sim.Control(RunControl.Pause); // Esc twice, or a focus loss, before the beat
            sim.StepTo(14);
            Assert.IsTrue(sim.State.FrozenFor(FreezeReason.Pause));
            sim.Control(RunControl.Resume);
            sim.StepTo(16);
            var freezes = Freezes(sim);
            Assert.AreEqual(2, freezes.Count, "one freeze, started once and ended once");
            Assert.IsTrue(freezes[0].Starts);
            Assert.IsFalse(freezes[1].Starts);
            Assert.Greater(freezes[1].T, 14);
            Assert.AreEqual(paused + (16 - freezes[1].T), sim.State.RunClock, 1e-6);
        }

        [Test]
        public void QuietAgainBeforeTheResumeRestartsTheLock()
        {
            // Locked at 53 (as below), but quiet again at 53.6, before the second downbeat at 55.
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 52, silent: true).Beats(52, 53.6, 120)
                .Gap(53.6, 54.4, silent: true).Beats(54.4, 80, 120);
            var sim = Start(new SimConfig(), Known(song));
            sim.StepTo(80);
            var freezes = Freezes(sim);
            Assert.AreEqual(2, freezes.Count);
            Assert.AreEqual(SecondDownbeatAfterLock(sim, 54.4), freezes[1].T, Eps, "the lock after the second quiet");
            Assert.Greater(freezes[1].T, 56.4);
        }

        [Test]
        public void TheMusicStoppingFreezesTheWorldUntilTheSecondDownbeatAfterTheBeatLocks()
        {
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 52, silent: true).Beats(52, 90, 120);
            var sim = Start(new SimConfig(), Known(song));
            sim.StepTo(38);
            sim.Press(Key.Right, 37.98); // held through the freeze
            sim.StepTo(90);

            var freezes = Freezes(sim);
            Assert.AreEqual(2, freezes.Count);
            Assert.AreEqual(FreezeReason.MusicStopped, freezes[0].Reason);
            // 5 s of quiet from 40.00 is known with the tick at 45.00: the first stride step after.
            double known = sim.Stream.KnownAt(EntryKind.Tick, sim.Stream.Ticks.ToArray().ToList().FindIndex(t => t.T >= 45 - Eps));
            double freeze = freezes[0].T;
            Assert.AreEqual(StrideSteps(sim).First(t => t >= known - Eps), freeze, Eps);

            double resume = freezes[1].T;
            Assert.AreEqual(SecondDownbeatAfterLock(sim, 52), resume, Eps);
            Assert.IsTrue(Beats(sim).Single(b => Math.Abs(b.T - resume) < Eps).Downbeat);
            Assert.AreEqual(55, resume, 0.01, "locked at 53 (a second of clear evidence), one bar heard, then go");

            Assert.AreEqual(freeze + (90 - resume), sim.State.RunClock, 1e-6, "the run clock stood still through the gap");
            var moves = Moves(sim);
            Assert.IsFalse(moves.Any(m => m.Start > freeze - Eps && m.Start < resume - Eps), "the held key moves nothing while frozen");
            Assert.IsTrue(moves.Any(m => m.Start < freeze) && moves.Any(m => m.Start >= resume - Eps));
        }

        [Test]
        public void AShortGapDoesNotStopTheMusic()
        {
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 42, silent: true).Beats(42, 60, 120);
            var sim = Start(new SimConfig(), Known(song));
            sim.StepTo(60);
            Assert.AreEqual(0, Freezes(sim).Count);
            Assert.AreEqual(60, sim.State.RunClock, 1e-6);
        }

        [Test]
        public void AConfirmedEndStopsTheMusicAfterThreeQuietSeconds()
        {
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 52, silent: true).Beats(52, 70, 120);
            var known = Known(song);
            known.Add(new KnownEntry(StreamEntry.End(40, FixtureSong.StampOf(43)), FixtureSong.StampOf(43) + 0.5));
            var sim = Start(new SimConfig(), known);
            sim.StepTo(70);
            var freezes = Freezes(sim);
            Assert.AreEqual(FreezeReason.MusicStopped, freezes[0].Reason);
            int tick43 = sim.Stream.Ticks.ToArray().ToList().FindIndex(t => t.T >= 43 - Eps);
            double known43 = sim.Stream.KnownAt(EntryKind.Tick, tick43);
            Assert.AreEqual(StrideSteps(sim).First(t => t >= known43 - Eps), freezes[0].T, Eps, "3 s, not 5");
        }

        [Test]
        public void TheWatchdogStopsTheMusicOnTheNextStrideStepUntilTheBeatLocksAgain()
        {
            var sim = Start(new SimConfig(), Known(new FixtureSong(3).Beats(0, 40, 120)));
            sim.StepTo(10.03);
            double given = sim.Now;
            sim.Control(RunControl.Watchdog);
            sim.StepTo(30);
            var freezes = Freezes(sim);
            Assert.AreEqual(2, freezes.Count);
            Assert.AreEqual(FreezeReason.MusicStopped, freezes[0].Reason);
            Assert.AreEqual(StrideSteps(sim).First(t => t > given), freezes[0].T, Eps);
            Assert.AreEqual(SecondDownbeatAfterLock(sim, freezes[0].T), freezes[1].T, Eps);
        }

        [Test]
        public void APauseDuringTheMusicStoppingWaitsForBoth()
        {
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 52, silent: true).Beats(52, 70, 120);
            var sim = Start(new SimConfig(), Known(song));
            sim.StepTo(48);
            sim.Control(RunControl.Pause);
            sim.StepTo(60);
            Assert.IsTrue(sim.State.Frozen, "still paused after the music's resume");
            Assert.IsFalse(sim.State.FrozenFor(FreezeReason.MusicStopped));
            sim.Control(RunControl.Resume);
            sim.StepTo(70);
            var freezes = Freezes(sim);
            Assert.AreEqual(4, freezes.Count);
            Assert.IsTrue(freezes.Skip(2).All(f => !f.Starts && Math.Abs(f.T - freezes[2].T) < Eps), "both end together, on a beat");
            Assert.Greater(freezes[2].T, 60);
        }

        // ---------------------------------------------------------------- starts

        [Test]
        public void ARunStartsOnTheFirstBeatAfterItsKey()
        {
            var config = new SimConfig { Start = RunStart.OnKey };
            var sim = Start(config, Known(new FixtureSong(3).Beats(0, 30, 120)));
            sim.StepTo(8);
            sim.Press(Key.Up, 7.98);
            sim.StepTo(8.03);
            Assert.AreEqual(RunPhase.Waiting, sim.State.Phase);
            Assert.IsTrue(sim.State.Frozen);
            Assert.AreEqual(0, sim.State.RunClock);
            Assert.AreEqual(0, Moves(sim).Count, "the world is held before the start");
            sim.Release(Key.Up, 8.0);
            sim.Control(RunControl.Start);
            sim.StepTo(12);
            double beat = sim.Stream.Beats.ToArray().Select(b => b.T).First(t => t > 8.03);
            var start = Drive.Events(sim, SimEventKind.RunStart).Single();
            Assert.AreEqual(beat, start.T, Eps);
            Assert.AreEqual(beat, sim.State.StartedAt, Eps);
            Assert.AreEqual(RunPhase.Playing, sim.State.Phase);
            Assert.AreEqual(12 - beat, sim.State.RunClock, 1e-6);
        }

        [Test]
        public void AHeadlessRunStartsAfterTwoSecondsOfClearBeat()
        {
            var config = new SimConfig { Start = RunStart.AfterClearBeat };
            var sim = Start(config, Known(new FixtureSong(3).Gap(0, 5).Beats(5, 30, 120)));
            sim.StepTo(20);
            double reliable = Drive.Events(sim, SimEventKind.BeatState).Select(e => e.AsBeatState)
                .First(e => e.Now == Reliability.Reliable).T;
            double start = Drive.Events(sim, SimEventKind.RunStart).Single().T;
            Assert.AreEqual(sim.Stream.Beats.ToArray().Select(b => b.T).First(t => t >= reliable + 2 - Eps), start, Eps);
            Assert.AreEqual(20 - start, sim.State.RunClock, 1e-6);
        }

        // ---------------------------------------------------------------- victory

        [Test]
        public void VictoryComesAtExactlyThirtyMinutesOfRunClockWithTheFinaleOnTheNextDownbeat()
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            var sim = Start(config, Known(new FixtureSong(3).Beats(0, 1830, 120)));
            sim.StepTo(600);
            sim.Control(RunControl.Pause);
            sim.StepTo(610);
            sim.Control(RunControl.Resume);
            double resumed = sim.Stream.Beats.ToArray().Select(b => b.T).First(t => t > 610);
            double victory = resumed + 1200; // 600 s played before the pause
            sim.StepTo(victory - 0.01);
            Assert.AreEqual(RunPhase.Playing, sim.State.Phase);
            Assert.AreEqual(1799.99, sim.State.RunClock, 0.01);
            sim.StepTo(victory + 0.01);
            Assert.AreEqual(RunPhase.Finale, sim.State.Phase);
            Assert.AreEqual(1800, sim.State.RunClock, "exactly 30:00");

            int skeleton = HordeFixture.Place(sim, "skeleton", sim.State.Hero.X + 20, sim.State.Hero.Y);
            sim.StepTo(1830);
            var finale = Drive.Events(sim, SimEventKind.Finale).Single().AsFinale;
            Assert.AreEqual(Beats(sim).First(b => b.T >= victory && b.Downbeat).T, finale.T, Eps);
            Assert.IsTrue(Drive.Events(sim, SimEventKind.Death).Any(e => e.AsDeath.Enemy == skeleton && Math.Abs(e.T - finale.T) < Eps),
                "the blast clears the screen");
            var end = Drive.Events(sim, SimEventKind.RunEnd).Single().AsRunEnd;
            Assert.AreEqual(RunEnd.Victory, end.Why);
            Assert.AreEqual(finale.T + HordeFixture.Content.Schedule.Finale.VictoryCardS, end.T, Eps);
            Assert.AreEqual(RunPhase.Ended, sim.State.Phase);
            Assert.AreEqual(1800, sim.State.RunClock);
            Assert.IsFalse(Drive.Events(sim, SimEventKind.Attack).Any(e => e.T > finale.T + Eps), "the hero holds");
        }

        // ---------------------------------------------------------------- ends

        [TestCase(RunControl.Quit, RunEnd.Quit)]
        [TestCase(RunControl.DeviceChanged, RunEnd.DeviceChanged)]
        public void AnEndByHandStopsTheRunClock(RunControl control, RunEnd why)
        {
            var sim = Start(new SimConfig(), Known(new FixtureSong(3).Beats(0, 20, 120)));
            sim.StepTo(10);
            sim.Control(control);
            sim.StepTo(15);
            var end = Drive.Events(sim, SimEventKind.RunEnd).Single().AsRunEnd;
            Assert.AreEqual(why, end.Why);
            Assert.AreEqual(10, end.T, Eps);
            Assert.AreEqual(RunPhase.Ended, sim.State.Phase);
            Assert.AreEqual(why, sim.State.End);
            Assert.AreEqual(10, sim.State.RunClock, 1e-6);
        }

        [Test]
        public void TheHerosFallEndsTheRun()
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], sword: 1);
            var sim = Start(config, Known(new FixtureSong(3).Beats(0, 20, 120)));
            sim.StepTo(4);
            for (int k = 0; k < 8; k++) HordeFixture.Place(sim, "minotaur", sim.State.Hero.X + 1.2 * Math.Cos(k), sim.State.Hero.Y + 1.2 * Math.Sin(k));
            sim.StepTo(20);
            var fell = Drive.Events(sim, SimEventKind.Death).Single(e => e.AsDeath.Enemy == -1);
            var end = Drive.Events(sim, SimEventKind.RunEnd).Single().AsRunEnd;
            Assert.AreEqual(RunEnd.Death, end.Why);
            Assert.AreEqual(fell.T, end.T, Eps);
            Assert.AreEqual(fell.T, sim.State.RunClock, 1e-6);
        }

        // ---------------------------------------------------------------- replay

        [Test]
        public void ARunsControlsReplayToItsHash()
        {
            var song = new FixtureSong(3).Beats(0, 40, 120).Gap(40, 52, silent: true).Beats(52, 80, 120);
            var known = Known(song);
            var config = HordeFixture.Config(minimum: 60, sword: 1);
            config.Start = RunStart.OnKey;
            var live = Start(config, known);
            void At(double t, RunControl c)
            {
                live.StepTo(t);
                live.Control(c);
            }
            At(3, RunControl.Start);
            At(20, RunControl.Pause);
            At(23, RunControl.Resume);
            At(30, RunControl.Watchdog);
            live.StepTo(80);
            Assert.AreEqual(4, live.Controls.Length);
            Assert.GreaterOrEqual(Freezes(live).Count, 6);

            config = HordeFixture.Config(minimum: 60, sword: 1);
            config.Start = RunStart.OnKey;
            var replay = Start(config, known);
            foreach (var c in live.Controls.ToArray())
            {
                replay.StepTo(c.Effective);
                replay.Control(c.Control);
            }
            replay.StepTo(80);
            Assert.AreEqual(live.Hash, replay.Hash);
        }

        // ---------------------------------------------------------------- Easy

        [Test]
        public void OnEasyAChickenHealsTheHeroThatReachesIt()
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            config.Easy = true;
            var sim = Start(config, Known(new FixtureSong(3).Beats(0, 20, 120)));
            sim.StepTo(4);
            var hero = sim.State.Hero;
            hero.Health = 50;
            sim.Chickens.Drop(sim, sim.Now, hero.X + 0.5, hero.Y, 1);
            sim.Chickens.Drop(sim, sim.Now, hero.X + 6, hero.Y, 2); // a boss's two, out of reach
            sim.StepTo(5);
            var drops = Drive.Events(sim, SimEventKind.Drop).Select(e => e.AsDrop).Where(d => d.ItemKind == ItemKind.Chicken).ToList();
            Assert.AreEqual(3, drops.Count);
            Assert.AreEqual(FloorChickens.SpreadM, drops[2].X - drops[1].X, 1e-9);
            var pickup = Drive.Events(sim, SimEventKind.Pickup).Single().AsPickup;
            Assert.AreEqual((int)ItemKind.Chicken, pickup.ItemKind);
            Assert.AreEqual(FloorChickens.Heal, pickup.Value, 1e-9);
            Assert.AreEqual(80, hero.Health, 1e-9);
            Assert.AreEqual(2, sim.Chickens.Count);
        }

        [Test]
        public void OnEasyDyingBrutesSometimesDropAChickenAndOffEasyNone()
        {
            int Drops(bool easy)
            {
                var config = HordeFixture.Config(minimum: 0, roster: new string[0]);
                config.Easy = easy;
                var sim = Start(config, Known(new FixtureSong(3).Beats(0, 10, 120)));
                sim.StepTo(4);
                int brute = Array.FindIndex(HordeFixture.Content.Roles, r => r.Brute);
                for (int k = 0; k < 400; k++)
                {
                    int id = HordeFixture.Place(sim, HordeFixture.Content.Roles[brute].Id, 30, 30);
                    sim.Horde.Kill(sim, id, sim.Now, 1, 0, 0);
                }
                return Drive.Events(sim, SimEventKind.Drop).Count(e => e.AsDrop.ItemKind == ItemKind.Chicken);
            }
            Assert.That(Drops(true), Is.InRange(8, 40), "5% of 400");
            Assert.AreEqual(0, Drops(false));
        }
    }
}
