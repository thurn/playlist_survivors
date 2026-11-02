namespace Champions.Sim
{
    /// <summary>
    /// What the sim tells the client (plan.md, "The Sim", Shape): the beats and the beat's state, the
    /// attack cadence's strikes and changes, quiet moments, the run's start, holds and end, the hero's
    /// moves, dashes, hits, heals and death, the attacks: the build, swings, blows, waves, hits and kills on
    /// the enemies, and the horde: spawns, departures, formed waves, elite moves and spores. The systems'
    /// beads add their kinds after these.
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
        Upgrade,
        Swing,
        SwingHold,
        Strike,
        Wave,
        Hit,
        Kill,
        Orbit,
        EnemySpawn,
        DashEcho,
        EnemyGone,
        HordeWave,
        EliteTelegraph,
        EliteMove,
        Spore,
        SporeEnd,
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

        /// <summary>An attack or mod was set to a rank: I0 its <see cref="UpgradeGroup"/>, I1 its id, I2 the rank; D0 the rank before (0: new).</summary>
        public static SimEvent Upgrade(double t, in Sim.Upgrade u, int before)
        {
            var e = Of(SimEventKind.Upgrade, t);
            e.I0 = (int)u.Group;
            e.I1 = u.Id;
            e.I2 = u.Rank;
            e.D0 = before;
            return e;
        }

        /// <summary>
        /// Sword Slash's swing is planned, its aim held from now: the client starts the hero's clip so its impact
        /// lands on D0, the strike's time. D1/D2 the aim (a unit vector); I0 1 for a spin; I1 Sword Slash's rank.
        /// </summary>
        public static SimEvent Swing(double t, double impact, double aimX, double aimY, bool spin, int rank)
        {
            var e = Of(SimEventKind.Swing, t);
            e.D0 = impact;
            e.D1 = aimX;
            e.D2 = aimY;
            e.I0 = spin ? 1 : 0;
            e.I1 = rank;
            return e;
        }

        /// <summary>Nothing is within reach: the hero holds a ready stance from now, skipping the strike at D0, until its next <see cref="Swing"/>.</summary>
        public static SimEvent SwingHold(double t, double impact)
        {
            var e = Of(SimEventKind.SwingHold, t);
            e.D0 = impact;
            return e;
        }

        /// <summary>
        /// An attack's blow (or a copy of a dash's) landed, after its mods: I0 its <see cref="BlowSource"/>, I1 its
        /// <see cref="BlowFlags"/> | its <see cref="ShapeKind"/> &lt;&lt; 8, I2 the targets it struck; D0/D1 its centre (a
        /// path's start), D2/D3 a path's end (a cone's facing), D4 its radius, D5 a cone's half angle (a ring's inner
        /// radius), as <see cref="DashPart"/> gives a dash's.
        /// </summary>
        public static SimEvent Strike(double t, in Blow b, int struck)
        {
            var e = Of(SimEventKind.Strike, t);
            var s = b.Shape;
            e.I0 = (int)b.Source;
            e.I1 = (int)b.Flags | (int)s.Kind << 8;
            e.I2 = struck;
            e.D0 = s.X;
            e.D1 = s.Y;
            e.D2 = s.Kind == ShapeKind.Cone ? s.DirX : s.X2;
            e.D3 = s.Kind == ShapeKind.Cone ? s.DirY : s.Y2;
            e.D4 = s.Radius;
            e.D5 = s.Kind == ShapeKind.Cone ? s.HalfArc : s.Inner;
            return e;
        }

        /// <summary>
        /// A wave set off from D0/D1: I0 its <see cref="BlowSource"/>, I1 its <see cref="BlowFlags"/>, I2 its slot;
        /// D2 its heading (radians from +x; NaN a ring), D3 its half width, D4 its range, D5 its speed (m/s).
        /// </summary>
        public static SimEvent Wave(double t, BlowSource source, BlowFlags flags, int slot, double x, double y, double heading,
            double halfWidth, double range, double speed)
        {
            var e = Of(SimEventKind.Wave, t);
            e.I0 = (int)source;
            e.I1 = (int)flags;
            e.I2 = slot;
            e.D0 = x;
            e.D1 = y;
            e.D2 = heading;
            e.D3 = halfWidth;
            e.D4 = range;
            e.D5 = speed;
            return e;
        }

        /// <summary>
        /// An enemy hit and still standing: I0 the enemy, I1 the blow's <see cref="BlowSource"/>, I2 its
        /// <see cref="BlowFlags"/>; D0/D1 the way the blow throws it (a unit vector), D2 its power (the damage
        /// dealt, in Sword Slash's base hits), D3 the health left.
        /// </summary>
        public static SimEvent Hit(double t, int enemy, BlowSource source, BlowFlags flags, double dirX, double dirY, double power, double health)
        {
            var e = Of(SimEventKind.Hit, t);
            e.I0 = enemy;
            e.I1 = (int)source;
            e.I2 = (int)flags;
            e.D0 = dirX;
            e.D1 = dirY;
            e.D2 = power;
            e.D3 = health;
            return e;
        }

        /// <summary>
        /// An enemy killed, on the impact frame: I0 the enemy, I1 the blow's <see cref="BlowSource"/>, I2 its
        /// <see cref="BlowFlags"/>; D0/D1 the way the blow throws its shards, D2 its power, D3/D4 where it died.
        /// </summary>
        public static SimEvent Kill(double t, int enemy, BlowSource source, BlowFlags flags, double dirX, double dirY, double power, double x, double y)
        {
            var e = Of(SimEventKind.Kill, t);
            e.I0 = enemy;
            e.I1 = (int)source;
            e.I2 = (int)flags;
            e.D0 = dirX;
            e.D1 = dirY;
            e.D2 = power;
            e.D3 = x;
            e.D4 = y;
            return e;
        }

        /// <summary>
        /// Orbit Blades' ring changed (swords, rank or speed): I0 its swords, I1 its rank (0 gone); D0 the first
        /// sword's angle at T (radians from +x, the rest evenly after it), D1 its angular speed (rad/s), D2 its
        /// radius at T. Between these the ring turns steadily; its radius follows the rank's rule.
        /// </summary>
        public static SimEvent Orbit(double t, int swords, int rank, double angle, double speed, double radius)
        {
            var e = Of(SimEventKind.Orbit, t);
            e.I0 = swords;
            e.I1 = rank;
            e.D0 = angle;
            e.D1 = speed;
            e.D2 = radius;
            return e;
        }

        /// <summary>An enemy appeared: I0 its id, I1 its type, I2 1 for a boss; D0/D1 where, D2 its radius, D3 its health.</summary>
        public static SimEvent EnemySpawn(double t, int id, int type, bool boss, double x, double y, double radius, double health)
        {
            var e = Of(SimEventKind.EnemySpawn, t);
            e.I0 = id;
            e.I1 = type;
            e.I2 = boss ? 1 : 0;
            e.D0 = x;
            e.D1 = y;
            e.D2 = radius;
            e.D3 = health;
            return e;
        }

        /// <summary>
        /// An afterimage (I0 -1) or phantom (I0 its index) repeats a dash's move from now: D0/D1 to D2/D3 over D4
        /// seconds. Drawn only: its hits come as their own strikes.
        /// </summary>
        public static SimEvent DashEcho(double t, int who, double fromX, double fromY, double toX, double toY, double duration)
        {
            var e = Of(SimEventKind.DashEcho, t);
            e.I0 = who;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = duration;
            return e;
        }

        /// <summary>A mob was left far behind and goes, without a shatter (the ring brings another): I0 its id; D0/D1 where.</summary>
        public static SimEvent EnemyGone(double t, int id, double x, double y)
        {
            var e = Of(SimEventKind.EnemyGone, t);
            e.I0 = id;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>
        /// A formed wave entered (its mobs' spawns follow it): I0 its <see cref="WaveKind"/>, I1 how many, I2 their
        /// cast type; D0/D1 its centre (a line's middle, a swarm's), D2/D3 its heading (a line's march).
        /// </summary>
        public static SimEvent HordeWave(double t, WaveKind kind, int count, int type, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.HordeWave, t);
            e.I0 = (int)kind;
            e.I1 = count;
            e.I2 = type;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            return e;
        }

        /// <summary>
        /// An elite's or mushroom's telegraph starts filling at D0 and its move lands at D1 (as planned; on the beat,
        /// the beat I2, else -1): I0 the enemy, I1 its <see cref="Sim.EliteMove"/>; D2/D3 where it stands, D4/D5 its
        /// aim (a unit vector: a charge's line).
        /// </summary>
        public static SimEvent EliteTelegraph(double t, int enemy, EliteMove move, in Telegraph telegraph, double x, double y,
            double aimX, double aimY)
        {
            var e = Of(SimEventKind.EliteTelegraph, t);
            e.I0 = enemy;
            e.I1 = (int)move;
            e.I2 = telegraph.LandBeat;
            e.D0 = telegraph.Start;
            e.D1 = telegraph.Land;
            e.D2 = x;
            e.D3 = y;
            e.D4 = aimX;
            e.D5 = aimY;
            return e;
        }

        /// <summary>
        /// An elite's or mushroom's move landed on its beat, or was called off (I2 1: the world held, or it died):
        /// I0 the enemy, I1 its <see cref="Sim.EliteMove"/>; D0/D1 where it stood, D2/D3 a charge's end (a spore's
        /// range end; a slam's centre).
        /// </summary>
        public static SimEvent EliteMove(double t, int enemy, EliteMove move, bool calledOff, double x, double y, double toX, double toY)
        {
            var e = Of(SimEventKind.EliteMove, t);
            e.I0 = enemy;
            e.I1 = (int)move;
            e.I2 = calledOff ? 1 : 0;
            e.D0 = x;
            e.D1 = y;
            e.D2 = toX;
            e.D3 = toY;
            return e;
        }

        /// <summary>A spore fired: I0 its number, I1 the mushroom; D0/D1 from, D2/D3 its velocity, D4 its range.</summary>
        public static SimEvent Spore(double t, int spore, int owner, double x, double y, double vx, double vy, double range)
        {
            var e = Of(SimEventKind.Spore, t);
            e.I0 = spore;
            e.I1 = owner;
            e.D0 = x;
            e.D1 = y;
            e.D2 = vx;
            e.D3 = vy;
            e.D4 = range;
            return e;
        }

        /// <summary>A spore ended: I0 its number, I1 why (<see cref="Sim.SporeEnd"/>); D0/D1 where.</summary>
        public static SimEvent SporeEnd(double t, int spore, SporeEnd why, double x, double y)
        {
            var e = Of(SimEventKind.SporeEnd, t);
            e.I0 = spore;
            e.I1 = (int)why;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        // Reading a beat's flags.
        public bool Downbeat => (I2 & 1) != 0;
        public bool Strong => (I2 & 2) != 0;
        public Reliability BeatReliability => (Reliability)(I2 >> 2);
    }
}
