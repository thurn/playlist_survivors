using System;
using System.Collections.Generic;
using System.IO;
using Game.Client.Hero;
using Game.Client.Loop;
using Game.Client.Music;
using NUnit.Framework;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using SimKey = PS.Game.Sim.Key;

namespace Game.Tests
{
    /// <summary>The client's frame loop: its clocks, the state history, event dispatch and the fixed-time run.</summary>
    public class LoopTests
    {
        const double Fps = 60;

        /// <summary>A clock 5 s of DSP time ahead of real time, sampled each frame for `seconds`, as MusicPlayer samples it.</summary>
        static MusicClock Sampled(double seconds, double outputLatency = 0.038)
        {
            var clock = new MusicClock { OutputLatency = outputLatency, DisplayLatency = 1 / Fps };
            clock.SetAnchor(dsp: 5.0, song: 0.0);
            for (int i = 0; i <= seconds * Fps; i++) clock.Sample(i / Fps, 5.0 + i / Fps);
            return clock;
        }

        [Test]
        public void TheFrontierRidesTheMixerByTheMarginAndLeadsTheHeardSongByLatencyPlusMargin()
        {
            const double buffer = 256 / 48000.0;
            var clock = Sampled(3);
            var times = new MusicTimes(clock, frame: 1 / Fps, dspBuffer: buffer, dspNow: () => 8.0);
            var t = times.Frame(new ClockReadings { Real = 3.0 });

            Assert.That(t.Song, Is.EqualTo(3.0).Within(1e-9)); // songAtDsp(dspAt(now))
            Assert.That(t.Heard, Is.EqualTo(3.0 - 0.038).Within(1e-9));
            Assert.That(times.Margin, Is.EqualTo(2 / Fps + Simulation.TickSeconds + 2 * buffer).Within(1e-12));
            Assert.That(t.Frontier, Is.EqualTo(t.Song + times.Margin).Within(1e-12));
            Assert.That(t.Lead, Is.EqualTo(0.038 + times.Margin).Within(1e-9));
            Assert.That(t.FrameSong, Is.EqualTo(clock.HeardSong(3.0 + 1 / Fps)).Within(1e-12));

            // Frames longer than a refresh widen the margin to two of them.
            for (int i = 0; i < 200; i++) times.Measure(1 / 30.0);
            Assert.That(times.Margin, Is.EqualTo(2 / 30.0 + Simulation.TickSeconds + 2 * buffer).Within(1e-6));
            times.Measure(2.0); // a hitch is not a frame rate
            Assert.That(times.Margin, Is.EqualTo(2 / 30.0 + Simulation.TickSeconds + 2 * buffer).Within(1e-6));
        }

        [Test]
        public void AKeyIsHeardAtItsTimestampLessTheInputCalibration()
        {
            var clock = Sampled(3);
            clock.InputCalibration = 0.012;
            var times = new MusicTimes(clock, 1 / Fps, 0.005, () => 8.0);
            Assert.That(times.KeySong(2.5), Is.EqualTo(clock.HeardSong(2.5) - 0.012).Within(1e-12));
            Assert.That(times.KeySong(2.5), Is.EqualTo(2.5 - 0.038 - 0.012).Within(1e-9));
        }

        [Test]
        public void SoundsAreScheduledAtTheirSongTimesDspTimeAndDroppedOnceTheMixerIsPast()
        {
            var clock = Sampled(3);
            double dspNow = 8.0;
            var times = new MusicTimes(clock, 1 / Fps, dspBuffer: 0.005, () => dspNow);
            Assert.That(times.TryDspAt(3.1, out double dsp), Is.True);
            Assert.That(dsp, Is.EqualTo(8.1).Within(1e-12));
            Assert.That(times.TryDspAt(3.004, out _), Is.False, "a sound needs a buffer past the DSP clock's reading");
            Assert.That(times.TryDspAt(3.006, out _), Is.True);
            dspNow = 8.01; // the reading when the sound is scheduled counts, not the frame's
            Assert.That(times.TryDspAt(3.006, out _), Is.False);
            Assert.That(new FrameClock().TryDspAt(3.1, out _), Is.False, "no music plays in time with fixed frames");
        }

