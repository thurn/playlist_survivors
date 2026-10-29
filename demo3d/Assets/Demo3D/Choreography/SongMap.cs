using System;
using System.Collections.Generic;

namespace Demo3D.Choreography
{
    public enum SectionFeel { Pulsed, Free }

    public readonly struct TimedStrength
    {
        public readonly double Time;
        public readonly float Strength;
        public readonly string Label;
        public TimedStrength(double time, float strength, string label) { Time = time; Strength = strength; Label = label; }
    }

    public sealed class SongSection
    {
        public string Label;
        public int FirstBar, LastBar;           // 1-based, inclusive
        public SectionFeel Feel;
        public TimedStrength[] Anchors = new TimedStrength[0];
        /// <summary>Song seconds, from its first downbeat to the next section's (or the song's end).</summary>
        public double Start, End;
        /// <summary>Beat indices: the first beat of the section, and one past its last.</summary>
        public int FirstBeat, EndBeat;
    }

    /// <summary>
    /// The beat-analysis tool's song map (docs/design/tech-demo.md, "Song Map"): beats in song
    /// seconds, meter, sections with their feel, phrases, accents and energy.
    /// </summary>
    public sealed class SongMap
    {
        public string Song;
        public double Duration;
        public double[] Beats;
        public int FirstDownbeatIndex;
        public int[] MeterFromBar = { 1 };
        public int[] MeterBeatsPerBar = { 4 };
        public SongSection[] Sections;
        public int[][] Phrases = new int[0][];   // [firstBar, lastBar], 1-based
        public TimedStrength[] Accents = new TimedStrength[0];
        public float[] Energy = new float[0];

        public int BeatsPerBarAt(int bar)
        {
            int bpb = MeterBeatsPerBar[0];
            for (int i = 0; i < MeterFromBar.Length; i++) if (bar >= MeterFromBar[i]) bpb = MeterBeatsPerBar[i];
            return bpb;
        }

        /// <summary>The beat index of bar <paramref name="bar"/>'s first beat (1-based bars).</summary>
        public int DownbeatIndexOfBar(int bar)
        {
            int idx = FirstDownbeatIndex;
            for (int b = 1; b < bar; b++) idx += BeatsPerBarAt(b);
            return idx;
        }

        /// <summary>Piecewise-linear over the beat list, extrapolating from the nearest two beats.</summary>
        public double TimeOfBeatIndex(double x)
        {
            int n = Beats.Length;
            if (n == 1) return Beats[0];
            int i = (int)Math.Floor(x);
            if (i < 0) i = 0;
            if (i > n - 2) i = n - 2;
            return Beats[i] + (x - i) * (Beats[i + 1] - Beats[i]);
        }

        public double BeatIndexAt(double t)
        {
            int n = Beats.Length;
            if (n == 1) return 0;
            int i = Array.BinarySearch(Beats, t);
            if (i < 0) i = ~i - 1;
            if (i < 0) i = 0;
            if (i > n - 2) i = n - 2;
            return i + (t - Beats[i]) / (Beats[i + 1] - Beats[i]);
        }

        /// <summary>The section containing song time <paramref name="t"/> (the first before it starts, the last after it ends).</summary>
        public int SectionIndexAt(double t)
        {
            for (int i = Sections.Length - 1; i >= 0; i--) if (t >= Sections[i].Start) return i;
            return 0;
        }

        public int SectionIndexOf(string label)
        {
            for (int i = 0; i < Sections.Length; i++) if (Sections[i].Label == label) return i;
            return -1;
        }

