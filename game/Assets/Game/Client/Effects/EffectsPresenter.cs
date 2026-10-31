using System;
using System.Collections.Generic;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Effects
{
    /// <summary>
    /// The sim's events as effects (unity-game.md, Weapon and Ability Effects) on demo3d's compute
    /// particle runtime (<see cref="EffectSystem"/>), each spawned on the first frame that reaches
    /// its song time (<see cref="EffectCues"/>):
    /// - **the sword**: a white slash along the strike's direction at the hero, oversized on a heavy
    ///   strike, the spin's on a spin and the slam's shock on the slam, all sized to the reach; and a
    ///   crystal wave ringing out to the reach in every direction (<see cref="CrystalWaves"/>), plus a
    ///   heavy strike's cleave wave following its bands;
    /// - **deaths**: one coherent crystal burst of chips and dust from the body, thrown along the
    ///   blow and tinted by the role's gem (demo3d's crystal shatter burst); the deaths of one instant
    ///   share their light, each 1/sqrt(n) of n, so a mass kill flashes without washing the view;
    /// - **hits** that don't kill: a few white-hot chips off the body;
    /// - **structure breaks**: the larger crystal burst from the strike point
    ///   (<see cref="EffectCues.Burst"/> is the hook for a regrowth's).
    /// The hero's position at an impact comes from the frame's state.
    /// </summary>
    public sealed class EffectsPresenter : IPresenter, IDisposable
    {
        public const float SlashHeight = 1f, SlamHeight = 0.1f, BodyHeight = 0.7f, FlashHeight = 0.9f;

        readonly EffectSystem _fx;
        readonly CrystalWaves _waves;
        readonly EffectCues _cues;
        readonly Camera _camera;
        readonly int _slash, _spin, _slam, _death, _flash, _structure;
        readonly List<EffectCues.Cue> _due = new List<EffectCues.Cue>(256);
        double _last = double.NaN;

        public EffectSystem System => _fx;
        public EffectCues Cues => _cues;
        /// <summary>Effects spawned.</summary>
        public int Spawned { get; private set; }

        public EffectsPresenter(Camera camera, EffectLibrary library, RoleLooks roles)
        {
            _camera = camera;
            _cues = new EffectCues(roles);
            _fx = new EffectSystem(library);
            _fx.AddCamera(camera);
            _waves = new CrystalWaves(camera, library.Wave);
            _slash = Find(library, "slash");
            _spin = Find(library, "spin");
            _slam = Find(library, "slam");
            _death = Find(library, "crystal-burst");
            _flash = Find(library, "hit-flash");
            _structure = Find(library, "structure-burst");
        }

        static int Find(EffectLibrary library, string name)
        {
            int i = library.IndexOf(name);
            if (i < 0) Debug.LogWarning($"EffectsPresenter: the effect library has no '{name}' (rebake the effects)");
            return i;
        }

        public void OnEvent(in SimEvent e) => _cues.OnEvent(e);

        public void Present(in PresentFrame frame)
        {
            double now = frame.Song;
            float dt = double.IsNaN(_last) ? 0f : (float)Math.Max(0, now - _last);
            _last = now;
            _fx.BeginFrame();
            _cues.TakeDue(now, _due);
            foreach (var c in _due) Spawn(c, frame);
            _fx.Update(now, dt, _camera);
            _waves.Present(now);
        }

        void Spawn(in EffectCues.Cue c, in PresentFrame frame)
        {
            uint seed = Seed(c);
            switch (c.Kind)
            {
                case EffectCues.Kind.Slash:
                case EffectCues.Kind.Spin:
                case EffectCues.Kind.Slam:
                {
                    frame.State.Hero.PositionAt(c.T, out double hx, out double hy);
                    var hero = new Vector2((float)hx, (float)hy);
                    int fx = c.Kind == EffectCues.Kind.Slam ? _slam : c.Kind == EffectCues.Kind.Spin ? _spin : _slash;
                    float height = c.Kind == EffectCues.Kind.Slam ? SlamHeight : SlashHeight;
                    Add(fx, new Vector3(hero.x, height, hero.y), Yaw(c.Dir), c.Scale, seed, c.T, default, 0);
                    _waves.Strike(c.T, hero, c.Reach, c.Heavy);
                    break;
                }
                case EffectCues.Kind.Cleave:
                    _waves.Cleave(c.T, c.At, c.Reach, c.Outer, c.Bands, c.FirstBand, c.BandSeconds);
                    break;
                case EffectCues.Kind.Death:
                    Add(_death, new Vector3(c.At.x, BodyHeight * c.Scale, c.At.y), Yaw(c.Dir), c.Scale, seed, c.T, c.Tint,
                        1f / Mathf.Sqrt(Mathf.Max(1, c.Together)));
                    break;
                case EffectCues.Kind.Flash:
                {
                    var back = c.At - c.Dir * (0.3f * c.Scale);
                    Add(_flash, new Vector3(back.x, FlashHeight, back.y), Yaw(c.Dir), c.Scale, seed, c.T, default, 0);
                    break;
                }
                case EffectCues.Kind.Burst:
                    Add(_structure, new Vector3(c.At.x, c.Reach, c.At.y), Yaw(c.Dir), c.Scale, seed, c.T, c.Tint, 0);
                    break;
            }
        }

        void Add(int fx, Vector3 position, float yaw, float scale, uint seed, double t, Color tint, float lightGain)
        {
            if (fx < 0) return;
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = fx, Position = position, Yaw = yaw, Scale = scale, Seed = seed, Time = t, Tint = tint, LightGain = lightGain,
            });
            Spawned++;
        }

        /// <summary>The yaw that turns an effect's +z along a ground direction (sim x, y = world x, z); 0 for none.</summary>
        public static float Yaw(Vector2 dir) => dir.sqrMagnitude > 1e-8f ? Mathf.Atan2(dir.x, dir.y) : 0f;

        static uint Seed(in EffectCues.Cue c) =>
            EffectSystem.Hash((uint)(c.T * 10000) ^ (uint)c.Kind * 0x632BE5ABu, (uint)c.Enemy * 2654435761u ^ (uint)(c.At.x * 64) ^ (uint)(c.At.y * 4096));

        public void Dispose()
        {
            _fx.Dispose();
            _waves.Dispose();
        }
    }
}
