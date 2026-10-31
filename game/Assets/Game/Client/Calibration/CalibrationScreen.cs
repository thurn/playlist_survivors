using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Client.Loop;
using Game.Client.Music;
using Game.Client.Title;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Client.Calibration
{
    /// <summary>
    /// The calibration screens (unity-game.md, Calibration), one for each offset, both saved
    /// at once for the default output device (<see cref="CalibrationStore"/>), and reachable from the
    /// title screen and the pause menu. Clicks play on Unity's DSP clock as the game's sounds do,
    /// and the screen maps them with the game's own clock (<see cref="MusicClock"/>: song time is
    /// DSP time here, heard the device's latency plus Unity's buffering later), so each offset is
    /// what the game's clocks still lack.
    /// - **Video**: a click on every beat at 120 BPM and a flash drawn at frameSong; Left and Right
    ///   move the flash 5 ms earlier or later until it lands with the click, Enter saves, Esc leaves.
    /// - **Input**: eight clicks at 120 BPM; the player taps an arrow key or Space with each
    ///   (<see cref="KeyInput"/>'s timestamps, as the sim gets them). The median error after the first
    ///   two taps is saved (<see cref="TapAlong"/>); Enter goes again, Esc leaves.
    /// In a run, the music is ducked under the clicks while the screen is open.
    /// Player flags: `--calibrate video|input` opens the screen at launch; under `--fixed-time` its
    /// song time is the frame's and no click plays, for stills; `--tap-along MS,MS,...` taps each
    /// click that many ms late (the list repeats), for windowless checks of the tap-along.
    /// </summary>
    public sealed class CalibrationScreen : MonoBehaviour
    {
        public enum Mode { Video, Input }

        public const double Nudge = 0.005, VideoLimit = 0.2;
        const double Horizon = 0.25;  // clicks are scheduled this far ahead
        const double Lead = 1.0;      // the first click's wait
        const double FlashFade = 0.15;
        const float DuckedVolume = 0.15f;
        const int Voices = 4;
        const float ScaleWidth = 640;

        static readonly Color[] Spectrum =
        {
            new Color(1f, 0.55f, 0.62f), new Color(1f, 0.72f, 0.5f), new Color(1f, 0.92f, 0.55f), new Color(0.62f, 1f, 0.7f),
            new Color(0.5f, 0.92f, 1f), new Color(0.6f, 0.7f, 1f), new Color(0.85f, 0.6f, 1f), new Color(1f, 0.6f, 0.85f),
        };

        Mode _mode;
        Action _closed;
        MusicPlayer _ducked;
        float _duckedFrom;
        ScreenPanel _panel;
        VisualElement _flash, _taps, _scale;
        Label _offset, _message;
        CalibrationStore _store;
        Plugin.Device _device;
        Offsets _offsets;
        readonly MusicClock _clock = new MusicClock();
        readonly AudioSource[] _voices = new AudioSource[Voices];
        AudioListener _listener; // added when the scene has none (the title screen), removed with the screen
        int _voice;
        KeyInput _keys;
        bool _fixed;
        double _opened;
        int _openedFrame;
        double[] _synthetic;

        // The clicks, in the screen's song time: the video screen's every Period from _first, the tap-along's listed.
        double _first, _nextClick;
        readonly List<double> _clicks = new List<double>();
        readonly List<double> _tapTimes = new List<double>();
        int _synthesized;
        bool _measuring;

        /// <summary>Opens a calibration screen on `host`; `closed` runs once it has closed. In a run, `music` is ducked meanwhile.</summary>
        public static CalibrationScreen Begin(GameObject host, Mode mode, Action closed, MusicPlayer music = null)
        {
            var screen = host.AddComponent<CalibrationScreen>();
            screen._mode = mode;
            screen._closed = closed;
            screen._ducked = music;
            screen.Setup();
            return screen;
        }

        /// <summary>The screen `--calibrate` names, or null.</summary>
        public static Mode? Wanted
        {
            get
            {
                var flag = Args.Get("--calibrate");
                if (flag == null) return null;
                if (Enum.TryParse(flag, true, out Mode mode)) return mode;
                Debug.LogError($"CalibrationScreen: --calibrate {flag}: video or input");
                return null;
            }
        }

        /// <summary>The default output device, or an unnamed one if the plugin can't say.</summary>
        public static Plugin.Device CurrentDevice()
        {
            try { return Plugin.OutputDevice(); }
            catch (Exception e)
            {
                Debug.LogWarning("CalibrationScreen: no output device from the plugin: " + e.Message);
                return new Plugin.Device("", "", 0, false);
            }
        }

        void Setup()
        {
            _fixed = Args.Has("--fixed-time");
            _opened = Time.timeAsDouble;
            _openedFrame = Time.frameCount;
            _store = CalibrationStore.Load();
            _device = CurrentDevice();
            _offsets = _store.For(_device.Uid);
            _synthetic = ParseTaps(Args.Get("--tap-along"));
            _keys = new KeyInput();
            if (_ducked != null)
            {
                _duckedFrom = _ducked.Volume;
                _ducked.Volume = DuckedVolume;
            }
            if (FindAnyObjectByType<AudioListener>() == null) _listener = gameObject.AddComponent<AudioListener>();
            var clip = BeatClicks.MakeClip(AudioSettings.outputSampleRate);
            for (int i = 0; i < Voices; i++)
            {
                var v = gameObject.AddComponent<AudioSource>();
                v.playOnAwake = false;
                v.spatialBlend = 0;
                v.priority = 0;
                v.clip = clip;
                _voices[i] = v;
            }
            // Song time is DSP time: the anchor pins them together. The flash is drawn with the offset on the clock.
            _clock.SetAnchor(0, 0);
            _clock.DisplayLatency = MusicPlayer.Refresh;
            _offsets.ApplyTo(_clock);

            _panel = new ScreenPanel(transform, "Calibration screen", "Calibration/Calibration");
            var root = _panel.Root;
            bool video = _mode == Mode.Video;
            root.Q<Label>("heading").text = video ? "CALIBRATE VIDEO" : "CALIBRATE INPUT";
            string name = string.IsNullOrEmpty(_device.Name) ? "this output" : _device.Name;
            root.Q<Label>("device").text = name + (_device.Bluetooth ? "   ·   Bluetooth: sync is best effort" : "");
            root.Q("video").style.display = video ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("input").style.display = video ? DisplayStyle.None : DisplayStyle.Flex;
            _flash = root.Q("flash");
            _offset = root.Q<Label>("offset");
            _taps = root.Q("taps");
            _scale = root.Q("scale");
            _message = root.Q<Label>("message");
            root.Q<Label>("hint").text = video
                ? "Left / Right moves the flash earlier or later   ·   Enter saves   ·   Esc leaves"
                : "Tap an arrow key or Space with each click   ·   Enter goes again   ·   Esc leaves";
            Sample();
            if (video)
            {
                _first = _nextClick = Math.Ceiling((Now() + Lead) / TapAlong.Period) * TapAlong.Period;
                ShowOffset();
                Say("Line the flash up with the click you hear.");
            }
            else StartTapAlong();
            Debug.Log($"CalibrationScreen: {_mode} for {(_device.Uid.Length > 0 ? _device.Uid : "no device")} " +
                      $"({_offsets}; output latency {1000 * (_device.Latency + MusicPlayer.DspBuffering):F1} ms{(_fixed ? ", fixed time: no clicks" : "")}) in {_store.Path}");
        }

        static double[] ParseTaps(string list)
        {
            if (list == null) return null;
            var parts = list.Split(',');
            var ms = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) ms[i] = double.Parse(parts[i], CultureInfo.InvariantCulture) / 1000;
            return ms;
        }

        /// <summary>The screen's song time now: the DSP clock's, or under --fixed-time the frames' since it opened.</summary>
        double Now() => _fixed ? Time.timeAsDouble - _opened : AudioSettings.dspTime;

        /// <summary>The song time this frame shows (the clock's video offset applied, as in the game).</summary>
        double FrameSong() => _fixed ? Now() + _clock.VideoCalibration : _clock.FrameSong(Time.realtimeSinceStartupAsDouble);

        /// <summary>The song time heard when a key went down at real time `real`, before any input offset.</summary>
        double Heard(double real) => _fixed ? Now() : _clock.HeardSong(real);

        void Sample()
        {
            if (_fixed) return;
            MusicPlayer.SampleDsp(_clock);
            _clock.OutputLatency = _device.Latency + MusicPlayer.DspBuffering;
        }

        void Update()
        {
            Sample();
            // The key that opened the screen is the opener's, not a key here.
            var k = Time.frameCount == _openedFrame ? null : Keyboard.current;
            bool esc = k != null && k.escapeKey.wasPressedThisFrame;
            bool enter = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
            if (_mode == Mode.Video) UpdateVideo(k, enter);
            else UpdateInput(enter);
            if (esc) Close();
        }

        void UpdateVideo(Keyboard k, bool enter)
        {
            while (_keys.TryTake(out _)) { } // the arrows are read below
            if (k != null)
            {
                int by = (k.leftArrowKey.wasPressedThisFrame ? 1 : 0) - (k.rightArrowKey.wasPressedThisFrame ? 1 : 0);
                if (by != 0)
                {
                    _offsets.Video = Math.Clamp(Math.Round((_offsets.Video + by * Nudge) * 1000) / 1000, -VideoLimit, VideoLimit);
                    _offsets.ApplyTo(_clock);
                    ShowOffset();
                }
            }
            for (; _nextClick <= Now() + Horizon; _nextClick += TapAlong.Period) Click(_nextClick);
            double song = FrameSong();
            double phase = song - _first;
            phase = phase < 0 ? double.PositiveInfinity : phase - Math.Floor(phase / TapAlong.Period) * TapAlong.Period;
            float a = Mathf.Clamp01(1f - (float)(phase / FlashFade));
            _flash.style.opacity = a * a;
            if (enter)
            {
                bool saved = _store.Set(_device, _offsets);
                Debug.Log($"CalibrationScreen: video {(saved ? "saved" : "NOT saved")}: {_offsets}");
                Close();
            }
        }

        void ShowOffset() => _offset.text = $"{Offsets.Ms(_offsets.Video):+0;-0;0} ms";

        void StartTapAlong()
        {
            _clicks.Clear();
            _tapTimes.Clear();
            _taps.Clear();
            foreach (var mark in _scale.Query(className: "scale__mark").ToList()) mark.RemoveFromHierarchy();
            while (_keys.TryTake(out _)) { }
            double first = Math.Ceiling((Now() + Lead) * 1000) / 1000;
            for (int i = 0; i < TapAlong.Clicks; i++)
            {
                double t = first + i * TapAlong.Period;
                _clicks.Add(t);
                Click(t);
                var gem = new VisualElement();
                gem.AddToClassList("taps__gem");
                _taps.Add(gem);
            }
            _synthesized = 0;
            _measuring = true;
            Say("Listen, and tap with each of the eight clicks.");
        }

        void UpdateInput(bool enter)
        {
            if (enter)
            {
                StartTapAlong();
                return;
            }
            if (!_measuring)
            {
                while (_keys.TryTake(out _)) { }
                return;
            }
            while (_keys.TryTake(out var change))
                if (change.Down) Tap(Heard(change.Real));
            if (_synthetic != null)
                for (; _synthesized < _clicks.Count; _synthesized++)
                {
                    double t = _clicks[_synthesized] + _synthetic[_synthesized % _synthetic.Length];
                    if (t > Now()) break;
                    Tap(t);
                }
            if (Now() > _clicks[_clicks.Count - 1] + TapAlong.Period) Finish();
        }

        void Tap(double heard)
        {
            if (heard < _clicks[0] - TapAlong.Period) return; // before the clicks began
            int n = _tapTimes.Count;
            _tapTimes.Add(heard);
            if (n < _taps.childCount)
            {
                var gem = _taps[n];
                gem.AddToClassList("taps__gem--tapped");
                gem.style.backgroundColor = Spectrum[n % Spectrum.Length];
            }
            if (TapAlong.Error(heard, _clicks) is double e)
            {
                var mark = new VisualElement();
                mark.AddToClassList("scale__mark");
                if (n < 2) mark.AddToClassList("scale__mark--warmup"); // the first two taps don't count
                else mark.style.backgroundColor = Spectrum[n % Spectrum.Length];
                mark.style.left = ScaleWidth / 2 * (float)(1 + e / TapAlong.Window) - 7;
                _scale.Add(mark);
            }
        }

        void Finish()
        {
            _measuring = false;
            var offset = TapAlong.Offset(_tapTimes, _clicks);
            if (offset is double o)
            {
                _offsets.Input = o;
                bool saved = _store.Set(_device, _offsets);
                int ms = Offsets.Ms(o);
                string when = ms == 0 ? "right on the clicks" : $"{Math.Abs(ms)} ms {(ms > 0 ? "late" : "early")}";
                Say($"Your taps land {when}. " + (saved ? "Saved: the game hears your keys that much earlier." : "It couldn't be saved."));
                Debug.Log($"CalibrationScreen: input {(saved ? "saved" : "NOT saved")}: {_offsets} from {_tapTimes.Count} taps");
            }
            else
            {
                Say($"Too few taps landed on the clicks ({TapAlong.Needed} after the first two are needed). Enter goes again.");
                Debug.Log($"CalibrationScreen: input not measured, {_tapTimes.Count} taps");
            }
        }

        /// <summary>A click at song time `t`, on its DSP sample; none under --fixed-time, and none the mixer is already past.</summary>
        void Click(double t)
        {
            if (_fixed || t < AudioSettings.dspTime + MusicPlayer.ScheduleHeadroom) return;
            _voices[_voice].PlayScheduled(t);
            _voice = (_voice + 1) % Voices;
        }

        void Say(string message) => _message.text = message ?? "";

        void Close()
        {
            var closed = _closed;
            _closed = null;
            Destroy(this);
            closed?.Invoke();
        }

        void OnDestroy()
        {
            _keys?.Dispose();
            _panel?.Dispose();
            foreach (var v in _voices)
                if (v != null) Destroy(v);
            if (_listener != null) Destroy(_listener);
            if (_ducked != null) _ducked.Volume = _duckedFrom;
        }
    }
}
