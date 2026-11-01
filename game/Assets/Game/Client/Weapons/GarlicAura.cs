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
    /// Garlic's look and sound (unity-game.md, The Other Weapons and Weapon and Ability Effects;
    /// native's slice/garlic.rs): a pulsing prism aura on the ground around the hero, at exactly the
    /// radius the sim hits, in the weapon's colour with a white-hot rim.
    /// - Each of its attack events (an incarnation, on the beat) lands at its song time: the rim
    ///   swells out to the radius, the aura flares, motes lift off its edge (effects.json's
    ///   `garlic-pulse`, with its light) and the pulse sounds (`garlic-pulse`).
    /// - Between pulses it breathes with the beat, a kick on every beat as native's, and stays until
    ///   the pulses stop (twice the latest gap), then fades.
    /// - Its hits sound its own `garlic-hit` instead of the sword's; the white chips are every weapon's.
    /// Soul Eater, its evolution, has the same aura in its own colour; its distinct layer is the
    /// chests bead's, from <see cref="Tint"/>.
    /// </summary>
    public sealed class GarlicAura : IPresenter, IDisposable
    {
        /// <summary>native's hud colours: garlic's mint, Soul Eater's teal.</summary>
        public static readonly Color Garlic = new Color(0.55f, 1f, 0.66f), SoulEater = new Color(0.3f, 0.95f, 0.9f);
        /// <summary>The rim's swell to the radius, the pulse's flare, and the fade once the pulses stop.</summary>
        public const double SwellSeconds = 0.12, FlareSeconds = 0.2, FadeSeconds = 0.4;

        struct Pulse
        {
            public double T;
            public float Radius;
            public int Weapon;
        }

        readonly Simulation _sim;
        readonly GameContent _content;
        readonly PrismGround _ground;
        readonly EffectSystem _fx;
        readonly SoundPresenter _sound;
        readonly int _effect;
        readonly List<Pulse> _pending = new List<Pulse>(8);
        Pulse _current = new Pulse { T = double.NaN };
        double _gap = 1.3;

        /// <param name="fx">The effects' runtime, or null without compute (no motes).</param>
        /// <param name="sound">The sounds, or null without a bank.</param>
        public GarlicAura(Camera camera, Simulation sim, GameContent content, EffectSystem fx, SoundPresenter sound)
        {
            _sim = sim;
            _content = content;
            _ground = new PrismGround(camera);
            _fx = fx;
            _sound = sound;
            _effect = fx?.Library.IndexOf("garlic-pulse") ?? -1;
            if (fx != null && _effect < 0) Debug.LogWarning("GarlicAura: the effect library has no 'garlic-pulse' (rebake the effects)");
            for (int w = 0; w < content.Weapons.Length; w++)
                if (IsAura(w)) sound?.VoiceHitsElsewhere(w);
        }

        bool IsAura(int weapon) => weapon >= 0 && weapon < _content.Weapons.Length && _content.Weapons[weapon].Shape.Kind == ShapeKind.Aura;

        /// <summary>The aura's colour for weapon <paramref name="weapon"/>: Garlic's, or its evolution's.</summary>
        public Color Tint(int weapon) => _content.Weapons[weapon].EvolutionOf != null ? SoulEater : Garlic;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (!a.ByHero || a.Shape != ShapeKind.Aura) break;
                    _pending.Add(new Pulse { T = a.Impact, Radius = (float)a.Reach, Weapon = a.Weapon });
                    _sound?.Request(SoundKind.GarlicPulse, a.Impact, 1f, 1f, 0.8f);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (!h.OnHero && h.Damage > 0 && IsAura(h.Weapon)) _sound?.Request(SoundKind.GarlicHit, e.T, 1f, 1f, 0.6f);
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double now = frame.Song;
            frame.State.Hero.PositionAt(now, out double hx, out double hy);
            var hero = new Vector2((float)hx, (float)hy);
            for (int i = 0; i < _pending.Count;)
            {
                var p = _pending[i];
                if (p.T > now) { i++; continue; }
                _pending.RemoveAt(i);
                if (!double.IsNaN(_current.T)) _gap = Math.Max(0.25, p.T - _current.T);
                _current = p;
                Motes(p, frame);
            }
            if (double.IsNaN(_current.T)) return;
            double since = now - _current.T;
            float fade = 1f - Mathf.Clamp01((float)((since - 2 * _gap) / FadeSeconds));
            if (fade <= 0) return;
            double beat = MusicalGrid.BeatIndexAt(_sim.Stream.Beats, now);
            float kick = double.IsNaN(beat) ? 0 : Mathf.Exp(-7f * (float)(beat - Math.Floor(beat)));
            float flare = Mathf.Exp(-(float)since / (float)FlareSeconds);
            float swell = since < SwellSeconds ? Mathf.Lerp(0.82f, 1f, 1f - Mathf.Pow(1f - (float)(since / SwellSeconds), 3)) : 1f;
            float r = _current.Radius * swell;
            var tint = Tint(_current.Weapon);
            float age = (float)(now - Math.Floor(now / 100) * 100);
            // The aura's body: a glow leaning to its rim, breathing with the beat.
            _ground.Draw(hero, r, r, new PrismGround.Look
            {
                Tint = tint, Fill = 1, Intensity = fade * (0.05f + 0.04f * kick + 0.3f * flare),
            }, age);
            // The rim, exactly at the radius the sim hits; a fainter inner ring as native's.
            _ground.Draw(hero, r * (1 + 0.025f * kick), 0.35f, new PrismGround.Look
            {
                Tint = tint, Edge = 1, Fill = 0.5f, Intensity = fade * (0.3f + 0.25f * kick + 1.1f * flare),
            }, age);
            _ground.Draw(hero, r * 0.76f, 0.2f, new PrismGround.Look
            {
                Tint = tint, Edge = 0.4f, Fill = 0.3f, Intensity = fade * (0.12f + 0.1f * kick + 0.3f * flare),
            }, age);
        }

        /// <summary>The pulse's motes off the aura's edge, from where the hero is at its time.</summary>
        void Motes(in Pulse p, in PresentFrame frame)
        {
            if (_fx == null || _effect < 0) return;
            frame.State.Hero.PositionAt(p.T, out double x, out double y);
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = _effect, Position = new Vector3((float)x, 0, (float)y), Scale = p.Radius, Time = p.T,
                Seed = EffectSystem.Hash((uint)(p.T * 10000), 0x6A1C0u), Tint = Tint(p.Weapon),
            });
        }

        public void Dispose() => _ground.Dispose();
    }
}
