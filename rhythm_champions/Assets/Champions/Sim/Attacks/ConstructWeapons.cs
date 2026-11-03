using System;

namespace Champions.Sim
{
    /// <summary>Persistent visuals, finite beat-bound sweeps/volleys. Overlapping members share each strike's ledger.</summary>
    public sealed class ConstructWeapons
    {
        readonly Simulation sim;
        readonly StructList<int> near = new StructList<int>(2048);
        double nextSummon, summonX, summonY, summonDx, summonDy, heldSeconds;
        public const int MaxSentinels = 3;
        public const double SentinelLifetime = 2.4;
        public ConstructWeapons(Simulation sim)
        {
            this.sim = sim;
            sim.Resumed += elapsed =>
            {
                heldSeconds += elapsed;
                if (nextSummon > 0) nextSummon += elapsed;
            };
            sim.Weapons.Bind(WeaponKind.OrbitBlades, t => HasTarget(WeaponKind.OrbitBlades, t), Fire);
            sim.Weapons.Bind(WeaponKind.Sentinel, t => HasTarget(WeaponKind.Sentinel, t), Fire);
        }
        public static double Radius(WeaponKind kind, int level)
        {
            int form = CombatRules.FormIndex(level);
            return kind == WeaponKind.OrbitBlades ? (form == 0 ? 2.5 : 3.4) : 2.3;
        }
        bool HasTarget(WeaponKind kind, double t)
        {
            var w = sim.State.Build.Weapon(kind);
            double radius = Radius(kind, w.Level) * Math.Sqrt(sim.State.Build.Stats(kind).AreaFactor * sim.State.Build.AreaAt(sim.CombatTime(t)));
            sim.HeroWas(t, out double x, out double y);
            double dx, dy;
            if (kind == WeaponKind.Sentinel && t < nextSummon)
            {
                if (t >= w.PersistentUntil) return false;
                x = summonX; y = summonY; dx = summonDx; dy = summonDy;
                sim.Enemies.Near(x, y, radius * 3, near);
            }
            else Aim(x, y, radius * 3, out dx, out dy);
            for (int i = 0; i < near.Count; i++)
            {
                var e = sim.Enemies[near[i]];
                if (Touches(kind, w.Level, w.ActivationSequence + 1, x, y, dx, dy, radius, e.X, e.Y, e.Radius) ||
                    kind == WeaponKind.OrbitBlades && CombatRules.FormIndex(w.Level) == 2 &&
                    Touches(kind, w.Level, w.ActivationSequence + 2, x, y, dx, dy, radius, e.X, e.Y, e.Radius)) return true;
            }
            return false;
        }
        void Aim(double x, double y, double reach, out double dx, out double dy)
        {
            dx = sim.State.Hero.FacingX; dy = sim.State.Hero.FacingY;
            double best = double.PositiveInfinity;
            // Queries never nest on this scratch list.
            sim.Enemies.Near(x, y, reach, near);
            for (int i = 0; i < near.Count; i++)
            {
                var e = sim.Enemies[near[i]];
                double ax = e.X - x, ay = e.Y - y, d = ax * ax + ay * ay;
                if (d >= best) continue;
                best = d;
                if (d > 1e-12) { dx = ax / Math.Sqrt(d); dy = ay / Math.Sqrt(d); }
            }
        }
        void Fire(WeaponActivation a)
        {
            sim.HeroWas(a.T, out double x, out double y);
            double radius = Radius(a.Kind, a.Level) * Math.Sqrt(a.AreaFactor);
            Aim(x, y, radius * 3, out double dx, out double dy);
            long sweep = a.Sequence;
            if (a.Kind == WeaponKind.OrbitBlades && CombatRules.FormIndex(a.Level) == 2)
            {
                bool occupied = false;
                for (int i = 0; i < near.Count; i++)
                {
                    var e = sim.Enemies[near[i]];
                    occupied |= Touches(a.Kind, a.Level, sweep, x, y, dx, dy, radius, e.X, e.Y, e.Radius);
                }
                // Prefer alternating axes; an isolated foe on the other axis must not hold the phase forever.
                if (!occupied) sweep++;
            }
            var state = sim.State.Build.Weapon(a.Kind);
            if (a.Kind == WeaponKind.Sentinel)
            {
                if (a.T >= nextSummon)
                {
                    summonX = x; summonY = y; summonDx = dx; summonDy = dy;
                    nextSummon = a.T + 4;
                    WeaponSupports.Refresh(state, a.Stats, a.T, SentinelLifetime);
                }
                else { x = summonX; y = summonY; dx = summonDx; dy = summonDy; }
            }
            else WeaponSupports.Refresh(state, a.Stats, a.T, .5);
            // Echo captures this roster's end, never a subsequent summon or activation's lifetime.
            double until = state.PersistentUntil, heldAtLaunch = heldSeconds;
            int phase = a.Kind == WeaponKind.Sentinel
                ? (int)((a.Sequence - 1) % (CombatRules.FormIndex(a.Level) >= 2 ? 3 : 2))
                : (int)((sweep - 1) % 2);
            sim.Weapons.Supports.Launch(a, a.Kind == WeaponKind.Sentinel ? .9 : .45, HitWindow.Pulse,
                effect =>
                {
                    double area = sim.State.Build.AreaAt(sim.CombatTime(effect.T));
                    double radius = Radius(a.Kind, a.Level) * Math.Sqrt(a.Stats.AreaFactor * area);
                    double duration = Math.Max(.4, until + heldSeconds - heldAtLaunch - effect.T);
                    sim.Emit(SimEvent.ConstructAction(effect.T, a.Kind, CombatRules.FormIndex(a.Level),
                        phase | (area > 1 ? 4 : 0), x, y, dx, dy, radius, duration));
                    foreach (var shape in Shapes(a.Kind, a.Level, sweep, x, y, dx, dy, radius))
                        sim.Strikes.LandWeapon(sim, new Blow { T = effect.T, Shape = shape, Damage = effect.Damage,
                            Source = a.Kind == WeaponKind.Sentinel ? BlowSource.Sentinel : BlowSource.OrbitBlades,
                            Tags = HitTags.Summon | HitTags.Area, Flags = BlowFlags.Spectral, Owner = -1, Part = -1 }, effect);
                }, authoredTargets: int.MaxValue / 2);
        }
        internal ulong FoldHash(ulong h)
        {
            if (nextSummon == 0) return h;
            h = Hashing.Fold(h, Hashing.Bits(nextSummon));
            h = Hashing.Fold(h, Hashing.Bits(summonX));
            h = Hashing.Fold(h, Hashing.Bits(summonY));
            h = Hashing.Fold(h, Hashing.Bits(summonDx));
            return Hashing.Fold(h, Hashing.Bits(summonDy));
        }
        static bool Touches(WeaponKind kind, int level, long sequence, double x, double y, double dx, double dy,
            double radius, double tx, double ty, double tr)
        {
            foreach (var shape in Shapes(kind, level, sequence, x, y, dx, dy, radius))
                if (shape.Touches(tx, ty, tr)) return true;
            return false;
        }
        static System.Collections.Generic.IEnumerable<HitShape> Shapes(WeaponKind kind, int level, long sequence,
            double x, double y, double dx, double dy, double r)
        {
            int form = CombatRules.FormIndex(level);
            if (kind == WeaponKind.OrbitBlades)
            {
                // Swept blades plus inner guard cover the whole disk, including when empowered.
                yield return HitShape.Circle(x, y, r);
                if (form >= 2)
                {
                    double turn = sequence % 2 == 0 ? Math.PI / 2 : 0;
                    int arms = form == 3 ? 4 : 2;
                    for (int k = 0; k < arms; k++)
                    {
                        double a = turn + k * Math.PI * 2 / arms;
                        yield return HitShape.Path(x, y, x + SimMath.Cos(a) * r * 1.3, y + SimMath.Sin(a) * r * 1.3, r * .3);
                    }
                }
            }
            else
            {
                // Sentry sits alongside the hero; empowerment expands a filled attack, never a hollow ring.
                double sx = x + dx * r * .6, sy = y + dy * r * .6;
                yield return HitShape.Circle(sx, sy, r);
                if (form >= 1) yield return HitShape.Path(sx, sy, sx + dx * r * .7, sy + dy * r * .7, r * .7);
                if (form >= 2) yield return HitShape.Circle(sx + dx * r, sy + dy * r, r);
                if (form == 3)
                    for (int k = -1; k <= 1; k += 2)
                        yield return HitShape.Circle(sx + dx * r - dy * r * .75 * k,
                            sy + dy * r + dx * r * .75 * k, r);
            }
        }
    }
}
