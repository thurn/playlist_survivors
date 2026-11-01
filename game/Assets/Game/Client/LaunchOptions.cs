using System;
using System.Globalization;
using Game.Client.Arena;
using PS.Game.Sim;
using PS.Game.Sim.Bots;
using UnityEngine;

namespace Game.Client
{
    /// <summary>How a launch runs: in a window with a player at the keys, or windowless on real or frame time.</summary>
    public enum LaunchMode
    {
        /// <summary>A window and a player: the title screen, a run that starts on a key, sessions recorded.</summary>
        Windowed,
        /// <summary>Windowless on real time (`--headless`, or Unity's batch mode): the output silenced, frames paced at 60 per second.</summary>
        Headless,
        /// <summary>`--fixed-time`: song time is the frame's, exactly n/60 at frame n, windowless or not.</summary>
        FixedTime,
    }

    /// <summary>
    /// The player's command line, parsed once (<see cref="Current"/>): every flag the client reads (Boot's and
    /// <see cref="Loop.GameLoop"/>'s lists say what each does), paths made absolute, and the questions several places
    /// ask (is there a player, is the run recorded, does the title screen open) answered once here. The run's own
    /// inputs are <see cref="Run"/>, the value its config is built from and its session records.
    /// </summary>
    public sealed class LaunchOptions
    {
        static LaunchOptions _current;

        /// <summary>This launch's options, parsed from its command line on first use.</summary>
        public static LaunchOptions Current => _current ??= Parse(Environment.GetCommandLineArgs(), Application.isBatchMode);

        /// <summary>The flags as given, for a session's run.json.</summary>
        public string[] Flags;
        public LaunchMode Mode;
        public bool Windowed => Mode == LaunchMode.Windowed;
        public bool Headless => Mode == LaunchMode.Headless;
        public bool FixedTime => Mode == LaunchMode.FixedTime;

        /// <summary>The run's inputs from the flags; GameLoop puts them on the build (<see cref="RunInputs.On"/>).</summary>
        public RunInputs Run;
        /// <summary>`--level PATH`: level files in place of the bake's (<see cref="Loop.LevelFiles"/>).</summary>
        public string LevelPath;
        /// <summary>`--events FILE`: a synthetic event feed in place of the horde and sword.</summary>
        public string Events;
        public bool NoArena, NoHero, Overlay, SoundProbe, HeroPath;
        public string Palette = ArenaLighting.DefaultPalette, ChestPreview;
        /// <summary>`--view-width M`; 0 when not given.</summary>
        public float ViewWidth;

        // The music: one of these, or the title screen's pick.
        public string File, App, Replay, TitlePick, MusicDir;
        /// <summary>`--shuffle`: each File run plays its playlist's tracks in a new random order (<see cref="Music.MusicPlayer.Play"/>).</summary>
        public bool Shuffle;
        /// <summary>Whether the launch opens on the title screen: a window and no source, or `--title`.</summary>
        public bool Title;
        /// <summary>Whether the run has music (a source, or the title screen's pick): a run without starts at once.</summary>
        public bool Music => File != null || App != null || Replay != null || Title;

        // Sessions.
        public string SessionsDir;
        public bool Watch;
        /// <summary>Whether this launch records its runs: with a window, or `--sessions` or `--watch`; never with `--no-session`.</summary>
        public bool Recording;

        // Boot's other launches and its frame.
        public float Seconds;
        public int DspBuffer;
        public string StillsDir, StillsAt = "0";
        public bool StillsMask;
        public bool Spike;
        public string SpikeOut;
        public float ClickLead = -1;
        public bool ArenaStage;
        public string ArenaAt;
        public float ArenaTour;
        public string ShatterBench;
        /// <summary>`--gpu-probe-at t1,t2,...`: song times for the windowless frame probe (<see cref="GpuProbe"/>).</summary>
        public string GpuProbeAt;
        /// <summary>`--gpu-probe-ablate`: the probe also times its frames with each pass turned off (<see cref="GpuProbe.Ablations"/>).</summary>
        public bool GpuProbeAblate;
        /// <summary>`--gpu-probe-without PASS,...`: the probe's frames all drawn without those passes.</summary>
        public string GpuProbeWithout;
        /// <summary>`--gpu-probe-firsts`: the frame probe also at each kind of moment's first sighting (<see cref="GpuProbe.Firsts"/>).</summary>
        public bool GpuProbeFirsts;
        /// <summary>`--no-warmup`: the build's traced graphics states aren't warmed at boot (<see cref="GraphicsStates"/>).</summary>
        public bool NoWarmup;

