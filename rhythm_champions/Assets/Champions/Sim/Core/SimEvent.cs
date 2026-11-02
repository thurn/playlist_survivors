namespace Champions.Sim
{
    /// <summary>
    /// What the sim tells the client (plan.md, "The Sim", Shape): the beats and the beat's state, the
    /// attack cadence's strikes and changes, quiet moments, the run's start, holds and end, and the
    /// hero's moves. The systems' beads add their kinds after these.
    /// </summary>
    public enum SimEventKind
    {
        None = 0,
        Beat,
        BeatState,
        Pulse,
        Cadence,
        Quiet,
        RunStart,
        Hold,
        RunEnd,
        HeroMove,
    }

    /// <summary>
    /// One settled event: dense and blittable, so the log is a plain array and its hash a pass over
    /// fields. <see cref="T"/> is the song time it fired at; the slots' meaning depends on
    /// <see cref="Kind"/>, as each factory documents. Positions are ground-plane metres (x right, y up
    /// the screen), ids index the sim's dense arrays, and -1 means none.
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public double T;
        public int I0, I1, I2;
        public double D0, D1, D2, D3, D4, D5;

        static SimEvent Of(SimEventKind kind, double t) => new SimEvent { Kind = kind, T = t };

        /// <summary>
        /// A committed beat reached: I0 beat index, I1 bar (-1 before any lock), I2 flags: 1 downbeat |
        /// 2 strong (a strong onset on it) | reliability &lt;&lt; 2; D0 its period, D1 evidence, D2 the tempo
        /// (<see cref="BeatServices.TempoAt"/>).
        /// </summary>
        public static SimEvent Beat(double t, int index, int bar, bool downbeat, bool strong, Reliability reliability,
            double period, double evidence, double tempo)
        {
            var e = Of(SimEventKind.Beat, t);
            e.I0 = index;
            e.I1 = bar;
            e.I2 = (downbeat ? 1 : 0) | (strong ? 2 : 0) | ((int)reliability << 2);
            e.D0 = period;
            e.D1 = evidence;
            e.D2 = tempo;
            return e;
        }

        /// <summary>The beat's state changed: I0 the new state, I1 the previous (<see cref="Reliability"/>).</summary>
        public static SimEvent BeatState(double t, Reliability now, Reliability before)
        {
            var e = Of(SimEventKind.BeatState, t);
            e.I0 = (int)now;
            e.I1 = (int)before;
            return e;
        }

        /// <summary>The attack cadence strikes: I0 the beat (-1 on the seconds pulse), I1 its count (0 on the pulse); D0 its period, s.</summary>
        public static SimEvent Pulse(double t, int beat, int count, double periodS)
        {
            var e = Of(SimEventKind.Pulse, t);
            e.I0 = beat;
            e.I1 = count;
            e.D0 = periodS;
            return e;
        }

        /// <summary>The attack cadence's period changed: I0 the new count (0 the pulse), I1 the previous; D0 the lock tempo (NaN on the pulse).</summary>
        public static SimEvent Cadence(double t, int count, int before, double lockPeriod)
        {
            var e = Of(SimEventKind.Cadence, t);
            e.I0 = count;
            e.I1 = before;
            e.D0 = lockPeriod;
            return e;
        }

        /// <summary>A quiet moment began, changed reason or ended: I0 the reason now (<see cref="QuietReason"/>, 0 ended), I1 the previous; D0 since when.</summary>
        public static SimEvent Quiet(double t, QuietReason now, QuietReason before, double since)
        {
            var e = Of(SimEventKind.Quiet, t);
            e.I0 = (int)now;
            e.I1 = (int)before;
            e.D0 = since;
            return e;
        }

        /// <summary>The run started.</summary>
        public static SimEvent RunStart(double t) => Of(SimEventKind.RunStart, t);

        /// <summary>A hold began or ended: I0 the reason (<see cref="HoldReason"/>), I1 1 on, 0 off, I2 the holds now (a bit per reason).</summary>
        public static SimEvent Hold(double t, HoldReason reason, bool on, int holds)
        {
            var e = Of(SimEventKind.Hold, t);
            e.I0 = (int)reason;
            e.I1 = on ? 1 : 0;
            e.I2 = holds;
            return e;
        }

        /// <summary>The run ended: I0 why (<see cref="RunEnd"/>); D0 the run clock.</summary>
        public static SimEvent RunEnd(double t, RunEnd why, double runClock)
        {
            var e = Of(SimEventKind.RunEnd, t);
            e.I0 = (int)why;
            e.D0 = runClock;
            return e;
        }

        /// <summary>
        /// The hero's velocity changed, taken at <see cref="T"/> and drawn from D0 (up to the sim's lead
        /// earlier: a heard input's re-walk): D1/D2 where it was then, D3/D4 its velocity (m/s).
        /// </summary>
        public static SimEvent HeroMove(double t, double start, double x, double y, double vx, double vy)
        {
            var e = Of(SimEventKind.HeroMove, t);
            e.D0 = start;
            e.D1 = x;
            e.D2 = y;
            e.D3 = vx;
            e.D4 = vy;
            return e;
        }

        // Reading a beat's flags.
        public bool Downbeat => (I2 & 1) != 0;
        public bool Strong => (I2 & 2) != 0;
        public Reliability BeatReliability => (Reliability)(I2 >> 2);
    }
}
