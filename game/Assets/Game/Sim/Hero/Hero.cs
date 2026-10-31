using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The grid hero's rules (unity-game.md, "Hero Movement on the Grid" and "Key Presses and Late
    /// Steps"). The hero moves one tile per stride step, in four directions.
    ///
    /// - <b>A press</b> (direction d, heard at h), taken at the sim's current time F:
    ///   <code>
    ///   P = stride period at h;  g = min(0.10, 0.4 * P)
    ///   s = the stride step in [h - g, h] if any, else the first step after h
    ///   if s is still to come:     queue d for step s       // normal
    ///   else if no move at s:      stride now (late), from the hero's current tile
    ///   else:                      queue d for the first step after F
    ///   </code>
    ///   Every step at or before F is simulated, so "still to come" is the next step. One press
    ///   waits for it: a later press replaces it, as the last-pressed held key wins.
    /// - <b>Held keys</b> stride on every step a press hasn't claimed; the last-pressed held
    ///   direction wins. Space is a press only: it stomps (stands still) on its step.
    /// - <b>A stride</b> takes <see cref="StrideSeconds"/> from its step, or from F when late,
    ///   compressed to end before the next step. A step into a blocked tile or the arena's edge
    ///   bumps in place and is spent. The hero faces the way it tried to go.
    /// - While the world is frozen (<see cref="SimState.Frozen"/>) the hero doesn't move: steps
    ///   spend held keys and queued presses without moving it, and presses don't stride late.
    ///
    /// Each move is a <see cref="SimEventKind.HeroStep"/> event, emitted at its start.
    /// </summary>
    internal sealed class Hero
    {
        public const double StrideSeconds = 0.110;
        public const double MaxGrace = 0.10;
        public const double GraceShare = 0.4;

        const int None = -1, Stomp = 4;

        readonly HeroState state;
        readonly ArenaGrid arena;
        readonly StrideGrid strides;
        // Whether the hero moved on each recent step, by step index, as far back as the grid remembers.
        readonly bool[] moved = new bool[StrideGrid.HistoryLength];
        // The held directions, last pressed last.
        readonly Direction[] held = new Direction[4];
        int heldCount;
        int queued = None;

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
            int action = key == Key.Space ? Stomp : (int)key;
            int s = SimulatedStepOf(heard);
            if (s < 0 || moved[s % moved.Length] || sim.State.Frozen)
            {
                queued = action;
                return;
            }
            // A late step: the press belongs to a step already simulated, on which the hero stood still.
            // (A step at the frontier itself is on time: the stride starts on it.)
            moved[s % moved.Length] = true;
            double step = strides.Recent(strides.Count - 1 - s).T;
            bool late = sim.Now > step + 1e-9;
            double start = late ? sim.Now : step;
            // The next step as the grid plans it now: a beat revealed before then may still move it.
            double next = strides.NextTime(sim.Now);
            Move(sim, action, s, start, start + Math.Min(StrideSeconds, next - start), late);
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
            int action = queued != None ? queued : heldCount > 0 ? (int)held[heldCount - 1] : None;
            queued = None;
            bool moves = action != None && !sim.State.Frozen;
            moved[index % moved.Length] = moves;
            if (moves) Move(sim, action, index, t, t + Math.Min(StrideSeconds, period), false);
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
        /// The index of the simulated step a press heard at <paramref name="heard"/> belongs to: the
        /// latest step within the grace window before it, else the first step after it; -1 when that
        /// step is still to come (or older than the grid remembers, which counts as taken).
        /// </summary>
        int SimulatedStepOf(double heard)
        {
            int n = strides.RecentCount;
            if (n == 0) return -1;
            int back = 0;
            while (back < n && strides.Recent(back).T > heard + 1e-9) back++;
            double period = strides.Recent(Math.Min(back, n - 1)).Period;
            double grace = Math.Min(MaxGrace, GraceShare * period);
            if (back < n && strides.Recent(back).T >= heard - grace - 1e-9) return strides.Count - 1 - back;
            if (back == 0) return -1;
            return back < n || strides.Count == n ? strides.Count - back : -1;
        }

        void Move(Simulation sim, int action, int step, double start, double end, bool late)
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
            sim.Emit(SimEvent.HeroStep(start, step, late, bump, action == Stomp, state.Facing,
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
