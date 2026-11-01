using System.Runtime.CompilerServices;

// The client's EditMode tests read GPU state the client keeps to itself (the shatter's shard records).
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
