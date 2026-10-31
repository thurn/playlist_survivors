using System;
using System.Globalization;
using System.Threading;
using Game.Client.Arena;
using Game.Client.Calibration;
using Game.Client.Crowd;
using Game.Client.Effects;
using Game.Client.Hero;
using Game.Client.Hud;
using Game.Client.Look;
using Game.Client.Music;
using Game.Client.Pickups;
using Game.Client.Sound;
using Game.Client.Structures;
using Game.Client.Weapons;
using PS.Game.Sim;
using PS.Game.Sim.Level;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Client.Loop
{
    /// <summary>
    /// The game's frame loop on the main thread (unity-game.md, Architecture): the music's
    /// entries and the keys into the sim, the sim stepped to the frontier, its events and state
    /// to the presenters at frameSong. The sim plays the slice's level (<see cref="SliceLevel"/>:
    /// the arena's level data, its horde and the sword) from the build's <see cref="LevelBake"/>;
    /// `--run` plays the run's schedule instead (<see cref="RunLevel"/>), and `--cast ROLE,...`
    /// stands those roles in a row before a swordless hero, for the cast's stills. `--level PATH` plays the run on
    /// level files in place of the bake's (<see cref="LevelFiles"/>), and `--start-minute N` starts the run at N:00
    /// with a hero built for it (<see cref="SimConfig.StartMinute"/>); each implies `--run`.
    /// Its music is the source Boot resolved from `--file` or `--app`, or the title screen's pick
    /// (<see cref="MusicSource"/>): played through the music plugin, which feeds the sim; live,
    /// song time is the music's (<see cref="MusicTimes"/>), calibrated for the output device it
    /// plays on (<see cref="CalibrationStore"/>).
    ///
    /// The run (unity-game.md, The Run; <see cref="RunControl"/>): with a window, Enter or Space
    /// starts it on the next beat; a headless or fixed-time run starts after 2 s of clear beat, and
    /// a run without music at once. Esc and focus loss pause it under the pause menu
    /// (<see cref="PauseMenu"/>: resume, the calibration screens, end the run), and Esc or its
    /// Resume resumes it on the next beat. The plugin's watchdog releasing the tap stops the
    /// music. Backspace and an output device change (the latency changes) end it, each a run
    /// control the sim records.
    /// The loop finishes when the run ends (after <see cref="EndCardS"/> for a victory or a fall,
    /// whose card the HUD shows), or with a file's music, and then quits, or returns to the title
    /// screen if it started there.
    ///
    /// Each run with music is recorded (<see cref="Sessions"/>): the plugin writes its audio and
    /// entries into the run's folder, and the run's end its <see cref="SessionRecord"/>. A Replay
    /// source plays one again (Watch my run, `--replay DIR`): the sim takes the recorded entries,
    /// keys, debug commands and run controls at their recorded times (<see cref="SessionReplay"/>),
    /// no live input reaches it (Backspace stops the replay), and at the run's end the replay
    /// compares the settled hash, logging a `Replay:` line (a headless `--replay` exits 1 when it
    /// differs in the runtime that recorded it). The run's config is built once, from its <see cref="RunInputs"/> (the
    /// flags' on the build, or the replayed session's), which its session records. Player flags (<see cref="LaunchOptions"/>):
    /// - `--fixed-time`: song time is the frame's, exactly n/60 at frame n (<see cref="FrameClock"/>),
    ///   so a rerun shows the same frames. The frames drive a file's or replay's music
    ///   (<see cref="MusicPlayer.Driven"/>): each renders it, silently, to the frame's mixer position,
    ///   so a run takes as long as its frames compute, not its song time. With `--file`, each frame
    ///   then waits until the plugin's analysis covers its frontier, and entries are known exactly at
    ///   stamp + B;
    /// - `--seed N`: the sim's seed;
    /// - `--easy`: Easy (half contact damage, and floor chickens that heal);
    /// - `--pause-at S`: pauses the run once frameSong reaches S, under the pause menu, windowless too (for stills);
    /// - `--events FILE`: a synthetic event feed (<see cref="ScriptedEvents"/>) in place of the slice's
    ///   horde and sword, whose enemies' ids it would share: the sim keeps only the hero, on open ground;
    /// - `--debug "change@20,spawn:werewolf@15"`, the F1 menu (<see cref="DebugMenu"/>) and F5 to F8: the debug commands
    ///   (<see cref="DebugCommands"/>), which the sim records like keys;
    /// - `--arsenal "garlic:8,fire-trail:3"`: the hero's starting weapons and ranks, in slot order, in place of the sword;
    ///   `--no-cards`: levels count without opening their cards, so the arsenal stays as given (for weapons' stills);
    /// - `--walk "right@9-11,up@11-12.5"`: keys held over song-time spans (<see cref="ScriptedKeys"/>), for a walking hero in stills;
    /// - `--bot positioning|idle`: the sweeps' bot plays the run and picks its cards (<see cref="BotInput"/>), for the full run;
    /// - `--sessions DIR`, `--no-session` and `--watch` (<see cref="Sessions"/>): where runs are recorded,
    ///   and with --watch, the run replayed once it ends (under --watch, `--seconds N` ends the run, not the launch);
    ///   a run a replay couldn't rebuild (on `--level`, `--cast`, `--arsenal` or `--no-cards`: <see cref="RunInputs.Unrecordable"/>)
    ///   isn't recorded, which it says as it starts, and --watch refuses one;
    /// - `--overlay`: starts with the debug overlay shown (F3 toggles it), stills included, with the enemies' flow field;
    /// - `--headless` (or Unity's batch mode): silences the output and paces frames at 60 per second. (Unity's player
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
    /// It logs a summary when the run ends (<see cref="RunTelemetry"/>): the lead, frameSong's holds and the sim's timings.
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
        /// <summary>How long the victory or fall card shows before the loop finishes.</summary>
        public const double EndCardS = 5;
        /// <summary>How far past the sim a replay's frame wants its entries supplied (s): more than a frame steps.</summary>
        const double ReplayAhead = 1;

        MusicPlayer _music;
        MusicTimes _musicTimes;
        KeyInput _keys;
        DebugCommands _commands;
        DebugMenu _menu;
        ScriptedKeys _walk;
        BotInput _bot;
        DebugOverlay _overlay;
        LookPresenter _look;
        HeroPresenter _hero;
        EffectsPresenter _effects;
        SoundScheduler _sounds;
        SoundPresenter _soundPresenter;
        GarlicAura _garlic;
        FireTrailView _fireTrail;
        MixCapture _capture;
        string _soundOut;
        ArenaRuntime _arena;
        CrystalStructures _crystal;
        StructureShatter _shatter;
        CrowdView _crowd;
        GemView _gems;
        WandView _wand;
        BibleView _bible;
        ChestView _chests;
        EvolutionLayers _evolutions;
        LightningView _lightning;
        HudPresenter _hud;
        RunTelemetry _telemetry;
        bool _fixed, _done, _quitting, _interactive;
        bool _deviceGiven, _focusLost;
        PauseMenu _pause;
        CalibrationScreen _calibrating;
        double _pauseAt;
        int _releases;
        LaunchOptions _options;
        RunInputs _run;
        string _sessionDir;
        SessionReplay _replay;
        Action<string> _ended;

        public SimDriver Driver { get; private set; }

        /// <summary>What the music plays, null without music; its <see cref="MusicSource.Tag"/> is the HUD's source tag.</summary>
        public MusicSource Source => _music != null ? _music.Source : null;

        /// <summary>
        /// Starts the loop on `host`'s scene with the command line's flags, playing `source` (null:
        /// no music). When the run ends it calls `ended` with why, or quits if that is null.
        /// </summary>
        public static GameLoop Begin(GameObject host, Camera camera, MusicSource source, Action<string> ended = null)
        {
            var loop = host.AddComponent<GameLoop>();
            loop._ended = ended;
            loop.Setup(camera, source);
            return loop;
        }

        void Setup(Camera camera, MusicSource source)
        {
            var options = _options = LaunchOptions.Current;
            _fixed = options.FixedTime;
            bool headless = options.Headless;
            if (headless) Application.targetFrameRate = Boot.FixedFps;
            var listenerHost = camera != null ? camera.gameObject : gameObject;
            if (listenerHost.GetComponent<AudioListener>() == null) listenerHost.AddComponent<AudioListener>();
            _soundOut = options.Outputs.Sound;
            if (headless || _fixed || _soundOut != null)
            {
                _capture = listenerHost.AddComponent<MixCapture>();
                _capture.Mute = headless || _fixed;
                float seconds = options.Seconds;
                _capture.Begin(_soundOut == null ? 0 : (seconds > 0 ? seconds : 600) + 2);
            }

            SessionRecord replayed = null;
            if (source != null && source.IsReplay) replayed = SessionRecord.Read(source.Path);
            // A replay plays the build's own level, on the run's own inputs whatever this command line says.
            var level = LevelFiles.Load(replayed == null ? options.LevelPath : null);
            var content = level?.Content();
            bool arena = !options.NoArena && ArenaBake.Load() != null;
            var events = replayed == null ? options.Events : null;
            RunInputs run;
            if (replayed != null)
            {
                run = replayed.Run;
                if (level == null && (run.Level != SessionLevel.Open || run.Arena))
                    throw new InvalidOperationException("GameLoop: a replay of the slice needs the build's level bake");
                arena = run.Arena && ArenaBake.Load() != null;
            }
            else
            {
                // The spawn ring stays out of the widest view: the loud zoom's, on a screen at least 16:9.
                float aspect = Mathf.Max(16f / 9, camera != null ? camera.aspect : 0);
                run = options.Run.On(level != null, arena, CameraRig.Footprint(aspect));
                if (source != null && options.Recording)
                {
                    if (run.Unrecordable == null) _sessionDir = Sessions.Begin();
                    else Debug.Log($"GameLoop: not recorded: {run.Unrecordable}, which a replay couldn't rebuild");
                }
            }
            _run = run;

            IGameClock clock = new FrameClock();
            if (source != null)
            {
                var musicHost = new GameObject("Music");
                musicHost.AddComponent<AudioSource>();
                _music = musicHost.AddComponent<MusicPlayer>();
                _music.Play(source, _sessionDir, driven: _fixed && !source.IsApp);
                Calibrate();
                if (!_fixed)
                {
                    clock = _musicTimes = new MusicTimes(_music.Clock, MusicPlayer.Refresh,
                        MusicPlayer.ScheduleHeadroom, () => AudioSettings.dspTime);
                }
            }
            _interactive = options.Windowed;
            var config = run.Build(content, run.Arena ? level.Arena() : null);
            Driver = new SimDriver(config, clock);
            if (replayed != null) Driver.Replay = _replay = new SessionReplay(replayed);
            if (events != null) Driver.Scripted = ScriptedEvents.Load(events);
            _keys = new KeyInput();
            _pause = new PauseMenu(transform);
            _pauseAt = options.Scripted.PauseAt;
            _commands = new DebugCommands(options.Scripted.Debug, content);
            _menu = new DebugMenu(camera, content);
            _walk = new ScriptedKeys(options.Scripted.Walk);
            if (replayed == null) _bot = BotInput.For(options.Scripted.Bot, config.Seed, content);
            if (_bot != null) Driver.Events.Subscribe(_bot);

            var heroBake = options.NoHero ? null : HeroBake.Load();
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
            if (content != null)
            {
                // Instanced crystals; a headless player draws nothing.
                if (SystemInfo.supportsInstancing)
                {
                    _gems = new GemView(camera, content.Progression.Gems);
                    Driver.Events.Subscribe(_gems);
                }
            }
            var roles = new RoleLooks(content);
            var weapons = new WeaponLooks(content?.Weapons);
            var library = compute ? EffectLibrary.TryLoad() : null;
            if (library != null)
            {
                _effects = new EffectsPresenter(camera, library, roles);
                _effects.Cues.SlashTint = w => weapons.Of(w) == WeaponLooks.Family.Sword && weapons.Evolved(w) ? WeaponLooks.Tear : default;
                Driver.Events.Subscribe(_effects);
            }
            SetupSound(roles);
            if (content != null)
            {
                // The weapons' looks and sounds, into the effects and sounds set up above.
                _garlic = new GarlicAura(camera, Driver.Sim, content, _effects?.System, _soundPresenter);
                Driver.Events.Subscribe(_garlic);
                _fireTrail = new FireTrailView(camera, content, _effects?.System, _soundPresenter);
                Driver.Events.Subscribe(_fireTrail);
                _lightning = new LightningView(camera, weapons, _effects, _soundPresenter);
                Driver.Events.Subscribe(_lightning);
                if (SystemInfo.supportsInstancing)
                {
                    // The weapons' crystals; their casts and hits go to the effects as cues.
                    _wand = new WandView(camera, weapons, _effects, _soundPresenter);
                    Driver.Events.Subscribe(_wand);
                    _bible = new BibleView(camera, weapons, _effects, _soundPresenter);
                    Driver.Events.Subscribe(_bible);
                    // The bosses' chests and their roulette, and the evolutions' own layers.
                    _chests = new ChestView(camera, Driver.Sim, content, weapons, _effects, _soundPresenter);
                    Driver.Events.Subscribe(_chests);
                    _evolutions = new EvolutionLayers(camera, Driver.Sim, content, weapons, _effects, _soundPresenter);
                    Driver.Events.Subscribe(_evolutions);
                }
            }
            if (_shatter != null) _shatter.Erupted += Erupted;
            _hud = new HudPresenter(transform, camera, Driver.Sim, content, source, _interactive) { Menu = _pause };
            if (_hero != null) _hud.DrawnHero = _hero.Drawn;
            Driver.Events.Subscribe(_hud);
            _overlay = new DebugOverlay(camera, Driver, options.Overlay);
            Driver.Events.Subscribe(_overlay);
            _telemetry = new RunTelemetry(Driver, options.Outputs, _shatter);
            Debug.Log($"GameLoop: {(source != null ? (source.IsApp ? $"app {source.Tag} (pid {source.Pid})" : source.Path) : "no music")}, " +
                      $"{(_fixed ? "fixed time" : "real time")}, seed {config.Seed}, " +
                      $"{(config.Horde == null ? "no horde" : config.Horde.Schedule ? "the run's level" : "the slice's level")}, events {(events ?? "none")}, headless {headless}, " +
                      $"start {config.Start}, easy {config.Easy}, start minute {config.StartMinute}, " +
                      $"bot {options.Scripted.Bot?.ToString().ToLowerInvariant() ?? "none"}" +
                      (level != null && level.Replaced.Count > 0 ? $", --level files {string.Join(", ", level.Replaced)}" : ""));
        }

        /// <summary>The output device's saved calibration on the music's clock (a device change ends the run, so it holds for the run).</summary>
        void Calibrate()
        {
            var device = CalibrationScreen.CurrentDevice();
            var offsets = CalibrationStore.Load().For(device.Uid);
            offsets.ApplyTo(_music.Clock);
            Debug.Log($"GameLoop: calibrated for {(device.Uid.Length > 0 ? $"{device.Name} ({device.Uid})" : "no device")}: {offsets}");
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
            bool probe = _options.SoundProbe;
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
            _arena = ArenaRuntime.Load(camera, _options.Palette);
            _arena.OnReady += a =>
            {
                _crystal = new CrystalStructures();
                _crystal.Apply(a, camera);
                _shatter.Attach(a.Sites, a.RefreshShadows);
                _look.Volume = a.Post;
                _look.FollowHero = a.FollowHero;
                _look.ZoneLight = new ZoneLighting(a.LayoutJson, a.Lighting, a.Relight);
            };
        }

        void Update()
        {
            if (_done) return;
            if (_music != null)
            {
                if (_music.Failed) { Finish(1, _music.Error); return; }
                if (_music.DeviceChanged && !_deviceGiven)
                {
                    _deviceGiven = true;
                    // A replay's run ends as it ended: watching it just stops.
                    if (_replay != null) { Finish(0, "the output device changed"); return; }
                    if (!Control(RunControl.DeviceChanged)) return;
                }
                // Fixed time: the frames drive the music, to this frame's mixer position.
                _music.RenderTo(FrameClock.SongAt(Time.timeSinceLevelLoadAsDouble));
                if (_replay != null) { if (!FeedReplay()) return; }
                else if (_fixed) { if (!FeedSettled()) return; }
                else FeedLive();
            }
            // Before the music's anchor no key has a song time: they wait for it. A replay takes only its recorded keys.
            if (Driver.Clock.Ready && _replay == null)
            {
                _telemetry.KeyChanges += _keys.Drain(Driver.Sim, Driver.Clock);
                _commands.Apply(Driver.Sim);
                _menu.Apply(Driver.Sim, _commands);
                _walk.Apply(Driver.Sim);
                RunInput();
                _bot?.Act(Driver.Sim);
            }
            _musicTimes?.Measure(Time.unscaledDeltaTime);
            var now = new ClockReadings
            {
                Real = Time.realtimeSinceStartupAsDouble,
                Frame = Time.timeSinceLevelLoadAsDouble,
            };
            if (Driver.Step(now)) _telemetry.Keep();
            if (_soundOut != null && _music != null && _music.Clock.Anchored) _telemetry.KeepFrame(_music);
            _overlay.Toggle();
            _menu.Present(Driver.Sim);
            if (_replay != null)
            {
                if (_replay.Done(Driver.Sim) || _music.Ended) FinishReplay();
                else if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame) Finish(0, "the replay was stopped");
                return;
            }
            var state = Driver.Sim.State;
            if (state.Phase == RunPhase.Ended && (state.End == RunEnd.Quit || state.End == RunEnd.DeviceChanged
                                                  || Driver.Times.FrameSong >= state.EndedAt + EndCardS))
                Finish(0, Why(state.End));
            else if (_music != null && !_fixed && _music.Ended) Finish(0, null);
            else if (_calibrating == null && Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame)
                Control(RunControl.Quit);
        }

        /// <summary>
        /// Gives the sim a run control now; before the clock is ready (no song time yet) an end
        /// finishes the loop at once instead. False if it finished.
        /// </summary>
        bool Control(RunControl control)
        {
            if (Driver.Clock.Ready)
            {
                Driver.Sim.Control(control);
                return true;
            }
            if (control != RunControl.Quit && control != RunControl.DeviceChanged) return true;
            Finish(0, Why(control == RunControl.Quit ? RunEnd.Quit : RunEnd.DeviceChanged));
            return false;
        }

        /// <summary>The run's inputs besides the keys: the start, a pause or resume, and the watchdog.</summary>
        void RunInput()
        {
            var state = Driver.Sim.State;
            var keyboard = Keyboard.current;
            if (_music != null && _music.Status.Releases != _releases)
            {
                _releases = _music.Status.Releases;
                Driver.Sim.Control(RunControl.Watchdog);
            }
            bool focusLost = _focusLost;
            _focusLost = false;
            if (_pauseAt >= 0 && Driver.Times.FrameSong >= _pauseAt)
            {
                _pauseAt = -1;
                Driver.Sim.Control(RunControl.Pause);
            }
            if (!_interactive)
            {
                if (state.FrozenFor(FreezeReason.Pause)) _pause.Show(); // a scripted pause's menu, for stills
                if (_bot == null) AutoCards.Confirm(Driver.Sim); // nobody to choose: the selected card, once all three are in
                return;
            }
            if (state.Phase == RunPhase.Waiting)
            {
                if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                                         || keyboard.spaceKey.wasPressedThisFrame))
                    Driver.Sim.Control(RunControl.Start);
                return;
            }
            if (_calibrating != null) return; // its screen has the keys
            if (!state.FrozenFor(FreezeReason.Pause))
            {
                _pause.Hide(); // resumed, or the run ended
                bool esc = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
                if (esc || focusLost) Driver.Sim.Control(RunControl.Pause);
                if (!Driver.Sim.State.FrozenFor(FreezeReason.Pause)) return;
            }
            _pause.Show();
            switch (_pause.Read())
            {
                case PauseMenu.Choice.Resume:
                    Driver.Sim.Control(RunControl.Resume);
                    _pause.Hide();
                    break;
                case PauseMenu.Choice.CalibrateVideo:
                    OpenCalibration(CalibrationScreen.Mode.Video);
                    break;
                case PauseMenu.Choice.CalibrateInput:
                    OpenCalibration(CalibrationScreen.Mode.Input);
                    break;
                case PauseMenu.Choice.EndRun:
                    _pause.Hide();
                    Driver.Sim.Control(RunControl.Quit);
                    break;
            }
        }

        /// <summary>A calibration screen over the paused run, the music ducked; its new offsets take effect when it closes.</summary>
        void OpenCalibration(CalibrationScreen.Mode mode)
        {
            _pause.Visible = false;
            _calibrating = CalibrationScreen.Begin(gameObject, mode, () =>
            {
                _calibrating = null;
                if (_done) return;
                if (_music != null) Calibrate();
                _pause.Visible = true;
            }, _music);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) _focusLost = true;
        }

        static string Why(RunEnd end)
        {
            switch (end)
            {
                case RunEnd.Victory: return "victory";
                case RunEnd.Death: return "the hero fell";
                case RunEnd.Quit: return "the run was ended";
                case RunEnd.DeviceChanged: return "the output device changed";
                default: return null;
            }
        }

        /// <summary>
        /// Supplies the replay with the recorded entries the plugin's Replay source delivers, which it
        /// hands over as fast as they're polled, waiting until those the sim may take this frame are in
        /// (or with `all`, every one). False if the replay failed.
        /// </summary>
        bool FeedReplay(bool all = false)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                for (int i = 0; i < _music.EntryCount; i++) _replay.Supply(_music.Entries[i]);
                _telemetry.Entries += _music.EntryCount;
                if (_music.Failed) { Finish(1, _music.Error); return false; }
                bool drained = _music.EntryCount < MusicPlayer.MaxEntries;
                if (drained && (_replay.AllSupplied || (!all && _replay.NextUnsupplied > Driver.Sim.Now + ReplayAhead))) return true;
                if (drained && _music.Status.AnalysisDone != 0)
                {
                    Finish(1, $"the session's record holds {_replay.Supplied} of the {_replay.Record.Entries.Length} entries its run took");
                    return false;
                }
                if (Time.realtimeSinceStartupAsDouble - started > AnalysisTimeout)
                {
                    Finish(1, $"the Replay source delivered {_replay.Supplied} entries, then none for {AnalysisTimeout} s");
                    return false;
                }
                if (drained) Thread.Sleep(1);
                _music.Poll();
            }
        }

        /// <summary>
        /// The replay reached its run's end (or its music ended: the rest is stepped at once): compares
        /// the settled hash and finishes, failing only on a difference in the runtime that recorded it.
        /// </summary>
        void FinishReplay()
        {
            var record = _replay.Record;
            if (!_replay.Done(Driver.Sim))
            {
                if (!FeedReplay(all: true)) return;
                _replay.StepTo(Driver.Sim, record.End);
            }
            bool matches = _replay.Matches(Driver.Sim);
            bool sameRuntime = record.Runtime == SessionRecord.CurrentRuntime;
            string verdict = matches ? "matches" : sameRuntime ? "DIFFERS" : $"differs (recorded in {record.Runtime})";
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "Replay: {0}: song {1:F3} s, hash {2:X16} ({3} events), recorded {4:X16} ({5} events) in {6}: {7}",
                _music.Source.Path, Driver.Sim.Now, Driver.Sim.Hash, Driver.Sim.EventCount, record.Hash, record.Events,
                record.Runtime, verdict));
            Finish(matches || !sameRuntime ? 0 : 1, "the replay ended: its hash " + verdict);
        }

        void FeedLive()
        {
            for (int i = 0; i < _music.EntryCount; i++) Driver.Add(_music.Entries[i]);
            _telemetry.Entries += _music.EntryCount;
        }

        /// <summary>
        /// Feeds entries known exactly at stamp + B, polling the plugin until its analysis covers
        /// this frame's frontier (or is done), so the frame never depends on the plugin's timing.
        /// The frame's render position lets the analysis read M past it, past that frontier's
        /// stamps; a driven music that hasn't started yet is rendered as the wait goes. False if the
        /// run failed.
        /// </summary>
        bool FeedSettled()
        {
            double frame = Time.timeSinceLevelLoadAsDouble;
            double needed = Driver.StampNeededFor(FrameClock.FrontierAt(frame));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                _music.RenderTo(FrameClock.SongAt(frame));
                for (int i = 0; i < _music.EntryCount; i++) Driver.AddSettled(_music.Entries[i]);
                _telemetry.Entries += _music.EntryCount;
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

        void OnApplicationQuit()
        {
            _quitting = true;
            Finish(0, null);
        }

        void OnDestroy()
        {
            _pause?.Dispose();
            _keys?.Dispose();
            _look?.Dispose();
            _hero?.Dispose();
            _effects?.Dispose();
            _shatter?.Dispose();
            _crystal?.Dispose();
            _arena?.Dispose();
            _crowd?.Dispose();
            _gems?.Dispose();
            _garlic?.Dispose();
            _fireTrail?.Dispose();
            _wand?.Dispose();
            _bible?.Dispose();
            _chests?.Dispose();
            _evolutions?.Dispose();
            _lightning?.Dispose();
            _hud?.Dispose();
            _overlay?.Dispose();
            _telemetry?.Dispose();
        }

        /// <summary>
        /// Logs the run's summary with why it ended (null: its music did) and quits with `code`
        /// (not 0: `why` is an error), or hands `why` to whoever started it.
        /// </summary>
        void Finish(int code, string why)
        {
            if (_done) return;
            _done = true;
            _pause?.Hide();
            string error = code != 0 ? why : null;
            Debug.Log("GameLoop: " + (why ?? "done") + "; " + _telemetry.Summary(_music, _musicTimes, _keys, _commands, _effects, _sounds));
            _telemetry.Dispose();
            if (_soundOut != null && _music != null && _sounds != null) _telemetry.KeepSounds(_soundOut, _music, _capture, _sounds, error);
            if (_music != null) _music.Close();
            // Under --watch, a session that wasn't kept leaves nothing to watch: the launch fails.
            if (_sessionDir != null && !KeepSession() && code == 0 && _options.Watch) code = 1;
            if (error != null) Debug.LogError("GameLoop: " + error);
            if (_sessionDir != null && code == 0 && !_quitting && _options.Watch)
            {
                // Watch it at once: the boot scene again, which replays it.
                Sessions.WatchNext = _sessionDir;
                SceneManager.LoadScene(gameObject.scene.name, LoadSceneMode.Single);
            }
            else if (_ended != null && !_quitting) _ended(why ?? "the music ended");
            else Application.Quit(code);
        }

        /// <summary>Ends the run now, as Backspace does (Boot's `--seconds` under --watch).</summary>
        public void End(string why) => Finish(0, why);

        /// <summary>The run's half of its session, beside the plugin's (closed by now): its inputs, config and hash. False if it wasn't kept.</summary>
        bool KeepSession()
        {
            try
            {
                var record = SessionRecord.Of(_run, Driver.Sim);
                record.Build = $"{Application.version} {Application.buildGUID}";
                record.Platform = Application.platform.ToString();
                record.Source = _music.Source.IsApp ? $"app {_music.Source.Tag}" : _music.Source.Path;
                record.Flags = _options.Flags;
                record.Write(_sessionDir);
                Sessions.Last = _sessionDir;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "GameLoop: session in {0}: song {1:F3} s, {2} entries, {3} key changes, " +
                    "{4} debug commands, hash {5:X16}", _sessionDir, record.End, record.Entries.Length, record.Keys.Length,
                    record.Commands.Length, record.Hash));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameLoop: the session in {_sessionDir} wasn't kept: {e}");
                return false;
            }
        }
    }
}
