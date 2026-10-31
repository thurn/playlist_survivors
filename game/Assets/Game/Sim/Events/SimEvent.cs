namespace PS.Game.Sim
{
    /// <summary>
    /// What the sim tells presentation (unity-game.md, "The Sim's Contract"). The skeleton emits
    /// beats, stride steps and beat-state changes; the rest are declared, with their fields, for
    /// the rule beads to emit and the client to build against.
    /// </summary>
    public enum SimEventKind
    {
        None = 0,
        Beat,
        StrideStep,
        BeatState,
        Attack,
        EnemyStep,
        Hit,
        Death,
        StructureBreak,
        Regrowth,
        Spawn,
        Pickup,
        LevelUp,
        Chest,
        Freeze,
        SoundCue,
    }

    /// <summary>The beat's three states (unity-game.md, "Beats, Bars and Reliability").</summary>
    public enum Reliability
    {
        /// <summary>No committed beat ahead: the hero strides on a free pulse, enemies hold.</summary>
        NoBeatAhead = 0,
        /// <summary>Beats known ahead, but below the gate or the music unclear: enemies hold.</summary>
        BelowGate = 1,
        /// <summary>Reliable beats: enemies step on main beats.</summary>
        Reliable = 2,
    }

    public enum FreezeReason { LevelUp, Chest, Pause, MusicStopped }

    /// <summary>
    /// One settled event: dense and blittable, so the log is a plain array and its hash a pass over
    /// fields. <see cref="T"/> is the song time it fired at; the slots' meaning depends on
    /// <see cref="Kind"/>, as each factory documents, and is read through the kind's view. Positions
    /// are ground-plane metres (x, y), ids index the sim's dense arrays, and -1 means none.
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public double T;
        public int I0, I1, I2;
        public double D0, D1, D2, D3, D4, D5;

        static SimEvent Of(SimEventKind kind, double t) => new SimEvent { Kind = kind, T = t };

        /// <summary>A committed beat reached: I0 beat index, I1 bar (-1 before any lock), I2 downbeat (0/1) | reliability &lt;&lt; 1; D0 period, D1 evidence.</summary>
        public static SimEvent Beat(double t, int index, int bar, bool downbeat, Reliability reliability, double period, double evidence)
        {
            var e = Of(SimEventKind.Beat, t);
            e.I0 = index;
            e.I1 = bar;
            e.I2 = (downbeat ? 1 : 0) | ((int)reliability << 1);
            e.D0 = period;
            e.D1 = evidence;
            return e;
        }

        /// <summary>A stride step: I0 step index, I1 halvings (-1 on the free pulse), I2 the segment's first beat (-1 free); D0 period to the next step.</summary>
        public static SimEvent StrideStep(double t, int index, int halvings, int beat, double period)
        {
            var e = Of(SimEventKind.StrideStep, t);
            e.I0 = index;
            e.I1 = halvings;
            e.I2 = beat;
            e.D0 = period;
            return e;
        }

        /// <summary>The beat's state changed: I0 the new state, I1 the previous.</summary>
        public static SimEvent BeatState(double t, Reliability now, Reliability before)
        {
            var e = Of(SimEventKind.BeatState, t);
            e.I0 = (int)now;
            e.I1 = (int)before;
            return e;
        }

        /// <summary>An attack's wind-up starts: I0 attacker (-1 the hero), I1 attack kind; D0 wind-up start, D1 impact time, D2/D3 direction.</summary>
        public static SimEvent Attack(double t, int attacker, int kind, double windupStart, double impact, double dirX, double dirY)
        {
            var e = Of(SimEventKind.Attack, t);
            e.I0 = attacker;
            e.I1 = kind;
            e.D0 = windupStart;
            e.D1 = impact;
            e.D2 = dirX;
            e.D3 = dirY;
            return e;
        }

        /// <summary>An enemy step decided: I0 enemy; D0/D1 from, D2/D3 to, D4 move start, D5 footplant.</summary>
        public static SimEvent EnemyStep(double t, int enemy, double fromX, double fromY, double toX, double toY, double moveStart, double footplant)
        {
            var e = Of(SimEventKind.EnemyStep, t);
            e.I0 = enemy;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = moveStart;
            e.D5 = footplant;
            return e;
        }

        /// <summary>A hit: I0 target (-1 the hero), I1 source attack kind; D0 damage, D1/D2 position, D3/D4 direction.</summary>
        public static SimEvent Hit(double t, int target, int source, double damage, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.Hit, t);
            e.I0 = target;
            e.I1 = source;
            e.D0 = damage;
            e.D1 = x;
            e.D2 = y;
            e.D3 = dirX;
            e.D4 = dirY;
            return e;
        }

        /// <summary>A death: I0 enemy (-1 the hero), I1 role; D0/D1 position, D2/D3 impact direction, D4 force.</summary>
        public static SimEvent Death(double t, int enemy, int role, double x, double y, double dirX, double dirY, double force)
        {
            var e = Of(SimEventKind.Death, t);
            e.I0 = enemy;
            e.I1 = role;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            e.D4 = force;
            return e;
        }

        /// <summary>A structure shatters: I0 structure, I1/I2 its tile; D0/D1 impact direction, D2 force.</summary>
        public static SimEvent StructureBreak(double t, int structure, int tileX, int tileY, double dirX, double dirY, double force)
        {
            var e = Of(SimEventKind.StructureBreak, t);
            e.I0 = structure;
            e.I1 = tileX;
            e.I2 = tileY;
            e.D0 = dirX;
            e.D1 = dirY;
            e.D2 = force;
            return e;
        }

        /// <summary>A structure grows back: I0 structure, I1/I2 its tile.</summary>
        public static SimEvent Regrowth(double t, int structure, int tileX, int tileY)
        {
            var e = Of(SimEventKind.Regrowth, t);
            e.I0 = structure;
            e.I1 = tileX;
            e.I2 = tileY;
            return e;
        }

        /// <summary>An enemy spawns: I0 enemy, I1 role; D0/D1 position.</summary>
        public static SimEvent Spawn(double t, int enemy, int role, double x, double y)
        {
            var e = Of(SimEventKind.Spawn, t);
            e.I0 = enemy;
            e.I1 = role;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>The hero picks up an item: I0 item, I1 item kind; D0/D1 position, D2 value.</summary>
        public static SimEvent Pickup(double t, int item, int itemKind, double x, double y, double value)
        {
            var e = Of(SimEventKind.Pickup, t);
            e.I0 = item;
            e.I1 = itemKind;
            e.D0 = x;
            e.D1 = y;
            e.D2 = value;
            return e;
        }

        /// <summary>A level-up: I0 the new level, I1 the offer's index.</summary>
        public static SimEvent LevelUp(double t, int level, int offer)
        {
            var e = Of(SimEventKind.LevelUp, t);
            e.I0 = level;
            e.I1 = offer;
            return e;
        }

        /// <summary>A chest opens: I0 chest, I1 tier; D0/D1 position.</summary>
        public static SimEvent Chest(double t, int chest, int tier, double x, double y)
        {
            var e = Of(SimEventKind.Chest, t);
            e.I0 = chest;
            e.I1 = tier;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>The world freezes or resumes: I0 reason, I1 1 when it starts, 0 when it ends.</summary>
        public static SimEvent Freeze(double t, FreezeReason reason, bool starts)
        {
            var e = Of(SimEventKind.Freeze, t);
            e.I0 = (int)reason;
            e.I1 = starts ? 1 : 0;
            return e;
        }

        /// <summary>A sound to schedule: I0 cue, I1 variant; D0 its song time, D1 pitch (semitones), D2 gain, D3/D4 position.</summary>
        public static SimEvent SoundCue(double t, int cue, int variant, double at, double pitch, double gain, double x, double y)
        {
            var e = Of(SimEventKind.SoundCue, t);
            e.I0 = cue;
            e.I1 = variant;
            e.D0 = at;
            e.D1 = pitch;
            e.D2 = gain;
            e.D3 = x;
            e.D4 = y;
            return e;
        }

        void Expect(SimEventKind kind)
        {
            if (Kind != kind) throw new System.InvalidOperationException($"a {Kind} event read as a {kind}");
        }

        public BeatEvent AsBeat { get { Expect(SimEventKind.Beat); return new BeatEvent(this); } }
        public StrideStepEvent AsStrideStep { get { Expect(SimEventKind.StrideStep); return new StrideStepEvent(this); } }
        public BeatStateEvent AsBeatState { get { Expect(SimEventKind.BeatState); return new BeatStateEvent(this); } }
        public AttackEvent AsAttack { get { Expect(SimEventKind.Attack); return new AttackEvent(this); } }
        public EnemyStepEvent AsEnemyStep { get { Expect(SimEventKind.EnemyStep); return new EnemyStepEvent(this); } }
        public HitEvent AsHit { get { Expect(SimEventKind.Hit); return new HitEvent(this); } }
        public DeathEvent AsDeath { get { Expect(SimEventKind.Death); return new DeathEvent(this); } }
        public StructureBreakEvent AsStructureBreak { get { Expect(SimEventKind.StructureBreak); return new StructureBreakEvent(this); } }
        public RegrowthEvent AsRegrowth { get { Expect(SimEventKind.Regrowth); return new RegrowthEvent(this); } }
        public SpawnEvent AsSpawn { get { Expect(SimEventKind.Spawn); return new SpawnEvent(this); } }
        public PickupEvent AsPickup { get { Expect(SimEventKind.Pickup); return new PickupEvent(this); } }
        public LevelUpEvent AsLevelUp { get { Expect(SimEventKind.LevelUp); return new LevelUpEvent(this); } }
        public ChestEvent AsChest { get { Expect(SimEventKind.Chest); return new ChestEvent(this); } }
        public FreezeEvent AsFreeze { get { Expect(SimEventKind.Freeze); return new FreezeEvent(this); } }
        public SoundCueEvent AsSoundCue { get { Expect(SimEventKind.SoundCue); return new SoundCueEvent(this); } }
    }
}
