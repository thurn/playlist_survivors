using System;

namespace PS.Choreography
{
    /// <summary>
    /// The effect and sound ids a timeline's events use. The timeline also carries these names,
    /// so the client maps them to its prefabs and clips by name.
    /// </summary>
    public static class Catalog
    {
        public static readonly string[] Effects =
        {
            "hit-spark", "death-burst", "parry-spark", "slash-trail", "thrust-trail", "spin-trail",
            "slam-shock", "ring", "bolt-launch", "bolt-impact", "lightning", "laser",
        };

        public const int HitSpark = 0, DeathBurst = 1, ParrySpark = 2, BoltLaunch = 8, BoltImpact = 9;

        /// <summary>Sound categories; each has <see cref="Variants"/> variants, chosen by seed.</summary>
        public static readonly string[] SoundCategories = { "swing", "hit", "death", "parry", "ring", "bolt", "lightning", "laser" };

        public const int Variants = 3;
        public const int SwingSound = 0, HitSound = 1, DeathSound = 2, ParrySound = 3;

        public static string[] SoundNames()
        {
            var names = new string[SoundCategories.Length * Variants];
            for (int c = 0; c < SoundCategories.Length; c++)
                for (int v = 0; v < Variants; v++) names[c * Variants + v] = SoundCategories[c] + "/" + v;
            return names;
        }

        public static int Effect(string name) => name == null ? -1 : Index(Effects, name, "effect");
        public static int Sound(string name) => name == null ? -1 : Index(SoundCategories, name, "sound category");

        static int Index(string[] names, string name, string what)
        {
            int i = Array.IndexOf(names, name);
            if (i < 0) throw new FormatException("unknown " + what + " '" + name + "'");
            return i;
        }
    }
}
