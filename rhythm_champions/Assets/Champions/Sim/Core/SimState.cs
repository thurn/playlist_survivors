namespace Champions.Sim
{
    /// <summary>
    /// The latest settled state (plan.md, "The Sim", Shape): time, the run, the beat, the attack cadence,
    /// quiet moments, the zone key, the progression and the hero. The client copies what it needs each frame (<see cref="Simulation.Snapshot"/>)
    /// to show song times trailing the frontier. Only the sim writes it: every property is
    /// <c>{ get; internal set; }</c>. The systems' beads add their state after these.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>The frontier: the song time the sim has simulated to.</summary>
        public double Now { get; internal set; }
        public long Tick { get; internal set; }

        public RunPhase Phase { get; internal set; }
        /// <summary>The song time the run started at, or NaN before.</summary>
        public double StartedAt { get; internal set; } = double.NaN;
        /// <summary>Why the run ended, and when (NaN before).</summary>
        public RunEnd End { get; internal set; }
        public double EndedAt { get; internal set; } = double.NaN;

        /// <summary>Playing time: advances with song time while the run plays and nothing holds the world.</summary>
        public double RunClock { get; internal set; }
        /// <summary>Spawn/progression time, frozen from each boss call through its defeat.</summary>
        public double HordeClock { get; internal set; }
        /// <summary>What holds the world now, a bit per <see cref="HoldReason"/>.</summary>
        public int Holds { get; internal set; }

        /// <summary>The world is held: before the start, after the end, or while anything holds it.</summary>
        public bool Held => Phase != RunPhase.Playing || Holds != 0;

        /// <summary>Does <paramref name="reason"/> hold the world now?</summary>
        public bool HeldFor(HoldReason reason) => (Holds & (1 << (int)reason)) != 0;

        /// <summary>The beat's state at the frontier.</summary>
        public Reliability Beat { get; internal set; }
        /// <summary>The latest committed beat fired, or -1.</summary>
        public int LastBeat { get; internal set; } = -1;
        /// <summary>The latest beat's bar (-1 before any lock), and whether it was a downbeat.</summary>
        public int Bar { get; internal set; } = -1;
        public bool Downbeat { get; internal set; }

        /// <summary>The attack cadence's period in beats (0 on the seconds pulse) and its latest strike's time.</summary>
        public int CadenceBeats { get; internal set; }
        public double LastPulse { get; internal set; } = double.NegativeInfinity;

        /// <summary>The quiet moment now (<see cref="QuietReason.None"/> when not quiet), and since when.</summary>
        public QuietReason Quiet { get; internal set; }
        public double QuietSince { get; internal set; } = double.NaN;

        /// <summary>The floor's zone key now (<see cref="Zones"/>).</summary>
        public int Zone { get; internal set; }

        /// <summary>The hero's level (1 at the start), the experience toward the next and what that needs (<see cref="Sim.Progression"/>).</summary>
        public int Level { get; internal set; } = 1;
        public double Experience { get; internal set; }
        public double ExperienceNeeded { get; internal set; }
        /// <summary>Levels reached and held for a quiet moment (the indicator's dot), not counting the cards up.</summary>
        public int HeldLevels { get; internal set; }
        /// <summary>The cards up (<see cref="Offer.Up"/>), and the focused one.</summary>
        public Offer Offer { get; internal set; }

        /// <summary>Survivor build; legacy Arsenal ranks remain an adapter until combat migration.</summary>
        public WeaponBuild Build { get; internal set; }

        public HeroState Hero { get; } = new HeroState();
    }

    /// <summary>
    /// A copy of the state for the client's history (<see cref="Simulation.Snapshot"/>): a plain value, so
    /// keeping a few frames of them allocates nothing.
    /// </summary>
    public struct SimSnapshot
    {
        public double Now;
        public long Tick;
        public RunPhase Phase;
        public double RunClock, HordeClock;
        public int Holds;
        public Reliability Beat;
        public int LastBeat, Bar;
        public bool Downbeat;
        public int CadenceBeats;
        public QuietReason Quiet;
        /// <summary>The hero's chord over the tick ahead and its facing.</summary>
        public double HeroFromX, HeroFromY, HeroX, HeroY, HeroMoveStart, HeroMoveEnd, HeroFacingX, HeroFacingY;
        /// <summary>The health bar's: health, full health, the latest hit that hurt.</summary>
        public double HeroHealth, HeroMaxHealth, HeroHurtAt;
        /// <summary>The dash pip's: the equipped dash and rank, the latest dash's span and beat, the cooldown's end and length, a press waiting.</summary>
        public int Dash, DashRank;
        public double DashStart, DashEnd;
        public bool DashOnBeat;
        public double DashReadyAt, DashCooldownS;
        public bool DashWaiting;
        /// <summary>The hero holds a ready stance (nothing in Sword Slash's reach); its phantoms.</summary>
        public bool Ready;
        public int Phantoms;
        /// <summary>Orbit Blades' swords, the first one's angle (radians from +x) and the ring's radius.</summary>
        public int OrbitSwords;
        public double OrbitAngle, OrbitRadius;
        public int EnemiesAlive;
        /// <summary>The floor's zone key.</summary>
        public int Zone;
        /// <summary>
        /// The boss bar's, through a duel (<see cref="Bosses"/>): the boss's enemy id and cast index (-1 outside a duel; its
        /// colour is its cast slot's), health, full health, Break (0 to 1, the white bar), phase, and its stagger's span
        /// (NaN when not staggered).
        /// </summary>
        public int Boss, BossCast;
        public double BossHealth, BossMaxHealth, BossBreak, BossElapsed;
        public int BossPhase;
        public double BossStaggerFrom, BossStaggerUntil;
        /// <summary>The hairline bar's: the level, the experience toward the next and what it needs; the indicator's dot: the levels held; the cards up.</summary>
        public int Level;
        public double Experience, ExperienceNeeded;
        public int HeldLevels;
        public Offer Offer;
        /// <summary>A Boss Rush run (<see cref="Sim.BossRush"/>): its stage, and the boss and act now (0-based; -1 outside one).</summary>
        public bool Rush;
        public BossRushStage RushStage;
        public int RushBoss, RushAct;

        public bool Held => Phase != RunPhase.Playing || Holds != 0;

        /// <summary>Where the hero is at song time <paramref name="t"/> in the snapshot's tick ahead.</summary>
        public void HeroAt(double t, out double x, out double y)
        {
            double u = t >= HeroMoveEnd || HeroMoveEnd <= HeroMoveStart ? 1 : System.Math.Max(0, (t - HeroMoveStart) / (HeroMoveEnd - HeroMoveStart));
            x = HeroFromX + (HeroX - HeroFromX) * u;
            y = HeroFromY + (HeroY - HeroFromY) * u;
        }
    }
}
