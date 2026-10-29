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

        /// <summary>The run's inputs on a layout (Data/&lt;layout&gt;-layout.json; "open-ground" until PX6's arena).</summary>
        public static ChoreographyData LoadData(string root, string layout, bool placeholders = false)
        {
            string measured = DataPath(root, MeasuredAnnotationsFile);
            string annotations = !placeholders && File.Exists(measured) ? measured : DataPath(root, PlaceholderAnnotationsFile);
            return ChoreographyData.FromJson(
                File.ReadAllText(annotations),
                File.ReadAllText(DataPath(root, RosterFile)),
                File.ReadAllText(DataPath(root, layout + "-layout.json")));
        }

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
