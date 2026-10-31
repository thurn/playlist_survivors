using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Easy's floor chickens (unity-game.md, The Run; native's engine/pickups.rs): a dying brute
    /// drops one at schedule.json's <c>easy.bruteChickenChance</c>, and a boss
    /// <c>easy.bossChickens</c> of them, spread across its death point (<see cref="Drop"/>, for the
    /// cast's bosses). The hero eats one it comes within <see cref="TouchM"/> of, while the world
    /// plays: it heals <see cref="Heal"/> at once (native waits for the next sixteenth) and can't be
    /// hurt for <see cref="ImmunityS"/>. Each emits a <see cref="ItemKind.Chicken"/> drop, and a
    /// pickup carrying the health gained.
    /// </summary>
    internal sealed class FloorChickens
    {
        /// <summary>Native's 24 px reach, 28 px spread and 30 health.</summary>
        public const double TouchM = 24.0 / 27.0;
        public const double SpreadM = 28.0 / 27.0;
        public const double Heal = 30;
        public const double ImmunityS = 0.3;
        /// <summary>More chickens on the floor at once than a run drops; a drop beyond it is lost.</summary>
        public const int Capacity = 64;

        struct Chicken
        {
            public int Id;
            public double X, Y;
        }

        readonly EasyRules rules;
        readonly SimState state;
        readonly HeroHealth health;
        readonly Chicken[] floor = new Chicken[Capacity];
        int count, dropped;

        public FloorChickens(EasyRules rules, SimState state, HeroHealth health)
        {
            this.rules = rules;
            this.state = state;
            this.health = health;
        }

        /// <summary>How many chickens are on the floor.</summary>
        public int Count => count;

        public void BruteDied(Simulation sim, double t, double x, double y)
        {
            if (sim.Random.NextDouble() < rules.BruteChickenChance) Drop(sim, t, x, y, 1);
        }

        public void BossDied(Simulation sim, double t, double x, double y) => Drop(sim, t, x, y, (int)rules.BossChickens);

        /// <summary><paramref name="n"/> chickens drop at <paramref name="t"/>, spread along x across (x, y).</summary>
        public void Drop(Simulation sim, double t, double x, double y, int n)
        {
            for (int k = 0; k < n && count < Capacity; k++)
            {
                double cx = x + (k - (n - 1) / 2.0) * SpreadM;
                floor[count++] = new Chicken { Id = dropped, X = cx, Y = y };
                sim.Emit(SimEvent.Drop(t, dropped++, ItemKind.Chicken, cx, y, Heal));
            }
        }

        /// <summary>At the tick's end: the hero eats the chickens in reach.</summary>
        public void Tick(Simulation sim, double end)
        {
            if (count == 0 || state.Frozen || state.Hero.Fallen) return;
            state.Hero.PositionAt(end, out double hx, out double hy);
            for (int i = count - 1; i >= 0; i--)
            {
                double dx = floor[i].X - hx, dy = floor[i].Y - hy;
                if (Math.Sqrt(dx * dx + dy * dy) > TouchM) continue;
                double gained = health.Heal(end, Heal, ImmunityS);
                sim.Emit(SimEvent.Pickup(end, floor[i].Id, (int)ItemKind.Chicken, floor[i].X, floor[i].Y, gained));
                floor[i] = floor[--count];
            }
        }
    }
}
