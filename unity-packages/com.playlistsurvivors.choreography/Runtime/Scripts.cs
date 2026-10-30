using System;
using System.Collections.Generic;
using System.Globalization;

namespace PS.Choreography
{
    /// <summary>
    /// A combo or spell entry: a name, optionally "@" and a grid position, optionally "/k" to fire
    /// on every k-th occurrence of that position (an extension of choreography.md's syntax, used
    /// by the load presets: "lightning@beat/3" is about one a second).
    /// </summary>
    public sealed class GridEntry
    {
        public string Name;
        public bool Positioned;        // false: a plain combo entry that cycles
        public GridFlags Position = GridFlags.Beat;
        public int Every = 1;

        /// <summary>When positions coincide: phrase beats downbeat, which beats accent, which beats beat.</summary>
        public int Priority
        {
            get
            {
                switch (Position)
                {
                    case GridFlags.Phrase: return 5;
                    case GridFlags.Downbeat: return 4;
                    case GridFlags.Accent: return 3;
                    case GridFlags.Beat: return 2;
                    case GridFlags.Eighth: return 1;
                    default: return 0;
                }
            }
        }

        public static GridEntry Parse(string s)
        {
            var e = new GridEntry();
            int at = s.IndexOf('@');
            if (at < 0) { e.Name = s.Trim(); return e; }
            e.Name = s.Substring(0, at).Trim();
            e.Positioned = true;
            string pos = s.Substring(at + 1).Trim();
            int slash = pos.IndexOf('/');
            if (slash >= 0)
            {
                e.Every = Math.Max(1, int.Parse(pos.Substring(slash + 1), CultureInfo.InvariantCulture));
                pos = pos.Substring(0, slash);
            }
            switch (pos)
            {
                case "beat": e.Position = GridFlags.Beat; break;
                case "eighth": e.Position = GridFlags.Eighth; break;
                case "sixteenth": e.Position = GridFlags.Sixteenth; break;
                case "downbeat": e.Position = GridFlags.Downbeat; break;
                case "phrase": e.Position = GridFlags.Phrase; break;
                case "accent": e.Position = GridFlags.Accent; break;
                default: throw new FormatException("unknown grid position '" + pos + "' in '" + s + "'");
            }
            return e;
        }

        public static GridEntry[] ParseList(JsonValue arr)
        {
            var list = new GridEntry[arr.Count];
            for (int i = 0; i < arr.Count; i++) list[i] = Parse(arr[i].AsString());
            return list;
        }

        public override string ToString() => Positioned ? Name + "@" + Position.ToString().ToLowerInvariant() + (Every > 1 ? "/" + Every : "") : Name;
    }

    /// <summary>
    /// An effect load preset (choreography.md, "Stress timelines"): deaths per second as a
    /// fraction of N, hits per death, and its spells.
    /// </summary>
    public sealed class LoadPreset
    {
        public string Name;
        public float DeathsPerN;     // deaths/s = N × this
        public float HitsPerDeath;
        public string[] Spells;

        public static readonly LoadPreset None = new LoadPreset { Name = "none", DeathsPerN = 0, HitsPerDeath = 0, Spells = new string[0] };
        public static readonly LoadPreset Light = new LoadPreset { Name = "light", DeathsPerN = 1f / 40, HitsPerDeath = 2, Spells = new[] { "ring@downbeat" } };
        public static readonly LoadPreset Demo = new LoadPreset
        {
            Name = "demo", DeathsPerN = 1f / 20, HitsPerDeath = 2,
            Spells = new[] { "ring@downbeat", "bolt@beat", "lightning@beat/3" },
        };
        public static readonly LoadPreset Heavy = new LoadPreset
        {
            Name = "heavy", DeathsPerN = 1f / 10, HitsPerDeath = 3,
            Spells = new[] { "ring@beat/2", "bolt@eighth", "lightning@eighth/3" },
        };

        public static LoadPreset Get(string name)
        {
            switch (name)
            {
                case null:
                case "none": return None;
                case "light": return Light;
                case "demo": return Demo;
                case "heavy": return Heavy;
                default: throw new FormatException("unknown load preset '" + name + "'");
            }
        }

        public float DeathsPerSecond(int n) => n * DeathsPerN;
        public float HitsPerSecond(int n) => n * DeathsPerN * HitsPerDeath;
    }

    public readonly struct MixEntry
    {
        public readonly string Type;
        public readonly float Weight;
        public MixEntry(string type, float weight) { Type = type; Weight = weight; }

        public static MixEntry[] ParseMix(JsonValue obj)
        {
            var list = new List<MixEntry>();
            if (obj.Type == JsonType.Object)
                foreach (KeyValuePair<string, JsonValue> kv in obj.Members) list.Add(new MixEntry(kv.Key, kv.Value.AsFloat(1f)));
            return list.ToArray();
        }
    }

