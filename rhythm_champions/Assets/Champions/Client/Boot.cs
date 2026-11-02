using System;
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
    /// - `--seconds N`: quits after N seconds.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const int FixedFps = 60;

        [SerializeField] Camera _camera;
        LaunchOptions _options;
        Stills _stills;

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
            if (_options.Headless) AudioListener.volume = 0f;
            if (_options.StillsDir != null) _stills = new Stills(_camera, _options.StillsDir, _options.StillsAt);
            Debug.Log($"Boot: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, batch mode {Application.isBatchMode}" +
                      $"{(_options.FixedTime ? ", fixed time" : "")}{(_options.Headless ? ", headless" : "")}");
        }

        void Update()
        {
            if (_options == null) return;
            double t = Time.timeAsDouble;
            if (_stills != null)
            {
                _stills.CaptureDue(t);
                if (_stills.Done) Quit($"stills done at t={t:F3}");
            }
            if (_options.Seconds > 0 && t >= _options.Seconds) Quit($"--seconds {_options.Seconds} reached");
        }

        void Quit(string why)
        {
            Debug.Log("Boot: quitting, " + why);
            enabled = false;
            Application.Quit(0);
        }

        void OnDestroy() => _stills?.Dispose();
    }
}
