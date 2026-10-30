using System;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>
    /// The song's clock and its beat. Live, the song plays from <c>startAt</c> and loops back
    /// there at the end, timed by the audio DSP clock; for stills (or with no sound) the clock
    /// steps with the frame. Every frame it publishes the beat to the shaders' globals
    /// (StyleLabCommon.hlsl).
    /// </summary>
    public sealed class Beat
    {
        static readonly int TimeId = Shader.PropertyToID("_SL_Time");
        static readonly int BeatId = Shader.PropertyToID("_SL_Beat");
        static readonly int PulseId = Shader.PropertyToID("_SL_Pulse");
        static readonly int BarPulseId = Shader.PropertyToID("_SL_BarPulse");
        static readonly int EnergyId = Shader.PropertyToID("_SL_Energy");
        static readonly int WaveSpeedId = Shader.PropertyToID("_SL_WaveSpeed");

        public const float WaveSpeed = 11f; // metres per beat

        readonly StyleLabData _data;
        readonly AudioSource _source;
        readonly float _startAt;
        double _dspStart;
        double _manual;

        public double Time { get; private set; }
        public float BeatF { get; private set; }
        public int Index { get; private set; }
        public float Phase => BeatF - Index;
        public float Pulse { get; private set; }
        public float BeatLength { get; private set; } = 0.39f;
        public bool IsDownbeat(int beat) => ((beat - _data.firstDownbeat) % _data.beatsPerBar + _data.beatsPerBar) % _data.beatsPerBar == 0;
        public int BarOf(int beat) => Mathf.FloorToInt((beat - _data.firstDownbeat) / (float)_data.beatsPerBar);
        public double TimeOfBeat(int beat) => _data.beats[Mathf.Clamp(beat, 0, _data.beats.Length - 1)];

        public Beat(StyleLabData data, AudioSource source, float startAt)
        {
            _data = data;
            _source = source;
            _startAt = startAt;
            _manual = startAt;
            if (_source != null) Restart();
            Time = startAt;
        }

        void Restart()
        {
            _source.Stop();
            _source.clip = _data.song;
            _source.time = _startAt;
            _dspStart = AudioSettings.dspTime + 0.1;
            _source.PlayScheduled(_dspStart);
        }

        public void Update(float dt)
        {
            if (_source != null)
            {
                Time = _startAt + Math.Max(0, AudioSettings.dspTime - _dspStart);
                if (Time > _data.song.length - 0.5) Restart();
            }
            else
            {
                _manual += dt;
                if (_manual > _data.beats[_data.beats.Length - 1] - 1) _manual = _startAt;
                Time = _manual;
            }
            Publish();
        }

        void Publish()
        {
            var b = _data.beats;
            int i = Array.BinarySearch(b, (float)Time);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, b.Length - 2);
            BeatLength = b[i + 1] - b[i];
            float since = (float)(Time - b[i]);
            Index = i;
            BeatF = i + Mathf.Clamp01(since / BeatLength);
            Pulse = Mathf.Exp(-since * 7f);
            int down = i - (((i - _data.firstDownbeat) % _data.beatsPerBar) + _data.beatsPerBar) % _data.beatsPerBar;
            float barPulse = Mathf.Exp(-(float)(Time - b[Mathf.Max(down, 0)]) * 4f);
            int bar = Mathf.Clamp(BarOf(i), 0, Math.Max(_data.energy.Length - 1, 0));
            Shader.SetGlobalFloat(TimeId, (float)Time);
            Shader.SetGlobalFloat(BeatId, BeatF);
            Shader.SetGlobalFloat(PulseId, Pulse);
            Shader.SetGlobalFloat(BarPulseId, barPulse);
            Shader.SetGlobalFloat(EnergyId, _data.energy.Length > 0 ? _data.energy[bar] : 0.5f);
            Shader.SetGlobalFloat(WaveSpeedId, WaveSpeed);
        }
    }
}
