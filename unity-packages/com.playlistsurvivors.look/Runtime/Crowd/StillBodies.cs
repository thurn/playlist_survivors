using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// The living monsters as last presented, for a still's view json: look.py measures swarm separation and monster
    /// form in tiles scaled to the bodies' height on screen (neon-look-style.md), since a game troll stands four
    /// times a crystal cut skeleton there. A stills writer sets <see cref="Collect"/>; each frame the crowd's
    /// presenter starts the list anew (<see cref="Begin"/>) and adds each living monster's feet, height and enemy
    /// slot (<see cref="MonsterSlots"/>: the palette hue look.py tells its clumps apart by).
    /// </summary>
    public static class StillBodies
    {
        /// <summary>Set while stills are kept: <see cref="Add"/> records nothing otherwise.</summary>
        public static bool Collect;

        static readonly List<(Vector4 body, int slot)> Bodies = new List<(Vector4, int)>(); // feet, height in metres
        static readonly List<float> Heights = new List<float>();
        static readonly SortedDictionary<int, List<float>> BySlot = new SortedDictionary<int, List<float>>();

        public static void Begin() => Bodies.Clear();

        public static void Add(Vector3 feet, float heightM, int slot)
        {
            if (Collect) Bodies.Add((new Vector4(feet.x, feet.y, feet.z, heightM), slot));
        }

        /// <summary>
        /// The view json's fields for the bodies whose middle lies in view of <paramref name="viewProj"/> (OpenGL
        /// clip space, as the view json's): `"bodyM"`, the height in metres of the body the median monster pixel
        /// belongs to (a median weighted by height squared, so a few trolls outweigh many mushrooms), and
        /// `"bodies"`, the same per enemy slot. Empty with no body in view; each field after a comma.
        /// </summary>
        public static string ViewJson(Matrix4x4 viewProj)
        {
            Heights.Clear();
            foreach (var list in BySlot.Values) list.Clear();
            foreach (var (b, slot) in Bodies)
            {
                var c = viewProj * new Vector4(b.x, b.y + b.w * 0.5f, b.z, 1);
                if (c.w <= 0 || Mathf.Abs(c.x) > c.w || Mathf.Abs(c.y) > c.w) continue;
                Heights.Add(b.w);
                if (slot < 0) continue; // the boss: no enemy hue
                if (!BySlot.TryGetValue(slot, out var heights)) BySlot[slot] = heights = new List<float>();
                heights.Add(b.w);
            }
            if (Heights.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, ", \"bodyM\": {0:R}, \"bodies\": {{", AreaMedian(Heights)));
            bool first = true;
            foreach (var kv in BySlot)
            {
                if (kv.Value.Count == 0) continue;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}\"{1}\": {2:R}", first ? "" : ", ", kv.Key, AreaMedian(kv.Value)));
                first = false;
            }
            return sb.Append('}').ToString();
        }

        /// <summary>The height at which bodies at least that tall hold half the heights' squares (sorts the list).</summary>
        static float AreaMedian(List<float> heights)
        {
            heights.Sort();
            float total = 0, sum = 0;
            foreach (var h in heights) total += h * h;
            foreach (var h in heights)
                if ((sum += h * h) >= 0.5f * total) return h;
            return heights[heights.Count - 1];
        }
    }
}
