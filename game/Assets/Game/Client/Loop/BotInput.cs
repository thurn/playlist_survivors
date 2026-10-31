using PS.Game.Sim;
using PS.Game.Sim.Bots;
using PS.Game.Sim.Content;

namespace Game.Client.Loop
{
    /// <summary>
    /// `--bot positioning|idle`: the sweeps' <see cref="SurvivorBot"/> plays the run in place of the
    /// keyboard (unity-game.md, Tools, Checks and Evidence), as <see cref="BotRun"/> drives it: its keys
    /// at the sim's time before each frame's step, the events it sees as they settle, like any
    /// presenter's. It picks its own cards. Its keys are the sim's like a player's, so the run's
    /// session replays them.
    /// </summary>
    public sealed class BotInput : IPresenter
    {
        readonly SurvivorBot _bot;

        BotInput(BotKind kind, ulong seed, GameContent content) => _bot = new SurvivorBot(kind, seed, content);

        /// <summary>The bot `--bot` names, seeded as the sweeps seed it (the run's seed), for the run's content; null without one.</summary>
        public static BotInput For(BotKind? kind, ulong seed, GameContent content) =>
            kind != null ? new BotInput(kind.Value, seed, content) : null;

        /// <summary>This frame's keys, before the sim steps.</summary>
        public void Act(Simulation sim) => _bot.Act(sim);

        public void OnEvent(in SimEvent e) => _bot.Observe(e);

        public void Present(in PresentFrame frame) { }
    }
}
