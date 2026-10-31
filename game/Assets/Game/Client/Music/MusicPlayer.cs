using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Music
{
    /// <summary>
    /// The music: one plugin session played through Unity's output (unity-game.md, The Delay Line
    /// and Unity's Output). Its audio source has no clip; its audio callback asks the plugin for
    /// each buffer of the delayed music, so the music and every scheduled sound leave through one
    /// mixer. Each frame it samples the DSP clock, takes the plugin's anchor into
    /// <see cref="Clock"/> and drains new entries into <see cref="Entries"/>.
    ///
    /// The plugin must see the audio callback's last return before ps_close, so the callback marks
    /// itself busy before it looks at the session, and closing waits for that mark to clear.
    ///
    /// It updates before every other script, so a frame's readers see this frame's clock sample
    /// and entries, and none of a poll's entries is overwritten before they read it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DefaultExecutionOrder(-1000)]
    public sealed class MusicPlayer : MonoBehaviour
    {
        public const double M = 3.0;
        public const int MaxEntries = 512;

        IntPtr _session;
        int _busy, _closing;
        int _rendered; // audio callbacks that reached the plugin
        double _rate, _halfBuffer;
        AudioSource _source;
        string _error;

        public readonly MusicClock Clock = new MusicClock();
        public readonly StreamEntry[] Entries = new StreamEntry[MaxEntries];
        [NonSerialized] public Plugin.Status Status;

        /// <summary>How many of <see cref="Entries"/> this frame's poll filled.</summary>
        public int EntryCount { get; private set; }
        public bool Open => _session != IntPtr.Zero;
        public bool Failed => _error != null;
        public string Error => _error;
        public int Rendered => Volatile.Read(ref _rendered);
        public bool Playing => Open && Status.State == Plugin.Playing;
        public bool Ended => Open && Status.State == Plugin.Ended;

        /// <summary>Unity's DSP buffering: its buffer length times its buffer count, in seconds.</summary>
        public static double DspBuffering
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out int count);
                return (double)length * count / AudioSettings.outputSampleRate;
            }
        }

        /// <summary>Opens a File session on `path` (an absolute path to an audio file or .m3u) and starts the music.</summary>
        public void Play(string path, string sessionDir = null)
        {
            if (Open) throw new InvalidOperationException("MusicPlayer: already playing");
            _rate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            _halfBuffer = bufferLength / (2 * _rate);
            string models = Path.Combine(Application.streamingAssetsPath, "ps", "madmom.bin");
            var config = new Plugin.Config
            {
                Source = Plugin.SourceFile,
                M = M,
                OutputRate = _rate,
                Path = Plugin.Utf8(path),
                Models = Plugin.Utf8(File.Exists(models) ? models : null),
                SessionDir = Plugin.Utf8(sessionDir),
            };
            try
            {
                int code = Plugin.Open(ref config, out IntPtr session);
                if (code != Plugin.Ok)
                {
                    _error = $"ps_open {code}: {Plugin.LastError()}";
                    return;
                }
                _session = session;
            }
            finally
            {
                Free(config.Path);
                Free(config.Models);
                Free(config.SessionDir);
            }
            Clock.DisplayLatency = DisplayLatency();
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            _source.clip = null;
            _source.Play();
        }

        static void Free(IntPtr p)
        {
            if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }

        /// <summary>One refresh, as the 3D demo assumes (vsync's queued frame); 60 Hz without a display.</summary>
        static double DisplayLatency()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            return hz > 1 ? 1.0 / hz : 1.0 / 60.0;
        }

        void Update()
        {
            if (!Open) return;
            // The main thread's DSP clock reads the end of the buffer being mixed, a whole buffer at
            // a time, so a fit through its readings runs half a buffer ahead of the audio thread's
            // buffer times: the sync spike measured frameSong 2.7, 5.5 and 10.5 ms ahead at 256, 512
            // and 1024 frames (game/evidence/spike). Each reading is taken half a buffer back.
            Clock.Sample(Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime - _halfBuffer);
            Poll();
        }

        /// <summary>
        /// Reads the plugin's status and anchor, then its new entries into <see cref="Entries"/>,
        /// replacing the previous poll's. Update does this once a frame; a reader that has taken
        /// this frame's entries may poll again for more (the --fixed-time loop waiting on the
        /// analysis), and other readers then miss what it replaced.
        /// </summary>
        public void Poll()
        {
            if (!Open) return;
            Plugin.GetStatus(_session, out Status);
            Clock.OutputLatency = Status.OutputLatency + DspBuffering;
            if (Status.State != Plugin.Waiting) Clock.SetAnchor(Status.AnchorDsp, Status.AnchorSong);
            int n = Plugin.Poll(_session, Entries);
            if (n == Plugin.ErrFailed)
            {
                _error = $"ps_poll: {Plugin.LastError()}";
                n = 0;
            }
            EntryCount = Math.Max(0, n);
        }

        // A source's filter fills the buffer the mixer plays one buffer later than the DSP time it
        // reads: the sync spike measured the music exactly one buffer behind clicks scheduled at
        // that time, at 256, 512 and 1024 frames (game/evidence/spike). So the buffer's anchor is
        // the DSP time plus its own length.
        void OnAudioFilterRead(float[] data, int channels)
        {
            Interlocked.Exchange(ref _busy, 1);
            if (Volatile.Read(ref _closing) == 0 && _session != IntPtr.Zero)
            {
                int frames = data.Length / channels;
                Plugin.RenderMusic(_session, data, frames, channels, AudioSettings.dspTime + (double)frames / _rate);
                Interlocked.Increment(ref _rendered);
            }
            Interlocked.Exchange(ref _busy, 0);
        }

        // OnDisable, not OnDestroy: Unity also calls it before a script reload in play mode, which
        // would otherwise drop the session's handle and leave the plugin's threads running.
        void OnDisable() => Close();

        /// <summary>Stops the audio callback's use of the session, then closes it.</summary>
        public void Close()
        {
            if (_session == IntPtr.Zero) return;
            Interlocked.Exchange(ref _closing, 1);
            if (_source != null) _source.Stop();
            var spin = new SpinWait();
            while (Volatile.Read(ref _busy) != 0) spin.SpinOnce();
            Plugin.Close(_session);
            _session = IntPtr.Zero;
        }
    }
}
