using System;
using System.Collections.Generic;
using PS.Choreography;
using PS.Look;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The style shaders' beat globals (StyleCommon.hlsl, after stylelab's Beat.cs), from demo3d's
    /// song clock and song map, published through the look package's <see cref="BeatGlobals"/>, and
    /// the horde's beat pulse (crowd.md, The beat pulse): every
    /// monster's squash and stretch (<c>_SL_Bounce</c>, CrowdCommon.hlsl's SkinVertex, so every
    /// style and the baseline) and brightness throb (<c>_SL_Throb</c>). Set every frame of every
    /// run. A function of song time and the hero's feet alone, so fixed-time stills reproduce.
    /// </summary>
    public sealed class StyleBeat
    {
        static readonly int BounceId = Shader.PropertyToID("_SL_Bounce");
        static readonly int BouncePrevId = Shader.PropertyToID("_SL_BouncePrev");
        static readonly int ThrobId = Shader.PropertyToID("_SL_Throb");

        /// <summary>
        /// The pulse's squash on a beat and a downbeat at full energy (the share of its height a monster loses on the
        /// impact), the energy's floor (a beat at energy 0 has this share of it), an accent's extra share at strength 1
        /// and the cap. A pulsed section's every beat hits; a free section's anchors take a downbeat's at their strength.
        /// </summary>
        public const float BeatSquash = 0.13f, DownbeatSquash = 0.22f, EnergyFloor = 0.5f, AccentBoost = 0.4f, MaxSquash = 0.28f;
        /// <summary>How near a beat an accent lands to strengthen it, seconds (off-beat accents add nothing).</summary>
        const double AccentWindow = 0.06;
        /// <summary>The rebound's period and decay time (seconds): squashed on the impact, stretched half a period later.</summary>
        const float Period = 0.3f, Decay = 0.14f;
        /// <summary>The anticipation before a hit: a rise to this share of its squash as a stretch over the last Lead seconds.</summary>
        const float Anticipation = 0.3f, Lead = 0.08f;
        /// <summary>How fast the throb fades after its hit, per second.</summary>
        const float ThrobFade = 9f;

        readonly SongMap _map;
        readonly int[] _downbeats; // beat indices of every bar's first beat, ascending
        readonly double[] _hitAt;  // the pulse's hits, ascending song times
        readonly float[] _hitSquash;
        float _bounce;
        bool _published;

        public StyleBeat(SongMap map)
        {
            _map = map;
            var downbeats = new List<int>();
            for (int bar = 1, at = map.FirstDownbeatIndex; at < map.Beats.Length; at += map.BeatsPerBarAt(bar++)) downbeats.Add(at);
            _downbeats = downbeats.ToArray();
            var hits = Hits(map, _downbeats);
            _hitAt = hits.ConvertAll(h => h.time).ToArray();
            _hitSquash = hits.ConvertAll(h => h.squash).ToArray();
        }

        /// <summary>
        /// The pulse's hits: every beat of a pulsed section (a downbeat's harder, an accent on it harder still) and
        /// a free section's anchors, each scaled by the song map's energy there.
        /// </summary>
        static List<(double time, float squash)> Hits(SongMap map, int[] downbeats)
        {
            var hits = new List<(double time, float squash)>();
            var b = map.Beats;
            foreach (var sec in map.Sections)
            {
                if (sec.Feel == SectionFeel.Free)
                {
                    foreach (var a in sec.Anchors)
                        hits.Add((a.Time, Math.Min(MaxSquash, DownbeatSquash * a.Strength * EnergyAt(map, (int)Math.Floor(map.BeatIndexAt(a.Time))))));
                    continue;
                }
                int from = sec == map.Sections[0] ? 0 : sec.FirstBeat; // the pickup beats before the first downbeat pulse too
                for (int i = from; i < Math.Min(sec.EndBeat, b.Length); i++)
                {
                    float squash = (Array.BinarySearch(downbeats, i) >= 0 ? DownbeatSquash : BeatSquash) * EnergyAt(map, i);
                    float accent = 0;
                    foreach (var a in map.Accents)
                        if (Math.Abs(a.Time - b[i]) <= AccentWindow) accent = Math.Max(accent, a.Strength);
                    hits.Add((b[i], Math.Min(MaxSquash, squash * (1 + AccentBoost * accent))));
                }
            }
            hits.Sort((x, y) => x.time.CompareTo(y.time));
            return hits;
        }

        static float EnergyAt(SongMap map, int beat) =>
            EnergyFloor + (1 - EnergyFloor) * (map.Energy.Length > 0 ? map.Energy[Mathf.Clamp(beat, 0, map.Energy.Length - 1)] : 0.5f);

        /// <summary>
        /// The horde's pulse at song time t: x the bounce (the height's change as a share: negative squashed, positive
        /// stretched), y the throb (1 on a full-energy downbeat's impact, fading within the beat). The impact lands on
        /// the hit's own frame: the bounce snaps from the anticipation's stretch to the full squash, then rebounds.
        /// </summary>
        public Vector2 PulseAt(double t)
        {
            int k = Array.BinarySearch(_hitAt, t);
            if (k < 0) k = ~k - 1;
            float bounce = 0, throb = 0;
            if (k >= 0)
            {
                float since = (float)(t - _hitAt[k]), a = _hitSquash[k];
                bounce = -a * Mathf.Exp(-since / Decay) * Mathf.Cos(2 * Mathf.PI * since / Period);
                throb = a / DownbeatSquash * Mathf.Exp(-since * ThrobFade);
            }
            if (k + 1 < _hitAt.Length)
            {
                float until = (float)(_hitAt[k + 1] - t);
                if (until < Lead)
                {
                    float u = 1 - until / Lead;
                    bounce += Anticipation * _hitSquash[k + 1] * u * u * (3 - 2 * u);
                }
            }
            return new Vector2(bounce, throb);
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
            BeatGlobals.Publish(t, At(t), heroFeet);
            var pulse = PulseAt(t);
            Shader.SetGlobalFloat(BouncePrevId, _published ? _bounce : pulse.x); // the previous frame's, for motion vectors
            Shader.SetGlobalFloat(BounceId, pulse.x);
            Shader.SetGlobalFloat(ThrobId, pulse.y);
            _bounce = pulse.x;
            _published = true;
        }
    }
}
