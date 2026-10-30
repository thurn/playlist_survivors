using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Demo2D.Choreography;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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
    ///
    /// `--effect-stills` (PX7) adds a still of each effect kind as it fires, near the camera's
    /// focus, and lists them in `effect-stills.txt`. `--measure &lt;json&gt;` draws every
    /// fixed-time frame by hand at 1080p and times it through a one-pixel readback (the main
    /// thread then waits for the GPU's work too, PX1's spike), with the effect and light counts.
    ///
    /// Effect sounds (PX8) are scheduled on the mixer's clock (SoundScheduler) in live play, not in
    /// fixed time; `--dump-sounds &lt;json&gt;` writes the timeline's sound list with each event's clip.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class DemoMain : MonoBehaviour
    {
        public const int StillW = 1920, StillH = 1080;

        DevFlags _flags;
        SongPlayback _playback;
        SongClock _clock;
        SoundScheduler _sounds;
        RenderTexture _still;
        readonly List<double> _stillsAt = new List<double>();
        double _endAt;
        bool _quitNext, _stillNow;
        int _frames;
        readonly List<string> _effectStills = new List<string>();
        Measurement _measure;

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
            // The boot scene's camera, test card and moonlight would draw too (the night owns the moon: NightLighting).
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include)) Destroy(c.gameObject);
            foreach (var card in FindObjectsByType<TestCard>(FindObjectsInactive.Include)) Destroy(card.gameObject);
            foreach (var l in FindObjectsByType<Light2D>(FindObjectsInactive.Include)) DestroyImmediate(l.gameObject);

            var data = PlaybackData.Load();
            _playback = new SongPlayback(data, _flags.Zoom, transform) { Feel = !_flags.NoFeel };
            _playback.Empty = _flags.Empty;
            _playback.Effects.Enabled = !_flags.NoEffects && !_flags.Empty;
            _playback.Night.Extras = !_flags.NoNight;
            _playback.Pipeline.PostProcessing = !_flags.NoPost;
            var tl = _playback.Timeline;
            Debug.Log($"DemoMain: {tl.Name} built in {_playback.BuildSeconds:F2} s, {tl.EntityCount} entities; " +
                      $"target {_playback.Pipeline.TargetW}x{_playback.Pipeline.TargetH} ({_flags.Zoom} zoom)");
            _endAt = _flags.Seconds > 0 ? _flags.StartAt + _flags.Seconds : tl.Duration;

            if (_flags.CaptureStills != null)
            {
                foreach (var s in (_flags.StillsAt ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    _stillsAt.Add(double.Parse(s, CultureInfo.InvariantCulture));
                if (_flags.EffectStills) AddEffectStills(data);
                _stillsAt.Sort();
                _stillsAt.RemoveAll(s => s < _flags.StartAt);
            }
            if (_flags.CaptureStills != null || _flags.Measure != null)
                _still = new RenderTexture(StillW, StillH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Still" };
            if (_flags.Measure != null) _measure = new Measurement(_flags);

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
            if (_flags.DumpSounds != null) DumpSounds(data, _flags.DumpSounds);
            if (!_flags.FixedTime && !_flags.NoSound) _sounds = new SoundScheduler(tl, _clock, data, gameObject, _flags.StartAt);
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
            bool screen = !Application.isBatchMode && !_stillNow && _measure == null;
            _measure?.Begin();
            _playback.Frame(t, screen ? Screen.width : StillW, screen ? Screen.height : StillH, _stillNow);
            _sounds?.Update(t);
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
            else if (_measure != null)
            {
                _playback.Pipeline.Render(_still);
                _measure.End(_still, SongTime, _playback);
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

        /// <summary>
        /// A still of each effect kind as it fires: of each kind's events, the one nearest the
        /// camera's focus (the earliest of equals), a moment after it (0.15 s, once the hit flash is over; the beam's sweep 0.45 s).
        /// </summary>
        void AddEffectStills(PlaybackData data)
        {
            var tl = _playback.Timeline;
            var library = EffectLibrary.Parse(data.effects.text);
            for (int k = 0; k < tl.EffectNames.Length; k++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < tl.Effects.Length; i++)
                {
                    var ev = tl.Effects[i];
                    if (ev.Effect != k || ev.Time < _flags.StartAt) continue;
                    var at = new Vector2(ev.Position.X, ev.Position.Z);
                    if (ev.AttachTo >= 0) { var p = tl.PositionAt(ev.AttachTo, ev.Time); at = new Vector2(p.X, p.Y); }
                    var f = tl.FramingAt(ev.Time);
                    float d = Vector2.Distance(at, new Vector2(f.Focus.X, f.Focus.Y));
                    if (d < bestD - 1e-3f) { bestD = d; best = i; }
                }
                var name = tl.EffectNames[k];
                if (best < 0) { _effectStills.Add($"{name}: never fires"); continue; }
                var spec = library.Kinds[name];
                double after = spec.Sweep ? 0.45 : 0.15;
                double at_ = Math.Round((tl.Effects[best].Time + after) * 1000) / 1000;
                _stillsAt.Add(at_);
                _effectStills.Add(string.Format(CultureInfo.InvariantCulture, "{0}: event {1} at {2:F3} s, {3:F1} m from the focus: still-{4:000.000}.jpg",
                    name, best, tl.Effects[best].Time, bestD, at_));
            }
            Directory.CreateDirectory(_flags.CaptureStills);
            File.WriteAllLines(Path.Combine(_flags.CaptureStills, "effect-stills.txt"), _effectStills);
        }

        /// <summary>`--dump-sounds`: every sound event with its name, clip and gain (demo3d's format).</summary>
        void DumpSounds(PlaybackData data, string path)
        {
            var tl = _playback.Timeline;
            var sb = new StringBuilder("[\n");
            for (int i = 0; i < tl.Sounds.Length; i++)
            {
                var s = tl.Sounds[i];
                var name = tl.SoundNames[s.Sound];
                var clip = data.Sound(name);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {{\"t\": {0:F4}, \"sound\": \"{1}\", \"clip\": \"{2}\", \"gain\": {3:F3}{4}{5}\n",
                    s.Time, name, clip != null ? clip.name : "", s.Gain, "}", i + 1 < tl.Sounds.Length ? "," : "");
            }
            sb.Append("]\n");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"DemoMain: wrote {tl.Sounds.Length} sound events to {path} ({data.soundNames?.Length ?? 0} sounds baked)");
        }

        /// <summary>`--measure`: each frame's time through a one-pixel readback, and what it drew.</summary>
        sealed class Measurement
        {
            readonly DevFlags _flags;
            readonly Texture2D _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            readonly List<double> _ms = new List<double>();
            readonly List<string> _rows = new List<string>();
            long _start;
            int _maxLive, _maxParticles, _maxEffectLights, _maxOffered;

            public Measurement(DevFlags flags) { _flags = flags; }

            public void Begin() => _start = System.Diagnostics.Stopwatch.GetTimestamp();

            public void End(RenderTexture rt, double t, SongPlayback playback)
            {
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                _pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                RenderTexture.active = previous;
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _ms.Add(ms);
                var fx = playback.Effects;
                _maxLive = Math.Max(_maxLive, fx.Live);
                _maxEffectLights = Math.Max(_maxEffectLights, fx.Lights.Lit);
                if (_ms.Count % 15 == 0)
                {
                    int particles = fx.ParticleCount();
                    _maxParticles = Math.Max(_maxParticles, particles);
                    _rows.Add(string.Format(CultureInfo.InvariantCulture, "[{0:F3}, {1:F2}, {2}, {3}, {4}, {5}, {6}, {7}]", t, ms, playback.OnScreenAlive,
                        fx.Live, particles, fx.Lights.Lit, playback.Night.FireflyLights.Lit, playback.Night.PlantLights.Lit));
                }
            }

            public void Write()
            {
                if (_ms.Count == 0) return;
                var sorted = new List<double>(_ms);
                sorted.Sort();
                double P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
                double mean = 0;
                foreach (var m in _ms) mean += m;
                mean /= _ms.Count;
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"startAt\": {0}, \"seconds\": {1}, \"effects\": {2}, \"night\": {3}, \"post\": {4}, \"frames\": {5},\n",
                    _flags.StartAt, _flags.Seconds, Json(!_flags.NoEffects), Json(!_flags.NoNight), Json(!_flags.NoPost), _ms.Count);
                // (A closing brace straight after a format item would read as part of its format.)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"ms\": {{\"mean\": {0:F2}, \"p50\": {1:F2}, \"p95\": {2:F2}, \"p99\": {3:F2}, \"max\": {4:F2}",
                    mean, P(0.5), P(0.95), P(0.99), sorted[sorted.Count - 1]).Append("},\n");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"most\": {{\"effects\": {0}, \"particles\": {1}, \"effectLights\": {2}", _maxLive, _maxParticles, _maxEffectLights).Append("},\n");
                sb.Append("  \"columns\": [\"t\", \"ms\", \"onScreen\", \"effects\", \"particles\", \"effectLights\", \"fireflyLights\", \"plantLights\"],\n");
                sb.Append("  \"every15thFrame\": [\n    ").Append(string.Join(",\n    ", _rows)).Append("\n  ]\n}\n");
                var dir = Path.GetDirectoryName(_flags.Measure);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_flags.Measure, sb.ToString());
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "DemoMain: measured {0} frames: mean {1:F2} ms, p99 {2:F2} ms, max {3:F2} ms -> {4}",
                    _ms.Count, mean, P(0.99), sorted[sorted.Count - 1], _flags.Measure));
            }

            static string Json(bool b) => b ? "true" : "false";
        }

        void Quit(int code)
        {
            _measure?.Write();
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