        /// <summary>
        /// The lead's purpose (unity-game.md, The Sim Lead): the sim settles every event before the
        /// mixer needs it, so each event a frame dispatches can still be scheduled on its sample.
        /// Frames jitter by ±2 ms; the DSP clock reads the end of the buffer being mixed, stepping a
        /// buffer at a time, and MusicPlayer samples it half a buffer back.
        /// </summary>
        [Test]
        public void EveryEventAFrameSettlesCanStillBeScheduled(
            [Values(60.0, 120.0)] double fps, [Values(256, 512, 1024)] int bufferFrames)
        {
            double buffer = bufferFrames / 48000.0;
            var random = new System.Random(bufferFrames + (int)fps);
            var clock = new MusicClock { OutputLatency = 0.017 + 4 * buffer, DisplayLatency = 1 / fps };
            clock.SetAnchor(dsp: 5.0, song: 0.0);
            double dspNow = 0;
            var times = new MusicTimes(clock, 1 / fps, buffer, () => dspNow);
            var driver = new SimDriver(new SimConfig(), times);
            int unschedulable = 0, frames = 0;
            for (int i = 0; i < 8 * fps; i++)
            {
                double real = i / fps + (random.NextDouble() - 0.5) * 0.004;
                dspNow = Math.Ceiling((5.0 + real) / buffer) * buffer;
                clock.Sample(real, dspNow - buffer / 2);
                times.Measure(1 / fps);
                double settledAfter = driver.Sim.Now;
                driver.Step(new ClockReadings { Real = real });
                if (i < 2 * fps) continue; // the fit's first 2 s
                frames++;
                // The earliest event this frame can settle is just past the previous frontier.
                if (!times.TryDspAt(settledAfter + 1e-9, out _)) unschedulable++;
            }
            Assert.That(unschedulable, Is.Zero, $"of {frames} frames, the earliest settled event was already too late to schedule");
        }

        [Test]
        public void TheDspFitAveragesFrameJitterAndForgetsReadingsOlderThanItsWindow()
        {
            const double buffer = 256 / 48000.0;
            var clock = new MusicClock();
            var random = new System.Random(7);
            // 2 s on a clock 1 s off, then 3 s of frames with ±4 ms jitter, the DSP clock in whole buffers.
            for (int i = 0; i < 120; i++) clock.Sample(i / Fps, 1.0 + i / Fps);
            double real = 0;
            for (int i = 120; i < 300; i++)
            {
                real = i / Fps + (random.NextDouble() - 0.5) * 0.008;
                clock.Sample(real, 5.0 + Math.Floor(real / buffer) * buffer + buffer / 2);
            }
            // The steps, read half a buffer back, average to the continuous clock.
            Assert.That(clock.DspAt(real), Is.EqualTo(5.0 + real).Within(buffer / 4));
            Assert.That(clock.DspAt(real - 0.5), Is.EqualTo(5.0 + real - 0.5).Within(buffer / 4));
        }

        [Test]
        public void TheFrameClockIsFrameExact()
        {
            var clock = new FrameClock();
            for (int n = 0; n < 600; n += 37)
            {
                var t = clock.Frame(new ClockReadings { Real = 123.4 + n, Frame = n / Fps });
                Assert.That(t.FrameSong, Is.EqualTo(n / Fps));
                Assert.That(t.Frontier, Is.EqualTo(FrameClock.FrontierAt(n / Fps)));
                Assert.That(t.Lead, Is.EqualTo(FrameClock.OutputLatency + FrameClock.Margin).Within(1e-12));
                Assert.That(clock.KeySong(999), Is.EqualTo(n / Fps));
            }
        }

        /// <summary>A driver on fixed frames, stepped frame by frame from `first`.</summary>
        internal static SimDriver FixedDriver(out ClockReadings[] frames, int count, int first = 0)
        {
            frames = new ClockReadings[count];
            for (int i = 0; i < count; i++) frames[i] = new ClockReadings { Frame = (first + i) / Fps };
            return new SimDriver(new SimConfig(), new FrameClock());
        }

