using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// The latest settled snapshot the client reads (unity-game.md, "The Sim's Contract"): time,
    /// the run clock, the beat, the hero, the enemies, the structures and the zone; M3 adds items
    /// as dense arrays here. The client copies what it needs into its own
    /// short history to show <c>frameSong</c>, which trails the frontier.
    ///
    /// Only the sim writes it: every property is <c>{ get; internal set; }</c>, and dense arrays
    /// are exposed as <c>ReadOnlySpan</c> getters over the sim's storage, never as arrays.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>The frontier: the song time the sim has simulated to.</summary>
        public double Now { get; internal set; }
        public long Tick { get; internal set; }

        /// <summary>Playing time: advances with song time except while the world is frozen.</summary>
        public double RunClock { get; internal set; }
        public bool Frozen { get; internal set; }

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

        /// <summary>The damage each of the hero's weapons has dealt to enemies this run, by content's weapon index (empty without a horde).</summary>
        public ReadOnlySpan<double> WeaponDamage => WeaponDamageStore;
        internal double[] WeaponDamageStore = Array.Empty<double>();

        /// <summary>Every crystal structure, standing or broken; the index is the level data's and the structure's id in events.</summary>
        public ReadOnlySpan<StructureState> Structures => StructureStore;
        internal StructureState[] StructureStore = Array.Empty<StructureState>();

        /// <summary>The arena's zone: an index into schedule.json's zones, from 0 at the start.</summary>
        public int Zone { get; internal set; }
    }
}
