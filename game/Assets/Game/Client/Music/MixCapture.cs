using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Game.Client.Music
{
    /// <summary>
    /// Unity's final mix, captured in process: on the listener, the audio callback sees every
    /// source mixed, just before the output. It keeps the samples (up to a fixed length, allocated
    /// up front) and each buffer's DSP time and real time (a Stopwatch, which the main thread can
    /// read too), so the spike can find the music and the clicks in what Unity actually played.
    /// With <see cref="Mute"/> it then silences the buffer: a headless run is measured, not heard.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MixCapture : MonoBehaviour
    {
        public const int MaxBuffers = 1 << 16;

        float[] _samples = Array.Empty<float>();
        readonly long[] _bufferTicks = new long[MaxBuffers];
        readonly double[] _bufferDsp = new double[MaxBuffers];
        int _length, _buffers, _channels, _gaps, _full;
        double _expectDsp = double.NaN;

        public bool Mute { get; set; }

        public int Channels => _channels;
        public int Frames => _channels == 0 ? 0 : Volatile.Read(ref _length) / _channels;
        public int Buffers => Volatile.Read(ref _buffers);

        /// <summary>Buffers whose DSP time didn't follow the previous buffer's end (an audio dropout).</summary>
        public int Gaps => Volatile.Read(ref _gaps);

        /// <summary>The DSP time of the first captured sample.</summary>
        public double StartDsp => _bufferDsp[0];

        public ReadOnlySpan<long> BufferTicks => new ReadOnlySpan<long>(_bufferTicks, 0, Buffers);
        public ReadOnlySpan<double> BufferDsp => new ReadOnlySpan<double>(_bufferDsp, 0, Buffers);

        /// <summary>Real time on the capture's clock, in seconds.</summary>
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public static double TicksToSeconds(long ticks) => (double)ticks / Stopwatch.Frequency;

        /// <summary>Starts keeping up to `seconds` of the mix. Call on the main thread before the music starts.</summary>
        public void Begin(double seconds)
        {
            int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            _samples = new float[(int)(seconds * AudioSettings.outputSampleRate) * channels];
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            double dsp = AudioSettings.dspTime;
            long ticks = Stopwatch.GetTimestamp();
            int frames = data.Length / channels;
            if (_buffers < MaxBuffers && _length + data.Length <= _samples.Length)
            {
                if (_buffers > 0 && Math.Abs(dsp - _expectDsp) * AudioSettings.outputSampleRate > 0.5) _gaps++;
                _channels = channels;
                _bufferDsp[_buffers] = dsp;
                _bufferTicks[_buffers] = ticks;
                Array.Copy(data, 0, _samples, _length, data.Length);
                Volatile.Write(ref _length, _length + data.Length);
                Volatile.Write(ref _buffers, _buffers + 1);
            }
            else _full = 1;
            _expectDsp = dsp + (double)frames / AudioSettings.outputSampleRate;
            if (Mute) Array.Clear(data, 0, data.Length);
        }

        /// <summary>Writes the captured mix as a 32-bit float WAV.</summary>
        public void WriteWav(string path, int rate)
        {
            int n = Volatile.Read(ref _length), channels = Math.Max(1, _channels);
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write("RIFF".ToCharArray());
                w.Write(36 + n * 4);
                w.Write("WAVEfmt ".ToCharArray());
                w.Write(16);
                w.Write((short)3); // IEEE float
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 4);
                w.Write((short)(channels * 4));
                w.Write((short)32);
                w.Write("data".ToCharArray());
                w.Write(n * 4);
                var bytes = new byte[n * 4];
                Buffer.BlockCopy(_samples, 0, bytes, 0, bytes.Length);
                w.Write(bytes);
            }
        }

        public bool Overflowed => Volatile.Read(ref _full) != 0;
    }
}
