using System.IO;
namespace Champions.Sim.Tests
{
    // Archived combat tests exercise their original encounters; current encounters have separate tests.
    internal static class LegacyEncounterContent
    {
        internal static GameContent Load()
        {
            var c = GameContent.Load(Checkout.In("Content"));
            string dir = Checkout.In("dotnet/Champions.Sim.Tests/Fixtures");
            c.Enemies = EnemyRules.Read(JsonValue.Parse(File.ReadAllText(Path.Combine(dir, "legacy-enemies.json"))), "legacy-enemies");
            c.Bosses = BossRules.Read(JsonValue.Parse(File.ReadAllText(Path.Combine(dir, "legacy-bosses.json"))), "legacy-bosses", c.Enemies);
            c.Level = LevelRules.Read(JsonValue.Parse(File.ReadAllText(Path.Combine(dir, "legacy-level1.json"))), "legacy-level1", c.Enemies);
            c.Progression.Levels = new[] { 20.0 };
            c.Progression.Growth = 1.09;
            return c;
        }
    }
}
