using System;

namespace Champions.Sim
{
    /// <summary>Bounded connected-target lightning and placed, finite-lived ground pulses.</summary>
    public sealed class MagicWeapons
    {
        public const double LightningReach = 7, ChainReach = 3, FieldRadius = 2.4, FieldSeconds = 3;
        readonly Simulation sim;
        readonly StructList<int> near = new StructList<int>(2048);
        double fieldX, fieldY, aimX = 1, aimY, heldSeconds;
        public MagicWeapons(Simulation sim)
        {
            this.sim = sim;
            sim.Resumed += elapsed => heldSeconds += elapsed;
            sim.Weapons.Bind(WeaponKind.Lightning, HasLightningTarget, Lightning);
            sim.Weapons.Bind(WeaponKind.EmberField, t => true, Field);
        }
        bool HasLightningTarget(double t)
        {
            sim.HeroWas(t, out double x, out double y);
            var w = sim.State.Build.Weapon(WeaponKind.Lightning);
            double scale = Math.Sqrt(sim.State.Build.Stats(WeaponKind.Lightning).AreaFactor * (w.Empowered ? 1.5 : 1));
            sim.Enemies.Near(x, y, LightningReach * scale, near);
            return near.Count > 0;
        }
        void Lightning(WeaponActivation a)
        {
            sim.HeroWas(a.T, out double x, out double y);
            int form = CombatRules.FormIndex(a.Level);
            double scale = Math.Sqrt(a.AreaFactor), reach = LightningReach * scale;
            double nx = x, ny = y;
            if (form >= 2)
            {
                double angle = (a.Sequence - 1) * Math.PI * .5;
                nx += SimMath.Cos(angle) * scale * (form == 3 ? 2 : 1);
                ny += SimMath.Sin(angle) * scale * (form == 3 ? 2 : 1);
            }
            sim.Emit(SimEvent.MagicAction(a.T, a.Kind, form, false, x, y, nx, ny, scale, .6));
            sim.Weapons.Supports.Launch(a, 1.1, HitWindow.Activation, effect =>
            {
                double fromX = nx, fromY = ny;
                bool child = effect.ImpactTarget.HasValue;
                if (child)
                {
                    // A split starts at the captured parent's impact, including after that enemy dies.
                    fromX = effect.ImpactX ?? nx; fromY = effect.ImpactY ?? ny;
                }
                for (int hop = 0; hop < effect.TargetLimit && !effect.Finished; hop++)
                {
                    int id = Nearest(effect, x, y, reach, fromX, fromY,
                        child || hop > 0 ? ChainReach * scale : reach + 2 * scale);
                    if (id < 0) break;
                    var e = sim.Enemies[id];
                    var blow = new Blow { T = effect.T, Shape = HitShape.Circle(e.X, e.Y, .01),
                        Damage = effect.Damage, Source = BlowSource.Lightning, Tags = HitTags.Strike,
                        Flags = effect.Spectral ? BlowFlags.Spectral : BlowFlags.None, Owner = -1, Part = -1 };
                    if (sim.Strikes.StrikeWeaponEnemy(sim, id, blow, effect))
                        sim.Emit(SimEvent.LightningLink(effect.T, fromX, fromY, e.X, e.Y, effect.Spectral));
                    fromX = e.X; fromY = e.Y;
                }
            }, authoredProjectiles: form == 0 ? 1 : form == 3 ? 3 : 2, authoredTargets: 1 + form);
        }
        int Nearest(WeaponEffect effect, double x, double y, double totalReach,
            double fromX, double fromY, double hopReach)
        {
            // Queries finish before Impact can reenter with a fork; no shared list survives a callback.
            sim.Enemies.Near(fromX, fromY, hopReach, near);
            int best = -1; double distance = double.PositiveInfinity;
            for (int k = 0; k < near.Count; k++)
            {
                int id = near[k]; var e = sim.Enemies[id];
                if (effect.ImpactTarget == e.Generation || effect.Hits.Contains(e.Generation) || !HitShape.Circle(x, y, totalReach).Touches(e.X, e.Y, e.Radius)) continue;
                double d = (e.X - fromX) * (e.X - fromX) + (e.Y - fromY) * (e.Y - fromY);
                if (d > distance || d == distance && id > best) continue;
                best = id; distance = d;
            }
            return best;
        }
        void Field(WeaponActivation a)
        {
            var state = sim.State.Build.Weapon(a.Kind);
            if (!WeaponSupports.Active(state, a.T))
            {
                sim.HeroWas(a.T, out fieldX, out fieldY);
                aimX = sim.State.Hero.FacingX; aimY = sim.State.Hero.FacingY;
                WeaponSupports.Refresh(state, a.Stats, a.T, FieldSeconds);
            }
            // Capture each pulse's origin. Echo never follows a later seal or the moving hero.
            double x = fieldX, y = fieldY, dx = aimX, dy = aimY;
            double radius = FieldRadius * Math.Sqrt(a.AreaFactor);
            double until = state.PersistentUntil, heldAtLaunch = heldSeconds;
            int form = CombatRules.FormIndex(a.Level);
            int placement = form == 3 ? (int)((a.Sequence - 1) % 3) : 0;
            double step = placement * radius * .65;
            double nx = x + dx * step, ny = y + dy * step;
            sim.Weapons.Supports.Launch(a, .7, HitWindow.Pulse, effect =>
            {
                var cue = SimEvent.MagicAction(effect.T, a.Kind, form, effect.Spectral,
                    x, y, dx, dy, radius, Math.Max(.05, until + heldSeconds - heldAtLaunch - effect.T));
                cue.I2 |= placement << 1;
                if (a.EmpowermentArea > 1) cue.I2 |= 8;
                sim.Emit(cue);
                Land(HitShape.Circle(x, y, radius));
                if (form >= 1) Land(HitShape.Path(x, y, x + dx * radius * 1.4, y + dy * radius * 1.4, radius * .35));
                if (form >= 2) Land(HitShape.Path(x, y, x - dy * radius, y + dx * radius, radius * .5));
                if (form == 3) Land(HitShape.Circle(nx, ny, radius * .8));
                void Land(HitShape shape) => sim.Strikes.LandWeapon(sim, new Blow
                { T = effect.T, Shape = shape, Damage = effect.Damage, Source = BlowSource.EmberField,
                    Tags = HitTags.Area, Flags = BlowFlags.Spectral, Owner = -1, Part = -1 }, effect);
            }, authoredTargets: int.MaxValue / 2);
        }
        internal ulong FoldHash(ulong h)
        {
            if (sim.State.Build.Level(WeaponKind.EmberField) == 0) return h;
            h = Hashing.Fold(h, Hashing.Bits(heldSeconds));
            h = Hashing.Fold(h, Hashing.Bits(fieldX)); h = Hashing.Fold(h, Hashing.Bits(fieldY));
            h = Hashing.Fold(h, Hashing.Bits(aimX)); return Hashing.Fold(h, Hashing.Bits(aimY));
        }
    }
}
