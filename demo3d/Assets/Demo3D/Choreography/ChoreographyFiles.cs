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

        /// <summary>An environment's whole layout (ps-bop): Data/environment-&lt;name&gt;.json resolved over the layout it extends.</summary>
        public static string EnvironmentLayout(string root, string environment) =>
            LayoutEnvironment.Resolve(File.ReadAllText(DataPath(root, "environment-" + environment + ".json")),
                layout => File.Exists(DataPath(root, layout + "-layout.json")) ? File.ReadAllText(DataPath(root, layout + "-layout.json")) : null, environment);

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

        /// <summary>The intensity that applies no overlay (`--intensity normal`, the default).</summary>
        public const string NormalIntensity = "normal";

        /// <summary>An intensity's overlay file (intensity-NAME.json beside the level scripts).</summary>
        public static string IntensityFile(string intensity) => "intensity-" + intensity + ".json";

        /// <summary>Every intensity the checkout has: normal and one per overlay file.</summary>
        public static string[] Intensities(string root)
        {
            string[] files = Directory.GetFiles(Path.Combine(root, DataDir), IntensityFile("*"));
            var names = new string[files.Length + 1];
            names[0] = NormalIntensity;
            for (int i = 0; i < files.Length; i++)
            {
                string f = Path.GetFileNameWithoutExtension(files[i]);
                names[i + 1] = f.Substring("intensity-".Length);
            }
            Array.Sort(names, 1, files.Length, StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// A level script, with an intensity's overlay merged over it (<see cref="IntensityOverlay"/>; null or
        /// normal: none). An unknown intensity fails, listing the known ones.
        /// </summary>
        public static LevelScript LoadLevel(string root, string file = LevelFile, string intensity = null)
        {
            string json = File.ReadAllText(DataPath(root, file));
            if (intensity == null || intensity == NormalIntensity) return LevelScript.Parse(json);
            string overlay = DataPath(root, IntensityFile(intensity));
            if (!File.Exists(overlay))
                throw new ArgumentException("--intensity: unknown intensity '" + intensity + "' (one of " + string.Join(", ", Intensities(root)) + ")");
            return LevelScript.Parse(json, File.ReadAllText(overlay));
        }

        /// <summary>The `--roster` sets (RosterSet.Names): each plays its own level script.</summary>
        public static readonly string[] RosterSets = { "default", "forest", "heroic" };

        /// <summary>A `--roster` set's level script: action-level.json, or its variant (action-level-NAME.json).</summary>
        public static string RosterLevelFile(string roster) =>
            roster == null || roster == RosterSets[0] ? LevelFile
            : Array.IndexOf(RosterSets, roster) >= 0 ? "action-level-" + roster + ".json"
            : throw new ArgumentException("--roster: unknown set '" + roster + "' (one of " + string.Join(", ", RosterSets) + ")");

        /// <summary>The forest, the arena's own layout, which the other environments extend.</summary>
        public const string ForestEnvironment = "forest";
        /// <summary>The decoration density every baked environment shares (trailer.md, "Environments in one player").</summary>
        public const float TrailerDensity = 2;

        /// <summary>Every environment the checkout has: the forest, then one per environment-NAME.json.</summary>
        public static string[] Environments(string root)
        {
            string[] files = Directory.GetFiles(Path.Combine(root, DataDir), "environment-*.json");
            var names = new string[files.Length + 1];
            names[0] = ForestEnvironment;
            for (int i = 0; i < files.Length; i++) names[i + 1] = Path.GetFileNameWithoutExtension(files[i]).Substring("environment-".Length);
            Array.Sort(names, 1, files.Length, StringComparer.Ordinal);
            return names;
        }

        /// <summary>An environment's run inputs at <paramref name="density"/>, so monsters walk round its own structures; null for an unknown one.</summary>
        public static ChoreographyData LoadEnvironmentData(string root, string environment, float density = TrailerDensity)
        {
            if (Array.IndexOf(Environments(root), environment) < 0) return null;
            string layout = environment == ForestEnvironment ? File.ReadAllText(DataPath(root, DefaultLayout + "-layout.json")) : EnvironmentLayout(root, environment);
            string measured = Path.Combine(root, MeasuredAnnotationsPath);
            return ChoreographyData.FromJson(File.ReadAllText(File.Exists(measured) ? measured : DataPath(root, AnnotationsFile)),
                File.ReadAllText(DataPath(root, RosterFile)), layout, File.ReadAllText(DataPath(root, FootprintsFile)), density);
        }

        /// <summary>A trailer's file (trailer-NAME.json beside the level scripts).</summary>
        public static string TrailerFile(string name) => "trailer-" + name + ".json";

        /// <summary>
        /// A trailer (trailer.md, "The trailer file"), validated against its song and the checkout's environments, and
        /// each shot's environment data (by environment). <paramref name="pinEnvironment"/> plays every shot in one.
        /// </summary>
        public static Trailer LoadTrailer(string root, string name, out System.Collections.Generic.Dictionary<string, ChoreographyData> environments, string pinEnvironment = null)
        {
            string path = DataPath(root, TrailerFile(name));
            if (!File.Exists(path)) throw new ArgumentException("--trailer: unknown trailer '" + name + "' (no " + TrailerFile(name) + ")");
            string json = File.ReadAllText(path);
            var data = new System.Collections.Generic.Dictionary<string, ChoreographyData>();
            ChoreographyData Env(string env)
            {
                if (!data.TryGetValue(env, out ChoreographyData d)) data[env] = d = LoadEnvironmentData(root, env);
                return d;
            }
            Trailer trailer = Trailer.Parse(json, LoadSong(root, JsonValue.Parse(json)["song"].AsString()), env => Env(env)?.Layout, pinEnvironment);
            environments = data;
            return trailer;
        }

        /// <summary>A trailer shot's timeline for a `--roster` set (trailer.md, "Shot simulations").</summary>
        public static Timeline BuildShot(string root, Trailer trailer, int index, ChoreographyData data, string roster = null)
        {
            LevelScript script = trailer.ShotScript(index, File.ReadAllText(DataPath(root, RosterLevelFile(roster))), File.ReadAllText(DataPath(root, IntensityFile(trailer.Base))));
            return Choreographer.Run(LoadSong(root, trailer.Song), script, data);
        }

        /// <summary>A stress scene's script, "S1" to "S6" (stress-s1.json and so on).</summary>
        public static StressScript LoadStress(string root, string scene) =>
            StressScript.Parse(File.ReadAllText(DataPath(root, "stress-" + scene.ToLowerInvariant() + ".json")));

        /// <summary>The song's timeline from a tracked level script (by default action-level.json), at an intensity.</summary>
        public static Timeline BuildSong(string root, uint? seed = null, string file = LevelFile, string intensity = null)
        {
            LevelScript level = LoadLevel(root, file, intensity);
            return Choreographer.Run(LoadSong(root, level.Song), level, LoadData(root, level.Layout ?? DefaultLayout), seed);
        }
    }
}
