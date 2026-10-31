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
    /// Driven (a --fixed-time run of a File or Replay source), the frames drive the music instead:
    /// the main thread renders it (<see cref="RenderTo"/>) and drops it, and the audio source never
    /// plays, so the plugin decodes and analyses as fast as frames compute rather than in real time.
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
        /// <summary>The frames a driven render asks for at a time.</summary>
        const int DrivenChunk = 1024;
        /// <summary>
        /// A driven analysis decodes all eight of a hindsight window's models at once: the frames
        /// wait on it, and one at a time it reads only about four times real time.
        /// </summary>
        const int DrivenHindsightJobs = 8;

        IntPtr _session;
        int _busy, _closing;
        int _rendered; // audio callbacks that reached the plugin
        volatile float _volume = 1;
        double _rate;
        AudioSource _source;
        string _error;
        volatile bool _driven;
        float[] _dropped; // a driven render's frames
        long _drivenFrames; // the frames a driven render has played
        bool _drivenStarted;

        public readonly MusicClock Clock = new MusicClock();
        public readonly StreamEntry[] Entries = new StreamEntry[MaxEntries];
        [NonSerialized] public Plugin.Status Status;

        /// <summary>What it plays; its <see cref="MusicSource.Tag"/> is the HUD's source tag.</summary>
        public MusicSource Source { get; private set; }

        /// <summary>How many of <see cref="Entries"/> this frame's poll filled.</summary>
        public int EntryCount { get; private set; }
        public bool Open => _session != IntPtr.Zero;
        /// <summary>The frames drive the music (<see cref="RenderTo"/>); Unity's output plays none of it.</summary>
        public bool Driven => _driven;
        public bool Failed => _error != null;
        public string Error => _error;
        public int Rendered => Volatile.Read(ref _rendered);
        public bool Playing => Open && Status.State == Plugin.Playing;
        public bool Ended => Open && Status.State == Plugin.Ended;
        /// <summary>The default output device changed since the session opened: the latency did, so the run ends.</summary>
        public bool DeviceChanged => Open && Status.DeviceChanged != 0;

        /// <summary>The music's level, 1 normally: a calibration screen opened in a run ducks it under its clicks.</summary>
        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Clamp01(value);
        }

        /// <summary>One of Unity's DSP buffers, in seconds.</summary>
        public static double DspBuffer
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out _);
                return (double)length / AudioSettings.outputSampleRate;
            }
        }

        /// <summary>
        /// How far past the DSP clock's reading a sound must be scheduled to start on its sample: one
        /// buffer. The buffers Unity keeps queued are behind the mixer, in the output latency, not
        /// ahead of the reading (game/evidence/spike, Scheduling headroom).
        /// </summary>
        public static double ScheduleHeadroom => DspBuffer;

        /// <summary>One display refresh; 60 Hz without a display.</summary>
        public static double Refresh
        {
            get
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                return hz > 1 ? 1.0 / hz : 1.0 / 60.0;
            }
        }

        /// <summary>Unity's DSP buffering: its buffer length times its buffer count, in seconds.</summary>
        public static double DspBuffering
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out int count);
                return (double)length * count / AudioSettings.outputSampleRate;
            }
        }

        /// <summary>
        /// Opens a session on `source` and starts the music: a file decoded, or an app tapped muted
        /// at once (its sound is the plugin's from then on, M late). `driven`: the frames render it
        /// (<see cref="RenderTo"/>), never Unity's output; not for an app, whose music comes in real time.
        /// </summary>
        public void Play(MusicSource source, string sessionDir = null, bool driven = false)
        {
            if (Open) throw new InvalidOperationException("MusicPlayer: already playing");
            if (driven && source.IsApp) throw new ArgumentException("MusicPlayer: an app's music can't be driven by frames");
            _driven = driven;
            Source = source;
            _rate = AudioSettings.outputSampleRate;
            string models = Path.Combine(Application.streamingAssetsPath, "ps", "madmom.bin");
            var config = new Plugin.Config
            {
                Source = source.Kind,
                Pid = source.Pid,
                M = M,
                OutputRate = _rate,
                Path = Plugin.Utf8(source.Path),
                Models = Plugin.Utf8(File.Exists(models) ? models : null),
                SessionDir = Plugin.Utf8(sessionDir),
                HindsightJobs = driven ? DrivenHindsightJobs : 0,
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
            Clock.DisplayLatency = Refresh; // one refresh, as the 3D demo assumes (vsync's queued frame)
            if (driven)
            {
                _dropped = new float[2 * DrivenChunk];
                return;
            }
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            // Past the real voices Unity silences the least important; the music is never one of them.
            _source.priority = 0;
            _source.clip = null;
            _source.Play();
        }

        static void Free(IntPtr p)
        {
            if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }

        void Update()
        {
            if (!Open) return;
            SampleDsp(Clock);
            Poll();
        }

        /// <summary>
        /// Records the DSP clock's reading in `clock`, once a frame. The main thread's DSP clock reads
        /// the end of the buffer being mixed, a whole buffer at a time, so a fit through its readings
        /// runs half a buffer ahead of the audio thread's buffer times: the sync spike measured
        /// frameSong 2.7, 5.5 and 10.5 ms ahead at 256, 512 and 1024 frames (game/evidence/spike).
        /// Each reading is taken half a buffer back.
        /// </summary>
        public static void SampleDsp(MusicClock clock) =>
            clock.Sample(Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime - DspBuffer / 2);

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

        /// <summary>
        /// Driven: renders the music on this thread, dropping it, until its render position reaches
        /// song time `song`, so the plugin's File source decodes and analyses up to M past it (and a
        /// Replay source reads its audio there). Nothing renders until the line holds the music's
        /// start: call again. Each buffer's anchor is its own song time, as no DSP clock plays it.
        /// </summary>
        public void RenderTo(double song)
        {
            if (!Driven || !Open) return;
            long target = (long)Math.Ceiling(song * _rate);
            while (_drivenFrames < target)
            {
                int frames = (int)Math.Min(DrivenChunk, target - _drivenFrames);
                Plugin.RenderMusic(_session, _dropped, frames, 2, _drivenFrames / _rate);
                if (!_drivenStarted)
                {
                    Plugin.GetStatus(_session, out var status);
                    if (status.State == Plugin.Waiting) return; // the line is still filling: silence, no time passed
                    _drivenStarted = true;
                }
                _drivenFrames += frames;
            }
        }

        // A source's filter fills the buffer the mixer plays one buffer later than the DSP time it
        // reads: the sync spike measured the music exactly one buffer behind clicks scheduled at
        // that time, at 256, 512 and 1024 frames (game/evidence/spike). So the buffer's anchor is
        // the DSP time plus its own length.
        void OnAudioFilterRead(float[] data, int channels)
        {
            Interlocked.Exchange(ref _busy, 1);
            if (Volatile.Read(ref _closing) == 0 && _session != IntPtr.Zero && !_driven)
            {
                int frames = data.Length / channels;
                Plugin.RenderMusic(_session, data, frames, channels, AudioSettings.dspTime + (double)frames / _rate);
                float volume = _volume;
                if (volume < 1)
                    for (int i = 0; i < data.Length; i++) data[i] *= volume;
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
