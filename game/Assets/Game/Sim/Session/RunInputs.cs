using System;
using System.IO;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>The level a run plays: what <see cref="RunInputs.Build"/> builds.</summary>
    public enum SessionLevel : byte
    {
        /// <summary>The hero alone, with no horde or sword (no level bake, or a run on --events).</summary>
        Open = 0,
        /// <summary><see cref="SliceLevel"/>'s horde and sword.</summary>
        Slice = 1,
        /// <summary><see cref="RunLevel"/>'s schedule, events and arsenal.</summary>
        Run = 2,
        /// <summary><see cref="RunLevel.Lineup"/>'s cast in a row (`--cast`): never recorded.</summary>
        Cast = 3,
    }

    /// <summary>
    /// A run's inputs besides the stream and the keys, built once from the launch's flags (the client's
    /// LaunchOptions) and the build's level, or read back from a session: the one value that makes a run's
    /// <see cref="SimConfig"/> (<see cref="Build"/>), and the one a session writes and reads, so a replay builds
    /// exactly what the run did. Every other config field is the level's, the content's or fixed.
    ///
    /// A run on inputs a replay can't rebuild (a player's `--arsenal`, `--no-cards`, `--cast` or `--level`) says so
    /// in <see cref="Unrecordable"/>, known before it starts; a session refuses it.
    /// </summary>
    public sealed class RunInputs
    {
        static readonly SimConfig Defaults = new SimConfig();

        public ulong Seed = Defaults.Seed;
        public SessionLevel Level;
        /// <summary>Whether the sim plays the level's arena (<see cref="SimConfig.Arena"/>); an open level never does.</summary>
        public bool Arena;
        /// <summary>The level's camera footprint (<see cref="HordeConfig.CameraFootprint"/>); empty for an open level.</summary>
        public double[] Footprint = new double[0];
        public RunStart Start = Defaults.Start;
        public double StartTime = Defaults.StartTime;
        /// <summary>The run clock's minute it starts at (`--start-minute`): the run's level only.</summary>
        public int StartMinute = Defaults.StartMinute;
        public double Gate = Defaults.Gate, KnownAtBudget = Defaults.KnownAtBudget;
        public bool Easy = Defaults.Easy;
        /// <summary>`--mode`: the way of playing (<see cref="GameModes"/>).</summary>
        public GameMode Mode = Defaults.Mode;

        // Never recorded: a run on any of these sets Unrecordable.
        /// <summary>`--cast`'s roles, for the <see cref="SessionLevel.Cast"/> level.</summary>
        public string[] Cast;
        /// <summary>`--arsenal`'s weapons and passives with their ranks, in slot order, in place of the level's; null keeps its own.</summary>
        public (string Id, int Rank)[] Loadout;
        /// <summary>`--no-cards`: levels count without opening their cards.</summary>
        public bool NoCards;
        /// <summary>`--level`: the level's files in place of the build's, which a replay doesn't have.</summary>
        public bool LevelFiles;

        /// <summary>Why a session can't record this run (the flag that made it so), or null if it can.</summary>
        public string Unrecordable =>
            Level == SessionLevel.Cast ? "--cast"
            : LevelFiles ? "--level"
            : Loadout != null && Armed ? "--arsenal"
            : NoCards && Armed ? "--no-cards"
            : null;

        /// <summary>Whether the level has an arsenal for a loadout or `--no-cards` to change.</summary>
        bool Armed => Level == SessionLevel.Slice || Level == SessionLevel.Run;

        /// <summary>
        /// The run's config, on the level's <paramref name="content"/> and <paramref name="arena"/>: the only place a
        /// run's <see cref="SimConfig"/> is made.
        /// </summary>
        public SimConfig Build(GameContent content, ArenaData arena)
        {
            if (Arena && arena == null) throw new ArgumentException("the run plays an arena", nameof(arena));
            if (Level != SessionLevel.Open && content == null) throw new ArgumentNullException(nameof(content));
            var a = Arena ? arena : null;
            SimConfig config;
            switch (Level)
            {
                case SessionLevel.Slice: config = SliceLevel.Config(content, a, Footprint, Seed); break;
                case SessionLevel.Run: config = RunLevel.Config(content, a, Footprint, Seed); break;
                case SessionLevel.Cast: config = RunLevel.Lineup(content, a, Footprint, Cast, Seed); break;
                default: config = new SimConfig { Seed = Seed, Arena = a }; break;
            }
            config.Start = Start;
            config.StartTime = StartTime;
            config.StartMinute = StartMinute;
            config.Gate = Gate;
            config.KnownAtBudget = KnownAtBudget;
            config.Easy = Easy;
            config.Mode = Mode;
            if (config.Arsenal != null)
            {
                if (Loadout != null)
                {
                    // Passives (an evolution's partner) go with the weapons: "magic-wand:8,empty-tome:1".
                    config.Arsenal.Weapons = Array.FindAll(Loadout, i => content.PassiveIndex(i.Id) < 0);
                    config.Arsenal.Passives = Array.FindAll(Loadout, i => content.PassiveIndex(i.Id) >= 0);
                }
                if (NoCards) config.Arsenal.Cards = false;
            }
            return config;
        }

        /// <summary>
        /// These inputs on a build: with no level bake (<paramref name="baked"/> false) the run is an open one, and
        /// otherwise the sim plays the arena if <paramref name="arena"/> and spawns outside <paramref name="footprint"/>,
        /// the camera's widest view. An open level has neither.
        /// </summary>
        public RunInputs On(bool baked, bool arena, double[] footprint)
        {
            var run = (RunInputs)MemberwiseClone();
            if (!baked) run.Level = SessionLevel.Open;
            bool open = run.Level == SessionLevel.Open;
            run.Arena = !open && arena;
            run.Footprint = open ? new double[0] : (double[])footprint.Clone();
            if (open) run.StartMinute = 0;
            return run;
        }

        /// <summary>
        /// Writes the recorded inputs as <c>inputs.bin</c> holds them (version 3's layout, which keeps a byte for whether
        /// the hero stands: now the level's alone, <see cref="SimConfig.HeroStands"/>).
        /// </summary>
        public void Write(BinaryWriter w)
        {
            if (Unrecordable != null) throw new InvalidOperationException("a session can't record a run on " + Unrecordable);
            w.Write(Seed);
            w.Write(StartTime);
            w.Write(StartMinute);
            w.Write(Gate);
            w.Write(KnownAtBudget);
            w.Write(Easy);
            w.Write(Level == SessionLevel.Slice); // the hero standing, which only the slice's does of the levels recorded
            w.Write((byte)Start);
            w.Write((byte)Level);
            w.Write(Arena);
            w.Write(Footprint.Length);
            foreach (double x in Footprint) w.Write(x);
            w.Write((byte)Mode);
        }

        /// <summary>Reads the recorded inputs of an <c>inputs.bin</c> of <paramref name="version"/> (1 has no start minute, 1 and 2 no mode).</summary>
        public static RunInputs Read(BinaryReader r, int version, string path)
        {
            var run = new RunInputs { Seed = r.ReadUInt64(), StartTime = r.ReadDouble() };
            if (version >= 2) run.StartMinute = r.ReadInt32();
            run.Gate = r.ReadDouble();
            run.KnownAtBudget = r.ReadDouble();
            run.Easy = r.ReadBoolean();
            r.ReadBoolean(); // the hero standing: the level's, as it always was
            byte start = r.ReadByte(), level = r.ReadByte();
            if (start > (byte)RunStart.AfterClearBeat) throw new FormatException($"{path}: run start {start}");
            if (level > (byte)SessionLevel.Run) throw new FormatException($"{path}: level {level}");
            run.Start = (RunStart)start;
            run.Level = (SessionLevel)level;
            run.Arena = r.ReadBoolean();
            run.Footprint = new double[r.ReadInt32()];
            for (int i = 0; i < run.Footprint.Length; i++) run.Footprint[i] = r.ReadDouble();
            if (version >= 3)
            {
                byte mode = r.ReadByte();
                if (!GameModes.Known(mode)) throw new FormatException($"{path}: mode {mode}");
                run.Mode = (GameMode)mode;
            }
            return run;
        }
    }
}
