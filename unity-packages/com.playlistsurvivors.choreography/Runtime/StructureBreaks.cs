using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>
    /// A structure-breaks overlay (choreography.md, "Structure breaks"): `--structure-breaks <name>` merges
    /// Choreography/Data/structure-breaks-NAME.json over the level script, after any intensity overlay, in the
    /// same way (<see cref="IntensityOverlay"/>). It only schedules breaks, so its sections may set nothing but
    /// <c>breaks</c>:
    /// <code>
    /// { "name": "walls",
    ///   "sections": { "drop-2": { "breaks": ["courtyard/south-west@phrase", "courtyard/north-east@phrase/2"] } } }
    /// </code>
    /// Each entry is a target, <c>@</c> and a grid position, as combos and spells are; <c>/k</c> picks the
    /// position's k-th occurrence in the section (the first by default). A target is an area's name, for every
    /// piece the area placed, or the area's name, <c>/</c> and one part of it (<see cref="PartName"/>).
    /// </summary>
    public static class StructureBreaks
    {
        /// <summary>The overlay's JSON, checked to set nothing but its name, a note and its sections' breaks.</summary>
        public static IntensityOverlay Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root.Type != JsonType.Object) throw new FormatException("structure breaks: not an object");
            string name = root["name"].AsString() ?? "?";
            foreach (KeyValuePair<string, JsonValue> kv in root.Members)
                if (kv.Key != "name" && kv.Key != "note" && kv.Key != "sections")
                    throw new FormatException("structure breaks '" + name + "': may only schedule breaks, not set '" + kv.Key + "'");
            JsonValue sections = root["sections"];
            if (sections.Type != JsonType.Object) throw new FormatException("structure breaks '" + name + "': no sections");
            foreach (KeyValuePair<string, JsonValue> s in sections.Members)
            {
                if (s.Key == IntensityOverlay.AllSections) throw new FormatException("structure breaks '" + name + "': breaks name their section, not '*'");
                if (s.Value.Type != JsonType.Object || s.Value["breaks"].Type != JsonType.Array)
                    throw new FormatException("structure breaks '" + name + "': section '" + s.Key + "' needs a breaks list");
                foreach (KeyValuePair<string, JsonValue> f in s.Value.Members)
                    if (f.Key != "breaks") throw new FormatException("structure breaks '" + name + "': section '" + s.Key + "' may only set breaks, not '" + f.Key + "'");
                foreach (JsonValue e in s.Value["breaks"].Items)
                    if (e.AsString() == null || !GridEntry.Parse(e.AsString()).Positioned)
                        throw new FormatException("structure breaks '" + name + "': '" + e.AsString() + "' in '" + s.Key + "' needs a grid position (target@position)");
            }
            return IntensityOverlay.Parse(json);
        }

        static readonly string[] Sides = { "south", "east", "north", "west" };
        // Each courtyard side's halves, in the order its wall runs (counter-clockwise from the south-west corner).
        static readonly string[,] Halves = { { "west", "east" }, { "south", "north" }, { "east", "west" }, { "north", "south" } };

        /// <summary>
        /// A placement's part within its area: a courtyard's wall segments are its side and the half's end
        /// (<c>south-west</c>, <c>south-east</c>, <c>east-south</c> ... <c>west-south</c>, before the courtyard's
        /// yaw; a closed side is just <c>north</c>), a ruin wall's are numbered from its <c>from</c> end
        /// (<c>1</c>, <c>2</c> ...), and the area's own pieces are <c>piece-N</c>, in its pieces list's order.
        /// </summary>
        public static string PartName(LayoutArea area, Placement p, int pieceIndex)
        {
            if (p.Segment < 0) return "piece-" + (pieceIndex + 1);
            if (area.Kind == "courtyard")
            {
                int side = p.Segment / 2;
                bool split = area.Gaps == 4 || side != area.ClosedSide;
                return split ? Sides[side] + "-" + Halves[side, p.Segment % 2] : Sides[side];
            }
            return (p.Segment + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The placements a target names (indices into the layout's placement list), in list order. A target
        /// the layout lacks, or one with nothing that blocks, fails, listing what the area has.
        /// </summary>
        public static int[] Resolve(GroundLayout ground, string target)
        {
            if (ground.Arena == null) throw new FormatException("structure breaks: '" + target + "' needs an arena layout, not open ground '" + ground.Name + "'");
            int slash = target.IndexOf('/');
            string areaName = slash < 0 ? target : target.Substring(0, slash), part = slash < 0 ? null : target.Substring(slash + 1);
            int area = Array.FindIndex(ground.Arena.Areas, a => a.Name == areaName);
            if (area < 0) throw new FormatException("structure breaks: layout '" + ground.Name + "' has no area '" + areaName + "'");
            var found = new List<int>();
            var parts = new List<string>();
            bool blocks = false;
            PlacementList list = ground.Placements;
            int pieces = 0;
            for (int i = 0; i < list.Items.Length; i++)
            {
                Placement p = list.Items[i];
                if (p.Area != area) continue;
                string name = PartName(ground.Arena.Areas[area], p, p.Segment < 0 ? pieces++ : -1);
                if (!parts.Contains(name)) parts.Add(name);
                if (part != null && name != part) continue;
                found.Add(i);
                blocks |= list.Props[p.Prop].Blocks;
            }
            if (found.Count == 0 || !blocks)
                throw new FormatException("structure breaks: '" + target + "' " + (found.Count == 0 ? "names nothing" : "blocks nothing") + " in layout '" + ground.Name
                    + "' (" + (parts.Count == 0 ? "area '" + areaName + "' places no pieces" : "its parts: " + string.Join(", ", parts)) + ")");
            return found.ToArray();
        }
    }
}
