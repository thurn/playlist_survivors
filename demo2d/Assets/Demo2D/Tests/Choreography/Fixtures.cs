using System;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>
    /// demo2d's inputs and the song's timeline, built once per test run. The tests run under
    /// `dotnet test` (demo2d/dotnet/) and in Unity's EditMode runner, which both start in a
    /// directory inside the checkout.
    /// </summary>
    public static class Fixtures
    {
        static string root;
        static ChoreographyData data;
        static LevelScript level;
        static SongMap song;
        static Timeline songTimeline;
        static ForestArena arena;

        /// <summary>Unity's editor runs Mono, which is slower than .NET; timing bounds loosen there.</summary>
        public static bool OnMono => Type.GetType("Mono.Runtime") != null;

        public static string Root => root ?? (root = ChoreographyFiles.FindRepoRoot());
        public static ChoreographyData Data => data ?? (data = ChoreographyFiles.LoadData(Root, Level.Layout));
        public static LevelScript Level => level ?? (level = ChoreographyFiles.LoadLevel(Root));
        /// <summary>PX6's arena, the level script's layout.</summary>
        public static ForestArena Arena => arena ?? (arena = ChoreographyFiles.LoadArena(Root, Level.Layout));
        public static SongMap Song => song ?? (song = ChoreographyFiles.LoadSong(Root, Level.Song));

        public static Timeline SongTimeline => songTimeline ?? (songTimeline = BuildSong());

        public static Timeline BuildSong() => Choreographer.Run(Song, Level, Data);
    }
}
