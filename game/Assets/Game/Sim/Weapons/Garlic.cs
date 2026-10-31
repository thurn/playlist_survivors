using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Garlic and its evolution Soul Eater (unity-game.md, The Other Weapons; native's
    /// engine/weapons.rs, Vampire Survivors' GarlicProjectile): an aura around the hero. Each shot
    /// is a new incarnation of it: an attack event naming the aura's radius (its shape's radius times
    /// the rank's Area and the hero's), then, at once and at every tick's end while it lasts, every
    /// live enemy whose body touches the circle around the hero takes the shot's power times Might,
    /// once an incarnation, and 0.3 more knockback from every later blow (<see cref="Horde.Weaken"/>,
    /// native's debuff). The aura keeps its shot's radius and power, and lasts until the next shot
    /// replaces it, as native's; it holds while the world is frozen, the hero has fallen or the run
    /// isn't playing.
    ///
    /// An enemy is remembered by its life (<see cref="EnemyState.Life"/>): a boss or a set piece may
    /// spawn inside the aura into the slot of one it just killed.
    /// </summary>
    internal sealed class Garlic : Weapon
    {
        readonly SimState state;
        readonly Horde horde;
        readonly int[] reached;
        // Each slot's enemy this incarnation has hit, by its life (0: none).
        readonly int[] hit;
        // The aura's radius and power since the latest shot; NaN before the first.
        double radius = double.NaN, power;

        public Garlic(Arsenal arsenal, SimState state, Horde horde, WeaponDef def, int index)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Aura) throw new ArgumentException(def.Id + " is not an aura");
            this.state = state;
            this.horde = horde;
            int slots = state.EnemyStore?.Length ?? 0;
            reached = new int[slots];
            hit = new int[slots];
        }

        internal override void Shoot(Simulation sim, double t)
        {
            radius = Radius;
            power = Power;
            Array.Clear(hit, 0, hit.Length);
            sim.Emit(SimEvent.HeroAttack(t, Index, ShapeKind.Aura, 0, t, t, 0, 0, false, radius));
            Pulse(sim, t);
        }

        internal override void Tick(Simulation sim, double end)
        {
            if (!double.IsNaN(radius) && !Holding) Pulse(sim, end);
        }

        /// <summary>The aura at <paramref name="t"/>: the enemies inside it that this incarnation hasn't hit.</summary>
        void Pulse(Simulation sim, double t)
        {
            if (horde == null) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            int n = horde.InReach(t, hx, hy, radius, reached);
            var enemies = state.Enemies;
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                if (hit[i] == enemies[i].Life) continue;
                hit[i] = enemies[i].Life;
                enemies[i].PositionAt(t, out double x, out double y);
                double dx = x - hx, dy = y - hy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                horde.Damage(sim, i, t, power, Index, WeaponHit.Strike, d > 1e-9 ? dx / d : 0, d > 1e-9 ? dy / d : 1,
                    Numbers.Knockback, Launch(1));
                horde.Weaken(i);
            }
        }
    }
}
