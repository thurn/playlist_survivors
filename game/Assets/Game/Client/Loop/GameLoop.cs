using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Game.Client.Arena;
using Game.Client.Crowd;
using Game.Client.Hero;
using Game.Client.Look;
using Game.Client.Music;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// The game's frame loop on the main thread (unity-game.md, Architecture): the music's
    /// entries and the keys into the sim, the sim stepped to the frontier, its events and state
    /// to the presenters at frameSong. Player flags:
    /// - `--file &lt;audio or .m3u&gt;`: plays it through the music plugin, which feeds the sim;
    ///   live, song time is the music's (<see cref="MusicTimes"/>), and the run ends with it;
    /// - `--fixed-time`: song time is the frame's, exactly n/60 at frame n (<see cref="FrameClock"/>),
    ///   so a rerun shows the same frames. With `--file`, each frame first waits until the
    ///   plugin's analysis covers its frontier, and entries are known exactly at stamp + B. The
    ///   output is silent, since the music doesn't play in time with these frames;
    /// - `--seed N`: the sim's seed;
    /// - `--events FILE`: a synthetic event feed beside the sim's (<see cref="ScriptedEvents"/>);
    /// - `--overlay`: starts with the debug overlay shown (F3 toggles it), stills included;
    /// - `--headless`: silences the output and paces frames at 60 per second. (Unity's player
    ///   also takes it as -nographics, so stills can't use it: they run under --fixed-time.)
    /// - `--loop-out FILE`: writes each frame's times and the sim's hash, as CSV;
    /// - `--no-arena`: the placeholder's plane instead of the baked arena (which a build has unless
    ///   its bake failed), `--palette NAME` the arena's zone palette;
    /// - the look's `--hero-path`, `--view-width M` and `--camera-out FILE` (<see cref="LookPresenter"/>);
    /// - the hero's `--hero-out FILE` (<see cref="HeroPresenter"/>), and `--no-hero` for the placeholder's capsule.
    /// It logs a summary when the run ends: the lead, frameSong's holds and the sim's timings.
    /// </summary>
    [DefaultExecutionOrder(-100)] // after the music's poll, before Boot keeps a still of the frame
    public sealed class GameLoop : MonoBehaviour
    {
        /// <summary>
        /// The plugin's stamp trails its analysis by M - A (ps_plugin.h), and the latest block's
        /// entries may not be delivered yet when its position is reported (4096 samples at 44.1 kHz).
        /// </summary>
        const double StampLag = MusicPlayer.M - 0.048, BlockLag = 0.1;
        const double AnalysisTimeout = 10;
        const int LeadCapacity = 1 << 17;

        MusicPlayer _music;
        MusicTimes _musicTimes;
        KeyInput _keys;
        DebugOverlay _overlay;
        LookPresenter _look;
        HeroPresenter _hero;
        ArenaRuntime _arena;
        CrystalStructures _crystal;
        CrowdView _crowd;
        StreamWriter _out;
        bool _fixed, _done;
        int _entries, _keyChanges;
        readonly double[] _leads = new double[LeadCapacity];
        readonly double[] _steps = new double[LeadCapacity];

        public SimDriver Driver { get; private set; }

        /// <summary>Starts the loop on `host`'s scene with the command line's flags.</summary>
        public static GameLoop Begin(GameObject host, Camera camera)
        {
            var loop = host.AddComponent<GameLoop>();
            loop.Setup(camera);
            return loop;
        }

        void Setup(Camera camera)
        {
            _fixed = Args.Has("--fixed-time");
            bool headless = Args.Has("--headless");
            if (headless && !_fixed) Application.targetFrameRate = Boot.FixedFps;
            var listenerHost = camera != null ? camera.gameObject : gameObject;
            if (listenerHost.GetComponent<AudioListener>() == null) listenerHost.AddComponent<AudioListener>();
            if (headless || _fixed)
            {
                var mute = listenerHost.AddComponent<MixCapture>();
                mute.Mute = true;
                mute.Begin(0);
            }

            var file = Args.Get("--file");
            IGameClock clock = new FrameClock();
            if (file != null)
            {
                var musicHost = new GameObject("Music");
                musicHost.AddComponent<AudioSource>();
                _music = musicHost.AddComponent<MusicPlayer>();
                _music.Play(Args.Path(file));
                if (!_fixed)
                {
                    clock = _musicTimes = new MusicTimes(_music.Clock, MusicPlayer.Refresh,
                        MusicPlayer.ScheduleHeadroom, () => AudioSettings.dspTime);
                }
            }
            var config = new SimConfig { Seed = (ulong)Math.Max(0, Args.GetInt("--seed", 1)) };
            Driver = new SimDriver(config, clock);
            var events = Args.Get("--events");
            if (events != null) Driver.Scripted = ScriptedEvents.Load(Args.Path(events));
            _keys = new KeyInput();

            bool arena = !Args.Has("--no-arena") && ArenaBake.Load() != null;
            var heroBake = Args.Has("--no-hero") ? null : HeroBake.Load();
            Driver.Events.Subscribe(new PlaceholderView(camera, ground: !arena, hero: heroBake == null));
            if (heroBake != null)
            {
                _hero = new HeroPresenter(heroBake);
                Driver.Events.Subscribe(_hero);
            }
            _look = new LookPresenter(camera, Driver.Sim);
            if (_hero != null) _look.DrawnHero = _hero.Drawn; // the hero presents first, then the camera follows it
            Driver.Events.Subscribe(_look);
            if (arena) LoadArena(camera);
            // After the look, whose camera rig it culls with.
            _crowd = new CrowdView(camera);
            Driver.Events.Subscribe(_crowd);
            _overlay = new DebugOverlay(camera, Driver, Args.Has("--overlay"));
            Driver.Events.Subscribe(_overlay);

            var outPath = Args.Get("--loop-out");
            if (outPath != null)
            {
                _out = new StreamWriter(Args.Path(outPath));
                _out.WriteLine("frame,frame_song,frontier,lead,tick,hash,events,hero_x,hero_y");
            }
            Debug.Log($"GameLoop: {(file != null ? file : "no music")}, {(_fixed ? "fixed time" : "real time")}, " +
                      $"seed {config.Seed}, events {(events ?? "none")}, headless {headless}");
        }

        /// <summary>The baked arena, its structures in crystal; the look drives its night volume and hero light.</summary>
        void LoadArena(Camera camera)
        {
            ArenaClock.Deterministic = _fixed;
            _arena = ArenaRuntime.Load(camera, Args.Get("--palette", ArenaLighting.DefaultPalette));
            _arena.OnReady += a =>
            {
                _crystal = new CrystalStructures();
                _crystal.Apply(a, camera);
                _look.Volume = a.Post;
                _look.FollowHero = a.FollowHero;
            };
        }

        void Update()
        {
            if (_done) return;
            if (_music != null)
            {
                if (_music.Failed) { Finish(1, _music.Error); return; }
                if (_fixed) { if (!FeedSettled()) return; }
                else FeedLive();
            }
            // Before the music's anchor no key has a song time: they wait for it.
            if (Driver.Clock.Ready) _keyChanges += _keys.Drain(Driver.Sim, Driver.Clock);
            _musicTimes?.Measure(Time.unscaledDeltaTime);
            var now = new ClockReadings
            {
                Real = Time.realtimeSinceStartupAsDouble,
                Frame = Time.timeSinceLevelLoadAsDouble,
            };
            if (Driver.Step(now)) Keep();
            _overlay.Toggle();
            if (_music != null && !_fixed && _music.Ended) Finish(0, null);
        }

        void FeedLive()
        {
            for (int i = 0; i < _music.EntryCount; i++) Driver.Add(_music.Entries[i]);
            _entries += _music.EntryCount;
        }

        /// <summary>
        /// Feeds entries known exactly at stamp + B, polling the plugin until its analysis covers
        /// this frame's frontier (or is done), so the frame never depends on the plugin's timing.
        /// False if the run failed.
        /// </summary>
        bool FeedSettled()
        {
            double needed = Driver.StampNeededFor(FrameClock.FrontierAt(Time.timeSinceLevelLoadAsDouble));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                for (int i = 0; i < _music.EntryCount; i++) Driver.AddSettled(_music.Entries[i]);
                _entries += _music.EntryCount;
                if (_music.Failed) { Finish(1, _music.Error); return false; }
                // A full poll may have left entries behind; the status read before it vouches for the rest.
                bool drained = _music.EntryCount < MusicPlayer.MaxEntries;
                if (drained && (_music.Status.AnalysisDone != 0 || _music.Status.Analysed - BlockLag - StampLag >= needed))
                    return true;
                if (Time.realtimeSinceStartupAsDouble - started > AnalysisTimeout)
                {
                    Finish(1, $"the plugin's analysis stayed at {_music.Status.Analysed:F3} s for {AnalysisTimeout} s");
                    return false;
                }
                if (drained) Thread.Sleep(1);
                _music.Poll();
            }
        }

        void Keep()
        {
            int frame = Driver.Frames - 1;
            if (frame < LeadCapacity)
            {
                _leads[frame] = Driver.Times.Lead;
                _steps[frame] = Driver.StepMs;
            }
            if (_out == null) return;
            var t = Driver.Times;
            var hero = Driver.Presented.State.Hero;
            hero.PositionAt(Driver.Presented.Song, out double x, out double y);
            _out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4},{5:X16},{6},{7:R},{8:R}",
                frame, t.FrameSong, t.Frontier, t.Lead, Driver.Sim.State.Tick, Driver.Sim.Hash, Driver.Sim.EventCount, x, y));
        }

        void OnApplicationQuit() => Finish(0, null);

        void OnDestroy()
        {
            _keys?.Dispose();
            _look?.Dispose();
            _hero?.Dispose();
            _crystal?.Dispose();
            _arena?.Dispose();
            _crowd?.Dispose();
            _out?.Dispose();
            _out = null;
        }

        /// <summary>Logs the run's summary and quits with `code`.</summary>
        void Finish(int code, string error)
        {
            if (_done) return;
            _done = true;
            Debug.Log("GameLoop: " + (error ?? "done") + "; " + Summary());
            _out?.Dispose();
            _out = null;
            if (_music != null) _music.Close();
            if (error != null) Debug.LogError("GameLoop: " + error);
            Application.Quit(code);
        }

        string Summary()
        {
            int n = Math.Min(Driver.Frames, LeadCapacity);
            var c = CultureInfo.InvariantCulture;
            var s = string.Format(c, "{0} frames to song {1:F3} s, {2} entries ({3} late), {4} key changes ({5} dropped), {6} events, hash {7:X16}",
                Driver.Frames, Driver.Times.FrameSong, _entries, Driver.LateEntries, _keyChanges, _keys.Dropped,
                Driver.Sim.EventCount, Driver.Sim.Hash);
            if (n == 0) return s;
            // The first second settles the DSP fit and the frame interval; the lead is measured after it.
            int skip = Math.Min(n - 1, Boot.FixedFps);
            var leads = new double[n - skip];
            Array.Copy(_leads, skip, leads, 0, leads.Length);
            Array.Sort(leads);
            var steps = new double[n];
            Array.Copy(_steps, steps, n);
            Array.Sort(steps);
            s += string.Format(c, "; lead (ms) median {0:F1}, p5..p95 {1:F1}..{2:F1}, min..max {3:F1}..{4:F1}",
                1000 * Pct(leads, 0.5), 1000 * Pct(leads, 0.05), 1000 * Pct(leads, 0.95), 1000 * leads[0], 1000 * leads[leads.Length - 1]);
            if (_musicTimes != null)
                s += string.Format(c, " (output latency {0:F1} + margin {1:F1})", 1000 * _music.Clock.OutputLatency, 1000 * _musicTimes.Margin);
            s += string.Format(c, "; frameSong backwards {0}, held {1} frames (largest {2:F2} ms); frames at the frontier {3}",
                Driver.BackwardFrames, _music != null ? _music.Clock.HeldFrames : 0,
                _music != null ? 1000 * _music.Clock.LargestHold : 0, Driver.FramesAtFrontier);
            s += string.Format(c, "; sim step (ms) median {0:F3}, p99 {1:F3}, max {2:F3}",
                Pct(steps, 0.5), Pct(steps, 0.99), steps[n - 1]);
            return s;
        }

        static double Pct(double[] sorted, double p) => sorted[(int)Math.Round(p * (sorted.Length - 1))];
    }
}