    public sealed class LevelSection
    {
        public string Section;
        public float OnScreen;          // fraction of the envelope's count
        public float ViewWidthM;
        public string Load;
        public string Goal;             // a goal area's name in the layout, or null to hold ground
        public MixEntry[] Mix = new MixEntry[0];
        public GridEntry[] Combo = new GridEntry[0];
        public GridEntry[] Spells = new GridEntry[0];
        public string Boss;             // a roster type that enters at the section's start
        /// <summary>
        /// Opt-in (demo2d, PX8): a goal in the layout the boss enters from (outside the view, in its
        /// direction from the hero), and it always walks in rather than waiting off screen to be
        /// released. Null keeps the original entry: toward the hero's goal, released like any other.
        /// </summary>
        public string BossFrom;
        public bool Clear;              // the hero's combos clear the screen: no releases or refills (the ending, TD13)
    }

    /// <summary>The level script (choreography.md, "The choreographer"): per song-map section, its targets.</summary>
    public sealed class LevelScript
    {
        public string Song, Layout;
        public int EnvelopeOnScreen;
        public float EnvelopeViewWidthM;
        public uint Seed = 1;
        public float ReserveFactor = 0.5f;
        /// <summary>The game camera's pitch, which shapes the view the horde is counted and spawned around (its <c>viewPitchDeg</c>: demo3d's 55°, or 90° for a top-down 2D view).</summary>
        public double ViewPitchDeg = ViewGeometry.PitchDeg;
        /// <summary>
        /// The clear ring (its <c>clearRadiusM</c>): monsters hold their bodies at least this far from the
        /// hero's centre, backing off when the hero closes in, so the hero stands in a cleared ring as in a
        /// horde survivor game instead of buried in melee. 0, the default, lets them close to contact.
        /// </summary>
        public float ClearRadiusM;
        /// <summary>
        /// Opt-in (demo3d's look pass): refills arrive in same-type clumps (its <c>clumpSize</c>, the section's
        /// heaviest type's; lighter types' clumps scale with their weights), so the horde forms masses of colour
        /// instead of a random mix. Each section's count per type still follows its mix. 0, the default, picks
        /// every monster's type at random.
        /// </summary>
        public int ClumpSize;
        public LevelSection[] Sections;

        public LevelSection For(string sectionLabel)
        {
            foreach (LevelSection s in Sections) if (s.Section == sectionLabel) return s;
            return null;
        }

        /// <summary>
        /// Scales every section's view width so the widest is <paramref name="widestM"/>, keeping
        /// the zoom arc's shape (the player's `--view-width` on the song). The timeline places the
        /// horde and its reserve for these widths, so the camera never shows the reserve's edge.
        /// </summary>
        public void ScaleViewWidth(float widestM)
        {
            if (!(widestM > 0)) throw new ArgumentOutOfRangeException(nameof(widestM));
            float widest = 0;
            foreach (LevelSection s in Sections) widest = Math.Max(widest, s.ViewWidthM);
            if (widest <= 0) return;
            float k = widestM / widest;
            foreach (LevelSection s in Sections) s.ViewWidthM *= k;
            EnvelopeViewWidthM = widestM;
        }

