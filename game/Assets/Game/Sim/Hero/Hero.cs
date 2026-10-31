using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The grid hero's rules (unity-game.md, "Hero Movement on the Grid" and "Key Presses"). The
    /// hero moves one tile at once on a press, then one per stride step while the key is held, in
    /// four directions. Keys reach the sim the lead after the player heard them, so every move is
    /// judged in heard time and drawn from when it happened there, though the sim takes it later:
    ///
    /// - <b>A press</b> (direction d, heard at h), taken at the sim's current time F:
    ///   <code>
    ///   s = the latest stride step at or before h + 0.11   // one the stride would still be on is its
    ///   if a press already strode for s:  queue d for the first step after F no press took
    ///   else:                             stride now, drawn from h; s is this press's
    ///   </code>
    ///   The stride sets off from where the hero is at h, cutting short a move in progress, so the
    ///   client shows it on the frame the key went down: the sim's lead never delays it. No held
    ///   key strides on s, and one press stride per step keeps mashing at the grid's pace.
    /// - <b>Held keys</b> stride on every step a press hasn't taken, the last-pressed held direction
    ///   winning, as they were held when the step was heard. A step's held stride is judged once
    ///   the sim has every key heard before it: at the next key change heard after it, or the
    ///   latest key's lead after it (<see cref="Tick"/>). So a tap released before the next step
    ///   was heard never strides on it, and a turn heard before a step the sim has passed turns on it.
    /// - Space is a press only: it stomps (stands still).
    /// - <b>A stride</b> takes <see cref="StrideSeconds"/> from its start, compressed to end before
    ///   the next step that may stride. A step into a blocked tile or the arena's edge bumps in
    ///   place and is spent. The hero faces the way it tried to go.
    /// - While the world is frozen (<see cref="SimState.Frozen"/>) the hero doesn't move: steps
    ///   spend held keys and queued presses without moving it, and presses made then are only
    ///   held (as native's, a press made during a freeze never strides after it).
    /// - A fallen hero (<see cref="HeroState.Fallen"/>) never moves again.
    ///
    /// Each move is a <see cref="SimEventKind.HeroStep"/> event, emitted when the sim takes it and
    /// carrying its start in heard time, which may precede it by up to the lead.
    /// </summary>
    internal sealed class Hero
    {
        public const double StrideSeconds = 0.110;

        const int None = -1, Stomp = 4;

        /// <summary>A stride step whose held stride is still to be judged.</summary>
        struct Pending
        {
            public int Index;
            public double T, Period;
            public bool Frozen;
        }

        readonly HeroState state;
        readonly ArenaGrid arena;
        readonly StrideGrid strides;
        // The held directions, last pressed last.
        readonly Direction[] held = new Direction[4];
        int heldCount;
        int queued = None;
        // The stride step the latest press stride took, by index.
        int pressed = int.MinValue;
        // How long after the player hears a key the sim takes it: the latest key's.
        double lag;
        readonly Pending[] pending = new Pending[8];
        int pendingHead, pendingCount;

        public Hero(HeroState state, ArenaGrid arena, StrideGrid strides)
        {
            this.state = state;
            this.arena = arena;
            this.strides = strides;
            Place(arena.HeroStart.X, arena.HeroStart.Y);
            state.FromX = state.X;
            state.FromY = state.Y;
        }

        public void Press(Simulation sim, Key key, double heard)
        {
            KeyChange(sim, heard);
            if (key != Key.Space) Hold((Direction)key);
            // A press made while frozen never strides, even when the world thaws before the next step.
            if (sim.State.Frozen || state.Fallen) return;
            int action = key == Key.Space ? Stomp : (int)key;
            // The next step, as the grid plans it now: a beat revealed before then may still move it.
            double next = strides.NextTime(sim.Now);
            int s = StepOf(heard, next);
            if (s == pressed)
            {
                queued = action;
                return;
            }
            pressed = s;
            queued = None;
            double start = Math.Min(heard, sim.Now);
            double end = start + Math.Min(StrideSeconds, NextStrideAfter(start, s, next) - start);
            Move(sim, action, s, sim.Now, start, end, true);
        }

        public void Release(Simulation sim, Key key, double heard)
        {
            KeyChange(sim, heard);
            if (key == Key.Space) return;
            int i = Array.IndexOf(held, (Direction)key, 0, heldCount);
            if (i < 0) return;
            Array.Copy(held, i + 1, held, i, heldCount - i - 1);
            heldCount--;
        }

        /// <summary>Stride step <paramref name="index"/> at <paramref name="t"/>, <paramref name="period"/> before the next.</summary>
        public void Step(Simulation sim, int index, double t, double period)
        {
            if (pendingCount == pending.Length) Judge(sim, t);
            pending[(pendingHead + pendingCount++) % pending.Length] =
                new Pending { Index = index, T = t, Period = period, Frozen = sim.State.Frozen };
            Tick(sim, t);
        }

        /// <summary>At song time <paramref name="now"/>, judges the steps the keys heard by now have settled.</summary>
        public void Tick(Simulation sim, double now)
        {
            while (pendingCount > 0 && pending[pendingHead].T + lag <= now + 1e-9) Judge(sim, now);
        }

        /// <summary>A key heard at <paramref name="heard"/>: the steps heard before it are judged without it.</summary>
        void KeyChange(Simulation sim, double heard)
        {
            lag = Math.Max(0, sim.Now - heard);
            while (pendingCount > 0 && pending[pendingHead].T < heard - 1e-9) Judge(sim, sim.Now);
        }

        /// <summary>Judges the oldest pending step, taking its held stride at song time <paramref name="now"/>.</summary>
        void Judge(Simulation sim, double now)
        {
            var p = pending[pendingHead];
            pendingHead = (pendingHead + 1) % pending.Length;
            pendingCount--;
            if (p.Index == pressed) return; // a press strode for it, ahead of it
            int action = queued != None ? queued : heldCount > 0 ? (int)held[heldCount - 1] : None;
            queued = None;
            if (action != None && !p.Frozen && !state.Fallen)
                Move(sim, action, p.Index, now, p.T, p.T + Math.Min(StrideSeconds, p.Period), false);
        }

        void Hold(Direction d)
        {
            int i = Array.IndexOf(held, d, 0, heldCount);
            if (i >= 0)
            {
                Array.Copy(held, i + 1, held, i, heldCount - i - 1);
                heldCount--;
            }
            held[heldCount++] = d;
        }

        /// <summary>
        /// The index of the stride step a press heard at <paramref name="heard"/> belongs to: the
        /// latest at or before the end of its stride, so one the stride would still be on is the
        /// press's. <see cref="StrideGrid.Count"/> is the step still to come, planned at <paramref name="next"/>.
        /// </summary>
        int StepOf(double heard, double next)
        {
            double end = heard + StrideSeconds + 1e-9;
            if (next <= end) return strides.Count;
            int n = strides.RecentCount, back = 0;
            while (back < n && strides.Recent(back).T > end) back++;
            return strides.Count - 1 - back;
        }

        /// <summary>
        /// The first step after <paramref name="start"/> that may stride, the press having taken step
        /// <paramref name="taken"/>: a step simulated but not yet judged, else the next one planned
        /// (at <paramref name="next"/>), or the one after it if the press took that.
        /// </summary>
        double NextStrideAfter(double start, int taken, double next)
        {
            double at = taken == strides.Count ? next + strides.Period : next;
            for (int back = 0; back < strides.RecentCount; back++)
            {
                var r = strides.Recent(back);
                if (r.T <= start + 1e-9) break;
                if (strides.Count - 1 - back != taken) at = r.T;
            }
            return at;
        }

        /// <summary>A move from where the hero is at <paramref name="start"/>, cutting short one in progress; emitted at <paramref name="t"/>.</summary>
        void Move(Simulation sim, int action, int step, double t, double start, double end, bool press)
        {
            state.PositionAt(start, out double fromX, out double fromY);
            bool bump = false;
            if (action != Stomp)
            {
                var d = (Direction)action;
                state.Facing = d;
                int x = state.TileX + (d == Direction.Right ? 1 : d == Direction.Left ? -1 : 0);
                int y = state.TileY + (d == Direction.Up ? 1 : d == Direction.Down ? -1 : 0);
                if (arena.IsOpen(x, y)) Place(x, y);
                else bump = true;
            }
            state.FromX = fromX;
            state.FromY = fromY;
            state.MoveStart = start;
            state.MoveEnd = end;
            sim.Emit(SimEvent.HeroStep(t, step, press, bump, action == Stomp, state.Facing,
                fromX, fromY, state.X, state.Y, start, end));
        }

        void Place(int x, int y)
        {
            state.TileX = x;
            state.TileY = y;
            state.X = arena.CentreX(x);
            state.Y = arena.CentreY(y);
        }
    }
}
