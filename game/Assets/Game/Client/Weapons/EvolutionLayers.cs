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
    /// The evolutions' own layers (unity-game.md, The Other Weapons and Weapon and Ability Effects;
    /// native's slice/evolution.rs), on top of their families' looks, each from the sim's events at
    /// their song times:
    /// - <b>Unholy Vespers</b>: a violet ring of light on the ground under its shards, kicking on the
    ///   beat, for as long as a ring turns (its permanent ring).
    /// - <b>Thunder Loop</b>: each strike and return rings the ground where it lands, a magenta
    ///   shock ring thrown out past its reach.
    /// - <b>Soul Eater</b>: each hit draws a teal soul out of its enemy, arcing into the hero's chest,
    ///   and the hero glows teal as they arrive.
    /// - <b>Bloody Tear</b>: a crimson burst on each hit, larger on a critical blow; its slashes are
    ///   crimson too (<see cref="EffectCues.SlashTint"/>).
    /// - <b>An evolution's arrival</b> (a chest's reward that evolves a weapon, at the landing): rings
    ///   in its colour sweep out from the hero over <see cref="ArrivalS"/>, with a prism of sparks,
    ///   and its sound (`evolve`).
    /// </summary>
    public sealed class EvolutionLayers : IPresenter, IDisposable
    {
        /// <summary>An evolution's arrival lasts this long (native's LIFE), and a soul's flight this long.</summary>
        public const double ArrivalS = 1.1, SoulS = 0.45;
        const double ShockS = 0.35, GlowS = 0.3;
        const float ChestM = 1.1f, SoulArcM = 1.4f, SoulM = 0.55f;
        const int SoulAfterimages = 6;
        const float BoundsM = 120;

        struct Shock
        {
            public double T, Life;
            public Vector2 At;
            /// <summary>Around the hero (an arrival), placed once its time comes; an arrival's first ring throws its sparks.</summary>
            public bool AtHero, Placed, Sparks;
            public float From, To;
            public Color Tint;
        }

        struct Soul
        {
            public double T;
            public Vector2 From;
        }

        struct Ring
        {
            public double Born, End;
            public float Radius;
        }

        readonly Simulation _sim;
        readonly GameContent _content;
        readonly WeaponLooks _looks;
        readonly EffectsPresenter _effects;
        readonly SoundPresenter _sound;
        readonly Camera _camera;
        readonly PrismGround _ground;
        readonly Mesh _mesh;
        readonly CrystalBatch _souls;
        readonly int _cast, _hit;
        readonly List<Shock> _shocks = new List<Shock>();
        readonly List<Soul> _soulList = new List<Soul>();
        readonly Dictionary<int, Ring> _vespers = new Dictionary<int, Ring>();
        readonly List<int> _done = new List<int>();
        double _lastArrival = double.NegativeInfinity;

        /// <param name="effects">The effects, or null without compute.</param>
        /// <param name="sound">The sounds, or null without them.</param>
        public EvolutionLayers(Camera camera, Simulation sim, GameContent content, WeaponLooks looks, EffectsPresenter effects, SoundPresenter sound)
        {
            _camera = camera;
            _sim = sim;
            _content = content;
            _looks = looks;
            _effects = effects;
            _sound = sound;
            _ground = new PrismGround(camera);
            _mesh = CrystalBatch.Build("soul", 5, 0.32f, 0.32f, 0.45f, 0.3f);
            _souls = new CrystalBatch(_mesh, GarlicAura.SoulEater * 0.55f, "soul", 0.3f);
            _cast = effects?.IndexOf("prism-cast") ?? -1;
            _hit = effects?.IndexOf("prism-hit") ?? -1;
        }

        /// <summary>Shock rings, souls and arrivals drawn or still to come, for tests.</summary>
        public int Count => _shocks.Count + _soulList.Count;

        bool Evolved(int weapon) => weapon >= 0 && weapon < _content.Weapons.Length && _content.Weapons[weapon].IsEvolution;

        ShapeKind Shape(int weapon) => _content.Weapons[weapon].Shape.Kind;

        /// <summary>An evolution's colour: its family's evolved colour, Soul Eater's teal for an aura.</summary>
        public Color Tint(int weapon) => Shape(weapon) == ShapeKind.Aura ? GarlicAura.SoulEater : _looks.Colour(weapon);

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Bolt:
                {
                    var b = e.AsBolt;
                    if (!Evolved(b.Weapon) || b.Kind == BoltKind.Jump) break;
                    float r = (float)b.Radius;
                    _shocks.Add(new Shock
                    {
                        T = b.T, Life = ShockS, At = new Vector2((float)b.X, (float)b.Y), From = r, To = r * 2.6f, Tint = WeaponLooks.Thunder,
                    });
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.OnHero || h.Damage <= 0 || !Evolved(h.Weapon)) break;
                    var at = new Vector3((float)h.X, ChestM, (float)h.Y);
                    var dir = new Vector2((float)h.DirX, (float)h.DirY);
                    var shape = Shape(h.Weapon);
                    if (shape == ShapeKind.Sword) _effects?.Cues.Spark(e.T, _hit, at, dir, h.Critical ? 1.6f : 0.8f, WeaponLooks.Tear);
                    else if (shape == ShapeKind.Aura)
                    {
                        _soulList.Add(new Soul { T = e.T, From = new Vector2((float)h.X, (float)h.Y) });
                        _effects?.Cues.Spark(e.T, _hit, at, dir, 0.6f, GarlicAura.SoulEater);
                    }
                    break;
                }
                case SimEventKind.Orbit:
                {
                    var o = e.AsOrbit;
                    if (!Evolved(o.Weapon) || Shape(o.Weapon) != ShapeKind.Orbit) break;
                    _vespers[o.Ring] = new Ring { Born = o.Born, End = o.End, Radius = (float)o.Radius };
                    break;
                }
                case SimEventKind.Chest:
                {
                    var c = e.AsChest;
                    if (c.Phase != ChestPhase.Reward) break;
                    var card = c.Reward;
                    if (card.Item != CardItem.Weapon || !Evolved(card.Index)) break;
                    var tint = Tint(card.Index);
                    // Each evolution of one landing sweeps its own rings, a little behind the one before.
                    for (int k = 0; k < 3; k++)
                        _shocks.Add(new Shock
                        {
                            T = e.T + 0.12 * k + 0.06 * c.Place, Life = ArrivalS - 0.12 * k, AtHero = true, From = 0.8f, To = 12f - 2.5f * k,
                            Tint = tint, Sparks = k == 0,
                        });
                    // One sound a landing, however many evolve (native's evolve-multiple).
                    if (e.T > _lastArrival + 1e-9) _sound?.Request(SoundKind.Evolve, e.T, 1f, 1f, 1f);
                    _lastArrival = e.T;
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            var hero = frame.State.Hero;
            hero.PositionAt(t, out double hx, out double hy);
            var centre = new Vector2((float)hx, (float)hy);
            float age = (float)(t - Math.Floor(t / 100) * 100);
            double beat = MusicalGrid.BeatIndexAt(_sim.Stream.Beats, t);
            float kick = double.IsNaN(beat) ? 0 : Mathf.Exp(-7f * (float)(beat - Math.Floor(beat)));

            for (int i = _shocks.Count - 1; i >= 0; i--)
            {
                var s = _shocks[i];
                if (t < s.T) continue;
                float u = (float)((t - s.T) / s.Life);
                if (u >= 1)
                {
                    _shocks.RemoveAt(i);
                    continue;
                }
                if (s.AtHero && !s.Placed)
                {
                    // Around the hero where it stood when the chest landed.
                    hero.PositionAt(s.T, out double sx, out double sy);
                    s.At = new Vector2((float)sx, (float)sy);
                    s.Placed = true;
                    _shocks[i] = s;
                    if (s.Sparks) Rays(s);
                }
                float r = Mathf.Lerp(s.From, s.To, 1 - Mathf.Pow(1 - u, 3));
                _ground.Draw(s.At, r, Mathf.Max(0.15f, 0.18f * r), new PrismGround.Look
                {
                    Tint = s.Tint, Edge = 1, Fill = 0.5f, Intensity = 1.6f * (1 - u) * (1 - u),
                }, age);
            }

            _done.Clear();
            foreach (var pair in _vespers)
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
                _ground.Draw(centre, ring.Radius + 0.35f, 0.7f, new PrismGround.Look
                {
                    Tint = WeaponLooks.Vespers, Edge = 0.8f, Fill = 0.6f, Intensity = grow * fade * (0.22f + 0.3f * kick),
                }, age);
            }
            foreach (int key in _done) _vespers.Remove(key);

            float glow = 0;
            var chest = new Vector3(centre.x, ChestM, centre.y);
            for (int i = _soulList.Count - 1; i >= 0; i--)
            {
                var s = _soulList[i];
                double since = t - s.T;
                if (since < 0) continue;
                if (since >= SoulS + GlowS)
                {
                    _soulList.RemoveAt(i);
                    continue;
                }
                if (since >= SoulS)
                {
                    glow = Mathf.Max(glow, 1 - (float)((since - SoulS) / GlowS));
                    continue;
                }
                for (int k = 0; k <= SoulAfterimages; k++)
                {
                    double back = since - k * 0.025;
                    if (back < 0) break;
                    var at = SoulAt(s, (float)(back / SoulS), chest);
                    var next = SoulAt(s, Mathf.Min(1, (float)((back + 0.02) / SoulS)), chest);
                    var way = next - at;
                    var turn = way.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(way) : Quaternion.identity;
                    float size = SoulM * Mathf.Pow(0.8f, k);
                    var m = Matrix4x4.TRS(at, turn, new Vector3(size, size, size * 1.4f));
                    if (k == 0) _souls.Crystal(m);
                    else _souls.Afterimage(m);
                }
            }
            if (glow > 0)
                _ground.Draw(centre, 1.3f, 1.3f, new PrismGround.Look { Tint = GarlicAura.SoulEater, Fill = 1, Core = 1, Intensity = 0.6f * glow }, age);
            _souls.Draw(_camera, new Bounds(new Vector3(centre.x, 0, centre.y), Vector3.one * BoundsM));
        }

        /// <summary>A soul's place at share <paramref name="u"/> of its flight: out of its enemy, arcing up, into the hero's chest.</summary>
        static Vector3 SoulAt(in Soul s, float u, Vector3 chest)
        {
            float e = u * u * (3 - 2 * u);
            var from = new Vector3(s.From.x, ChestM, s.From.y);
            return Vector3.Lerp(from, chest, e) + Vector3.up * (SoulArcM * 4 * u * (1 - u));
        }

        /// <summary>An arrival's prism of sparks, six rays out from the hero in its colour.</summary>
        void Rays(in Shock s)
        {
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _effects?.Cues.Spark(s.T, _cast, new Vector3(s.At.x + dir.x * 1.2f, ChestM, s.At.y + dir.y * 1.2f), dir, 1.4f, s.Tint);
            }
        }

        public void Dispose()
        {
            _ground.Dispose();
            _souls.Dispose();
            CrystalBatch.Destroy(_mesh);
        }
    }
}
