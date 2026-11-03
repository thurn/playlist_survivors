namespace Champions.Sim
{
    /// <summary>
    /// A run's fixed inputs besides the stream and the player's inputs: made by <see cref="RunInputs.Build"/>
    /// from the launch's flags and the content, so a session's replay builds exactly what the run did. The
    /// capacities size the sim's storage up front so a whole level allocates nothing per tick.
    /// </summary>
    public sealed class SimConfig
    {
        /// <summary>Seeds the run's one random generator.</summary>
        public ulong Seed = 1;

        /// <summary>The song time the sim starts at.</summary>
        public double StartTime;

        /// <summary>B: the budget the game adds to the plugin's stamp (more than the sim's lead).</summary>
        public double KnownAtBudget = 0.5;

        /// <summary>How the run starts: at once (tests, scripted runs) or on <see cref="RunControl.Start"/>.</summary>
        public RunStart Start;

        /// <summary>The beat services' numbers (Content/beat.json).</summary>
        public BeatRules Beat = new BeatRules();

        /// <summary>The hero's numbers (Content/hero.json).</summary>
        public HeroRules Hero = new HeroRules();

        /// <summary>The dashes' numbers and forms (Content/dashes.json; the defaults hold only the plain Dash).</summary>
        public DashRules Dashes = new DashRules();

        /// <summary>The attacks' and mods' numbers (Content/attacks.json, Content/mods.json).</summary>
        public AttackRules Attacks = new AttackRules();
        public ModRules Mods = new ModRules();

        public CombatRules Combat = CombatRules.Default;

        /// <summary>The build the run starts with (<see cref="RunInputs.Loadout"/>).</summary>
        public Upgrade[] Loadout = RunInputs.StartLoadout();

        /// <summary>Standing enemies placed at the start (<see cref="RunInputs.Dummies"/>).</summary>
        public Dummy[] Dummies = new Dummy[0];

        /// <summary>The horde's numbers and cast (Content/enemies.json; the defaults have no cast).</summary>
        public EnemyRules Enemies = new EnemyRules();

        /// <summary>Level 1's spawner (Content/level1.json), or null: no level, an empty world (tests, stills).</summary>
        public LevelRules Level;

        /// <summary>The zone keys' rules (Content/zones.json).</summary>
        public ZoneRules Zones = new ZoneRules();

        /// <summary>The world's structures (Content/structures.json), or null: an open arena (tests, most stills).</summary>
        public StructureRules Structures;

        /// <summary>The bosses' numbers and moves (Content/bosses.json; the defaults have no boss).</summary>
        public BossRules Bosses = new BossRules();

        /// <summary>A scripted duel: the cast index of the boss called at the start (<see cref="RunInputs.Duel"/>), or -1.</summary>
        public int Duel = -1;

        /// <summary>The experience curve and held levels' rules (Content/progression.json).</summary>
        public ProgressionRules Progression = new ProgressionRules();

        // The storage's sizes up front: a list past its size doubles, a large copy and garbage mid-run.
        /// <summary>The ticks kept (20 a second), and the locks' per-tick record.</summary>
        public int TickCapacity = 40_000;
        /// <summary>The beats kept, and as many onsets.</summary>
        public int BeatCapacity = 8_192;
        /// <summary>The hindsight windows' grid beats kept.</summary>
        public int HindsightCapacity = 256;
        /// <summary>Each other stream kind's entries kept (hindsight windows, timbre, changes, jumps, forecasts, ends).</summary>
        public int SparseCapacity = 256;
        public int EntryCapacity = 4_096;
        public int EventCapacity = 65_536;
        /// <summary>The enemies' slots: the cap of 1,000 alive, the bosses and room.</summary>
        public int EnemyCapacity = 2_048;
        public int InputCapacity = 4_096;
        /// <summary>The targets one blow can reach (the horde's cap and the structures near it).</summary>
        public int TargetCapacity = 2_048;
    }
}
