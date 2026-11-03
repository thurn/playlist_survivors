using System;

namespace Champions.Sim
{
    /// <summary>One authored damage activation. Extra strikes are simultaneous spectral actions.</summary>
    public readonly struct WeaponActivation
    {
        public readonly WeaponKind Kind;
        public readonly double T, EmpowermentArea, CadenceDamageFactor;
        public readonly int Level, Strikes;
        public readonly long Sequence;
        public readonly CombatStats Stats;
        public readonly bool Persistent;
        public WeaponActivation(WeaponKind kind, double t, int level, int strikes, long sequence,
            double area, double cadenceDamageFactor, CombatStats stats, bool persistent)
        {
            Kind = kind; T = t; Level = level; Strikes = strikes; Sequence = sequence;
            EmpowermentArea = area; CadenceDamageFactor = cadenceDamageFactor;
            Stats = stats; Persistent = persistent;
        }
        // Slower quantized periods retain sustained power; faster periods never reduce per-hit damage.
        public double DamageFactor => Stats.DamageFactor * CadenceDamageFactor;
        public double AreaFactor => Stats.AreaFactor * EmpowermentArea;
        public bool Spectral(int strike) => Persistent || strike > 0;
    }

    /// <summary>
    /// Common survivor timing owner. Register weapon geometry with Bind; unbound weapons do not run
    /// alongside the legacy arsenal. Beat/reliability/tick hooks are driven by Simulation in song order.
    /// Target predicates must be pure. Holds spend due grid points without spending strike carry.
    /// </summary>
    public sealed class WeaponScheduler
    {
        sealed class Slot
        {
            public Cadence Cadence;
            public double Planned = double.NaN;
            public Func<double, bool> Target;
            public Action<WeaponActivation> Fire;
        }
        static readonly int[] Counts = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
        readonly Slot[] slots = new Slot[CombatRules.WeaponCount];
        readonly Simulation sim;
        public WeaponSupports Supports { get; } = new WeaponSupports();
        public WeaponScheduler(Simulation sim)
        {
            this.sim = sim;
            sim.Resumed += elapsed =>
            {
                Supports.Resume(elapsed);
                foreach (var slot in slots) if (slot != null) slot.Planned = double.NaN;
                for (int i = 0; i < CombatRules.WeaponCount; i++)
                {
                    var weapon = sim.State.Build.Weapon((WeaponKind)i);
                    if (weapon != null && weapon.PersistentActive) weapon.PersistentUntil += elapsed;
                }
            };
        }
        public static double BaseSeconds(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Hammer: return .75;
                case WeaponKind.Spear: case WeaponKind.Lightning: return .375;
                case WeaponKind.OrbitBlades: return .25;
                default: CombatRules.WeaponIndex(kind); return .5;
            }
        }
        /// <summary>Bind once, before play. Persistent handlers create/refresh visuals separately from pulses.</summary>
        public void Bind(WeaponKind kind, Func<double, bool> hasTarget, Action<WeaponActivation> fire, double? baseSeconds = null)
        {
            int i = CombatRules.WeaponIndex(kind);
            if (slots[i] != null) throw new InvalidOperationException("weapon already bound");
            double target = baseSeconds ?? BaseSeconds(kind);
            if (!(target > 0) || double.IsInfinity(target)) throw new ArgumentOutOfRangeException(nameof(baseSeconds));
            var cadence = new Cadence(sim.Beats, target, Counts, target, subdivisions: 4);
            cadence.Begin(sim.Now);
            slots[i] = new Slot { Cadence = cadence, Target = hasTarget ?? throw new ArgumentNullException(nameof(hasTarget)),
                Fire = fire ?? throw new ArgumentNullException(nameof(fire)) };
        }
        internal void Plan(double t)
        {
            if (sim.Melee == null || sim.State.Held) return;
            var slot = slots[(int)WeaponKind.Sword];
            if (slot == null || sim.State.Build.Level(WeaponKind.Sword) == 0) return;
            double impact = slot.Cadence.NextStrike(t);
            if (double.IsNaN(impact) || impact - t > .35 + 1e-9 || impact <= t ||
                Math.Abs(impact - slot.Planned) < 1e-9) return;
            slot.Planned = impact;
            sim.Emit(SimEvent.Swing(t, impact, sim.State.Hero.FacingX, sim.State.Hero.FacingY, false,
                sim.State.Build.Level(WeaponKind.Sword)));
        }
        internal void Beat(double t, int index)
        {
            if (sim.State.Beat == Reliability.Reliable) Supports.Step(t, sim.State.Held, true);
            foreach (var slot in slots)
                if (slot != null)
                {
                    var step = slot.Cadence.OnBeat(index, t, sim.State.Beat == Reliability.Reliable);
                    if ((step & CadenceStep.Strike) != 0) Activate(slot, t, sim.Beats.TempoAt(index) * slot.Cadence.Beats / 4);
                }
        }
        internal void ReliabilityChanged(double t)
        {
            Supports.ReliabilityChanged(t, sim.State.Beat == Reliability.Reliable);
            if (sim.State.Beat == Reliability.Reliable) return;
            foreach (var slot in slots) slot?.Cadence.Beatless(t);
        }
        internal double NextPulse
        {
            get
            {
                // A pending echo stays queued during holds; an overdue held deadline must not pin RunTick.
                double next = sim.State.Held ? double.PositiveInfinity : Supports.NextPulse;
                foreach (var slot in slots) if (slot != null) next = Math.Min(next, slot.Cadence.NextPulse);
                return next;
            }
        }
        internal void Pulse(double t)
        {
            Supports.Step(t, sim.State.Held, false);
            foreach (var slot in slots)
                if (slot != null && slot.Cadence.NextPulse <= t + 1e-9)
                {
                    if (sim.State.Beat == Reliability.Reliable && slot.Cadence.Beats > 0)
                    {
                        if ((slot.Cadence.GridPulse(t) & CadenceStep.Strike) != 0)
                            Activate(slot, t, slot.Cadence.PeriodS);
                    }
                    else { slot.Cadence.FirePulse(t); Activate(slot, t, slot.Cadence.TargetS); }
                }
        }
        internal ulong FoldHash(ulong h)
        {
            bool bound = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                bound = true;
                h = Hashing.Fold(h, (ulong)i);
                h = Hashing.Fold(h, Hashing.Bits(slot.Cadence.TargetS));
                h = Hashing.Fold(h, Hashing.Bits(slot.Cadence.LastStrike));
                h = Hashing.Fold(h, Hashing.Bits(slot.Cadence.LockPeriod));
                h = Hashing.Fold(h, (ulong)(uint)slot.Cadence.Anchor);
                h = Hashing.Fold(h, Hashing.Bits(slot.Cadence.NextPulse));
                h = Hashing.Fold(h, (ulong)(uint)slot.Cadence.Beats);
                h = Hashing.Fold(h, (ulong)(uint)slot.Cadence.NextGridIndex);
                h = Hashing.Fold(h, Hashing.Bits(slot.Planned));
            }
            if (bound) { h = Hashing.Fold(h, Hashing.Bits(sim.State.Build.AuraUntil)); h = Hashing.Fold(h, Hashing.Bits(sim.CombatTime(sim.Now))); }
            if (!bound) return h; // Archived sessions keep their hashes.
            for (int i = 0; i < CombatRules.WeaponCount; i++)
            {
                var w = sim.State.Build.Weapon((WeaponKind)i);
                if (w == null) continue;
                h = Hashing.Fold(h, (ulong)(uint)i | (ulong)(uint)w.Level << 8);
                h = Hashing.Fold(h, Hashing.Bits(w.StrikeCarry));
                h = Hashing.Fold(h, (ulong)w.ActivationSequence);
                h = Hashing.Fold(h, (ulong)w.PulseSequence);
                h = Hashing.Fold(h, w.PersistentActive ? 1UL : 0);
                h = Hashing.Fold(h, Hashing.Bits(w.PersistentUntil));
            }
            for (int i = 0; i < CombatRules.SupportCount; i++) h = Hashing.Fold(h, (ulong)sim.State.Build.Level((SupportKind)i));
            return h;
        }
        void Activate(Slot slot, double t, double interval)
        {
            int i = Array.IndexOf(slots, slot);
            var kind = (WeaponKind)i;
            var weapon = sim.State.Build.Weapon(kind);
            if (sim.State.Held || weapon == null || !slot.Target(t)) return;
            var stats = sim.State.Build.Stats(kind);
            double area = sim.State.Build.BeginActivation(kind, true, sim.CombatTime(t));
            double credit = weapon.StrikeCarry + stats.StrikeBonus;
            int extra = (int)Math.Floor(credit + 1e-9);
            weapon.StrikeCarry = Math.Max(0, credit - extra);
            bool persistent = sim.State.Build.Rules.Weapon(kind).Form(weapon.Level).Persistent;
            var activation = new WeaponActivation(kind, t, weapon.Level, 1 + extra, weapon.ActivationSequence,
                area, Math.Max(1, interval / slot.Cadence.TargetS), stats, persistent);
            var hero = sim.State.Hero;
            sim.Emit(SimEvent.WeaponActivation(t, kind, weapon.Level, activation.Sequence, activation.Strikes, area, hero.X, hero.Y, 0, activation.CadenceDamageFactor));
            if (persistent)
                for (int k = 0; k < activation.Strikes; k++)
                    sim.Emit(SimEvent.WeaponPulse(t, kind, activation.Sequence, ++weapon.PulseSequence, area));
            slot.Fire(activation);
        }
    }
}
