using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The demo player (PX5): plays the song's timeline (SongPlayback) at the song clock's time,
    /// with the music and the development flags (DevFlags). It starts itself in a player unless
    /// the timing spike (`--frame-timing`) or the test card (`--test-card`) owns the run.
    ///
    /// Stills (`--capture-stills &lt;dir&gt; --stills-at s,...`, fixed time) are drawn by hand
    /// through the same pipeline at 1920×1080 (a batch-mode player draws no frames of its own),
    /// each on a frame set to exactly its song time. The frame is set in Update and the still
    /// drawn in LateUpdate, after every other script's: the 2D lights refresh their culling
    /// bounds in their own LateUpdate, so a still drawn earlier would light a moved light (the
    /// hero's, after a seek) from where it was.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class DemoMain : MonoBehaviour
    {
        public const int StillW = 1920, StillH = 1080;

        DevFlags _flags;
        SongPlayback _playback;
        SongClock _clock;
        RenderTexture _still;
        readonly List<double> _stillsAt = new List<double>();
        double _endAt;
        bool _quitNext, _stillNow;
        int _frames;

        public double SongTime { get; private set; }

        public static bool Runs => !Application.isEditor && !Args.Has("--frame-timing") && !Args.Has("--test-card");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Runs) new GameObject("DemoMain").AddComponent<DemoMain>();
        }

        void Start()
        {
            _flags = DevFlags.FromCommandLine();
            QualitySettings.vSyncCount = _flags.FixedTime ? 0 : 1;
            Application.targetFrameRate = -1;
            // The boot scene's camera and test card would draw too; its moonlight stays.
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include)) Destroy(c.gameObject);
            foreach (var card in FindObjectsByType<TestCard>(FindObjectsInactive.Include)) Destroy(card.gameObject);

            var data = PlaybackData.Load();
            _playback = new SongPlayback(data, _flags.Zoom, transform) { Feel = !_flags.NoFeel };
            var tl = _playback.Timeline;
            Debug.Log($"DemoMain: {tl.Name} built in {_playback.BuildSeconds:F2} s, {tl.EntityCount} entities; " +
                      $"target {_playback.Pipeline.TargetW}x{_playback.Pipeline.TargetH} ({_flags.Zoom} zoom)");
            _endAt = _flags.Seconds > 0 ? _flags.StartAt + _flags.Seconds : tl.Duration;

            if (_flags.CaptureStills != null)
            {
                foreach (var s in (_flags.StillsAt ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    _stillsAt.Add(double.Parse(s, CultureInfo.InvariantCulture));
                _stillsAt.Sort();
                _stillsAt.RemoveAll(s => s < _flags.StartAt);
                _still = new RenderTexture(StillW, StillH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Still" };
            }

            _clock = new SongClock(_flags.FixedTime, _flags.StartAt);
            var music = gameObject.AddComponent<AudioSource>();
            AudioSource click = null;
            AudioClip clickClip = null;
            if (_flags.Click)
            {
                click = gameObject.AddComponent<AudioSource>();
                clickClip = ClickTrack.Build(tl.Map);
            }
            if (data.music == null && !_flags.FixedTime) Debug.LogWarning("DemoMain: no music baked (run demo2d setup); playing silent");
            _clock.Start(music, data.music, 0.5, click, clickClip);
        }

        void Update()
        {
            if (_playback == null) return;
            try { Frame(); }
            catch (Exception e)
            {
                // A broken frame never recovers; end the run instead of spinning on it.
                Debug.LogException(e);
                Quit(1);
            }
        }

        void Frame()
        {
            if (_quitNext) { Quit(0); return; }
            double t = _flags.FreezeAt >= 0 ? _flags.FreezeAt : _clock.Now();
            // A frame with a still due shows exactly the still's time, so a seek's still and a
            // run's agree; the clock carries on from its own frames.
            _stillNow = _stillsAt.Count > 0 && _stillsAt[0] <= t;
            if (_stillNow) t = _stillsAt[0];
            SongTime = t;
            bool screen = !Application.isBatchMode && !_stillNow;
            _playback.Frame(t, screen ? Screen.width : StillW, screen ? Screen.height : StillH);
            _clock.EndFrame();
            _frames++;
        }

        void LateUpdate()
        {
            if (_playback == null || !enabled) return;
            if (_stillNow)
            {
                try
                {
                    _playback.Pipeline.Render(_still);
                    StillCapture.Write(_still, _flags.CaptureStills, _stillsAt[0]);
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "DemoMain: still at {0:F3} s: {1} on screen (+{2} dying), view {3:F1} m, upscale {4:F2}x",
                        SongTime, _playback.OnScreenAlive, _playback.OnScreenDying, _playback.ViewWidth, _playback.Pipeline.Upscale));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Quit(1);
                    return;
                }
                _stillsAt.RemoveAt(0);
                _stillNow = false;
            }
            bool stillsDone = _flags.CaptureStills != null && _stillsAt.Count == 0 && _flags.Seconds <= 0;
            if (SongTime >= _endAt || stillsDone) _quitNext = true; // after this frame is drawn
        }

        void OnGUI()
        {
            if (_flags == null || !_flags.Overlay || _playback == null) return;
            var tl = _playback.Timeline;
            int si = tl.Map.SectionIndexAt(SongTime);
            var sec = si >= 0 && si < tl.Sections.Length ? tl.Sections[si] : null;
            var text = string.Format(CultureInfo.InvariantCulture,
                "t {0:F2} s  {1}\non screen {2} (+{3} dying) of {4}\nview {5:F1} m, upscale {6:F2}x, target {7}x{8}\nsprites {9}  {10:F1} ms",
                SongTime, sec?.Label ?? "", _playback.OnScreenAlive, _playback.OnScreenDying, sec?.TargetOnScreen ?? 0,
                _playback.ViewWidth, _playback.Pipeline.Upscale, _playback.Pipeline.TargetW, _playback.Pipeline.TargetH,
                _playback.Drawn, Time.unscaledDeltaTime * 1000);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(16, 12, 900, 140), text, style);
        }

        void Quit(int code)
        {
            Debug.Log($"DemoMain: done at song time {SongTime.ToString("F2", CultureInfo.InvariantCulture)} after {_frames} frames");
            enabled = false;
            Application.Quit(code);
        }

        void OnDestroy()
        {
            _playback?.Dispose();
            if (_still != null) _still.Release();
        }
    }
}
