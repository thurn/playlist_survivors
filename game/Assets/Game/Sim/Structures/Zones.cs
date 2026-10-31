using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>What moved the arena to its next zone, the cause in zone-change events.</summary>
    public enum ZoneCause
    {
        /// <summary>A structure change: the music moved to a new section.</summary>
        Change = 0,
        /// <summary>A loudness jump of schedule.json's <c>jumpDb</c> or more.</summary>
        Jump = 1,
        /// <summary>No detected change in time: the timed fallback.</summary>
        Fallback = 2,
    }

    /// <summary>
    /// The arena's five zones (unity-game.md, Music-Driven Events, "Zones"; schedule.json's
    /// <c>zones</c>). M3's music-events bead adds the palettes and the rest.
    ///
    /// - <b>Detected changes</b>: revealed structure changes, and jumps of <c>jumpDb</c> or more,
    ///   taken in known-at order (a change first at equal times). One whose time is less than
    ///   <c>minGapS</c> after the previous zone change is dropped (the first has no gap to keep).
    /// - <b>Landing</b>: a kept change lands on the first downbeat at or after both its time and
    ///   its known-at time: the first downbeat at or after its time when it is known by then, else
    ///   the first downbeat after it became known. One change waits at a time; the next is taken
    ///   once it lands, and judged against it.
    /// - <b>Fallback</b>: with no change waiting, the first downbeat at or after
    ///   <c>openingFallbackS</c> past the previous zone change (or the start) while that change
    ///   was within the run's first <c>openingS</c> of playing time, else <c>fallbackS</c> past it.
    ///   With no downbeat by <see cref="FallbackGraceS"/> after that (no beat locked), it lands at
    ///   the tick's end.
    /// - Each zone change moves to the next zone, cycling, and tells <see cref="Structures"/>.
    ///   While the world is frozen nothing lands; a change waits for a downbeat after the thaw.
    /// </summary>
    internal sealed class Zones
    {
        public const double FallbackGraceS = 4;

        readonly BeliefLog log;
        readonly ZoneRules rules;
        readonly SimState state;
        readonly Structures structures;
        int changeFrom, jumpFrom;
        bool waiting, any;
        ZoneCause waitingCause;
        double waitingT, waitingKnownAt;
        double last, lastRun;
        // Detected zone changes landed, and how many of them a slam has claimed.
        int detected, slammed;

        public Zones(ZoneRules rules, BeliefLog log, SimState state, Structures structures, double start)
        {
            this.rules = rules;
            this.log = log;
            this.state = state;
            this.structures = structures;
            last = start;
        }

        /// <summary>When the fallback falls due.</summary>
        double Due => last + (lastRun < rules.OpeningS - 1e-9 ? rules.OpeningFallbackS : rules.FallbackS);

        /// <summary>A downbeat at <paramref name="t"/>: a waiting change lands if it may, else the fallback if it is due.</summary>
        internal void Downbeat(Simulation sim, double t)
        {
            if (state.Frozen) return;
            Take();
            if (waiting)
            {
                if (t < Math.Max(waitingT, waitingKnownAt) - 1e-9) return;
                waiting = false;
                Change(sim, t, waitingCause, waitingT, waitingKnownAt);
                return;
            }
            double due = Due;
            if (t >= due - 1e-9) Change(sim, t, ZoneCause.Fallback, due, due);
        }

        /// <summary>
        /// Does the hero's strike on the downbeat at <paramref name="downbeat"/>, winding up now,
        /// slam (unity-game.md, The Sword: the first downbeat after a section change)? It does when
        /// the detected change waiting now lands on that downbeat, so the slam and the zone change
        /// share it, or when one landed since the last slam (revealed after its downbeat's wind-up
        /// began), a bar late. Each detected change claims one slam; the fallback claims none.
        /// </summary>
        internal bool Slams(double downbeat)
        {
            Take();
            if (waiting && downbeat >= Math.Max(waitingT, waitingKnownAt) - 1e-9)
            {
                slammed = detected + 1;
                return true;
            }
            if (slammed >= detected) return false;
            slammed = detected;
            return true;
        }

        /// <summary>The per-tick check, at the tick's end: a fallback no downbeat took.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (state.Frozen) return;
            Take();
            double due = Due;
            if (!waiting && end >= due + FallbackGraceS - 1e-9) Change(sim, end, ZoneCause.Fallback, due, due);
        }

        /// <summary>With no change waiting, takes the next revealed one, dropping those too close after the previous zone change.</summary>
        void Take()
        {
            var changes = log.Revealed(EntryKind.Change);
            var jumps = log.Revealed(EntryKind.Jump);
            while (!waiting)
            {
                while (jumpFrom < jumps.Length && new JumpEntry(jumps[jumpFrom]).Db < rules.JumpDb - 1e-9) jumpFrom++;
                bool change = changeFrom < changes.Length, jump = jumpFrom < jumps.Length;
                if (!change && !jump) return;
                if (change && jump)
                    change = log.KnownAt(EntryKind.Change, changeFrom) <= log.KnownAt(EntryKind.Jump, jumpFrom);
                var kind = change ? EntryKind.Change : EntryKind.Jump;
                int i = change ? changeFrom++ : jumpFrom++;
                double t = (change ? changes : jumps)[i].T;
                if (any && t < last + rules.MinGapS - 1e-9) continue;
                waiting = true;
                waitingCause = change ? ZoneCause.Change : ZoneCause.Jump;
                waitingT = t;
                waitingKnownAt = log.KnownAt(kind, i);
            }
        }

        void Change(Simulation sim, double t, ZoneCause cause, double causeT, double knownAt)
        {
            int previous = state.Zone;
            state.Zone = (previous + 1) % rules.Ids.Length;
            last = t;
            lastRun = state.RunClock;
            any = true;
            if (cause != ZoneCause.Fallback) detected++;
            sim.Emit(SimEvent.ZoneChange(t, state.Zone, previous, cause, causeT, knownAt));
            structures.ZoneChanged();
        }
    }
}
