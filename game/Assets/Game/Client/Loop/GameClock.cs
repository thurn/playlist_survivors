using Game.Client.Music;

namespace Game.Client.Loop
{
    /// <summary>The clocks' raw readings at the start of a frame.</summary>
    public struct ClockReadings
    {
        /// <summary>Real time (Time.realtimeSinceStartupAsDouble), the Input System's timeline too.</summary>
        public double Real;
        /// <summary>The main thread's DSP clock (AudioSettings.dspTime).</summary>
        public double Dsp;
        /// <summary>Unity's frame time (Time.timeSinceLevelLoadAsDouble): exactly 1/60 s a frame under --fixed-time.</summary>
        public double Frame;
    }

    /// <summary>
    /// One frame's song times (unity-game.md, Clocks and The Sim Lead):
    /// <code>
    /// Song      = songAtDsp(dspNow)                  // the mixer's position
    /// Heard     = heardSong(now)                     // Song less the output latency
    /// FrameSong = heardSong(now + displayLatency) + videoCalibration, never backwards
    /// Frontier  = Song + margin                      // margin: 2 frames + 1 DSP buffer
    /// Lead      = Frontier - Heard                   // outputLatency + margin
    /// </code>
    /// </summary>
    public struct FrameTimes
    {
        public double Song;
        public double Heard;
        public double FrameSong;
        public double Frontier;
        public double Lead => Frontier - Heard;
    }

    /// <summary>Song time for the frame loop: the music's (<see cref="MusicTimes"/>) or Unity's frames' (<see cref="FrameClock"/>).</summary>
    public interface IGameClock
    {
        /// <summary>Whether song time is defined yet: the music has its anchor.</summary>
        bool Ready { get; }

        /// <summary>This frame's times. Call once a frame, after the music's clock sample.</summary>
        FrameTimes Frame(in ClockReadings now);

        /// <summary>The song time a key pressed at real time `real` (its Input System timestamp) was played against.</summary>
        double KeySong(double real);

        /// <summary>
        /// The sound-scheduling hook: the DSP time at which the mixer plays song time `song`, for
        /// PlayScheduled. False when the mixer is already past it, or no music is playing in time
        /// with the frames: the sound is then dropped, never started late (unity-game.md, Sound Timing).
        /// </summary>
        bool TryDspAt(double song, out double dsp);
    }

    /// <summary>
    /// Live song time from the music's clock (<see cref="MusicClock"/>): the frontier rides the
    /// mixer's position, frameSong the light's.
    /// </summary>
    public sealed class MusicTimes : IGameClock
    {
        readonly MusicClock _clock;
        double _earliestDsp = double.PositiveInfinity;

        /// <param name="frame">The nominal frame interval (a refresh).</param>
        /// <param name="dspBuffer">One DSP buffer, in seconds.</param>
        /// <param name="dspBuffering">Unity's whole DSP buffering: the mixer may be this far past the DSP clock's reading.</param>
        public MusicTimes(MusicClock clock, double frame, double dspBuffer, double dspBuffering)
        {
            _clock = clock;
            NominalFrame = frame;
            DspBuffer = dspBuffer;
            DspBuffering = dspBuffering;
        }

        public double NominalFrame { get; }
        public double DspBuffer { get; }
        public double DspBuffering { get; }

        /// <summary>The frame interval the margin covers: the nominal one, or longer frames' smoothed length.</summary>
        public double FrameInterval { get; private set; }

        /// <summary>How far past the mixer's position the sim steps: two frames and one DSP buffer.</summary>
        public double Margin => 2 * (FrameInterval > NominalFrame ? FrameInterval : NominalFrame) + DspBuffer;

        public bool Ready => _clock.Anchored;

        /// <summary>Folds a frame's length into <see cref="FrameInterval"/> (an average over about 16 frames).</summary>
        public void Measure(double frameLength)
        {
            if (frameLength <= 0 || frameLength > 0.25) return; // a hitch or a pause, not a frame rate
            FrameInterval = FrameInterval == 0 ? frameLength : FrameInterval + (frameLength - FrameInterval) / 16;
        }

        public FrameTimes Frame(in ClockReadings now)
        {
            _earliestDsp = now.Dsp + DspBuffering;
            double song = _clock.SongAtDsp(_clock.DspAt(now.Real));
            return new FrameTimes
            {
                Song = song,
                Heard = song - _clock.OutputLatency,
                FrameSong = _clock.FrameSong(now.Real),
                Frontier = song + Margin,
            };
        }

        public double KeySong(double real) => _clock.KeySong(real);

        public bool TryDspAt(double song, out double dsp)
        {
            dsp = _clock.DspAtSong(song);
            return _clock.Anchored && dsp >= _earliestDsp;
        }
    }

    /// <summary>
    /// Song time from Unity's frame time: frame-exact under --fixed-time (frame n shows song
    /// n/60), for stills and filmstrips, and for runs without music. The lead is the built-in
    /// speakers' (the sync spike's 38 ms output latency plus the margin at 60 fps and a 256-frame
    /// buffer), so the sim runs as far ahead as it would live. No music plays in time with these
    /// frames, so no sound can be scheduled.
    /// </summary>
    public sealed class FrameClock : IGameClock
    {
        public const double OutputLatency = 0.038;
        public const double Margin = 2.0 / Boot.FixedFps + 256.0 / 48000;

        double _frameSong;

        public bool Ready => true;

        public FrameTimes Frame(in ClockReadings now)
        {
            _frameSong = now.Frame;
            return new FrameTimes
            {
                Song = now.Frame + OutputLatency,
                Heard = now.Frame,
                FrameSong = now.Frame,
                Frontier = FrontierAt(now.Frame),
            };
        }

        /// <summary>The frontier of the frame at Unity frame time `frame`.</summary>
        public static double FrontierAt(double frame) => frame + OutputLatency + Margin;

        /// <summary>A key can't be placed between frames on this clock: it was heard at the latest frame's song time.</summary>
        public double KeySong(double real) => _frameSong;

        public bool TryDspAt(double song, out double dsp)
        {
            dsp = double.NaN;
            return false;
        }
    }
}
