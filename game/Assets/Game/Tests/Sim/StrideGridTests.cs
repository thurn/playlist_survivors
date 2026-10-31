using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The stride grid: halvings, pace bands, hysteresis, the free pulse (unity-game.md, "Key
    /// Presses and Late Steps" and "Speeds Follow the Tempo").
    /// </summary>
    public class StrideGridTests
    {
        static List<StrideStepEvent> Steps(Simulation sim)
        {
            var list = new List<StrideStepEvent>();
            foreach (var e in Drive.Events(sim, SimEventKind.StrideStep)) list.Add(e.AsStrideStep);
            return list;
        }

        static Dictionary<int, BeatEvent> BeatsByIndex(Simulation sim)
        {
            var map = new Dictionary<int, BeatEvent>();
            foreach (var e in Drive.Events(sim, SimEventKind.Beat)) map[e.AsBeat.Index] = e.AsBeat;
            return map;
        }

        static Simulation Run(FixtureSong song) => Drive.Replay(Drive.Known(song.Entries()), song.End);

        /// <summary>The grid steps where the halvings changed from the previous grid step's.</summary>
        static List<StrideStepEvent> Switches(List<StrideStepEvent> steps)
        {
            var switches = new List<StrideStepEvent>();
            int h = -1;
            foreach (var s in steps)
            {
                if (s.Free) continue;
                if (h >= 0 && s.Halvings != h) switches.Add(s);
                h = s.Halvings;
            }
            return switches;
        }

        static void AssertSwitchesOnDownbeats(Simulation sim)
        {
            var beats = BeatsByIndex(sim);
            foreach (var s in Switches(Steps(sim)))
                Assert.IsTrue(beats[s.Beat].Downbeat, $"switch at {s.T} on beat {s.Beat}, not a downbeat");
        }

        [TestCase(149.0, 1)]
        [TestCase(151.0, 0)]
        [TestCase(150.0, 0)]
        [TestCase(60.0, 2)]
        [TestCase(100.0, 1)]
        [TestCase(200.0, 0)]
        public void TheFewestHalvingsBringTheStepTo0Point4OrLess(double bpm, int halvings)
        {
            Assert.AreEqual(halvings, StrideGrid.FewestHalvings(60.0 / bpm));
        }

        [TestCase(149.0, 0.2013)]
        [TestCase(151.0, 0.3974)]
        public void PaceBands(double bpm, double stride)
        {
            // A 149 BPM song strides every 0.20 s; a 151 BPM song every 0.40 s.
            var steps = Steps(Run(new FixtureSong().Beats(0, 30, bpm)));
            Assert.Greater(steps.Count, 60);
            for (int i = 1; i < steps.Count - 1; i++)
            {
                Assert.IsFalse(steps[i].Free);
                Assert.AreEqual(stride, steps[i].Period, 1e-4);
                Assert.AreEqual(stride, steps[i + 1].T - steps[i].T, 1e-4);
            }
        }

        [Test]
        public void WithNoBeatTheGridIsAFreePulse()
        {
            var sim = Run(new FixtureSong().Gap(0, 10));
            var steps = Steps(sim);
            Assert.AreEqual(28, steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                Assert.IsTrue(steps[i].Free);
                Assert.AreEqual(0.35 * (i + 1), steps[i].T, 1e-9);
                Assert.AreEqual(0.35, steps[i].Period, 1e-12);
            }
        }

        [Test]
        public void TheGridJoinsTheBeatAndLeavesItAtAGap()
        {
            var sim = Run(new FixtureSong().Gap(0, 3).Beats(3, 20, 120).Gap(20, 25));
            var steps = Steps(sim);
            // Free to 2.8, then the beat at 3.0 in quarter-second steps (120 BPM halved once).
            int join = steps.FindIndex(s => !s.Free);
            Assert.AreEqual(3.0, steps[join].T, 1e-9);
            Assert.AreEqual(2.8, steps[join - 1].T, 1e-9);
            Assert.AreEqual(1, steps[join].Halvings);
            Assert.AreEqual(3.25, steps[join + 1].T, 1e-9);
            // The last beat (19.5) has no interval after it: it continues for 1.5 periods, to 20.25,
            // and then the free pulse takes over.
            int leave = steps.FindIndex(join, s => s.Free);
            Assert.AreEqual(20.25, steps[leave - 1].T, 1e-9);
            Assert.AreEqual(1, steps[leave - 1].Halvings);
            Assert.AreEqual(20.6, steps[leave].T, 1e-9);
            for (int i = 1; i < steps.Count; i++)
                Assert.GreaterOrEqual(steps[i].T - steps[i - 1].T, 0.5 * steps[i].Period - 1e-9, $"steps {i - 1} and {i} too close");
        }

        [Test]
        public void ABeatKnownLateKeepsThePace()
        {
            // Hindsight commits beats in batches, often less than a beat ahead: here each beat is
            // known only 0.1 s after the one before it fires. The grid steps on through each beat and
            // the beat never counts as missing.
            var known = Drive.Known(new FixtureSong().Beats(0, 20, 120).Entries());
            for (int i = 0; i < known.Count; i++)
                if (known[i].Entry.Kind == EntryKind.Beat && known[i].Entry.T > 1)
                    known[i] = new KnownEntry(known[i].Entry, known[i].Entry.T - 0.4);
            var sim = Drive.Replay(known, 19.4);
            var steps = Steps(sim);
            for (int i = 1; i < steps.Count; i++)
            {
                Assert.IsFalse(steps[i].Free, $"step at {steps[i].T}");
                Assert.AreEqual(0.25, steps[i].T - steps[i - 1].T, 1e-9, $"step at {steps[i].T}");
            }
            foreach (var e in Drive.Events(sim, SimEventKind.BeatState))
                Assert.AreNotEqual(Reliability.NoBeatAhead, e.AsBeatState.Now, $"at {e.T}");
        }

        [Test]
        public void ARealSongStridesOnEveryHalfBeat()
        {
            // house-128's committed beats arrive in hindsight batches, some less than a beat ahead.
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var sim = new Simulation(new SimConfig());
            Drive.Feed(sim, entries, duration);
            Assert.AreEqual(0, sim.Stream.Dropped);
            Assert.AreEqual(0, sim.Beats.Missed);
            var steps = Steps(sim);
            Assert.Greater(steps.Count, 80);
            // After the analyzer's first, unsteady beats (0.28 s, then 0.44 s apart): 128 BPM halved once.
            foreach (var s in steps)
                if (s.T > 1)
                {
                    Assert.AreEqual(1, s.Halvings, $"step at {s.T}");
                    Assert.That(s.Period, Is.InRange(0.22, 0.25), $"step at {s.T}");
                }
            for (int i = 1; i < steps.Count; i++)
                if (steps[i].T > 1) Assert.That(steps[i].T - steps[i - 1].T, Is.InRange(0.22, 0.25), $"step at {steps[i].T}");
            foreach (var e in Drive.Events(sim, SimEventKind.BeatState))
                if (e.T > 0.5) Assert.AreNotEqual(Reliability.NoBeatAhead, e.AsBeatState.Now, $"at {e.T}");
        }

        [Test]
        public void TheFreePulseYieldsToABeatHalfAStepAway()
        {
            // Free steps at 0.35 and 0.70; beats from 0.75, too close to the step at 0.70, so the
            // grid joins at its second step, 1.0 (0.75 + 0.25).
            var sim = Run(new FixtureSong().Gap(0, 0.75).Beats(0.75, 10, 120));
            var steps = Steps(sim);
            Assert.AreEqual(0.70, steps[1].T, 1e-9);
            Assert.AreEqual(1.0, steps[2].T, 1e-9);
            Assert.IsFalse(steps[2].Free);
        }

        [TestCase(150.0, 7UL)]
        [TestCase(150.0, 8UL)]
        [TestCase(150.0, 9UL)]
        [TestCase(133.33, 7UL)]
        [TestCase(166.67, 7UL)]
        public void ASongHoveringAtABoundaryNeverFlickers(double bpm, ulong seed)
        {
            // Every interval wobbles 4 ms around the boundary; the first one picks the halvings.
            var sim = Run(new FixtureSong(seed).Beats(0, 90, bpm, jitter: 0.004));
            Assert.AreEqual(0, Switches(Steps(sim)).Count);
        }

        [Test]
        public void EnteringTheUpperEdgeSwitchesOnceOnADownbeat()
        {
            // From 152 BPM (unhalved, 0.395 s) to hovering at the 0.45 s edge: once a step leaves the
            // band the grid halves, on a downbeat, and the edge's wobble never undoes it.
            var sim = Run(new FixtureSong(3).Beats(0, 20, 152).Beats(20, 90, 133.33, jitter: 0.004));
            var switches = Switches(Steps(sim));
            Assert.LessOrEqual(switches.Count, 1);
            AssertSwitchesOnDownbeats(sim);
        }

        [TestCase(151.0, 125.0, 0, 1)]
        [TestCase(140.0, 175.0, 1, 0)]
        public void ATempoRampSwitchesOnceAtTheFirstDownbeatOutsideTheBand(double from, double to, int before, int after)
        {
            var song = new FixtureSong().Beats(0, 60, from, to);
            var sim = Run(song);
            var steps = Steps(sim);
            var switches = Switches(steps);
            Assert.AreEqual(1, switches.Count);
            Assert.AreEqual(before, steps[1].Halvings);
            Assert.AreEqual(after, switches[0].Halvings);
            AssertSwitchesOnDownbeats(sim);
            // The first interval whose step leaves the band, at the old halvings.
            var beats = song.BeatTimes();
            int exit = -1;
            for (int i = 0; i + 1 < beats.Count && exit < 0; i++)
            {
                double step = (beats[i + 1] - beats[i]) / (1 << before);
                if (step < StrideGrid.BandLow - 1e-9 || step > StrideGrid.BandHigh + 1e-9) exit = i;
            }
            Assert.That(switches[0].Beat, Is.EqualTo(exit).Or.EqualTo(exit + 1), "the next downbeat at most a beat later");
        }

        [Test]
        public void WithNoLockYetTheGridSwitchesOnAnyBeat()
        {
            // Unclear music never locks, so no beat is a downbeat; the step still leaves the band,
            // and the grid switches on the first interval outside it.
            var song = new FixtureSong().Beats(0, 30, 151, 110, evidence: 0.3);
            var sim = Run(song);
            Assert.AreEqual(0, sim.Beats.Judging.Locks.Length);
            var steps = Steps(sim);
            var switches = Switches(steps);
            Assert.AreEqual(1, switches.Count);
            Assert.AreEqual(1, switches[0].Halvings);
            var beats = song.BeatTimes();
            int exit = beats.FindIndex(t => beats.IndexOf(t) + 1 < beats.Count && beats[beats.IndexOf(t) + 1] - t > StrideGrid.BandHigh + 1e-9);
            Assert.AreEqual(exit, switches[0].Beat);
            foreach (var s in steps)
                if (!s.Free) Assert.That(s.Period, Is.InRange(0.17, 0.46), $"step at {s.T}");
        }

        [Test]
        public void TheStepPeriodStaysWithinTheWidenedRange()
        {
            var sim = Run(new FixtureSong(5).Beats(0, 120, 70, 190, jitter: 0.002));
            foreach (var s in Steps(sim))
                if (!s.Free) Assert.That(s.Period, Is.InRange(0.17, 0.46), $"step at {s.T}");
            AssertSwitchesOnDownbeats(sim);
        }

        [Test]
        public void RecentStepsAreKept()
        {
            var sim = Drive.Replay(Drive.Known(new FixtureSong().Beats(0, 30, 120).Entries()), 29);
            Assert.AreEqual(64, sim.Strides.RecentCount);
            var latest = sim.Strides.Recent(0);
            Assert.AreEqual(sim.Strides.LastStep, latest.T);
            Assert.AreEqual(0.25, sim.Strides.Recent(1).Period, 1e-9);
            Assert.AreEqual(latest.T - 0.25, sim.Strides.Recent(1).T, 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Strides.Recent(64));
        }
    }
}
