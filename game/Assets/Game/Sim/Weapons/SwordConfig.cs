using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The hero's sword (unity-game.md, The Sword): its weapon, rank and the stats it reads. The
    /// slice owns it at a fixed rank; M3's arsenal ranks it up and changes the stats.
    /// </summary>
    public sealed class SwordConfig
    {
        public GameContent Content;
        /// <summary>The weapon in weapons.json: a sword-shaped one.</summary>
        public string Weapon = "sword";
        public int Rank = 1;
        /// <summary>The hero's Might, Area, Amount and Cooldown; null takes progression.json's base stats.</summary>
        public Stats? Stats;
        /// <summary>A strike's wind-up before its impact (the Synty clip's); the client retimes it 0.75x to 1.33x.</summary>
        public double WindupS = 0.32;
    }
}