        // Calibration.
        public string Calibrate, CalibrationFile, TapAlong;

        public OutputFiles Outputs = new OutputFiles();
        public ScriptedInput Scripted = new ScriptedInput();

        /// <summary>The CSVs and folders a run writes for evidence.</summary>
        public sealed class OutputFiles
        {
            public string Loop, Events, Structures, Shatter, Sound, Camera, Hero, GpuProbe, Profile, Moments, GraphicsStates;
        }

        /// <summary>What plays in place of a player: debug commands, held keys, the bot and a scripted pause.</summary>
        public sealed class ScriptedInput
        {
            public string Debug, Walk;
            public BotKind? Bot;
            /// <summary>`--pause-at S`; negative when not given.</summary>
            public double PauseAt = -1;
        }

        /// <summary>
        /// Parses <paramref name="args"/> as <see cref="Environment.GetCommandLineArgs"/> gives them, the executable first
        /// (Unity's own flags among them, ignored), in batch mode if <paramref name="batchMode"/>.
        /// </summary>
        public static LaunchOptions Parse(string[] args, bool batchMode)
        {
            args ??= new string[0];
            var a = new Reader(args);
            var o = new LaunchOptions
            {
                Flags = Rest(args),
                Mode = a.Has("--fixed-time") ? LaunchMode.FixedTime
                    : a.Has("--headless") || batchMode ? LaunchMode.Headless : LaunchMode.Windowed,
                LevelPath = a.Path("--level"),
                Events = a.Path("--events"),
                NoArena = a.Has("--no-arena"),
                NoHero = a.Has("--no-hero"),
                Overlay = a.Has("--overlay"),
                SoundProbe = a.Has("--sound-probe"),
                HeroPath = a.Has("--hero-path"),
                Palette = a.Get("--palette") ?? ArenaLighting.DefaultPalette,
                ChestPreview = a.Get("--chest-preview"),
                ViewWidth = a.Float("--view-width", 0),
                File = a.Path("--file"),
                App = a.Get("--app"),
                Replay = a.Path("--replay"),
                TitlePick = a.Get("--title-pick"),
                MusicDir = a.Path("--music-dir"),
                Shuffle = a.Has("--shuffle"),
                SessionsDir = a.Path("--sessions"),
                Watch = a.Has("--watch"),
                Seconds = a.Float("--seconds", 0),
                DspBuffer = a.Int("--dsp-buffer", 0),
                StillsDir = a.Get("--capture-stills"),
                StillsAt = a.Get("--stills-at") ?? "0",
                StillsMask = a.Has("--capture-mask"),
                Spike = a.Has("--spike") || a.Has("--spike-out"),
                SpikeOut = a.Path("--spike-out"),
                ClickLead = a.Float("--click-lead", -1),
                ArenaStage = a.Has("--arena"),
                ArenaAt = a.Get("--arena-at"),
                ArenaTour = a.Float("--arena-tour", 0),
                ShatterBench = a.Path("--shatter-bench"),
                GpuProbeAt = a.Get("--gpu-probe-at"),
                GpuProbeAblate = a.Has("--gpu-probe-ablate"),
                GpuProbeWithout = a.Get("--gpu-probe-without"),
                GpuProbeFirsts = a.Has("--gpu-probe-firsts"),
                NoWarmup = a.Has("--no-warmup"),
                Calibrate = a.Get("--calibrate"),
                CalibrationFile = a.Path("--calibration"),
                TapAlong = a.Get("--tap-along"),
                Outputs =
                {
                    Loop = a.Path("--loop-out"), Events = a.Path("--events-out"), Structures = a.Path("--structures-out"), Shatter = a.Path("--shatter-out"),
                    Sound = a.Path("--sound-out"), Camera = a.Path("--camera-out"), Hero = a.Path("--hero-out"),
                    GpuProbe = a.Path("--gpu-probe-out"), Profile = a.Path("--profile-out"),
                    Moments = a.Path("--moments-out"), GraphicsStates = a.Path("--graphics-states-out"),
                },
                Scripted = { Debug = a.Get("--debug"), Walk = a.Get("--walk"), Bot = Bot(a.Get("--bot")), PauseAt = a.Float("--pause-at", -1) },
            };
            o.Title = o.File == null && o.App == null && o.Replay == null && (a.Has("--title") || o.Windowed && o.Events == null);
            o.Recording = !a.Has("--no-session") && (o.SessionsDir != null || o.Watch || o.Windowed);
            o.Run = RunOf(a, o);
            if (o.Watch && o.Run.Unrecordable != null)
                throw new ArgumentException($"--watch: a run on {o.Run.Unrecordable} isn't recorded, so there's nothing to watch");
            return o;
        }

