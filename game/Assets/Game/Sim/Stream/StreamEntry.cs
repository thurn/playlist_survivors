using System;
using System.Runtime.InteropServices;

namespace PS.Game.Sim
{
    /// <summary>
    /// One belief-stream entry: the C# mirror of the plugin's
    /// <c>ps_entry { int32 kind; double t, known_at; double v[12]; }</c> (unity-game.md, "The C
    /// Interface"), 120 bytes and blittable, so the client polls the plugin straight into an array
    /// of these. <see cref="T"/> is the content time (when in the music the thing it describes
    /// happens) and <see cref="Stamp"/> the plugin's stamp (<c>position - (M - A)</c>, the C struct's
    /// <c>known_at</c>). The game's known-at time, <c>max(Stamp + B, frontier at arrival)</c>, is the
    /// sim's: it keeps it beside the entry (<see cref="BeliefLog.KnownAt"/>), and no rule reads an
    /// entry before it.
    ///
    /// The <c>v</c> slots are plain fields so the struct stays dense and blittable; each kind's
    /// layout is documented on its factory and read through its typed view (<see cref="AsBeat"/>
    /// and the rest). Unused slots are 0; a coded slot's "none" is -1.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StreamEntry
    {
        public const int Slots = 12;

        public EntryKind Kind;
        public double T;
        public double Stamp;
        public double V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11;

        public double this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return V0;
                    case 1: return V1;
                    case 2: return V2;
                    case 3: return V3;
                    case 4: return V4;
                    case 5: return V5;
                    case 6: return V6;
                    case 7: return V7;
                    case 8: return V8;
                    case 9: return V9;
                    case 10: return V10;
                    case 11: return V11;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (i)
                {
                    case 0: V0 = value; break;
                    case 1: V1 = value; break;
                    case 2: V2 = value; break;
                    case 3: V3 = value; break;
                    case 4: V4 = value; break;
                    case 5: V5 = value; break;
                    case 6: V6 = value; break;
                    case 7: V7 = value; break;
                    case 8: V8 = value; break;
                    case 9: V9 = value; break;
                    case 10: V10 = value; break;
                    case 11: V11 = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }

        static StreamEntry Of(EntryKind kind, double t, double stamp) =>
            new StreamEntry { Kind = kind, T = t, Stamp = stamp };

        /// <summary>A committed beat. v0 period (s), v1 evidence (0 to 1).</summary>
        public static StreamEntry Beat(double t, double stamp, double period, double evidence)
        {
            var e = Of(EntryKind.Beat, t, stamp);
            e.V0 = period;
            e.V1 = evidence;
            return e;
        }

        /// <summary>
        /// A tick. v0 evidence, v1 energy, v2 level (<see cref="LoudnessLevel"/>), v3 key (0 to 23
        /// into the analyzer's key list, major from C then minor from C; -1 none), v4 kick, v5 snare,
        /// v6 hat, v7 bass, v8 brightness. The analyzer gives every tick a level and bands.
        /// </summary>
        public static StreamEntry Tick(double t, double stamp, double evidence, double energy,
            LoudnessLevel level = LoudnessLevel.Quiet, int key = -1, double kick = 0, double snare = 0,
            double hat = 0, double bass = 0, double brightness = 0)
        {
            var e = Of(EntryKind.Tick, t, stamp);
            e.V0 = evidence;
            e.V1 = energy;
            e.V2 = (int)level;
            e.V3 = key;
            e.V4 = kick;
            e.V5 = snare;
            e.V6 = hat;
            e.V7 = bass;
            e.V8 = brightness;
            return e;
        }

        /// <summary>An onset. v0 strength, v1 strong (0 or 1), v2 band (-1 none, else <see cref="DrumBand"/>).</summary>
        public static StreamEntry Onset(double t, double stamp, double strength, bool strong, int band = -1)
        {
            var e = Of(EntryKind.Onset, t, stamp);
            e.V0 = strength;
            e.V1 = strong ? 1 : 0;
            e.V2 = band;
            return e;
        }

        /// <summary>
        /// A hindsight window's header. t is the window's end; v0 the offline grid's beat count, v1
        /// the online grid's. Its beats follow as <see cref="HindsightBeat"/> records, offline first,
        /// each with the header's stamp.
        /// </summary>
        public static StreamEntry Hindsight(double windowEnd, double stamp, int offline, int online)
        {
            var e = Of(EntryKind.Hindsight, windowEnd, stamp);
            e.V0 = offline;
            e.V1 = online;
            return e;
        }

        /// <summary>One beat of a hindsight window's grid. t is the beat; v0 grid (0 offline, 1 online), v1 the window's end.</summary>
        public static StreamEntry HindsightBeat(double t, double stamp, bool online, double windowEnd)
        {
            var e = Of(EntryKind.HindsightBeat, t, stamp);
            e.V0 = online ? 1 : 0;
            e.V1 = windowEnd;
            return e;
        }

        /// <summary>A timbre reading. v0 brightness, v1 flatness, v2 percussive, v3 density.</summary>
        public static StreamEntry Timbre(double t, double stamp, double brightness, double flatness,
            double percussive, double density)
        {
            var e = Of(EntryKind.Timbre, t, stamp);
            e.V0 = brightness;
            e.V1 = flatness;
            e.V2 = percussive;
            e.V3 = density;
            return e;
        }

        /// <summary>A structure change. v0 strength.</summary>
        public static StreamEntry Change(double t, double stamp, double strength)
        {
            var e = Of(EntryKind.Change, t, stamp);
            e.V0 = strength;
            return e;
        }

        /// <summary>A loudness jump. v0 its size in dB.</summary>
        public static StreamEntry Jump(double t, double stamp, double db)
        {
            var e = Of(EntryKind.Jump, t, stamp);
            e.V0 = db;
            return e;
        }

        /// <summary>A confirmed song end. No slots.</summary>
        public static StreamEntry End(double t, double stamp) => Of(EntryKind.End, t, stamp);

        /// <summary>An accent forecast. v0 its cycle (2, 4 or 8 beats), v1 confidence.</summary>
        public static StreamEntry Forecast(double t, double stamp, double cycle, double confidence)
        {
            var e = Of(EntryKind.Forecast, t, stamp);
            e.V0 = cycle;
            e.V1 = confidence;
            return e;
        }

        void Expect(EntryKind kind)
        {
            if (Kind != kind) throw new InvalidOperationException($"a {Kind} entry read as a {kind}");
        }

        public BeatEntry AsBeat { get { Expect(EntryKind.Beat); return new BeatEntry(this); } }
        public TickEntry AsTick { get { Expect(EntryKind.Tick); return new TickEntry(this); } }
        public OnsetEntry AsOnset { get { Expect(EntryKind.Onset); return new OnsetEntry(this); } }
        public HindsightEntry AsHindsight { get { Expect(EntryKind.Hindsight); return new HindsightEntry(this); } }
        public HindsightBeatEntry AsHindsightBeat { get { Expect(EntryKind.HindsightBeat); return new HindsightBeatEntry(this); } }
        public TimbreEntry AsTimbre { get { Expect(EntryKind.Timbre); return new TimbreEntry(this); } }
        public ChangeEntry AsChange { get { Expect(EntryKind.Change); return new ChangeEntry(this); } }
        public JumpEntry AsJump { get { Expect(EntryKind.Jump); return new JumpEntry(this); } }
        public ForecastEntry AsForecast { get { Expect(EntryKind.Forecast); return new ForecastEntry(this); } }
    }
}
