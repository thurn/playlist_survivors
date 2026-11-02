using System;
using System.IO;
using Champions.Client.Bench;
using Champions.Client.Loop;
using Champions.Client.Music;
using Champions.Render;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The Boot scene's driver, on the command line's flags (<see cref="LaunchOptions"/>). For now the game is
    /// the black void its camera clears to:
    /// - `--fixed-time`: time advances exactly 1/60 s per frame, however long a frame takes;
    /// - `--headless` (or Unity's batch mode): the output silenced, frames paced at 60 per second unless fixed;
    /// - `--capture-stills DIR --stills-at t1,t2,...`: windowless stills (<see cref="Stills"/>), quitting
    ///   once the last is kept;
    /// - `--seconds N`: quits after N seconds;
    /// - `--file PATH` or `--app NAME`: plays that music 3 s late through the music plugin, its entries
    ///   feeding the sim (<see cref="GameLoop"/>); a source that can't play quits with 2;
    /// - `--edge-crowd N`: the edge crowd's test scene (<see cref="EdgeCrowdBench"/>); without a baked cut it quits with 2.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const int FixedFps = 60;

        [SerializeField] Camera _camera;
        LaunchOptions _options;
        Stills _stills;
        EdgeCrowdBench _bench;

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
            if (_options.StillsDir != null) _stills = new Stills(_camera, _options.StillsDir, _options.StillsAt);
            Debug.Log($"Boot: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, batch mode {Application.isBatchMode}" +
                      $"{(_options.FixedTime ? ", fixed time" : "")}{(_options.Headless ? ", headless" : "")}");
            if (_options.EdgeCrowd > 0)
            {
                _bench = EdgeCrowdBench.Start(_camera, _options.EdgeCrowd, _options.EdgeType, _options.BenchOut, out string why);
                if (_bench == null)
                {
                    Debug.LogError("Boot: --edge-crowd: " + why);
                    enabled = false;
                    Application.Quit(2);
                    return;
                }
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
                GameLoop.Begin(gameObject, _camera != null ? _camera.gameObject : gameObject, source, _options.FixedTime,
                    mute: _options.Headless, Finished);
            }
        }

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
            _bench?.Frame(t);
            if (_stills != null)
            {
                if (_bench != null && _stills.IsDue(t)) _bench.Requeue();
                _stills.CaptureDue(t);
                if (_stills.Done) Quit($"stills done at t={t:F3}");
            }
            if (_options.Seconds > 0 && t >= _options.Seconds) Quit($"--seconds {_options.Seconds} reached");
        }

        void Quit(string why)
        {
            _bench?.Report();
            Debug.Log("Boot: quitting, " + why);
            enabled = false;
            Application.Quit(0);
        }

        void OnDestroy()
        {
            _stills?.Dispose();
            _bench?.Report(); // a windowed run the player quit
            _bench?.Dispose();
        }
    }
}