        public static LevelScript Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            var ls = new LevelScript
            {
                Song = root["song"].AsString(),
                Layout = root["layout"].AsString(),
                EnvelopeOnScreen = root["envelope"]["onScreen"].AsInt(2000),
                EnvelopeViewWidthM = root["envelope"]["viewWidthM"].AsFloat(40f),
                Seed = (uint)root["seed"].AsDouble(1),
                ReserveFactor = root["reserve"].AsFloat(0.5f),
                ViewPitchDeg = root["viewPitchDeg"].AsDouble(ViewGeometry.PitchDeg),
                ClearRadiusM = root["clearRadiusM"].AsFloat(0f),
                ClumpSize = Math.Max(0, root["clumpSize"].AsInt(0)),
            };
            JsonValue secs = root["sections"];
            ls.Sections = new LevelSection[secs.Count];
            for (int i = 0; i < secs.Count; i++)
            {
                JsonValue s = secs[i];
                ls.Sections[i] = new LevelSection
                {
                    Section = s["section"].AsString(),
                    OnScreen = s["onScreen"].AsFloat(0.5f),
                    ViewWidthM = Math.Min(s["viewWidthM"].AsFloat(ls.EnvelopeViewWidthM), ls.EnvelopeViewWidthM),
                    Load = s["load"].AsString("demo"),
                    Goal = s["goal"].AsString(),
                    Mix = MixEntry.ParseMix(s["mix"]),
                    Combo = GridEntry.ParseList(s["combo"]),
                    Spells = GridEntry.ParseList(s["spells"]),
                    Boss = s["boss"].AsString(),
                    BossFrom = s["bossFrom"].AsString(),
                    Clear = s["clear"].AsBool(false),
                };
                LoadPreset.Get(ls.Sections[i].Load);
            }
            return ls;
        }
    }

    /// <summary>
    /// A stress script (choreography.md, "Stress timelines"): N on screen with an off-screen
    /// reserve, a fixed view, the hero holding the centre, and a load preset, on a synthetic 4/4
    /// grid. The harness overrides N, the view width and the load per sweep point.
    /// </summary>
    public sealed class StressScript
    {
        public string Scene = "S3";
        public int N = 2000;
        public float ReserveFactor = 0.5f;
        public float ViewWidthM = 40f;
        public string Load = "demo";
        public double Bpm = 140;
        public double Seconds = 60;
        public uint Seed = 1;
        public MixEntry[] Mix = { new MixEntry("skeleton", 1) };
        public GridEntry[] Combo = new GridEntry[0];
        /// <summary>Null: the load preset's spells.</summary>
        public GridEntry[] Spells;
        public bool HeroAttacks = true, MonsterAttacks = true, WorstBeats;
        public float HoldRadiusM = 2f;
        /// <summary>The layout the script expects (its <c>layout</c>; null: any). The caller passes data on that layout.</summary>
        public string Layout;
        /// <summary>The named area whose goal centre is the hero's ground (its <c>hold</c>, TD6), or null for the origin.</summary>
        public string Hold;
        /// <summary>A ground point for the hero to hold instead of a named area's goal (TD15's beauty shots; null: <see cref="Hold"/>).</summary>
        public System.Numerics.Vector2? HoldAt;
        /// <summary>The opening horde starts at least this far from the hero (0: the hero's radius plus 1 m).</summary>
        public float OpeningInnerM;
        /// <summary>Which systems the client draws (crowd, hero, effects, lights); the timeline ignores it.</summary>
        public string[] Draw = new string[0];

        public StressScript Clone() => (StressScript)MemberwiseClone();

        /// <summary>A copy with the sweep axes replaced (non-positive or null keeps the script's value).</summary>
        public StressScript With(int n = 0, float viewWidthM = 0, string load = null, double seconds = 0)
        {
            StressScript c = Clone();
            if (n > 0) c.N = n;
            if (viewWidthM > 0) c.ViewWidthM = viewWidthM;
            if (load != null) c.Load = load;
            if (seconds > 0) c.Seconds = seconds;
            return c;
        }

        public GridEntry[] EffectiveSpells()
        {
            if (Spells != null) return Spells;
            string[] names = LoadPreset.Get(Load).Spells;
            var list = new GridEntry[names.Length];
            for (int i = 0; i < names.Length; i++) list[i] = GridEntry.Parse(names[i]);
            return list;
        }

        public static StressScript Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            var s = new StressScript
            {
                Scene = root["scene"].AsString("S3"),
                N = root["n"].AsInt(2000),
                ReserveFactor = root["reserve"].AsFloat(0.5f),
                ViewWidthM = root["viewWidthM"].AsFloat(40f),
                Load = root["load"].AsString("demo"),
                Bpm = root["bpm"].AsDouble(140),
                Seconds = root["seconds"].AsDouble(60),
                Seed = (uint)root["seed"].AsDouble(1),
                HeroAttacks = root["heroAttacks"].AsBool(true),
                MonsterAttacks = root["monsterAttacks"].AsBool(true),
                WorstBeats = root["worstBeats"].AsBool(false),
                HoldRadiusM = root["holdRadiusM"].AsFloat(2f),
                Layout = root["layout"].AsString(),
                Hold = root["hold"].AsString(),
                OpeningInnerM = root["openingInnerM"].AsFloat(0f),
            };
            if (root.Has("mix")) s.Mix = MixEntry.ParseMix(root["mix"]);
            s.Combo = GridEntry.ParseList(root["combo"]);
            if (root.Has("spells")) s.Spells = GridEntry.ParseList(root["spells"]);
            JsonValue draw = root["draw"];
            s.Draw = new string[draw.Count];
            for (int i = 0; i < draw.Count; i++) s.Draw[i] = draw[i].AsString();
            LoadPreset.Get(s.Load);
            return s;
        }
    }
}
