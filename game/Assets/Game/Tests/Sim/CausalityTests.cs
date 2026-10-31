using System.Collections.Generic;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The causality test (unity-game.md, "Stream Entries and What the Sim May Know"): truncate or
    /// perturb the stream after a cut, and nothing the sim settled up to the cut may change.
    /// </summary>
    public class CausalityTests
    {
        static FixtureSong Song(ulong seed) => new FixtureSong(seed)
            .Beats(0, 30, 128, jitter: 0.004)
            .Gap(30, 36)
            .Beats(36, 70, 100, 150, jitter: 0.003)
            .Beats(70, 90, 149, evidence: 0.35)
            .Beats(90, 110, 151);

        static List<StreamEntry> Truncated(List<StreamEntry> known, double cut)
        {
            var kept = new List<StreamEntry>();
            foreach (var e in known)
                if (e.KnownAt <= cut) kept.Add(e);
            return kept;
        }

        static List<StreamEntry> Perturbed(List<StreamEntry> known, double cut, ulong seed)
        {
            var rng = new SimRandom(seed);
            var list = new List<StreamEntry>();
            foreach (var e in known)
            {
                if (e.KnownAt <= cut)
                {
                    list.Add(e);
                    continue;
                }
                if (rng.NextDouble() < 0.2) continue; // dropped
                var p = e;
                p.T += rng.Range(-0.3, 0.3);
                p.V0 = rng.NextDouble();
                p.V1 = rng.NextDouble();
                p.KnownAt = cut + (e.KnownAt - cut) * rng.Range(1e-3, 2);
                list.Add(p);
                if (rng.NextDouble() < 0.05)
                {
                    // An extra beat or tick the original never had, known after the cut.
                    var extra = rng.NextDouble() < 0.5
                        ? StreamEntry.Beat(e.T + 0.1, e.Stamp, 0.4, 1.0)
                        : StreamEntry.Tick(e.T + 0.01, e.Stamp, 0, 0);
                    extra.KnownAt = cut + rng.Range(1e-6, 1);
                    list.Add(extra);
                }
            }
            return list;
        }

        static void AssertSameUpTo(Simulation a, Simulation b, double cut, string what)
        {
            Assert.AreEqual(a.Now, b.Now, 1e-12);
            var ea = a.EventsSince(0);
            var eb = b.EventsSince(0);
            Assert.AreEqual(ea.Length, eb.Length, $"{what}: event count at the cut {cut}");
            for (int i = 0; i < ea.Length; i++)
                Assert.AreEqual(ea[i], eb[i], $"{what}: event {i} at the cut {cut}");
            Assert.AreEqual(a.Hash, b.Hash, $"{what}: hash at the cut {cut}");
            Assert.AreEqual(a.State.Beat, b.State.Beat);
            Assert.AreEqual(a.State.StrideStep, b.State.StrideStep);
            Assert.AreEqual(a.State.StrideHalvings, b.State.StrideHalvings);
        }

        /// <summary>
        /// The stream with every entry known just before its own time, as after an analyzer stall:
        /// with the normal lookahead nothing near a cut depends on what becomes known right after
        /// it, so a small leak would go unseen there.
        /// </summary>
        static List<StreamEntry> JustInTime(List<StreamEntry> known)
        {
            var list = new List<StreamEntry>();
            foreach (var e in known)
            {
                var j = e;
                j.KnownAt = System.Math.Max(0, e.T - (e.Kind == EntryKind.Beat ? 0.03 : 0.01));
                list.Add(j);
            }
            return list;
        }

        [Test]
        public void TruncatingOrPerturbingAfterACutChangesNothingBeforeIt(
            [Values(1UL, 2UL, 3UL)] ulong seed, [Values(false, true)] bool justInTime)
        {
            var known = Drive.Known(Song(seed).Entries());
            if (justInTime) known = JustInTime(known);
            for (double cut = 3.25; cut < 110; cut += 7.3)
            {
                double at = System.Math.Floor(cut * 120) / 120;
                var full = Drive.Replay(known, at);
                var truncated = Drive.Replay(Truncated(known, at), at);
                var perturbed = Drive.Replay(Perturbed(known, at, seed * 1000 + (ulong)(cut * 10)), at);
                AssertSameUpTo(full, truncated, at, "truncated");
                AssertSameUpTo(full, perturbed, at, "perturbed");
            }
        }

        [Test]
        public void TheLivePathIsCausalToo()
        {
            // Entries fed frame by frame as they are emitted, against the same stream cut short.
            var entries = Song(4).Entries();
            const double cut = 52.5;
            var full = new Simulation(new SimConfig());
            Drive.Feed(full, entries, cut);
            var cutShort = new List<StreamEntry>();
            foreach (var e in entries)
                if (e.Stamp + 0.5 <= cut) cutShort.Add(e);
            var shortSim = new Simulation(new SimConfig());
            Drive.Feed(shortSim, cutShort, cut);
            AssertSameUpTo(full, shortSim, cut, "live, truncated");
        }

        [Test]
        public void EventsAtTheCutAreFinalWhenTheSimContinues()
        {
            var known = Drive.Known(Song(5).Entries());
            var sim = Drive.Replay(known, 40);
            var settled = sim.EventsSince(0).ToArray();
            sim.StepTo(80);
            var later = sim.EventsSince(0);
            for (int i = 0; i < settled.Length; i++) Assert.AreEqual(settled[i], later[i]);
        }
    }
}
