namespace PS.Game.Sim
{
    /// <summary>
    /// The hero's movement rules, as the mode picks them (<see cref="HeroMovement"/>): the grid's <see cref="Hero"/>
    /// or the <see cref="FreeHero"/>. The sim hands it every key change, pointer target and stride step, and its
    /// per-tick turn; it keeps <see cref="HeroState"/> and emits the hero's moves.
    /// </summary>
    internal interface IHeroControl
    {
        /// <summary>A key goes down, heard at <paramref name="heard"/>; one the level-up's cards took (<paramref name="forCards"/>) is not held.</summary>
        void Press(Simulation sim, Key key, double heard, bool forCards);

        void Release(Simulation sim, Key key, double heard);

        /// <summary>A pointer asks the hero to walk to (<paramref name="x"/>, <paramref name="y"/>), heard at <paramref name="heard"/>.</summary>
        void Target(Simulation sim, double x, double y, double heard);

        /// <summary>Lets go of every held key (and target): only presses after this move the hero.</summary>
        void LetGo();

        /// <summary>Stride step <paramref name="index"/> at <paramref name="t"/>, <paramref name="period"/> before the next.</summary>
        void Step(Simulation sim, int index, double t, double period);

        /// <summary>The per-tick turn, at the tick's end <paramref name="now"/>.</summary>
        void Tick(Simulation sim, double now);
    }
}
