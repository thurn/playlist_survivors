using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>The level a session played: what <see cref="SessionRecord.Config"/> rebuilds.</summary>
    public enum SessionLevel : byte
    {
        /// <summary>The hero alone, with no horde or sword (no level bake, or a run on --events).</summary>
        Open = 0,
        /// <summary><see cref="SliceLevel"/>'s horde and sword.</summary>
        Slice = 1,
    }

    /// <summary>
    /// The client's half of a session (unity-game.md, "Sessions and Replay"), beside the plugin's
    /// <c>audio.wav</c>, <c>entries.jsonl</c> and <c>meta.json</c> in the session's folder:
    /// <list type="bullet">
    /// <item><c>inputs.bin</c>: everything a replay needs, exactly (little-endian, doubles as their bits,
    /// so no runtime's number formatting can move a replay): the config's inputs, each entry's
    /// <see cref="EntryRecord"/> in arrival order (entry i is <c>entries.jsonl</c>'s line i), every key
    /// change, debug command and run control in the order the sim took them, the frontier the run settled to and
    /// its hash;</item>
    /// <item><c>run.json</c>: the same run for people: the build, runtime, platform, source and flags,
    /// the seed, the level, the end, the hash and the counts.</item>
    /// </list>
    /// </summary>
    public sealed class SessionRecord
    {
        public const string InputsFile = "inputs.bin", RunFile = "run.json", EntriesFile = "entries.jsonl";
        const int Version = 1;
        static readonly byte[] Magic = { (byte)'P', (byte)'S', (byte)'R', (byte)'U', (byte)'N' };
        const byte Press = 0, Release = 1, Command = 2, Control = 3;

        // The config's inputs.
        public ulong Seed;
        public double StartTime, Gate, KnownAtBudget;
        public bool Easy, HeroStands;
        public RunStart Start;
        public SessionLevel Level;
        public bool Arena;
        /// <summary>The slice's camera footprint (<see cref="HordeConfig.CameraFootprint"/>); empty for an open level.</summary>
        public double[] Footprint = new double[0];

        // The inputs, and where they led.
        public EntryRecord[] Entries = new EntryRecord[0];
        public KeyRecord[] Keys = new KeyRecord[0];
        public DebugRecord[] Commands = new DebugRecord[0];
        public ControlRecord[] Controls = new ControlRecord[0];
        /// <summary>The frontier the run settled to, its event count and the hash of its settled event log.</summary>
        public double End;
        public int Events;
        public ulong Hash;

        // For people (run.json alone).
        public string Build = "", Runtime = "", Platform = "", Source = "";
        public string[] Flags = new string[0];

        /// <summary>The runtime this code runs in, as <see cref="Runtime"/> names it: hashes are compared only within one.</summary>
        public static string CurrentRuntime =>
#if ENABLE_IL2CPP
            "IL2CPP";
#elif UNITY_5_3_OR_NEWER
            "Mono";
#else
            ".NET " + Environment.Version;
#endif

        /// <summary>
        /// The session of <paramref name="sim"/>, run on <paramref name="config"/>: the slice's level or an
        /// open one, which is what <see cref="Config"/> can rebuild.
        /// </summary>
        public static SessionRecord Of(SimConfig config, Simulation sim)
        {
            if (config.Horde != null && (config.Arsenal == null || !Same(config.Horde.Roster, SliceLevel.Roster)))
                throw new NotSupportedException("a session records the slice's level or an open one");
            return new SessionRecord
            {
                Seed = config.Seed,
                StartTime = config.StartTime,
                Gate = config.Gate,
                KnownAtBudget = config.KnownAtBudget,
                Easy = config.Easy,
                HeroStands = config.HeroStands,
                Start = config.Start,
                Level = config.Horde != null ? SessionLevel.Slice : SessionLevel.Open,
                Arena = config.Arena != null,
                Footprint = config.Horde?.CameraFootprint != null ? (double[])config.Horde.CameraFootprint.Clone() : new double[0],
                Entries = sim.Stream.Accepted.ToArray(),
                Keys = sim.Keys.ToArray(),
                Commands = sim.Commands.ToArray(),
                Controls = sim.Controls.ToArray(),
                End = sim.Now,
                Events = sim.EventCount,
                Hash = sim.Hash,
                Runtime = CurrentRuntime,
            };
        }

        static bool Same(string[] a, string[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The run's config again, on the level's <paramref name="content"/> and <paramref name="arena"/>.</summary>
        public SimConfig Config(GameContent content, ArenaData arena)
        {
            if (Arena && arena == null) throw new ArgumentException("the session played an arena", nameof(arena));
            var config = Level == SessionLevel.Slice
                ? SliceLevel.Config(content ?? throw new ArgumentNullException(nameof(content)), Arena ? arena : null, Footprint, Seed)
                : new SimConfig { Seed = Seed, Arena = Arena ? arena : null };
            config.StartTime = StartTime;
            config.Gate = Gate;
            config.KnownAtBudget = KnownAtBudget;
            config.Easy = Easy;
            config.HeroStands = HeroStands;
            config.Start = Start;
            return config;
        }

        /// <summary>Writes <c>inputs.bin</c> and <c>run.json</c> into the session's folder <paramref name="dir"/>.</summary>
        public void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, InputsFile))))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(Seed);
                w.Write(StartTime);
                w.Write(Gate);
                w.Write(KnownAtBudget);
                w.Write(Easy);
                w.Write(HeroStands);
                w.Write((byte)Start);
                w.Write((byte)Level);
                w.Write(Arena);
                w.Write(Footprint.Length);
                foreach (double x in Footprint) w.Write(x);
                w.Write(End);
                w.Write(Events);
                w.Write(Hash);
                w.Write(Entries.Length);
                foreach (var e in Entries)
                {
                    w.Write(e.Taken);
                    w.Write(e.KnownAt);
                }
                var inputs = Inputs();
                w.Write(inputs.Count);
                foreach (var input in inputs)
                {
                    w.Write(input.Type);
                    w.Write(input.Code);
                    w.Write(input.Heard);
                    w.Write(input.Effective);
                }
            }
            File.WriteAllText(Path.Combine(dir, RunFile), RunJson());
        }

        /// <summary>A key change, debug command or run control, as <c>inputs.bin</c> holds them.</summary>
        struct Input
        {
            public byte Type, Code;
            public double Heard, Effective;
            public int Order;
        }

        /// <summary>The key changes, debug commands and run controls, in the order the sim took them.</summary>
        List<Input> Inputs()
        {
            var inputs = new List<Input>(Keys.Length + Commands.Length + Controls.Length);
            foreach (var k in Keys)
                inputs.Add(new Input { Type = k.Down ? Press : Release, Code = (byte)k.Key, Heard = k.Heard, Effective = k.Effective, Order = k.Order });
            foreach (var c in Commands)
                inputs.Add(new Input { Type = Command, Code = (byte)c.Command, Effective = c.Effective, Order = c.Order });
            foreach (var c in Controls)
                inputs.Add(new Input { Type = Control, Code = (byte)c.Control, Effective = c.Effective, Order = c.Order });
            inputs.Sort((a, b) => a.Order.CompareTo(b.Order));
            return inputs;
        }

        /// <summary>Reads a session's <c>inputs.bin</c> and, when it is there, its <c>run.json</c>'s descriptions.</summary>
        public static SessionRecord Read(string dir)
        {
            var path = Path.Combine(dir, InputsFile);
            var r = new SessionRecord();
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                var magic = reader.ReadBytes(Magic.Length);
                for (int i = 0; i < Magic.Length; i++)
                    if (magic.Length != Magic.Length || magic[i] != Magic[i]) throw new FormatException(path + ": not a session's inputs");
                int version = reader.ReadInt32();
                if (version != Version) throw new FormatException($"{path}: version {version}, not {Version}");
                r.Seed = reader.ReadUInt64();
                r.StartTime = reader.ReadDouble();
                r.Gate = reader.ReadDouble();
                r.KnownAtBudget = reader.ReadDouble();
                r.Easy = reader.ReadBoolean();
                r.HeroStands = reader.ReadBoolean();
                r.Start = (RunStart)reader.ReadByte();
                r.Level = (SessionLevel)reader.ReadByte();
                r.Arena = reader.ReadBoolean();
                r.Footprint = new double[reader.ReadInt32()];
                for (int i = 0; i < r.Footprint.Length; i++) r.Footprint[i] = reader.ReadDouble();
                r.End = reader.ReadDouble();
                r.Events = reader.ReadInt32();
                r.Hash = reader.ReadUInt64();
                r.Entries = new EntryRecord[reader.ReadInt32()];
                for (int i = 0; i < r.Entries.Length; i++)
                    r.Entries[i] = new EntryRecord { Taken = reader.ReadDouble(), KnownAt = reader.ReadDouble() };
                int n = reader.ReadInt32();
                var keys = new List<KeyRecord>();
                var commands = new List<DebugRecord>();
                var controls = new List<ControlRecord>();
                for (int i = 0; i < n; i++)
                {
                    byte type = reader.ReadByte(), code = reader.ReadByte();
                    double heard = reader.ReadDouble(), effective = reader.ReadDouble();
                    if (type == Command) commands.Add(new DebugRecord { Command = (DebugCommand)code, Effective = effective, Order = i });
                    else if (type == Control) controls.Add(new ControlRecord { Control = (RunControl)code, Effective = effective, Order = i });
                    else if (type == Press || type == Release)
                        keys.Add(new KeyRecord { Key = (Key)code, Down = type == Press, Heard = heard, Effective = effective, Order = i });
                    else throw new FormatException($"{path}: input {i} of unknown type {type}");
                }
                r.Keys = keys.ToArray();
                r.Commands = commands.ToArray();
                r.Controls = controls.ToArray();
            }
            var run = Path.Combine(dir, RunFile);
            if (File.Exists(run)) r.ReadDescriptions(JsonValue.Parse(File.ReadAllText(run)));
            return r;
        }

        void ReadDescriptions(JsonValue run)
        {
            foreach (var m in run.Members)
            {
                switch (m.Key)
                {
                    case "build": Build = m.Value.String ?? ""; break;
                    case "runtime": Runtime = m.Value.String ?? ""; break;
                    case "platform": Platform = m.Value.String ?? ""; break;
                    case "source": Source = m.Value.String ?? ""; break;
                    case "flags":
                        var flags = new List<string>();
                        foreach (var f in m.Value.Items) flags.Add(f.String);
                        Flags = flags.ToArray();
                        break;
                }
            }
        }

        string RunJson()
        {
            var c = CultureInfo.InvariantCulture;
            var flags = new StringBuilder();
            for (int i = 0; i < Flags.Length; i++) flags.Append(i > 0 ? ", " : "").Append(Quote(Flags[i]));
            return string.Format(c,
                "{{\n  \"format\": \"ps-game-session\",\n  \"version\": {0},\n  \"build\": {1},\n  \"runtime\": {2},\n" +
                "  \"platform\": {3},\n  \"source\": {4},\n  \"flags\": [{5}],\n  \"seed\": {6},\n  \"level\": {7},\n" +
                "  \"arena\": {8},\n  \"easy\": {9},\n  \"heroStands\": {10},\n  \"startTime\": {11:R},\n  \"end\": {12:R},\n" +
                "  \"hash\": \"{13:X16}\",\n  \"events\": {14},\n  \"entries\": {15},\n  \"keys\": {16},\n  \"commands\": {17},\n" +
                "  \"start\": {18},\n  \"controls\": {19}\n}}\n",
                Version, Quote(Build), Quote(Runtime), Quote(Platform), Quote(Source), flags, Seed,
                Quote(Level == SessionLevel.Slice ? "slice" : "open"), Arena ? "true" : "false", Easy ? "true" : "false",
                HeroStands ? "true" : "false", StartTime, End, Hash, Events, Entries.Length, Keys.Length, Commands.Length,
                Quote(Start.ToString()), Controls.Length);
        }

        static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                else if (ch < ' ') b.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else b.Append(ch);
            }
            return b.Append('"').ToString();
        }

        /// <summary>
        /// The entries of a session's <c>entries.jsonl</c> (the plugin's record, in <c>ps entries</c>'
        /// line format), in order. The player gets them from the plugin's Replay source instead; this
        /// is for .NET, whose number parsing gives every double back exactly.
        /// </summary>
        public static List<StreamEntry> LoadEntries(string dir)
        {
            var path = Path.Combine(dir, EntriesFile);
            var entries = new List<StreamEntry>();
            int line = 0;
            foreach (string text in File.ReadLines(path))
            {
                line++;
                if (text.Length == 0) continue;
                var e = new StreamEntry();
                bool kind = false, t = false, stamp = false;
                foreach (var m in JsonValue.Parse(text).Members)
                {
                    switch (m.Key)
                    {
                        case "kind": e.Kind = (EntryKind)(int)m.Value.Number; kind = true; break;
                        case "t": e.T = m.Value.Number; t = true; break;
                        case "known_at": e.Stamp = m.Value.Number; stamp = true; break;
                        case "v":
                            if (m.Value.Items.Count > StreamEntry.Slots) throw new FormatException($"{path}:{line}: {m.Value.Items.Count} slots");
                            for (int i = 0; i < m.Value.Items.Count; i++) e[i] = m.Value.Items[i].Number;
                            break;
                    }
                }
                if (!kind || !t || !stamp) throw new FormatException($"{path}:{line}: an entry needs its kind, t and known_at");
                entries.Add(e);
            }
            return entries;
        }
    }
}
