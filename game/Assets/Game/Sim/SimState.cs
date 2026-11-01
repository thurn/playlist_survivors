using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The latest settled snapshot the client reads (unity-game.md, "The Sim's Contract"): time,
    /// the run clock, the beat, the hero, the enemies, the structures, the zone, and the arsenal
    /// with its experience and level-up offer. The client copies what it needs into its own
    /// short history to show <c>frameSong</c>, which trails the frontier. The chests on the field, the
    /// latest chest's roulette and the run's recap come last.
    ///
    /// Only the sim writes it: every property is <c>{ get; internal set; }</c>, and dense arrays
    /// are exposed as <c>ReadOnlySpan</c> getters over the sim's storage, never as arrays.
    /// </summary>
    public sealed class SimState
    {
        public SimState() => Recap = new RunRecap(this);

        /// <summary>The frontier: the song time the sim has simulated to.</summary>
        public double Now { get; internal set; }
        public long Tick { get; internal set; }

        /// <summary>Where the run is (<see cref="RunRules"/>): waiting for its start, playing, in its finale, or ended.</summary>
        public RunPhase Phase { get; internal set; }
        /// <summary>The song time the run started at, or NaN before.</summary>
        public double StartedAt { get; internal set; } = double.NaN;
        /// <summary>Why the run ended, and when (NaN before).</summary>
        public RunEnd End { get; internal set; }
        public double EndedAt { get; internal set; } = double.NaN;

        /// <summary>Playing time: advances with song time while the run plays, except while the world is frozen.</summary>
        public double RunClock { get; internal set; }
        /// <summary>The world is held: before the start, during a freeze, and after the finale's blast.</summary>
        public bool Frozen { get; internal set; }
        /// <summary>The freezes holding the world now, a bit per <see cref="FreezeReason"/> (the banners'); a resume waits for the next beat.</summary>
        public int FreezeReasons { get; internal set; }

        /// <summary>Does <paramref name="reason"/> hold the world now?</summary>
        public bool FrozenFor(FreezeReason reason) => (FreezeReasons & (1 << (int)reason)) != 0;

        public Reliability Beat { get; internal set; }
        /// <summary>The latest committed beat fired, or -1.</summary>
        public int LastBeat { get; internal set; } = -1;
        /// <summary>The latest beat's bar (-1 before any lock), and whether it was a downbeat.</summary>
        public int Bar { get; internal set; } = -1;
        public bool Downbeat { get; internal set; }

        /// <summary>The latest stride step's time, its planned period and halvings (-1 on the free pulse).</summary>
        public double StrideStep { get; internal set; } = double.NegativeInfinity;
        public double StridePeriod { get; internal set; } = StrideGrid.FreePeriod;
        public int StrideHalvings { get; internal set; } = -1;

        public HeroState Hero { get; } = new HeroState();

        /// <summary>Every enemy slot, alive or free (<see cref="EnemyState.Alive"/>); a slot's index is the enemy's id in events.</summary>
        public ReadOnlySpan<EnemyState> Enemies => EnemyStore;
        public int EnemiesAlive { get; internal set; }
        internal EnemyState[] EnemyStore = Array.Empty<EnemyState>();


        /// <summary>Every crystal structure, standing or broken; the index is the level data's and the structure's id in events.</summary>
        public ReadOnlySpan<StructureState> Structures => StructureStore;
        internal StructureState[] StructureStore = Array.Empty<StructureState>();

        /// <summary>The arena's zone: an index into schedule.json's zones, from 0 at the start.</summary>
        public int Zone { get; internal set; }

        /// <summary>The arsenal's weapons and passives in slot order (empty without an arsenal: <see cref="Arsenal"/>).</summary>
        public ReadOnlySpan<OwnedItem> WeaponSlots => new ReadOnlySpan<OwnedItem>(WeaponStore, 0, WeaponCount);
        public ReadOnlySpan<OwnedItem> PassiveSlots => new ReadOnlySpan<OwnedItem>(PassiveStore, 0, PassiveCount);
        internal OwnedItem[] WeaponStore = Array.Empty<OwnedItem>(), PassiveStore = Array.Empty<OwnedItem>();
        internal int WeaponCount, PassiveCount;

        /// <summary>The hero's final stats: progression.json's base, the passives, the caps.</summary>
        public Stats Stats { get; internal set; }
        /// <summary>How near a gem must lie to take off for the hero: the base radius times Magnet.</summary>
        public double PickupRadius { get; internal set; }

        /// <summary>The hero's level (1 at the start), and levels reached whose cards are still to come.</summary>
        public int Level { get; internal set; }
        public int PendingLevels { get; internal set; }
        /// <summary>Experience gathered, and the totals the level started at and the next one needs.</summary>
        public double Experience { get; internal set; }
        public double ExperienceFloor { get; internal set; }
        public double ExperienceNext { get; internal set; }

        /// <summary>The experience bar: how far from this level's start to the next, 0 to 1.</summary>
        public double ExperienceFill =>
            ExperienceNext > ExperienceFloor ? Math.Max(0, Math.Min(1, (Experience - ExperienceFloor) / (ExperienceNext - ExperienceFloor))) : 0;

        /// <summary>The latest level-up offer: open while its cards are shown (<see cref="LevelUps"/>).</summary>
        public Offer Offer { get; internal set; }

        /// <summary>The bosses' chests lying on the field, in drop order (empty without an arsenal: <see cref="Chests"/>).</summary>
        public ReadOnlySpan<FieldChest> FieldChests => new ReadOnlySpan<FieldChest>(FieldChestStore, 0, FieldChestCount);
        internal FieldChest[] FieldChestStore = Array.Empty<FieldChest>();
        internal int FieldChestCount;

        /// <summary>The latest chest's roulette: open while it spins, then landed with its rewards (<see cref="Chests"/>).</summary>
        public ChestState Chest { get; internal set; }

        /// <summary>The end card's numbers: the kills, and each weapon's damage and time held, final from the run's end.</summary>
        public RunRecap Recap { get; }
    }
}
