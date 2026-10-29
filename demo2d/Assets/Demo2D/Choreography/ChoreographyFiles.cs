using System;
using System.IO;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>
    /// Loads demo2d's choreography inputs from a checkout (the dotnet tests). The Unity client reads
    /// the same JSON from TextAssets and the baked song map, and calls the string parsers.
    /// </summary>
    public static class ChoreographyFiles
    {
        public const string DataDir = "demo2d/Assets/Demo2D/Data";
        public const string PlaceholderAnnotationsFile = "placeholder-annotations.json";
        /// <summary>PX3's measured annotations, used instead of the placeholders once they exist.</summary>
        public const string MeasuredAnnotationsFile = "clip-annotations.json";
        public const string RosterFile = "roster.json";
        public const string LevelFile = "action-level.json";
        /// <summary>PX6's 2D kit: the props' footprints and sprites.</summary>
        public const string PropsFile = "forest-props.json";
        public const string TilesFile = "forest-tiles.json";
        public const string ArenaLayout = "arena";

        /// <summary>The checkout's root: the first directory up from <paramref name="start"/> (or the
        /// working directory, then the assembly's) that holds songs/ and demo2d's data.</summary>
        public static string FindRepoRoot(string start = null)
        {
            foreach (string s in new[] { start, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (string.IsNullOrEmpty(s)) continue;
                var dir = new DirectoryInfo(s);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, DataDir, RosterFile)) && Directory.Exists(Path.Combine(dir.FullName, "songs")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("no checkout with " + DataDir + " above " + (start ?? Directory.GetCurrentDirectory()));
        }

        public static string DataPath(string root, string file) => Path.Combine(root, DataDir, file);

        /// <summary>The run's inputs on a layout (Data/&lt;layout&gt;-layout.json): PX6's arena, or open ground.</summary>
        public static ChoreographyData LoadData(string root, string layout, bool placeholders = false)
        {
            string measured = DataPath(root, MeasuredAnnotationsFile);
            string annotations = File.ReadAllText(!placeholders && File.Exists(measured) ? measured : DataPath(root, PlaceholderAnnotationsFile));
            string roster = File.ReadAllText(DataPath(root, RosterFile));
            string layoutJson = File.ReadAllText(DataPath(root, layout + "-layout.json"));
            return JsonValue.Parse(layoutJson).Has("areas")
                ? ForestArena.Parse(layoutJson, File.ReadAllText(DataPath(root, PropsFile))).Data(annotations, roster)
                : ChoreographyData.FromJson(annotations, roster, layoutJson);
        }

        /// <summary>The arena (Data/&lt;layout&gt;-layout.json with the 2D kit).</summary>
        public static ForestArena LoadArena(string root, string layout = ArenaLayout) =>
            ForestArena.Parse(File.ReadAllText(DataPath(root, layout + "-layout.json")), File.ReadAllText(DataPath(root, PropsFile)));

        public static TileSheet LoadTileSheet(string root) => TileSheet.Parse(File.ReadAllText(DataPath(root, TilesFile)));

        public static SongMap LoadSong(string root, string song) =>
            SongMap.Parse(File.ReadAllText(Path.Combine(root, "songs", song, "songmap.json")));

        public static LevelScript LoadLevel(string root, string file = LevelFile) => LevelScript.Parse(File.ReadAllText(DataPath(root, file)));

        /// <summary>The song's timeline from the tracked level script.</summary>
        public static Timeline BuildSong(string root, uint? seed = null)
        {
            LevelScript level = LoadLevel(root);
            return Choreographer.Run(LoadSong(root, level.Song), level, LoadData(root, level.Layout), seed);
        }
    }
}
