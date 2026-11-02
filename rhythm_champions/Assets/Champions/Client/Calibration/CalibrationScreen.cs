using System;
using System.Collections.Generic;
using Champions.Client.Loop;
using Champions.Client.Music;
using Champions.Client.UI;
using UnityEngine;
using UnityEngine.UIElements;
using SimKey = Champions.Sim.Key;

namespace Champions.Client.Calibration
{
    /// <summary>
    /// The calibration screens (design.md, Music > Sources), ported from game/'s and slimmed: one for each offset, saved for
    /// the default output device (<see cref="CalibrationStore"/>), reachable from the title and the pause menu. Clicks play
    /// on Unity's DSP clock as the game's sounds do, and the screen maps them with the game's own clock
    /// (<see cref="MusicClock"/>: song time is DSP time here, heard the device's latency plus Unity's buffering later), so
    /// each offset is what the game's clocks still lack.
    /// - **Video**: a click on every beat at 120 BPM and a flash drawn at frameSong; Left and Right on the offset's row
    ///   move the flash 5 ms earlier or later until it lands with the click; Save keeps it, Esc leaves.
    /// - **Input**: eight clicks at 120 BPM; the player taps Space or an arrow with each (<see cref="KeyInput"/>'s
    ///   timestamps, as the sim gets them). The median error after the first two taps is saved (<see cref="TapAlong"/>);
    ///   Again goes again, Esc leaves.
    /// In a run the music is ducked under the clicks while it shows. Under `--fixed-time` its song time is the frames' and
    /// no click plays, for stills.
    /// </summary>
    public sealed class CalibrationScreen : VisualElement, IMenuScreen, IDisposable
    {
        public enum Mode { Video, Input }

        public const double Nudge = 0.005, VideoLimit = 0.2;
        const double Horizon = 0.25; // clicks are scheduled this far ahead
        const double Lead = 1.0;     // the first click's wait
        const double FlashFade = 0.15;
        const float DuckedVolume = 0.15f;
        const int Voices = 4;
        const float ScaleWidth = 560;

        public readonly MenuList Menu = new MenuList();
        readonly Mode _mode;
        readonly Action _closed;
        readonly MusicPlayer _ducked;
        readonly float _duckedFrom;
        readonly GameObject _host;
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly MusicClock _clock = new MusicClock();
        readonly CalibrationStore _store;
        readonly Plugin.Device _device;
        readonly KeyInput _keys = new KeyInput();
        readonly bool _fixed;
        readonly double _opened;
        readonly VisualElement _flash, _taps, _scale;
        readonly Label _say;
        Offsets _offsets;
        int _voice;
        bool _closing;

        // The clicks, in the screen's song time: the video screen's every Period from _first, the tap-along's listed.
        double _first, _nextClick;
        readonly List<double> _clicks = new List<double>();
        readonly List<double> _tapTimes = new List<double>();
        bool _measuring;

