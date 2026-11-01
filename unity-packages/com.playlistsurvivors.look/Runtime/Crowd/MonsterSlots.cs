using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// Which palette role colours each monster type (neon-look-style.md, Type to slot): the style guide's table,
    /// the same in both projects, whose crowd types share the roster's ids. Types sharing a slot are told apart by
    /// silhouette and size. A monster look that draws in the palette's hues (rather than the role's gem colour
    /// on each instance) takes a type's colour from here.
    /// </summary>
    public static class MonsterSlots
    {
        /// <summary>The boss's slot: <c>boss.body</c>, not one of the enemy hues.</summary>
        public const int Boss = -1;

        /// <summary>The type's index into the palette's <c>enemies</c>, or <see cref="Boss"/>.</summary>
        public static int Of(string typeId)
        {
            switch (typeId)
            {
                case "skeleton": case "slime": return 0; // key, light
                case "demon": case "bat": return 1;      // key, deep
                case "goblin": return 2;                 // accent
                case "mushroom": return 3;               // accent
                case "bigork": return 4;                 // counter (elite)
                case "troll": return 5;                  // counter (elite)
                case "dragon": case "evilgod": return Boss;
                default: return StableIndex(typeId);     // a type the table doesn't know yet: one of the fodder slots
            }
        }

        /// <summary>The type's colour in <paramref name="palette"/> (gamma, as the palette's roles are).</summary>
        public static Color Colour(Palette palette, string typeId)
        {
            int slot = Of(typeId);
            if (slot == Boss) return palette.BossBody;
            return palette.Enemies[slot % palette.Enemies.Length];
        }

        static int StableIndex(string id)
        {
            int h = 17;
            foreach (char c in id ?? "") h = unchecked(h * 31 + c);
            return (int)((uint)h % 4);
        }
    }
}
