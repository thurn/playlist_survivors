using System;
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

        /// <summary>The bot `--bot` names, seeded as the sweeps seed it (the run's seed), for the run's content; null without the flag.</summary>
        public static BotInput FromArgs(ulong seed, GameContent content)
        {
            var name = Args.Get("--bot");
            if (name == null) return null;
            if (name == "positioning") return new BotInput(BotKind.Positioning, seed, content);
            if (name == "idle") return new BotInput(BotKind.Idle, seed, content);
            throw new ArgumentException($"--bot: '{name}' is not positioning or idle");
        }

        /// <summary>This frame's keys, before the sim steps.</summary>
        public void Act(Simulation sim) => _bot.Act(sim);

        public void OnEvent(in SimEvent e) => _bot.Observe(e);

        public void Present(in PresentFrame frame) { }
    }
}
