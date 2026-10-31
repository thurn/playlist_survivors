using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The hero's arsenal at the run's start (unity-game.md, The Other Weapons): its weapons and
    /// passives with their ranks, its base stats, and whether level-ups offer cards.
    /// </summary>
    public sealed class ArsenalConfig
    {
        /// <summary>Weapons in weapons.json and their ranks, in slot order; native's run starts with the sword at rank 1.</summary>
        public (string Id, int Rank)[] Weapons = { ("sword", 1) };
        /// <summary>Passives in passives.json and their ranks.</summary>
        public (string Id, int Rank)[] Passives = { };
        /// <summary>The hero's base stats before passives; null takes progression.json's.</summary>
        public Stats? Stats;
        /// <summary>A sword strike's wind-up before its impact (the Synty clip's); the client retimes it 0.75x to 1.33x.</summary>
        public double SwordWindupS = 0.32;
        /// <summary>
        /// Reaching a level opens its cards and freezes the world (the run); false only counts the
        /// levels, for tests of other systems that kill a lot.
        /// </summary>
        public bool Cards = true;

        /// <summary>Is the arsenal the run's own start: the sword at rank 1, no passives, the base stats?</summary>
        public bool StartsAsTheRun =>
            Weapons.Length == 1 && Weapons[0].Id == "sword" && Weapons[0].Rank == 1 && Passives.Length == 0 && Stats == null;
    }
}
