using System;
using System.IO;
using PS.Choreography;

namespace Demo3D.Choreography
{
    /// <summary>
    /// Loads demo3d's choreography inputs from a checkout (the dotnet tests and the CLI). The Unity
    /// client reads the same JSON from TextAssets and the baked song map, and calls the string parsers.
    /// </summary>
    public static class ChoreographyFiles
    {
        public const string DataDir = "demo3d/Assets/Demo3D/Choreography/Data";
        public const string AnnotationsFile = "placeholder-annotations.json";
        public const string RosterFile = "roster.json";
        public const string LevelFile = "action-level.json";
        public const string FootprintsFile = "footprints.json";
        public const string DefaultLayout = "arena";

        /// <summary>The checkout's root: the first directory up from <paramref name="start"/> (or the
        /// working directory, then the assembly's) that holds songs/ and the layer's data.</summary>
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

        /// <summary>TD2's measured annotations (TD4 switched to them); the placeholders remain for tests.</summary>
        public const string MeasuredAnnotationsPath = "demo3d/Assets/Demo3D/Data/clip-annotations.json";

        /// <summary>The run's inputs on a layout: "arena" (the default: the arena's parameters, TD6), "courtyard-test", or "open-ground" (TD3's test layout).</summary>
        public static ChoreographyData LoadData(string root, string layout = DefaultLayout, bool placeholders = false)
        {
            string measured = Path.Combine(root, MeasuredAnnotationsPath);
            string annotations = !placeholders && File.Exists(measured) ? measured : DataPath(root, AnnotationsFile);
            return ChoreographyData.FromJson(
                File.ReadAllText(annotations),
                File.ReadAllText(DataPath(root, RosterFile)),
                File.ReadAllText(DataPath(root, layout + "-layout.json")),
                File.ReadAllText(DataPath(root, FootprintsFile)));
        }

        public static FootprintTable LoadFootprints(string root) => FootprintTable.Parse(File.ReadAllText(DataPath(root, FootprintsFile)));

        /// <summary>A layout's parameters (Data/&lt;layout&gt;-layout.json); <paramref name="density"/> overrides its decoration density.</summary>
        public static ArenaLayout LoadLayout(string root, string layout = DefaultLayout, float density = 0)
        {
            ArenaLayout l = ArenaLayout.Parse(File.ReadAllText(DataPath(root, layout + "-layout.json")));
            return density > 0 ? l.WithDensity(density) : l;
        }

        /// <summary>The placement list TD9's scene generator instantiates (the same one the choreographer navigates).</summary>
        public static PlacementList LoadPlacements(string root, string layout = DefaultLayout, float density = 0) =>
            LoadLayout(root, layout, density).Generate(LoadFootprints(root));

        public static SongMap LoadSong(string root, string song) =>
            SongMap.Parse(File.ReadAllText(Path.Combine(root, "songs", song, "songmap.json")));

        public static LevelScript LoadLevel(string root, string file = LevelFile) => LevelScript.Parse(File.ReadAllText(DataPath(root, file)));

        /// <summary>A stress scene's script, "S1" to "S6" (stress-s1.json and so on).</summary>
        public static StressScript LoadStress(string root, string scene) =>
            StressScript.Parse(File.ReadAllText(DataPath(root, "stress-" + scene.ToLowerInvariant() + ".json")));

        /// <summary>The song's timeline from the tracked level script.</summary>
        public static Timeline BuildSong(string root, uint? seed = null)
        {
            LevelScript level = LoadLevel(root);
            return Choreographer.Run(LoadSong(root, level.Song), level, LoadData(root, level.Layout ?? DefaultLayout), seed);
        }
    }
}
