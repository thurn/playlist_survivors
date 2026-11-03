using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    public enum PickupKind : byte { Experience, Healing }

    public struct Pickup
    {
        public int Id;
        public PickupKind Kind;
        public double X, Y, Value;
    }

    /// <summary>Sparse persistent world cells, independent of structure streaming and encounters.
    /// One representation per kind per metre cell conserves all value; only nearby cells are visited.
    /// Attracted drops leave the world index and finish traveling even if the hero moves away.</summary>
    public sealed class Pickups
    {
        public const double Radius = 3;
        public const int FlightLimit = 256;
        readonly Dictionary<(int, int, PickupKind), Pickup> stored = new Dictionary<(int, int, PickupKind), Pickup>();
        readonly List<Pickup> flying = new List<Pickup>(FlightLimit);
        readonly ProgressionRules rules;
        internal Pickups(ProgressionRules rules) { this.rules = rules; }
        int nextId;
        public int StoredCount => stored.Count;
        public int FlyingCount => flying.Count;
        public long CellsVisited { get; private set; }
        public double ExperienceDropped { get; private set; }
        public double ExperienceCollected { get; private set; }
        public double OutstandingExperience => ExperienceDropped - ExperienceCollected;

        internal void Drop(Simulation sim, PickupKind kind, double x, double y, double value)
        {
            if (!(value > 0) || double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) return;
            var key = ((int)Math.Floor(x), (int)Math.Floor(y), kind);
            if (!stored.TryGetValue(key, out var p))
                p = new Pickup { Id = nextId++, Kind = kind, X = x, Y = y };
            p.Value += value;
            stored[key] = p;
            if (kind == PickupKind.Experience) ExperienceDropped += value;
            sim.Emit(SimEvent.Pickup(SimEventKind.PickupDrop, sim.Now, p));
        }

        /// <summary>Bounded local preview, not an ownership transfer. Undrawn value remains stored.</summary>
        public int Preview(double x, double y, double radius, Pickup[] output)
        {
            int n = 0;
            for (int i = 0; i < flying.Count && n < output.Length; i++) output[n++] = flying[i];
            for (int ix = (int)Math.Floor(x - radius); ix <= (int)Math.Floor(x + radius); ix++)
                for (int iy = (int)Math.Floor(y - radius); iy <= (int)Math.Floor(y + radius); iy++)
                    for (int k = 0; k < 2; k++)
                        if (stored.TryGetValue((ix, iy, (PickupKind)k), out var p) && n < output.Length) output[n++] = p;
            return n;
        }

        internal void Tick(Simulation sim)
        {
            if (sim.State.Held || sim.State.Phase != RunPhase.Playing || sim.State.Hero.Dead) return;
            var hero = sim.State.Hero;
            double radius = rules.PickupRadiusM * (1 + sim.State.Build.HeroBonus(StatFamily.Attraction));
            for (int x = (int)Math.Floor(hero.X - radius); x <= (int)Math.Floor(hero.X + radius); x++)
                for (int y = (int)Math.Floor(hero.Y - radius); y <= (int)Math.Floor(hero.Y + radius); y++)
                    for (int k = 0; k < 2; k++)
                    {
                        CellsVisited++;
                        var key = (x, y, (PickupKind)k);
                        if (flying.Count == FlightLimit || !stored.TryGetValue(key, out var p)) continue;
                        double dx = p.X - hero.X, dy = p.Y - hero.Y;
                        if (dx * dx + dy * dy > radius * radius) continue;
                        stored.Remove(key);
                        flying.Add(p);
                        sim.Emit(SimEvent.Pickup(SimEventKind.PickupAttract, sim.Now, p));
                    }
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var p = flying[i];
                double dx = hero.X - p.X, dy = hero.Y - p.Y, distance = Math.Sqrt(dx * dx + dy * dy);
                double step = rules.PickupSpeedMps * Simulation.TickSeconds;
                if (distance > step + 0.2)
                {
                    p.X += dx / distance * step; p.Y += dy / distance * step;
                    flying[i] = p;
                    continue;
                }
                flying.RemoveAt(i);
                sim.Emit(SimEvent.Pickup(SimEventKind.PickupCollect, sim.Now, p));
                if (p.Kind == PickupKind.Experience)
                {
                    ExperienceCollected += p.Value;
                    sim.Progression.CollectExperience(sim, p.Value, sim.Now);
                }
                else sim.Health.Heal(sim, p.Value * hero.MaxHealth, HealReason.Pickup);
            }
        }

        internal ulong Fold(ulong h)
        {
            // XOR makes the index hash independent of Dictionary enumeration order.
            ulong values = 0;
            foreach (var p in stored.Values) values ^= Hash(p);
            foreach (var p in flying) values ^= Hash(p);
            return Hashing.Fold(h, values);
        }
        static ulong Hash(Pickup p)
        {
            ulong h = Hashing.Fold((ulong)p.Id, (ulong)p.Kind);
            h = Hashing.Fold(h, Hashing.Bits(p.X)); h = Hashing.Fold(h, Hashing.Bits(p.Y));
            return Hashing.Fold(h, Hashing.Bits(p.Value));
        }
    }
}
