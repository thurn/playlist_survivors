using System.Runtime.CompilerServices;

// The sim's tests reach its internals (the run's generator, the stepping machinery); the client
// cannot (unity-game.md, "The Sim's Contract").
[assembly: InternalsVisibleTo("PS.Game.Sim.Tests")]
