namespace Champions.Sim
{
    /// <summary>
    /// What the sim tells the client (plan.md, "The Sim", Shape): the beats and the beat's state, the
    /// attack cadence's strikes and changes, quiet moments, the run's start, holds and end, the hero's
    /// moves, dashes, hits, heals and death. The systems' beads add their kinds after these.
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
        HeroDash,
        DashDropped,
        DashPart,
        DashEquip,
        HeroHit,
        HeroHeal,
        HeroDeath,
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

        /// <summary>
        /// A dash started, taken at <see cref="T"/> and drawn from D0 (its press's heard time, or the moment
        /// the cooldown ended for a buffered press): I0 the run's dash index, I1 the dash type
        /// (<see cref="DashRules.Kinds"/>) | rank &lt;&lt; 8 (4 the evolution), I2 flags: 1 on the beat | 2 buffered |
        /// <see cref="JudgedBy"/> &lt;&lt; 2; D1 its end, D2/D3 where it started, D4/D5 its direction.
        /// </summary>
        public static SimEvent HeroDash(double t, int index, int kind, int rank, JudgedBy by, bool buffered,
            double start, double end, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.HeroDash, t);
            e.I0 = index;
            e.I1 = kind | rank << 8;
            e.I2 = (by != JudgedBy.Off ? 1 : 0) | (buffered ? 2 : 0) | (int)by << 2;
            e.D0 = start;
            e.D1 = end;
            e.D2 = x;
            e.D3 = y;
            e.D4 = dirX;
            e.D5 = dirY;
            return e;
        }

        /// <summary>A press came too early in the cooldown and was dropped (the pip blinks): D0 when it was heard, D1 when the dash is ready.</summary>
        public static SimEvent DashDropped(double t, double heard, double ready)
        {
            var e = Of(SimEventKind.DashDropped, t);
            e.D0 = heard;
            e.D1 = ready;
            return e;
        }

        /// <summary>
        /// A dash's blow lands (a path part: starts its sweep, drawn along the planned path) at <see cref="T"/>:
        /// I0 the dash's run index, I1 the part (<see cref="DashForm.Parts"/>, then the mods' extra parts), I2 1
        /// on the beat | the shape's <see cref="ShapeKind"/> &lt;&lt; 1; D0/D1 its centre (a path's start), D2/D3 a
        /// path's end (a cone's facing), D4 its radius (sized for the beat), D5 its damage.
        /// </summary>
        public static SimEvent DashPart(double t, int dash, int part, bool onBeat, in HitShape shape, double damage)
        {
            var e = Of(SimEventKind.DashPart, t);
            e.I0 = dash;
            e.I1 = part;
            e.I2 = (onBeat ? 1 : 0) | (int)shape.Kind << 1;
            e.D0 = shape.X;
            e.D1 = shape.Y;
            e.D2 = shape.Kind == ShapeKind.Cone ? shape.DirX : shape.X2;
            e.D3 = shape.Kind == ShapeKind.Cone ? shape.DirY : shape.Y2;
            e.D4 = shape.Radius;
            e.D5 = damage;
            return e;
        }

        /// <summary>The hero's dash changed: I0 the dash type, I1 its rank (4 the evolution).</summary>
        public static SimEvent DashEquip(double t, int kind, int rank)
        {
            var e = Of(SimEventKind.DashEquip, t);
            e.I0 = kind;
            e.I1 = rank;
            return e;
        }

        /// <summary>
        /// A hit on the hero settled, landing at <see cref="T"/>: I0 how (<see cref="HitOutcome"/>), I1 what
        /// (<see cref="HitSource"/>), I2 the attacker (-1 none); D0 the damage taken, D1 the health after, D2/D3
        /// where it came from, D4 the dash whose i-frames dodged it (-1 none).
        /// </summary>
        public static SimEvent HeroHit(double t, HitOutcome outcome, HitSource source, int owner, double taken, double health,
            double fromX, double fromY, int dash)
        {
            var e = Of(SimEventKind.HeroHit, t);
            e.I0 = (int)outcome;
            e.I1 = (int)source;
            e.I2 = owner;
            e.D0 = taken;
            e.D1 = health;
            e.D2 = fromX;
            e.D3 = fromY;
            e.D4 = dash;
            return e;
        }

        /// <summary>The hero healed: I0 why (<see cref="HealReason"/>); D0 the health gained, D1 the health after.</summary>
        public static SimEvent HeroHeal(double t, HealReason reason, double gained, double health)
        {
            var e = Of(SimEventKind.HeroHeal, t);
            e.I0 = (int)reason;
            e.D0 = gained;
            e.D1 = health;
            return e;
        }

        /// <summary>The hero died on the killing hit's frame (it shatters): D0/D1 where it was, D2/D3 where the hit came from.</summary>
        public static SimEvent HeroDeath(double t, double x, double y, double fromX, double fromY)
        {
            var e = Of(SimEventKind.HeroDeath, t);
            e.D0 = x;
            e.D1 = y;
            e.D2 = fromX;
            e.D3 = fromY;
            return e;
        }

        // Reading a dash's fields.
        public int DashType => I1 & 0xff;
        public int DashRank => I1 >> 8;
        public bool OnBeat => (I2 & 1) != 0;
        public bool Buffered => (I2 & 2) != 0;
        public JudgedBy Judged => (JudgedBy)(I2 >> 2);

        // Reading a beat's flags.
        public bool Downbeat => (I2 & 1) != 0;
        public bool Strong => (I2 & 2) != 0;
        public Reliability BeatReliability => (Reliability)(I2 >> 2);
    }
}
