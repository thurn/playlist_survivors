using System;

namespace Champions.Sim
{
    /// <summary>Sword opens a lane; Hammer clears nearby crowds. Each form preserves its close footprint.</summary>
    public sealed class MeleeWeapons
    {
        readonly Simulation sim;
        readonly StructList<int> near = new StructList<int>(2048);
        public MeleeWeapons(Simulation sim)
        {
            this.sim = sim;
            sim.Weapons.Bind(WeaponKind.Sword, t => HasTarget(WeaponKind.Sword, t), Fire);
            sim.Weapons.Bind(WeaponKind.Hammer, t => HasTarget(WeaponKind.Hammer, t), Fire);
        }
        public static double Radius(WeaponKind kind) => kind == WeaponKind.Hammer ? 2.6 : 3;
        bool HasTarget(WeaponKind kind, double t)
        {
            var w = sim.State.Build.Weapon(kind);
            double scale = Math.Sqrt(sim.State.Build.Stats(kind).AreaFactor * (w.Empowered ? 1.5 : 1));
            sim.HeroWas(t, out double x, out double y);
            sim.Enemies.Near(x, y, Radius(kind) * scale, near);
            return near.Count > 0;
        }
        void Fire(WeaponActivation activation)
        {
            sim.HeroWas(activation.T, out double x, out double y);
            double radius = Radius(activation.Kind) * Math.Sqrt(activation.AreaFactor);
            Aim(x, y, radius, out double dx, out double dy);
            // Capture the origin and aim once; Echo retains the original lane and empowerment.
            sim.Weapons.Supports.Launch(activation, activation.Kind == WeaponKind.Hammer ? 1.65 : 1,
                HitWindow.Strike, effect => Strike(effect, x, y, dx, dy, radius), authoredTargets: int.MaxValue / 2);
        }
        void Aim(double x, double y, double radius, out double dx, out double dy)
        {
            dx = sim.State.Hero.FacingX; dy = sim.State.Hero.FacingY;
            sim.Enemies.Near(x, y, radius, near);
            int bestCount = -1;
            double bestDistance = double.PositiveInfinity;
            // Score candidate lanes, one vote per enemy including bosses; deterministic nearest tie-break.
            for (int i = 0; i < near.Count; i++)
            {
                var e = sim.Enemies[near[i]];
                double ax = e.X - x, ay = e.Y - y, distance = Math.Sqrt(ax * ax + ay * ay);
                double ux = distance > 1e-9 ? ax / distance : 1, uy = distance > 1e-9 ? ay / distance : 0;
                var cone = HitShape.Cone(x, y, ux, uy, radius, Math.PI * 0.42);
                int count = 0;
                for (int j = 0; j < near.Count; j++)
                {
                    var other = sim.Enemies[near[j]];
                    if (cone.Touches(other.X, other.Y, other.Radius)) count++;
                }
                if (count < bestCount || count == bestCount && distance >= bestDistance) continue;
                bestCount = count; bestDistance = distance; dx = ux; dy = uy;
            }
        }
        void Strike(WeaponEffect effect, double x, double y, double dx, double dy, double radius)
        {
            var a = effect.Activation;
            int form = CombatRules.FormIndex(a.Level);
            int step = (int)((a.Sequence - 1) % 3);
            bool hammer = a.Kind == WeaponKind.Hammer;
            double travel = form == 3 ? radius * (hammer ? step * 0.7 : 1.5) : 0;
            int flags = (effect.Spectral ? 1 : 0) | ((a.Sequence + effect.Strike) % 2 == 0 ? 2 : 0) | step << 2;
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