        /// <summary>Opens a screen whose clicks play from a child of <paramref name="parent"/>; <paramref name="closed"/> runs once it closes.</summary>
        /// <param name="music">A run's music, ducked while it shows.</param>
        public CalibrationScreen(Transform parent, Mode mode, bool fixedTime, Action closed, MusicPlayer music = null)
        {
            _mode = mode;
            _closed = closed;
            _fixed = fixedTime;
            _opened = Time.timeAsDouble;
            _store = CalibrationStore.Load();
            _device = CurrentDevice();
            _offsets = _store.For(_device.Uid);
            _ducked = music;
            if (_ducked != null)
            {
                _duckedFrom = _ducked.Volume;
                _ducked.Volume = DuckedVolume;
            }
            _host = new GameObject("Calibration clicks");
            _host.transform.SetParent(parent, false);
            // A listener when the scene has none (the title), gone with the screen.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) _host.AddComponent<AudioListener>();
            var clip = ClickClip(AudioSettings.outputSampleRate);
            for (int i = 0; i < Voices; i++)
            {
                var v = _host.AddComponent<AudioSource>();
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

            AddToClassList("screen");
            AddToClassList(music != null ? "screen--veil" : "screen--void");
            pickingMode = PickingMode.Ignore;
            var column = TitleScreen.Column();
            column.AddToClassList("calibration");
            Add(column);
            bool video = mode == Mode.Video;
            _flash = Part(column, "calibration__flash", video);
            _taps = Part(column, "calibration__taps", !video);
            _scale = Part(column, "calibration__scale", !video);
            _scale.Add(Part(_scale, "calibration__centre", true));
            _say = new Label { pickingMode = PickingMode.Ignore };
            _say.AddToClassList("calibration__say");
            column.Add(_say);
            column.Add(Menu);
            Sample();
            if (video)
            {
                _first = _nextClick = Math.Ceiling((Now() + Lead) / TapAlong.Period) * TapAlong.Period;
                Menu.Set(new[] { new MenuList.Row(OffsetText(), null, by => NudgeBy(-by)), new MenuList.Row("Save", Save), new MenuList.Row("Back", Close) });
                Say("Match the flash to the click");
            }
            else StartTapAlong();
            Debug.Log($"Calibration: {_mode} for {(_device.Uid.Length > 0 ? _device.Uid : "no device")} ({_offsets}; output latency " +
                      $"{1000 * (_device.Latency + MusicPlayer.DspBuffering):F1} ms{(_fixed ? ", fixed time: no clicks" : "")}) in {_store.Path}");
        }

        static VisualElement Part(VisualElement parent, string className, bool shown)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(className);
            e.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            parent.Add(e);
            return e;
        }

        /// <summary>The default output device, or an unnamed one if the plugin can't say.</summary>
        public static Plugin.Device CurrentDevice()
        {
            try { return Plugin.OutputDevice(); }
            catch (Exception e) when (e is InvalidOperationException || e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.LogWarning("Calibration: no output device from the plugin: " + e.Message);
                return new Plugin.Device("", "", 0, false);
            }
        }

