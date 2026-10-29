using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>The grid positions a point stands on. A beat is also an eighth and a sixteenth position.</summary>
    [Flags]
    public enum GridFlags
    {
        None = 0,
        Sixteenth = 1,
        Eighth = 2,
        Beat = 4,
        Downbeat = 8,
        Phrase = 16,   // the downbeat that opens a phrase
        Accent = 32,
        Free = 64,     // a point of a free section's grid (its accents and anchors)
    }

    public readonly struct GridPoint
    {
        public readonly double Time;
        public readonly GridFlags Flags;
        public readonly int Section;
        public readonly int BeatIndex;          // the song map's beat for a pulsed beat, else -1
        public readonly float AccentStrength;   // 0 unless an accent

        public GridPoint(double time, GridFlags flags, int section, int beatIndex, float accentStrength)
        {
            Time = time; Flags = flags; Section = section; BeatIndex = beatIndex; AccentStrength = accentStrength;
        }

        public bool Has(GridFlags f) => (Flags & f) == f;
    }

    /// <summary>
    /// The beat grid (choreography.md, "Timeline contract"): the song map's beats with their halves
    /// and quarters in pulsed sections, and the section's accents in free sections. A beat counts
    /// as an accent when an accent lies within 30 ms of it.
    /// </summary>
    public sealed class BeatGrid
    {
        public const double AccentWindow = 0.030;

        public readonly SongMap Map;
        public readonly GridPoint[] Points;
        readonly double[] times;
        /// <summary>Per beat index: is it a downbeat, and does it open a phrase.</summary>
        public readonly bool[] IsDownbeat, IsPhraseStart;

        public BeatGrid(SongMap map)
        {
            Map = map;
            int n = map.Beats.Length;
            IsDownbeat = new bool[n];
            IsPhraseStart = new bool[n];
            for (int bar = 1; ; bar++)
            {
                int b = map.DownbeatIndexOfBar(bar);
                if (b >= n) break;
                if (b >= 0) IsDownbeat[b] = true;
            }
            foreach (int[] ph in map.Phrases)
            {
                int b = map.DownbeatIndexOfBar(ph[0]);
                if (b >= 0 && b < n) IsPhraseStart[b] = true;
            }

            var pts = new List<GridPoint>();
            for (int s = 0; s < map.Sections.Length; s++)
            {
                SongSection sec = map.Sections[s];
                if (sec.Feel == SectionFeel.Free) AddFree(map, s, pts);
                else AddPulsed(map, s, pts);
            }
            pts.Sort((a, b) => a.Time.CompareTo(b.Time));
            Points = pts.ToArray();
            times = new double[Points.Length];
            for (int i = 0; i < Points.Length; i++) times[i] = Points[i].Time;
        }

        void AddPulsed(SongMap map, int s, List<GridPoint> pts)
        {
            SongSection sec = map.Sections[s];
            int n = map.Beats.Length;
            for (int b = Math.Max(0, sec.FirstBeat); b < sec.EndBeat && b < n; b++)
            {
                double t = map.Beats[b];
                double next = b + 1 < n ? map.Beats[b + 1] : t + (t - map.Beats[b - 1]);
                double q = (next - t) / 4;
                GridFlags f = GridFlags.Beat | GridFlags.Eighth | GridFlags.Sixteenth;
                if (IsDownbeat[b]) f |= GridFlags.Downbeat;
                if (IsPhraseStart[b]) f |= GridFlags.Phrase;
                float acc = AccentNear(map, t);
                if (acc > 0) f |= GridFlags.Accent;
                pts.Add(new GridPoint(t, f, s, b, acc));
                if (t + q >= sec.End) continue;
                pts.Add(new GridPoint(t + q, GridFlags.Sixteenth, s, -1, 0));
                pts.Add(new GridPoint(t + 2 * q, GridFlags.Eighth | GridFlags.Sixteenth, s, -1, 0));
                pts.Add(new GridPoint(t + 3 * q, GridFlags.Sixteenth, s, -1, 0));
            }
        }

        static float AccentNear(SongMap map, double t)
        {
            float best = 0;
            foreach (TimedStrength a in map.Accents)
                if (Math.Abs(a.Time - t) <= AccentWindow && a.Strength > best) best = Math.Max(a.Strength, 0.01f);
            return best;
        }

        static void AddFree(SongMap map, int s, List<GridPoint> pts)
        {
            SongSection sec = map.Sections[s];
            var cand = new List<TimedStrength>();
            foreach (TimedStrength a in map.Accents) if (a.Time >= sec.Start && a.Time < sec.End) cand.Add(a);
            foreach (TimedStrength a in sec.Anchors)
            {
                bool dup = false;
                for (int i = 0; i < cand.Count; i++)
                {
                    if (Math.Abs(cand[i].Time - a.Time) > AccentWindow) continue;
                    dup = true;
                    if (a.Strength > cand[i].Strength) cand[i] = new TimedStrength(cand[i].Time, a.Strength, cand[i].Label);
                }
                if (!dup && a.Time >= sec.Start && a.Time < sec.End) cand.Add(a);
            }
            cand.Sort((a, b) => a.Time.CompareTo(b.Time));
            const GridFlags f = GridFlags.Free | GridFlags.Accent | GridFlags.Beat | GridFlags.Eighth | GridFlags.Sixteenth;
            foreach (TimedStrength a in cand) pts.Add(new GridPoint(a.Time, f, s, -1, Math.Max(0.01f, a.Strength)));
        }

        public int Count => Points.Length;

        /// <summary>The index of the first point strictly after <paramref name="t"/> (Count if none).</summary>
        public int FirstAfter(double t)
        {
            int i = Array.BinarySearch(times, t);
            if (i >= 0)
            {
                while (i < times.Length && times[i] <= t) i++;
                return i;
            }
            return ~i;
        }

        /// <summary>The index of the point nearest <paramref name="t"/>.</summary>
        public int Nearest(double t)
        {
            int i = FirstAfter(t);
            if (i >= times.Length) return times.Length - 1;
            if (i > 0 && t - times[i - 1] <= times[i] - t) return i - 1;
            return i;
        }
    }
}
