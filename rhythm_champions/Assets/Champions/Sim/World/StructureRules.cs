using System;

namespace Champions.Sim
{
    /// <summary>A structure's ground shape: what blocks bodies and what blows must reach (<see cref="StructureKind"/>).</summary>
    public enum FootprintShape : byte
    {
        /// <summary>A disc of <see cref="StructureKind.Radius"/>: a tree's trunk, a rock, a round tower.</summary>
        Circle = 0,
        /// <summary>A rectangle <see cref="StructureKind.HalfX"/> by <see cref="StructureKind.HalfY"/>, turned with the structure.</summary>
        Box = 1,
    }

    /// <summary>
    /// One kind of structure (design.md, The World &gt; Structures): its id (the edge cut's, which the client draws), its
    /// footprint on the ground at angle 0 (x along its length) and the hits it takes to break.
    /// </summary>
    public sealed class StructureKind
    {
        public string Id;
        public FootprintShape Shape;
        public double Radius;
        public double HalfX, HalfY;
        /// <summary>The hero's blows it takes: a small one 1, a building 2 or 3.</summary>
        public int Hits = 1;

        /// <summary>The radius of a circle round its middle that holds all of the footprint, at any angle.</summary>
        public double Bound => Shape == FootprintShape.Circle ? Radius : Math.Sqrt(HalfX * HalfX + HalfY * HalfY);
    }

    /// <summary>One structure in a template: its kind, its middle from the chunk's middle and its angle (degrees).</summary>
    public struct Placement
    {
        public int Kind;
        public double X, Y, Degrees;
    }

    /// <summary>
    /// A cluster a chunk may hold (design.md, The World &gt; The infinite arena: a hamlet, a grove, a lone tower, or
    /// nothing). A chunk turns it a random quarter turn and nudges each structure by up to <see cref="JitterM"/> and
    /// <see cref="JitterDegrees"/>.
    /// </summary>
    public sealed class StructureTemplate
    {
        public string Id;
        /// <summary>Its share of the chunks, among the templates' weights.</summary>
        public double Weight = 1;
        public double JitterM, JitterDegrees;
        public Placement[] Placements = new Placement[0];
    }

    /// <summary>
    /// The structures' rules (Content/structures.json; design.md, The World, Decision 18; plan.md, The Sim &gt; Systems):
    /// the endless arena is cut into <see cref="ChunkM"/> chunks, each holding one template's cluster, chosen and placed
    /// from the run's seed and the chunk's coordinates alone. Chunks load as the hero nears them and unload once far,
    /// remembering what broke. Templates keep inside the chunk's <see cref="LaneM"/> margin, so open lanes run between
    /// every pair of chunks.
    /// </summary>
    public sealed class StructureRules
    {
        public const string File = "structures.json";
        /// <summary>A chunk remembers its structures' breaks in one 64-bit mask.</summary>
        public const int MaxPerChunk = 64;

        public double ChunkM = 24;
        /// <summary>
        /// A chunk loads once any of it is this close to the hero: beyond the camera's sight, even eased out for a duel, and
        /// beyond the 40 m round a duel that no new structure may enter (design.md, Bosses &gt; Every boss).
        /// </summary>
        public double LoadM = 48;
        /// <summary>And unloads once all of it is farther than this.</summary>
        public double UnloadM = 72;
        /// <summary>No structure within this of the hero's start.</summary>
        public double ClearStartM = 10;
        /// <summary>The band along each chunk edge no template's structure enters: the lanes between clusters.</summary>
        public double LaneM = 3;

        public StructureKind[] Kinds = new StructureKind[0];
        public StructureTemplate[] Templates = new StructureTemplate[0];

        public int IndexOf(string id) => Array.FindIndex(Kinds, k => k.Id == id);

        /// <summary>The largest footprint's bound: what a query adds to find every structure it may touch.</summary>
        public double MaxBound
        {
            get
            {
                double m = 0;
                foreach (var k in Kinds) m = Math.Max(m, k.Bound);
                return m;
            }
        }