        /// <summary>A decaying 2 kHz cosine, full height on its first sample so its onset is exact (game/'s BeatClicks).</summary>
        static AudioClip ClickClip(int rate)
        {
            const float hz = 2000f, decay = 0.003f, length = 0.025f, gain = 0.6f;
            int n = (int)(length * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                data[i] = gain * Mathf.Exp(-t / decay) * Mathf.Cos(2f * Mathf.PI * hz * t);
            }
            var clip = AudioClip.Create("calibration-click", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>The screen's song time now: the DSP clock's, or under --fixed-time the frames' since it opened.</summary>
        double Now() => _fixed ? Time.timeAsDouble - _opened : AudioSettings.dspTime;

        /// <summary>The song time this frame shows (the clock's video offset applied, as in the game).</summary>
        double FrameSong() => _fixed ? Now() + _clock.VideoCalibration : _clock.FrameSong(Time.realtimeSinceStartupAsDouble);

        /// <summary>The song time heard when a key went down at real time <paramref name="real"/>, before any input offset.</summary>
        double Heard(double real) => _fixed ? Now() : _clock.HeardSong(real);

        void Sample()
        {
            if (_fixed) return;
            MusicPlayer.SampleDsp(_clock);
            _clock.OutputLatency = _device.Latency + MusicPlayer.DspBuffering;
        }

        /// <summary>The frame: clicks scheduled, the flash drawn, taps taken.</summary>
        public void Update()
        {
            if (_closing) return;
            Sample();
            if (_mode == Mode.Video) UpdateVideo();
            else UpdateInput();
        }

        public bool Key(MenuKey key)
        {
            if (key == MenuKey.Back)
            {
                Close();
                return true;
            }
            return !_measuring && Menu.Key(key); // the tap-along's keys are taps
        }

        void NudgeBy(int earlier)
        {
            _offsets.Video = Math.Clamp(Math.Round((_offsets.Video + earlier * Nudge) * 1000) / 1000, -VideoLimit, VideoLimit);
            _offsets.ApplyTo(_clock);
            Menu.SetText(0, OffsetText());
        }

        string OffsetText() => $"{Offsets.Ms(_offsets.Video):+0;-0;0} ms";

        void Save()
        {
            bool saved = _store.Set(_device, _offsets);
            Debug.Log($"Calibration: video {(saved ? "saved" : "NOT saved")}: {_offsets}");
            Close();
        }

        void UpdateVideo()
        {
            while (_keys.TryTake(out _)) { } // the menu's keys come through the flow
            for (; _nextClick <= Now() + Horizon; _nextClick += TapAlong.Period) Click(_nextClick);
            double phase = FrameSong() - _first;
            phase = phase < 0 ? double.PositiveInfinity : phase - Math.Floor(phase / TapAlong.Period) * TapAlong.Period;
            float a = Mathf.Clamp01(1f - (float)(phase / FlashFade));
            _flash.style.opacity = a * a;
        }

        void StartTapAlong()
        {
            _clicks.Clear();
            _tapTimes.Clear();
            _taps.Clear();
            foreach (var mark in _scale.Query(className: "calibration__mark").ToList()) mark.RemoveFromHierarchy();
            while (_keys.TryTake(out _)) { }
            double first = Math.Ceiling((Now() + Lead) * 1000) / 1000;
            for (int i = 0; i < TapAlong.Clicks; i++)
            {
                double t = first + i * TapAlong.Period;
                _clicks.Add(t);
                Click(t);
                var gem = new VisualElement { pickingMode = PickingMode.Ignore };
                gem.AddToClassList("calibration__tap");
                _taps.Add(gem);
            }
            _measuring = true;
            Menu.Set(Array.Empty<MenuList.Row>());
            Say("Tap Space with each click");
        }

        void UpdateInput()
        {
            while (_keys.TryTake(out var change))
                if (_measuring && change.Down && change.Key != SimKey.Enter) Tap(Heard(change.Real));
            if (_measuring && Now() > _clicks[_clicks.Count - 1] + TapAlong.Period) Finish();
        }

        void Tap(double heard)
        {
            if (heard < _clicks[0] - TapAlong.Period) return; // before the clicks began
            int n = _tapTimes.Count;
            _tapTimes.Add(heard);
            if (n < _taps.childCount) _taps[n].AddToClassList("calibration__tap--lit");
            if (TapAlong.Error(heard, _clicks) is double e)
            {
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("calibration__mark");
                if (n < 2) mark.AddToClassList("calibration__mark--warmup"); // the first two taps don't count
                mark.style.left = ScaleWidth / 2 * (float)(1 + e / TapAlong.Window);
                _scale.Add(mark);
            }
        }

        void Finish()
        {
            _measuring = false;
            if (TapAlong.Offset(_tapTimes, _clicks) is double o)
            {
                _offsets.Input = o;
                bool saved = _store.Set(_device, _offsets);
                int ms = Offsets.Ms(o);
                Say(ms == 0 ? "Right on the clicks" : $"Taps land {Math.Abs(ms)} ms {(ms > 0 ? "late" : "early")}");
                Debug.Log($"Calibration: input {(saved ? "saved" : "NOT saved")}: {_offsets} from {_tapTimes.Count} taps");
            }
            else
            {
                Say("Too few taps landed");
                Debug.Log($"Calibration: input not measured, {_tapTimes.Count} taps");
            }
            Menu.Set(new[] { new MenuList.Row("Again", StartTapAlong), new MenuList.Row("Back", Close) });
        }

        /// <summary>A click at song time <paramref name="t"/>, on its DSP sample; none under --fixed-time, and none the mixer is already past.</summary>
        void Click(double t)
        {
            if (_fixed || t < AudioSettings.dspTime + MusicPlayer.ScheduleHeadroom) return;
            _voices[_voice].PlayScheduled(t);
            _voice = (_voice + 1) % Voices;
        }

        void Say(string message) => _say.text = message ?? "";

        void Close()
        {
            if (_closing) return;
            _closing = true;
            _closed?.Invoke();
        }

        public void Dispose()
        {
            _keys.Dispose();
            if (_ducked != null) _ducked.Volume = _duckedFrom;
            if (_host != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_host);
                else UnityEngine.Object.DestroyImmediate(_host);
            }
            RemoveFromHierarchy();
        }
    }
}
