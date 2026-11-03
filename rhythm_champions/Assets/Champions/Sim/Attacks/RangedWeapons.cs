using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>Finite thrust heads and arrows. Flight freezes on holds; every form retains projectile supports.</summary>
    public sealed class RangedWeapons
    {
        public sealed class Flight
        {
            public WeaponEffect Effect;
            public double X, Y, ToX, ToY, Age, Duration, Delay, Width, Height;
            public int Form;
            public bool Arc;
            public void Pose(double age, out double x, out double y, out double height)
            {
                double u = Math.Max(0, Math.Min(1, age / Duration));
                x = X + (ToX - X) * u; y = Y + (ToY - Y) * u;
                height = 0.8 + (Arc ? 4 * Height * u * (1 - u) : 0);
            }
        }
        readonly Simulation sim;
        readonly List<Flight> flights = new List<Flight>();
        readonly bool[] bound = new bool[2];
        readonly StructList<int> near = new StructList<int>(64);
        public IReadOnlyList<Flight> Flights => flights;
        public RangedWeapons(Simulation sim) { this.sim = sim; }
        static readonly int[] SpearCounts = { 1, 3, 5, 15 }, BowCounts = { 1, 3, 5, 8 };
        public static int Count(WeaponKind kind, int level)
        {
            int form = CombatRules.FormIndex(level);
            return kind == WeaponKind.Spear ? SpearCounts[form] : BowCounts[form];
        }
        public static double Range(WeaponKind kind) => kind == WeaponKind.Spear ? 8 : 18;
        public void BindOwned()
        {
            for (int i = 0; i < 2; i++)
            {
                var kind = i == 0 ? WeaponKind.Spear : WeaponKind.Bow;
                if (bound[i] || sim.State.Build.Weapon(kind) == null) continue;
                Bind(kind);
                bound[i] = true;
            }
        }
        void Bind(WeaponKind kind) => sim.Weapons.Bind(kind, t => Target(kind, t) >= 0, Activate);
        int Target(WeaponKind kind, double t)
        {
            var h = sim.State.Hero;
            sim.Enemies.Near(h.X, h.Y, Range(kind) * Math.Sqrt(sim.State.Build.Stats(kind).AreaFactor * sim.State.Build.AreaAt(sim.CombatTime(t))), near);
            int best = -1; double score = kind == WeaponKind.Bow ? -1 : double.PositiveInfinity;
            foreach (int id in near.AsSpan())
            {
                ref readonly var e = ref sim.Enemies[id];
                double dx = e.X - h.X, dy = e.Y - h.Y, d = dx * dx + dy * dy;
                // Bow reaches distant pressure; Spear clears the closest narrow lane. Ties use slot order.
                if (best < 0 || (kind == WeaponKind.Bow ? d > score : d < score) || d == score && id < best)
                { best = id; score = d; }
            }
            return best;
        }
        void Activate(WeaponActivation a)
        {
            int target = Target(a.Kind, a.T);
            if (target < 0) return;
            ref readonly var enemy = ref sim.Enemies[target];
            var h = sim.State.Hero;
            double originX = h.X, originY = h.Y, targetX = enemy.X, targetY = enemy.Y;
            // Echo retains this activation's authored aim, not a new target or hero position.
            sim.Weapons.Supports.Launch(a, a.Kind == WeaponKind.Spear ? 1.2 : 1,
                a.Kind == WeaponKind.Spear ? HitWindow.Strike : HitWindow.Activation,
                effect => Launch(effect, originX, originY, targetX, targetY), Count(a.Kind, a.Level),
                a.Kind == WeaponKind.Spear ? 2 : 1);
        }
        void Launch(WeaponEffect effect, double ox, double oy, double tx, double ty)
        {
            var a = effect.Activation;
            bool spear = a.Kind == WeaponKind.Spear;
            int form = CombatRules.FormIndex(a.Level), count = WeaponSupports.VolleyCount(a.Stats, Count(a.Kind, a.Level));
            double aimX = tx - ox, aimY = ty - oy;
            // A split starts where its parent actually collided, and inherits the same aim and ledger.
            if (effect.ImpactX.HasValue && effect.ImpactY.HasValue)
            { ox = effect.ImpactX.Value; oy = effect.ImpactY.Value; }
            double dx = tx - ox, dy = ty - oy, length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9) { dx = 0; dy = 1; } else { dx /= length; dy /= length; }
            double scale = Math.Sqrt(a.AreaFactor);
            double lane = effect.Projectile - (count - 1) * 0.5;
            double delay = 0;
            if (effect.ImpactTarget.HasValue)
            {
                double aimLength = Math.Sqrt(aimX * aimX + aimY * aimY);
                if (aimLength > 1e-9) { dx = aimX / aimLength; dy = aimY / aimLength; }
                double angle = effect.Projectile == 0 ? -0.33 : 0.33;
                double c = SimMath.Cos(angle), sn = SimMath.Sin(angle);
                double fx = dx * c - dy * sn, fy = dx * sn + dy * c;
                tx = ox + fx * (spear ? 8 : 6) * scale; ty = oy + fy * (spear ? 8 : 6) * scale;
            }
            else if (spear)
            {
                if (form == 3) { lane = effect.Projectile % 5 - 2; delay = effect.Projectile / 5 * 0.12;
                    ox -= dx * (effect.Projectile / 5 * 0.7); oy -= dy * (effect.Projectile / 5 * 0.7); }
                double offset = lane * 0.65 * scale;
                ox -= dy * offset; oy += dx * offset;
                // Later ranks sweep into line before their finite thrust; no invented marching character clip.
                delay += form >= 2 ? 0.12 : 0;
                tx = ox + dx * Range(a.Kind) * scale; ty = oy + dy * Range(a.Kind) * scale;
            }
            else
            {
                if (form == 3)
                {
                    double angle = effect.Projectile * 2 * Math.PI / count + a.Sequence * 0.45;
                    ox += SimMath.Cos(angle) * 1.5; oy += SimMath.Sin(angle) * 1.5;
                    delay = effect.Projectile * 0.035;
                    dx = tx - ox; dy = ty - oy; length = Math.Sqrt(dx * dx + dy * dy);
                    if (length > 1e-9) { dx /= length; dy /= length; } else { dx = 0; dy = 1; }
                }
                double angleSpread = lane * 0.075;
                double c = SimMath.Cos(angleSpread), s = SimMath.Sin(angleSpread);
                double fx = dx * c - dy * s, fy = dx * s + dy * c;
                double distance = form >= 2 ? Math.Max(2, Math.Min(Range(a.Kind) * scale, length)) : Range(a.Kind) * scale;
                tx = ox + fx * distance; ty = oy + fy * distance;
            }
            flights.Add(new Flight { Effect = effect, X = ox, Y = oy, ToX = tx, ToY = ty,
                Duration = spear ? 0.38 : form >= 2 ? 0.85 : 0.65, Delay = delay,
                Width = (spear ? 0.20 : 0.16) * scale, Height = 3, Form = form, Arc = !spear && form >= 2 });
        }
        public void Tick(double t)
        {
            if (sim.State.Held || flights.Count == 0) return;
            // Split callbacks append flights. Only the batch present at tick start advances this tick.
            int n = flights.Count;
            for (int i = 0; i < n; i++)
            {
                var f = flights[i];
                double previous = f.Age;
                f.Age += Math.Min(Simulation.TickSeconds, Math.Max(0, t - f.Effect.T));
                if (f.Age <= f.Delay) continue;
                double old = Math.Max(0, previous - f.Delay), age = f.Age - f.Delay;
                f.Pose(old, out double x, out double y, out _);
                f.Pose(age, out double nx, out double ny, out _);
                // Arrows collide on their descent, not while high over a body. Rain's impact shares its arrow ledger.
                if (!f.Arc || age >= f.Duration)
                {
                    var shape = f.Arc && age >= f.Duration
                        ? HitShape.Circle(nx, ny, 0.9 * Math.Sqrt(f.Effect.Activation.AreaFactor))
                        : HitShape.Path(x, y, nx, ny, f.Width);
                    if (f.Arc) sim.Emit(SimEvent.RangedImpact(t, f.Effect.Activation.Kind, nx, ny,
                        0.9 * Math.Sqrt(f.Effect.Activation.AreaFactor)));
                    sim.Strikes.LandSupported(sim, new Blow { T = t, Damage = f.Effect.Damage, Shape = shape,
                        Source = f.Effect.Activation.Kind == WeaponKind.Spear ? BlowSource.Spear : BlowSource.Bow,
                        Flags = BlowFlags.Effect | (f.Effect.Spectral ? BlowFlags.Spectral : 0), Owner = -1, Part = -1 }, f.Effect);
                }
            }
            flights.RemoveAll(f => f.Effect.Finished || f.Age >= f.Duration + f.Delay);
        }
        internal ulong FoldHash(ulong h)
        {
            foreach (var f in flights)
            {
                h = Hashing.Fold(h, (ulong)f.Effect.Activation.Kind);
                h = Hashing.Fold(h, (ulong)f.Effect.Activation.Sequence);
                h = Hashing.Fold(h, Hashing.Bits(f.X)); h = Hashing.Fold(h, Hashing.Bits(f.Y));
                h = Hashing.Fold(h, Hashing.Bits(f.ToX)); h = Hashing.Fold(h, Hashing.Bits(f.ToY));
                h = Hashing.Fold(h, Hashing.Bits(f.Age)); h = Hashing.Fold(h, Hashing.Bits(f.Effect.Damage));
            }
            return h;
        }
    }
}
