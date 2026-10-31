using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    // Typed views of a SimEvent, one per kind: read-only copies naming its slots.

    public readonly struct BeatEvent
    {
        readonly SimEvent e;
        public BeatEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Index => e.I0;
        /// <summary>Bars are two beats from the first committed beat after a lock; -1 before any lock.</summary>
        public int Bar => e.I1;
        public bool Downbeat => (e.I2 & 1) != 0;
        public Reliability Reliability => (Reliability)(e.I2 >> 1);
        public double Period => e.D0;
        public double Evidence => e.D1;
    }

    public readonly struct StrideStepEvent
    {
        readonly SimEvent e;
        public StrideStepEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Index => e.I0;
        public int Halvings => e.I1;
        public bool Free => e.I1 < 0;
        public int Beat => e.I2;
        public double Period => e.D0;
    }

    public readonly struct BeatStateEvent
    {
        readonly SimEvent e;
        public BeatStateEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public Reliability Now => (Reliability)e.I0;
        public Reliability Before => (Reliability)e.I1;
    }

    public readonly struct AttackEvent
    {
        internal const int ShapeShift = 1, WeaponShift = 4;
        readonly SimEvent e;
        public AttackEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public bool ByHero => e.I0 < 0;
        /// <summary>The attacking enemy; -1 for the hero's attacks.</summary>
        public int Enemy => e.I0;
        /// <summary>The hero's weapon: its index in content's weapons; -1 for an enemy's attack.</summary>
        public int Weapon => ByHero ? e.I2 >> WeaponShift : -1;
        /// <summary>The hero's weapon's shape, which says how its kind reads.</summary>
        public ShapeKind Shape => ByHero ? (ShapeKind)((e.I2 >> ShapeShift) & 7) : throw Not("the hero's");
        /// <summary>A sword's strike (the sword or its evolution): the only attacks <see cref="SwordKind"/> reads.</summary>
        public bool IsSword => ByHero && Shape == ShapeKind.Sword;
        public HeroAttackKind SwordKind => IsSword ? (HeroAttackKind)e.I1 : throw Not("a sword's");
        public EnemyAttackKind EnemyKind => !ByHero ? (EnemyAttackKind)e.I1 : throw Not("an enemy's");
        /// <summary>A heavy strike: 2.5x damage, a wider reach and a cleave wave.</summary>
        public bool Heavy => (e.I2 & 1) != 0;
        public double WindupStart => e.D0;
        public double Impact => e.D1;
        public double DirX => e.D2;
        public double DirY => e.D3;
        /// <summary>The hero's strike: how far around it the hit reaches, which the visible strike shows.</summary>
        public double Reach => e.D4;

        static System.InvalidOperationException Not(string whose) => new System.InvalidOperationException($"not {whose} attack");
    }

    public readonly struct EnemyStepEvent
    {
        readonly SimEvent e;
        public EnemyStepEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Enemy => e.I0;
        public double FromX => e.D0;
        public double FromY => e.D1;
        public double ToX => e.D2;
        public double ToY => e.D3;
        public double MoveStart => e.D4;
        public double Footplant => e.D5;
    }

    public readonly struct HitEvent
    {
        readonly SimEvent e;
        public HitEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public bool OnHero => e.I0 < 0;
        /// <summary>The enemy struck; -1 for the hero.</summary>
        public int Target => e.I0;
        /// <summary>The hero's weapon that struck an enemy: its index in content's weapons; -1 on the hero.</summary>
        public int Weapon => OnHero ? -1 : e.I2;
        public WeaponHit WeaponHit => !OnHero ? (WeaponHit)e.I1 : throw new System.InvalidOperationException("a hit on the hero");
        public EnemyAttackKind EnemyKind => OnHero ? (EnemyAttackKind)e.I1 : throw new System.InvalidOperationException("a hit on an enemy");
        public double Damage => e.D0;
        public double X => e.D1;
        public double Y => e.D2;
        public double DirX => e.D3;
        public double DirY => e.D4;
    }

    public readonly struct DeathEvent
    {
        readonly SimEvent e;
        public DeathEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Enemy => e.I0;
        public int Role => e.I1;
        public double X => e.D0;
        public double Y => e.D1;
        public double DirX => e.D2;
        public double DirY => e.D3;
        public double Force => e.D4;
    }

    public readonly struct StructureBreakEvent
    {
        readonly SimEvent e;
        public StructureBreakEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        /// <summary>The level data's structure index (<see cref="SimState.Structures"/>).</summary>
        public int Structure => e.I0;
        public StructureHit Source => (StructureHit)e.I1;
        /// <summary>Where the hit met the structure: shards release outward from here.</summary>
        public double X => e.D0;
        public double Y => e.D1;
        public double DirX => e.D2;
        public double DirY => e.D3;
        /// <summary>How far the hit throws, in metres.</summary>
        public double Force => e.D4;
    }

    public readonly struct GlintEvent
    {
        readonly SimEvent e;
        public GlintEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Structure => e.I0;
        public StructureHit Source => (StructureHit)e.I1;
        /// <summary>The breaking hits a large structure has taken toward its break; 0 for a hit that can't break it.</summary>
        public int Hits => e.I2;
        public double X => e.D0;
        public double Y => e.D1;
        public double DirX => e.D2;
        public double DirY => e.D3;
    }

    public readonly struct RegrowthEvent
    {
        readonly SimEvent e;
        public RegrowthEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Structure => e.I0;
        /// <summary>It erupts from the ground from <see cref="Start"/> (a downbeat, when it blocks its tiles) to <see cref="End"/>, a beat later.</summary>
        public double Start => e.D0;
        public double End => e.D1;
    }

    public readonly struct ZoneChangeEvent
    {
        readonly SimEvent e;
        public ZoneChangeEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        /// <summary>schedule.json's zone index.</summary>
        public int Zone => e.I0;
        public int Previous => e.I1;
        public ZoneCause Cause => (ZoneCause)e.I2;
        /// <summary>The section change's or jump's song time; a fallback's due time.</summary>
        public double CauseT => e.D0;
        public double KnownAt => e.D1;
    }

    public readonly struct SpawnEvent
    {
        readonly SimEvent e;
        public SpawnEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Enemy => e.I0;
        public int Role => e.I1;
        /// <summary>A recycled enemy re-entering on the spawn ring: it leaves its old place at once.</summary>
        public bool Recycled => e.I2 != 0;
        public double X => e.D0;
        public double Y => e.D1;
    }

    public readonly struct PickupEvent
    {
        readonly SimEvent e;
        public PickupEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Item => e.I0;
        public int ItemKind => e.I1;
        public double X => e.D0;
        public double Y => e.D1;
        public double Value => e.D2;
    }

    public readonly struct LevelUpEvent
    {
        readonly SimEvent e;
        public LevelUpEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Level => e.I0;
        public int Offer => e.I1;
    }

    public readonly struct ChestEvent
    {
        readonly SimEvent e;
        public ChestEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Chest => e.I0;
        public int Tier => e.I1;
        public double X => e.D0;
        public double Y => e.D1;
    }

    public readonly struct FreezeEvent
    {
        readonly SimEvent e;
        public FreezeEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public FreezeReason Reason => (FreezeReason)e.I0;
        public bool Starts => e.I1 != 0;
    }

    public readonly struct SoundCueEvent
    {
        readonly SimEvent e;
        public SoundCueEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Cue => e.I0;
        public int Variant => e.I1;
        public double At => e.D0;
        public double Pitch => e.D1;
        public double Gain => e.D2;
        public double X => e.D3;
        public double Y => e.D4;
    }

    public readonly struct HeroStepEvent
    {
        readonly SimEvent e;
        public HeroStepEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        /// <summary>The stride step it belongs to: for a press stride, the step the press took.</summary>
        public int Step => e.I0;
        /// <summary>
        /// A press's stride, taken at once, rather than a held key's on its step (unity-game.md, "Key
        /// Presses"). Either is emitted when the sim takes it (T) and drawn from its start in heard
        /// time (Start), which may precede T by up to the lead.
        /// </summary>
        public bool Pressed => (e.I1 & 1) != 0;
        /// <summary>The tile ahead was blocked: the hero bumped in place.</summary>
        public bool Bump => (e.I1 & 2) != 0;
        public bool Stomp => (e.I1 & 4) != 0;
        public Direction Facing => (Direction)e.I2;
        public double FromX => e.D0;
        public double FromY => e.D1;
        public double ToX => e.D2;
        public double ToY => e.D3;
        public double Start => e.D4;
        public double End => e.D5;
    }

    public readonly struct EnemyKnockEvent
    {
        readonly SimEvent e;
        public EnemyKnockEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Enemy => e.I0;
        public double FromX => e.D0;
        public double FromY => e.D1;
        public double ToX => e.D2;
        public double ToY => e.D3;
        public double Start => e.D4;
        public double End => e.D5;
    }

    public readonly struct CleaveEvent
    {
        readonly SimEvent e;
        public CleaveEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Wave => e.I0;
        public int Bands => e.I1;
        public double X => e.D0;
        public double Y => e.D1;
        public double Inner => e.D2;
        public double Outer => e.D3;
        public double FirstBand => e.D4;
        public double BandSeconds => e.D5;
    }

    public readonly struct DropEvent
    {
        readonly SimEvent e;
        public DropEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Item => e.I0;
        public ItemKind ItemKind => (ItemKind)e.I1;
        public double X => e.D0;
        public double Y => e.D1;
        public double Value => e.D2;
    }
}
