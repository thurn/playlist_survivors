using System;
using System.Collections.Generic;
using System.IO;
using Champions.Client.Bench;
using Champions.Client.Loop;
using Champions.Client.Music;
using Champions.Client.Scenes;
using Champions.Client.UI;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The Boot scene's driver, on the command line's flags (<see cref="LaunchOptions"/>). The game is the world
    /// (<see cref="WorldPresenter"/>): the black void with its grid, the hero's feet at the origin until a run moves
    /// them; without music on the command line the title and its menus lead to a run (<see cref="Flow"/>):
    /// - `--tier low|medium|high`: the quality tier, applied first (<see cref="QualityTiers"/>);
    /// - `--fixed-time`: time advances exactly 1/60 s per frame, however long a frame takes;
    /// - `--headless` (or Unity's batch mode): the output silenced, frames paced at 60 per second unless fixed;
    /// - `--mute`: silence the output in a window too, while music continues to drive the run;
    /// - `--capture-stills DIR --stills-at t1,t2,...`: windowless stills (<see cref="Stills"/>), quitting
    ///   once the last is kept; `--stills-scenes NAMES` instead plays the scripted scenes (<see cref="StillScenes"/>)
    ///   for their stills;
    /// - `--seconds N`: quits after N seconds;
    /// - `--file PATH` or `--app NAME`: level 1 straight away on that music, played 3 s late through the music plugin, its
    ///   entries feeding the sim (<see cref="GameLoop"/>) through the run's flow (<see cref="Flow.BeginRun"/>: pause, the end
    ///   card, Again); a source that can't play quits with 2;
    /// - `--mode bossrush`: the Boss Rush (boss-rush.md): the title's Boss Rush entry, or with music its run straight away;
    ///   `--boss N` starts it at boss N with that point's scripted reference build (never saved over the act checkpoint);
    /// - `--seed N`: the run's seed (a scripted run's defaults to 1, a played one's to the clock);
    /// - `--walk idle|reference|KEYS` and `--picks 1,2,..|reference`: the scripted player (<see cref="ScriptedPlayer"/>);
    /// - `--record DIR`: the first run's session recorded into DIR (the plugin's entries and audio, then its inputs);
    /// - `--replay DIR[,DIR...]`: each recorded session played again through the presenters with no music, at
    ///   `--replay-speed X` (default 1); quits with 0 when every replay settled its recorded hash, else 1 (2: unreadable);
    /// - `--calibration FILE`: the calibrations' file (Calibration.CalibrationStore);
    /// - `--gallery`: the weapon effects' gallery in a window, every effect in turn (<see cref="GalleryScene"/>); with
    ///   `--capture-stills DIR`, its stills as a scripted scene;
    /// - `--look trailer`: the monsters as the neon edge trailer drew them (the still scenes and the bench take it);
    /// - `--crowd-bench N`: the crowd's timing scene (<see cref="CrowdBench"/>), with its own world; `--bench-out PATH`
    ///   for its timings;
    /// - `--perf-peak`: level 1 held at 1,000 alive and timed (<see cref="PerfPeak"/>, `champions perf`), on Boot's world;
    ///   `--bench-out PATH` for its timings;
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const int FixedFps = 60;

        [SerializeField] Camera _camera;
        LaunchOptions _options;
        Stills _stills;
        StillScenes _scenes;
        WorldPresenter _world;
        GalleryDriver _gallery;
        CrowdBench _bench;
        PerfPeak _peak;
        Flow _flow;
        Palette _palette;
        GameContent _content;
        CardFaces _faces;
        GameLoop _replayLoop;
        int _replayed, _replayCode;
        bool _driven;

        void Start()
        {
            try { _options = LaunchOptions.Current; }
            catch (ArgumentException e)
            {
                Debug.LogError("Boot: " + e.Message);
                Application.Quit(2);
                return;
            }
            foreach (var note in _options.Unrecognized) Debug.LogWarning("Boot: " + note);
            // A replay is fixed time: its frames run as fast as they draw.
            if (_options.FixedTime || _options.Replay.Length > 0) Time.captureFramerate = FixedFps;
            else if (_options.Headless) Application.targetFrameRate = FixedFps;
            // A headless run's music mutes the final mix instead (GameLoop), so its filters still run.
            if ((_options.Headless || _options.Mute) && _options.File == null && _options.App == null) AudioListener.volume = 0f;
            // The tier first: the stills' target takes the pipeline's MSAA.
            QualityTiers.Apply(_options.Tier ?? QualityTiers.Default());
            // The still scenes, the gallery and the bench place the camera themselves: no world of Boot's under them.
            if (_options.StillsScenes.Length == 0 && !_options.Gallery && _options.CrowdBench <= 0)
            {
                try
                {
                    string dir = ContentDir();
                    _palette = Palette.Load(dir);
                    _content = GameContent.Load(dir);
                    _faces = CardFaces.Load(dir, _content);
                    _world = new WorldPresenter(_camera, _palette);
                }
                catch (ContentException e)
                {
                    Debug.LogError("Boot: " + e.Message);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
            }
            if (_options.StillsDir != null) _stills = new Stills(_camera, _options.StillsDir, _options.StillsAt);
            if (_options.StillsScenes.Length > 0)
            {
                try { _scenes = new StillScenes(_stills, _camera, _options.StillsScenes, ContentDir(), _options.Look); }
                catch (Exception e) when (e is ArgumentException || e is ContentException)
                {
                    Debug.LogError("Boot: " + e.Message);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
            }
            Debug.Log($"Boot: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, batch mode {Application.isBatchMode}" +
                      $"{(_options.FixedTime ? ", fixed time" : "")}{(_options.Headless ? ", headless" : "")}");
            if (_options.Gallery && _scenes == null)
            {
                try { _gallery = GalleryScene.Play(_camera, Palette.Load(ContentDir()), ContentDir()); }
                catch (ContentException e)
                {
                    Debug.LogError("Boot: " + e.Message);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
            }
            if (_options.CrowdBench > 0)
            {
                _bench = CrowdBench.Start(_camera, _options.CrowdBench, _options.Look, Palette.Load(ContentDir()), _options.BenchOut, out string why);
                if (_bench == null)
                {
                    Debug.LogError("Boot: --crowd-bench: " + why);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
                if (_stills != null) _stills.Requeue = _bench.Requeue;
            }
            if (_options.PerfPeak && _world != null)
            {
                _driven = true;
                _peak = new PerfPeak(_camera, _content, ContentDir(), _palette, _world, MakeRunUi(), _options.Look, _options.BenchOut);
            }
            else if (_options.Replay.Length > 0 && _world != null)
            {
                _driven = true;
                NextReplay();
            }
            else if (_options.File != null || _options.App != null)
            {
                var source = ResolveMusic(_options, out string error);
                if (source == null)
                {
                    Debug.LogError("Boot: " + error);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
                RunMode mode;
                try { mode = LaunchMode(); }
                catch (ArgumentOutOfRangeException e)
                {
                    Debug.LogError("Boot: --boss: " + e.Message);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
                _flow = Flow.BeginRun(gameObject, _camera, _world, MakeSetup, ContentDir(), _options.FixedTime, source, mode);
            }
            else if (_world != null) _flow = Flow.Begin(gameObject, _camera, _world, MakeSetup, ContentDir(), _options.FixedTime, _content.BossRush, _options.BossRush);
            if (_stills != null && _flow != null) _stills.Requeue = () => _flow.Run?.Presentation?.Queue(_camera);
        }

        RunUi MakeRunUi() => new RunUi(transform, _palette, _content, _faces, _camera.gameObject.layer);

        /// <summary>The command line's run with music: the main game, or the Boss Rush new or from `--boss N`'s reference.</summary>
        RunMode LaunchMode()
        {
            if (!_options.BossRush) return RunMode.Main;
            if (_options.Boss == 0) return new RunMode { BossRush = true, Save = true };
            return new RunMode { BossRush = true, From = _content.BossRush.ReferenceCheckpoint(_options.Boss - 1, _content.Combat) };
        }

        /// <summary>
        /// A run's setup on <paramref name="source"/> in <paramref name="mode"/>: level 1 (or the Boss Rush) on the flags' seed,
        /// scripts and record. A scripted Boss Rush never touches the saved act checkpoint.
        /// </summary>
        RunSetup MakeSetup(MusicSource source, RunMode mode)
        {
            bool scripted = _options.Walk != null || _options.Picks != null;
            ulong seed = _options.Seed ?? (scripted ? 1UL : (ulong)DateTime.UtcNow.Ticks);
            return new RunSetup
            {
                Source = source, FixedTime = _options.FixedTime, Mute = _options.Headless || _options.Mute, World = _world, MakeUi = MakeRunUi,
                Content = _content, ContentDir = ContentDir(), Palette = _palette,
                // `--fight ID`: the boss's fight alone, no level (no horde, no structures).
                Run = mode.BossRush
                    ? new RunInputs { Seed = seed, BossRush = true, SurvivorWeapons = true, Start = RunStart.Immediately, RushStart = mode.From?.Clone() }
                    : new RunInputs
                    {
                        Seed = seed, Level = _options.Fight == null, SurvivorWeapons = true, Start = RunStart.Immediately,
                        Fight = _options.Fight, FightAct = _options.FightAct,
                    },
                SaveRush = mode.BossRush && mode.Save && !scripted,
                Walk = _options.Walk, Picks = _options.Picks, RecordDir = _options.Record, Flags = Environment.GetCommandLineArgs(), Look = _options.Look,
                BuildIcons = BuildIcons,
            };
        }

        /// <summary>The survivor build's weapon and support icons for the end card.</summary>
        List<Icon> BuildIcons(Simulation sim)
        {
            var icons = new List<Icon>();
            var build = sim.State.Build;
            for (int k = 0; k < CombatRules.WeaponCount; k++)
                if (build.Level((WeaponKind)k) > 0) icons.Add(_faces.Of(Card.Of((WeaponKind)k, build.Level((WeaponKind)k), 0)).Icon);
            for (int k = 0; k < CombatRules.SupportCount; k++)
                if (build.Level((SupportKind)k) > 0) icons.Add(_faces.Of(Card.Of((SupportKind)k, build.Level((SupportKind)k), 0)).Icon);
            icons.RemoveAll(i => i == null);
            return icons;
        }

        /// <summary>`--replay`'s next session, on a fresh world; the player quits with the worst code once all are done.</summary>
        void NextReplay()
        {
            if (_replayed >= _options.Replay.Length)
            {
                Finished(_replayCode, $"{_options.Replay.Length} replays done");
                return;
            }
            string dir = Path.GetFullPath(_options.Replay[_replayed++]);
            SessionRecord record;
            try { record = SessionRecord.Read(dir); }
            catch (Exception e) when (e is IOException || e is FormatException)
            {
                Debug.LogError($"Boot: --replay {dir}: {e.Message}");
                _replayCode = Math.Max(_replayCode, 2);
                NextReplay();
                return;
            }
            if (_replayLoop != null) Destroy(_replayLoop);
            _world.Dispose();
            _world = new WorldPresenter(_camera, _palette);
            _replayLoop = GameLoop.Begin(gameObject, _camera, new RunSetup
            {
                Replay = record, ReplayDir = dir, ReplaySpeed = _options.ReplaySpeed, Mute = true, World = _world, MakeUi = MakeRunUi, Look = _options.Look,
                Content = _content, ContentDir = ContentDir(), Palette = _palette,
            }, (code, why) =>
            {
                _replayCode = Math.Max(_replayCode, code);
                NextReplay();
            });
            if (_stills != null) _stills.Requeue = () => _replayLoop?.Presentation?.Queue(_camera);
        }

        /// <summary>rhythm_champions/Content, above the player (a build inside the checkout) or the working directory.</summary>
        static string ContentDir() =>
            GameContent.Find(Application.dataPath) ?? GameContent.Find(Environment.CurrentDirectory)
            ?? throw new ContentException($"no rhythm_champions/Content above {Application.dataPath} or {Environment.CurrentDirectory}");

        /// <summary>`--file`'s file (which must exist) or `--app`'s app (which must be producing audio); null with why if neither.</summary>
        static MusicSource ResolveMusic(LaunchOptions options, out string error)
        {
            if (options.App != null) return MusicSource.FindApp(options.App, out error);
            string path = Path.GetFullPath(options.File);
            error = File.Exists(path) ? null : $"--file {options.File}: no file at {path}";
            return error == null ? MusicSource.File(path) : null;
        }

        void Finished(int code, string why)
        {
            Debug.Log($"Boot: quitting with {code}, {why}");
            enabled = false;
            Application.Quit(code);
        }

        void Update()
        {
            if (_options == null) return;
            double t = Time.timeAsDouble;
            // Without a run or a scene, the world stands still around the hero's spot.
            if (!_driven && (_flow == null || !_flow.Running)) _world?.Present(t, Vector2.zero, Time.deltaTime);
            _bench?.Frame(t);
            _peak?.Step();
            if (_scenes != null)
            {
                _scenes.Update(t);
                if (_scenes.Done) Quit($"still scenes done at t={t:F3}");
            }
            else if (_stills != null)
            {
                _stills.CaptureDue(t);
                if (_stills.Done) Quit($"stills done at t={t:F3}");
            }
            if (_options.Seconds > 0 && t >= _options.Seconds) Quit($"--seconds {_options.Seconds} reached");
        }

        void Quit(string why)
        {
            _bench?.Report();
            _bench = null;
            _peak?.Report();
            _peak?.Dispose();
            _peak = null;
            Debug.Log("Boot: quitting, " + why);
            enabled = false;
            Application.Quit(0);
        }

        void OnDestroy()
        {
            _bench?.Report(); // a windowed run the player quit
            _bench?.Dispose();
            _peak?.Dispose();
            _stills?.Dispose();
            _world?.Dispose();
            if (_gallery != null) Destroy(_gallery.gameObject);
        }
    }
}