        /// <summary>
        /// The run's inputs from the flags: the level (`--events`' open ground, `--cast`, the run's with `--run`,
        /// `--level` or `--start-minute`, else the slice's), the seed, the start (with a window on a key, without one
        /// after clear beat, and at once without music), Easy, and a player's loadout.
        /// </summary>
        static RunInputs RunOf(Reader a, LaunchOptions o)
        {
            int startMinute = a.Int("--start-minute", 0);
            if (startMinute < 0) throw new ArgumentException("--start-minute: a minute of the run, from 0");
            var cast = a.Get("--cast");
            var arsenal = a.Get("--arsenal");
            var level = o.Events != null ? SessionLevel.Open
                : cast != null ? SessionLevel.Cast
                : a.Has("--run") || o.LevelPath != null || startMinute > 0 ? SessionLevel.Run
                : SessionLevel.Slice;
            return new RunInputs
            {
                Seed = (ulong)Math.Max(0, a.Int("--seed", 1)),
                Level = level,
                Start = !o.Music ? RunStart.Immediately : o.Windowed ? RunStart.OnKey : RunStart.AfterClearBeat,
                StartMinute = level == SessionLevel.Run ? startMinute : 0,
                Easy = a.Has("--easy"),
                Cast = level == SessionLevel.Cast ? cast.Split(',') : null,
                Loadout = arsenal != null ? Loadout(arsenal) : null,
                NoCards = a.Has("--no-cards"),
                LevelFiles = o.LevelPath != null,
            };
        }

        /// <summary>`--arsenal`'s weapons: `id:rank`, comma-separated, in slot order.</summary>
        static (string Id, int Rank)[] Loadout(string list) => Array.ConvertAll(list.Split(','), item =>
        {
            var parts = item.Trim().Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank) || rank < 1)
                throw new ArgumentException($"--arsenal: '{item}' is not weapon:rank or passive:rank");
            return (parts[0].Trim(), rank);
        });

        static string[] Rest(string[] args)
        {
            var rest = new string[Math.Max(0, args.Length - 1)];
            Array.Copy(args, args.Length - rest.Length, rest, 0, rest.Length);
            return rest;
        }

        static BotKind? Bot(string name)
        {
            if (name == null) return null;
            if (name == "positioning") return BotKind.Positioning;
            if (name == "idle") return BotKind.Idle;
            throw new ArgumentException($"--bot: '{name}' is not positioning or idle");
        }

        /// <summary>
        /// `path` made absolute against the directory the player was launched from: a Mac player
        /// starts in its app's folder, but the shell's PWD survives.
        /// </summary>
        public static string FullPath(string path)
        {
            if (System.IO.Path.IsPathRooted(path)) return path;
            var pwd = Environment.GetEnvironmentVariable("PWD");
            return System.IO.Path.GetFullPath(pwd != null ? System.IO.Path.Combine(pwd, path) : path);
        }

        /// <summary>`--name value` and `--flag` arguments.</summary>
        readonly struct Reader
        {
            readonly string[] _args;

            public Reader(string[] args) => _args = args;

            public bool Has(string name) => Array.IndexOf(_args, name) >= 0;

            public string Get(string name)
            {
                for (int i = 0; i < _args.Length - 1; i++)
                    if (_args[i] == name) return _args[i + 1];
                return null;
            }

            public string Path(string name)
            {
                var value = Get(name);
                return value != null ? FullPath(value) : null;
            }

            public float Float(string name, float fallback) =>
                float.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

            public int Int(string name, int fallback) =>
                int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
