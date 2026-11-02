using System;

namespace Champions.Sim
{
    /// <summary>One structure a chunk's template places: its index in the template, kind, middle and angle (radians).</summary>
    public struct Placed
    {
        public int Index, Kind;
        public double X, Y, Angle;
    }

    /// <summary>
    /// The endless arena's chunks (plan.md, The Sim &gt; Systems): what chunk (cx, cy) holds is a function of the run's
    /// seed and its coordinates alone, never of the order the hero meets chunks in, so a run replays exactly and a
    /// chunk the hero comes back to stands as it did. A chunk draws its own generator from the seed and its
    /// coordinates, picks a template by weight, turns it a quarter turn and nudges each structure; structures within
    /// <see cref="StructureRules.ClearStartM"/> of the hero's start are left out.
    /// </summary>
    public static class StructureChunks
    {
        /// <summary>The chunk holding ground point (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public static void ChunkOf(StructureRules rules, double x, double y, out int cx, out int cy)
        {
            cx = (int)Math.Floor(x / rules.ChunkM);
            cy = (int)Math.Floor(y / rules.ChunkM);
        }

        /// <summary>A chunk's key in maps: both coordinates in one long.</summary>
        public static long Key(int cx, int cy) => (long)cx << 32 | (uint)cy;

        /// <summary>The seed of chunk (<paramref name="cx"/>, <paramref name="cy"/>)'s own generator.</summary>
        public static ulong Seed(ulong seed, int cx, int cy)
        {
            ulong h = Mix(seed ^ 0x5851F42D4C957F2DUL);
            h = Mix(h ^ (ulong)(uint)cx * 0x9E3779B97F4A7C15UL);
            return Mix(h ^ (ulong)(uint)cy * 0xC2B2AE3D27D4EB4FUL);
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with chunk (<paramref name="cx"/>, <paramref name="cy"/>)'s
        /// structures, drawing from <paramref name="random"/> reseeded for the chunk; returns its template's index.
        /// </summary>
        public static int Generate(StructureRules rules, ulong seed, int cx, int cy, SimRandom random, StructList<Placed> into)
        {
            into.Clear();
            random.Reseed(Seed(seed, cx, cy));
            int template = Pick(rules, random.NextDouble());
            var t = rules.Templates[template];
            int turn = random.Below(4);
            double mx = (cx + 0.5) * rules.ChunkM, my = (cy + 0.5) * rules.ChunkM;
            for (int i = 0; i < t.Placements.Length; i++)
            {
                var p = t.Placements[i];
                // Every draw happens whether or not the structure is left out, so the rest stand where they would.
                double jx = random.Range(-t.JitterM, t.JitterM), jy = random.Range(-t.JitterM, t.JitterM);
                double ja = random.Range(-t.JitterDegrees, t.JitterDegrees);
                Turn(p.X + jx, p.Y + jy, turn, out double ox, out double oy);
                double x = mx + ox, y = my + oy;
                var kind = rules.Kinds[p.Kind];
                if (Math.Sqrt(x * x + y * y) - kind.Bound < rules.ClearStartM) continue;
                into.Add(new Placed { Index = i, Kind = p.Kind, X = x, Y = y, Angle = (p.Degrees + 90 * turn + ja) * (Math.PI / 180) });
            }
            return template;
        }

        static int Pick(StructureRules rules, double u)
        {
            double total = 0;
            foreach (var t in rules.Templates) total += t.Weight;
            double at = u * total;
            for (int i = 0; i < rules.Templates.Length; i++)
            {
                at -= rules.Templates[i].Weight;
                if (at < 0) return i;
            }
            return rules.Templates.Length - 1;
        }

        /// <summary>(x, y) turned <paramref name="quarters"/> quarter turns anticlockwise.</summary>
        static void Turn(double x, double y, int quarters, out double ox, out double oy)
        {
            switch (quarters & 3)
            {
                case 1: ox = -y; oy = x; break;
                case 2: ox = -x; oy = -y; break;
                case 3: ox = y; oy = -x; break;
                default: ox = x; oy = y; break;
            }
        }
    }
}
