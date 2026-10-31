using System;
using System.Collections.Generic;
using System.Globalization;

namespace PS.Choreography
{
    /// <summary>
    /// The trailer's positions (trailer.md, "The trailer file"), each a single grid point resolved before
    /// the run: <c>cut</c> (the script's cut), <c>final</c> (its final slam), <c>anchor</c> (the next unused
    /// anchor of a free section) and an explicit bar.beat such as <c>82.3</c>.
    /// </summary>
    public enum FixedPosition { None, Anchor, BarBeat, Cut, Final }

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
        /// <summary>A trailer position (<see cref="FixedPosition"/>): the entry fires only at <see cref="At"/>, once it is resolved.</summary>
        public FixedPosition Fixed;
        public int Bar, Beat;          // an explicit bar.beat's
        /// <summary>A fixed entry's song time (NaN until <see cref="Resolve"/>).</summary>
        public double At = double.NaN;

        /// <summary>A plain combo entry's rank: it plays on the beat.</summary>
        public const int BeatPriority = 2;

        /// <summary>
        /// When positions coincide: final beats cut, which beats an explicit bar.beat, then anchor, phrase,
        /// downbeat, accent and beat (trailer.md, "How a shot uses the base").
        /// </summary>
        public int Priority
        {
            get
            {
                switch (Fixed)
                {
                    case FixedPosition.Final: return 9;
                    case FixedPosition.Cut: return 8;
                    case FixedPosition.BarBeat: return 7;
                    case FixedPosition.Anchor: return 6;
                }
                switch (Position)
                {
                    case GridFlags.Phrase: return 5;
                    case GridFlags.Downbeat: return 4;
                    case GridFlags.Accent: return 3;
                    case GridFlags.Beat: return BeatPriority;
                    case GridFlags.Eighth: return 1;
                    default: return 0;
                }
            }
        }

