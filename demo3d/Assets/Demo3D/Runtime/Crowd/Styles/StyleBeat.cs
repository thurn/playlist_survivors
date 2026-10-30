using System;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The style shaders' beat globals (StyleCommon.hlsl, after stylelab's Beat.cs), from demo3d's
    /// song clock and song map: set every frame under a `--monster-style` other than baseline. A
    /// function of song time and the hero's feet alone, so fixed-time stills reproduce.
    /// </summary>
    public sealed class StyleBeat
    {
        static readonly int TimeId = Shader.PropertyToID("_SL_Time");
        static readonly int BeatId = Shader.PropertyToID("_SL_Beat");
        static readonly int PulseId = Shader.PropertyToID("_SL_Pulse");
        static readonly int BarPulseId = Shader.PropertyToID("_SL_BarPulse");
        static readonly int EnergyId = Shader.PropertyToID("_SL_Energy");
        static readonly int HeroPosId = Shader.PropertyToID("_SL_HeroPos");
        static readonly int WaveSpeedId = Shader.PropertyToID("_SL_WaveSpeed");
        static readonly int RingGainId = Shader.PropertyToID("_SL_RingGain");

        /// <summary>The beat wave's speed, metres per beat (the lab's).</summary>
        public const float WaveSpeed = 11f;

        readonly SongMap _map;
        readonly int[] _downbeats; // beat indices of every bar's first beat, ascending

        public StyleBeat(SongMap map)
        {
            _map = map;
            var downbeats = new System.Collections.Generic.List<int>();
            for (int bar = 1, at = map.FirstDownbeatIndex; at < map.Beats.Length; at += map.BeatsPerBarAt(bar++)) downbeats.Add(at);
            _downbeats = downbeats.ToArray();
        }

        /// <summary>The globals at song time t, as (beat, pulse, bar pulse, energy).</summary>
        public Vector4 At(double t)
        {
            var b = _map.Beats;
            if (b.Length < 2 || t < b[0]) return new Vector4(0, 0, 0, _map.Energy.Length > 0 ? _map.Energy[0] : 0.5f);
            int i = Math.Min((int)Math.Floor(_map.BeatIndexAt(t)), b.Length - 2);
            float since = (float)(t - b[i]);
            float beat = i + Mathf.Clamp01(since / (float)(b[i + 1] - b[i]));
            float pulse = Mathf.Exp(-since * 7f);
            int d = Array.BinarySearch(_downbeats, i);
            if (d < 0) d = ~d - 1;
            float barPulse = d >= 0 ? Mathf.Exp(-(float)(t - b[_downbeats[d]]) * 4f) : 0f;
            float energy = _map.Energy.Length > 0 ? _map.Energy[Mathf.Clamp(i, 0, _map.Energy.Length - 1)] : 0.5f; // per beat
            return new Vector4(beat, pulse, barPulse, energy);
        }

        public void Publish(double t, Vector3 heroFeet)
        {
            var g = At(t);
            Shader.SetGlobalFloat(TimeId, (float)t);
            Shader.SetGlobalFloat(BeatId, g.x);
            Shader.SetGlobalFloat(PulseId, g.y);
            Shader.SetGlobalFloat(BarPulseId, g.z);
            Shader.SetGlobalFloat(EnergyId, g.w);
            Shader.SetGlobalVector(HeroPosId, heroFeet);
            Shader.SetGlobalFloat(WaveSpeedId, WaveSpeed);
            Shader.SetGlobalFloat(RingGainId, 0f);
        }
    }
}
