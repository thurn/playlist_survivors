using System;

namespace Champions.Sim
{
    /// <summary>What hit the hero (the horde's and the bosses' beads give it).</summary>
    public enum HitSource : byte
    {
        /// <summary>A monster's bite on touch.</summary>
        Touch = 0,
        /// <summary>A big ork's charge.</summary>
        Charge = 1,
        /// <summary>A troll's slam.</summary>
        Slam = 2,
        /// <summary>A mushroom's spore.</summary>
        Spore = 3,
        /// <summary>A boss's attack.</summary>
        Boss = 4,
    }

    /// <summary>How a hit on the hero settled.</summary>
    public enum HitOutcome : byte
    {
        /// <summary>It took the damage.</summary>
        Hurt = 0,
        /// <summary>A dash's i-frames covered it.</summary>
        Dodged = 1,
        /// <summary>A dash's i-frames covered it, pressed on the beat it landed on (a boss's Break fills).</summary>
        PerfectDodge = 2,
        /// <summary>It landed within the invulnerability after a hit.</summary>
        Immune = 3,
        /// <summary>It took the last of the hero's health.</summary>
        Killed = 4,
    }

    /// <summary>Why the hero healed.</summary>
    public enum HealReason : byte
    {
        LevelUp = 0, // Reserved legacy wire value.
        BossKill = 1,
        /// <summary>A mod's healing (Lifeblood).</summary>
        Mod = 2,
        Pickup = 3,
        Regeneration = 4,
    }

    /// <summary>An attack reaching the hero (<see cref="HeroHealth.Hurt"/>).</summary>
    public struct HeroHit
    {
        /// <summary>The song time it lands on the hero.</summary>
        public double T;
        public double Damage;
        public HitSource Source;
        /// <summary>The attacker's id (a monster, a boss), or -1.</summary>
        public int Owner;
        /// <summary>The beat it lands on (a telegraph's landing time, beatless too), or NaN (a touch): a perfect dodge needs one.</summary>
        public double Beat;
        /// <summary>Where it came from, for the hurt flash's direction.</summary>
        public double FromX, FromY;
    }

    /// <summary>
    /// The hero's health (design.md, "The Hero", Decisions 3 and 15): 100, 0.5 s of invulnerability after a
    /// hit, healing pickups, passive regeneration, full recovery on a boss kill and death on the killing hit.
    ///
    /// - <b>Settling in heard time.</b> The sim runs ahead of the speakers, so a dash the player heard
    ///   before a hit landed reaches the sim after it. A hit (<see cref="Hurt"/>) is held until every input
    ///   heard by its landing can have arrived: the inputs' lag behind the sim (the latest few inputs'
    ///   greatest, plus a tick; <see cref="HeroRules.HitSettleS"/> before any), at most
    ///   <see cref="HeroWalk.History"/> ticks. The client draws trailing by that lead, so the settled hit
    ///   shows on its own frame. Nothing settled is revised.
    /// - <b>Settling</b>, in landing order: a dash's i-frames covering the landing dodge it (a perfect
    ///   dodge when it was pressed on the beat the hit lands on, <see cref="HeroHit.Beat"/>: heard within
    ///   the beat window of it, or judged beatless); else a hit within <see cref="HeroRules.HurtInvulnerableS"/>
    ///   of the latest that hurt does nothing; else it hurts, and at no health kills, ending the run.
    /// - Every settled hit is a <see cref="SimEventKind.HeroHit"/> and a <see cref="Settled"/> call (the
    ///   bosses fill Break on perfect dodges).
    /// </summary>
    internal sealed class HeroHealth
    {
        /// <summary>The inputs whose lag the settle delay follows.</summary>
        const int LagSamples = 8;
        /// <summary>The dashes' i-frame windows kept.</summary>
        const int Windows = 4;

        struct Window
        {
            public int Dash;
            public double From, To, Heard;
            public JudgedBy By;
        }

        readonly HeroState state;
        readonly HeroRules rules;
        readonly double beatWindow;
        readonly StructList<HeroHit> pending = new StructList<HeroHit>(64);
        readonly Window[] windows = new Window[Windows];
        int windowCount;
        readonly double[] lags = new double[LagSamples];
        int lagCount, lagHead;

        public HeroHealth(HeroState state, HeroRules rules, BeatRules beat)
        {
            this.state = state;
            this.rules = rules;
            beatWindow = beat.BeatWindowS;
            state.MaxHealth = state.Health = rules.Health;
        }

        /// <summary>Called as each hit settles, with how it did.</summary>
        public event Action<Simulation, HeroHit, HitOutcome> Settled;

        /// <summary>Hits waiting to settle.</summary>
        public int Pending => pending.Count;

        /// <summary>How long after its landing a hit settles now.</summary>
        public double SettleS
        {
            get
            {
                if (lagCount == 0) return rules.HitSettleS;
                double most = 0;
                for (int i = 0; i < lagCount; i++) most = Math.Max(most, lags[i]);
                return Math.Min(most + Simulation.TickSeconds, (HeroWalk.History - 1) * Simulation.TickSeconds);
            }
        }