        /// <summary>Does the entry fire on grid point <paramref name="p"/>: at its fixed time, or on every point with its position's flags?</summary>
        public bool Matches(in GridPoint p) => Fixed != FixedPosition.None ? Math.Abs(p.Time - At) <= 1e-6 : (p.Flags & Position) == Position;

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
                case "cut": e.Fixed = FixedPosition.Cut; break;
                case "final": e.Fixed = FixedPosition.Final; break;
                case "anchor": e.Fixed = FixedPosition.Anchor; break;
                default:
                    if (!SongMap.TryParseBarBeat(pos, out e.Bar, out e.Beat)) throw new FormatException("unknown grid position '" + pos + "' in '" + s + "'");
                    e.Fixed = FixedPosition.BarBeat;
                    break;
            }
            return e;
        }

        public static GridEntry[] ParseList(JsonValue arr)
        {
            var list = new GridEntry[arr.Count];
            for (int i = 0; i < arr.Count; i++) list[i] = Parse(arr[i].AsString());
            return list;
        }

        /// <summary>
        /// Copies of <paramref name="entries"/> with their fixed positions resolved to song times on
        /// <paramref name="map"/>: <c>cut</c> and <c>final</c> to <paramref name="cut"/> and <paramref name="final"/>,
        /// a bar.beat to its beat (on the grid), and each <c>anchor</c>, in list order, to the next anchor of a free
        /// section from <paramref name="anchorsFrom"/> on, consuming it. An entry its script gives no time for fails.
        /// </summary>
        public static GridEntry[] Resolve(GridEntry[] entries, SongMap map, BeatGrid grid, double cut, double final, double anchorsFrom, string what)
        {
            var anchors = new List<double>();
            foreach (SongSection sec in map.Sections)
                if (sec.Feel == SectionFeel.Free)
                    foreach (TimedStrength a in sec.Anchors) if (a.Time >= anchorsFrom - 1e-9) anchors.Add(a.Time);
            anchors.Sort();
            int nextAnchor = 0;
            var list = new GridEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                GridEntry e = entries[i];
                if (e.Fixed == FixedPosition.None) { list[i] = e; continue; }
                var r = (GridEntry)e.MemberwiseClone();
                switch (e.Fixed)
                {
                    case FixedPosition.Cut:
                        if (double.IsNaN(cut)) throw new FormatException(what + " '" + e + "' needs the script's cut");
                        r.At = cut;
                        break;
                    case FixedPosition.Final:
                        if (double.IsNaN(final)) throw new FormatException(what + " '" + e + "' needs the script's final");
                        r.At = final;
                        break;
                    case FixedPosition.Anchor:
                        if (nextAnchor >= anchors.Count) throw new FormatException(what + " '" + e + "': no free section's anchor is left for it");
                        r.At = anchors[nextAnchor++];
                        break;
                    default:
                        r.At = map.TimeOfBarBeat(e.Bar, e.Beat, grid);
                        break;
                }
                list[i] = r;
            }
            return list;
        }

        public override string ToString()
        {
            if (!Positioned) return Name;
            string pos = Fixed == FixedPosition.BarBeat ? Bar + "." + Beat : Fixed != FixedPosition.None ? Fixed.ToString().ToLowerInvariant() : Position.ToString().ToLowerInvariant();
            return Name + "@" + pos + (Every > 1 ? "/" + Every : "");
        }
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

        /// <summary>A level script's own preset (its <c>loads</c>): <c>deathsPerN</c>, <c>hitsPerDeath</c> and <c>spells</c>.</summary>
        public static LoadPreset Parse(string name, JsonValue o)
        {
            JsonValue spells = o["spells"];
            var p = new LoadPreset { Name = name, DeathsPerN = o["deathsPerN"].AsFloat(), HitsPerDeath = o["hitsPerDeath"].AsFloat(), Spells = new string[spells.Count] };
            for (int i = 0; i < spells.Count; i++) p.Spells[i] = spells[i].AsString();
            if (!(p.DeathsPerN >= 0) || !(p.HitsPerDeath >= 0)) throw new FormatException("load preset '" + name + "': negative rate");
            return p;
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
        /// <summary>The structures a hero's spell breaks in this section (a structure-breaks overlay's; <see cref="StructureBreaks"/>): target@position entries.</summary>
        public GridEntry[] Breaks = new GridEntry[0];
    }

    /// <summary>
    /// The sim's tuning knobs a level script (or an intensity overlay over one) may set; each defaults
    /// to the value the sim had before it became a field, so a script without them runs as before.
    /// </summary>
    public sealed class SimTuning
    {
        /// <summary>The hero's combo clips speed up at most this much to land on their grid point (its <c>comboMaxSpeed</c>; monsters keep <see cref="ClipLibrary.MaxSpeed"/>).</summary>
        public float ComboMaxSpeed = ClipLibrary.MaxSpeed;
        /// <summary>
        /// ... and play at least this fast (its <c>comboMinSpeed</c>): the fit plays a clip as slowly as still
        /// lands it, so a slow follow-through can run past the next beat; a higher floor frees the hero for it.
        /// </summary>
        public float ComboMinSpeed = ClipLibrary.MinSpeed;
        /// <summary>The kill-rate feedback's caps on the hit shapes' scale: while the kill budget runs behind (<c>maxShapeScale</c>), and otherwise (<c>hitsShapeScale</c>).</summary>
        public float MaxShapeScale = 4f, HitsShapeScale = 2.5f;
        /// <summary>A scaled hit shape is at most this fraction of the view's width (its <c>maxShapeViewFraction</c>).</summary>
        public float MaxShapeViewFraction = 0.25f;
        /// <summary>
        /// Per rank, a multiplier on the roster's hit points (its <c>hitPointScale</c>, an object keyed by rank:
        /// <c>{ "fodder": 0 }</c> makes every fodder a one-hit kill). Struck monsters die fewest hit points first.
        /// </summary>
        public float FodderHitPointScale = 1f, EliteHitPointScale = 1f, BossHitPointScale = 1f;
        /// <summary>A beat's refill spawns at most max(<c>refillMinPerBeat</c>, the wanted total / <c>refillDivisor</c>).</summary>
        public int RefillMinPerBeat = 24;
        public float RefillDivisor = 6f;
        /// <summary>Releases add this many seconds of the expected deaths, for the walk in from the spawn band (its <c>releaseLeadS</c>).</summary>
        public float ReleaseLeadS = 2.5f;
        /// <summary>
        /// A hero on its way to a goal attacks whatever its scaled hit shapes reach, as it does holding ground
        /// (its <c>travelScaledReach</c>), so it carves its way instead of striding between blows. False, the
        /// default: travelling, it attacks only what is within its unscaled reach.
        /// </summary>
        public bool TravelScaledReach;
        /// <summary>
        /// The hero holds back a beat's blow (or a flurry's) that would leave no room for the windup of the
        /// section's next spin or slam on its accent or phrase (its <c>yieldToAccents</c>). False, the default:
        /// such a spin or slam moves later, as any positioned combo that can't fit does.
        /// </summary>
        public bool YieldToAccents;
        /// <summary>
        /// The aggregated sounds' gain for a beat's <c>n</c> requests of a category (its <c>soundGain</c> object:
        /// <c>base</c>, <c>perDoubling</c> and <c>max</c>): min(max, (base + perDoubling × log2 n) × (0.7 + 0.3 × strongest)).
        /// </summary>
        public double SoundGainBase = 0.35, SoundGainPerDoubling = 0.15, SoundGainMax = 1.0;
        /// <summary>
        /// A hero blow that kills at least this many also asks for the ring's heavy impact (its <c>heavyBlowKills</c>),
        /// at a strength growing to 1 at twice as many, so a mass kill lands as one layered boom. 0, the default: none.
        /// </summary>
        public int HeavyBlowKills;
        /// <summary>
        /// Every monster but the boss dies to the first blow or spell that strikes it, the laser's sweep
        /// included (its <c>oneHitKills</c>), as in a horde survivor game: the load preset's rate steers only
        /// the hit shapes' size, never who survives a strike. False, the default: the kill budget decides who
        /// dies, and the rest take non-lethal hits.
        /// </summary>
        public bool OneHitKills;
        /// <summary>
        /// A walking-in boss tramples the monsters in its step's way in view too, the big ones included, not
        /// only the small fry on its way in (its <c>bossTramplesInView</c>), so a dense horde never holds it off
        /// the hero until its last bar. The trailer's boss rule keeps its own boss. False, the default: in view
        /// the boss walks round the horde.
        /// </summary>
        public bool BossTramplesInView;

        public static readonly SimTuning Default = new SimTuning();

        public float HitPointScale(MonsterRank rank) =>
            rank == MonsterRank.Fodder ? FodderHitPointScale : rank == MonsterRank.Elite ? EliteHitPointScale : BossHitPointScale;

        public static SimTuning Parse(JsonValue root)
        {
            var d = Default;
            JsonValue hp = root["hitPointScale"], gain = root["soundGain"];
            var t = new SimTuning
            {
                ComboMaxSpeed = root["comboMaxSpeed"].AsFloat(d.ComboMaxSpeed),
                ComboMinSpeed = root["comboMinSpeed"].AsFloat(d.ComboMinSpeed),
                MaxShapeScale = root["maxShapeScale"].AsFloat(d.MaxShapeScale),
                HitsShapeScale = root["hitsShapeScale"].AsFloat(d.HitsShapeScale),
                MaxShapeViewFraction = root["maxShapeViewFraction"].AsFloat(d.MaxShapeViewFraction),
                FodderHitPointScale = hp["fodder"].AsFloat(d.FodderHitPointScale),
                EliteHitPointScale = hp["elite"].AsFloat(d.EliteHitPointScale),
                BossHitPointScale = hp["boss"].AsFloat(d.BossHitPointScale),
                RefillMinPerBeat = root["refillMinPerBeat"].AsInt(d.RefillMinPerBeat),
                RefillDivisor = root["refillDivisor"].AsFloat(d.RefillDivisor),
                ReleaseLeadS = root["releaseLeadS"].AsFloat(d.ReleaseLeadS),
                TravelScaledReach = root["travelScaledReach"].AsBool(d.TravelScaledReach),
                YieldToAccents = root["yieldToAccents"].AsBool(d.YieldToAccents),
                SoundGainBase = gain["base"].AsDouble(d.SoundGainBase),
                SoundGainPerDoubling = gain["perDoubling"].AsDouble(d.SoundGainPerDoubling),
                SoundGainMax = gain["max"].AsDouble(d.SoundGainMax),
                HeavyBlowKills = root["heavyBlowKills"].AsInt(d.HeavyBlowKills),
                OneHitKills = root["oneHitKills"].AsBool(d.OneHitKills),
                BossTramplesInView = root["bossTramplesInView"].AsBool(d.BossTramplesInView),
            };
            if (!(t.ComboMinSpeed >= ClipLibrary.MinSpeed) || !(t.ComboMaxSpeed >= t.ComboMinSpeed))
                throw new FormatException("hero combo speeds " + t.ComboMinSpeed + " to " + t.ComboMaxSpeed + " are out of order or below the clips' minimum");
            if (!(t.MaxShapeScale > 0) || !(t.HitsShapeScale > 0) || !(t.MaxShapeViewFraction > 0)) throw new FormatException("hit shape caps must be positive");
            if (!(t.RefillDivisor > 0) || t.RefillMinPerBeat < 0 || !(t.ReleaseLeadS >= 0)) throw new FormatException("refill and release tuning out of range");
            if (!(t.SoundGainBase >= 0) || !(t.SoundGainPerDoubling >= 0) || !(t.SoundGainMax > 0) || t.HeavyBlowKills < 0)
                throw new FormatException("soundGain and heavyBlowKills must not be negative");
            if (!(t.FodderHitPointScale >= 0) || !(t.EliteHitPointScale >= 0) || !(t.BossHitPointScale >= 0)) throw new FormatException("hitPointScale must not be negative");
            return t;
        }
    }

    /// <summary>A point on the layout: a named area's goal centre, or [x, y] metres.</summary>
    public sealed class LayoutPoint
    {
        public string Area;                  // null: metres
        public System.Numerics.Vector2 At;

        public static LayoutPoint Parse(JsonValue v, string what)
        {
            if (v.Type == JsonType.String) return new LayoutPoint { Area = v.AsString() };
            if (v.Type == JsonType.Array && v.Count == 2) return new LayoutPoint { At = new System.Numerics.Vector2(v[0].AsFloat(), v[1].AsFloat()) };
            throw new FormatException(what + ": expected a named area or [x, y] metres");
        }

        /// <summary>The point on <paramref name="layout"/>; an unknown area fails, naming it.</summary>
        public System.Numerics.Vector2 Resolve(GroundLayout layout, string what)
        {
            if (Area == null) return At;
            GoalArea g = layout.Goal(Area);
            if (g == null) throw new FormatException(what + ": unknown area '" + Area + "' in layout '" + layout.Name + "'");
            return g.Center;
        }

        public override string ToString() => Area ?? "[" + At.X.ToString(CultureInfo.InvariantCulture) + ", " + At.Y.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>The hero's kite path (trailer.md, "Shot simulations"): waypoints, a stride per beat, and whether it closes into a circuit.</summary>
    public sealed class HeroPath
    {
        public LayoutPoint[] Via;
        public float StepM;
        public bool Loop;

        public static HeroPath Parse(JsonValue o)
        {
            JsonValue via = o["via"];
            var p = new HeroPath { Via = new LayoutPoint[via.Count], StepM = o["stepM"].AsFloat(0f), Loop = o["loop"].AsBool(false) };
            for (int i = 0; i < via.Count; i++) p.Via[i] = LayoutPoint.Parse(via[i], "path waypoint " + (i + 1));
            if (p.Via.Length == 0 || !(p.StepM > 0)) throw new FormatException("path: needs at least one waypoint and a positive stepM");
            return p;
        }
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
        /// <summary>
        /// Opt-in (demo2d): a monster left off screen and unreleased this long (its <c>strandedS</c>) leaves
        /// the song, so the horde a travelling hero leaves behind (beyond demo2d's stream, which it crosses
        /// only by its bridges) stops filling the reserve that refills on the hero's side would. 0, the
        /// default, keeps it until it is past the far despawn.
        /// </summary>
        public float StrandedS;
        /// <summary>
        /// Opt-in (demo2d, its <c>bossFallsLast</c>): a clearing section's blows spare the boss until its last,
        /// however near, so the boss falls to the song's final blow. False, the default, kills nearest first.
        /// </summary>
        public bool BossFallsLast;
        /// <summary>The sim's tuning knobs (<see cref="SimTuning"/>), from the script's top-level fields.</summary>
        public SimTuning Tuning = SimTuning.Default;
        /// <summary>The script's own load presets (its <c>loads</c>, by name), beside the built-in ones; a section's <c>load</c> may name either.</summary>
        public Dictionary<string, LoadPreset> Loads = new Dictionary<string, LoadPreset>();
        /// <summary>The intensity overlay merged over the script (<see cref="IntensityOverlay"/>), or null.</summary>
        public string Intensity;
        /// <summary>The structure-breaks overlay merged over the script after it (<see cref="PS.Choreography.StructureBreaks"/>), or null.</summary>
        public string StructureBreaks;
        public LevelSection[] Sections;

        // A trailer shot's fields (trailer.md, "Shot simulations"). Each is opt-in: its default runs the song as before.

        /// <summary>The simulated span in song seconds (its <c>spanS</c>: [from, to]). NaN, the default: the whole song.</summary>
        public double SpanFromS = double.NaN, SpanToS = double.NaN;
        /// <summary>The bar.beat an <c>@cut</c> entry fires on (its <c>cut</c>), and an <c>@final</c> entry's (its <c>final</c>): the final slam, which nothing else shares. Null: none.</summary>
        public string Cut, Final;
        /// <summary>Where the hero starts (its <c>heroAt</c>: a named area or [x, y] metres). Null: the layout's hero start, or the path's first waypoint.</summary>
        public LayoutPoint HeroAt;
        /// <summary>The hero's kite path (its <c>heroPath</c>), replacing goals: null, the default, heads for the sections' goals.</summary>
        public HeroPath HeroPath;
        /// <summary>
        /// The hero holds ground (its <c>holdGround</c>, the trailer's duel): it faces the nearest monster for each
        /// blow, and on a beat without one takes a short sideways step. False, the default: it steps toward the densest monsters.
        /// </summary>
        public bool HoldGround;
        /// <summary>The hero's step time (its <c>heroStepS</c>), replacing the roster's <see cref="Roster.HeroStepS"/>. Negative, the default: the roster's.</summary>
        public float HeroStepS = -1;
        /// <summary>The kill budget (its <c>killBudget</c>): false lets every blow and spell kill all it strikes, and spells never hold back. True, the default.</summary>
        public bool KillBudget = true;
        /// <summary>A fixed scale for the hit shapes (its <c>shapeScale</c>), with the kill-rate feedback off. 0, the default: the feedback scales them.</summary>
        public float ShapeScale;
        /// <summary>The camera's heading in degrees (its <c>yawDeg</c>), which turns the view the horde is counted and spawned around. 0, the default.</summary>
        public float YawDeg;
        /// <summary>A view width the zoom eases to (its <c>viewWidthToM</c>), from the cut (or the span's start) to the span's end. 0, the default: none.</summary>
        public float ViewWidthToM;
        /// <summary>
        /// The boss rule (its <c>boss</c>: <c>ahead</c>, <c>behind</c>, <c>left</c>, <c>right</c>, relative to the hero's travel
        /// at the cut, or a named area): the roster's boss enters from there outside the view on the cut, walks in, keeps
        /// beyond the largest hit shape plus 1 m, takes no blow or spell, and dies to the final slam. Null, the default: none.
        /// </summary>
        public string Boss;
        /// <summary>A fixed cast (its <c>duel</c>: counts by type), staged to meet the fixed combo entries in order, with no refill. Null, the default: none.</summary>
        public MixEntry[] Duel;
        /// <summary>
        /// The horde's formation (its <c>formation</c>: <c>{ "kind": "charge" | "vortex" | "converge", "streams": 2 to 6 }</c>,
        /// trailer.md "Kiting and formations"): the opening placement, the refill and never-jam steering. Null, the default:
        /// the song's opening horde, releases and refill.
        /// </summary>
        public string Formation;
        /// <summary>A converge formation's streams (its <c>formation</c>'s <c>streams</c>).</summary>
        public int FormationStreams;
        /// <summary>The most monsters per square metre a formation's refill spawns into (its <c>densityCap</c>).</summary>
        public float DensityCap = DefaultDensityCap;

        public const float DefaultDensityCap = 1.2f;
        static readonly string[] FormationKinds = { "charge", "vortex", "converge" };

        /// <summary>A section's load preset: the script's own by that name, else a built-in one.</summary>
        public LoadPreset Preset(string name) => name != null && Loads.TryGetValue(name, out LoadPreset p) ? p : LoadPreset.Get(name);

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

        /// <summary>
        /// The script from its JSON, with an intensity overlay's JSON (<see cref="IntensityOverlay"/>) merged over it
        /// first, if given, and then a structure-breaks overlay's (<see cref="PS.Choreography.StructureBreaks"/>).
        /// </summary>
        public static LevelScript Parse(string json, string overlayJson = null, string breaksJson = null)
        {
            JsonValue root = JsonValue.Parse(json);
            string intensity = null, breaks = null;
            if (overlayJson != null)
            {
                var overlay = IntensityOverlay.Parse(overlayJson);
                root = overlay.Apply(root);
                intensity = overlay.Name;
            }
            if (breaksJson != null)
            {
                var overlay = PS.Choreography.StructureBreaks.Parse(breaksJson);
                root = overlay.Apply(root);
                breaks = overlay.Name;
            }
            return FromJson(root, intensity, breaks);
        }

        /// <summary>The script from its parsed JSON (a trailer's shot script builds it), naming the overlays already merged into it, if any.</summary>
        public static LevelScript FromJson(JsonValue root, string intensity = null, string structureBreaks = null)
        {
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
                StrandedS = Math.Max(0f, root["strandedS"].AsFloat(0f)),
                BossFallsLast = root["bossFallsLast"].AsBool(false),
                Tuning = SimTuning.Parse(root),
                Intensity = intensity,
                Cut = root["cut"].AsString(),
                Final = root["final"].AsString(),
                HeroAt = root.Has("heroAt") ? LayoutPoint.Parse(root["heroAt"], "heroAt") : null,
                HeroPath = root.Has("heroPath") ? HeroPath.Parse(root["heroPath"]) : null,
                HoldGround = root["holdGround"].AsBool(false),
                HeroStepS = root["heroStepS"].AsFloat(-1f),
                KillBudget = root["killBudget"].AsBool(true),
                ShapeScale = root["shapeScale"].AsFloat(0f),
                YawDeg = root["yawDeg"].AsFloat(0f),
                ViewWidthToM = root["viewWidthToM"].AsFloat(0f),
                Boss = root["boss"].AsString(),
                Duel = root.Has("duel") ? MixEntry.ParseMix(root["duel"]) : null,
                StructureBreaks = structureBreaks,
                Formation = root["formation"]["kind"].AsString(),
                FormationStreams = root["formation"]["streams"].AsInt(0),
                DensityCap = root["densityCap"].AsFloat(DefaultDensityCap),
            };
            if (root.Has("formation") && Array.IndexOf(FormationKinds, ls.Formation) < 0) throw new FormatException("level script: unknown formation '" + ls.Formation + "'");
            if (ls.Formation == "converge" && (ls.FormationStreams < 2 || ls.FormationStreams > 6)) throw new FormatException("level script: converge needs 2 to 6 streams");
            if (!(ls.DensityCap > 0)) throw new FormatException("level script: densityCap must be positive");
            JsonValue span = root["spanS"];
            if (span.Type == JsonType.Array)
            {
                if (span.Count != 2 || !(span[1].AsDouble() > span[0].AsDouble())) throw new FormatException("level script: spanS must be [from, to] with to after from");
                ls.SpanFromS = span[0].AsDouble();
                ls.SpanToS = span[1].AsDouble();
            }
            if (!(ls.ShapeScale >= 0) || !(ls.ViewWidthToM >= 0)) throw new FormatException("level script: shapeScale and viewWidthToM must not be negative");
            if (ls.ViewWidthToM > 0 && double.IsNaN(ls.SpanFromS)) throw new FormatException("level script: viewWidthToM eases across a span, so it needs spanS");
            foreach (KeyValuePair<string, JsonValue> kv in root["loads"].Type == JsonType.Object ? root["loads"].Members : new List<KeyValuePair<string, JsonValue>>())
                ls.Loads[kv.Key] = LoadPreset.Parse(kv.Key, kv.Value);
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
                    Breaks = GridEntry.ParseList(s["breaks"]),
                };
                ls.Preset(ls.Sections[i].Load);
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
