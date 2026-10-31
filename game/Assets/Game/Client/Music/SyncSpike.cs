using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Client.Music
{
    /// <summary>
    /// The sync spike (unity-game.md, Build Order step 1): a File source through the plugin into
    /// Unity's output, with a click on every committed beat. Player flags:
    /// - `--file &lt;audio or .m3u&gt;`: the source;
    /// - `--headless`: the output is silenced after the in-process capture (run it with -batchmode,
    ///   which opens no window), and frames are paced at 60 per second as a display would;
    /// - `--spike-out DIR`: keeps Unity's final mix (mix.wav) and the clicks, buffers and frames
    ///   (spike.json) for `game spike` to measure.
    /// It quits with exit code 1 if the plugin fails or Unity's mixer doesn't run, and when the
    /// music ends.
    /// </summary>
    public sealed class SyncSpike : MonoBehaviour
    {
        public const double MixerTimeout = 3.0;

        MusicPlayer _music;
        BeatClicks _clicks;
        MixCapture _capture;
        string _out;
        double _startReal, _startDsp;
        bool _done;
        readonly List<FrameRow> _frames = new List<FrameRow>(1 << 16);

        [Serializable]
        public struct FrameRow
        {
            public double tick;      // the capture's clock (s)
            public double frameSong; // the clock's frameSong
            public double display;   // the display latency it assumed
        }

        [Serializable]
        public sealed class Report
        {
            public string file;
            public int outputRate, dspBufferLength, dspBuffers, channels;
            public double outputLatency, deviceLatency, dspBuffering, anchorDsp, anchorSong, captureStartDsp;
            public int stalls, state, failed, gaps, overflowed, rendered;
            public string error;
            public double[] clickSong, clickDsp;
            public double[] bufferTick, bufferDsp;
            public FrameRow[] frames;
        }

        /// <summary>Starts the spike on `host`'s scene with the command line's flags.</summary>
        public static SyncSpike Begin(GameObject host, Camera camera, string file, float seconds)
        {
            var spike = host.AddComponent<SyncSpike>();
            var outDir = Args.Get("--spike-out");
            spike._out = outDir != null ? Args.Path(outDir) : null;
            bool headless = Args.Has("--headless");
            if (headless) Application.targetFrameRate = Boot.FixedFps;

            var listenerHost = camera != null ? camera.gameObject : host;
            if (listenerHost.GetComponent<AudioListener>() == null) listenerHost.AddComponent<AudioListener>();
            if (headless || spike._out != null)
            {
                spike._capture = listenerHost.AddComponent<MixCapture>();
                spike._capture.Mute = headless;
                // Only a kept run needs the samples; a headless one is muted either way.
                spike._capture.Begin(spike._out == null ? 0 : (seconds > 0 ? seconds : 600) + 2);
            }

            var musicHost = new GameObject("Music");
            musicHost.AddComponent<AudioSource>();
            spike._music = musicHost.AddComponent<MusicPlayer>();
            // A filter's callback serves one source or listener, so the clicks' voices live apart.
            var clicksHost = new GameObject("Clicks");
            clicksHost.transform.SetParent(musicHost.transform);
            spike._clicks = new BeatClicks(clicksHost);
            spike._startReal = Time.realtimeSinceStartupAsDouble;
            spike._startDsp = AudioSettings.dspTime;
            spike._music.Play(Args.Path(file));
            AudioSettings.GetDSPBufferSize(out int length, out int count);
            Debug.Log($"SyncSpike: {file}, output {AudioSettings.outputSampleRate} Hz, DSP buffer {length} x {count}, " +
                      $"speaker mode {AudioSettings.speakerMode}, headless {headless}");
            return spike;
        }

        void Update()
        {
            if (_done) return;
            if (_music.Failed) { Finish(1, _music.Error); return; }
            if (AudioSettings.dspTime == _startDsp && Time.realtimeSinceStartupAsDouble - _startReal > MixerTimeout)
            {
                Finish(1, $"Unity's mixer isn't running: the DSP clock stayed at {_startDsp} for {MixerTimeout} s");
                return;
            }
            _clicks.Update(_music);
            if (_music.Clock.Anchored)
            {
                _frames.Add(new FrameRow
                {
                    tick = MixCapture.Now,
                    frameSong = _music.Clock.FrameSong(Time.realtimeSinceStartupAsDouble),
                    display = _music.Clock.DisplayLatency,
                });
            }
            if (_music.Ended) Finish(0, null);
        }

        void OnApplicationQuit() => Finish(0, null);

        /// <summary>Logs the run's summary, keeps the measurement if asked and quits with `code`.</summary>
        void Finish(int code, string error)
        {
            if (_done) return;
            _done = true;
            var s = _music.Status;
            Debug.Log($"SyncSpike: {(error ?? "done")}; song {_music.Clock.SongAtDsp(AudioSettings.dspTime):F3} s, " +
                      $"stalls {s.Stalls}, clicks {_clicks.Count} ({_clicks.Dropped} dropped), " +
                      $"output latency {_music.Clock.OutputLatency * 1000:F1} ms (device {s.OutputLatency * 1000:F1}), " +
                      $"capture gaps {(_capture != null ? _capture.Gaps : 0)}, audio callbacks {_music.Rendered}");
            if (_out != null) Keep(error);
            _music.Close();
            if (error != null) Debug.LogError("SyncSpike: " + error);
            Application.Quit(code);
        }

        void Keep(string error)
        {
            Directory.CreateDirectory(_out);
            var s = _music.Status;
            AudioSettings.GetDSPBufferSize(out int length, out int count);
            var log = _clicks.Log;
            var r = new Report
            {
                file = Args.Get("--file"),
                outputRate = AudioSettings.outputSampleRate,
                dspBufferLength = length,
                dspBuffers = count,
                channels = _capture.Channels,
                outputLatency = _music.Clock.OutputLatency,
                deviceLatency = s.OutputLatency,
                dspBuffering = MusicPlayer.DspBuffering,
                anchorDsp = s.AnchorDsp,
                anchorSong = s.AnchorSong,
                captureStartDsp = _capture.Buffers > 0 ? _capture.StartDsp : double.NaN,
                stalls = s.Stalls,
                state = s.State,
                failed = s.Failed,
                gaps = _capture.Gaps,
                overflowed = _capture.Overflowed ? 1 : 0,
                rendered = _music.Rendered,
                error = error ?? "",
                clickSong = new double[log.Count],
                clickDsp = new double[log.Count],
                bufferTick = new double[_capture.Buffers],
                bufferDsp = _capture.BufferDsp.ToArray(),
                frames = _frames.ToArray(),
            };
            for (int i = 0; i < log.Count; i++)
            {
                r.clickSong[i] = log.Array[i].Song;
                r.clickDsp[i] = log.Array[i].Dsp;
            }
            var ticks = _capture.BufferTicks;
            for (int i = 0; i < r.bufferTick.Length; i++) r.bufferTick[i] = MixCapture.TicksToSeconds(ticks[i]);
            _capture.WriteWav(Path.Combine(_out, "mix.wav"), AudioSettings.outputSampleRate);
            File.WriteAllText(Path.Combine(_out, "spike.json"), JsonUtility.ToJson(r));
            Debug.Log($"SyncSpike: kept {_capture.Frames} frames of the mix and {log.Count} clicks in {_out}");
        }
    }
}
