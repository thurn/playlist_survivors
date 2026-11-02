using System;
using System.IO;

namespace Champions.Sim
{
    /// <summary>
    /// The game's content: rhythm_champions/Content/*.json, read strictly (<see cref="ContentReader"/>: a
    /// missing, unknown or mistyped field fails with its file and path). Units are metres and seconds.
    /// The systems' beads add their files here (bosses).
    /// </summary>
    public sealed class GameContent
    {
        public BeatRules Beat = new BeatRules();
        public HeroRules Hero = new HeroRules();
        public DashRules Dashes = new DashRules();
        public AttackRules Attacks = new AttackRules();
        public ModRules Mods = new ModRules();
        public EnemyRules Enemies = new EnemyRules();
        public LevelRules Level = new LevelRules();
        public ZoneRules Zones = new ZoneRules();
        public StructureRules Structures = new StructureRules();
        public BossRules Bosses = new BossRules();
        public ProgressionRules Progression = new ProgressionRules();

        /// <summary>Loads the content folder <paramref name="dir"/>.</summary>
        public static GameContent Load(string dir)
        {
            var content = new GameContent
            {
                Beat = BeatRules.Read(Parse(dir, BeatRules.File), BeatRules.File),
                Hero = HeroRules.Read(Parse(dir, HeroRules.File), HeroRules.File),
                Dashes = DashRules.Read(Parse(dir, DashRules.File), DashRules.File),
                Attacks = AttackRules.Read(Parse(dir, AttackRules.File), AttackRules.File),
                Mods = ModRules.Read(Parse(dir, ModRules.File), ModRules.File),
                Enemies = EnemyRules.Read(Parse(dir, EnemyRules.File), EnemyRules.File),
                Zones = ZoneRules.Read(Parse(dir, ZoneRules.File), ZoneRules.File),
                Structures = StructureRules.Read(Parse(dir, StructureRules.File), StructureRules.File),
                Progression = ProgressionRules.Read(Parse(dir, ProgressionRules.File), ProgressionRules.File),
            };
            content.Level = LevelRules.Read(Parse(dir, LevelRules.File), LevelRules.File, content.Enemies);
            content.Bosses = BossRules.Read(Parse(dir, BossRules.File), BossRules.File, content.Enemies);
            foreach (var b in content.Level.Bosses)
                if (content.Bosses.Of(b.Cast) == null)
                    throw new ContentException($"{LevelRules.File}.bosses: {content.Enemies.Cast[b.Cast].Id} has no duel in {BossRules.File}");
            return content;
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
