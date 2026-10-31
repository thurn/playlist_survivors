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
    /// Magic Wand's crystal bolts (unity-game.md, The Other Weapons and Weapon and Ability Effects):
    /// violet crystals, white at the core, flying from the hero's chest along their legs
    /// (<see cref="SimEventKind.Projectile"/>), spinning about their flight and streaking light
    /// behind them; a prism of sparks where each leaves the hero and a burst where each hits. A shot
    /// sounds its fire (wand-fire) on its grid point and each hit its own (wand-hit).
    ///
    /// - A leg runs from its from point at its time toward its aim, ending where the bolt's next event
    ///   (its turn or end) puts it, at that time: the enemy where it was hit. That event settles about
    ///   a lead ahead of frameSong, so the bolt bends onto its enemy as it closes in; until then it
    ///   flies at its aim.
    /// - Effects spawn on the first frame that reaches their song time (<see cref="EffectCues"/>).
    /// - Holy Wand, its evolution, is a stream of prism bolts: gold crystals whose longer streak of
    ///   afterimages runs through a prism's hues (<see cref="WeaponLooks.Prism"/>).
    /// </summary>
    public sealed class WandView : IPresenter, IDisposable
    {
        public const float ChestM = 1.05f, LengthM = 1.5f;
        /// <summary>The afterimages: how many, how far apart in song time, and how much each shrinks.</summary>
        const int Afterimages = 7, PrismAfterimages = 12;
        const double AfterimageS = 0.018;
        const float AfterimageShrink = 0.82f;
        /// <summary>A bolt shrinks away this long after it ends.</summary>
        const double VanishS = 0.08;
        const float SpinDegPerS = 540;
        const float CastScale = 0.7f, HitScale = 1f;
        /// <summary>The bolts' draw bounds around the hero: past the widest view's corners.</summary>
        const float BoundsM = 120;

        struct Leg
        {
            public double T, FromX, FromY, ToX, ToY, Arrive;
        }

        sealed class Bolt
        {
            public readonly List<Leg> Legs = new List<Leg>(2);
            public double End = double.PositiveInfinity;
            public float Radius;
            public int Seed;
            public bool Evolved;
        }

        readonly WeaponLooks _looks;
        readonly EffectsPresenter _effects;
        readonly SoundPresenter _sound;
        readonly Camera _camera;
        readonly Mesh _mesh;
        readonly CrystalBatch _batch, _holy;
        readonly CrystalBatch[] _prism;
        readonly int _cast, _hit;
        readonly Dictionary<long, Bolt> _bolts = new Dictionary<long, Bolt>();
        readonly List<long> _done = new List<long>();

        /// <param name="effects">The effects, or null without compute.</param>
        /// <param name="sound">The sounds, or null without them; its hits are this view's to sound.</param>
        public WandView(Camera camera, WeaponLooks looks, EffectsPresenter effects, SoundPresenter sound)
        {
            _camera = camera;
            _looks = looks;
            _effects = effects;
            _sound = sound;
            for (int w = 0; w < looks.Count; w++)
                if (looks.Of(w) == WeaponLooks.Family.Wand) sound?.VoiceHitsElsewhere(w);
            _mesh = CrystalBatch.Build("wand-bolt", 6, 0.45f, 0.45f, 0.62f, 0.38f);
            _batch = new CrystalBatch(_mesh, WeaponLooks.Wand, "wand");
            _holy = new CrystalBatch(_mesh, WeaponLooks.Holy, "holy-wand");
            _prism = new CrystalBatch[WeaponLooks.Prism.Length];
            for (int k = 0; k < _prism.Length; k++) _prism[k] = new CrystalBatch(_mesh, WeaponLooks.Prism[k], "holy-wand-prism-" + k, 0.6f);
            _cast = effects?.IndexOf("prism-cast") ?? -1;
            _hit = effects?.IndexOf("prism-hit") ?? -1;
        }

        /// <summary>Bolts drawn or still to come.</summary>
        public int Count => _bolts.Count;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Projectile:
                {
                    var p = e.AsProjectile;
                    if (_looks.Of(p.Weapon) != WeaponLooks.Family.Wand) break;
                    long key = ((long)p.Weapon << 32) | (uint)p.Projectile;
                    if (!_bolts.TryGetValue(key, out var bolt))
                    {
                        if (p.Phase != ProjectilePhase.Launch) break;
                        _bolts[key] = bolt = new Bolt { Radius = (float)p.Radius, Seed = p.Projectile * 7919, Evolved = _looks.Evolved(p.Weapon) };
                    }
                    if (p.Phase == ProjectilePhase.End) bolt.End = p.T;
                    bolt.Legs.Add(new Leg { T = p.T, FromX = p.FromX, FromY = p.FromY, ToX = p.ToX, ToY = p.ToY, Arrive = p.Arrive });
                    if (p.Phase == ProjectilePhase.Launch)
                    {
                        var dir = new Vector2((float)(p.ToX - p.FromX), (float)(p.ToY - p.FromY));
                        _effects?.Cues.Spark(p.T, _cast, new Vector3((float)p.FromX, ChestM, (float)p.FromY), dir.normalized,
                            CastScale, _looks.Colour(p.Weapon));
                    }
                    break;
                }
                case SimEventKind.Attack:
                {
                    // A shot fires on its eighth; its burst's later bolts are seen, not heard.
                    var a = e.AsAttack;
                    if (a.ByHero && _looks.Of(a.Weapon) == WeaponLooks.Family.Wand) _sound?.Request(SoundKind.WandFire, e.T, 1f, 1f, 0.6f);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.OnHero || h.Damage <= 0 || _looks.Of(h.Weapon) != WeaponLooks.Family.Wand) break;
                    _sound?.Request(SoundKind.WandHit, e.T, 1f, 1f, 0.6f);
                    _effects?.Cues.Spark(e.T, _hit, new Vector3((float)h.X, ChestM, (float)h.Y), new Vector2((float)h.DirX, (float)h.DirY),
                        HitScale, _looks.Colour(h.Weapon));
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            _done.Clear();
            foreach (var pair in _bolts)
            {
                var bolt = pair.Value;
                if (t >= bolt.End + VanishS)
                {
                    _done.Add(pair.Key);
                    continue;
                }
                if (t < bolt.Legs[0].T) continue;
                float vanish = t <= bolt.End ? 1 : 1 - (float)((t - bolt.End) / VanishS);
                float size = Mathf.Max(0.35f, bolt.Radius / 0.4571f) * vanish;
                if (!At(bolt, t, out var at, out var heading)) continue;
                var spin = Quaternion.AngleAxis((float)(t * SpinDegPerS) + bolt.Seed % 360, Vector3.forward);
                var turn = Quaternion.LookRotation(heading, Vector3.up) * spin;
                (bolt.Evolved ? _holy : _batch).Crystal(Matrix4x4.TRS(at, turn, new Vector3(size, size, size * LengthM)));
                float scale = size;
                int afterimages = bolt.Evolved ? PrismAfterimages : Afterimages;
                for (int k = 1; k <= afterimages; k++)
                {
                    double back = t - k * AfterimageS;
                    if (back < bolt.Legs[0].T) break;
                    scale *= bolt.Evolved ? 0.9f : AfterimageShrink;
                    if (!At(bolt, back, out var past, out var pastHeading)) break;
                    (bolt.Evolved ? _prism[(k + bolt.Seed) % _prism.Length] : _batch).Afterimage(Matrix4x4.TRS(past, Quaternion.LookRotation(pastHeading, Vector3.up) * spin,
                        new Vector3(scale, scale, size * LengthM * 1.3f)));
                }
            }
            foreach (long key in _done) _bolts.Remove(key);
            frame.State.Hero.PositionAt(t, out double hx, out double hy);
            var bounds = new Bounds(new Vector3((float)hx, 0, (float)hy), Vector3.one * BoundsM);
            _batch.Draw(_camera, bounds);
            _holy.Draw(_camera, bounds);
            foreach (var prism in _prism) prism.Draw(_camera, bounds);
        }

        /// <summary>Where a bolt is at song time <paramref name="t"/>, and its heading; false before it leaves or once it is gone.</summary>
        static bool At(Bolt bolt, double t, out Vector3 at, out Vector3 heading)
        {
            at = heading = default;
            int k = bolt.Legs.Count - 1;
            while (k > 0 && bolt.Legs[k].T > t) k--;
            var leg = bolt.Legs[k];
            if (t < leg.T) return false;
            // The leg ends where and when the next event puts the bolt, once that has settled.
            double toX = leg.ToX, toY = leg.ToY, end = leg.Arrive;
            if (k + 1 < bolt.Legs.Count)
            {
                var next = bolt.Legs[k + 1];
                toX = next.FromX;
                toY = next.FromY;
                end = next.T;
            }
            double span = end - leg.T;
            float u = span > 1e-9 ? Mathf.Clamp01((float)((t - leg.T) / span)) : 1;
            at = new Vector3((float)(leg.FromX + (toX - leg.FromX) * u), ChestM, (float)(leg.FromY + (toY - leg.FromY) * u));
            heading = new Vector3((float)(toX - leg.FromX), 0, (float)(toY - leg.FromY));
            if (heading.sqrMagnitude < 1e-8f)
            {
                // A leg that ends where it starts (an end): the previous leg's heading.
                if (k == 0) heading = Vector3.forward;
                else
                {
                    var prev = bolt.Legs[k - 1];
                    heading = new Vector3((float)(leg.FromX - prev.FromX), 0, (float)(leg.FromY - prev.FromY));
                    if (heading.sqrMagnitude < 1e-8f) heading = Vector3.forward;
                }
            }
            return true;
        }

        public void Dispose()
        {
            _batch.Dispose();
            _holy.Dispose();
            foreach (var prism in _prism) prism.Dispose();
            CrystalBatch.Destroy(_mesh);
        }
    }
}
