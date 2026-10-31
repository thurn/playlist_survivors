using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Experience gems (unity-game.md, Pickups, Level-ups and Chests; native's engine/pickups.rs and
    /// survivors/pickups.rs).
    ///
    /// - <b>Drops</b>: a dying enemy drops a gem worth <c>floor((r + 0.5) * xp)</c>, r from the
    ///   run's generator, so a weak enemy's is often worth nothing and none drops. At the field's
    ///   cap a drop adds its value to the nearest gem, which emits its drop again, grown.
    /// - <b>Pickup</b>: at a tick's end, a gem within the pickup radius (progression.json's times
    ///   Magnet) plus its body of the hero takes off (<see cref="SimEventKind.GemFlight"/>) and flies
    ///   by native's attraction, stepped at 60 Hz of run clock: kicked outward, then pulled in ever
    ///   faster. Once its step passes within the hero's body it is collected, and it arrives on the
    ///   first sixteenth at least <see cref="NeverLateS"/> later (or then, with no beat known): a
    ///   <see cref="SimEventKind.Pickup"/>, which the client chimes, and its experience.
    /// - Gems wait while the world is frozen or the hero has fallen.
    /// </summary>
    internal sealed class Gems
    {
        public const double SubstepS = 1.0 / 60;
        /// <summary>One Vampire Survivors world unit, native's 154.27 px, in metres.</summary>
        public const double VsUnitM = 140.0 / (0.825 * 1.1) / 27;
        /// <summary>native's hero body for collection, 10 px.</summary>
        public const double HeroRadiusM = 10.0 / 27;
        /// <summary>native's never-late margin before a pickup's sixteenth.</summary>
        public const double NeverLateS = 0.06;
        /// <summary>The attraction's starting speed, its gain each step, and how long its outward kick ramps (VS's).</summary>
        const double StartSpeed = 250, KickRampS = 0.5, LostSpeed = 65536;

        struct Gem
        {
            public int Id;
            public double X, Y, Value;
            public double Speed, Start, Next;
            /// <summary>When a collected gem arrives; NaN while it flies.</summary>
            public double Arrive;
        }

        readonly GemRules rules;
        readonly SimState state;
        readonly BeatClock clock;
        readonly Arsenal arsenal;
        readonly Gem[] field, flying;
        int fieldCount, flyingCount, dropped;

        public Gems(GemRules rules, SimState state, BeatClock clock, Arsenal arsenal)
        {
            this.rules = rules;
            this.state = state;
            this.clock = clock;
            this.arsenal = arsenal;
            field = new Gem[rules.Cap];
            flying = new Gem[rules.Cap];
        }

        /// <summary>Gems lying on the field, and in flight.</summary>
        public int OnField => fieldCount;
        public int Flying => flyingCount;

        bool Holding => state.Frozen || state.Hero.Fallen;

        /// <summary>An enemy worth <paramref name="xp"/> died at (x, y) at <paramref name="t"/>.</summary>
        public void Drop(Simulation sim, double t, double x, double y, double xp)
        {
            double value = Math.Floor((sim.Random.NextDouble() + 0.5) * xp);
            if (value <= 0) return;
            if (fieldCount == field.Length)
            {
                int near = 0;
                double best = double.PositiveInfinity;
                for (int i = 0; i < fieldCount; i++)
                {
                    double d = (field[i].X - x) * (field[i].X - x) + (field[i].Y - y) * (field[i].Y - y);
                    if (d < best)
                    {
                        best = d;
                        near = i;
                    }
                }
                ref var g = ref field[near];
                g.Value += value;
                sim.Emit(SimEvent.Drop(t, g.Id, ItemKind.Gem, g.X, g.Y, g.Value));
                return;
            }
            field[fieldCount++] = new Gem { Id = dropped, X = x, Y = y, Value = value, Arrive = double.NaN };
            sim.Emit(SimEvent.Drop(t, dropped++, ItemKind.Gem, x, y, value));
        }

        /// <summary>The next collected gem's arrival at or after <paramref name="floor"/>.</summary>
        internal double NextTime(double floor)
        {
            if (Holding) return double.PositiveInfinity;
            double t = double.PositiveInfinity;
            for (int i = 0; i < flyingCount; i++)
                if (!double.IsNaN(flying[i].Arrive)) t = Math.Min(t, Math.Max(flying[i].Arrive, floor));
            return t;
        }

        /// <summary>A gem arrives: the pickup, and its experience.</summary>
        internal void Fire(Simulation sim, double t)
        {
            for (int i = 0; i < flyingCount; i++)
            {
                if (!(flying[i].Arrive <= t)) continue;
                var g = flying[i];
                flying[i] = flying[--flyingCount];
                sim.Emit(SimEvent.Pickup(t, g.Id, (int)ItemKind.Gem, g.X, g.Y, g.Value));
                arsenal.LevelUps.Gain(sim, t, g.Value);
                return;
            }
        }

        /// <summary>At the tick's end: the flying gems' steps up to the run clock, then the gems in reach take off.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (Holding) return;
            double world = state.RunClock;
            for (int i = 0; i < flyingCount; i++)
            {
                ref var g = ref flying[i];
                while (double.IsNaN(g.Arrive) && g.Next <= world + 1e-9)
                {
                    double song = end - (world - g.Next);
                    state.Hero.PositionAt(song, out double hx, out double hy);
                    if (Step(ref g, hx, hy))
                    {
                        double sixteenth = clock.GridAtOrAfter(song + NeverLateS, 0.25, out _);
                        g.Arrive = double.IsNaN(sixteenth) ? song + NeverLateS : sixteenth;
                    }
                }
            }
            state.Hero.PositionAt(end, out double x, out double y);
            double reach = state.PickupRadius + rules.BodyRadiusM;
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                ref var g = ref field[i];
                if ((g.X - x) * (g.X - x) + (g.Y - y) * (g.Y - y) > reach * reach || flyingCount == flying.Length) continue;
                g.Speed = StartSpeed;
                g.Start = world;
                g.Next = world + SubstepS;
                sim.Emit(SimEvent.GemFlight(end, g.Id, g.X, g.Y));
                flying[flyingCount++] = g;
                field[i] = field[--fieldCount];
            }
        }

        /// <summary>The run clock jumped <paramref name="delta"/> ahead: the flying gems' steps move with it.</summary>
        internal void Shift(double delta)
        {
            for (int i = 0; i < flyingCount; i++)
            {
                flying[i].Start += delta;
                flying[i].Next += delta;
            }
        }

        /// <summary>
        /// One 60 Hz step of native's attraction toward the hero at (hx, hy): returns whether the
        /// gem's path passed within the hero's body and its own, which collects it.
        /// </summary>
        bool Step(ref Gem g, double hx, double hy)
        {
            double dx = hx - g.X, dy = hy - g.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double ramp = Math.Max(-1, Math.Min(1, (g.Next - g.Start) / KickRampS * 2 - 1));
            double step = g.Speed * 0.01 * VsUnitM * ramp / 60;
            double ox = g.X, oy = g.Y;
            if (d > 1e-9)
            {
                g.X += dx / d * step;
                g.Y += dy / d * step;
            }
            double sx = g.X - ox, sy = g.Y - oy;
            double len2 = sx * sx + sy * sy;
            double u = len2 > 1e-12 ? Math.Max(0, Math.Min(1, ((hx - ox) * sx + (hy - oy) * sy) / len2)) : 0;
            double cx = ox + sx * u - hx, cy = oy + sy * u - hy;
            bool collected = Math.Sqrt(cx * cx + cy * cy) <= HeroRadiusM + rules.BodyRadiusM || g.Speed > LostSpeed;
            if (collected)
            {
                g.X = hx;
                g.Y = hy;
            }
            g.Speed += 1;
            g.Next += SubstepS;
            return collected;
        }
    }
}
