using System.IO;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>Sims with enemies: the game's content, the default open arena and the slice's schedule unless a test says otherwise.</summary>
    public static class HordeFixture
    {
        static GameContent content;

        public static GameContent Content =>
            content ??= GameContent.Load(Path.GetDirectoryName(Checkout.File("game", "Content", "enemies.json")));

        /// <summary>The game's arena, game/Content/arena.json: 160 x 160 tiles with 38 crystal structures.</summary>
        public static ArenaData GameArena() => ArenaData.Parse(File.ReadAllText(Checkout.File("game", "Content", "arena.json")), "arena.json");

        /// <summary>A sim's config; <paramref name="sword"/> gives the hero its sword at that rank (0: none).</summary>
        public static SimConfig Config(int minimum = 120, ArenaData arena = null, string[] roster = null, ulong seed = 1,
            int sword = 0, bool heroStands = false)
        {
            var horde = new HordeConfig { Content = Content, Minimum = minimum };
            if (roster != null) horde.Roster = roster;
            return new SimConfig
            {
                Seed = seed, Arena = arena, Horde = horde, HeroStands = heroStands,
                Sword = sword > 0 ? new SwordConfig { Content = Content, Rank = sword } : null,
            };
        }

        /// <summary>A sim that spawns nothing by itself, for tests that place their own enemies.</summary>
        public static Simulation Empty(ArenaData arena = null, int sword = 0, bool heroStands = false) =>
            new Simulation(Config(minimum: 0, arena: arena, roster: new string[0], sword: sword, heroStands: heroStands));

        /// <summary>Places an enemy of role <paramref name="role"/> at (x, y) now.</summary>
        public static int Place(Simulation sim, string role, double x, double y)
        {
            int before = sim.EventCount;
            sim.Horde.Spawn(sim, sim.Now, Content.RoleIndex(role), x, y);
            return sim.EventsSince(before)[0].AsSpawn.Enemy;
        }

        /// <summary>Stands the hero at (x, y) at once, as if it had arrived there, for tests of what enemies do around it.</summary>
        public static void PutHero(Simulation sim, double x, double y)
        {
            var hero = sim.State.Hero;
            hero.X = hero.FromX = x;
            hero.Y = hero.FromY = y;
            hero.MoveStart = hero.MoveEnd = double.NegativeInfinity;
        }
    }
}
