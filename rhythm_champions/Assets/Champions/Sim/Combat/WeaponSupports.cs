using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>A launched action; geometry owns flight, this object owns support lineage and hit limits.</summary>
    public sealed class WeaponEffect
    {
        public readonly WeaponActivation Activation;
        public readonly int Strike, Projectile;
        public readonly double T, Damage;
        public readonly bool Spectral;
        public readonly long? ImpactTarget;
        public readonly EffectLineage Lineage;
        internal readonly TargetHitLedger Hits, RepeatHits;
        internal readonly Action<WeaponEffect> Launch;
        readonly HashSet<long> encountered = new HashSet<long>();
        int targetsLeft;
        public readonly int TargetLimit;
        public bool Finished => targetsLeft == 0;
        internal WeaponEffect(WeaponActivation activation, int strike, int projectile, double t, double damage,
            bool spectral, long? impactTarget, EffectLineage lineage, TargetHitLedger hits,
            TargetHitLedger repeatHits, int targets, Action<WeaponEffect> launch)
        {
            Activation = activation; Strike = strike; Projectile = projectile; T = t; Damage = damage;
            Spectral = spectral; ImpactTarget = impactTarget; Lineage = lineage; Hits = hits;
            RepeatHits = repeatHits; targetsLeft = targets; TargetLimit = targets; Launch = launch;
        }
        internal bool TryHit(long target)
        {
            if (Finished || !encountered.Add(target)) return false;
            targetsLeft--;
            return Hits.TryHit(target);
        }
    }

    /// <summary>
    /// Executable Multishot/Pierce/Split/Echo/Persistence support mechanics. Families supply geometry
    /// and call Impact at continuous collision times. Children inherit captured stats and empowerment.
    /// Echo uses one explicit independent repeat window, shared across all branches of a strike/volley.
    /// </summary>
    public sealed class WeaponSupports
    {
        struct Repeat { public WeaponEffect Effect; public double Due; }
        readonly StructList<Repeat> repeats = new StructList<Repeat>(32);
        bool reliable;
        const double FallbackEchoS = 0.5;
        static TargetHitLedger Ledger()
        { var ledger = new TargetHitLedger(HitWindow.Activation, 16); ledger.BeginWindow(0); return ledger; }
        public static int VolleyCount(CombatStats stats, int authoredCount = 1) => authoredCount + (int)Math.Floor(stats.ProjectileBonus + 1e-9);
        public static int TargetCount(CombatStats stats, int authoredCount = 1) => authoredCount + (int)Math.Floor(stats.PierceBonus + 1e-9);
        public static double Duration(CombatStats stats, double authoredSeconds) => authoredSeconds * stats.DurationFactor;
        /// <summary>Refresh a temporary construct; pulses do not refresh duration or consume empowerment here.</summary>
        public static void Refresh(WeaponState state, CombatStats stats, double t, double authoredSeconds)
        {
            if (!(authoredSeconds > 0) || double.IsInfinity(authoredSeconds)) throw new ArgumentOutOfRangeException(nameof(authoredSeconds));
            state.PersistentActive = true;
            state.PersistentUntil = t + Duration(stats, authoredSeconds);
        }
        public static bool Active(WeaponState state, double t)
        {
            if (state.PersistentActive && t >= state.PersistentUntil) state.PersistentActive = false;
            return state.PersistentActive;
        }
        /// <summary>One body action, extra spectral strikes/pulses, and a volley per independent strike.</summary>
        public void Launch(WeaponActivation activation, double baseDamage, HitWindow policy,
            Action<WeaponEffect> launch, int authoredProjectiles = 1, int authoredTargets = 1)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (!CombatRules.Nonnegative(baseDamage) || authoredProjectiles < 1 || authoredTargets < 1)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            if (!Enum.IsDefined(typeof(HitWindow), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            for (int strike = 0; strike < activation.Strikes; strike++)
            {
                // Each authored/Tempo strike or pulse is independent; volley members share its window.
                var hits = Ledger();
                var repeatHits = Ledger();
                for (int projectile = 0; projectile < VolleyCount(activation.Stats, authoredProjectiles); projectile++)
                {
                    var effect = new WeaponEffect(activation, strike, projectile, activation.T,
                        baseDamage * activation.DamageFactor, activation.Spectral(strike), null,
                        new EffectLineage(), hits, repeatHits, TargetCount(activation.Stats, authoredTargets), launch);
                    launch(effect);
                    Echo(effect, activation.T);
                }
            }
        }
        /// <summary>Returns whether to apply damage. Call once per collision, before applying family damage.</summary>
        public bool Impact(WeaponEffect effect, long target, double t)
        {
            if (!effect.TryHit(target)) return false;
            double split = effect.Activation.Stats.SplitBonus;
            if (split > 0)
                for (int i = 0; i < 2; i++)
                    if (effect.Lineage.TryChild(EffectTrigger.Split, out var lineage))
                    {
                        var child = new WeaponEffect(effect.Activation, effect.Strike, i, t, effect.Damage * split,
                            true, target, lineage, effect.Hits, effect.RepeatHits,
                            effect.TargetLimit, effect.Launch);
                        child.Launch(child);
                        Echo(child, t);
                    }
            return true;
        }
        void Echo(WeaponEffect effect, double t)
        {
            double echo = effect.Activation.Stats.EchoBonus;
            if (echo <= 0 || !effect.Lineage.TryChild(EffectTrigger.Echo, out var lineage)) return;
            var child = new WeaponEffect(effect.Activation, effect.Strike, effect.Projectile, t,
                effect.Damage * echo, true, effect.ImpactTarget, lineage, effect.RepeatHits, effect.RepeatHits,
                effect.TargetLimit, effect.Launch);
            repeats.Add(new Repeat { Effect = child, Due = t + FallbackEchoS });
        }
        internal double NextPulse
        {
            get
            {
                double next = double.PositiveInfinity;
                if (!reliable) foreach (var repeat in repeats.AsSpan()) next = Math.Min(next, repeat.Due);
                return next;
            }
        }
        internal void ReliabilityChanged(double t, bool now)
        {
            if (reliable == now) return;
            reliable = now;
            for (int i = 0; i < repeats.Count; i++) repeats[i].Due = t + FallbackEchoS;
        }
        internal void Resume(double elapsed)
        {
            for (int i = 0; i < repeats.Count; i++) repeats[i].Due += elapsed;
        }

        internal void Step(double t, bool held, bool beat)
        {
            if (held || repeats.Count == 0 || !beat && reliable) return;
            // Detach the due batch: callbacks can create more work, but never execute it on this grid point.
            var due = new List<WeaponEffect>();
            for (int i = 0; i < repeats.Count;)
            {
                var repeat = repeats[i];
                bool ready = beat ? repeat.Effect.T < t - 1e-9 : repeat.Due <= t + 1e-9;
                if (!ready) { i++; continue; }
                due.Add(repeat.Effect);
                for (int j = i + 1; j < repeats.Count; j++) repeats[j - 1] = repeats[j];
                repeats.RemoveLast();
            }
            if (held) return;
            foreach (var effect in due)
            {
                var delivered = new WeaponEffect(effect.Activation, effect.Strike, effect.Projectile, t,
                    effect.Damage, true, effect.ImpactTarget, effect.Lineage, effect.Hits, effect.RepeatHits,
                    effect.TargetLimit, effect.Launch);
                delivered.Launch(delivered);
            }
        }
    }
}