        [Test]
        public void TheHistoryInterpolatesBetweenFrontiersAndFlagsTimesPastTheFrontier()
        {
            var driver = FixedDriver(out var frames, 120);
            for (int i = 0; i < 60; i++) driver.Step(frames[i]);
            driver.Sim.Press(SimKey.Right, driver.Sim.Now); // a tap: one stride on the next step
            driver.Sim.Release(SimKey.Right, driver.Sim.Now);
            for (int i = 60; i < 120; i++) driver.Step(frames[i]);
            var history = driver.History;
            Assert.That(history.Count, Is.EqualTo(120));

            // The run clock runs with song time, so between two frontiers it interpolates to it.
            for (double t = 0.2; t < history.Latest.Now; t += 0.0371)
            {
                Assert.That(history.Sample(t, out var s), Is.True);
                Assert.That(s.RunClock, Is.EqualTo(t).Within(1e-9));
            }
            // The stride: before its start the hero stands on its first tile; from it on, it strides.
            var hero = history.Latest.Hero;
            Assert.That(hero.TileX, Is.EqualTo(driver.Sim.Arena.HeroStart.X + 1));
            history.Sample(hero.MoveStart - 1e-6, out var before);
            history.Sample(hero.MoveStart + 1e-6, out var after);
            Assert.That(before.Hero.TileX, Is.EqualTo(hero.TileX - 1));
            Assert.That(after.Hero.TileX, Is.EqualTo(hero.TileX));
            after.Hero.PositionAt(hero.MoveStart, out double x0, out _);
            after.Hero.PositionAt(hero.MoveEnd, out double x1, out _);
            Assert.That(x0, Is.EqualTo(hero.FromX));
            Assert.That(x1, Is.EqualTo(hero.X));

            Assert.That(history.Sample(history.Latest.Now + 0.01, out var past), Is.False);
            Assert.That(past.Now, Is.EqualTo(history.Latest.Now));
        }

        [Test]
        public void TheHistoryKeepsItsNewestSnapshotsAndAnswersBeforeThemWithTheOldest()
        {
            var driver = FixedDriver(out var frames, StateHistory.Capacity + 50);
            foreach (var f in frames) driver.Step(f);
            var history = driver.History;
            Assert.That(history.Count, Is.EqualTo(StateHistory.Capacity));
            Assert.That(history.Sample(0, out var oldest), Is.True);
            Assert.That(oldest.Now, Is.EqualTo(Math.Floor(FrameClock.FrontierAt(50 / Fps) * Simulation.TicksPerSecond + 1e-9) / Simulation.TicksPerSecond).Within(1e-12));
        }

        sealed class Recorder : IPresenter
        {
            public readonly List<SimEvent> Events = new List<SimEvent>();
            public readonly List<PresentFrame> Frames = new List<PresentFrame>();
            public void OnEvent(in SimEvent e) => Events.Add(e);
            public void Present(in PresentFrame frame) => Frames.Add(frame);
        }

        internal static List<StreamEntry> House128()
        {
            var entries = new List<StreamEntry>();
            foreach (var line in File.ReadLines(Path.GetFullPath("../native/fixtures/entries/house-128-20s.jsonl")))
            {
                var row = JsonUtility.FromJson<DumpRow>(line);
                if (row.kind == 0) continue; // the header
                var e = new StreamEntry { Kind = (EntryKind)row.kind, T = row.t, Stamp = row.known_at };
                for (int i = 0; i < row.v.Length; i++) e[i] = row.v[i];
                entries.Add(e);
            }
            return entries;
        }

        [Serializable]
        struct DumpRow
        {
            public int kind;
            public double t, known_at;
            public double[] v;
        }

        /// <summary>
        /// Runs house-128 on fixed frames, handing entries over as the fixed-time loop does: before
        /// each frame, every entry whose stamp the frontier needs, plus `extra(frame)` more.
        /// </summary>
        static (List<ulong> hashes, List<SimEvent> events) FixedRun(List<StreamEntry> entries, Func<int, int> extra, int frameCount)
        {
            var driver = FixedDriver(out var frames, frameCount);
            var recorder = new Recorder();
            driver.Events.Subscribe(recorder);
            var hashes = new List<ulong>();
            int next = 0;
            for (int f = 0; f < frameCount; f++)
            {
                double needed = driver.StampNeededFor(FrameClock.FrontierAt(frames[f].Frame));
                while (next < entries.Count && entries[next].Stamp <= needed) driver.AddSettled(entries[next++]);
                for (int k = extra(f); k > 0 && next < entries.Count; k--) driver.AddSettled(entries[next++]);
                driver.Step(frames[f]);
                hashes.Add(driver.Sim.Hash);
                Assert.That(recorder.Frames[f].Song, Is.EqualTo(frames[f].Frame));
            }
            Assert.That(driver.LateEntries, Is.Zero);
            return (hashes, recorder.Events);
        }

