using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The free hero's rules (<see cref="HeroMovement.Free"/>, docs/design/game-modes.md): it walks continuously
    /// rather than hopping tiles.
    ///
    /// - <b>Keys</b>: the held arrows steer, eight ways, diagonals as fast as the rest; opposite keys cancel. An arrow
    ///   press drops the pointer's target.
    /// - <b>Pointer</b>: a target (<see cref="Target"/>) walks the hero straight to it; held, the client sends the
    ///   cursor's point as it moves, so the hero follows it. The held arrows win over a target while any is down.
    /// - <b>Speed</b>: one tile per stride step, the grid hero's pace with a key held, so it follows the tempo as the
    ///   enemies' speeds do (unity-game.md decision 16).
    /// - <b>Walls</b>: the hero is a box <see cref="HalfWidthM"/> either side of its centre that never enters a
    ///   blocked tile or leaves the arena, moved one axis then the other, so it slides along what blocks it. There
    ///   is no path finding: a target behind a wall leaves the hero pressed against it.
    /// - <b>Heard time</b>: as the grid hero's presses, a key change or target heard at h, taken at the sim's time F,
    ///   moves the hero from h: its path since h (up to <see cref="History"/> ticks back, and never before a freeze
    ///   or a fall) is walked again with the new input. So the client shows the turn on the frame the key went down;
    ///   the sim's systems in (h, F] already saw the old path, which differs by at most the lead's walk.
    /// - Frozen or fallen, it stands; a level-up's cards let go of its keys and target (<see cref="LetGo"/>).
    /// - <b>Dashes</b> (`--dash`, <see cref="Sim.Dash"/>): from a dash's start to its end the hero moves at the dash's
    ///   velocity instead of walking, by the same box rules, so a wall stops it; a dash heard before the sim took it is
    ///   walked again from its start as a heard input is.
    ///
    /// <see cref="HeroState"/> holds the chord of the tick ahead: from where it is at the tick's start to where it
    /// will be at its end, so <see cref="HeroState.PositionAt"/> answers for any time in the tick. Each change of
    /// velocity is a <see cref="SimEventKind.HeroGlide"/> event, emitted when the sim takes it and drawn from its
    /// start, which a heard input's re-walk puts up to the lead before it.
    /// </summary>
    internal sealed class FreeHero : IHeroControl
    {
        /// <summary>Half the hero's width against walls (m): a tile is 1.5 m, so it fits a one-tile gap with room.</summary>
        public const double HalfWidthM = 0.45;
        /// <summary>The ticks of path kept, about half a second: the furthest back a heard input re-walks.</summary>
        public const int History = 64;
        /// <summary>A velocity change under this (m/s) emits no glide: a target's aim, renormalised each tick, wobbles in its last bits.</summary>
        const double Same = 1e-6;
        /// <summary>How far short of a wall's face the box stops, so it never counts as inside the tile.</summary>
        const double Gap = 1e-7;

        readonly HeroState state;
        readonly ArenaGrid arena;
        readonly StrideGrid strides;
        // The held arrows, by Direction.
        readonly bool[] held = new bool[4];
        bool targeting;
        double targetX, targetY;
        // The path at the latest ticks' ends, newest at pathHead.
        readonly double[] pathT = new double[History], pathX = new double[History], pathY = new double[History];
        int pathHead = -1, pathCount;
        // The latest glide's velocity.
        double glideX, glideY;
        bool glided;
        // The earliest time an input may re-walk from: the latest tick the hero was frozen or fallen.
        double floor = double.NegativeInfinity;
        // The latest dash: its span and velocity.
        double dashStart = double.NegativeInfinity, dashEnd = double.NegativeInfinity, dashX, dashY;

        public FreeHero(HeroState state, ArenaGrid arena, StrideGrid strides)
        {
            this.state = state;
            this.arena = arena;
            this.strides = strides;
            double x = arena.CentreX(arena.HeroStart.X), y = arena.CentreY(arena.HeroStart.Y);
            Chord(x, y, x, y, double.NegativeInfinity, double.NegativeInfinity);
        }

        /// <summary>The hero's walking speed (m/s): a tile per stride step.</summary>
        public double Speed => arena.TileM / strides.Period;

        public void Press(Simulation sim, Key key, double heard, bool forCards)
        {
            if (forCards || key > Key.Right) return;
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

        /// <summary>A target given while frozen or fallen is dropped: no click on a card or the pause menu walks the hero after.</summary>
        public void Target(Simulation sim, double x, double y, double heard)
        {
            if (sim.State.Frozen || state.Fallen) return;
            targeting = true;
            targetX = x;
            targetY = y;
            Rewalk(sim, heard);
        }

        public void LetGo()
        {
            Array.Clear(held, 0, held.Length);
            targeting = false;
        }

        public void Step(Simulation sim, int index, double t, double period) { }

        /// <summary>
        /// A dash from <paramref name="start"/> (up to the sim's time back, its press's heard time) to <paramref name="end"/>
        /// at velocity (<paramref name="vx"/>, <paramref name="vy"/>), walked again from its start as a heard input is.
        /// </summary>
        internal void Dash(Simulation sim, double start, double end, double vx, double vy)
        {
            dashStart = start;
            dashEnd = end;
            dashX = vx;
            dashY = vy;
            Rewalk(sim, start);
        }

        /// <summary>
        /// The way a dash from (<paramref name="x"/>, <paramref name="y"/>) goes, a unit vector: the held arrows', else
        /// toward the pointer's target, else the walk's, else the facing.
        /// </summary>
        internal void DashDirection(double x, double y, out double dx, out double dy)
        {
            int across = (held[(int)Direction.Right] ? 1 : 0) - (held[(int)Direction.Left] ? 1 : 0);
            int up = (held[(int)Direction.Up] ? 1 : 0) - (held[(int)Direction.Down] ? 1 : 0);
            double ex = across, ey = up;
            if (across == 0 && up == 0 && targeting)
            {
                ex = targetX - x;
                ey = targetY - y;
            }
            if (ex * ex + ey * ey < 1e-12)
            {
                ex = glideX;
                ey = glideY;
            }
            double d = Math.Sqrt(ex * ex + ey * ey);
            if (d < 1e-9)
            {
                Sword.Facing(state.Facing, out dx, out dy);
                return;
            }
            dx = ex / d;
            dy = ey / d;
        }

        /// <summary>The four-way facing of a travel along (<paramref name="dx"/>, <paramref name="dy"/>), the larger axis's.</summary>
        internal static Direction FacingOf(double dx, double dy) =>
            Math.Abs(dx) >= Math.Abs(dy) ? dx > 0 ? Direction.Right : Direction.Left : dy > 0 ? Direction.Up : Direction.Down;

        public void Tick(Simulation sim, double now)
        {
            if (sim.State.Frozen || state.Fallen) floor = now;
            Remember(now, state.X, state.Y);
            Walk(sim, now, state.X, state.Y, false);
        }

        /// <summary>Plans the tick ahead from (<paramref name="x"/>, <paramref name="y"/>) at the sim's time <paramref name="now"/>.</summary>
        void Walk(Simulation sim, double now, double x, double y, bool force)
        {
            double dt = Simulation.TickSeconds;
            Advance(sim, now, x, y, dt, out double nx, out double ny);
            Chord(x, y, nx, ny, now, now + dt);
            Glide(sim, now, now, x, y, (nx - x) / dt, (ny - y) / dt, force);
        }

        /// <summary>
        /// An input heard at <paramref name="heard"/> changed what the hero means to do: its path from then to the
        /// sim's time is walked again (see the class's Heard time), then the tick ahead planned.
        /// </summary>
        void Rewalk(Simulation sim, double heard)
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
                    Glide(sim, now, t, x, y, (nx - x) / span, (ny - y) / span, first);
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
        /// Where the hero was at <paramref name="t"/>: along the tick ahead's chord from its start, else between the
        /// kept tick ends (the path a heard input's re-walk revised), else at the oldest kept.
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

        /// <summary>
        /// Where the hero's input takes it from (<paramref name="x"/>, <paramref name="y"/>) over the <paramref name="span"/>
        /// seconds from <paramref name="t"/>: dashing for the share of them in a dash, walking the rest.
        /// </summary>
        void Advance(Simulation sim, double t, double x, double y, double span, out double nx, out double ny)
        {
            nx = x;
            ny = y;
            if (sim.State.Frozen || state.Fallen) return;
            int across = (held[(int)Direction.Right] ? 1 : 0) - (held[(int)Direction.Left] ? 1 : 0);
            int up = (held[(int)Direction.Up] ? 1 : 0) - (held[(int)Direction.Down] ? 1 : 0);
            double dashing = Math.Max(0, Math.Min(t + span, dashEnd) - Math.Max(t, dashStart));
            double step = Speed * (span - dashing), dx = 0, dy = 0;
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
            if (dashing > 0)
            {
                state.Facing = FacingOf(dashX, dashY);
                dx += dashX * dashing;
                dy += dashY * dashing;
            }
            else if (dx != 0 || dy != 0) state.Facing = FacingOf(dx, dy);
            if (dx == 0 && dy == 0) return;
            nx = Slide(x, y, dx, true);
            ny = Slide(y, nx, dy, false);
        }

        /// <summary>
        /// The box's centre <paramref name="p"/> moved <paramref name="d"/> along one axis (x if <paramref name="alongX"/>),
        /// <paramref name="q"/> its centre on the other, stopping at the first blocked tile its leading edge would enter.
        /// </summary>
        double Slide(double p, double q, double d, bool alongX)
        {
            if (d == 0) return p;
            double tile = arena.TileM, origin = alongX ? arena.OriginX : arena.OriginY, across = alongX ? arena.OriginY : arena.OriginX;
            int low = Cell(q - HalfWidthM + Gap, across), high = Cell(q + HalfWidthM - Gap, across);
            double edge = p + (d > 0 ? HalfWidthM : -HalfWidthM);
            int from = Cell(edge, origin), to = Cell(edge + d, origin), dir = d > 0 ? 1 : -1;
            for (int c = from + dir; dir > 0 ? c <= to : c >= to; c += dir)
                for (int r = low; r <= high; r++)
                {
                    if (alongX ? arena.IsOpen(c, r) : arena.IsOpen(r, c)) continue;
                    // Stop against the blocked cell's near face.
                    double face = origin + (d > 0 ? c : c + 1) * tile;
                    return d > 0 ? Math.Max(p, face - HalfWidthM - Gap) : Math.Min(p, face + HalfWidthM + Gap);
                }
            return p + d;
        }

        int Cell(double v, double origin) => (int)Math.Floor((v - origin) / arena.TileM);

        void Chord(double fromX, double fromY, double toX, double toY, double start, double end)
        {
            state.FromX = fromX;
            state.FromY = fromY;
            state.X = toX;
            state.Y = toY;
            state.MoveStart = start;
            state.MoveEnd = end;
            state.TileX = Cell(toX, arena.OriginX);
            state.TileY = Cell(toY, arena.OriginY);
        }

        void Glide(Simulation sim, double t, double start, double x, double y, double vx, double vy, bool force)
        {
            if (!force && glided && Math.Abs(vx - glideX) < Same && Math.Abs(vy - glideY) < Same) return;
            glided = true;
            glideX = vx;
            glideY = vy;
            sim.Emit(SimEvent.HeroGlide(t, start, x, y, vx, vy, state.Facing));
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
