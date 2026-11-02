using System;
using System.IO;

namespace Champions.Sim
{
    /// <summary>
    /// The game's content: rhythm_champions/Content/*.json, read strictly (<see cref="ContentReader"/>: a
    /// missing, unknown or mistyped field fails with its file and path). Units are metres and seconds.
    /// The systems' beads add their files here (attacks, mods, enemies, bosses, level1, structures,
    /// progression).
    /// </summary>
    public sealed class GameContent
    {
        public BeatRules Beat = new BeatRules();
        public HeroRules Hero = new HeroRules();
        public DashRules Dashes = new DashRules();

        /// <summary>Loads the content folder <paramref name="dir"/>.</summary>
        public static GameContent Load(string dir)
        {
            return new GameContent
            {
                Beat = BeatRules.Read(Parse(dir, BeatRules.File), BeatRules.File),
                Hero = HeroRules.Read(Parse(dir, HeroRules.File), HeroRules.File),
                Dashes = DashRules.Read(Parse(dir, DashRules.File), DashRules.File),
            };
        }

        static JsonValue Parse(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ContentException(file + ": can't read " + path + ": " + e.Message, e);
            }
            try
            {
                return JsonValue.Parse(text);
            }
            catch (FormatException e)
            {
                throw new ContentException(file + ": " + e.Message, e);
            }
        }

        /// <summary>
        /// The content folder: rhythm_champions/Content, found from <paramref name="start"/> upward (a test's
        /// or tool's folder inside the checkout), or null.
        /// </summary>
        public static string Find(string start)
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                foreach (string candidate in new[] { Path.Combine(d.FullName, "Content"), Path.Combine(d.FullName, "rhythm_champions", "Content") })
                    if (File.Exists(Path.Combine(candidate, BeatRules.File))) return candidate;
            }
            return null;
        }
    }
}