        [Test]
        public void AFixedTimeRunIsFrameExactHoweverTheEntriesArrive()
        {
            var entries = House128();
            const int frameCount = 15 * 60;
            var (justInTime, events) = FixedRun(entries, f => 0, frameCount);
            var random = new System.Random(3);
            var bursts = FixedRun(entries, f => random.Next(4) == 0 ? random.Next(200) : 0, frameCount).hashes;
            var early = FixedRun(entries, f => f == 0 ? entries.Count : 0, frameCount).hashes;

            Assert.That(bursts, Is.EqualTo(justInTime));
            Assert.That(early, Is.EqualTo(justInTime));
            Assert.That(events.FindAll(e => e.Kind == SimEventKind.Beat).Count, Is.GreaterThan(20), "house-128's beats reached the sim");
        }

        [Test]
        public void EverySettledEventIsDispatchedOnceInOrderAndFreedFromTheSim()
        {
            var entries = House128();
            var driver = FixedDriver(out var frames, 600);
            var recorder = new Recorder();
            driver.Events.Subscribe(recorder);
            foreach (var e in entries) driver.AddSettled(e);
            var direct = new Simulation(new SimConfig());
            foreach (var e in entries) direct.AddKnown(e, Math.Max(e.Stamp + 0.5, 0));
            foreach (var f in frames)
            {
                driver.Step(f);
                Assert.That(recorder.Frames[recorder.Frames.Count - 1].Frontier, Is.EqualTo(driver.Sim.Now));
                Assert.That(recorder.Events.Count, Is.EqualTo(driver.Sim.EventCount));
                Assert.That(driver.Events.Cursor, Is.EqualTo(driver.Sim.EventCount));
                Assert.That(driver.Sim.EventsSince(driver.Events.Cursor).Length, Is.Zero);
            }
            direct.StepTo(driver.Sim.Now);
            Assert.That(recorder.Events.ToArray(), Is.EqualTo(direct.EventsSince(0).ToArray()));
            Assert.Throws<ArgumentOutOfRangeException>(() => driver.Sim.EventsSince(0), "dispatched events are freed");
            for (int i = 1; i < recorder.Events.Count; i++)
                Assert.That(recorder.Events[i].T, Is.GreaterThanOrEqualTo(recorder.Events[i - 1].T));
        }

        [Test]
        public void ScriptedEventsAreDispatchedWhenTheFrontierReachesThemInTimeOrder()
        {
            var scripted = ScriptedEvents.Parse(new[]
            {
                "# a synthetic feed",
                "{\"kind\": \"Spawn\", \"t\": 0.5, \"i\": [3, 1], \"d\": [2.0, -4.5]}",
                "{\"kind\": \"EnemyStep\", \"t\": 0.25, \"i\": [7], \"d\": [0, 0, 1.5, 0, 0.25, 0.5]}",
                "",
                "{\"kind\": \"Death\", \"t\": 0.5, \"i\": [3]}",
            });
            var driver = FixedDriver(out var frames, 60);
            driver.Scripted = scripted;
            var recorder = new Recorder();
            driver.Events.Subscribe(recorder);
            var seen = new List<(SimEventKind kind, double frontier)>();
            foreach (var f in frames)
            {
                int before = recorder.Events.Count;
                driver.Step(f);
                for (int i = before; i < recorder.Events.Count; i++)
                    if (recorder.Events[i].Kind != SimEventKind.StrideStep && recorder.Events[i].Kind != SimEventKind.HeroStep)
                        seen.Add((recorder.Events[i].Kind, driver.Sim.Now));
            }
            Assert.That(seen.ConvertAll(s => s.kind), Is.EqualTo(new[] { SimEventKind.EnemyStep, SimEventKind.Spawn, SimEventKind.Death }));
            Assert.That(seen[0].frontier, Is.GreaterThanOrEqualTo(0.25).And.LessThan(0.25 + 1 / Fps + 1e-9));
            Assert.That(seen[1].frontier, Is.GreaterThanOrEqualTo(0.5).And.LessThan(0.5 + 1 / Fps + 1e-9));
            var spawn = recorder.Events.Find(e => e.Kind == SimEventKind.Spawn).AsSpawn;
            Assert.That((spawn.Enemy, spawn.Role, spawn.X, spawn.Y), Is.EqualTo((3, 1, 2.0, -4.5)));
            var step = recorder.Events.Find(e => e.Kind == SimEventKind.EnemyStep).AsEnemyStep;
            Assert.That((step.ToX, step.Footplant), Is.EqualTo((1.5, 0.5)));
            Assert.Throws<FormatException>(() => ScriptedEvents.Parse(new[] { "{\"kind\": \"Explosion\", \"t\": 1}" }));
        }

