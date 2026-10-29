using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Song time from the audio clock (choreography.md, Time and Synchronization).
    ///
    /// The music starts with PlayScheduled half a second after Start, at a recorded DSP time.
    /// Song time is a least-squares fit of AudioSettings.dspTime against the realtime clock over
    /// the last 2 s, which smooths the DSP clock's buffer-sized steps. Each frame is drawn at its
    /// predicted present: now plus one frame plus the display's latency.
    ///
    /// In fixed-time mode song time advances exactly 1/60 s per frame and the music is muted.
    /// </summary>
    public sealed class SongClock
    {
        const int Window = 1024; // over 2 s at any frame rate the demo reaches
        readonly double[] _real = new double[Window];
        readonly double[] _dsp = new double[Window];
        int _count, _head;
        double _dspStart;
        double _startAt;
        readonly bool _fixed;
        long _fixedFrames;
        AudioSource _source;
        double _realStart;
        double _last = double.NegativeInfinity;

        /// <summary>From render to light: one refresh by default (vsync's queued frame).</summary>
        public double DisplayLatency { get; set; } = -1;

        /// <summary>From the DSP clock to the speakers: the mixer's buffers.</summary>
        public double OutputLatency { get; private set; }
        public bool Fixed => _fixed;
        public bool Started { get; private set; }

        public SongClock(bool fixedTime, double startAt)
        {
            _fixed = fixedTime;
            _startAt = startAt;
        }

        /// <summary>Schedules the music (null in fixed-time mode or without audio).</summary>
        public void Start(AudioSource source, AudioClip music, double lead = 0.5, AudioSource click = null, AudioClip clickClip = null)
        {
            Started = true;
            _realStart = Time.realtimeSinceStartupAsDouble + lead;
            if (_fixed || source == null || music == null) return;
            _source = source;
            source.clip = music;
            _dspStart = AudioSettings.dspTime + lead;
            // Sample-exact for a seek (`time` is a float and seeks coarsely in a streamed clip).
            source.timeSamples = (int)System.Math.Round(_startAt * music.frequency);
            source.PlayScheduled(_dspStart);
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int buffers);
            OutputLatency = (double)bufferLength * buffers / AudioSettings.outputSampleRate;
            if (DisplayLatency < 0)
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                DisplayLatency = hz > 1 ? 1.0 / hz : 1.0 / 60.0;
            }
            if (click != null && clickClip != null)
            {
                click.clip = clickClip;
                click.timeSamples = (int)System.Math.Round(_startAt * clickClip.frequency);
                click.PlayScheduled(_dspStart);
            }
        }

        /// <summary>Song seconds at which the frame being prepared will appear.</summary>
        public double Now()
        {
            if (_fixed) return _startAt + _fixedFrames / 60.0;
            if (_source == null) return _startAt + (Time.realtimeSinceStartupAsDouble - _realStart);
            double real = Time.realtimeSinceStartupAsDouble;
            Sample(real, AudioSettings.dspTime);
            double dsp = Fit(real);
            double frame = Time.unscaledDeltaTime > 0 ? Time.unscaledDeltaTime : 1.0 / 60.0;
            // The song heard when this frame appears: the DSP clock less the mixer's latency, plus
            // the frame and the display. Never backwards: the fit can wobble by a fraction of a ms.
            double t = _startAt + (dsp - _dspStart) - OutputLatency + frame + System.Math.Max(0, DisplayLatency);
            _last = System.Math.Max(_last, t);
            return _last;
        }

        /// <summary>Advances fixed-time mode by one frame; call once per rendered frame.</summary>
        public void EndFrame() => _fixedFrames++;

        void Sample(double real, double dsp)
        {
            _real[_head] = real;
            _dsp[_head] = dsp;
            _head = (_head + 1) % Window;
            if (_count < Window) _count++;
        }

        /// <summary>DSP time predicted at `real` from samples in the last 2 s.</summary>
        double Fit(double real)
        {
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                double x = _real[i] - real, y = _dsp[i];
                if (x < -2.0) continue;
                sx += x; sy += y; sxx += x * x; sxy += x * y; n++;
            }
            if (n < 8) return _dsp[(_head + Window - 1) % Window];
            double den = n * sxx - sx * sx;
            if (System.Math.Abs(den) < 1e-12) return sy / n;
            double slope = (n * sxy - sx * sy) / den;
            double intercept = (sy - slope * sx) / n; // at x = 0, i.e. at `real`
            return intercept;
        }
    }
}
