using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Render;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The player's command line, parsed once (<see cref="Current"/>). Unity's and the OS's flags have one
    /// hyphen (-batchmode, -screen-width 1920, -logFile F) and are left to them; the game's have two.
    /// </summary>
    public sealed class LaunchOptions
    {
        static LaunchOptions _current;

        /// <summary>This launch's options, parsed from its command line on first use.</summary>
        public static LaunchOptions Current => _current ??= Parse(Environment.GetCommandLineArgs(), Application.isBatchMode);

        /// <summary>
        /// Windowless (`--headless`, or Unity's batch mode): the output silenced, frames paced at 60 per second. Unity's
        /// player reads `--headless` too and draws with the Null device, so windowless stills run in batch mode without
        /// it (`champions play --headless` does that).
        /// </summary>
        public bool Headless;
        /// <summary>`--fixed-time`: time advances exactly 1/60 s per frame, however long a frame takes.</summary>
        public bool FixedTime;
        /// <summary>`--seconds N`: quit after N seconds of game time; 0 runs until quit.</summary>
        public double Seconds;
        /// <summary>`--capture-stills DIR`: windowless stills into DIR at the `--stills-at` times (seconds), or of the
        /// `--stills-scenes` scenes (names, or `all`; Scenes.StillScenes).</summary>
        public string StillsDir;
        public double[] StillsAt = Array.Empty<double>();
        public string[] StillsScenes = Array.Empty<string>();
        /// <summary>`--file PATH`: plays an audio file or M3U playlist (absolute, or relative to the working directory).</summary>
        public string File;
        /// <summary>`--app NAME`: Delayed listening to the app producing audio that NAME names (a pid, or its name or part of it).</summary>
        public string App;
        /// <summary>`--edge-crowd N`: the edge crowd's test scene with N alive (<see cref="Bench.EdgeCrowdBench"/>), of
        /// `--edge-type ID` (default skeleton), its timings to `--bench-out PATH`.</summary>
        public int EdgeCrowd;
        public string EdgeType;
        public string BenchOut;
        /// <summary>`--gallery`: the weapon effects' gallery (Scenes.GalleryScene): in a window, every effect in turn, over and
        /// over; with `--capture-stills DIR`, its stills (as `--stills-scenes gallery`), then quit.</summary>
        public bool Gallery;
        /// <summary>`--tier low|medium|high`: the quality tier (<see cref="QualityTiers"/>); null keeps the pause menu's choice or the GPU's.</summary>
        public Tier? Tier;
        /// <summary>A note for each argument the game read none of: an unknown flag, a missing value, a stray argument.</summary>
        public string[] Unrecognized;

        public static LaunchOptions Parse(string[] args, bool batchMode)
        {
            var a = new Reader(args ?? Array.Empty<string>());
            var o = new LaunchOptions
            {
                Headless = a.Has("--headless") || batchMode,
                FixedTime = a.Has("--fixed-time"),
                Seconds = a.Double("--seconds", 0),
                StillsDir = a.Get("--capture-stills"),
                File = a.Get("--file"),
                App = a.Get("--app"),
                EdgeCrowd = (int)a.Double("--edge-crowd", 0),
                Gallery = a.Has("--gallery"),
                EdgeType = a.Get("--edge-type"),
                BenchOut = a.Get("--bench-out"),
            };
            var tier = a.Get("--tier");
            if (tier != null)
                o.Tier = QualityTiers.TryParse(tier, out var t) ? t : throw new ArgumentException($"--tier: {tier} is not low, medium or high");
            if (o.File != null && o.App != null) throw new ArgumentException("--file and --app: give one music source");
            var at = a.Get("--stills-at");
            if (at != null) o.StillsAt = Array.ConvertAll(at.Split(','), s => Number("--stills-at", s));
            var scenes = a.Get("--stills-scenes");
            if (scenes != null) o.StillsScenes = scenes.Split(',');
            if (o.Gallery && o.StillsDir != null && Array.IndexOf(o.StillsScenes, "gallery") < 0)
                o.StillsScenes = new List<string>(o.StillsScenes) { "gallery" }.ToArray();
            if (o.StillsScenes.Length > 0 && o.StillsAt.Length > 0) throw new ArgumentException("--stills-at and --stills-scenes: give one");
            if (o.StillsScenes.Length > 0 && o.StillsDir == null) throw new ArgumentException("--stills-scenes: needs --capture-stills DIR");
            o.Unrecognized = a.Unrecognized(); // last: every flag has been read
            return o;
        }

        static double Number(string flag, string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : throw new ArgumentException($"{flag}: {s} is not a number");

        sealed class Reader
        {
            readonly string[] _args;
            readonly bool[] _claimed;
            readonly List<string> _notes = new List<string>();

            public Reader(string[] args)
            {
                _args = args;
                _claimed = new bool[args.Length];
                if (args.Length > 0) _claimed[0] = true; // the executable
            }

            public bool Has(string name)
            {
                bool found = false;
                for (int i = 1; i < _args.Length; i++)
                    if (_args[i] == name) _claimed[i] = found = true;
                return found;
            }

            public string Get(string name)
            {
                string value = null;
                for (int i = 1; i < _args.Length; i++)
                {
                    if (_args[i] != name) continue;
                    _claimed[i] = true;
                    if (i + 1 < _args.Length)
                    {
                        _claimed[i + 1] = true;
                        value ??= _args[i + 1];
                    }
                    else _notes.Add($"{name}: needs a value after it");
                }
                return value;
            }

            public double Double(string name, double fallback)
            {
                var s = Get(name);
                return s == null ? fallback : Number(name, s);
            }

            public string[] Unrecognized()
            {
                var notes = new List<string>(_notes);
                for (int i = 1; i < _args.Length; i++)
                {
                    if (_claimed[i]) continue;
                    string arg = _args[i];
                    if (arg.StartsWith("--", StringComparison.Ordinal)) notes.Add(arg + ": not a flag the game knows");
                    else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1 && !char.IsDigit(arg[1]))
                    {
                        // Unity's own flag; skip its value too.
                        if (i + 1 < _args.Length && !_args[i + 1].StartsWith("-", StringComparison.Ordinal)) i++;
                    }
                    else notes.Add(arg + ": no flag takes this argument");
                }
                return notes.ToArray();
            }
        }
    }
}
