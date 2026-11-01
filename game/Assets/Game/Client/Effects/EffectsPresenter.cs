using System;
using System.Collections.Generic;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Look;
using PS.Look.Effects;
using UnityEngine;

namespace Game.Client.Effects
{
    /// <summary>
    /// The sim's events as effects (unity-game.md, Weapon and Ability Effects) on demo3d's compute
    /// particle runtime (<see cref="EffectSystem"/>), each spawned on the first frame that reaches
    /// its song time (<see cref="EffectCues"/>):
    /// - **the sword**: the trailer's gold slash along the strike's direction at the hero, at the
    ///   blade's arc and sweeping the blade's way (mirrored when the clip cuts against the effect's
    ///   own sweep), moving with the free hero, oversized on a heavy strike; the spin's on a spin; the
    ///   slam's shock on the slam, sized to the reach; and a crystal wave ringing out to the reach in
    ///   every direction (<see cref="CrystalWaves"/>), faint on a light strike, plus a heavy strike's
    ///   cleave wave following its bands;
    /// - **deaths**: one coherent crystal burst of chips and dust from the body, thrown along the
    ///   blow and tinted by the role's gem (demo3d's crystal shatter burst); the deaths of one instant
    ///   share their light, each 1/sqrt(n) of n, so a mass kill flashes without washing the view;
    /// - **hits** that don't kill: a few white-hot chips off the body;
    /// - **structure breaks**: the larger crystal burst from the strike point
    ///   (<see cref="EffectCues.Burst"/> is the hook for a regrowth's).
    /// - **the weapons' own**: each weapon's view asks for its casts and hits (<see cref="EffectCues.Spark"/>).
    /// The sword's strikes, the deaths and the structure breaks also write the look's impulse field
    /// (<see cref="ImpulseField"/>, neon-look.md, The impulse field) at their impacts, in the look's palette: a ring
    /// from the hero per strike (faint on a light one, strong on a heavy one, strongest on the slam), a small one in
    /// the role's gem per death (a mass kill's merge) and a big one in the shatter's gem per break; it is published
    /// after the frame's cues, so a ring sets out on the frame its impact reaches, and marked as a moment.
    /// The hero's position at an impact comes from the frame's state.
    /// </summary>
    public sealed class EffectsPresenter : IPresenter, IDisposable
    {
        public const float SlashHeight = 1f, SlamHeight = 0.1f, BodyHeight = 0.7f, FlashHeight = 0.9f;
        /// <summary>
        /// The slash's crescent (Hovl's Sword Slash 11, the spin's too) as it comes, seen from above: it wipes clockwise
        /// round the back left of its +z, its middle at -135 degrees (clockwise from +z). A swing draws it turned so
        /// its middle lies on the clip's arc (<see cref="EffectCues.Arc"/>), mirrored when the clip cuts the other way.
        /// </summary>
        public const int SlashSweep = 1;
        public const float SlashCentreDeg = -135;

        readonly EffectSystem _fx;
        readonly CrystalWaves _waves;
        readonly ImpulseField _impulses = new ImpulseField();
        readonly Palette _palette;
        readonly EffectCues _cues;
        readonly Camera _camera;
        readonly EffectLibrary _library;
        readonly int _slash, _spin, _slam, _death, _flash, _structure;
        readonly List<EffectCues.Cue> _due = new List<EffectCues.Cue>(256);
        double _last = double.NaN;

        public EffectSystem System => _fx;
        public ImpulseField Impulses => _impulses;
        public EffectCues Cues => _cues;
        /// <summary>Effects spawned.</summary>
        public int Spawned { get; private set; }

