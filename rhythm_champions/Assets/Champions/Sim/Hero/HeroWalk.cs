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
    /// - <b>Dashes</b> (<see cref="Dashes"/>): from a dash's start to its end the hero moves at the dash's
    ///   velocity instead of walking; a dash heard before the sim took it is walked again from its start, as a
    ///   heard input is. A newer dash cuts the one before short. A dash whose path passes its click target
    ///   (within the hero's radius) clears it, so the hero never walks back to it.
    /// - <b>Structures</b> (<see cref="IStructures"/>, once installed): they stop the hero, walking or dashing,
    ///   and it slides along them. A damaging dash that runs into one strikes it first and, if that breaks it,
    ///   carries on through (<see cref="Dashes.BreakThrough"/>). Enemies never block it.
    ///
    /// Each change of velocity is a <see cref="SimEventKind.HeroMove"/>, emitted when the sim takes it and
    /// drawn from its start, which a re-walk puts up to the lead before it.
    /// </summary>
    internal sealed class HeroWalk
    {
        /// <summary>The ticks of path kept, about half a second: the furthest back a heard input re-walks.</summary>
        public const int History = 64;
        /// <summary>A velocity change under this (m/s) emits no move: a target's aim, renormalised each tick, wobbles in its last bits.</summary>
        const double Same = 1e-6;
        /// <summary>A damaging dash breaks through this many structures in one move at most.</summary>
        const int Breaks = 8;

        /// <summary>A dash's motion: its run index, span and velocity.</summary>
        struct DashSpan
        {
            public int Index;
            public double Start, End, Vx, Vy;
            public bool Damaging;
        }

        static readonly DashSpan NoDash = new DashSpan { Index = -1, Start = double.NegativeInfinity, End = double.NegativeInfinity };

        readonly HeroState state;
        readonly double speed;
        // The latest dash and the one before it (which a re-walk across both still needs).
        DashSpan dash = NoDash, dashBefore = NoDash;
        // Where the latest dash to finish ended: exact, where the kept ticks would only interpolate.
        int endedDash = -1;
        double endedX, endedY;
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
            Radius = rules.RadiusM;
            Chord(0, 0, 0, 0, start, start);
        }

        public bool Targeting => targeting;

        /// <summary>The hero's body radius: what structures stop.</summary>
        public double Radius { get; }

        /// <summary>The earliest time an input taken at <paramref name="now"/> can re-walk from: the oldest kept tick, never before a hold.</summary>
        public double Earliest(double now)
        {
            double oldest = pathCount > 0 ? PathT(pathCount - 1) : now;
            return Math.Min(now, Math.Max(floor, oldest));
        }

        /// <summary>Where dash <paramref name="index"/> ended, once the walk has passed its end (false before, or if a newer one cut it short).</summary>
        public bool DashEnded(int index, out double x, out double y)
        {
            x = endedX;
            y = endedY;
            return endedDash == index && index >= 0;
        }

        /// <summary>
        /// A dash (run index <paramref name="index"/>) from <paramref name="start"/> (up to the history back) to
        /// <paramref name="end"/> at velocity (<paramref name="vx"/>, <paramref name="vy"/>), walked again from its
        /// start as a heard input is.
        /// </summary>
        public void Dash(Simulation sim, int index, double start, double end, double vx, double vy, bool damaging)
        {
            dashBefore = dash;
            dash = new DashSpan { Index = index, Start = start, End = end, Vx = vx, Vy = vy, Damaging = damaging };
            Rewalk(sim, start);
        }

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
        internal void Resume(double elapsed)
        {
            dash.Start += elapsed; dash.End += elapsed;
            dashBefore.Start += elapsed; dashBefore.End += elapsed;
        }

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
            Advance(sim, now, x, y, dt, out double nx, out double ny);
            Chord(x, y, nx, ny, now, now + dt);
            Emit(sim, now, now, x, y, (nx - x) / dt, (ny - y) / dt, force);
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
                    Advance(sim, t, x, y, span, out double nx, out double ny);
                    Emit(sim, now, t, x, y, (nx - x) / span, (ny - y) / span, first);
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

        /// <summary>
        /// Where the hero's input takes it from (<paramref name="x"/>, <paramref name="y"/>) over the
        /// <paramref name="span"/> seconds from <paramref name="t"/>: dashing for the parts of them in a dash,
        /// walking the rest, each part stopped by the structures in its way.
        /// </summary>
        void Advance(Simulation sim, double t, double x, double y, double span, out double nx, out double ny)
        {
            nx = x;
            ny = y;
            if (sim.State.Held) return;
            double end = t + span;
            while (t < end - 1e-12)
            {
                bool dashing = DashAt(t, out var d);
                double to = Math.Min(end, NextChange(t));
                if (dashing) DashMove(sim, d, t, to - t, ref nx, ref ny);
                else WalkPart(sim, t, to - t, ref nx, ref ny);
                t = to;
            }
        }

        /// <summary>The dash moving the hero at <paramref name="t"/>, if any: the latest, else the one before until the latest starts.</summary>
        bool DashAt(double t, out DashSpan d)
        {
            if (dash.Start <= t + 1e-12 && t < dash.End - 1e-12)
            {
                d = dash;
                return true;
            }
            if (dashBefore.Start <= t + 1e-12 && t < dashBefore.End - 1e-12 && t < dash.Start - 1e-12)
            {
                d = dashBefore;
                return true;
            }
            d = NoDash;
            return false;
        }

        /// <summary>The first dash start or end after <paramref name="t"/>.</summary>
        double NextChange(double t)
        {
            double next = double.PositiveInfinity;
            if (dash.Start > t + 1e-12) next = Math.Min(next, dash.Start);
            if (dash.End > t + 1e-12) next = Math.Min(next, dash.End);
            if (dashBefore.Start > t + 1e-12) next = Math.Min(next, dashBefore.Start);
            if (dashBefore.End > t + 1e-12) next = Math.Min(next, dashBefore.End);
            return next;
        }

        void WalkPart(Simulation sim, double t, double span, ref double x, ref double y)
        {
            Keys(out int across, out int up);
            double step = speed * (1 + sim.State.Build.HeroBonus(StatFamily.Stride)) * span, dx = 0, dy = 0;
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
            Move(sim, t, ref x, ref y, dx, dy, NoDash);
        }

        void DashMove(Simulation sim, in DashSpan d, double t, double span, ref double x, ref double y)
        {
            double dx = d.Vx * span, dy = d.Vy * span, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) return;
            state.FacingX = dx / len;
            state.FacingY = dy / len;
            double fromX = x, fromY = y;
            Move(sim, t, ref x, ref y, dx, dy, d);
            if (targeting && HitShape.DistanceToSegment(targetX, targetY, fromX, fromY, x, y) <= Radius + 1e-9) targeting = false;
            if (Math.Abs(t + span - d.End) < 1e-12)
            {
                endedDash = d.Index;
                endedX = x;
                endedY = y;
            }
        }

        /// <summary>Moves the hero by (dx, dy), stopped and slid by the structures; a damaging dash breaks through what it can.</summary>
        void Move(Simulation sim, double t, ref double x, ref double y, double dx, double dy, in DashSpan d)
        {
            var structures = sim.Structures;
            if (structures == null)
            {
                x += dx;
                y += dy;
                return;
            }
            for (int attempt = 0; ; attempt++)
            {
                structures.Move(x, y, Radius, dx, dy, out double nx, out double ny, out int blocker);
                if (blocker >= 0 && d.Damaging && attempt < Breaks && sim.Dashes.BreakThrough(sim, d.Index, blocker, t, nx, ny)) continue;
                x = nx;
                y = ny;
                return;
            }
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

        void Emit(Simulation sim, double t, double start, double x, double y, double vx, double vy, bool force)
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
