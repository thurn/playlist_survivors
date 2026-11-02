using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Known-at causality (plan.md, "The Sim", Shape): the sim never reads an entry before its known-at
    /// time. Entries fed late, dropped or changed after a cut change nothing the sim settled before they
    /// are known.
    /// </summary>
    public class CausalityTests
    {
        static FixtureSong Song(ulong seed) => new FixtureSong(seed)
            .Beats(0, 30, 128, jitter: 0.004, halfOnsets: true)
            .Gap(30, 32, silent: true)
            .Gap(32, 36)
            .Beats(36, 70, 100, 150, jitter: 0.003)
            .Beats(70, 90, 174, evidence: 0.15)
            .Beats(90, 110, 80);

        /// <summary>The same inputs every run: walking, a click target, a pause, at fixed times.</summary>
        static Simulation Run(List<KnownEntry> known, double until)
        {
            var sim = new Simulation(new SimConfig());
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            var script = new (double At, Action<Simulation> Give)[]
            {
                (5.0, s => s.Press(Key.Right, s.Now - 0.07)),
                (9.0, s => s.Press(Key.Up, s.Now - 0.05)),
                (12.0, s => s.Release(Key.Right, s.Now - 0.06)),
                (20.0, s => s.Release(Key.Up, s.Now - 0.04)),
                (21.0, s => s.Target(-5, 3, s.Now - 0.08)),
                (40.0, s => s.Control(RunControl.Pause, s.Now - 0.02)),
                (44.0, s => s.Control(RunControl.Resume, s.Now - 0.02)),
                (60.0, s => s.Press(Key.Down, s.Now - 0.05)),
            };
            foreach (var (at, give) in script)
            {
                if (at > until) break;
                sim.StepTo(at);
                give(sim);
            }
            sim.StepTo(until);
            return sim;
        }

        static void AssertSameUpTo(Simulation a, Simulation b, double cut, string what)
        {
            Assert.AreEqual(a.Now, b.Now, 1e-12);
            var ea = a.EventsSince(0);
            var eb = b.EventsSince(0);
            Assert.AreEqual(ea.Length, eb.Length, $"{what}: event count at the cut {cut}");
            for (int i = 0; i < ea.Length; i++)
                Assert.AreEqual(ea[i], eb[i], $"{what}: event {i} at the cut {cut}");
            Assert.AreEqual(a.EventHash, b.EventHash, $"{what}: event hash at the cut {cut}");
            Assert.AreEqual(a.State.Beat, b.State.Beat);
            Assert.AreEqual(a.State.Quiet, b.State.Quiet);
            Assert.AreEqual(a.State.CadenceBeats, b.State.CadenceBeats);
            Assert.AreEqual(a.State.Hero.X, b.State.Hero.X);
            Assert.AreEqual(a.State.Hero.Y, b.State.Hero.Y);
        }

        static List<KnownEntry> Truncated(List<KnownEntry> known, double cut) => known.FindAll(k => k.KnownAt <= cut);

        static List<KnownEntry> Perturbed(List<KnownEntry> known, double cut, ulong seed)
        {
            var rng = new SimRandom(seed);
            var list = new List<KnownEntry>();
            foreach (var k in known)
            {
                if (k.KnownAt <= cut)
                {
                    list.Add(k);
                    continue;
                }
                if (rng.NextDouble() < 0.2) continue; // dropped
                var p = k.Entry;
                p.T += rng.Range(-0.3, 0.3);
                p.V0 = rng.NextDouble();
                p.V1 = rng.NextDouble();
                list.Add(new KnownEntry(p, cut + (k.KnownAt - cut) * rng.Range(1e-3, 2)));
                if (rng.NextDouble() < 0.05)
                {
                    // An extra beat or onset the original never had, known after the cut.
                    var extra = rng.NextDouble() < 0.5
                        ? StreamEntry.Beat(k.Entry.T + 0.1, k.Entry.Stamp, 0.4, 1.0)
                        : StreamEntry.Onset(k.Entry.T + 0.01, k.Entry.Stamp, 1, true);
                    list.Add(new KnownEntry(extra, cut + rng.Range(1e-6, 1)));
                }
            }
            return list;
        }

        [Test]
        public void TruncatingTheStreamAfterACutChangesNothingBeforeIt()
        {
            var known = Drive.Known(Song(3).Entries());
            foreach (double cut in new[] { 0.75, 4.33, 10.0, 31.2, 35.9, 41.0, 63.7, 88.8, 104.5 })
                AssertSameUpTo(Run(known, cut), Run(Truncated(known, cut), cut), cut, "truncated");
        }

        [Test]
        public void PerturbingTheStreamAfterACutChangesNothingBeforeIt()
        {
            var known = Drive.Known(Song(4).Entries());
            ulong seed = 11;
            foreach (double cut in new[] { 2.5, 12.1, 30.6, 36.4, 58.0, 71.3, 99.9 })
                AssertSameUpTo(Run(known, cut), Run(Perturbed(known, cut, seed++), cut), cut, "perturbed");
        }

        [Test]
        public void EntriesFedLateChangeNothingBeforeTheyAreKnown()
        {
            var known = Drive.Known(Song(5).Entries());
            // An analyzer stall: everything from 14 s of the music on that would have been known before 15.5 s
            // is known only then, some of it after its own time. (Content order holds: a beat or tick known
            // out of order would be dropped.)
            const double late = 15.5;
            var delayed = new List<KnownEntry>();
            var dropped = new List<KnownEntry>();
            foreach (var k in known)
            {
                bool stalled = k.Entry.T >= 14 && k.KnownAt < late;
                delayed.Add(stalled ? new KnownEntry(k.Entry, Math.Max(k.KnownAt, late)) : k);
                if (!stalled) dropped.Add(k);
            }
            // Up to just before 15.5 s, a sim given them late is the sim never given them at all...
            double before = Math.Floor(late * Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond;
            AssertSameUpTo(Run(dropped, before), Run(delayed, before), before, "late vs never");
            // ...and they do matter once known.
            Assert.AreNotEqual(Run(dropped, 20).EventHash, Run(delayed, 20).EventHash);
            // Fed normally, they changed the run from when they were known, well before.
            Assert.AreNotEqual(Run(known, before).EventHash, Run(delayed, before).EventHash);
        }

        [Test]
        public void EntriesAreKnownJustInTimeAfterAStall()
        {
            // Every entry known just before its own time: nothing near a cut depends on the normal lookahead.
            var list = new List<KnownEntry>();
            foreach (var k in Drive.Known(Song(6).Entries()))
                list.Add(new KnownEntry(k.Entry, Math.Max(k.KnownAt, Math.Max(0, k.Entry.T - 0.02))));
            foreach (double cut in new[] { 6.3, 33.3, 77.7 })
                AssertSameUpTo(Run(list, cut), Run(Truncated(list, cut), cut), cut, "just in time");
        }

        [Test]
        public void LiveArrivalsAndTheirRecordedKnownAtTimesSettleTheSameRun()
        {
            var entries = Song(7).Entries();
            var live = Drive.Sim();
            Drive.Feed(live, entries, 60);
            var replayed = Drive.Sim();
            var accepted = live.Stream.Accepted;
            int n = 0;
            foreach (var e in entries)
            {
                if (n == accepted.Length) break;
                replayed.StepTo(accepted[n].Taken);
                replayed.AddKnown(e, accepted[n].KnownAt);
                n++;
            }
            replayed.StepTo(60);
            Assert.AreEqual(live.Hash, replayed.Hash);
            Assert.AreEqual(live.EventCount, replayed.EventCount);
        }
    }
}
