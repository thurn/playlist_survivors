using Game.Client.Calibration;
using Game.Client.Loop;
using Game.Client.Music;
using Game.Client.Title;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client
{
    /// <summary>
    /// The boot scene's driver. Player flags:
    /// - `--fixed-time`: time advances exactly 1/60 s per frame, however long a frame takes;
    /// - `--capture-stills DIR --stills-at t1,t2,...`: windowless stills (<see cref="Stills"/>),
    ///   quitting once the last is kept, each with its ID render under `--capture-mask`;
    /// - `--seconds N`: quits after N seconds;
    /// - `--dsp-buffer N`: Unity's DSP buffer length in frames, in place of the project's;
    /// - `--spike` or `--spike-out DIR` with `--file &lt;audio or .m3u&gt;`: the sync spike, the music
    ///   with a click on every beat (<see cref="SyncSpike"/>, which reads `--headless` too);
    /// - `--arena`: the baked arena alone in the game camera's framing (<see cref="Arena.ArenaStage"/>);
    /// - `--shatter-bench OUT.json`: the GPU shatter's bench (<see cref="Crowd.ShatterBench"/>);
    /// - `--calibrate video|input`: that calibration screen first (<see cref="CalibrationScreen"/>),
    ///   then the game as below once it closes.
    /// Otherwise it runs the game's frame loop (<see cref="GameLoop"/>, with `--seed`, `--events`,
    /// `--overlay`, `--headless` and `--loop-out`) on its music:
    /// - `--file &lt;audio or .m3u&gt;`: the file;
    /// - `--app &lt;name&gt;`: the app producing audio now that the name picks, tapped muted and played
    ///   M late (Delayed listening; <see cref="MusicSource.Pick"/>, a number is a pid). The player
    ///   needs the System Audio Recording grant to hear it (docs/design/unity-game.md, Platform
    ///   Capture and Crash Safety); without it the app is muted and the music silent;
    /// - `--replay DIR`: a recorded session played again, its recorded music and inputs, checking
    ///   its hash at the end (<see cref="GameLoop"/>); it quits then, with 1 if the hash differs;
    /// - neither, in a window (or with `--title`): the title screen's pick (<see cref="TitleScreen"/>).
    ///   That run's end returns to the title screen, which says why it ended, and where W watches it again;
    /// - neither, windowless: no music, on frame time.
    /// With `--watch`, a recorded run is replayed once it ends (<see cref="Sessions"/>), and `--seconds N`
    /// ends that run rather than the launch.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        public const int FixedFps = 60;

        [SerializeField] Camera _camera;
        Stills _stills;
        float _seconds;
        GameLoop _loop;
        bool _watching;

        void Start()
        {
            if (Args.Has("--fixed-time")) Time.captureFramerate = FixedFps;
            _seconds = Args.GetFloat("--seconds", 0f);
            var dir = Args.Get("--capture-stills");
            if (dir != null) _stills = new Stills(_camera, dir, Args.Get("--stills-at", "0"), Args.Has("--capture-mask"));
            int dspBuffer = Args.GetInt("--dsp-buffer", 0);
            if (dspBuffer > 0)
            {
                var audio = AudioSettings.GetConfiguration();
                audio.dspBufferSize = dspBuffer;
                AudioSettings.Reset(audio);
            }
            var file = Args.Get("--file");
            if (file != null && (Args.Has("--spike") || Args.Has("--spike-out"))) SyncSpike.Begin(gameObject, _camera, file, _seconds);
            else if (Args.Has("--arena")) Arena.ArenaStage.Begin(gameObject, _camera);
            else if (Args.Has("--shatter-bench")) Crowd.ShatterBench.Begin(gameObject, _camera, Args.Get("--shatter-bench"));
            else if (CalibrationScreen.Wanted is CalibrationScreen.Mode mode) CalibrationScreen.Begin(gameObject, mode, StartGame);
            else StartGame();
            Debug.Log($"Boot: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, batch mode {Application.isBatchMode}");
        }

        void StartGame()
        {
            if (Sessions.WatchNext != null)
            {
                // --watch: the run just recorded, again; the launch quits when it ends.
                _watching = true;
                GameLoop.Begin(gameObject, _camera, MusicSource.Replay(Sessions.WatchNext));
                Sessions.WatchNext = null;
                return;
            }
            var replay = Args.Get("--replay");
            if (replay != null)
            {
                GameLoop.Begin(gameObject, _camera, MusicSource.Replay(Args.Path(replay)));
                return;
            }
            var app = Args.Get("--app");
            var file = Args.Get("--file");
            if (app != null)
            {
                var source = MusicSource.FindApp(app, out string error);
                if (source == null)
                {
                    Debug.LogError("Boot: --app: " + error);
                    Application.Quit(1);
                    return;
                }
                _loop = GameLoop.Begin(gameObject, _camera, source);
            }
            else if (file != null) _loop = GameLoop.Begin(gameObject, _camera, MusicSource.File(Args.Path(file)));
            else if (TitleScreen.Wanted)
                TitleScreen.Begin(gameObject, source => GameLoop.Begin(gameObject, _camera, source, ReturnToTitle));
            else GameLoop.Begin(gameObject, _camera, null);
        }

        /// <summary>A run started from the title screen ended: the boot scene again, alone, on the title screen.</summary>
        void ReturnToTitle(string why)
        {
            Debug.Log("Boot: back to the title screen, " + why);
            TitleScreen.LastEnd = why;
            SceneManager.LoadScene(gameObject.scene.name, LoadSceneMode.Single);
        }

        void Update()
        {
            double t = Time.timeSinceLevelLoadAsDouble;
            if (_stills != null)
            {
                _stills.CaptureDue(t);
                if (_stills.Done) Quit($"stills done at t={t:F3}");
            }
            // Since launch: a return to the title screen reloads the scene. Under --watch it ends the
            // run, and its replay plays to its end.
            if (_seconds > 0f && Time.timeAsDouble >= _seconds && !_watching)
            {
                if (_loop != null && Args.Has("--watch"))
                {
                    _loop.End($"--seconds {_seconds} reached");
                    _loop = null;
                }
                else if (!Args.Has("--watch")) Quit($"--seconds {_seconds} reached");
            }
        }

        void OnDestroy() => _stills?.Dispose();

        static void Quit(string why)
        {
            Debug.Log("Boot: quitting, " + why);
            Application.Quit(0);
        }
    }
}
