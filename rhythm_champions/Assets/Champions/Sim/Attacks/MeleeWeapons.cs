using System;

namespace Champions.Sim
{
    /// <summary>Sword opens a lane; Hammer clears nearby crowds. Each form preserves its close footprint.</summary>
    public sealed class MeleeWeapons
    {
        readonly Simulation sim;
        readonly StructList<int> near = new StructList<int>(2048);
        struct AimTarget { public double X, Y, Distance, CosLimit; public bool AllDirections; }
        readonly StructList<AimTarget> aimTargets = new StructList<AimTarget>(2048);
        public MeleeWeapons(Simulation sim)
        {
            this.sim = sim;
            sim.Weapons.Bind(WeaponKind.Sword, t => HasTarget(WeaponKind.Sword, t), Fire);
            sim.Weapons.Bind(WeaponKind.Hammer, t => HasTarget(WeaponKind.Hammer, t), Fire);
        }
        public static double Radius(WeaponKind kind) => kind == WeaponKind.Hammer ? 2.6 : 3;
        bool HasTarget(WeaponKind kind, double t)
        {
            if (kind == WeaponKind.Sword && sim.State.Hero.Dashes > 0 && t < sim.State.Hero.DashEnd + .22) return false;
            double scale = Math.Sqrt(sim.State.Build.Stats(kind).AreaFactor * sim.State.Build.AreaAt(sim.CombatTime(t)));
            sim.HeroWas(t, out double x, out double y);
            sim.Enemies.Near(x, y, Radius(kind) * scale, near);
            return near.Count > 0;
        }
        void Fire(WeaponActivation activation)
        {
            sim.HeroWas(activation.T, out double x, out double y);
            double radius = Radius(activation.Kind) * Math.Sqrt(activation.AreaFactor);
            double dx = sim.State.Hero.FacingX, dy = sim.State.Hero.FacingY;
            if (activation.Kind == WeaponKind.Hammer) Aim(x, y, radius, out dx, out dy);
            // Capture the origin and aim once; Echo retains the original lane and empowerment.
            sim.Weapons.Supports.Launch(activation, activation.Kind == WeaponKind.Hammer ? 1.65 : 1,
                HitWindow.Strike, effect => Strike(effect, x, y, dx, dy, radius), authoredTargets: int.MaxValue / 2);
        }
        void Aim(double x, double y, double radius, out double dx, out double dy)
        {
            dx = sim.State.Hero.FacingX; dy = sim.State.Hero.FacingY;
            sim.Enemies.Near(x, y, radius, near);
            aimTargets.Clear();
            // Body angular width depends only on the target, not on the candidate lane.
            // Precompute it once: crowd scoring needs dot products, not N-squared inverse trig calls.
            for (int i = 0; i < near.Count; i++)
            {
                var e = sim.Enemies[near[i]];
                double ax = e.X - x, ay = e.Y - y, distance = Math.Sqrt(ax * ax + ay * ay);
                aimTargets.Add(new AimTarget { X = distance > 1e-9 ? ax / distance : 1,
                    Y = distance > 1e-9 ? ay / distance : 0, Distance = distance,
                    AllDirections = distance <= e.Radius + 1e-9,
                    CosLimit = SimMath.Cos(Math.PI * .42 + SimMath.Asin(Math.Min(1, e.Radius / Math.Max(distance, 1e-30))) + 1e-9) });
            }
            int bestCount = -1;
            double bestDistance = double.PositiveInfinity;
            // Score candidate lanes, one vote per enemy including bosses; deterministic nearest tie-break.
            for (int i = 0; i < near.Count; i++)
            {
                var lane = aimTargets[i];
                int count = 0;
                for (int j = 0; j < near.Count; j++)
                {
                    var other = aimTargets[j];
                    double dot = lane.X * other.X + lane.Y * other.Y;
                    if (other.AllDirections) count++;
                    else if (Math.Abs(dot - other.CosLimit) <= 1e-12)
                    {
                        // Preserve the original geometric verdict at floating-point boundaries.
                        var e = sim.Enemies[near[j]];
                        if (HitShape.Cone(x, y, lane.X, lane.Y, radius, Math.PI * .42).Touches(e.X, e.Y, e.Radius)) count++;
                    }
                    else if (dot > other.CosLimit) count++;
                }
                if (count < bestCount || count == bestCount && lane.Distance >= bestDistance) continue;
                bestCount = count; bestDistance = lane.Distance; dx = lane.X; dy = lane.Y;
            }
        }

        void Strike(WeaponEffect effect, double x, double y, double dx, double dy, double radius)
        {
            var a = effect.Activation;
            int form = CombatRules.FormIndex(a.Level);
            int step = (int)((a.Sequence - 1) % 3);
            bool hammer = a.Kind == WeaponKind.Hammer;
            double travel = form == 3 ? radius * (hammer ? step * 0.7 : 1.5) : 0;
            int flags = (a.EmpowermentArea > 1 ? 16 : 0) | (effect.Spectral ? 1 : 0) | ((a.Sequence + effect.Strike) % 2 == 0 ? 2 : 0) | step << 2;
            sim.Emit(SimEvent.MeleeAction(effect.T, a.Kind, form, flags, x, y, dx, dy, radius, travel));
            // A single strike ledger spans every component: the retained sweep and added footprint cannot double-hit.
            Land(HitShape.Cone(x, y, dx, dy, radius, Math.PI * 0.42));
            if (!hammer)
            {
                if (form == 1)
                {
                    double c = SimMath.Cos(55 * Math.PI / 180), s = SimMath.Sin(55 * Math.PI / 180);
                    Land(HitShape.Cone(x, y, dx * c - dy * s, dx * s + dy * c, radius, Math.PI * 0.42));
                }
                if (form >= 2) Land(HitShape.Circle(x, y, radius));
                if (form == 3) Land(HitShape.Path(x, y, x + dx * travel, y + dy * travel, radius * 0.65));
            }
            else
            {
                if (form >= 1) Land(HitShape.Circle(x, y, radius));
                if (form >= 2) Land(HitShape.Path(x, y, x + dx * radius, y + dy * radius, radius * 0.6));
                if (form == 3) Land(HitShape.Circle(x + dx * travel, y + dy * travel, radius));
            }
            void Land(HitShape shape)
            {
                var blow = new Blow { T = effect.T, Shape = shape, Damage = effect.Damage,
                    Source = hammer ? BlowSource.Hammer : BlowSource.Sword, Tags = HitTags.Strike | HitTags.Area,
                    Flags = effect.Spectral ? BlowFlags.Spectral : BlowFlags.None, Owner = -1, Part = -1 };
                sim.Strikes.LandWeapon(sim, blow, effect);
            }
        }
    }
}
