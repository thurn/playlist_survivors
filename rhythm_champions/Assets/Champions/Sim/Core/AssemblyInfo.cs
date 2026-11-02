using System.Runtime.CompilerServices;

// The sim's tests reach its internals (the run's generator, the systems' hooks); the client cannot.
// The Unity EditMode tests may, to stage sim state the client reacts to.
[assembly: InternalsVisibleTo("Champions.Sim.Tests")]
[assembly: InternalsVisibleTo("Champions.Tests")]
