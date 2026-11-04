using System;
using System.IO;

namespace Champions.Sim
{
    /// <summary>
    /// A Boss Rush act checkpoint (boss-rush.md, Decisions 2): the boss a run (re)starts at, and the build, level, XP and
    /// pending choices it starts with; plus the run's totals so far, which the sim never reads, so a victory card can show
    /// the whole run's time and deaths per act after retries and quitting. The sim takes one at its start
    /// (<see cref="SimConfig.BossRushStart"/>) and makes the next as each act begins (<see cref="BossRush.Checkpoint"/>);
    /// the client keeps the latest on disk (<see cref="Write"/>, <see cref="Read"/>).
    /// </summary>
    public sealed class BossRushCheckpoint
    {
        const int Magic = 0x48535242; // "BRSH"
        const int Version = 1;

        /// <summary>The boss to start at, 0-based (an act's first, or `--boss N`'s).</summary>
        public int Boss;
        /// <summary>The weapons and supports owned, as cards at their levels (Sword 1 is added if missing).</summary>
        public Card[] Build = new Card[0];
        public int Level = 1;
        public double Experience;
        /// <summary>Levels reached and not yet chosen: their offers open as the run starts.</summary>
        public int HeldLevels;
        /// <summary>Max-health fallbacks taken (a capped build's level-ups).</summary>
        public int Fallbacks;

        /// <summary>The run's combat time before this attempt (s), across acts, deaths and quits.</summary>
        public double TimeBefore;
        /// <summary>Deaths so far, per act.</summary>
        public int[] Deaths = new int[0];

        public BossRushCheckpoint Clone()
        {
            var c = (BossRushCheckpoint)MemberwiseClone();
            c.Build = (Card[])Build.Clone();
            c.Deaths = (int[])Deaths.Clone();
            return c;
        }

        /// <summary>The weapons, then the supports, of <paramref name="build"/>, as cards at their levels.</summary>
        public static Card[] CardsOf(WeaponBuild build)
        {
            var cards = new System.Collections.Generic.List<Card>();
            for (int i = 0; i < CombatRules.WeaponCount; i++)
                if (build.Level((WeaponKind)i) > 0) cards.Add(Card.Of((WeaponKind)i, build.Level((WeaponKind)i), 0));
            for (int i = 0; i < CombatRules.SupportCount; i++)
                if (build.Level((SupportKind)i) > 0) cards.Add(Card.Of((SupportKind)i, build.Level((SupportKind)i), 0));
            return cards.ToArray();
        }

        /// <summary>The upgrade steps the build holds (Sword 1 is the start, not a step).</summary>
        public int Steps
        {
            get
            {
                int n = -1;
                foreach (var c in Build) n += c.Rank;
                return Math.Max(0, n);
            }
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(Boss);
            w.Write(Build.Length);
            foreach (var c in Build) w.Write(c.Code);
            w.Write(Level);
            w.Write(Experience);
            w.Write(HeldLevels);
            w.Write(Fallbacks);
            w.Write(TimeBefore);
            w.Write(Deaths.Length);
            foreach (int d in Deaths) w.Write(d);
        }

        /// <summary>Reads what <see cref="Write"/> wrote; a bad or foreign record fails with <see cref="FormatException"/>.</summary>
        public static BossRushCheckpoint Read(BinaryReader r, string path)
        {
            try
            {
                if (r.ReadInt32() != Magic) throw new FormatException($"{path}: not a Boss Rush checkpoint");
                int version = r.ReadInt32();
                if (version != Version) throw new FormatException($"{path}: checkpoint version {version}, not {Version}");
                var c = new BossRushCheckpoint { Boss = r.ReadInt32() };
                c.Build = new Card[Count(r, path, "build", 64)];
                for (int i = 0; i < c.Build.Length; i++)
                {
                    int code = r.ReadInt32();
                    var card = Card.FromCode(code);
                    if (code != card.Code || card.From != 0 || card.Rank < 1 ||
                        !(card.Kind == CardKind.Weapon && card.Id < CombatRules.WeaponCount && card.Rank <= CombatRules.MaxWeaponLevel ||
                          card.Kind == CardKind.Support && card.Id < CombatRules.SupportCount && card.Rank <= 5))
                        throw new FormatException($"{path}: invalid build card {i}");
                    c.Build[i] = card;
                }
                c.Level = r.ReadInt32();
                c.Experience = r.ReadDouble();
                c.HeldLevels = r.ReadInt32();
                c.Fallbacks = r.ReadInt32();
                c.TimeBefore = r.ReadDouble();
                c.Deaths = new int[Count(r, path, "deaths", 64)];
                for (int i = 0; i < c.Deaths.Length; i++) c.Deaths[i] = r.ReadInt32();
                if (c.Boss < 0 || c.Level < 1 || c.HeldLevels < 0 || c.Fallbacks < 0 || !(c.Experience >= 0) || !(c.TimeBefore >= 0))
                    throw new FormatException($"{path}: checkpoint values out of range");
                return c;
            }
            catch (EndOfStreamException e)
            {
                throw new FormatException($"{path}: checkpoint cut short", e);
            }
        }

        /// <summary>Saves to <paramref name="path"/> through a temporary file, so a crash never leaves half a checkpoint.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(temp))) Write(w);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>The checkpoint saved at <paramref name="path"/>, or null when there is none or it can't be read.</summary>
        public static BossRushCheckpoint Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) return null;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path))) return Read(r, path);
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is UnauthorizedAccessException)
            {
                error = e.Message;
                return null;
            }
        }

        static int Count(BinaryReader r, string path, string what, int most)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > most) throw new FormatException($"{path}: {n} {what}");
            return n;
        }
    }
}
