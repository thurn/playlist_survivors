using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// A Boss Rush fight's flavour (boss-rush.md, Look: "trails, particles, ambient arena effects and the body take the
    /// boss's own hue"; ps-8z4.6): sparks in the fighting boss's palette colour as its hazards land, never the danger red
    /// the telegraphs fill with. A projectile's launch flings a few along its way (the Treasure Goblin's coins), a
    /// signature landing bursts up and round (its bombs), any other landing flickers on its ground; each XP burst sprays
    /// behind the boss and its death bursts wide. Every fight gets it; its counts are this class's.
    /// </summary>
    public sealed class FightFlavour : IDisposable
    {
        readonly EffectLibrary _lib;
        readonly Palette _palette;
        readonly EnemyRules _cast;
        readonly Dictionary<int, (Vector2 At, Vector2 Velocity, HazardFlags Flags)> _marks = new Dictionary<int, (Vector2, Vector2, HazardFlags)>();
        Sparks _sparks;
        string _type;

        public FightFlavour(EffectLibrary lib, Palette palette, EnemyRules cast)
        {
            _lib = lib;
            _palette = palette;
            _cast = cast;
        }

        /// <summary>One settled event, in log order (<paramref name="sim"/> for the hazards' travel).</summary>
        public void Take(in SimEvent e, Simulation sim)
        {
            switch (e.Kind)
            {
                case SimEventKind.FightCall:
                    Use(_cast.Cast[e.I0].Id);
                    _marks.Clear();
                    break;
                case SimEventKind.BossTelegraph when e.I0 >= BossFight.FirstMarkId:
                {
                    var ground = e.MarkGround();
                    foreach (var h in sim.Fight.Hazards)
                        if (h.Id == e.I0)
                            _marks[e.I0] = (new Vector2((float)ground.X, (float)ground.Y), new Vector2((float)h.Vx, (float)h.Vy), h.Flags);
                    break;
                }
                case SimEventKind.BossMove when e.I0 >= BossFight.FirstMarkId:
                    if (_marks.TryGetValue(e.I0, out var m))
                    {
                        _marks.Remove(e.I0);
                        if (e.I2 == 0) Landed(m.At, m.Velocity, m.Flags, e.T);
                    }
                    break;
                case SimEventKind.RushShed when sim.Fight.InFight:
                {
                    ref readonly var b = ref sim.Fight.Lead;
                    var back = new Vector3(-Mathf.Cos((float)b.Facing), 0.6f, -Mathf.Sin((float)b.Facing));
                    Burst(new Vector2((float)e.D1, (float)e.D2), e.T, back, 0.7f, 16, new Vector2(3, 7), new Vector2(0.3f, 0.7f), 0.5f);
                    break;
                }
                case SimEventKind.BossDeath when sim.Fight.Owns(e.I0):
                    Burst(new Vector2((float)e.D0, (float)e.D1), e.T, Vector3.up, Mathf.PI, 48, new Vector2(4, 10), new Vector2(0.4f, 1.0f), 1.0f);
                    break;
            }
        }

        void Landed(Vector2 at, Vector2 velocity, HazardFlags flags, double t)
        {
            if ((flags & HazardFlags.Projectile) != 0)
            {
                // Coins flung: a glint along the shot's way.
                var way = new Vector3(velocity.x, 0.35f * velocity.magnitude, velocity.y);
                Burst(at, t, way, 0.25f, 5, new Vector2(4, 8), new Vector2(0.2f, 0.45f), 0.4f);
            }
            else if ((flags & HazardFlags.Signature) != 0)
                Burst(at, t, Vector3.up, 1.2f, 14, new Vector2(2, 6), new Vector2(0.25f, 0.55f), 0.3f);
            else Burst(at, t, Vector3.up, 0.9f, 6, new Vector2(1.5f, 4), new Vector2(0.15f, 0.35f), 0.2f);
        }

        void Burst(Vector2 at, double t, Vector3 way, float spread, int count, Vector2 speed, Vector2 life, float up) =>
            _sparks?.Burst(new Vector3(at.x, 0.4f, at.y), t, way, spread, count, speed, life, 0.04f, 3, 7, up, 1.6f);

        /// <summary>The fight's boss is of cast <paramref name="type"/>: its sparks take that type's colour.</summary>
        void Use(string type)
        {
            if (type == _type) return;
            _type = type;
            _sparks?.Dispose();
            var hue = _palette.ForType(type);
            _sparks = new Sparks(_lib.Sparks, _lib.Spark, Color.Lerp(hue, hue * 1.6f, 0.5f), hue);
        }

        public void Present(double song, Camera camera) => _sparks?.Present(song, camera);

        public void Queue(Camera camera) => _sparks?.Queue(camera);

        public void Dispose() => _sparks?.Dispose();
    }
}
