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
        readonly SimEvent e;
        public AttackEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Attacker => e.I0;
        public int Kind => e.I1;
        public double WindupStart => e.D0;
        public double Impact => e.D1;
        public double DirX => e.D2;
        public double DirY => e.D3;
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
        public int Target => e.I0;
        public int Source => e.I1;
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
        public int Structure => e.I0;
        public int TileX => e.I1;
        public int TileY => e.I2;
        public double DirX => e.D0;
        public double DirY => e.D1;
        public double Force => e.D2;
    }

    public readonly struct RegrowthEvent
    {
        readonly SimEvent e;
        public RegrowthEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Structure => e.I0;
        public int TileX => e.I1;
        public int TileY => e.I2;
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
        /// <summary>The stride step it belongs to: a late stride's is the missed step.</summary>
        public int Step => e.I0;
        /// <summary>Applied after its step was simulated, starting at once (unity-game.md, "Key Presses and Late Steps").</summary>
        public bool Late => (e.I1 & 1) != 0;
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
}
