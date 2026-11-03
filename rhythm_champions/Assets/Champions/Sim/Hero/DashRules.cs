using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>Where a dash part lands: along the path as the hero passes, or at the dash's start, midpoint or end.</summary>
    public enum PartAt : byte
    {
        /// <summary>Along the dash's path (a <see cref="ShapeKind.Path"/> part).</summary>
        Path = 0,
        Start = 1,
        Mid = 2,
        End = 3,
    }

    /// <summary>When a dash part lands: during the dash, or on the beats after it.</summary>
    public enum PartWhen : byte
    {
        /// <summary>A path part as the hero passes each target; any other at its <see cref="PartAt"/> moment.</summary>
        Dash = 0,
        /// <summary>On the first beat after the dash ends, and the beats after it for each repeat (0.5 s apart beatless).</summary>
        NextBeat = 1,
        /// <summary>On the first downbeat after the dash ends, and the downbeats after it (as <see cref="NextBeat"/> before bars or beatless).</summary>
        NextDownbeat = 2,
    }

    /// <summary>
    /// One blow of a dash form (design.md, "Dashes"): its shape, where and when it lands, and what it
    /// deals. Sizes are the off-beat ones; an on-beat dash grows them by <see cref="DashRules.OnBeatArea"/>.
    /// </summary>
    public struct DashPart
    {
        public ShapeKind Shape;
        public PartAt At;
        public PartWhen When;
        /// <summary>How many times it lands, on successive beats or downbeats (1 for a <see cref="PartWhen.Dash"/> part).</summary>
        public int Repeat;
        /// <summary>The outer radius; a path part's reach either side of the path.</summary>
        public double RadiusM;
        /// <summary>A ring's inner radius.</summary>
        public double InnerM;
        /// <summary>A cone's half angle (radians; dashes.json gives the whole arc in degrees).</summary>
        public double HalfArc;
        /// <summary>Base hits (Sword Slash's 1x), off the beat.</summary>
        public double Damage;
        /// <summary>The tags besides <see cref="HitTags.Dash"/>, which every dash blow carries.</summary>
        public HitTags Tags;
        public ThrowKind Throw;
        public double ThrownDamage;
        public bool ThrownShatter;
        public bool Shatter;
        /// <summary>How many slashes the client draws (Blade Rush's three, then four); the sim deals <see cref="Damage"/> once.</summary>
        public int Slashes;
        /// <summary>The client's look for it (a name it knows, such as "cut" or "phantomBurst"); the sim ignores it.</summary>
        public string Look;
    }

    /// <summary>A dash at one rank, or its evolution: its fixed distance and duration and its blows.</summary>
    public sealed class DashForm
    {
        public double DistanceM;
        public double DurationS;
        public DashPart[] Parts = Array.Empty<DashPart>();

        /// <summary>Does it deal damage (so it breaks through a structure it runs into)?</summary>
        public bool Damaging
        {
            get
            {
                foreach (var p in Parts) if (p.Damage > 0) return true;
                return false;
            }
        }
    }

    /// <summary>One dash type (design.md, "Dashes"): ranks 1-3 and an evolution, each a fixed form.</summary>
    public sealed class DashKind
    {
        public string Id;
        public string Name;
        public DashForm[] Ranks;
        /// <summary>The fourth form, treasure's only; null when the dash has none (the plain Dash).</summary>
        public DashForm Evolution;

        /// <summary>The highest rank short of the evolution.</summary>
        public int TopRank => Ranks.Length;

        /// <summary>The evolution's rank number.</summary>
        public const int EvolutionRank = 4;

        public bool HasRank(int rank) => rank >= 1 && rank <= Ranks.Length || rank == EvolutionRank && Evolution != null;

        /// <summary>The form at <paramref name="rank"/> (1 to <see cref="TopRank"/>, or <see cref="EvolutionRank"/>).</summary>
        public DashForm Form(int rank)
        {
            if (rank == EvolutionRank && Evolution != null) return Evolution;
            if (rank < 1 || rank > Ranks.Length) throw new ArgumentOutOfRangeException(nameof(rank), $"{Id} has no rank {rank}");
            return Ranks[rank - 1];
        }
    }

    /// <summary>
    /// The dashes' numbers (Content/dashes.json; design.md Decisions 5 and 6, "The Dash" and "Dashes"):
    /// the rules every dash keeps and each dash type's forms. The defaults are the design's rules with
    /// only the plain Dash (a sim without content); dashes.json holds the five.
    /// </summary>
    public sealed class DashRules
    {
        public const string File = "dashes.json";

        // Archived replay fixtures only. Live runs always use the plain movement dash;
        // the old content schema remains readable while its consumers migrate.
        internal bool LegacyForms;

        /// <summary>From one dash's start to the next's earliest, for every dash type (Quickstep lowers it).</summary>
        public double CooldownS = 0.6;
        /// <summary>A press heard this close before the dash is ready fires the moment it is; earlier ones are dropped.</summary>
        public double BufferS = 0.15;
        /// <summary>The hero can't be hurt from a dash's start to this long after its end.</summary>
        public double IFramesAfterS = 0.1;
        /// <summary>On the beat, a dash's blows deal this many times their damage...</summary>
        public double OnBeatDamage = 2.0;
        /// <summary>...over shapes this many times as wide ("half again as wide").</summary>
        public double OnBeatArea = 1.5;
        /// <summary>The dash the hero starts with.</summary>
        public string Start = "dash";

        public DashKind[] Kinds =
        {
            new DashKind { Id = "dash", Name = "Dash", Ranks = new[] { new DashForm { DistanceM = 4, DurationS = 0.15 } } },
        };

        /// <summary>The index of dash type <paramref name="id"/>, or -1.</summary>
        public int IndexOf(string id)
        {
            for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].Id == id) return i;
            return -1;
        }

        public DashRules Clone() => (DashRules)MemberwiseClone();

        /// <summary>Reads dashes.json's object.</summary>
        public static DashRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new DashRules
            {
                CooldownS = r.Number("cooldownS"),
                BufferS = r.Number("bufferS"),
                IFramesAfterS = r.Number("iframesAfterS"),
                Start = r.String("start"),
            };
            var onBeat = r.Object("onBeat");
            rules.OnBeatDamage = onBeat.Number("damage");
            rules.OnBeatArea = onBeat.Number("area");
            onBeat.End();
            rules.Kinds = r.Objects("dashes", ReadKind);
            r.End();
            if (!(rules.CooldownS > 0)) throw ContentReader.Fail(path + ".cooldownS", "positive");
            if (!(rules.BufferS >= 0 && rules.BufferS < rules.CooldownS)) throw ContentReader.Fail(path + ".bufferS", "at least 0 and under the cooldown");
            if (!(rules.IFramesAfterS >= 0)) throw ContentReader.Fail(path + ".iframesAfterS", "at least 0");
            if (!(rules.OnBeatDamage >= 1)) throw ContentReader.Fail(path + ".onBeat.damage", "at least 1");
            if (!(rules.OnBeatArea >= 1)) throw ContentReader.Fail(path + ".onBeat.area", "at least 1");
            var ids = new HashSet<string>();
            for (int i = 0; i < rules.Kinds.Length; i++)
                if (!ids.Add(rules.Kinds[i].Id)) throw ContentReader.Fail(path + ".dashes[" + i + "].id", "duplicate " + rules.Kinds[i].Id);
            if (rules.IndexOf(rules.Start) < 0) throw ContentReader.Fail(path + ".start", "no dash " + rules.Start);
            return rules;
        }

        static DashKind ReadKind(ContentReader r)
        {
            var kind = new DashKind { Id = r.String("id"), Name = r.String("name") };
            var ranks = r.Array("ranks");
            if (ranks.Count < 1 || ranks.Count > 3) throw ContentReader.Fail(r.Child("ranks"), "1 to 3 ranks");
            kind.Ranks = new DashForm[ranks.Count];
            for (int i = 0; i < ranks.Count; i++)
            {
                var f = new ContentReader(ranks[i], r.Child("ranks") + "[" + i + "]");
                kind.Ranks[i] = ReadForm(f);
                f.End();
            }
            var evolution = r.ObjectOrNull("evolution");
            if (evolution != null)
            {
                kind.Evolution = ReadForm(evolution);
                evolution.End();
            }
            return kind;
        }

        static DashForm ReadForm(ContentReader r)
        {
            var form = new DashForm
            {
                DistanceM = r.Number("distanceM"),
                DurationS = r.Number("durationS"),
                Parts = r.Objects("parts", ReadPart),
            };
            if (!(form.DistanceM > 0)) throw ContentReader.Fail(r.Child("distanceM"), "positive");
            if (!(form.DurationS > 0)) throw ContentReader.Fail(r.Child("durationS"), "positive");
            return form;
        }

        static DashPart ReadPart(ContentReader r)
        {
            var part = new DashPart
            {
                Shape = Enum(r, "shape", ShapeNames),
                When = Enum(r, "when", WhenNames, PartWhen.Dash),
                Repeat = r.IntOr("repeat", 1),
                RadiusM = r.Number("radiusM"),
                Damage = r.Number("damage"),
                Throw = Enum(r, "throw", ThrowNames, ThrowKind.None),
                ThrownDamage = r.NumberOr("thrownDamage", 0),
                ThrownShatter = r.BoolOr("thrownShatter", false),
                Shatter = r.BoolOr("shatter", false),
                Slashes = r.IntOr("slashes", 0),
                Look = r.StringOr("look", null),
            };
            part.At = part.Shape == ShapeKind.Path ? PartAt.Path : Enum(r, "at", AtNames);
            if (part.Shape == ShapeKind.Ring) part.InnerM = r.Number("innerM");
            if (part.Shape == ShapeKind.Cone) part.HalfArc = r.Number("arcDeg") * Math.PI / 360;
            if (r.Has("tags"))
                foreach (string tag in r.Strings("tags"))
                {
                    if (!TagNames.TryGetValue(tag, out var t)) throw ContentReader.Fail(r.Child("tags"), "no tag " + tag);
                    part.Tags |= t;
                }
            if (!(part.RadiusM > 0)) throw ContentReader.Fail(r.Child("radiusM"), "positive");
            if (!(part.InnerM >= 0 && part.InnerM < part.RadiusM)) throw ContentReader.Fail(r.Child("innerM"), "at least 0 and under radiusM");
            if (part.Shape == ShapeKind.Cone && !(part.HalfArc > 0 && part.HalfArc <= Math.PI)) throw ContentReader.Fail(r.Child("arcDeg"), "over 0, at most 360");
            if (!(part.Damage >= 0)) throw ContentReader.Fail(r.Child("damage"), "at least 0");
            if (part.Repeat < 1 || part.When == PartWhen.Dash && part.Repeat != 1) throw ContentReader.Fail(r.Child("repeat"), "1 during the dash, at least 1 after it");
            if (part.At == PartAt.Path && part.Shape != ShapeKind.Path) throw ContentReader.Fail(r.Child("at"), "a path is a path part's");
            return part;
        }

        static readonly Dictionary<string, ShapeKind> ShapeNames = new Dictionary<string, ShapeKind>
        {
            ["circle"] = ShapeKind.Circle, ["ring"] = ShapeKind.Ring, ["path"] = ShapeKind.Path, ["cone"] = ShapeKind.Cone,
        };

        static readonly Dictionary<string, PartAt> AtNames = new Dictionary<string, PartAt>
        {
            ["start"] = PartAt.Start, ["mid"] = PartAt.Mid, ["end"] = PartAt.End,
        };

        static readonly Dictionary<string, PartWhen> WhenNames = new Dictionary<string, PartWhen>
        {
            ["dash"] = PartWhen.Dash, ["nextBeat"] = PartWhen.NextBeat, ["nextDownbeat"] = PartWhen.NextDownbeat,
        };

        static readonly Dictionary<string, ThrowKind> ThrowNames = new Dictionary<string, ThrowKind>
        {
            ["none"] = ThrowKind.None, ["aside"] = ThrowKind.Aside, ["away"] = ThrowKind.Away,
        };

        static readonly Dictionary<string, HitTags> TagNames = new Dictionary<string, HitTags>
        {
            ["strike"] = HitTags.Strike, ["area"] = HitTags.Area, ["summon"] = HitTags.Summon,
        };

        static T Enum<T>(ContentReader r, string key, Dictionary<string, T> names)
        {
            string name = r.String(key);
            if (!names.TryGetValue(name, out var value))
                throw ContentReader.Fail(r.Child(key), "one of " + string.Join(", ", names.Keys) + ", not " + name);
            return value;
        }

        static T Enum<T>(ContentReader r, string key, Dictionary<string, T> names, T fallback) =>
            r.Has(key) ? Enum(r, key, names) : fallback;
    }
}
