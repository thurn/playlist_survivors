namespace PS.Game.Sim
{
    /// <summary>
    /// The debug surfaces' commands (unity-game.md, Debug Surfaces) that the slice and the music-driven
    /// events need: M3's F1 menu adds the rest. Each takes effect at the sim's time when it is given, like a key.
    /// </summary>
    public enum DebugCommand
    {
        /// <summary>A section change now: it lands on the next downbeat as a detected one (a zone change, a slam, regrowth after).</summary>
        SectionChange = 0,
        /// <summary>The next main strike to wind up is heavy, as on an accent, whatever the music.</summary>
        Accent = 1,
        /// <summary>Every standing structure within <see cref="Simulation.DebugBreakReachM"/> of the hero breaks now, large ones too.</summary>
        BreakStructures = 2,
        /// <summary>The broken structures regrow on the next downbeat, as after a zone change (the 3 m rule holds).</summary>
        RegrowStructures = 3,
        /// <summary>The music-driven events now, whatever the music, the run clock and the gaps between them
        /// (<see cref="MusicEvents"/>): each starts on the next main beat, a set piece in place of any in play.</summary>
        Stampede = 4,
        KickLine = 5,
        Wedge = 6,
        Ring = 7,
        /// <summary>A bat swarm, a ghost swarm or a flower wall as the schedule's first of its kind.</summary>
        BatSwarm = 8,
        GhostSwarm = 9,
        FlowerWall = 10,
        /// <summary>The signature minotaur lands on the next reliable main beat, unless it is on the field.</summary>
        Minotaur = 11,
        /// <summary>The finale now, whatever the run clock: its blast lands on the next downbeat and the victory card follows (<see cref="RunRules.ForceFinale"/>).</summary>
        Finale = 12,
        /// <summary>A tier-3 chest of the last evolving minute's treasure two tiles ahead of the hero, for it to walk onto (<see cref="Chests"/>).</summary>
        Chest = 13,
    }

    /// <summary>A debug command as the sim recorded it: a replay gives it at <see cref="Effective"/>.</summary>
    public struct DebugRecord
    {
        public DebugCommand Command;
        public double Effective;
        /// <summary>Its place among the run's key changes, debug commands and run controls, which a replay keeps.</summary>
        public int Order;
    }
}
