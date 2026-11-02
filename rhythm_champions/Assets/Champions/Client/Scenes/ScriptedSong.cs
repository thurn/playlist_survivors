using System;
using System.Collections.Generic;
using Champions.Sim;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// A synthetic belief stream shaped like the analyzer's, for scripted scenes (ported from the sim tests' FixtureSong):
    /// committed beats at a steady tempo with an onset on each, a tick every 50 ms, hindsight windows, and the section
    /// changes a scene asks for, each stamped by the plugin's rule <c>s = position - (M - A)</c> with the analyzer's
    /// typical delays (beats about 1.2 s late, ticks 50 ms, changes 2.1 s). Gaps carry ticks below the gate, as unclear
    /// music does. Entries come in stamp order, so a scene feeds them as their stamps pass (<see cref="Loop.SimDriver.AddSettled"/>).
    /// </summary>
    public sealed class ScriptedSong
    {
        public const double M = 3.0, A = 0.048, TickSeconds = 0.05;

        readonly List<(double from, double to, double bpm)> _stretches = new List<(double, double, double)>();
        readonly List<StreamEntry> _extra = new List<StreamEntry>();

        public static double StampOf(double position) => position - (M - A);

        /// <summary>Beats from <paramref name="from"/> to <paramref name="to"/> at <paramref name="bpm"/>.</summary>
        public ScriptedSong Beats(double from, double to, double bpm)
        {
            _stretches.Add((from, to, bpm));
            return this;
        }

        /// <summary>No beat from <paramref name="from"/> to <paramref name="to"/>: audible ticks under the gate.</summary>
        public ScriptedSong Gap(double from, double to)
        {
            _stretches.Add((from, to, 0));
            return this;
        }

        /// <summary>A section change at <paramref name="t"/>.</summary>
        public ScriptedSong Change(double t, double strength = 0.8)
        {
            _extra.Add(StreamEntry.Change(t, StampOf(t + 2.1), strength));
            return this;
        }

        /// <summary>Every entry, in stamp order.</summary>
        public List<StreamEntry> Entries()
        {
            var list = new List<StreamEntry>(_extra);
            double end = 0;
            foreach (var (from, to, bpm) in _stretches)
            {
                end = Math.Max(end, to);
                if (bpm > 0)
                {
                    double period = 60.0 / bpm;
                    var grid = new List<double>();
                    int n = 0;
                    for (double t = from; t < to - 1e-9; t = from + ++n * period)
                    {
                        list.Add(StreamEntry.Beat(t, StampOf(t + 1.2), period, 0.9));
                        list.Add(StreamEntry.Onset(t + 0.004, StampOf(t + 0.08), 0.8, n % 2 == 0, n % 2 == 0 ? (int)DrumBand.Kick : (int)DrumBand.Snare));
                        grid.Add(t);
                        if (grid.Count < 9) continue;
                        double stamp = StampOf(t + 1.0);
                        list.Add(StreamEntry.Hindsight(t, stamp, grid.Count, 0));
                        foreach (double b in grid) list.Add(StreamEntry.HindsightBeat(b, stamp, false, t));
                        grid.Clear();
                    }
                }
                long k0 = (long)Math.Ceiling(from / TickSeconds - 1e-9);
                for (long k = k0; k * TickSeconds < to - 1e-9; k++)
                {
                    double t = k * TickSeconds;
                    list.Add(StreamEntry.Tick(t, StampOf(t + 0.05), bpm > 0 ? 0.9 : 0.1, 0.6, level: LoudnessLevel.Medium, key: 9,
                        kick: 0.5, snare: 0.5, hat: 0.5, bass: 0.5, brightness: 0.4));
                }
            }
            list.Add(StreamEntry.End(end, StampOf(end + 10)));
            var indexed = new List<(StreamEntry e, int i)>(list.Count);
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
}