        /// <summary>An input heard at <paramref name="heard"/> was taken at <paramref name="now"/>: the lag the settle delay follows.</summary>
        public void Took(double heard, double now)
        {
            lags[lagHead] = Math.Max(0, now - heard);
            lagHead = (lagHead + 1) % LagSamples;
            if (lagCount < LagSamples) lagCount++;
        }

        /// <summary>An attack reaches the hero: it settles once the inputs heard by then are in.</summary>
        public void Hurt(Simulation sim, in HeroHit hit)
        {
            if (state.Dead) return;
            int at = pending.Count;
            while (at > 0 && pending[at - 1].T > hit.T) at--;
            pending.Add(hit);
            for (int i = pending.Count - 1; i > at; i--) pending[i] = pending[i - 1];
            pending[at] = hit;
        }

        /// <summary>A dash's i-frames: the hero can't be hurt from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void Shield(int dash, double from, double to, double heard, JudgedBy by)
        {
            if (windowCount == Windows)
            {
                Array.Copy(windows, 1, windows, 0, Windows - 1);
                windowCount--;
            }
            windows[windowCount++] = new Window { Dash = dash, From = from, To = to, Heard = heard, By = by };
            state.IFramesUntil = to;
        }

        /// <summary>The tick's end: the hits whose settle time has come settle, in landing order.</summary>
        public void Tick(Simulation sim, double now)
        {
            double settle = SettleS;
            int n = 0;
            while (n < pending.Count && pending[n].T + settle <= now + 1e-9) n++;
            for (int i = 0; i < n; i++) Settle(sim, pending[i]);
            pending.RemoveFront(n);
        }

        internal void ApplySupports(Simulation sim)
        {
            if (sim.State.Held || sim.State.Phase != RunPhase.Playing || state.Dead) return;
            state.MaxHealth = rules.Health * (1 + sim.State.Build.HeroBonus(StatFamily.Vitality));
            // Vitality grants capacity, never a free heal.
            state.Health = Math.Min(state.Health, state.MaxHealth);
            Heal(sim, sim.State.Build.HeroBonus(StatFamily.Regeneration) * Simulation.TickSeconds, HealReason.Regeneration);
        }

        /// <summary>Heals a boss kill's share (all of it).</summary>
        public void HealBossKill(Simulation sim) => Heal(sim, rules.BossKillHeal * state.MaxHealth, HealReason.BossKill);

        /// <summary>Heals <paramref name="amount"/>, up to full health; returns what it gained.</summary>
        public double Heal(Simulation sim, double amount, HealReason reason)
        {
            if (state.Dead || !(amount > 0)) return 0;
            double gained = Math.Min(state.MaxHealth - state.Health, amount);
            if (!(gained > 0)) return 0;
            state.Health += gained;
            sim.Emit(SimEvent.HeroHeal(sim.Now, reason, gained, state.Health));
            return gained;
        }

        void Settle(Simulation sim, in HeroHit hit)
        {
            if (state.Dead) return;
            HitOutcome outcome;
            double taken = 0;
            int w = Covering(hit.T);
            if (w >= 0)
                outcome = Perfect(windows[w], hit) ? HitOutcome.PerfectDodge : HitOutcome.Dodged;
            else if (hit.T < state.HurtAt + rules.HurtInvulnerableS - 1e-9)
                outcome = HitOutcome.Immune;
            else
            {
                taken = Math.Min(state.Health, Math.Max(0, hit.Damage) / (1 + sim.State.Build.HeroBonus(StatFamily.Armor)));
                state.Health -= taken;
                state.HurtAt = hit.T;
                outcome = HitOutcome.Hurt;
                if (state.Health <= 1e-9)
                {
                    state.Health = 0;
                    state.Dead = true;
                    outcome = HitOutcome.Killed;
                }
            }
            sim.Emit(SimEvent.HeroHit(hit.T, outcome, hit.Source, hit.Owner, taken, state.Health, hit.FromX, hit.FromY,
                w >= 0 ? windows[w].Dash : -1));
            Settled?.Invoke(sim, hit, outcome);
            if (outcome != HitOutcome.Killed) return;
            sim.HeroWas(hit.T, out double x, out double y);
            sim.Emit(SimEvent.HeroDeath(hit.T, x, y, hit.FromX, hit.FromY));
            sim.End(RunEnd.Death);
        }

        /// <summary>The newest i-frame window covering <paramref name="t"/>, or -1.</summary>
        int Covering(double t)
        {
            for (int i = windowCount - 1; i >= 0; i--)
                if (windows[i].From <= t + 1e-9 && t <= windows[i].To + 1e-9) return i;
            return -1;
        }

        bool Perfect(in Window w, in HeroHit hit)
        {
            if (double.IsNaN(hit.Beat)) return false;
            if (w.By == JudgedBy.Beatless) return true;
            return w.By != JudgedBy.Off && Math.Abs(w.Heard - hit.Beat) <= beatWindow + 1e-9;
        }
    }
}
