using System;
using System.Collections.Generic;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Weapons
{
    /// <summary>
    /// King Bible's crystal shards (unity-game.md, The Other Weapons and Weapon and Ability Effects):
    /// gold crystals, white at the core, circling the hero at chest height on their ring
    /// (<see cref="SimEventKind.Orbit"/>), each leaning into its turn, tumbling, and streaking light
    /// along its arc; a prism of sparks where each appears and a burst where each hits. A ring sounds
    /// as it appears (bible-fire, not again when cut short) and each hit its own (bible-hit).
    ///
    /// A blade's place is the sim's: angle <c>omega (t - born) + 2 pi i / n</c> around the hero's
    /// ground position at frameSong, its size growing in and shrinking away as the sim's body does.
    /// A ring cut short (a freeze) arrives again with its new end. Unholy Vespers' shards are its
    /// violet (<see cref="WeaponLooks.Vespers"/>), on a ring with no end until the next takes over.
    /// </summary>
    public sealed class BibleView : IPresenter, IDisposable
    {
        public const float HeightM = 0.95f;
        /// <summary>A shard's length and width per metre of the blade's body radius.</summary>
        const float LengthPerM = 2.4f, WidthPerM = 1.3f;
        const int Afterimages = 8;
        /// <summary>The afterimages trail by this much of a turn each.</summary>
        const double AfterimageRad = 0.07;
        const float AfterimageShrink = 0.86f;
        const float TumbleDegPerS = 300, Lean = 25;
        const float CastScale = 0.6f, HitScale = 1f;
        const float BoundsM = 120;

        struct Ring
        {
            public int Blades;
            public double Born, End, Omega, Radius, Blade;
            public bool Evolved;
        }

        readonly WeaponLooks _looks;
        readonly EffectsPresenter _effects;
        readonly SoundPresenter _sound;
        readonly Camera _camera;
        readonly Mesh _mesh;
        readonly CrystalBatch _batch, _vespers;
        readonly int _cast, _hit;
        readonly Dictionary<long, Ring> _rings = new Dictionary<long, Ring>();
        readonly List<long> _done = new List<long>();
        /// <summary>New rings whose blades' sparks wait for their birth, when the hero's place is known.</summary>
        readonly List<(long Key, double T)> _pendingCasts = new List<(long, double)>();

        /// <param name="effects">The effects, or null without compute.</param>
        /// <param name="sound">The sounds, or null without them; its hits are this view's to sound.</param>
        public BibleView(Camera camera, WeaponLooks looks, EffectsPresenter effects, SoundPresenter sound)
        {
            _camera = camera;
            _looks = looks;
            _effects = effects;
            _sound = sound;
            for (int w = 0; w < looks.Count; w++)
                if (looks.Of(w) == WeaponLooks.Family.Bible) sound?.VoiceHitsElsewhere(w);
            _mesh = CrystalBatch.Build("bible-shard", 4, 1f, 0.35f, 0.6f, 0.4f);
            _batch = new CrystalBatch(_mesh, WeaponLooks.Bible, "bible", 0.35f);
            // Vespers' violet at full glow, and its afterimages overlapping along the orbit, read white: dimmed, it stays violet.
            _vespers = new CrystalBatch(_mesh, WeaponLooks.Vespers * 0.6f, "unholy-vespers", 0.4f);
            _cast = effects?.IndexOf("prism-cast") ?? -1;
            _hit = effects?.IndexOf("prism-hit") ?? -1;
        }

        public int Count => _rings.Count;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Orbit:
                {
                    var o = e.AsOrbit;
                    if (_looks.Of(o.Weapon) != WeaponLooks.Family.Bible) break;
                    long key = ((long)o.Weapon << 32) | (uint)o.Ring;
                    bool fresh = !_rings.ContainsKey(key);
                    _rings[key] = new Ring
                    {
                        Blades = o.Blades, Born = o.Born, End = o.End, Omega = o.Omega, Radius = o.Radius, Blade = o.Blade,
                        Evolved = _looks.Evolved(o.Weapon),
                    };
                    if (!fresh) break;
                    _pendingCasts.Add((key, o.Born));
                    _sound?.Request(SoundKind.BibleFire, o.Born, 1f, 1f, 1f);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.OnHero || h.Damage <= 0 || _looks.Of(h.Weapon) != WeaponLooks.Family.Bible) break;
                    _sound?.Request(SoundKind.BibleHit, e.T, 1f, 1f, 0.6f);
                    _effects?.Cues.Spark(e.T, _hit, new Vector3((float)h.X, HeightM, (float)h.Y), new Vector2((float)h.DirX, (float)h.DirY),
                        HitScale, _looks.Colour(h.Weapon));
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            var hero = frame.State.Hero;
            hero.PositionAt(t, out double hx, out double hy);
            for (int i = _pendingCasts.Count - 1; i >= 0; i--)
            {
                var (key, born) = _pendingCasts[i];
                if (born > t) continue;
                _pendingCasts.RemoveAt(i);
                if (!_rings.TryGetValue(key, out var ring)) continue;
                hero.PositionAt(born, out double bx, out double by);
                var colour = _looks.Colour((int)(key >> 32));
                for (int k = 0; k < ring.Blades; k++)
                {
                    double a = 2 * Math.PI * k / ring.Blades;
                    var dir = new Vector2((float)-Math.Sin(a), (float)Math.Cos(a));
                    _effects?.Cues.Spark(born, _cast, new Vector3((float)(bx + Math.Cos(a) * ring.Radius), HeightM,
                        (float)(by + Math.Sin(a) * ring.Radius)), dir, CastScale, colour);
                }
            }
            _done.Clear();
            foreach (var pair in _rings)
            {
                var ring = pair.Value;
                if (t >= ring.End + OrbitEvent.FadeS)
                {
                    _done.Add(pair.Key);
                    continue;
                }
                if (t < ring.Born) continue;
                float grow = Mathf.Clamp01((float)((t - ring.Born) / OrbitEvent.GrowS));
                float fade = 1 - Mathf.Clamp01((float)((t - ring.End) / OrbitEvent.FadeS));
                float body = (float)ring.Blade * grow * fade;
                if (body <= 1e-4f) continue;
                float sign = ring.Omega >= 0 ? 1 : -1;
                var batch = ring.Evolved ? _vespers : _batch;
                for (int k = 0; k < ring.Blades; k++)
                {
                    double a = ring.Omega * (t - ring.Born) + 2 * Math.PI * k / ring.Blades;
                    float tumble = (float)(t * TumbleDegPerS) + k * 90;
                    batch.Crystal(Pose(hx, hy, ring.Radius, a, sign, tumble, body, 1));
                    float scale = 1;
                    for (int j = 1; j <= Afterimages; j++)
                    {
                        double back = a - sign * j * AfterimageRad;
                        // Not behind where the blade started.
                        if (Math.Abs(ring.Omega) * (t - ring.Born) < j * AfterimageRad) break;
                        scale *= AfterimageShrink;
                        batch.Afterimage(Pose(hx, hy, ring.Radius, back, sign, tumble, body, scale));
                    }
                }
            }
            foreach (long key in _done) _rings.Remove(key);
            var bounds = new Bounds(new Vector3((float)hx, 0, (float)hy), Vector3.one * BoundsM);
            _batch.Draw(_camera, bounds);
            _vespers.Draw(_camera, bounds);
        }

        /// <summary>A shard at angle <paramref name="a"/> on the ring: pointing along its way round, leaning in, tumbling about its length.</summary>
        static Matrix4x4 Pose(double hx, double hy, double radius, double a, float sign, float tumble, float body, float scale)
        {
            var at = new Vector3((float)(hx + Math.Cos(a) * radius), HeightM, (float)(hy + Math.Sin(a) * radius));
            var way = new Vector3((float)-Math.Sin(a) * sign, 0, (float)Math.Cos(a) * sign);
            var turn = Quaternion.LookRotation(way, Vector3.up) * Quaternion.AngleAxis(Lean * sign, Vector3.forward)
                       * Quaternion.AngleAxis(tumble, Vector3.forward);
            float w = body * WidthPerM * scale;
            return Matrix4x4.TRS(at, turn, new Vector3(w, w, body * LengthPerM * Mathf.Lerp(1, 1.4f, 1 - scale)));
        }

        public void Dispose()
        {
            _batch.Dispose();
            _vespers.Dispose();
            CrystalBatch.Destroy(_mesh);
        }
    }
}
