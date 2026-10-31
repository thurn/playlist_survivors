using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The grid hero's rules (unity-game.md, "Hero Movement on the Grid" and "Key Presses"). The
    /// hero moves one tile per stride step, in four directions, and at once on a press.
    ///
    /// - <b>A press</b> (direction d, heard at h), taken at the sim's current time F:
    ///   <code>
    ///   s = the latest stride step at or before h + 0.11   // one the stride would still be on is its
    ///   if a press already strode for s:  queue d for the first step after F no press took
    ///   else:                             stride now, drawn from h; s is this press's
    ///   </code>
    ///   A press stride changes the tile at F, emitted then, and is drawn from h (or the end of the
    ///   move in progress, if later), so the client shows it on the frame the key went down: the
    ///   sim's lead never delays it. When s is still to come, no held key strides on it, so a tap
    ///   or the start of a hold never strides twice in a row. One press stride per step keeps
    ///   mashing at the grid's pace.
    /// - <b>Held keys</b> stride on every step a press hasn't taken; the last-pressed held
    ///   direction wins. Space is a press only: it stomps (stands still).
    /// - <b>A stride</b> takes <see cref="StrideSeconds"/> from its start, compressed to end before
    ///   the next step that may stride. A step into a blocked tile or the arena's edge bumps in
    ///   place and is spent. The hero faces the way it tried to go.
    /// - While the world is frozen (<see cref="SimState.Frozen"/>) the hero doesn't move: steps
    ///   spend held keys and queued presses without moving it, and presses made then are only
    ///   held (as native's, a press made during a freeze never strides after it).
    /// - A fallen hero (<see cref="HeroState.Fallen"/>) never moves again.
    ///
    /// Each move is a <see cref="SimEventKind.HeroStep"/> event, emitted at its start, or for a press
    /// stride when the sim takes the press.
    /// </summary>
    internal sealed class Hero
    {
        public const double StrideSeconds = 0.110;

        const int None = -1, Stomp = 4;

        readonly HeroState state;
        readonly ArenaGrid arena;
        readonly StrideGrid strides;
        // The held directions, last pressed last.
        readonly Direction[] held = new Direction[4];
        int heldCount;
        int queued = None;
        // The stride step the latest press stride took, by index.
        int pressed = int.MinValue;

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
            double start = Math.Max(Math.Min(heard, sim.Now), state.MoveEnd);
            if (s == strides.Count) next += strides.Period; // the press took it: the stride may run to the one after
            Move(sim, action, s, sim.Now, start, start + Math.Min(StrideSeconds, next - start), true);
        }

        public void Release(Key key)
        {
            if (key == Key.Space) return;
            int i = Array.IndexOf(held, (Direction)key, 0, heldCount);
            if (i < 0) return;
            Array.Copy(held, i + 1, held, i, heldCount - i - 1);
            heldCount--;
        }

        /// <summary>Stride step <paramref name="index"/> at <paramref name="t"/>, <paramref name="period"/> before the next.</summary>
        public void Step(Simulation sim, int index, double t, double period)
        {
            if (index == pressed) return; // a press strode for it, ahead of it
            int action = queued != None ? queued : heldCount > 0 ? (int)held[heldCount - 1] : None;
            queued = None;
            if (action != None && !sim.State.Frozen && !state.Fallen)
                Move(sim, action, index, t, t, t + Math.Min(StrideSeconds, period), false);
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

        void Move(Simulation sim, int action, int step, double t, double start, double end, bool press)
        {
            double fromX = state.X, fromY = state.Y;
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
