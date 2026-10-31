namespace PS.Game.Sim
{
    /// <summary>
    /// Confirms level-up cards for a run nobody plays (headless runs, stills, tests): <see cref="HoldS"/>
    /// after an open offer's third card has flipped in, so a watcher sees all three, it presses and
    /// releases Enter, recorded like any key, so the selected card is taken and a replay takes it too.
    /// </summary>
    public static class AutoCards
    {
        public const double HoldS = 0.5;

        /// <summary>Confirms the open offer's selected card if it can be now; returns whether it pressed.</summary>
        public static bool Confirm(Simulation sim)
        {
            var offer = sim.State.Offer;
            if (!offer.Open || sim.Now < offer.Reveal2 + HoldS - 1e-9) return false;
            sim.Press(Key.Enter, sim.Now);
            sim.Release(Key.Enter, sim.Now);
            return true;
        }
    }
}
