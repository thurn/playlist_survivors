using System;

namespace Game.Client.Music
{
    /// <summary>
    /// The client's clocks (unity-game.md, Clocks), from the plugin's anchor:
    /// <code>
    /// songAtDsp(d) = anchor.song + (d - anchor.dsp)
    /// heardSong(r) = songAtDsp(dspAt(r)) - outputLatency
    /// frameSong    = heardSong(now + displayLatency) + videoCalibration, never backwards
    /// </code>
    /// dspAt(r) is a least-squares fit of the DSP clock against real time over the last 2 s (as
    /// the 3D demo's song clock), which smooths the DSP clock's buffer-sized steps. The output
    /// latency is the device's, as the plugin reports it, plus Unity's DSP buffering. Times are
    /// seconds; real time is the caller's clock (Time.realtimeSinceStartupAsDouble).
    /// </summary>
    public sealed class MusicClock
    {
        public const double FitWindow = 2.0;
        const int Samples = 1024; // over 2 s at any frame rate

        readonly double[] _real = new double[Samples];
        readonly double[] _dsp = new double[Samples];
        int _count, _head;
        double _lastFrame = double.NegativeInfinity;

        public double AnchorDsp { get; private set; }
        public double AnchorSong { get; private set; }
        public bool Anchored { get; private set; }

        /// <summary>From the DSP clock to the speakers: the device's latency plus Unity's buffering.</summary>
        public double OutputLatency { get; set; }

        /// <summary>From a frame's render to its light: a refresh plus the compositor's delay.</summary>
        public double DisplayLatency { get; set; }

        public double VideoCalibration { get; set; }

        /// <summary>Takes the plugin's latest anchor.</summary>
        public void SetAnchor(double dsp, double song)
        {
            AnchorDsp = dsp;
            AnchorSong = song;
            Anchored = true;
        }

        /// <summary>Records the DSP clock's reading at real time `real` (once a frame).</summary>
        public void Sample(double real, double dsp)
        {
            _real[_head] = real;
            _dsp[_head] = dsp;
            _head = (_head + 1) % Samples;
            if (_count < Samples) _count++;
        }

        public double SongAtDsp(double dsp) => AnchorSong + (dsp - AnchorDsp);

        /// <summary>The DSP time at which the mixer reaches song time `song` (for PlayScheduled).</summary>
        public double DspAtSong(double song) => AnchorDsp + (song - AnchorSong);

        /// <summary>The DSP time at real time `real`, fitted over the samples of the last 2 s before it.</summary>
        public double DspAt(double real)
        {
            if (_count == 0) return 0;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                double x = _real[i] - real;
                if (x < -FitWindow || x > 0) continue;
                double y = _dsp[i];
                sx += x; sy += y; sxx += x * x; sxy += x * y; n++;
            }
            int latest = (_head + Samples - 1) % Samples;
            if (n < 8) return _dsp[latest] + (real - _real[latest]);
            double den = n * sxx - sx * sx;
            if (Math.Abs(den) < 1e-12) return sy / n;
            double slope = (n * sxy - sx * sy) / den;
            return (sy - slope * sx) / n; // the fit at x = 0, i.e. at `real`
        }

        /// <summary>The song time heard from the speakers at real time `real`.</summary>
        public double HeardSong(double real) => SongAtDsp(DspAt(real)) - OutputLatency;

        /// <summary>
        /// The song time a frame shows: heard at its light, `now` plus the display latency. Call
        /// once a frame, after <see cref="Sample"/>; it never runs backwards.
        /// </summary>
        public double FrameSong(double now)
        {
            double t = HeardSong(now + DisplayLatency) + VideoCalibration;
            _lastFrame = Math.Max(_lastFrame, t);
            return _lastFrame;
        }
    }
}