        public static SongMap Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            var map = new SongMap();
            map.Song = root["song"].AsString("song");
            JsonValue beats = root["beats"];
            if (beats.Count < 2) throw new FormatException("song map: needs at least two beats");
            map.Beats = new double[beats.Count];
            for (int i = 0; i < beats.Count; i++) map.Beats[i] = beats[i].AsDouble();
            for (int i = 1; i < map.Beats.Length; i++)
                if (!(map.Beats[i] > map.Beats[i - 1])) throw new FormatException("song map: beats must increase");
            map.Duration = root["audio"]["duration"].AsDouble(map.Beats[map.Beats.Length - 1]);
            map.FirstDownbeatIndex = root["firstDownbeatIndex"].AsInt();
            JsonValue meter = root["meter"];
            if (meter.Count > 0)
            {
                map.MeterFromBar = new int[meter.Count];
                map.MeterBeatsPerBar = new int[meter.Count];
                for (int i = 0; i < meter.Count; i++)
                {
                    map.MeterFromBar[i] = meter[i]["fromBar"].AsInt(1);
                    map.MeterBeatsPerBar[i] = meter[i]["beatsPerBar"].AsInt(4);
                }
            }
            JsonValue phrases = root["phrases"];
            map.Phrases = new int[phrases.Count][];
            for (int i = 0; i < phrases.Count; i++) map.Phrases[i] = new[] { phrases[i][0].AsInt(), phrases[i][1].AsInt() };
            map.Accents = ReadTimed(root["accents"]);
            JsonValue energy = root["energy"];
            map.Energy = new float[energy.Count];
            for (int i = 0; i < energy.Count; i++) map.Energy[i] = energy[i].AsFloat();

            JsonValue sections = root["sections"];
            var list = new List<SongSection>();
            for (int i = 0; i < sections.Count; i++)
            {
                JsonValue s = sections[i];
                list.Add(new SongSection
                {
                    Label = s["label"].AsString(),
                    FirstBar = s["bars"][0].AsInt(),
                    LastBar = s["bars"][1].AsInt(),
                    Feel = s["feel"].AsString("pulsed") == "free" ? SectionFeel.Free : SectionFeel.Pulsed,
                    Anchors = ReadTimed(s["anchors"]),
                });
            }
            map.Sections = list.ToArray();
            map.ResolveSections();
            return map;
        }

        static TimedStrength[] ReadTimed(JsonValue arr)
        {
            var result = new TimedStrength[arr.Count];
            for (int i = 0; i < arr.Count; i++)
                result[i] = new TimedStrength(arr[i]["time"].AsDouble(), arr[i]["strength"].AsFloat(1f), arr[i]["label"].AsString());
            return result;
        }

        void ResolveSections()
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                SongSection s = Sections[i];
                s.FirstBeat = DownbeatIndexOf(s.FirstBar);
                s.EndBeat = Math.Min(Beats.Length, DownbeatIndexOf(s.LastBar + 1));
                s.Start = i == 0 ? Math.Min(0, TimeOfBeatIndex(s.FirstBeat)) : TimeOfBeatIndex(s.FirstBeat);
                s.End = i == Sections.Length - 1 ? Duration : TimeOfBeatIndex(DownbeatIndexOf(s.LastBar + 1));
                if (s.End > Duration) s.End = Duration;
            }
        }

        int DownbeatIndexOf(int bar) => DownbeatIndexOfBar(bar);

        /// <summary>
        /// A synthetic map for stress timelines: a constant tempo in 4/4 from t = 0, one pulsed
        /// section, 8-bar phrases, and an accent on each phrase's opening downbeat.
        /// </summary>
        public static SongMap Synthetic(double bpm, double seconds, int beatsPerBar = 4, int phraseBars = 8, string label = "stress")
        {
            double period = 60.0 / bpm;
            int bars = Math.Max(1, (int)Math.Ceiling(seconds / (period * beatsPerBar)));
            int nBeats = bars * beatsPerBar;
            var map = new SongMap
            {
                Song = "synthetic-" + bpm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "bpm",
                Beats = new double[nBeats],
                MeterFromBar = new[] { 1 },
                MeterBeatsPerBar = new[] { beatsPerBar },
            };
            for (int i = 0; i < nBeats; i++) map.Beats[i] = i * period;
            map.Duration = nBeats * period;
            var phrases = new List<int[]>();
            var accents = new List<TimedStrength>();
            for (int b = 1; b <= bars; b += phraseBars)
            {
                phrases.Add(new[] { b, Math.Min(bars, b + phraseBars - 1) });
                accents.Add(new TimedStrength(map.Beats[(b - 1) * beatsPerBar], 0.9f, "phrase"));
            }
            map.Phrases = phrases.ToArray();
            map.Accents = accents.ToArray();
            map.Energy = new float[nBeats];
            for (int i = 0; i < nBeats; i++) map.Energy[i] = 1f;
            map.Sections = new[] { new SongSection { Label = label, FirstBar = 1, LastBar = bars, Feel = SectionFeel.Pulsed } };
            map.ResolveSections();
            return map;
        }
    }
}
