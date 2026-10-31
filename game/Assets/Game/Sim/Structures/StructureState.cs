namespace PS.Game.Sim
{
    /// <summary>What hit a structure, the source in its glint and break events.</summary>
    public enum StructureHit
    {
        /// <summary>An ordinary sword strike: it only glints.</summary>
        Strike = 0,
        HeavyStrike = 1,
        Cleave = 2,
        /// <summary>Hooks for M3: a boss's slam, the finale's blast, and an evolved weapon's hit within 50 ms of an accented beat.</summary>
        BossSlam = 3,
        FinaleBlast = 4,
        EvolvedAccent = 5,
    }

    /// <summary>
    /// One crystal structure as the sim settled it (<see cref="SimState.Structures"/>); its index
    /// is the level data's (<see cref="Level.ArenaData.Structures"/>) and the structure's id in
    /// events. A standing structure blocks its tiles; a broken one leaves them open until it
    /// regrows.
    /// </summary>
    public struct StructureState
    {
        public bool Standing { get; internal set; }
        public bool Large { get; internal set; }
        /// <summary>The breaking hits it takes to shatter: one small, two or three large.</summary>
        public int HitsToBreak { get; internal set; }
        /// <summary>The breaking hits taken since it last stood whole.</summary>
        public int Hits { get; internal set; }
        /// <summary>Its tiles' centre, ground-plane metres.</summary>
        public double CentreX { get; internal set; }
        public double CentreY { get; internal set; }
        /// <summary>When it last broke, or -infinity.</summary>
        public double BrokenAt { get; internal set; }
        /// <summary>Its latest regrowth, from a downbeat over one beat (-infinity before any); hits only glint until it ends.</summary>
        public double GrowStart { get; internal set; }
        public double GrowEnd { get; internal set; }
    }
}
