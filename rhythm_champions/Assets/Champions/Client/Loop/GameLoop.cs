using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Champions.Client.Calibration;
using Champions.Client.Music;
using Champions.Client.UI;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Loop
{
    /// <summary>Which game a run from the title or the command line plays: the main game, or the Boss Rush from a checkpoint.</summary>
    public sealed class RunMode
    {
        public static readonly RunMode Main = new RunMode();
        public bool BossRush;
        /// <summary>The Boss Rush's start (a saved checkpoint, `--boss N`'s reference), or null for its first boss.</summary>
        public BossRushCheckpoint From;
        /// <summary>The act checkpoints are saved (<see cref="RushSave"/>): not for `--boss N` or a scripted run.</summary>
        public bool Save;
    }

    /// <summary>What a run is played on: its music (or the session it replays), its content, inputs and scripts.</summary>
    public sealed class RunSetup
    {
        /// <summary>The music Boot or the picker resolved; null for a replay.</summary>
        public MusicSource Source;
        /// <summary>`--replay`: the session played again (its entries from its folder, its inputs at their times), or null.</summary>
        public SessionRecord Replay;
        public string ReplayDir;
        /// <summary>`--replay-speed`: song seconds a replay's frame advances per 1/60 s (a replay is fixed time).</summary>
        public double ReplaySpeed = 1;
        public bool FixedTime, Mute;
        public WorldPresenter World;
        /// <summary>Makes a run's UI (each run, Again's too); null for none.</summary>
        public Func<RunUi> MakeUi;
        public GameContent Content;
        public string ContentDir;
        public Palette Palette;
        /// <summary>The run's inputs besides the stream and the player's: the seed, the level, the first offer (a replay's are its record's).</summary>
        public RunInputs Run;
        /// <summary>`--walk` and `--picks`: the scripted player (<see cref="ScriptedPlayer"/>), or null for the keyboard and mouse.</summary>
        public string Walk, Picks;
        /// <summary>`--record DIR`: the first run's session is recorded there (the plugin's entries and audio, then its inputs).</summary>
        public string RecordDir;
        /// <summary>`--look`: the monsters' look.</summary>
        public MonsterLook Look;
        /// <summary>The end card's build icons from the run's sim, or null for none.</summary>
        public Func<Simulation, List<Icon>> BuildIcons;
        /// <summary>A Boss Rush keeps its act checkpoint on disk (<see cref="RushSave"/>).</summary>
        public bool SaveRush;
        /// <summary>The command line, for a session's run.json.</summary>
        public string[] Flags = Array.Empty<string>();
    }

    /// <summary>
    /// The frame loop on the main thread (plan.md, Architecture): the plugin's entries into the sim, the player's keys
    /// (<see cref="Keys"/>) and click targets (<see cref="Pointer"/>, cast through the camera) or the scripted player's
    /// (`--walk`), heard at keySong, the sim stepped to the frontier, its events handed to every presenter through one
    /// <see cref="EventDispatch"/> (<see cref="RunPresentation"/>) and the frame presented at its song time. The music clock
    /// takes the output device's calibration when the music opens (<see cref="CalibrationStore"/>); the pause menu holds the
    /// world through <see cref="Pause"/>.
    ///
    /// The run (design.md, The Run): level 1 on the content, from the first offer (the world empty and held until the pick,
    /// play from the next downbeat); death or victory ends it, and 1.5 s later (the shatter's moment) it hands the end card
    /// its summary (<see cref="Ended"/>); <see cref="Again"/> starts a new run on the same music, which plays on: a new sim
    /// from the frontier, given every entry the music has delivered.
    ///
    /// Its music is the source Boot resolved from `--file` or `--app` (<see cref="MusicSource"/>), played through the music
    /// plugin 3 s late (<see cref="MusicPlayer"/>); live, song time is the music's (<see cref="MusicTimes"/>). Under
    /// `--fixed-time` song time is the frame's (<see cref="FrameClock"/>) and the frames drive a file's music
    /// (<see cref="MusicPlayer.Driven"/>): each renders it, silently, to the frame's mixer position and waits until the
    /// plugin's analysis covers its frontier, so entries are known exactly at stamp + B and a rerun shows the same frames.
    ///
    /// A replay (`--replay DIR`) plays a recorded session instead, with no music: its recorded entries at their recorded
    /// known-at times and its inputs at their effective times (<see cref="SessionReplay"/>), through the same presenters on
    /// the frames' clock, until its run's end; then it compares the settled hash with the record's, logging a `Replay:` line.
    ///
    /// It never pauses for the music (design.md, The Run > Pauses): the music's end, a gap or a released tap play silence
    /// while song time and the sim run on. It logs the music as the sim meets it (`Music:` lines) and the run (`Run:` lines).
    /// </summary>
    [DefaultExecutionOrder(-100)] // after the music's poll
    public sealed class GameLoop : MonoBehaviour
    {
        /// <summary>
        /// The plugin's stamp trails its analysis by M - A (ps_plugin.h), and the latest block's entries
        /// may not be delivered yet when its position is reported (4096 samples at 44.1 kHz).
        /// </summary>
        const double StampLag = MusicPlayer.M - 0.048, BlockLag = 0.1;
        const double AnalysisTimeout = 10;
        /// <summary>Song time between status lines.</summary>
        const double StatusEvery = 5;
        /// <summary>The end card shows this long after the run ends (design.md, Ending: the camera holds on the shatter).</summary>
        public const double EndCardAfter = 1.5;

        RunDiagnostics _diagnostics;
        double _nextDiagnosticClock;
        RunSetup _setup;
        MusicPlayer _music;
        MusicTimes _musicTimes;
        bool _fixed, _done, _summarised, _deviceNoted, _playingNoted, _endShown, _recorded, _rushSaved;
        double _openedAt, _nextStatus, _frame0;
        Action<int, string> _finished;
        Camera _camera;
        GameObject _musicHost;
        RunPresentation _presentation;
        ScriptedPlayer _script;
        SessionReplay _replay;
        SimConfig _config;
        RunInputs _run;
        int _runs;
        readonly List<StreamEntry> _fed = new List<StreamEntry>();
        readonly Dictionary<HitSource, int> _hurt = new Dictionary<HitSource, int>();
        readonly List<double> _leads = new List<double>();
        int _beatsLate;
        double _delaySum;
        int _delaySamples;

        public SimDriver Driver { get; private set; }
        public MusicPlayer Music => _music;
        public RunPresentation Presentation => _presentation;
        /// <summary>The player's keys into the sim; a card's click taps its number here.</summary>
        public KeyInput Keys { get; } = new KeyInput();
        /// <summary>The left button's ground points into the sim, never through the UI.</summary>
        public PointerInput Pointer { get; } = new PointerInput();
        /// <summary>The run is scripted (`--walk`/`--picks`) or replayed: no player's input reaches it.</summary>
        public bool Scripted => _script != null || _replay != null;

        /// <summary>The run is a Boss Rush (boss-rush.md).</summary>
        public bool Rush => _run != null && _run.BossRush;

        /// <summary>The run ended: its end card's summary, and Again (a new run on the same music).</summary>
        public event Action<EndSummary, Action> Ended;

        /// <summary>
        /// Starts the loop on <paramref name="setup"/> on <paramref name="host"/>, the music's listener on <paramref name="camera"/>'s
        /// object. <paramref name="finished"/> gets an exit code and why when the loop can't go on (the music failed) or a
        /// replay is done.
        /// </summary>
        public static GameLoop Begin(GameObject host, Camera camera, RunSetup setup, Action<int, string> finished)
        {
            var loop = host.AddComponent<GameLoop>();
            loop._finished = finished;
            loop._camera = camera;
            loop._setup = setup;
            loop.Setup();
            return loop;
        }

        void Setup()
        {
            var s = _setup;
            _fixed = s.FixedTime || s.Replay != null;
            if (s.Replay != null)
            {
                _run = s.Replay.Run;
                _replay = new SessionReplay(s.Replay);
                foreach (var e in SessionRecord.LoadEntries(s.ReplayDir)) _replay.Supply(e);
                if (!_replay.AllSupplied)
                {
                    Finish(1, $"Replay: {s.ReplayDir}: the run took {s.Replay.Entries.Length} entries, its entries.jsonl holds {_replay.Supplied}");
                    return;
                }
                Begin(new FrameClock(), null);
                Log("Replay: {0}: {1} entries and {2} inputs to song {3:F3} s, recorded in {4}, at {5}x", s.ReplayDir, s.Replay.Entries.Length,
                    s.Replay.Inputs.Length, s.Replay.End, s.Replay.Runtime, s.ReplaySpeed);
                return;
            }
            var listener = _camera.gameObject;
            if (listener.GetComponent<AudioListener>() == null) listener.AddComponent<AudioListener>();
            if (s.Mute && listener.GetComponent<MuteOutput>() == null) listener.AddComponent<MuteOutput>();
            _musicHost = new GameObject("Music");
            _musicHost.AddComponent<AudioSource>();
            _music = _musicHost.AddComponent<MusicPlayer>();
            _openedAt = Time.realtimeSinceStartupAsDouble;
            bool driven = _fixed && !s.Source.IsApp;
            _music.Play(s.Source, s.RecordDir, driven: driven);
            if (_music.Failed)
            {
                Finish(1, _music.Error);
                return;
            }
            var device = CalibrationScreen.CurrentDevice();
            var offsets = CalibrationStore.Load().For(device.Uid);
            offsets.ApplyTo(_music.Clock);
            IGameClock clock = new FrameClock();
            if (!_fixed)
                clock = _musicTimes = new MusicTimes(_music.Clock, MusicPlayer.Refresh, MusicPlayer.ScheduleHeadroom, () => AudioSettings.dspTime);
            _run = s.Run ?? new RunInputs { Level = true, SurvivorWeapons = true, Start = RunStart.Immediately };
            if (s.Walk != null || s.Picks != null) _script = ScriptedPlayer.Parse(s.Walk, s.Picks, s.Content.Dashes);
            Begin(clock, Sounds(clock));
            Log("Music: {0} {1}, {2} s late, at {3} Hz{4}; {5}; calibration {6}", s.Source.IsApp ? "listening to" : "playing", s.Source.Tag,
                MusicPlayer.M, AudioSettings.outputSampleRate, driven ? ", driven by the frames" : "", DeviceText(), offsets);
        }

        SoundClock Sounds(IGameClock clock)
        {
            if (clock is FrameClock frames) return new SoundClock { Clock = frames, DspNow = () => frames.Mixer, Audible = false };
            return new SoundClock { Clock = clock, DspNow = () => AudioSettings.dspTime, Audible = true, Follower = () => _music.Status.FollowerGain };
        }

        /// <summary>A run's sim and presentation on <paramref name="clock"/>, from the sim's start (the first run) or from the frontier (Again).</summary>
        void Begin(IGameClock clock, SoundClock sound)
        {
            _diagnostics?.Dispose();
            _runs++;
            _endShown = false;
            _hurt.Clear();
            if (Driver != null) _run.StartTime = Math.Ceiling(Driver.Sim.Now * Simulation.TicksPerSecond - 1e-9) / Simulation.TicksPerSecond;
            _config = _run.Build(_setup.Content);
            Driver = new SimDriver(_config, clock);
            if (_replay != null) Driver.Replay = _replay;
            // Again's sim knows everything the music has said so far, from its start.
            foreach (var e in _fed) Driver.Sim.AddKnown(e, Driver.Sim.Now);
            _frame0 = Time.timeSinceLevelLoadAsDouble;
            var ui = _setup.MakeUi?.Invoke();
            if (ui != null) ui.Picked += slot => Keys.Tap((Key)((int)Key.One + slot), Time.realtimeSinceStartupAsDouble);
            _presentation = new RunPresentation(Driver.Sim, _config, _setup.Content, _setup.ContentDir, _setup.Palette, _camera, _setup.World,
                ui, sound ?? Sounds(clock), gameObject, _setup.Look);
            var sim = Driver.Sim;
            string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "playlist-survivors", "champions", "run-diagnostics");
            _diagnostics = new RunDiagnostics(sim, root, string.Format(CultureInfo.InvariantCulture,
                "Rhythm Champions diagnostics v1 build={0} unity={1} mode={2} fixed={3} mute={4} seed={5} quality={6} look={7} source={8}",
                Application.version + "/" + Application.buildGUID, Application.unityVersion, _replay != null ? "replay" : _script != null ? "scripted" : "operator", _fixed, _setup.Mute,
                _config.Seed, QualityTiers.Current, _setup.Look, _setup.Source == null ? "none" : _setup.Source.IsApp ? "app" : "file"), _config.Dashes.BufferS);
            _nextDiagnosticClock = sim.Now;
            Keys.ActionTimestamp = _diagnostics.KeyTimestamp;
            Log("Run diagnostics: {0}; summary: {0}.summary; error: {1}", _diagnostics.Path, _diagnostics.Error ?? "none");
            _presentation.Dispatch.Add("loop", (in SimEvent e) => Take(sim, e));
            Log("Run: {0} {1}, seed {2}, {3}, from song {4:F3} s{5}{6}", _replay != null ? "replaying" : "run", _runs, _config.Seed,
                _config.BossRush != null ? $"boss rush from boss {_config.BossRushStart.Boss + 1}" : _config.Level != null ? "level 1" : "no level", _config.StartTime, _config.Start == RunStart.FirstOffer ? ", the first offer up" : "",
                _script != null ? $", scripted ({_script.Kind}{(_setup.Picks != null ? ", picks " + _setup.Picks : "")})" : "");
        }

        static string DeviceText()
        {
            try
            {
                var d = Plugin.OutputDevice();
                return d.Uid.Length == 0 ? "no output device" :
                    string.Format(CultureInfo.InvariantCulture, "output {0}, {1:F1} ms{2}", d.Name, 1000 * d.Latency, d.Bluetooth ? " (Bluetooth)" : "");
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
        }

        void Update()
        {
            if (_done || Driver == null) return;
            if (_music != null)
            {
                if (_music.Failed)
                {
                    Finish(1, _music.Error);
                    return;
                }
                if (_music.DeviceChanged && !_deviceNoted)
                {
                    // The latency follows (the clock reads it each poll); the calibration's key is the device.
                    _deviceNoted = true;
                    Log("Music: the default output device changed: {0}", DeviceText());
                }
                if (_fixed) { if (!FeedSettled()) return; }
                else FeedLive();
            }
            var sim = Driver.Sim;
            if (Driver.Clock.Ready && _replay == null && sim.State.Phase != RunPhase.Ended)
            {
                if (_script != null) _script.Frame(sim, Driver.Times.Heard);
                else
                {
                    Keys.Drain(sim, Driver.Clock);
                    Pointer.Drain(sim, Driver.Clock, _camera, Keys.Suspended, Time.realtimeSinceStartupAsDouble);
                }
            }
            // A replay's song time starts at its own start, at its speed.
            double frame = _replay != null ? (Time.timeSinceLevelLoadAsDouble - _frame0) * _setup.ReplaySpeed : Time.timeSinceLevelLoadAsDouble;
            var now = new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = frame };
            if (Driver.Step(now))
            {
                _presentation.Drain();
                if (_music != null) Report();
                _presentation.Present(Driver.Times.FrameSong, Driver.Times.Frontier, Time.deltaTime, _camera);
                if (sim.Now >= _nextDiagnosticClock)
                {
                    _nextDiagnosticClock = sim.Now + 5;
                    var c = _music?.Clock;
                    var sounds = _presentation.Sounds?.Aggregator;
                    var audio = sounds?.Scheduler;
                    _diagnostics?.Sample(true, clock: string.Format(CultureInfo.InvariantCulture,
                        "heard={0:F6} frontier={1:F6} leadMs={2:F1} outputMs={3:F1} inputCalibrationMs={4:F1} videoCalibrationMs={5:F1} displayMs={6:F1} lateEntries={7} droppedKeyboard={8} stalls={9} soundsScheduled={10} soundsLate={11} soundsDropped={12} soundsFolded={13} tier={14} resolution={15}x{16}",
                        Driver.Times.Heard, Driver.Times.Frontier, 1000 * Driver.Times.Lead, 1000 * (c?.OutputLatency ?? 0),
                        1000 * (c?.InputCalibration ?? 0), 1000 * (c?.VideoCalibration ?? 0), 1000 * (c?.DisplayLatency ?? 0), Driver.LateEntries, Keys.Dropped, _music?.Status.Stalls ?? 0, audio?.Scheduled ?? 0, audio?.Late ?? 0, audio?.Dropped ?? 0, sounds?.Folded ?? 0, QualityTiers.Current, Screen.width, Screen.height));
                }
                else _diagnostics?.Sample();
                EndOfRun();
                if (_replay != null && _replay.Done(sim)) FinishReplay();
            }
            _musicTimes?.Measure(Time.unscaledDeltaTime);
        }

        void FeedLive()
        {
            for (int i = 0; i < _music.EntryCount; i++)
            {
                Driver.Add(_music.Entries[i]);
                _fed.Add(_music.Entries[i]);
            }
        }

        /// <summary>
        /// Feeds entries known exactly at stamp + B, polling the plugin until its analysis covers this
        /// frame's frontier (or is done), so the frame never depends on the plugin's timing. The frame's
        /// render position lets the analysis read M past it, past that frontier's stamps; a driven music
        /// that hasn't started yet is rendered as the wait goes. False if the loop finished.
        /// </summary>
        bool FeedSettled()
        {
            double frame = Time.timeSinceLevelLoadAsDouble;
            double needed = Driver.StampNeededFor(FrameClock.FrontierAt(frame));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                _music.RenderTo(FrameClock.SongAt(frame));
                for (int i = 0; i < _music.EntryCount; i++)
                {
                    Driver.AddSettled(_music.Entries[i]);
                    _fed.Add(_music.Entries[i]);
                }
                if (_music.Failed)
                {
                    Finish(1, _music.Error);
                    return false;
                }
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

        /// <summary>The loop's own reading of the events: the beats it logs, the script's eyes, the run's moments.</summary>
        void Take(Simulation sim, in SimEvent e)
        {
            _diagnostics?.Take(e);
            _script?.Take(sim, e);
            switch (e.Kind)
            {
                case SimEventKind.Beat when _music != null:
                    Beat(e);
                    break;
                case SimEventKind.RunStart:
                    Log("Run: play starts at song {0:F3} s", e.T);
                    break;
                case SimEventKind.Pick:
                    Log("Run: picked {0} at run clock {1:F1} s", Card.FromCode(e.I1), sim.State.RunClock);
                    break;
                case SimEventKind.BossArrive:
                    Log("Run: {0} arrives at run clock {1:F1} s, shattering {2}", _config.Enemies.Cast[e.I1].Id, sim.State.RunClock, e.I2);
                    break;
                case SimEventKind.BossDeath:
                    Log("Run: the boss dies at run clock {0:F1} s", sim.State.RunClock);
                    break;
                case SimEventKind.RushStage:
                    var rush = _config.BossRush;
                    Log("Run: boss rush {0}, act {1} ({2}), boss {3} ({4}, fought by {5}), at run clock {6:F1} s, level {7}",
                        ((BossRushStage)e.I0).ToString().ToLowerInvariant(), e.I2 + 1, rush.Acts[e.I2].Name, e.I1 + 1, rush.Bosses[e.I1].Id,
                        _config.Enemies.Cast[rush.Bosses[e.I1].Cast].Id, sim.State.RunClock, sim.State.Level);
                    if ((BossRushStage)e.I0 == BossRushStage.Act && _setup.SaveRush) RushSave.Save(sim.BossRush.Checkpoint);
                    break;
                case SimEventKind.HeroHit when e.D0 > 0:
                    _hurt[(HitSource)e.I1] = _hurt.TryGetValue((HitSource)e.I1, out int n) ? n + 1 : 1;
                    break;
                case SimEventKind.RunEnd:
                    var hurt = new List<string>();
                    foreach (var h in _hurt) hurt.Add($"{h.Key.ToString().ToLowerInvariant()} {h.Value}");
                    Log("Run: {0} at run clock {1:F1} s (song {2:F3} s): {3} kills, level {4}, health {5:F0}, hurt by {6}; presenter faults: {7}",
                        ((RunEnd)e.I0).ToString().ToLowerInvariant(), e.D0, e.T, _presentation.Kills, sim.State.Level, sim.State.Hero.Health,
                        hurt.Count > 0 ? string.Join(", ", hurt) : "nothing", _presentation.Dispatch.FaultReport());
                    break;
            }
        }

        /// <summary>A beat the sim reached: when it falls (heard), and when the sim knew it.</summary>
        void Beat(in SimEvent e)
        {
            double knownAt = Driver.Sim.Stream.KnownAt(EntryKind.Beat, e.I0);
            double lead = e.T - knownAt;
            _leads.Add(lead);
            if (lead <= 0) _beatsLate++;
            Log("Music: beat {0} heard at {1:F3} s, known at {2:F3} s, {3:F3} s ahead; reached on the frame heard at {4:F3} s",
                e.I0, e.T, knownAt, lead, Driver.Times.Heard);
        }

        /// <summary>A death or victory: the session recorded, and the end card once its moment has been shown.</summary>
        void EndOfRun()
        {
            var s = Driver.Sim.State;
            if (s.Phase != RunPhase.Ended || _endShown || _replay != null) return;
            if (s.End != RunEnd.Victory && s.End != RunEnd.Death) return;
            Record();
            var rush = Driver.Sim.BossRush;
            if (rush != null && !_rushSaved && _setup.SaveRush)
            {
                // Quitting at the death card keeps the act, with the death counted; a victory ends the run for good.
                _rushSaved = true;
                if (s.End == RunEnd.Death) RushSave.Save(rush.Retry(death: true));
                else RushSave.Delete();
            }
            if (Driver.Times.FrameSong < s.EndedAt + EndCardAfter) return;
            _endShown = true;
            var summary = new EndSummary { Victory = s.End == RunEnd.Victory, CombatSeconds = s.RunClock, Kills = _presentation.Kills };
            if (rush != null)
            {
                summary.CombatSeconds = rush.TotalTime;
                summary.Rush = new RushSummary { Act = rush.Act, Boss = rush.Boss, Deaths = s.End == RunEnd.Death ? rush.Retry(death: true).Deaths : rush.Deaths };
            }
            if (_setup.BuildIcons != null) summary.Build.AddRange(_setup.BuildIcons(Driver.Sim));
            Log("Run: the {0} card shows at song {1:F3} s", summary.Victory ? "victory" : "death", Driver.Times.FrameSong);
            Ended?.Invoke(summary, Again);
        }

        /// <summary>Again (design.md, Ending): a new run on the same music, from the frontier; the first run's session alone is recorded.</summary>
        public void Again()
        {
            if (_done || Driver == null || _replay != null) return;
            _presentation.Dispose();
            _presentation.Ui?.Dispose();
            Keys.Resume();
            Begin(Driver.Clock, null);
        }

        /// <summary>Retry act (the death card): the act again from its checkpoint, the death counted.</summary>
        public void RetryAct() => RestartRush(Driver?.Sim.BossRush?.Retry(death: Driver.Sim.State.End == RunEnd.Death));

        /// <summary>Restart act (the pause menu): the act again from its checkpoint; the time spent counts, no death does.</summary>
        public void RestartAct() => RestartRush(Driver?.Sim.BossRush?.Retry(death: false));

        /// <summary>New run (the end card): the Boss Rush from its first boss with the plain start.</summary>
        public void NewRush() => RestartRush(null);

        /// <summary>Abandon run (the pause menu): the saved checkpoint goes; the flow ends the run.</summary>
        public void AbandonRush()
        {
            if (_setup.SaveRush) RushSave.Delete();
        }

        void RestartRush(BossRushCheckpoint from)
        {
            if (_done || Driver == null || _replay != null || !Rush) return;
            if (Driver.Sim.State.Phase != RunPhase.Ended)
            {
                Driver.Sim.Control(RunControl.Quit, Driver.Times.Heard);
                Record();
            }
            _run.RushStart = from;
            _rushSaved = false;
            Again();
        }

        /// <summary>The first run's session into `--record`'s folder, beside the plugin's record of its music, once.</summary>
        void Record()
        {
            if (_recorded || _runs != 1 || _setup.RecordDir == null || _replay != null) return;
            _recorded = true;
            var record = SessionRecord.Of(_run, Driver.Sim);
            record.Build = Application.version;
            record.Platform = SystemInfo.operatingSystem;
            record.Source = _setup.Source?.Tag ?? "";
            record.Flags = _setup.Flags;
            record.Write(_setup.RecordDir);
            Log("Session: recorded to {0}: {1} entries, {2} inputs to song {3:F3} s, hash {4:X16} ({5} events)", _setup.RecordDir,
                record.Entries.Length, record.Inputs.Length, record.End, record.Hash, record.Events);
        }

        void FinishReplay()
        {
            var sim = Driver.Sim;
            var record = _replay.Record;
            bool matches = _replay.Matches(sim);
            bool sameRuntime = record.Runtime == SessionRecord.CurrentRuntime;
            Log("Replay: {0}: hash {1:X16} ({2} events), recorded {3:X16} ({4} events) in {5}: {6}; presenter faults: {7}", _setup.ReplayDir, sim.Hash,
                sim.EventCount, record.Hash, record.Events, record.Runtime, matches ? "matches" : "DIFFERS", _presentation.Dispatch.FaultReport());
            Finish(matches ? 0 : 1, matches ? "the replay matches" : sameRuntime ? "the replay differs" : $"the replay differs from {record.Runtime}'s");
        }

        /// <summary>The delay once the music plays, and a status line every <see cref="StatusEvery"/> s.</summary>
        void Report()
        {
            var s = _music.Status;
            var t = Driver.Times;
            if (s.State == Plugin.Playing)
            {
                _delaySum += s.Analysed - t.Song;
                _delaySamples++;
            }
            if (!_playingNoted && s.State == Plugin.Playing)
            {
                _playingNoted = true;
                Log("Music: playing after {0:F2} s", Time.realtimeSinceStartupAsDouble - _openedAt);
            }
            if (t.FrameSong < _nextStatus) return;
            _nextStatus = t.FrameSong + StatusEvery;
            var sim = Driver.Sim;
            Log("Music: song {0:F2} s, heard {1:F2} s, analysed {2:F2} s ({3:F2} s ahead), {4}, {5} beats known, lead {6:F0} ms, stalls {7}; " +
                "run clock {8:F1} s, {9} alive, level {10}, health {11:F0}",
                t.Song, t.Heard, s.Analysed, s.Analysed - t.Song, StateName(s.State), sim.Stream.Count(EntryKind.Beat),
                1000 * t.Lead, s.Stalls, sim.State.RunClock, sim.Enemies.Alive, sim.State.Level, sim.State.Hero.Health);
        }

        static string StateName(int state) => state switch
        {
            Plugin.Waiting => "waiting",
            Plugin.Playing => "playing",
            Plugin.Ended => "ended",
            Plugin.Stopped => "stopped",
            _ => "state " + state,
        };

        /// <summary>The pause menu opened (Esc or the focus lost): the keys let go and the world held, the music playing on.</summary>
        public void Pause()
        {
            if (Driver == null || _done || Scripted) return;
            double heard = Driver.Times.Heard;
            Keys.Suspend(Driver.Sim, Driver.Clock, heard);
            Driver.Sim.Control(RunControl.Pause, heard);
        }

        /// <summary>The pause menu closed: play goes on, keys counting from their next press.</summary>
        public void Resume()
        {
            if (Driver == null || _done || Scripted) return;
            Driver.Sim.Control(RunControl.Resume, Driver.Times.Heard);
            Keys.Resume();
        }

        /// <summary>End run: the run ends, the music closes and the loop goes.</summary>
        public void End()
        {
            if (Driver != null && !_done && _replay == null)
            {
                Driver.Sim.Control(RunControl.Quit, Driver.Times.Heard);
                Record();
            }
            _done = true;
            Summarise();
            Destroy(_musicHost);
            Destroy(this);
        }

        void Finish(int code, string why)
        {
            if (_done) return;
            _done = true;
            if (code != 0) Debug.LogError("GameLoop: " + why);
            _finished?.Invoke(code, why);
        }

        void Summarise()
        {
            if (_summarised || Driver == null || _music == null) return;
            _summarised = true;
            _leads.Sort();
            string leads = _leads.Count == 0 ? "no beats" : string.Format(CultureInfo.InvariantCulture,
                "{0} beats, known {1:F3} to {2:F3} s (median {3:F3} s) before they were heard, {4} not ahead",
                _leads.Count, _leads[0], _leads[_leads.Count - 1], _leads[_leads.Count / 2], _beatsLate);
            Log("Music: {0}; the speakers {1:F3} s behind the analyzer on average; {2} stalls; {3} late entries; {4} frames, {5} held",
                leads, _delaySamples > 0 ? _delaySum / _delaySamples : double.NaN, _music.Status.Stalls, Driver.LateEntries,
                Driver.Frames, _music.Clock.HeldFrames);
        }

        void OnApplicationQuit()
        {
            if (Driver != null && !_done && _replay == null) Record();
            Summarise();
            _diagnostics?.Dispose();
        }

        void OnDestroy()
        {
            Summarise();
            _diagnostics?.Dispose();
            Keys.Dispose();
            Pointer.Dispose();
            _presentation?.Dispose();
            _presentation?.Ui?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
