using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Champions.Sim
{
    /// <summary>
    /// A session (plan.md, "Sessions and replay"): the run's inputs, the stream's records and
    /// <c>run.json</c>, in one folder beside the plugin's <c>entries.jsonl</c> (and its audio):
    /// <list type="bullet">
    /// <item><c>inputs.bin</c>: everything a replay needs, exactly (little-endian, doubles as their bits, so
    /// no runtime's number formatting can move a replay): the <see cref="RunInputs"/>, the frontier the run
    /// settled to, its event count and state hash, each entry's <see cref="EntryRecord"/> in arrival order
    /// (entry i is <c>entries.jsonl</c>'s line i), and every input in the order the sim took it;</item>
    /// <item><c>run.json</c>: the same run for people: the build, runtime, platform, source and flags, the
    /// seed, the end, the hash and the counts.</item>
    /// </list>
    /// </summary>
    public sealed class SessionRecord
    {
        public const string InputsFile = "inputs.bin", RunFile = "run.json", EntriesFile = "entries.jsonl";
        const int Version = 4;
        static readonly byte[] Magic = { (byte)'R', (byte)'C', (byte)'R', (byte)'U', (byte)'N' };

        /// <summary>What built the run's config (<see cref="RunInputs.Build"/>).</summary>
        public RunInputs Run = new RunInputs();

        // The inputs, and where they led.
        public EntryRecord[] Entries = new EntryRecord[0];
        public InputRecord[] Inputs = new InputRecord[0];
        /// <summary>The frontier the run settled to, its event count and its state hash (<see cref="Simulation.Hash"/>).</summary>
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

        /// <summary>The session of <paramref name="sim"/>, run on the config <paramref name="run"/> built.</summary>
        public static SessionRecord Of(RunInputs run, Simulation sim)
        {
            return new SessionRecord
            {
                Run = run,
                Entries = sim.Stream.Accepted.ToArray(),
                Inputs = sim.Inputs.ToArray(),
                End = sim.Now,
                Events = sim.EventCount,
                Hash = sim.Hash,
                Runtime = CurrentRuntime,
            };
        }

        /// <summary>Writes <c>inputs.bin</c> and <c>run.json</c> into the session's folder <paramref name="dir"/>.</summary>
        public void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, InputsFile))))
            {
                w.Write(Magic);
                w.Write(Version);
                Run.Write(w);
                w.Write(End);
                w.Write(Events);
                w.Write(Hash);
                w.Write(Entries.Length);
                foreach (var e in Entries)
                {
                    w.Write(e.Taken);
                    w.Write(e.KnownAt);
                }
                w.Write(Inputs.Length);
                foreach (var input in Inputs)
                {
                    w.Write((byte)input.Kind);
                    w.Write(input.Code);
                    w.Write(input.Heard);
                    w.Write(input.Effective);
                    if (input.Kind != InputKind.Target) continue;
                    w.Write(input.X);
                    w.Write(input.Y);
                }
            }
            File.WriteAllText(Path.Combine(dir, RunFile), RunJson());
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
                if (version < 1 || version > Version) throw new FormatException($"{path}: version {version}, not {Version}");
                r.Run = RunInputs.Read(reader, version, path);
                r.End = reader.ReadDouble();
                r.Events = reader.ReadInt32();
                r.Hash = reader.ReadUInt64();
                r.Entries = new EntryRecord[reader.ReadInt32()];
                for (int i = 0; i < r.Entries.Length; i++)
                    r.Entries[i] = new EntryRecord { Taken = reader.ReadDouble(), KnownAt = reader.ReadDouble() };
                r.Inputs = new InputRecord[reader.ReadInt32()];
                for (int i = 0; i < r.Inputs.Length; i++)
                {
                    var input = new InputRecord { Kind = (InputKind)reader.ReadByte(), Code = reader.ReadByte() };
                    if (input.Kind > InputKind.Control) throw new FormatException($"{path}: input {i} of unknown kind {input.Kind}");
                    input.Heard = reader.ReadDouble();
                    input.Effective = reader.ReadDouble();
                    if (input.Kind == InputKind.Target)
                    {
                        input.X = reader.ReadDouble();
                        input.Y = reader.ReadDouble();
                    }
                    r.Inputs[i] = input;
                }
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
            var loadout = new StringBuilder();
            for (int i = 0; i < Run.Loadout.Length; i++) loadout.Append(i > 0 ? ", " : "").Append(Quote(Run.Loadout[i].ToString()));
            return string.Format(c,
                "{{\n  \"format\": \"rhythm-champions-session\",\n  \"version\": {0},\n  \"build\": {1},\n  \"runtime\": {2},\n" +
                "  \"platform\": {3},\n  \"source\": {4},\n  \"flags\": [{5}],\n  \"seed\": {6},\n  \"startTime\": {7:R},\n" +
                "  \"start\": {8},\n  \"loadout\": [{14}],\n  \"dummies\": {15},\n  \"level\": {16},\n  \"duel\": {17},\n  \"end\": {9:R},\n  \"hash\": \"{10:X16}\",\n" +
                "  \"events\": {11},\n  \"entries\": {12},\n  \"inputs\": {13}\n}}\n",
                Version, Quote(Build), Quote(Runtime), Quote(Platform), Quote(Source), flags, Run.Seed, Run.StartTime,
                Quote(Run.Start.ToString()), End, Hash, Events, Entries.Length, Inputs.Length, loadout, Run.Dummies.Length, Run.Level ? "true" : "false",
                Run.Duel != null ? Quote(Run.Duel) : "null");
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
        /// The entries of a session's <c>entries.jsonl</c> (the plugin's record: one JSON object per line with
        /// its <c>kind</c>, <c>t</c>, <c>known_at</c> stamp and the kind's <c>v</c> slots), in order. A header
        /// line (one with a <c>format</c> and no <c>kind</c>) is skipped. The player gets them from the plugin's
        /// replay source instead; this is for .NET, whose number parsing gives every double back exactly.
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
                bool kind = false, t = false, stamp = false, header = false;
                foreach (var m in JsonValue.Parse(text).Members)
                {
                    switch (m.Key)
                    {
                        case "format": header = true; break;
                        case "kind": e.Kind = (EntryKind)(int)m.Value.Number; kind = true; break;
                        case "t": e.T = m.Value.Number; t = true; break;
                        case "known_at": e.Stamp = m.Value.Number; stamp = true; break;
                        case "v":
                            if (m.Value.Items.Count > StreamEntry.Slots) throw new FormatException($"{path}:{line}: {m.Value.Items.Count} slots");
                            for (int i = 0; i < m.Value.Items.Count; i++) e[i] = m.Value.Items[i].Number;
                            break;
                    }
                }
                if (header && !kind) continue;
                if (!kind || !t || !stamp) throw new FormatException($"{path}:{line}: an entry needs its kind, t and known_at");
                entries.Add(e);
            }
            return entries;
        }

        /// <summary>
        /// Writes <paramref name="entries"/> as the plugin records them (<see cref="LoadEntries"/>'s format, each
        /// kind's slots), for sessions made without the plugin: scripted runs and the tests' recorded sessions.
        /// </summary>
        public static void WriteEntries(string dir, IEnumerable<StreamEntry> entries)
        {
            Directory.CreateDirectory(dir);
            var c = CultureInfo.InvariantCulture;
            var b = new StringBuilder();
            foreach (var e in entries)
            {
                b.Append("{\"kind\":").Append(((int)e.Kind).ToString(c))
                    .Append(",\"t\":").Append(e.T.ToString("R", c))
                    .Append(",\"known_at\":").Append(e.Stamp.ToString("R", c))
                    .Append(",\"v\":[");
                for (int i = 0, n = StreamEntry.SlotsOf(e.Kind); i < n; i++) b.Append(i > 0 ? "," : "").Append(e[i].ToString("R", c));
                b.Append("]}\n");
            }
            File.WriteAllText(Path.Combine(dir, EntriesFile), b.ToString());
        }
    }
}
