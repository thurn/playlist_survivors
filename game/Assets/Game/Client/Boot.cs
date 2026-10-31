using UnityEngine;

namespace Game.Client
{
    /// <summary>
    /// The boot scene's driver. Player flags:
    /// - `--fixed-time`: time advances exactly 1/60 s per frame, however long a frame takes;
    /// - `--capture-stills DIR --stills-at t1,t2,...`: windowless stills (<see cref="Stills"/>),
    ///   quitting once the last is kept;
    /// - `--seconds N`: quits after N seconds.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const int FixedFps = 60;

        [SerializeField] Camera _camera;
        Stills _stills;
        float _seconds;

        void Start()
        {
            if (Args.Has("--fixed-time")) Time.captureFramerate = FixedFps;
            _seconds = Args.GetFloat("--seconds", 0f);
            var dir = Args.Get("--capture-stills");
            if (dir != null) _stills = new Stills(_camera, dir, Args.Get("--stills-at", "0"));
            Debug.Log($"Boot: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, batch mode {Application.isBatchMode}");
        }

        void Update()
        {
            double t = Time.timeSinceLevelLoadAsDouble;
            if (_stills != null)
            {
                _stills.CaptureDue(t);
                if (_stills.Done) Quit($"stills done at t={t:F3}");
            }
            if (_seconds > 0f && t >= _seconds) Quit($"--seconds {_seconds} reached");
        }

        void OnDestroy() => _stills?.Dispose();

        static void Quit(string why)
        {
            Debug.Log("Boot: quitting, " + why);
            Application.Quit(0);
        }
    }
}
