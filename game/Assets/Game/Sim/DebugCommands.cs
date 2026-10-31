namespace PS.Game.Sim
{
    /// <summary>
    /// The debug surfaces' commands (unity-game.md, Debug Surfaces): the client's F1 menu, its F5 to F8 keys and
    /// `--debug`. Each takes effect at the sim's time when it is given, like a key; those that name a role, weapon,
    /// passive or time take it as the command's argument (<see cref="Simulation.Command"/>).
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
        /// <summary>The run clock jumps forward to the argument's second, as if the run had played there
        /// (<see cref="RunRules.JumpTo"/>): the minutes passed bring no bosses or swarms, the new one's come due.</summary>
        JumpClock = 14,
        /// <summary>One enemy of the argument's role, in view now (<see cref="Spawner.InViewPoint"/>), if the horde has room.</summary>
        SpawnRole = 15,
        /// <summary>The argument's role as a boss due now, at the current minute, landing as the schedule's bosses do (<see cref="Spawner"/>).</summary>
        SpawnBoss = 16,
        /// <summary>A rank of the argument's base weapon, as a card's (in a free slot, below its top rank and unevolved; else nothing).</summary>
        GrantWeapon = 17,
        /// <summary>A rank of the argument's passive, as a card's (in a free slot and below its top rank; else nothing).</summary>
        GrantPassive = 18,
        /// <summary>The argument's evolution: its base weapon raised to its top rank and its partner passive learned as
        /// needed, then the evolution replaces it (<see cref="Arsenal.Evolve"/>); nothing without the slots for them.</summary>
        GrantEvolution = 19,
        /// <summary>The hero can't be hurt while the argument is 1 (hits land for nothing), and can again at 0.</summary>
        Invulnerable = 20,
    }

    /// <summary>A debug command as the sim recorded it: a replay gives it at <see cref="Effective"/>.</summary>
    public struct DebugRecord
    {
        public DebugCommand Command;
        /// <summary>Its argument: a content index (a role, weapon or passive), a run-clock second, or 1 and 0; 0 when it takes none.</summary>
        public int Arg;
        public double Effective;
        /// <summary>Its place among the run's key changes, debug commands and run controls, which a replay keeps.</summary>
        public int Order;
    }
}
