using System.Runtime.CompilerServices;

// The sim's tests reach its internals (the run's generator, the stepping machinery); the client
// cannot (unity-game.md, "The Sim's Contract"). The client's EditMode tests may, to stage sim state
// the client reacts to (an open level-up offer for KeyInput's card taps).
[assembly: InternalsVisibleTo("PS.Game.Sim.Tests")]
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