        /// <summary>Reads structures.json's object.</summary>
        public static StructureRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new StructureRules
            {
                ChunkM = r.Number("chunkM"),
                LoadM = r.Number("loadM"),
                UnloadM = r.Number("unloadM"),
                ClearStartM = r.Number("clearStartM"),
                LaneM = r.Number("laneM"),
            };
            rules.Kinds = r.Objects("kinds", k =>
            {
                var kind = new StructureKind { Id = k.String("id"), Hits = k.Int("hits") };
                if (k.Has("radiusM"))
                {
                    kind.Shape = FootprintShape.Circle;
                    kind.Radius = k.Number("radiusM");
                }
                else
                {
                    kind.Shape = FootprintShape.Box;
                    var size = k.Numbers("sizeM");
                    if (size.Length != 2) throw ContentReader.Fail(k.Child("sizeM"), "expected [length, depth]");
                    kind.HalfX = size[0] / 2;
                    kind.HalfY = size[1] / 2;
                }
                k.End();
                if (kind.Hits < 1) throw ContentReader.Fail(k.Child("hits"), "at least 1");
                if (!(kind.Bound > 0)) throw ContentReader.Fail(k.Path, "a footprint with an area");
                return kind;
            });
            var kinds = rules.Kinds;
            rules.Templates = r.Objects("templates", t =>
            {
                var template = new StructureTemplate
                {
                    Id = t.String("id"),
                    Weight = t.Number("weight"),
                    JitterM = t.Number("jitterM"),
                    JitterDegrees = t.Number("jitterDegrees"),
                };
                template.Placements = t.Objects("placements", p =>
                {
                    string kind = p.String("kind");
                    int index = Array.FindIndex(kinds, k => k.Id == kind);
                    if (index < 0) throw ContentReader.Fail(p.Child("kind"), $"no kind {kind}");
                    var at = p.Numbers("at");
                    if (at.Length != 2) throw ContentReader.Fail(p.Child("at"), "expected [x, y]");
                    var placement = new Placement { Kind = index, X = at[0], Y = at[1], Degrees = p.NumberOr("degrees", 0) };
                    p.End();
                    return placement;
                });
                t.End();
                if (!(template.Weight >= 0)) throw ContentReader.Fail(t.Child("weight"), "at least 0");
                return template;
            });
            r.End();
            rules.Validate(path);
            return rules;
        }

        /// <summary>
        /// Checks the numbers: loading inside unloading, and every template's structures inside its chunk's lanes
        /// however it is turned and nudged (so the lanes hold by construction).
        /// </summary>
        void Validate(string path)
        {
            if (!(ChunkM > 0)) throw ContentReader.Fail(path + ".chunkM", "positive");
            if (!(LoadM > 0) || !(UnloadM > LoadM)) throw ContentReader.Fail(path + ".unloadM", "more than loadM, which is positive");
            if (!(LaneM >= 0) || !(2 * LaneM < ChunkM)) throw ContentReader.Fail(path + ".laneM", "at least 0 and under half the chunk");
            if (!(ClearStartM >= 0)) throw ContentReader.Fail(path + ".clearStartM", "at least 0");
            if (Templates.Length == 0) throw ContentReader.Fail(path + ".templates", "at least one");
            double total = 0;
            double inside = ChunkM / 2 - LaneM;
            for (int i = 0; i < Templates.Length; i++)
            {
                var t = Templates[i];
                total += t.Weight;
                if (t.Placements.Length > MaxPerChunk) throw ContentReader.Fail($"{path}.templates[{i}]", $"at most {MaxPerChunk} structures");
                for (int j = 0; j < t.Placements.Length; j++)
                {
                    var p = t.Placements[j];
                    double reach = Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) + t.JitterM + Kinds[p.Kind].Bound;
                    if (reach > inside + 1e-9)
                        throw ContentReader.Fail($"{path}.templates[{i}].placements[{j}]", $"reaches {reach:F2} m from the chunk's middle, past its lanes at {inside:F2} m");
                }
            }
            if (!(total > 0)) throw ContentReader.Fail(path + ".templates", "a positive total weight");
        }

        public StructureRules Clone()
        {
            var c = (StructureRules)MemberwiseClone();
            c.Kinds = (StructureKind[])Kinds.Clone();
            c.Templates = (StructureTemplate[])Templates.Clone();
            return c;
        }
    }
}
