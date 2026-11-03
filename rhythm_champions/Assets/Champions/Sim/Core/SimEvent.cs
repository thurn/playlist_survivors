namespace Champions.Sim
{
    /// <summary>
    /// What the sim tells the client (plan.md, "The Sim", Shape): the beats and the beat's state, the
    /// attack cadence's strikes and changes, quiet moments, the run's start, holds and end, the hero's
    /// moves, dashes, hits, heals and death, the attacks: the build, swings, blows, waves, hits and kills on
    /// the enemies, the horde: spawns, departures, formed waves, elite moves and spores, the zone key, the structures:
    /// loaded, unloaded, hit and broken, the bosses: calls, entrances, telegraphs, landings, Break, staggers, phases and
    /// deaths, and the progression: levels reached, offers, focus and picks. The systems' beads add their kinds after
    /// these.
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
        Zone,
        StructureSpawn,
        StructureGone,
        StructureHit,
        StructureBreak,
        BossCall,
        BossArrive,
        BossTelegraph,
        BossMove,
        BossBreak,
        BossStagger,
        BossPhase,
        BossDeath,
        LevelUp,
        Offer,
        CardFocus,
        Pick,
        WeaponUpgrade,
        SupportUpgrade,
        WeaponActivation,
        WeaponPulse,
        LandingBlast,
        PickupDrop,
        PickupAttract,
        PickupCollect,
        MeleeAction,
        RangedImpact,
        ConstructAction,
        MagicAction,
        LightningLink,
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

        /// <summary>Destination support: I0 dash index; D0/D1 actual endpoint, D2 radius, D3 damage.</summary>
        public static SimEvent LandingBlast(double t, int dash, double x, double y, double radius, double damage)
        {
            return new SimEvent { Kind = SimEventKind.LandingBlast, T = t, I0 = dash,
                D0 = x, D1 = y, D2 = radius, D3 = damage };
        }

        /// <summary>Pickup lifecycle: I0 stable drop ID, I1 PickupKind; D0/D1 world position,
        /// D2 total value (XP units or fraction of max health). Drop also reports merged totals.</summary>
        public static SimEvent Pickup(SimEventKind kind, double t, Pickup p) => new SimEvent
        { Kind = kind, T = t, I0 = p.Id, I1 = (int)p.Kind, D0 = p.X, D1 = p.Y, D2 = p.Value };

        /// <summary>Magic construct/pulse: I0 weapon, I1 form; I2 spectral bit 0, field placement bits 1–2, empowered bit 3.
        /// D0/D1 anchor, D2/D3 lightning node or field aim, D4 already-scaled coverage, D5 remaining duration.</summary>
        public static SimEvent MagicAction(double t, WeaponKind kind, int form, bool spectral,
            double x, double y, double nx, double ny, double radius, double duration) => new SimEvent
        { Kind = SimEventKind.MagicAction, T = t, I0 = (int)kind, I1 = form, I2 = spectral ? 1 : 0,
            D0 = x, D1 = y, D2 = nx, D3 = ny, D4 = radius, D5 = duration };

        /// <summary>One actual bolt hit: D0/D1 from, D2/D3 endpoint; I0 spectral.</summary>
        public static SimEvent LightningLink(double t, double x, double y, double tx, double ty, bool spectral) => new SimEvent
        { Kind = SimEventKind.LightningLink, T = t, I0 = spectral ? 1 : 0, D0 = x, D1 = y, D2 = tx, D3 = ty };

        /// <summary>Melee choreography: I0 weapon, I1 form, I2 spectral bit 0 / mirrored bit 1 / procession step bits 2+;
        /// D0/D1 origin, D2/D3 aim, D4 coverage radius (already empowered), D5 lane travel.</summary>
        public static SimEvent MeleeAction(double t, WeaponKind kind, int form, int flags,
            double x, double y, double dx, double dy, double radius, double travel)
        {
            var e = Of(SimEventKind.MeleeAction, t);
            e.I0 = (int)kind; e.I1 = form; e.I2 = flags;
            e.D0 = x; e.D1 = y; e.D2 = dx; e.D3 = dy; e.D4 = radius; e.D5 = travel;
            return e;
        }

        /// <summary>Ranged impact footprint: I0 weapon; D0/D1 centre, D2 actual radius including empowerment.</summary>
        public static SimEvent RangedImpact(double t, WeaponKind kind, double x, double y, double radius) =>
            new SimEvent { Kind = SimEventKind.RangedImpact, T = t, I0 = (int)kind, D0 = x, D1 = y, D2 = radius };

        /// <summary>Finite construct sweep/volley: I0 weapon, I1 form, I2 action phase bits 0–1, empowered bit 2;
        /// D0/D1 origin, D2/D3 aim, D4 empowered coverage radius, D5 remaining visual lifetime.</summary>
        public static SimEvent ConstructAction(double t, WeaponKind kind, int form, int phase,
            double x, double y, double dx, double dy, double radius, double duration) => new SimEvent
        { Kind = SimEventKind.ConstructAction, T = t, I0 = (int)kind, I1 = form, I2 = phase,
            D0 = x, D1 = y, D2 = dx, D3 = dy, D4 = radius, D5 = duration };

        static SimEvent Of(SimEventKind kind, double t) => new SimEvent { Kind = kind, T = t };

        /// <summary>Survivor weapon upgrade: I0 WeaponKind, I1 level, I2 zero-based form; D0 previous level.</summary>
        public static SimEvent WeaponUpgrade(double t, WeaponKind kind, int level, int previous)
        {
            var e = Of(SimEventKind.WeaponUpgrade, t);
            e.I0 = (int)kind; e.I1 = level; e.I2 = CombatRules.FormIndex(level); e.D0 = previous;
            return e;
        }

        /// <summary>Survivor support upgrade: I0 SupportKind, I1 level; D0 previous level.</summary>
        public static SimEvent SupportUpgrade(double t, SupportKind kind, int level, int previous)
        {
            var e = Of(SimEventKind.SupportUpgrade, t);
            e.I0 = (int)kind; e.I1 = level; e.D0 = previous;
            return e;
        }

        /// <summary>
        /// Whole survivor activation: I0 WeaponKind, I1 zero-based form, I2 total strikes/pulses;
        /// D0 activation sequence, D1 empowerment area factor (1 or 1.5), D2/D3 origin, D4 heading, D5 cadence damage factor.
        /// Extra strikes and secondary effects retain D0 and D1; they never replay the body faster.
        /// </summary>
        public static SimEvent WeaponActivation(double t, WeaponKind kind, int level, long sequence, int strikes,
            double empowermentArea, double x, double y, double heading, double cadenceDamageFactor = 1)
        {
            var e = Of(SimEventKind.WeaponActivation, t);
            e.I0 = (int)kind; e.I1 = CombatRules.FormIndex(level); e.I2 = strikes;
            e.D0 = sequence; e.D1 = empowermentArea; e.D2 = x; e.D3 = y; e.D4 = heading; e.D5 = cadenceDamageFactor;
            return e;
        }

        /// <summary>Persistent damage pulse: I0 WeaponKind; D0 activation sequence, D1 pulse sequence, D2 empowerment area.</summary>
        public static SimEvent WeaponPulse(double t, WeaponKind kind, long activation, long pulse, double empowermentArea)
        {
            var e = Of(SimEventKind.WeaponPulse, t);
            e.I0 = (int)kind; e.D0 = activation; e.D1 = pulse; e.D2 = empowermentArea;
            return e;
        }

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
        /// <see cref="JudgedBy"/> &lt;&lt; 2 | 256 in-place cleave; D1 its end, D2/D3 where it started, D4/D5 its direction.
        /// </summary>
        public static SimEvent HeroDash(double t, int index, int kind, int rank, JudgedBy by, bool buffered,
            double start, double end, double x, double y, double dirX, double dirY, bool inPlace = false)
        {
            var e = Of(SimEventKind.HeroDash, t);
            e.I0 = index;
            e.I1 = kind | rank << 8;
            e.I2 = (by != JudgedBy.Off ? 1 : 0) | (buffered ? 2 : 0) | (int)by << 2 | (inPlace ? 256 : 0);
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
        public bool InPlaceCleave => Kind == SimEventKind.HeroDash && (I2 & 256) != 0;
        public int DashType => I1 & 0xff;
        public int DashRank => I1 >> 8;
        public bool OnBeat => (I2 & 1) != 0;
        public bool Buffered => (I2 & 2) != 0;
        public JudgedBy Judged => (JudgedBy)((I2 & 255) >> 2);

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

        /// <summary>
        /// The floor's zone key changed (<see cref="Zones"/>), its crossfade starting at <see cref="T"/> (a beat's time, or
        /// the moment with no reliable beat): I0 the key now, I1 the previous, I2 why (<see cref="ZoneReason"/>); D0 the
        /// crossfade's length, s (a beat).
        /// </summary>
        public static SimEvent Zone(double t, int key, int previous, ZoneReason reason, double fadeS)
        {
            var e = Of(SimEventKind.Zone, t);
            e.I0 = key;
            e.I1 = previous;
            e.I2 = (int)reason;
            e.D0 = fadeS;
            return e;
        }

        /// <summary>
        /// A structure stands, its chunk loaded (<see cref="StructureField"/>): I0 the structure, I1 its kind
        /// (<see cref="StructureRules.Kinds"/>), I2 the hits it takes; D0/D1 its middle, D2 its angle (radians from +x,
        /// anticlockwise).
        /// </summary>
        public static SimEvent StructureSpawn(double t, int id, int kind, int hits, double x, double y, double angle)
        {
            var e = Of(SimEventKind.StructureSpawn, t);
            e.I0 = id;
            e.I1 = kind;
            e.I2 = hits;
            e.D0 = x;
            e.D1 = y;
            e.D2 = angle;
            return e;
        }

        /// <summary>A standing structure's chunk unloaded, far from the hero: I0 the structure; D0/D1 its middle. Remove it without a shatter.</summary>
        public static SimEvent StructureGone(double t, int id, double x, double y)
        {
            var e = Of(SimEventKind.StructureGone, t);
            e.I0 = id;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>
        /// A building hit and still standing: I0 the structure, I1 the hits it takes still, I2 the blow's
        /// <see cref="BlowSource"/>; D0/D1 the way the blow would throw it (a unit vector).
        /// </summary>
        public static SimEvent StructureHit(double t, int id, int hitsLeft, BlowSource source, double dirX, double dirY)
        {
            var e = Of(SimEventKind.StructureHit, t);
            e.I0 = id;
            e.I1 = hitsLeft;
            e.I2 = (int)source;
            e.D0 = dirX;
            e.D1 = dirY;
            return e;
        }

        /// <summary>
        /// A structure broke, on the impact frame: I0 the structure, I1 its kind, I2 what broke it (<see cref="BreakCause"/>);
        /// D0/D1 the way its shards are thrown (a unit vector, away from the blow), D2 the blow's power (its damage in
        /// Sword Slash's base hits; 0 for a boss's), D3/D4 its middle.
        /// </summary>
        public static SimEvent StructureBreak(double t, int id, int kind, BreakCause cause, double dirX, double dirY, double power, double x, double y)
        {
            var e = Of(SimEventKind.StructureBreak, t);
            e.I0 = id;
            e.I1 = kind;
            e.I2 = (int)cause;
            e.D0 = dirX;
            e.D1 = dirY;
            e.D2 = power;
            e.D3 = x;
            e.D4 = y;
            return e;
        }

        /// <summary>A boss is called: spawning stops, and it arrives on its downbeat (<see cref="Bosses"/>): I0 its cast index; D0 the run clock.</summary>
        public static SimEvent BossCall(double t, int cast, double runClock)
        {
            var e = Of(SimEventKind.BossCall, t);
            e.I0 = cast;
            e.D0 = runClock;
            return e;
        }

        /// <summary>
        /// A boss lands on its downbeat and its shockwave shatters every mob on screen (their <see cref="Kill"/>s from
        /// <see cref="BlowSource.Boss"/> came just before; mobs off screen went as <see cref="EnemyGone"/>): I0 its enemy id, I1
        /// its cast index, I2 the mobs shattered; D0/D1 where it lands, D2 the way it faces (radians from +x), D3/D4 the
        /// hero's feet (the screen the shockwave cleared).
        /// </summary>
        public static SimEvent BossArrive(double t, int boss, int cast, int shattered, double x, double y, double facing, double heroX, double heroY)
        {
            var e = Of(SimEventKind.BossArrive, t);
            e.I0 = boss;
            e.I1 = cast;
            e.I2 = shattered;
            e.D0 = x;
            e.D1 = y;
            e.D2 = facing;
            e.D3 = heroX;
            e.D4 = heroY;
            return e;
        }

        /// <summary>
        /// A boss's mark (<see cref="BossMark"/>): it starts filling at T (a beat, up to a beat after it was planned) and its
        /// attack lands at D0 as planned (on the beat, beat I2, which re-predicts it; beatless -1). I0 the mark's number,
        /// I1 its <see cref="Sim.BossMove"/> | its part &lt;&lt; 8 | the shape's <see cref="ShapeKind"/> &lt;&lt; 16. The shape: a
        /// circle's centre D1/D2 and radius D3 (a ring adds its inner radius D4); a path's start D1/D2, end D3/D4 and reach
        /// D5; a cone's apex D1/D2, facing D3 (radians from +x), radius D4 and half angle D5.
        /// </summary>
        public static SimEvent BossTelegraph(in BossMark mark)
        {
            var s = mark.Shape;
            var e = Of(SimEventKind.BossTelegraph, mark.Telegraph.Start);
            e.I0 = mark.Id;
            e.I1 = (int)mark.Move | mark.Part << 8 | (int)s.Kind << 16;
            e.I2 = mark.Telegraph.LandBeat;
            e.D0 = mark.Telegraph.Land;
            e.D1 = s.X;
            e.D2 = s.Y;
            switch (s.Kind)
            {
                case ShapeKind.Path:
                    e.D3 = s.X2;
                    e.D4 = s.Y2;
                    e.D5 = s.Radius;
                    break;
                case ShapeKind.Cone:
                    e.D3 = SimMath.Atan2(s.DirY, s.DirX);
                    e.D4 = s.Radius;
                    e.D5 = s.HalfArc;
                    break;
                default:
                    e.D3 = s.Radius;
                    e.D4 = s.Inner;
                    break;
            }
            return e;
        }

        /// <summary>
        /// A boss's mark landed on its beat (T), or was called off (I2 1: a stagger, a hold, the boss's death): I0 the
        /// mark's number, I1 its <see cref="Sim.BossMove"/> | its part &lt;&lt; 8; D0/D1 the boss then, D2/D3 where its move
        /// takes it (the charge's run's end, the dive's landing), D4 how long (the charge's run, the breath's burn), D5 the
        /// breath's sweep (radians, from its facing's clockwise side; 0 still).
        /// </summary>
        public static SimEvent BossMove(double t, in BossMark mark, bool calledOff, double x, double y, double toX, double toY,
            double duration, double sweep)
        {
            var e = Of(SimEventKind.BossMove, t);
            e.I0 = mark.Id;
            e.I1 = (int)mark.Move | mark.Part << 8;
            e.I2 = calledOff ? 1 : 0;
            e.D0 = x;
            e.D1 = y;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = duration;
            e.D5 = sweep;
            return e;
        }

        /// <summary>A perfect dodge filled a boss's Break: I0 the boss; D0 Break now (1: full, the stagger follows), D1 before.</summary>
        public static SimEvent BossBreak(double t, int boss, double now, double before)
        {
            var e = Of(SimEventKind.BossBreak, t);
            e.I0 = boss;
            e.D0 = now;
            e.D1 = before;
            return e;
        }

        /// <summary>
        /// A boss's stagger began (I1 1; its Break emptied) or ended (I1 0): I0 the boss, I2 its last beat (-1 beatless);
        /// D0 its end as planned.
        /// </summary>
        public static SimEvent BossStagger(double t, int boss, bool on, int untilBeat, double until)
        {
            var e = Of(SimEventKind.BossStagger, t);
            e.I0 = boss;
            e.I1 = on ? 1 : 0;
            e.I2 = untilBeat;
            e.D0 = until;
            return e;
        }

        /// <summary>A boss entered a phase (I1: 2): I0 the boss.</summary>
        public static SimEvent BossPhase(double t, int boss, int phase)
        {
            var e = Of(SimEventKind.BossPhase, t);
            e.I0 = boss;
            e.I1 = phase;
            return e;
        }

        /// <summary>
        /// A boss died, its <see cref="Kill"/> just before: I0 the boss, I1 its cast index, I2 what it sets off
        /// (<see cref="Sim.BossDeath"/>: the treasure, or the finale's shatter and the victory); D0/D1 where, D2/D3 the killing
        /// blow's way.
        /// </summary>
        public static SimEvent BossDeath(double t, int boss, int cast, BossDeath death, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.BossDeath, t);
            e.I0 = boss;
            e.I1 = cast;
            e.I2 = (int)death;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            return e;
        }

        /// <summary>
        /// A level reached on a kill's impact frame, held for its quiet moment: I0 the level, I1 the levels held now; D0
        /// the experience carried toward the next, D1 what the next needs.
        /// </summary>
        public static SimEvent LevelUp(double t, int level, int held, double experience, double needed)
        {
            var e = Of(SimEventKind.LevelUp, t);
            e.I0 = level;
            e.I1 = held;
            e.D0 = experience;
            e.D1 = needed;
            return e;
        }

        /// <summary>
        /// Cards opened, the world holding: I0, I1, I2 the cards' <see cref="Card.Code"/>s (-1 past the offer's count),
        /// D0 the <see cref="Sim.OfferKind"/> (<see cref="OfferKindOf"/>).
        /// </summary>
        public static SimEvent Offer(double t, in Sim.Offer offer)
        {
            var e = Of(SimEventKind.Offer, t);
            e.I0 = offer.CodeOf(0);
            e.I1 = offer.CodeOf(1);
            e.I2 = offer.CodeOf(2);
            e.D0 = (int)offer.Kind;
            return e;
        }

        /// <summary>The arrows focused a card: I0 its slot (0-2).</summary>
        public static SimEvent CardFocus(double t, int slot)
        {
            var e = Of(SimEventKind.CardFocus, t);
            e.I0 = slot;
            return e;
        }

        /// <summary>A card was picked (the build's <see cref="Upgrade"/> or <see cref="DashEquip"/> follows): I0 its slot, I1 its <see cref="Card.Code"/>, I2 the offer's <see cref="Sim.OfferKind"/>.</summary>
        public static SimEvent Pick(double t, int slot, in Card card, OfferKind kind)
        {
            var e = Of(SimEventKind.Pick, t);
            e.I0 = slot;
            e.I1 = card.Code;
            e.I2 = (int)kind;
            return e;
        }

        /// <summary>An <see cref="SimEventKind.Offer"/>'s kind.</summary>
        public OfferKind OfferKindOf => (OfferKind)(int)D0;

        // Reading a beat's flags.
        public bool Downbeat => (I2 & 1) != 0;
        public bool Strong => (I2 & 2) != 0;
        public Reliability BeatReliability => (Reliability)(I2 >> 2);

        // Reading a boss mark's move, part and shape (BossTelegraph, BossMove).
        public BossMove MarkMove => (BossMove)(I1 & 0xFF);
        public int MarkPart => (I1 >> 8) & 0xFF;
        public ShapeKind MarkShape => (ShapeKind)((I1 >> 16) & 0xFF);

        /// <summary>A <see cref="SimEventKind.BossTelegraph"/>'s ground, as <see cref="BossTelegraph"/> wrote it.</summary>
        public HitShape MarkGround()
        {
            switch (MarkShape)
            {
                case ShapeKind.Path: return HitShape.Path(D1, D2, D3, D4, D5);
                case ShapeKind.Cone: return HitShape.Cone(D1, D2, SimMath.Cos(D3), SimMath.Sin(D3), D4, D5);
                case ShapeKind.Ring: return HitShape.Ring(D1, D2, D4, D3);
                default: return HitShape.Circle(D1, D2, D3);
            }
        }
    }
}
