using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Game.Client.Arena;
using Game.Client.Crowd;
using Game.Client.Effects;
using Game.Client.Hero;
using Game.Client.Look;
using Game.Client.Music;
using Game.Client.Sound;
using Game.Client.Structures;
using PS.Game.Sim;
using PS.Game.Sim.Level;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// The game's frame loop on the main thread (unity-game.md, Architecture): the music's
    /// entries and the keys into the sim, the sim stepped to the frontier, its events and state
    /// to the presenters at frameSong. The sim plays the slice's level (<see cref="SliceLevel"/>:
    /// the arena's level data, its horde and the sword) from the build's <see cref="LevelBake"/>.
    /// Player flags:
    /// - `--file &lt;audio or .m3u&gt;`: plays it through the music plugin, which feeds the sim;
    ///   live, song time is the music's (<see cref="MusicTimes"/>), and the run ends with it;
    /// - `--fixed-time`: song time is the frame's, exactly n/60 at frame n (<see cref="FrameClock"/>),
    ///   so a rerun shows the same frames. With `--file`, each frame first waits until the
    ///   plugin's analysis covers its frontier, and entries are known exactly at stamp + B. The
    ///   output is silent, since the music doesn't play in time with these frames;
    /// - `--seed N`: the sim's seed;
    /// - `--events FILE`: a synthetic event feed (<see cref="ScriptedEvents"/>) in place of the slice's
    ///   horde and sword, whose enemies' ids it would share: the sim keeps only the hero, on open ground;
    /// - `--debug "change@20,break@15"` and F5 to F8: the slice's debug hooks (<see cref="DebugCommands"/>);
    /// - `--overlay`: starts with the debug overlay shown (F3 toggles it), stills included;
    /// - `--headless`: silences the output and paces frames at 60 per second. (Unity's player
    ///   also takes it as -nographics, so stills can't use it: they run under --fixed-time.)
    /// - `--loop-out FILE`: writes each frame's times, the sim's hash, the hero and the enemies alive, as CSV;
    /// - `--events-out FILE`: writes each sim event as it settles (kind, time, its first ints), as CSV;
    /// - `--no-arena`: the placeholder's plane instead of the baked arena (which a build has unless
    ///   its bake failed), `--palette NAME` the arena's zone palette;
    /// - the look's `--hero-path`, `--view-width M` and `--camera-out FILE` (<see cref="LookPresenter"/>);
    /// - the hero's `--hero-out FILE` (<see cref="HeroPresenter"/>), and `--no-hero` for the placeholder's capsule.
    /// - `--structures-out FILE`: each frame's structure shards and their physics and shadow costs, as CSV
    ///   (<see cref="StructureShatter"/>; the structures' bench).
    /// - `--sound-probe`: every sound plays a click instead of its clip, at full level (<see cref="SoundScheduler.Probe"/>);
    ///   `--sound-out DIR` keeps Unity's mix and the sounds scheduled, the audio buffers and the frames, as the
    ///   sync spike does, for `game spike --sounds` to measure.
    /// The crowd and the effects (<see cref="EffectsPresenter"/>) need compute, so a headless run leaves
    /// them out; the effects and the sounds (<see cref="SoundPresenter"/>) run when their bakes are in the build.
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
        DebugCommands _commands;
        readonly SliceCounts _counts = new SliceCounts();
        DebugOverlay _overlay;
        LookPresenter _look;
        HeroPresenter _hero;
        EffectsPresenter _effects;
        SoundScheduler _sounds;
        SoundPresenter _soundPresenter;
        MixCapture _capture;
        string _soundOut;
        readonly List<SyncSpike.FrameRow> _frames = new List<SyncSpike.FrameRow>();
        ArenaRuntime _arena;
        CrystalStructures _crystal;
        StructureShatter _shatter;
        CrowdView _crowd;
        StreamWriter _out, _structuresOut;
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
            _soundOut = Args.Get("--sound-out");
            if (_soundOut != null) _soundOut = Args.Path(_soundOut);
            if (headless || _fixed || _soundOut != null)
            {
                _capture = listenerHost.AddComponent<MixCapture>();
                _capture.Mute = headless || _fixed;
                float seconds = Args.GetFloat("--seconds", 0f);
                _capture.Begin(_soundOut == null ? 0 : (seconds > 0 ? seconds : 600) + 2);
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
            ulong seed = (ulong)Math.Max(0, Args.GetInt("--seed", 1));
            var events = Args.Get("--events");
            bool arena = !Args.Has("--no-arena") && ArenaBake.Load() != null;
            var level = LevelBake.Load();
            var content = level?.Content();
            var config = new SimConfig { Seed = seed };
            if (level != null && events == null)
            {
                // The spawn ring stays out of the widest view: the loud zoom's, on a screen at least 16:9.
                float aspect = Mathf.Max(16f / 9, camera != null ? camera.aspect : 0);
                config = SliceLevel.Config(content, arena ? level.Arena() : null, CameraRig.Footprint(aspect), seed);
            }
            Driver = new SimDriver(config, clock);
            if (events != null) Driver.Scripted = ScriptedEvents.Load(Args.Path(events));
            _keys = new KeyInput();
            _commands = new DebugCommands(Args.Get("--debug"));

            var heroBake = Args.Has("--no-hero") ? null : HeroBake.Load();
            Driver.Events.Subscribe(new PlaceholderView(camera, ground: !arena, hero: heroBake == null));
            if (heroBake != null)
            {
                _hero = new HeroPresenter(heroBake);
                Driver.Events.Subscribe(_hero);
            }
            _look = new LookPresenter(camera, Driver.Sim);
            if (_hero != null) _look.DrawnHero = _hero.Drawn; // the hero presents first, then the camera follows it
            _look.Zones = content?.Schedule.Zones.Ids;
            Driver.Events.Subscribe(_look);
            if (arena)
            {
                _shatter = new StructureShatter();
                Driver.Events.Subscribe(_shatter);
                LoadArena(camera);
            }
            // A headless player has no graphics device, so no compute: the crowd and the effects sit it out.
            bool compute = SystemInfo.supportsComputeShaders;
            if (compute)
            {
                // After the look, whose camera rig it culls with.
                _crowd = new CrowdView(camera);
                Driver.Events.Subscribe(_crowd);
            }
            var roles = new RoleLooks(content);
            var library = compute ? EffectLibrary.TryLoad() : null;
            if (library != null)
            {
                _effects = new EffectsPresenter(camera, library, roles);
                Driver.Events.Subscribe(_effects);
            }
            SetupSound(roles);
            if (_shatter != null) _shatter.Erupted += Erupted;
            _overlay = new DebugOverlay(camera, Driver, Args.Has("--overlay"));
            Driver.Events.Subscribe(_overlay);
            Driver.Events.Subscribe(_counts);
            var eventsPath = Args.Get("--events-out");
            if (eventsPath != null) _counts.Open(Args.Path(eventsPath));

            var outPath = Args.Get("--loop-out");
            if (outPath != null)
            {
                _out = new StreamWriter(Args.Path(outPath));
                _out.WriteLine("frame,frame_song,frontier,lead,tick,hash,events,hero_x,hero_y,alive");
            }
            var structuresPath = Args.Get("--structures-out");
            if (structuresPath != null && _shatter != null)
            {
                _structuresOut = new StreamWriter(Args.Path(structuresPath));
                _structuresOut.WriteLine("frame,frame_song,breaks,shards,flying,moved_m,steps,physics_ms,shadow_ms");
            }
            Debug.Log($"GameLoop: {(file != null ? file : "no music")}, {(_fixed ? "fixed time" : "real time")}, " +
                      $"seed {config.Seed}, {(config.Horde != null ? "the slice's level" : "no horde")}, events {(events ?? "none")}, " +
                      $"headless {headless}");
        }

        /// <summary>
        /// A regrowth erupting: the effects' structure burst thrown up from its footprint, half its height
        /// up, and the crystal shatter's sound, pitched by its size as a break's (2.5 small, 4 large).
        /// </summary>
        void Erupted(double t, int site, Vector3 centre, float sizeM, Color tint)
        {
            _effects?.Cues.Burst(t, centre + Vector3.up * 0.5f, Vector2.zero, Mathf.Clamp(sizeM / 3f, 0.8f, 1.5f), tint);
            _soundPresenter?.Shatter(t, sizeM > 3.5f ? 4f : 2.5f, (uint)site * 0x9E3779B1u ^ (uint)(t * 1000));
        }

        /// <summary>The sounds on their own voices (a filter's callback serves one source: not the music's object).</summary>
        void SetupSound(RoleLooks roles)
        {
            var bank = SoundBank.Load();
            bool probe = Args.Has("--sound-probe");
            if (bank == null && !probe) return;
            _sounds = new SoundScheduler(bank, new GameObject("Sounds"), Driver.Clock, () => AudioSettings.dspTime, log: _soundOut != null);
            if (probe) _sounds.Probe = BeatClicks.MakeClip(AudioSettings.outputSampleRate);
            Func<float> follower = null;
            if (_music != null) follower = () => _music.Status.FollowerGain > 0 ? _music.Status.FollowerGain : 1f;
            _soundPresenter = new SoundPresenter(_sounds, Driver.Sim, roles, bank != null ? bank.Limit : 1f, follower);
            Driver.Events.Subscribe(_soundPresenter);
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
                _shatter.Attach(a.Sites, a.RefreshShadows);
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
            if (Driver.Clock.Ready)
            {
                _keyChanges += _keys.Drain(Driver.Sim, Driver.Clock);
                _commands.Apply(Driver.Sim);
            }
            _musicTimes?.Measure(Time.unscaledDeltaTime);
            var now = new ClockReadings
            {
                Real = Time.realtimeSinceStartupAsDouble,
                Frame = Time.timeSinceLevelLoadAsDouble,
            };
            if (Driver.Step(now)) Keep();
            if (_soundOut != null && _music != null && _music.Clock.Anchored)
                _frames.Add(new SyncSpike.FrameRow
                {
                    tick = MixCapture.Now,
                    frameSong = Driver.Times.FrameSong,
                    display = _music.Clock.DisplayLatency,
                });
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
            _structuresOut?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2},{3},{4},{5:F4},{6},{7:F4},{8:F4}",
                frame, Driver.Times.FrameSong, _shatter.Breaks, _shatter.Shards, _shatter.Flying, _shatter.MovedM,
                _shatter.FrameSteps, _shatter.FramePhysicsMs, _shatter.FrameShadowMs));
            if (_out == null) return;
            var t = Driver.Times;
            var hero = Driver.Presented.State.Hero;
            hero.PositionAt(Driver.Presented.Song, out double x, out double y);
            _out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4},{5:X16},{6},{7:R},{8:R},{9}",
                frame, t.FrameSong, t.Frontier, t.Lead, Driver.Sim.State.Tick, Driver.Sim.Hash, Driver.Sim.EventCount, x, y,
                Driver.Sim.State.EnemiesAlive));
        }

        void OnApplicationQuit() => Finish(0, null);

        void OnDestroy()
        {
            _keys?.Dispose();
            _look?.Dispose();
            _hero?.Dispose();
            _effects?.Dispose();
            _shatter?.Dispose();
            _crystal?.Dispose();
            _arena?.Dispose();
            _crowd?.Dispose();
            _out?.Dispose();
            _out = null;
            _structuresOut?.Dispose();
            _structuresOut = null;
            _counts.Dispose();
        }

        /// <summary>Logs the run's summary and quits with `code`.</summary>
        void Finish(int code, string error)
        {
            if (_done) return;
            _done = true;
            Debug.Log("GameLoop: " + (error ?? "done") + "; " + Summary());
            _out?.Dispose();
            _out = null;
            _structuresOut?.Dispose();
            _structuresOut = null;
            _counts.Dispose();
            if (_soundOut != null && _music != null && _sounds != null) KeepSounds(error);
            if (_music != null) _music.Close();
            if (error != null) Debug.LogError("GameLoop: " + error);
            Application.Quit(code);
        }

        void KeepSounds(string error)
        {
            var log = _sounds.Log;
            var songs = new double[log.Count];
            var dsps = new double[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                songs[i] = log.Array[i].Song;
                dsps[i] = log.Array[i].Dsp;
            }
            SyncSpike.Write(_soundOut, _music, _capture, songs, dsps, _frames.ToArray(), error);
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
            s += string.Format(c, "; enemies alive {0}, {1} killed, {2} heavy strikes, {3} structures broken, {4} regrown, {5} zone changes, " +
                                  "{6} debug commands", Driver.Sim.State.EnemiesAlive, _counts.Kills, _counts.HeavyStrikes, _counts.Breaks,
                _counts.Regrowths, _counts.ZoneChanges, _commands.Given);
            if (_effects != null)
                s += string.Format(c, "; effects {0} spawned ({1} rejected)", _effects.Spawned, _effects.System.RejectedTotal);
            if (_sounds != null)
                s += string.Format(c, "; sounds {0} scheduled, {1} dropped", _sounds.Scheduled, _sounds.Dropped);
            return s;
        }

        static double Pct(double[] sorted, double p) => sorted[(int)Math.Round(p * (sorted.Length - 1))];

        /// <summary>What the run did, for its summary, and each event as CSV when asked.</summary>
        sealed class SliceCounts : IPresenter, IDisposable
        {
            public int Kills, HeavyStrikes, Breaks, Regrowths, ZoneChanges;
            StreamWriter _out;

            public void Open(string path)
            {
                _out = new StreamWriter(path);
                _out.WriteLine("kind,t,i0,i1,i2");
            }

            public void Dispose()
            {
                _out?.Dispose();
                _out = null;
            }

            public void OnEvent(in SimEvent e)
            {
                _out?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2},{3},{4}", e.Kind, e.T, e.I0, e.I1, e.I2));
                switch (e.Kind)
                {
                    case SimEventKind.Death when e.AsDeath.Enemy >= 0: Kills++; break;
                    case SimEventKind.Attack when e.AsAttack.Attacker == -1 && e.AsAttack.Heavy: HeavyStrikes++; break;
                    case SimEventKind.StructureBreak: Breaks++; break;
                    case SimEventKind.Regrowth: Regrowths++; break;
                    case SimEventKind.ZoneChange: ZoneChanges++; break;
                }
            }

            public void Present(in PresentFrame frame) { }
        }
    }
}