        public EffectsPresenter(Camera camera, EffectLibrary library, RoleLooks roles)
        {
            _camera = camera;
            _library = library;
            _cues = new EffectCues(roles);
            _fx = new EffectSystem(library);
            _fx.AddCamera(camera);
            // The on-the-beat trace (Moments), opened before the run builds its presenters.
            if (Moments.On) _fx.Marked = (t, effect) => Moments.Mark(MomentKind.Effect, t, effect, library.Effects[effect].Name);
            if (Moments.On) _impulses.Shown = (kind, t) => Moments.Mark(MomentKind.Impulse, t, (int)kind, kind.ToString());
            ImpulseField.Active = _impulses;
            _palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            _waves = new CrystalWaves(camera, CrystalWaves.ShaderIn(library));
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

        /// <summary>An effect's index in the library by its name in effects.json, or -1 (warned) without it.</summary>
        public int IndexOf(string name) => Find(_library, name);

        public void OnEvent(in SimEvent e) => _cues.OnEvent(e);

        public void Present(in PresentFrame frame)
        {
            double now = frame.Song;
            float dt = double.IsNaN(_last) ? 0f : (float)Math.Max(0, now - _last);
            _last = now;
            _fx.BeginFrame();
            _cues.TakeDue(now, _due);
            foreach (var c in _due) Spawn(c, frame);
            _impulses.Publish(now);
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
                    var pos = new Vector3(hero.x, SlamHeight, hero.y);
                    if (c.Kind == EffectCues.Kind.Slam) Add(_slam, pos, Yaw(c.Dir), c.Scale, seed, c.T, c.Tint, 0);
                    else
                    {
                        // The blade's cut, on the clip's arc and its way round, carried along with the hero as it runs.
                        var (sweep, centre) = EffectCues.Arc(c.Clip);
                        bool mirror = sweep != SlashSweep;
                        float yaw = Yaw(c.Dir) + (centre - (mirror ? -SlashCentreDeg : SlashCentreDeg)) * Mathf.Deg2Rad;
                        var v = _cues.HeroVelocityAt(c.T);
                        pos.y = SlashHeight;
                        Add(c.Kind == EffectCues.Kind.Spin ? _spin : _slash, pos, yaw, c.Scale, seed, c.T, c.Tint, c.Light, mirror, new Vector3(v.x, 0, v.y));
                    }
                    _waves.Strike(c.T, hero, c.Reach, c.Heavy);
                    float weight = c.Kind == EffectCues.Kind.Slam ? 1f : c.Heavy ? 0.7f : c.Kind == EffectCues.Kind.Spin ? 0.35f : 0f;
                    _impulses.Add(ImpulseField.Blow(hero, c.T, c.Reach, weight, _palette.HeroTrail));
                    break;
                }
                case EffectCues.Kind.Cleave:
                    _waves.Cleave(c.T, c.At, c.Reach, c.Outer, c.Bands, c.FirstBand, c.BandSeconds);
                    break;
                case EffectCues.Kind.Death:
                    Add(_death, new Vector3(c.At.x, BodyHeight * c.Scale, c.At.y), Yaw(c.Dir), c.Scale, seed, c.T, c.Tint,
                        1f / Mathf.Sqrt(Mathf.Max(1, c.Together)));
                    _impulses.Add(ImpulseField.Death(c.At, c.T, c.Tint.a > 0 ? c.Tint : _palette.Enemies[0]));
                    break;
                case EffectCues.Kind.Flash:
                {
                    var back = c.At - c.Dir * (0.3f * c.Scale);
                    Add(_flash, new Vector3(back.x, FlashHeight, back.y), Yaw(c.Dir), c.Scale, seed, c.T, default, 0);
                    break;
                }
                case EffectCues.Kind.Burst:
                    Add(_structure, new Vector3(c.At.x, c.Reach, c.At.y), Yaw(c.Dir), c.Scale, seed, c.T, c.Tint, 0);
                    // A break's burst is thrown along its blow; a regrowth's eruption (thrown up) writes no ring.
                    if (c.Dir.sqrMagnitude > 1e-8f) _impulses.Add(ImpulseField.Shatter(c.At, c.T, 3f * c.Scale, _palette.ShatterGem));
                    break;
                case EffectCues.Kind.Spark:
                    Add(c.Effect, new Vector3(c.At.x, c.Reach, c.At.y), Yaw(c.Dir), c.Scale, seed, c.T, c.Tint, 0);
                    break;
            }
        }

        void Add(int fx, Vector3 position, float yaw, float scale, uint seed, double t, Color tint, float lightGain,
            bool mirror = false, Vector3 velocity = default)
        {
            if (fx < 0) return;
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = fx, Position = position, Yaw = yaw, Scale = scale, Seed = seed, Time = t, Tint = tint, LightGain = lightGain,
                Mirror = mirror, Velocity = velocity,
            });
            Spawned++;
        }

        /// <summary>The yaw that turns an effect's +z along a ground direction (sim x, y = world x, z); 0 for none.</summary>
        public static float Yaw(Vector2 dir) => dir.sqrMagnitude > 1e-8f ? Mathf.Atan2(dir.x, dir.y) : 0f;

        static uint Seed(in EffectCues.Cue c) =>
            EffectSystem.Hash((uint)(c.T * 10000) ^ (uint)c.Kind * 0x632BE5ABu, (uint)c.Enemy * 2654435761u ^ (uint)(c.At.x * 64) ^ (uint)(c.At.y * 4096));

        public void Dispose()
        {
            if (ImpulseField.Active == _impulses)
            {
                ImpulseField.Active = null;
                ImpulseField.Unpublish();
            }
            _fx.Dispose();
            _waves.Dispose();
        }
    }
}
