using System;

namespace Champions.Sim
{
    /// <summary>
    /// The hero's walk and its history (design.md, "Controls" and "The Hero"; plan.md, "Inputs and heard
    /// time"), ported from game/'s FreeHero:
    ///
    /// - <b>Keys</b>: the held movement keys steer, eight ways, diagonals as fast as the rest; opposite keys
    ///   cancel. A movement key press drops the click target.
    /// - <b>Click target</b>: walks the hero straight to a ground point; held, the client sends the cursor's
    ///   point as it moves, so the hero follows it. The held keys win over a target while any is down. No
    ///   path finding.
    /// - <b>Speed</b>: <see cref="HeroRules.SpeedMps"/>, always.
    /// - <b>Heard time</b>: an input heard at h, taken at the sim's time F, moves the hero from h: its path
    ///   since h (up to <see cref="History"/> ticks back, and never before a hold) is walked again with the
    ///   new input (<see cref="Rewalk"/>). Nothing else is re-simulated: the sim's systems in (h, F] already
    ///   saw the old path, which differs by at most the lead's walk. So the client, drawing trailing by the
    ///   lead, shows the turn on the frame the key went down.
    /// - While the world is held the hero stands, and a click target given then is dropped.
    ///
    /// Each change of velocity is a <see cref="SimEventKind.HeroMove"/>, emitted when the sim takes it and
    /// drawn from its start, which a re-walk puts up to the lead before it. The dashes, structures and
    /// health build on this (<see cref="Advance"/> is where a dash's span and blocking go).
    /// </summary>
    internal sealed class HeroWalk
    {
        /// <summary>The ticks of path kept, about half a second: the furthest back a heard input re-walks.</summary>
        public const int History = 64;
        /// <summary>A velocity change under this (m/s) emits no move: a target's aim, renormalised each tick, wobbles in its last bits.</summary>
        const double Same = 1e-6;

        readonly HeroState state;
        readonly double speed;
        // The held movement keys, by Key (Up, Down, Left, Right).
        readonly bool[] held = new bool[4];
        bool targeting;
        double targetX, targetY;
        // The path at the latest ticks' ends, newest at pathHead.
        readonly double[] pathT = new double[History], pathX = new double[History], pathY = new double[History];
        int pathHead = -1, pathCount;
        // The latest move's velocity.
        double moveX, moveY;
        bool moved;
        // The earliest time an input may re-walk from: the latest tick end the world was held.
        double floor = double.NegativeInfinity;

        public HeroWalk(HeroState state, HeroRules rules, double start)
        {
            this.state = state;
            speed = rules.SpeedMps;
            Chord(0, 0, 0, 0, start, start);
        }

        public bool Targeting => targeting;

        public void Press(Simulation sim, Key key, double heard)
        {
            if (key > Key.Right) return;
            held[(int)key] = true;
            targeting = false;
            Rewalk(sim, heard);
        }

        public void Release(Simulation sim, Key key, double heard)
        {
            if (key > Key.Right || !held[(int)key]) return;
            held[(int)key] = false;
            Rewalk(sim, heard);
        }

        public void Target(Simulation sim, double x, double y, double heard)
        {
            if (sim.State.Held) return;
            targeting = true;
            targetX = x;
            targetY = y;
            Rewalk(sim, heard);
        }

        /// <summary>
        /// The world was just held or let go: the hero stops or sets off now, and no input re-walks across the
        /// moment.
        /// </summary>
        public void HeldChanged(Simulation sim)
        {
            floor = sim.Now;
            Rewalk(sim, sim.Now);
        }

        /// <summary>Lets go of the held keys and the target (a card moment's opening).</summary>
        public void LetGo()
        {
            Array.Clear(held, 0, held.Length);
            targeting = false;
        }

        /// <summary>
        /// The way the hero means to go from (<paramref name="x"/>, <paramref name="y"/>), a unit vector: the
        /// held keys', else toward the click target, else the facing (a dash's direction, design.md "Rules
        /// every dash keeps").
        /// </summary>
        public void Direction(double x, double y, out double dx, out double dy)
        {
            Keys(out int across, out int up);
            double ex = across, ey = up;
            if (across == 0 && up == 0 && targeting)
            {
                ex = targetX - x;
                ey = targetY - y;
            }
            double d = Math.Sqrt(ex * ex + ey * ey);
            if (d < 1e-9)
            {
                dx = state.FacingX;
                dy = state.FacingY;
                return;
            }
            dx = ex / d;
            dy = ey / d;
        }

        public void Tick(Simulation sim, double now)
        {
            if (sim.State.Held) floor = now;
            Remember(now, state.X, state.Y);
            Walk(sim, now, state.X, state.Y, false);
        }

        /// <summary>Plans the tick ahead from (<paramref name="x"/>, <paramref name="y"/>) at the sim's time <paramref name="now"/>.</summary>
        void Walk(Simulation sim, double now, double x, double y, bool force)
        {
            double dt = Simulation.TickSeconds;
            Advance(sim, x, y, dt, out double nx, out double ny);
            Chord(x, y, nx, ny, now, now + dt);
            Move(sim, now, now, x, y, (nx - x) / dt, (ny - y) / dt, force);
        }

