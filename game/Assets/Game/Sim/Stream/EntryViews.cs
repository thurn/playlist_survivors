namespace PS.Game.Sim
{
    public enum LoudnessLevel { Quiet = 0, Medium = 1, Loud = 2 }

    public enum DrumBand { Kick = 0, Snare = 1, Hat = 2, Bass = 3 }

    // Typed views of a StreamEntry, one per kind: read-only copies naming its v slots.

    public readonly struct BeatEntry
    {
        readonly StreamEntry e;
        public BeatEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Period => e.V0;
        public double Evidence => e.V1;
    }

    public readonly struct TickEntry
    {
        readonly StreamEntry e;
        public TickEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Evidence => e.V0;
        public double Energy => e.V1;
        public bool HasLevel => e.V2 >= 0;
        public LoudnessLevel Level => (LoudnessLevel)(int)e.V2;
        /// <summary>0 to 23 (major from C, then minor from C), or -1 with no key.</summary>
        public int Key => (int)e.V3;
        public bool HasBands => !double.IsNaN(e.V4);
        public double Kick => e.V4;
        public double Snare => e.V5;
        public double Hat => e.V6;
        public double Bass => e.V7;
        public double Brightness => e.V8;
    }

    public readonly struct OnsetEntry
    {
        readonly StreamEntry e;
        public OnsetEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Strength => e.V0;
        public bool Strong => e.V1 != 0;
        public bool HasBand => e.V2 >= 0;
        public DrumBand Band => (DrumBand)(int)e.V2;
    }

    public readonly struct HindsightEntry
    {
        readonly StreamEntry e;
        public HindsightEntry(in StreamEntry e) { this.e = e; }
        /// <summary>The window's end.</summary>
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public int OfflineCount => (int)e.V0;
        public int OnlineCount => (int)e.V1;
    }

    public readonly struct HindsightBeatEntry
    {
        readonly StreamEntry e;
        public HindsightBeatEntry(in StreamEntry e) { this.e = e; }
        /// <summary>The beat.</summary>
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public bool Online => e.V0 != 0;
        public double WindowEnd => e.V1;
    }

    public readonly struct TimbreEntry
    {
        readonly StreamEntry e;
        public TimbreEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Brightness => e.V0;
        public double Flatness => e.V1;
        public double Percussive => e.V2;
        public double Density => e.V3;
    }

    public readonly struct ChangeEntry
    {
        readonly StreamEntry e;
        public ChangeEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Strength => e.V0;
    }

    public readonly struct JumpEntry
    {
        readonly StreamEntry e;
        public JumpEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Db => e.V0;
    }

    public readonly struct ForecastEntry
    {
        readonly StreamEntry e;
        public ForecastEntry(in StreamEntry e) { this.e = e; }
        public double T => e.T;
        public double KnownAt => e.KnownAt;
        public double Cycle => e.V0;
        public double Confidence => e.V1;
    }
}
