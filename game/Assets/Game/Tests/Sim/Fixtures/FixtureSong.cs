using System;
using System.Collections.Generic;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// A synthetic belief stream shaped like the analyzer's: committed beats along a tempo map, a
    /// tick every 50 ms, onsets, changes, jumps, timbre, forecasts and hindsight chunks, each
    /// stamped by the plugin's rule <c>s = position - (M - A)</c> with the analyzer's typical
    /// emission delays (beats from hindsight about 1.2 s late, ticks 50 ms, changes 2.1 s).
    /// Stretches without a beat carry ticks below the gate, as quiet or unclear music does.
    /// </summary>
    public sealed class FixtureSong
    {
        public const double M = 3.0;
        public const double A = 0.048;
        public const double TickSeconds = 0.05;

        struct Stretch
        {
            public double From, To, Bpm0, Bpm1, Evidence, Jitter;
            public bool Ticks;
        }

        readonly List<Stretch> stretches = new List<Stretch>();
        readonly ulong seed;

        public FixtureSong(ulong seed = 7)
        {
            this.seed = seed;
        }

        public static double StampOf(double position) => position - (M - A);

        /// <summary>Beats from <paramref name="from"/> to <paramref name="to"/>, the tempo ramping linearly from bpm0 to bpm1.</summary>
        public FixtureSong Beats(double from, double to, double bpm0, double bpm1 = double.NaN,
            double evidence = 0.9, double jitter = 0)
        {
            stretches.Add(new Stretch
            {
                From = from, To = to, Bpm0 = bpm0, Bpm1 = double.IsNaN(bpm1) ? bpm0 : bpm1,
                Evidence = evidence, Jitter = jitter, Ticks = true,
            });
            return this;
        }

        /// <summary>No beats from <paramref name="from"/> to <paramref name="to"/>; ticks below the gate (or none).</summary>
        public FixtureSong Gap(double from, double to, bool ticks = true)
        {
            stretches.Add(new Stretch { From = from, To = to, Bpm0 = 0, Evidence = 0.1, Ticks = ticks });
            return this;
        }

        public double End
        {
            get
            {
                double end = 0;
                foreach (var s in stretches) end = Math.Max(end, s.To);
                return end;
            }
        }

        /// <summary>The committed beat times only.</summary>
        public List<double> BeatTimes()
        {
            var times = new List<double>();
            foreach (var e in Entries())
                if (e.Kind == EntryKind.Beat) times.Add(e.T);
            times.Sort();
            return times;
        }

        /// <summary>Every entry, in emission (stamp) order.</summary>
        public List<StreamEntry> Entries()
        {
            var rng = new SimRandom(seed);
            var list = new List<StreamEntry>();
            foreach (var s in stretches)
            {
                if (s.Bpm0 > 0)
                {
                    double t = s.From;
                    int n = 0;
                    var grid = new List<double>();
                    while (t < s.To - 1e-9)
                    {
                        double u = (t - s.From) / Math.Max(1e-9, s.To - s.From);
                        double period = 60.0 / (s.Bpm0 + (s.Bpm1 - s.Bpm0) * u);
                        list.Add(StreamEntry.Beat(t, StampOf(t + 1.2), period, s.Evidence));
                        list.Add(StreamEntry.Onset(t + 0.004, StampOf(t + 0.08), 0.6 + 0.4 * rng.NextDouble(), n % 2 == 0, n % 2 == 0 ? (int)DrumBand.Kick : (int)DrumBand.Snare));
                        if (n % 4 == 0) list.Add(StreamEntry.Forecast(t, StampOf(t - 1.5), 4, 0.7));
                        if (n > 0 && n % 32 == 0) list.Add(StreamEntry.Change(t, StampOf(t + 2.1), 0.8));
                        if (n > 0 && n % 64 == 0) list.Add(StreamEntry.Jump(t, StampOf(t + 0.3), 6));
                        grid.Add(t);
                        if (grid.Count == 9)
                        {
                            // A window's header and its (offline) grid, all with the header's stamp.
                            double stamp = StampOf(t + 1.0);
                            list.Add(StreamEntry.Hindsight(t, stamp, grid.Count, 0));
                            foreach (double b in grid) list.Add(StreamEntry.HindsightBeat(b, stamp, false, t));
                            grid.Clear();
                        }
                        t += period + (s.Jitter > 0 ? (rng.NextDouble() * 2 - 1) * s.Jitter : 0);
                        n++;
                    }
                }
                if (!s.Ticks) continue;
                long k0 = (long)Math.Ceiling(s.From / TickSeconds - 1e-9);
                for (long k = k0; k * TickSeconds < s.To - 1e-9; k++)
                {
                    double t = k * TickSeconds;
                    double evidence = s.Bpm0 > 0 ? s.Evidence : 0.1;
                    list.Add(StreamEntry.Tick(t, StampOf(t + 0.05), evidence, 0.5 + 0.2 * rng.NextDouble(),
                        level: LoudnessLevel.Medium, key: 9, kick: rng.NextDouble(), snare: rng.NextDouble(),
                        hat: rng.NextDouble(), bass: rng.NextDouble(), brightness: 0.4));
                    if (k % 20 == 0) list.Add(StreamEntry.Timbre(t, StampOf(t + 0.5), 0.4, 0.2, 0.6, 0.5));
                }
            }
            double end = End;
            list.Add(StreamEntry.End(end, StampOf(end + 10)));
            // Emission order: by stamp, then content time (a stable sort keeps the rest).
            var indexed = new List<(StreamEntry e, int i)>();
            for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
            indexed.Sort((a, b) =>
            {
                int c = a.e.Stamp.CompareTo(b.e.Stamp);
                if (c != 0) return c;
                c = a.e.T.CompareTo(b.e.T);
                return c != 0 ? c : a.i.CompareTo(b.i);
            });
            var sorted = new List<StreamEntry>(list.Count);
            foreach (var x in indexed) sorted.Add(x.e);
            return sorted;
        }
    }

    /// <summary>An entry with the known-at time a replay gives it (<see cref="Simulation.AddKnown"/>).</summary>
    public struct KnownEntry
    {
        public StreamEntry Entry;
        public double KnownAt;

        public KnownEntry(in StreamEntry entry, double knownAt)
        {
            Entry = entry;
            KnownAt = knownAt;
        }
    }

    /// <summary>Drives a sim the way the client does: frames, arrivals, StepTo.</summary>
    public static class Drive
    {
        /// <summary>
        /// Feeds entries as the live path would: each arrives once the frontier passes its stamp by
        /// <paramref name="arrivalLag"/> (the sim's lead over the analyzer's emission), then the
        /// frame steps the sim, and <paramref name="eachFrame"/> reads it as a client would. Returns
        /// the index of the first entry not yet fed.
        /// </summary>
        public static int Feed(Simulation sim, IReadOnlyList<StreamEntry> entries, double until,
            double frame = 1.0 / 60, double arrivalLag = 0.06, int from = 0, Action<Simulation> eachFrame = null)
        {
            int next = from;
            double frontier = sim.Now;
            // A first, empty frame takes what was emitted before the sim's current time.
            bool first = true;
            while (first || frontier < until - 1e-12)
            {
                if (!first) frontier = Math.Min(until, frontier + frame);
                first = false;
                while (next < entries.Count && entries[next].Stamp + arrivalLag <= frontier)
                {
                    sim.Add(entries[next]);
                    next++;
                }
                sim.StepTo(frontier);
                eachFrame?.Invoke(sim);
            }
            return next;
        }

        /// <summary>
        /// The entries with their normal known-at times (<c>Stamp + B</c>, and no earlier than the
        /// start), for <see cref="Simulation.AddKnown"/>.
        /// </summary>
        public static List<KnownEntry> Known(IEnumerable<StreamEntry> entries, double budget = 0.5)
        {
            var list = new List<KnownEntry>();
            foreach (var e in entries) list.Add(new KnownEntry(e, Math.Max(0, e.Stamp + budget)));
            return list;
        }

        public static Simulation Replay(IEnumerable<KnownEntry> known, double until, ulong seed = 1)
        {
            var sim = new Simulation(new SimConfig { Seed = seed });
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(until);
            return sim;
        }

        public static List<SimEvent> Events(Simulation sim, SimEventKind kind = SimEventKind.None)
        {
            var list = new List<SimEvent>();
            foreach (var e in sim.EventsSince(0))
                if (kind == SimEventKind.None || e.Kind == kind) list.Add(e);
            return list;
        }
    }
}