        /// <summary>
        /// An input heard at <paramref name="heard"/> changed what the hero means to do: its path from then to
        /// the sim's time is walked again, then the tick ahead planned.
        /// </summary>
        public void Rewalk(Simulation sim, double heard)
        {
            double now = sim.Now;
            double t = Math.Min(now, Math.Max(heard, floor));
            if (pathCount == 0 || t >= now - 1e-9)
            {
                state.PositionAt(now, out double hx, out double hy);
                Walk(sim, now, hx, hy, true);
                return;
            }
            // The first kept tick end after t, and where the path was at t; the oldest kept is as far back as it goes.
            int back = pathCount - 1;
            double x, y;
            if (t <= PathT(back))
            {
                t = PathT(back);
                x = pathX[Index(back)];
                y = pathY[Index(back)];
                back--;
            }
            else
            {
                while (PathT(back) <= t) back--;
                int i = Index(back + 1), j = Index(back);
                double u = (t - pathT[i]) / (pathT[j] - pathT[i]);
                x = pathX[i] + (pathX[j] - pathX[i]) * u;
                y = pathY[i] + (pathY[j] - pathY[i]) * u;
            }
            bool first = true;
            for (; back >= 0; back--)
            {
                double b = PathT(back), span = b - t;
                if (span > 1e-9)
                {
                    Advance(sim, x, y, span, out double nx, out double ny);
                    Move(sim, now, t, x, y, (nx - x) / span, (ny - y) / span, first);
                    first = false;
                    x = nx;
                    y = ny;
                }
                int i = Index(back);
                pathX[i] = x;
                pathY[i] = y;
                t = b;
            }
            Walk(sim, now, x, y, first);
        }

        /// <summary>
        /// Where the hero was at <paramref name="t"/>: along the tick ahead's chord from its start, else between
        /// the kept tick ends (the path a re-walk revised), else at the oldest kept.
        /// </summary>
        public void PositionWas(double t, out double x, out double y)
        {
            if (pathCount == 0 || t >= state.MoveStart)
            {
                state.PositionAt(t, out x, out y);
                return;
            }
            int back = 0;
            while (back < pathCount - 1 && PathT(back) > t) back++;
            int i = Index(back);
            if (back == 0 || pathT[i] >= t)
            {
                x = pathX[i];
                y = pathY[i];
                return;
            }
            int j = Index(back - 1);
            double u = (t - pathT[i]) / (pathT[j] - pathT[i]);
            x = pathX[i] + (pathX[j] - pathX[i]) * u;
            y = pathY[i] + (pathY[j] - pathY[i]) * u;
        }

        void Keys(out int across, out int up)
        {
            across = (held[(int)Key.Right] ? 1 : 0) - (held[(int)Key.Left] ? 1 : 0);
            up = (held[(int)Key.Up] ? 1 : 0) - (held[(int)Key.Down] ? 1 : 0);
        }

        /// <summary>Where the hero's input takes it from (<paramref name="x"/>, <paramref name="y"/>) over <paramref name="span"/> seconds.</summary>
        void Advance(Simulation sim, double x, double y, double span, out double nx, out double ny)
        {
            nx = x;
            ny = y;
            if (sim.State.Held) return;
            Keys(out int across, out int up);
            double step = speed * span, dx = 0, dy = 0;
            if (across != 0 || up != 0)
            {
                double scale = across != 0 && up != 0 ? Math.Sqrt(0.5) * step : step;
                dx = across * scale;
                dy = up * scale;
            }
            else if (targeting)
            {
                double ex = targetX - x, ey = targetY - y, d = Math.Sqrt(ex * ex + ey * ey);
                if (d <= step)
                {
                    dx = ex;
                    dy = ey;
                    targeting = false;
                }
                else
                {
                    dx = ex / d * step;
                    dy = ey / d * step;
                }
            }
            if (dx == 0 && dy == 0) return;
            double len = Math.Sqrt(dx * dx + dy * dy);
            state.FacingX = dx / len;
            state.FacingY = dy / len;
            nx = x + dx;
            ny = y + dy;
        }

        void Chord(double fromX, double fromY, double toX, double toY, double start, double end)
        {
            state.FromX = fromX;
            state.FromY = fromY;
            state.X = toX;
            state.Y = toY;
            state.MoveStart = start;
            state.MoveEnd = end;
        }

        void Move(Simulation sim, double t, double start, double x, double y, double vx, double vy, bool force)
        {
            if (!force && moved && Math.Abs(vx - moveX) < Same && Math.Abs(vy - moveY) < Same) return;
            moved = true;
            moveX = vx;
            moveY = vy;
            sim.Emit(SimEvent.HeroMove(t, start, x, y, vx, vy));
        }

        void Remember(double t, double x, double y)
        {
            pathHead = (pathHead + 1) % History;
            pathT[pathHead] = t;
            pathX[pathHead] = x;
            pathY[pathHead] = y;
            if (pathCount < History) pathCount++;
        }

        /// <summary>The ring index of the tick <paramref name="back"/> ticks before the newest.</summary>
        int Index(int back) => (pathHead - back + History) % History;

        double PathT(int back) => pathT[Index(back)];
    }
}
