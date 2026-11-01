using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// What the run's end card shows (native's eval/recap.rs), tallied by the sim as the run plays, so
    /// it is the same at any frame rate and in a session's replay: the kills, and each of the hero's
    /// weapons' damage and how long it was held on the run clock, from joining the slots until it
    /// left them (evolved away), a rank up not restarting it. A jump of the run clock
    /// (<see cref="RunRules.JumpTo"/>) isn't time held. The run's end closes it: nothing tallies after.
    /// </summary>
    public sealed class RunRecap
    {
        readonly SimState state;
        double[] damage = Array.Empty<double>(), held = Array.Empty<double>(), since = Array.Empty<double>();
        bool closed;

        internal RunRecap(SimState state) => this.state = state;

        /// <summary>The enemies the run killed: the hero's weapons' and the finale's.</summary>
        public int Kills { get; private set; }

        /// <summary>The damage each weapon dealt to enemies, by content's weapon index: the whole of each hit, overkill too.</summary>
        public ReadOnlySpan<double> Damage => damage;

        /// <summary>How long weapon <paramref name="weapon"/> (content's index) was held on the run clock (s).</summary>
        public double Held(int weapon) =>
            weapon < 0 || weapon >= held.Length ? 0
            : held[weapon] + (double.IsNaN(since[weapon]) ? 0 : Math.Max(0, state.RunClock - since[weapon]));

        /// <summary>The weapons content has, before any is learned or deals damage.</summary>
        internal void Begin(int weapons)
        {
            damage = new double[weapons];
            held = new double[weapons];
            since = new double[weapons];
            for (int i = 0; i < weapons; i++) since[i] = double.NaN;
        }

        internal void Dealt(int weapon, double amount)
        {
            if (!closed) damage[weapon] += amount;
        }

        internal void Killed()
        {
            if (!closed) Kills++;
        }

        /// <summary>Weapon <paramref name="weapon"/> joined the slots now.</summary>
        internal void Joined(int weapon)
        {
            if (!closed && double.IsNaN(since[weapon])) since[weapon] = state.RunClock;
        }

        /// <summary>Weapon <paramref name="weapon"/> left the slots now.</summary>
        internal void Left(int weapon)
        {
            if (closed || double.IsNaN(since[weapon])) return;
            held[weapon] += Math.Max(0, state.RunClock - since[weapon]);
            since[weapon] = double.NaN;
        }

        /// <summary>The run clock jumped <paramref name="delta"/> ahead: the weapons held go on from the new time.</summary>
        internal void Shift(double delta)
        {
            for (int i = 0; i < since.Length; i++) since[i] += delta; // NaN stays NaN
        }

        /// <summary>The run ended now: every weapon held stops being held, and the tallies are final.</summary>
        internal void Close()
        {
            if (closed) return;
            for (int i = 0; i < since.Length; i++) Left(i);
            closed = true;
        }
    }
}