        [Test]
        public void AFrameAllocatesNothing()
        {
            var entries = House128();
            var driver = FixedDriver(out var frames, 300);
            driver.Events.Subscribe(new Recorder2());
            foreach (var e in entries) driver.AddSettled(e);
            for (int i = 0; i < 200; i++) driver.Step(frames[i]);
            int f = 200;
            Assert.That(() => { driver.Step(frames[f++]); }, Is.Not.AllocatingGCMemory());
        }

        sealed class Recorder2 : IPresenter
        {
            public int Count;
            public void OnEvent(in SimEvent e) => Count++;
            public void Present(in PresentFrame frame) { }
        }
    }

    /// <summary>Keys from the Input System into the sim, through the clock's keySong, and on to the drawn hero.</summary>
    public class KeyInputTests : InputTestFixture
    {
        /// <summary>Draws the hero as <see cref="HeroPresenter"/> does, without the knight.</summary>
        sealed class DrawnHero : IPresenter
        {
            readonly HeroMotion _motion = new HeroMotion(HeroClips.Parse(File.ReadAllText(HeroClips.ContentPath)));
            public HeroPose Pose;
            public void OnEvent(in SimEvent e) => _motion.OnEvent(e);

            public void Present(in PresentFrame frame)
            {
                var hero = frame.State.Hero;
                hero.PositionAt(frame.Song, out double x, out double y);
                Pose = _motion.Evaluate(frame.Song, x, y, hero.Facing);
            }
        }

        const int First = 5 * 60, Every = 37, Presses = 24;

        /// <summary>
        /// House-128 on fixed frames, the sim running its lead ahead, with the hero drawn: before frame
        /// f's keys reach the sim, `keysAt(f)` presses and releases them through the Input System;
        /// after it steps, `drawnAt(f, pose)`. The snapshot the placeholder draws agrees every frame.
        /// </summary>
        void Run(int frameCount, Action<Keyboard, int> keysAt, Action<int, HeroPose> drawnAt)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var entries = LoopTests.House128();
            var driver = LoopTests.FixedDriver(out var frames, frameCount);
            var drawn = new DrawnHero();
            driver.Events.Subscribe(drawn);
            using (var keys = new KeyInput())
            {
                int next = 0;
                for (int f = 0; f < frames.Length; f++)
                {
                    double needed = driver.StampNeededFor(FrameClock.FrontierAt(frames[f].Frame));
                    while (next < entries.Count && entries[next].Stamp <= needed) driver.AddSettled(entries[next++]);
                    keysAt(keyboard, f);
                    keys.Drain(driver.Sim, driver.Clock);
                    driver.Step(frames[f]);
                    driver.Presented.State.Hero.PositionAt(driver.Presented.Song, out double sx, out double sy);
                    Assert.That((sx, sy), Is.EqualTo((drawn.Pose.X, drawn.Pose.Y)), $"frame {f}: the snapshot and the drawn hero agree");
                    drawnAt(f, drawn.Pose);
                }
            }
        }

