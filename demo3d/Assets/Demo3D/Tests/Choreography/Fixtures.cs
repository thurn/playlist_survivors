using System;
using System.Collections.Generic;
using System.Diagnostics;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// Shared inputs and timelines for the layer's tests, built once per test run. The tests run
    /// under `dotnet test` (demo3d/dotnet/) and in Unity's EditMode runner, which both start in a
    /// directory inside the checkout.
    /// </summary>
    public static class Fixtures
    {
        static string root;
        static ChoreographyData data;
        static LevelScript level;
        static SongMap song;
        static Timeline songTimeline;
        static LevelScript overdriveLevel;
        static Timeline overdriveTimeline, multiHitTimeline;
        static readonly Dictionary<string, Timeline> stress = new Dictionary<string, Timeline>();

        public static readonly string[] StressScenes = { "S1", "S2", "S3", "S4", "S5", "S6" };
        static ChoreographyData courtyardData;
        static Timeline courtyardSparse, courtyardDense;

        /// <summary>Unity's editor runs Mono, which is slower than .NET; timing bounds loosen there.</summary>
        public static bool OnMono => Type.GetType("Mono.Runtime") != null;

        public static string Root => root ?? (root = ChoreographyFiles.FindRepoRoot());
        public static ChoreographyData Data => data ?? (data = ChoreographyFiles.LoadData(Root, Level.Layout));
        public static LevelScript Level => level ?? (level = ChoreographyFiles.LoadLevel(Root));
        public static SongMap Song => song ?? (song = ChoreographyFiles.LoadSong(Root, Level.Song));

        public static Timeline SongTimeline
        {
            get
            {
                if (songTimeline == null) songTimeline = BuildSong();
                return songTimeline;
            }
        }

        public static Timeline BuildSong() => Choreographer.Run(Song, Level, Data);

        /// <summary>
        /// The song's timeline without one-hit kills (action-level.json under an overlay turning <c>oneHitKills</c> off):
        /// the kill budget decides who dies and the laser ticks, for the checks on non-lethal hits.
        /// </summary>
        public static Timeline MultiHitTimeline => multiHitTimeline ?? (multiHitTimeline = Choreographer.Run(Song,
            LevelScript.Parse(System.IO.File.ReadAllText(ChoreographyFiles.DataPath(Root, ChoreographyFiles.LevelFile)), "{ \"oneHitKills\": false }"), Data));

        /// <summary>The level script under `--intensity overdrive` (intensity-overdrive.json over action-level.json).</summary>
        public static LevelScript OverdriveLevel => overdriveLevel ?? (overdriveLevel = ChoreographyFiles.LoadLevel(Root, ChoreographyFiles.LevelFile, "overdrive"));

        /// <summary>The song's timeline under `--intensity overdrive`.</summary>
        public static Timeline OverdriveTimeline => overdriveTimeline ?? (overdriveTimeline = Choreographer.Run(Song, OverdriveLevel, Data));

        public static StressScript StressScript(string scene) => ChoreographyFiles.LoadStress(Root, scene);

        public static Timeline Stress(string scene)
        {
            if (!stress.TryGetValue(scene, out Timeline tl))
            {
                tl = Choreographer.RunStress(StressScript(scene), Data);
                stress[scene] = tl;
            }
            return tl;
        }

        /// <summary>The courtyard test layout (TD6's navigation tests).</summary>
        public static ChoreographyData CourtyardData => courtyardData ?? (courtyardData = ChoreographyFiles.LoadData(Root, "courtyard-test"));

        /// <summary>
        /// A sparse run on the courtyard test layout: 80 monsters starting 22 m out (behind the courtyard's
        /// closed side, inside the U, beyond the long wall), a wide view so all of them start on screen,
        /// no reserve, and the hero holding the courtyard without attacking, so nobody dies.
        /// </summary>
        public static Timeline CourtyardSparse => courtyardSparse ?? (courtyardSparse = Choreographer.RunStress(PS.Choreography.StressScript.Parse(
            "{ \"scene\": \"court-sparse\", \"layout\": \"courtyard-test\", \"hold\": \"court\", \"bpm\": 140, \"seconds\": 45, \"seed\": 2, \"reserve\": 0,"
            + " \"viewWidthM\": 70, \"n\": 80, \"openingInnerM\": 22, \"load\": \"none\", \"heroAttacks\": false,"
            + " \"mix\": { \"skeleton\": 3, \"goblin\": 2, \"bat\": 1, \"demon\": 1, \"bigork\": 0.5, \"troll\": 0.5 } }"), CourtyardData));

        /// <summary>A dense run on the courtyard test layout: 500 on screen pressing in on a hero who never attacks, so every gap jams.</summary>
        public static Timeline CourtyardDense => courtyardDense ?? (courtyardDense = Choreographer.RunStress(PS.Choreography.StressScript.Parse(
            "{ \"scene\": \"court-dense\", \"layout\": \"courtyard-test\", \"hold\": \"court\", \"bpm\": 140, \"seconds\": 30, \"seed\": 3, \"reserve\": 0.5,"
            + " \"viewWidthM\": 40, \"n\": 500, \"openingInnerM\": 9, \"load\": \"none\", \"heroAttacks\": false,"
            + " \"mix\": { \"skeleton\": 3, \"goblin\": 3, \"slime\": 2, \"bat\": 2, \"demon\": 1, \"bigork\": 0.3 } }"), CourtyardData));

        /// <summary>The timeline a test case names: "song", "song-overdrive" or a stress scene.</summary>
        public static Timeline Named(string name) => name == "song" ? SongTimeline : name == "song-overdrive" ? OverdriveTimeline : Stress(name);

        public static IEnumerable<string> AllTimelines()
        {
            yield return "song";
            yield return "song-overdrive";
            foreach (string s in StressScenes) yield return s;
        }

        public static double Seconds(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            return sw.Elapsed.TotalSeconds;
        }
    }
}
