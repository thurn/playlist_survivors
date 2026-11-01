using System;
using System.Collections.Generic;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;

namespace Game.Client.Weapons
{
    /// <summary>
    /// Fire Trail's look and sound (unity-game.md, The Other Weapons and Weapon and Ability Effects):
    /// a burning prism wake on the tiles the hero crossed, from the sim's burn events at their song
    /// times.
    /// - Each burn lights its tile: a prism disc in the weapon's colour, white-hot at its centre, at
    ///   exactly the circle the sim hits, flaring at the pulse and glowing on while the pulses keep
    ///   burning it; embers rise from it (effects.json's `fire-trail-flame`, one pulse's burns
    ///   sharing their light as one instant's deaths do).
    /// - A tile no pulse burns again fades once the next pulse is past.
    /// - Each pulse sounds `fire-trail-pulse`, and its hits `fire-trail-hit` instead of the sword's.
    /// </summary>
    public sealed class FireTrailView : IPresenter, IDisposable
    {
        /// <summary>native's hud colour for the fire trail.</summary>
        public static readonly Color Flame = new Color(1f, 0.54f, 0.23f);
        /// <summary>A burn's flare at its pulse, and its fade once no pulse renews it.</summary>
        public const double FlareSeconds = 0.15, FadeSeconds = 0.25;

        struct Burn
        {
            public double T;
            public Vector2 At;
            public float Radius;
        }

        readonly PrismGround _ground;
        readonly EffectSystem _fx;
        readonly SoundPresenter _sound;
        readonly bool[] _trail;
        readonly int _effect;
        readonly List<Burn> _pending = new List<Burn>(64);
        // The lit tiles: each one's latest burn.
        readonly List<Burn> _lit = new List<Burn>(64);
        double _lastPulse = double.NaN, _gap = 0.5;

        /// <param name="fx">The effects' runtime, or null without compute (no embers).</param>
        /// <param name="sound">The sounds, or null without a bank.</param>
        public FireTrailView(Camera camera, GameContent content, EffectSystem fx, SoundPresenter sound)
        {
            _ground = new PrismGround(camera);
            _fx = fx;
            _sound = sound;
            _effect = fx?.Library.IndexOf("fire-trail-flame") ?? -1;
            if (fx != null && _effect < 0) Debug.LogWarning("FireTrailView: the effect library has no 'fire-trail-flame' (rebake the effects)");
            _trail = Array.ConvertAll(content.Weapons, w => w.Shape.Kind == ShapeKind.Trail);
            for (int w = 0; w < _trail.Length; w++)
                if (_trail[w]) sound?.VoiceHitsElsewhere(w);
        }

        bool IsTrail(int weapon) => weapon >= 0 && weapon < _trail.Length && _trail[weapon];

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (a.ByHero && a.Shape == ShapeKind.Trail) _sound?.Request(SoundKind.FireTrailPulse, a.Impact, 1f, 1f, 0.7f);
                    break;
                }
                case SimEventKind.Burn:
                {
                    var b = e.AsBurn;
                    _pending.Add(new Burn { T = e.T, At = new Vector2((float)b.X, (float)b.Y), Radius = (float)b.Radius });
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (!h.OnHero && h.Damage > 0 && IsTrail(h.Weapon)) _sound?.Request(SoundKind.FireTrailHit, e.T, 1f, 1f, 0.6f);
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double now = frame.Song;
            for (int i = 0; i < _pending.Count;)
            {
                var b = _pending[i];
                if (b.T > now) { i++; continue; }
                int together = 0;
                for (int j = i; j < _pending.Count; j++) if (_pending[j].T == b.T) together++;
                if (b.T != _lastPulse)
                {
                    if (!double.IsNaN(_lastPulse)) _gap = Math.Max(0.1, b.T - _lastPulse);
                    _lastPulse = b.T;
                }
                for (int j = i; j < _pending.Count;)
                {
                    if (_pending[j].T != b.T) { j++; continue; }
                    Light(_pending[j], together);
                    _pending.RemoveAt(j);
                }
            }
            float age = (float)(now - Math.Floor(now / 100) * 100);
            for (int i = 0; i < _lit.Count;)
            {
                var b = _lit[i];
                double since = now - b.T;
                float fade = 1f - Mathf.Clamp01((float)((since - 1.15 * _gap) / FadeSeconds));
                if (fade <= 0)
                {
                    _lit[i] = _lit[_lit.Count - 1];
                    _lit.RemoveAt(_lit.Count - 1);
                    continue;
                }
                float flare = Mathf.Exp(-(float)since / (float)FlareSeconds);
                _ground.Draw(b.At, b.Radius, b.Radius, new PrismGround.Look
                {
                    Tint = Flame, Edge = 0.12f, Fill = 1.2f, Core = 1, Intensity = fade * (0.4f + 1.1f * flare),
                }, age + i);
                i++;
            }
        }

        /// <summary>A burn come due: its tile lit (or relit) and its embers, one of <paramref name="together"/> at its pulse.</summary>
        void Light(in Burn b, int together)
        {
            var at = b.At;
            int k = _lit.FindIndex(l => (l.At - at).sqrMagnitude < 1e-4f);
            if (k >= 0) _lit[k] = b;
            else _lit.Add(b);
            if (_fx == null || _effect < 0) return;
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = _effect, Position = new Vector3(b.At.x, 0, b.At.y), Scale = b.Radius, Time = b.T, Tint = Flame,
                Seed = EffectSystem.Hash((uint)(b.T * 10000), (uint)(b.At.x * 64) ^ (uint)(b.At.y * 4096)),
                LightGain = 1f / Mathf.Sqrt(together),
            });
        }

        public void Dispose() => _ground.Dispose();
    }
}
