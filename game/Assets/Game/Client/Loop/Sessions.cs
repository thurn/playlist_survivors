using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// Where runs are recorded (unity-game.md, Sessions and Replay): one folder per run, named by its
    /// start time, in <see cref="Root"/>, the newest <see cref="Kept"/> kept, as native keeps its
    /// own. The plugin writes its audio and entries there, and the run its <see cref="PS.Game.Sim.SessionRecord"/>.
    /// A windowed run is recorded in the player's data folder; a windowless one (batch mode,
    /// --headless or --fixed-time: checks and evidence) only when it names a folder with
    /// `--sessions DIR`, or with `--watch`, which replays it after. `--no-session` records nothing
    /// (<see cref="LaunchOptions.Recording"/>), nor does a run a replay couldn't rebuild (<see cref="PS.Game.Sim.RunInputs.Unrecordable"/>).
    /// </summary>
    public static class Sessions
    {
        public const int Kept = 20;

        /// <summary>A session folder's name, its start time (and a count for two in one second): only these are ever removed.</summary>
        static readonly Regex Name = new Regex(@"^\d{8}-\d{6}(-\d+)?$");

        /// <summary>The run recorded last in this launch, for Watch my run; null before one is.</summary>
        public static string Last;

        /// <summary>A session to replay when the boot scene next starts (--watch's), consumed by Boot.</summary>
        public static string WatchNext;

        /// <summary>The sessions' folder: `--sessions DIR`, else sessions/ in the player's data folder.</summary>
        public static string Root => LaunchOptions.Current.SessionsDir ?? Path.Combine(Application.persistentDataPath, "sessions");

        /// <summary>
        /// A new run's folder (created), after removing the oldest sessions so that <see cref="Kept"/>
        /// remain with it. Nothing but a session's folder is removed, wherever `--sessions` points.
        /// </summary>
        public static string Begin()
        {
            string root = Root;
            Directory.CreateDirectory(root);
            var old = Directory.GetDirectories(root)
                .Where(d => Name.IsMatch(Path.GetFileName(d)) && File.Exists(Path.Combine(d, "audio.wav")))
                .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
            for (int i = 0; i < old.Count - (Kept - 1); i++)
            {
                try { Directory.Delete(old[i], recursive: true); }
                catch (Exception e) { Debug.LogWarning($"Sessions: can't remove {old[i]}: {e.Message}"); }
            }
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string dir = Path.Combine(root, name);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, $"{name}-{n}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
