using System;
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
    /// - `--capture-stills DIR --stills-at t1,t2,...`: windowless stills (<see cref="Stills"/>), quitting
    ///   once the last is kept; `--stills-scenes NAMES` instead plays the scripted scenes (<see cref="StillScenes"/>)
    ///   for their stills;
    /// - `--seconds N`: quits after N seconds;
    /// - `--file PATH` or `--app NAME`: plays that music 3 s late through the music plugin, its entries
    ///   feeding the sim (<see cref="GameLoop"/>) with the run's UI (<see cref="RunUi"/>); a source that can't play quits with 2;
    /// - `--calibration FILE`: the calibrations' file (Calibration.CalibrationStore);
    /// - `--gallery`: the weapon effects' gallery in a window, every effect in turn (<see cref="GalleryScene"/>); with
    ///   `--capture-stills DIR`, its stills as a scripted scene;
    /// - `--look trailer`: the monsters as the neon edge trailer drew them (the still scenes and the bench take it);
    /// - `--crowd-bench N`: the crowd's timing scene (<see cref="CrowdBench"/>), with its own world; `--bench-out PATH`
    ///   for its timings;
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
        Flow _flow;
        Palette _palette;
        GameContent _content;
        CardFaces _faces;
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
            if (_options.FixedTime) Time.captureFramerate = FixedFps;
            else if (_options.Headless) Application.targetFrameRate = FixedFps;
            // A headless run's music mutes the final mix instead (GameLoop), so its filters still run.
            if (_options.Headless && _options.File == null && _options.App == null) AudioListener.volume = 0f;
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
            if (_options.File != null || _options.App != null)
            {
                var source = ResolveMusic(_options, out string error);
                if (source == null)
                {
                    Debug.LogError("Boot: " + error);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
                GameLoop.Begin(gameObject, _camera, source, _options.FixedTime, mute: _options.Headless, _world, _palette != null ? MakeRunUi() : null, Finished);
                _driven = true;
            }
            else if (_world != null) _flow = Flow.Begin(gameObject, _camera, _world, MakeRunUi, ContentDir(), _options.FixedTime, _options.Headless);
        }

        RunUi MakeRunUi() => new RunUi(transform, _palette, _content, _faces, _camera.gameObject.layer);

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
            Debug.Log("Boot: quitting, " + why);
            enabled = false;
            Application.Quit(0);
        }

        void OnDestroy()
        {
            _bench?.Report(); // a windowed run the player quit
            _bench?.Dispose();
            _stills?.Dispose();
            _world?.Dispose();
            if (_gallery != null) Destroy(_gallery.gameObject);
        }
    }
}