        /// <summary>
        /// The hero answers an arrow key on the frame it goes down (ps-msz.4.3): 100 ms taps at 24
        /// phases across the stride period, right and left in turn, through the Input System, the frame
        /// loop and the hero's motion. Each moves the drawn hero on its key-down frame or the next,
        /// and by one tile.
        /// </summary>
        [Test]
        public void TheDrawnHeroMovesOnTheFrameAnArrowKeyGoesDown()
        {
            const int hold = 6;
            var lags = new List<int>();
            var moved = new List<double>();
            int pressed = -1;
            double x0 = 0, y0 = 0, x = 0, y = 0;
            Run(First + Every * Presses + 30, (keyboard, f) =>
            {
                int k = (f - First) / Every;
                var key = k % 2 == 0 ? keyboard.rightArrowKey : keyboard.leftArrowKey;
                if (f >= First && (f - First) % Every == 0 && k < Presses)
                {
                    if (k > 0) moved.Add(Math.Abs(x - x0) + Math.Abs(y - y0));
                    Press(key);
                    pressed = f;
                    x0 = x;
                    y0 = y;
                }
                if (f >= First && (f - First) % Every == hold && k < Presses) Release(key);
            }, (f, pose) =>
            {
                x = pose.X;
                y = pose.Y;
                if (pressed < 0 || (x == x0 && y == y0)) return;
                lags.Add(f - pressed);
                pressed = -1;
            });
            moved.Add(Math.Abs(x - x0) + Math.Abs(y - y0));
            TestContext.WriteLine("frames from key-down to the drawn hero moving: " + string.Join(" ", lags));
            Assert.That(lags.Count, Is.EqualTo(Presses), "every tap moved the hero");
            Assert.That(lags, Is.All.LessThanOrEqualTo(1));
            Assert.That(moved, Is.All.EqualTo(1.5).Within(1e-9), "a tap moves one tile");
        }

        /// <summary>
        /// Turning while a key is held answers on the key-down frame too: Up held, then Right, then Up
        /// again, each turn releasing the other key, at 24 phases across the stride period. The drawn
        /// hero sets off along the new axis on the turn's key-down frame or the next.
        /// </summary>
        [Test]
        public void TheDrawnHeroTurnsOnTheFrameTheTurnsKeyGoesDown()
        {
            var lags = new List<int>();
            int turned = -1;
            bool right = false;
            double along = 0;
            Run(First + Every * Presses + 30, (keyboard, f) =>
            {
                if (f == First - Every) Press(keyboard.upArrowKey);
                if (f < First || (f - First) % Every != 0 || (f - First) / Every >= Presses) return;
                right = !right;
                Release(right ? keyboard.upArrowKey : keyboard.rightArrowKey);
                Press(right ? keyboard.rightArrowKey : keyboard.upArrowKey);
                turned = f;
            }, (f, pose) =>
            {
                double now = right ? pose.X : pose.Y;
                if (f == turned - 1 || turned < 0) along = now;
                if (turned < 0 || f < turned || now == along) return;
                lags.Add(f - turned);
                turned = -1;
            });
            TestContext.WriteLine("frames from a turn's key-down to the drawn hero turning: " + string.Join(" ", lags));
            Assert.That(lags.Count, Is.EqualTo(Presses), "every turn turned the hero");
            Assert.That(lags, Is.All.LessThanOrEqualTo(1));
        }

        sealed class Clock : IGameClock
        {
            public bool Ready => true;
            public FrameTimes Frame(in ClockReadings now) => default;
            public double KeySong(double real) => 2 * real - 1; // any mapping will do; this one is easy to spot
            public bool TryDspAt(double song, out double dsp) { dsp = 0; return false; }
        }

        [Test]
        public void InjectedKeysReachTheSimHeardAtTheirTimestamps()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            using (var keys = new KeyInput())
            {
                var sim = new Simulation(new SimConfig());
                Press(keyboard.upArrowKey, time: 10.25);
                Press(keyboard.wKey, time: 10.5); // a second key on Up: no second press
                Press(keyboard.spaceKey, time: 10.75);
                Release(keyboard.upArrowKey, time: 11.0);
                Release(keyboard.wKey, time: 11.25); // both up: Up is released
                Press(keyboard.f3Key, time: 11.5); // not the sim's
                Assert.That(keys.Drain(sim, new Clock()), Is.EqualTo(3));
                var k = sim.Keys.ToArray();
                Assert.That(k.Length, Is.EqualTo(3));
                Assert.That((k[0].Key, k[0].Down, k[0].Heard), Is.EqualTo((SimKey.Up, true, 19.5)));
                Assert.That((k[1].Key, k[1].Down, k[1].Heard), Is.EqualTo((SimKey.Space, true, 20.5)));
                Assert.That((k[2].Key, k[2].Down, k[2].Heard), Is.EqualTo((SimKey.Up, false, 21.5)));
                Assert.That(k[0].Effective, Is.EqualTo(sim.Now));
                Assert.That(keys.Drain(sim, new Clock()), Is.Zero);
            }
        }
    }
}
