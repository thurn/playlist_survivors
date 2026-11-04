using System;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The Bloom's authored effect (ps-8z4.10; boss-rush.md, The roster: "rotating arms of spores"): while its arms are
    /// live, spores stream outward along each one in its violet (palette.json's type colour, never the danger red that
    /// fills the arms' own marks), so a turning arm reads as a jet of spores breathed from the flower rather than a bare
    /// line. Each arm puffs every <see cref="EveryS"/> at a random point along its length as it is now, just off its
    /// trailing edge (the red fill would hide them on it), drifting out along it and falling behind its turn. One GPU
    /// spark ring, a burst record per puff.
    /// </summary>
    public sealed class BloomSporePresenter : IDisposable
    {
        public const double EveryS = 1.0 / 20;
        const int PerPuff = 3;

        readonly Sparks _sparks;
        readonly System.Random _random = new System.Random(3);
        double _last = double.NaN;

        BloomSporePresenter(EffectLibrary lib, Color hue) =>
            _sparks = new Sparks(lib.Sparks, lib.Spark, Color.Lerp(hue, hue * 1.8f, 0.5f), hue);

        /// <summary>The presenter, or null when the content has no Bloom.</summary>
        public static BloomSporePresenter Create(EffectLibrary lib, Palette palette, EnemyRules enemies) =>
            enemies.IndexOf(BloomModule.ModuleId) < 0 ? null : new BloomSporePresenter(lib, palette.ForType(BloomModule.ModuleId));

        public void Present(double song, Simulation sim, Camera camera)
        {
            if (sim.Fight.Module is BloomModule && sim.Fight.InFight)
            {
                // Puffs owed since the last frame (at most a few, after a hitch).
                double from = double.IsNaN(_last) || song - _last > 0.25 ? song - EveryS : _last;
                for (double t = from + EveryS; t <= song + 1e-9; t += EveryS)
                {
                    foreach (var h in sim.Fight.Hazards)
                        if (h.Move == BossMove.Breath && t >= h.Telegraph.Land && t < h.Until) Puff(h, t);
                    _last = t;
                }
            }
            else _last = double.NaN;
            _sparks.Present(song, camera);
        }

        void Puff(in Hazard arm, double t)
        {
            var s = arm.ShapeAt(t);
            float u = (float)(0.1 + 0.85 * _random.NextDouble());
            var along = new Vector3((float)(s.X2 - s.X), 0, (float)(s.Y2 - s.Y)).normalized;
            // Behind the turn: the arm's sideways direction against its spin.
            var behind = new Vector3(-along.z, 0, along.x) * -Math.Sign(arm.SpinRadPerS);
            var at = new Vector3((float)(s.X + (s.X2 - s.X) * u), 0.5f, (float)(s.Y + (s.Y2 - s.Y) * u)) + behind * (float)(s.Radius + 0.25);
            _sparks.Burst(at, t, along + behind * 0.4f + Vector3.up * 0.15f, 0.4f, PerPuff, new Vector2(1.5f, 4f), new Vector2(0.4f, 0.9f),
                0.07f, 2.5f, 0.5f, 0.2f, 2f);
        }

        public void Queue(Camera camera) => _sparks.Queue(camera);

        public void Dispose() => _sparks.Dispose();
    }
}
