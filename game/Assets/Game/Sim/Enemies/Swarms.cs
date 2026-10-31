using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The minutes' swarms (unity-game.md, Music-Driven Events: "each minute's scheduled swarm event waits for
    /// the first big loudness jump in that minute"; native's engine/survivor_events.rs; schedule.json's
    /// minutes' <c>events</c> and <c>swarms</c>).
    ///
    /// - A minute with events <b>arms</b> as the run clock enters it. The first revealed jump of
    ///   <c>swarms.jumpDb</c> or more that falls in the minute, by the run clock it will sound at (known
    ///   ahead, so a jump early in a minute is waited for even before the minute begins), <b>unlocks</b> it;
    ///   so does the run clock reaching <c>swarms.lateS</c> before the minute's end without one.
    /// - Each event then makes its attempts: the first on the first reliable main beat at or after the jump,
    ///   so the swarm arrives with it (or on the next one after a late unlock), then <c>extraRepeats</c> more
    ///   every <c>delayS</c> of run clock, each on the first reliable main beat once due. An attempt with no
    ///   reliable beat within <see cref="FallbackS"/> comes at a tick end. Of a row's attempts due together
    ///   (after a gap) only the latest comes. An attempt with a <c>chance</c> comes that percent of the time.
    /// - <b>Bats and ghosts</b> come as a flock from beyond the view, 0.72 view widths and heights from its
    ///   centre at a random angle, and fly straight through toward where the hero stood, a role step each main
    ///   beat, over structures. <b>A flower wall</b> rings the hero just as far out with a 60-degree exit, and
    ///   closes to <see cref="WallCloseM"/> over <see cref="WallCloseBeats"/> main beats, stopping short of
    ///   structures. Each ends after its <c>durationS</c> of run clock.
    /// - A swarm takes the stage from a set piece in play. Native draws each event's flock with an angle
    ///   step of pi/500 in a line; the game lays it out as a block so the flock reads as a mass.
    /// </summary>
    internal sealed class Swarms
    {
        /// <summary>An attempt due with no reliable main beat comes at a tick end this long after (as a boss's landing).</summary>
        public const double FallbackS = 4;
        /// <summary>The flower wall's closed radius around where the hero stood, and the main beats it takes to close.</summary>
        public const double WallCloseM = 9;
        public const int WallCloseBeats = 16;
        /// <summary>Where swarms enter: this share of the view's width and height from its centre (native's).</summary>
        public const double EntryShare = 0.72;
        /// <summary>A flock's spacing, in its role's radii.</summary>
        const double FlockSpacing = 1.6;
        const int MaxAttempts = 128;

        struct Attempt
        {
            public int Minute, Row, Ordinal;
            /// <summary>The run clock it is due at; a jump's first attempt is due on the jump's beat instead (<see cref="AtSong"/>).</summary>
            public double ReadyRun, AtSong;
            public double Cause, Known;
            public bool Forced;
        }

        readonly MusicEvents m;
        readonly Schedule schedule;
        readonly bool[] armed;
        readonly Attempt[] attempts = new Attempt[MaxAttempts];
        int attemptCount, entered = -1, jumpFrom;

        public Swarms(MusicEvents m)
        {
            this.m = m;
            schedule = m.Schedule;
            armed = new bool[schedule.Minutes.Length];
        }

        static SetPieceKind KindOf(SwarmKind k) =>
            k == SwarmKind.BatSwarm ? SetPieceKind.BatSwarm : k == SwarmKind.GhostSwarm ? SetPieceKind.GhostSwarm : SetPieceKind.FlowerWall;

        /// <summary>The schedule's first event of <paramref name="kind"/>, now, on the next main beat, whatever its chance.</summary>
        public void Force(SwarmKind kind, double now)
        {
            for (int minute = 0; minute < schedule.Minutes.Length; minute++)
            {
                var events = schedule.Minutes[minute].Events;
                for (int row = 0; row < events.Length; row++)
                {
                    if (events[row].Kind != kind) continue;
                    Add(new Attempt { Minute = minute, Row = row, ReadyRun = m.RunClock, AtSong = now, Cause = now, Known = now, Forced = true });
                    return;
                }
            }
        }

        public void Tick(Simulation sim, double end, double hx, double hy)
        {
            if (m.Natural)
            {
                int now = Math.Min(schedule.Minutes.Length - 1, (int)Math.Floor(m.RunClock / 60 + 1e-9));
                while (entered < now)
                {
                    entered++;
                    armed[entered] = schedule.Minutes[entered].Events.Length > 0;
                }
                Jumps(end);
                for (int minute = Math.Max(0, entered - 1); minute <= entered; minute++)
                    if (armed[minute] && m.RunClock >= (minute + 1) * 60 - schedule.SwarmLateS - 1e-9)
                        Unlock(minute, end, end, m.RunClock, jump: false);
            }
            FireDue(sim, -1, end, FallbackS, hx, hy);
        }

        /// <summary>On a main beat's footplant: the attempts due come, if the beat is reliable.</summary>
        public void Land(Simulation sim, int beat, double t, bool reliable, double hx, double hy)
        {
            if (reliable) FireDue(sim, beat, t, 0, hx, hy);
        }

        /// <summary>The revealed jumps up to the run's next minute, each unlocking the armed minute it sounds in.</summary>
        void Jumps(double now)
        {
            var jumps = m.Log.Revealed(EntryKind.Jump);
            for (; jumpFrom < jumps.Length; jumpFrom++)
            {
                var j = new JumpEntry(jumps[jumpFrom]);
                // The run clock when it sounds, as the world plays on from now.
                double runAt = m.RunClock + (j.T - now);
                int minute = (int)Math.Floor(runAt / 60 + 1e-9);
                if (minute > entered) return; // its minute hasn't armed yet
                if (j.Db < schedule.SwarmJumpDb - 1e-9 || minute < 0 || !armed[minute]) continue;
                Unlock(minute, j.T, m.Log.KnownAt(EntryKind.Jump, jumpFrom), runAt, jump: true);
            }
        }

        void Unlock(int minute, double cause, double known, double runAt, bool jump)
        {
            armed[minute] = false;
            var events = schedule.Minutes[minute].Events;
            for (int row = 0; row < events.Length; row++)
            {
                var e = events[row];
                int repeats = e.DelayS > 0 ? e.ExtraRepeats : 0;
                for (int k = 0; k <= repeats; k++)
                    Add(new Attempt
                    {
                        Minute = minute, Row = row, Ordinal = k, ReadyRun = runAt + k * e.DelayS,
                        AtSong = k == 0 && jump ? cause : double.NaN, Cause = k == 0 ? cause : double.NaN, Known = known,
                    });
            }
        }

        void Add(in Attempt a)
        {
            if (attemptCount < MaxAttempts) attempts[attemptCount++] = a;
        }

        /// <summary>Has attempt <paramref name="a"/> been due for <paramref name="late"/> by <paramref name="t"/>?</summary>
        bool Due(in Attempt a, double t, double late) =>
            double.IsNaN(a.AtSong) ? m.RunClock >= a.ReadyRun + late - 1e-9 : t >= a.AtSong + late - 1e-9;

        /// <summary>Every attempt due <paramref name="late"/> by <paramref name="t"/> comes, on main beat <paramref name="beat"/> (-1: a tick end), but one a row: its latest.</summary>
        void FireDue(Simulation sim, int beat, double t, double late, double hx, double hy)
        {
            for (int k = 0; k < attemptCount;)
            {
                var a = attempts[k];
                if (!Due(a, t, late))
                {
                    k++;
                    continue;
                }
                attempts[k] = attempts[--attemptCount];
                bool superseded = false;
                for (int o = 0; o < attemptCount; o++)
                    superseded |= attempts[o].Minute == a.Minute && attempts[o].Row == a.Row && attempts[o].Ordinal > a.Ordinal && Due(attempts[o], t, late);
                if (!superseded) Fire(sim, a, beat, t, hx, hy);
            }
        }

        void Fire(Simulation sim, in Attempt a, int beat, double t, double hx, double hy)
        {
            var e = schedule.Minutes[a.Minute].Events[a.Row];
            if (!a.Forced && !double.IsNaN(e.Chance) && sim.Random.NextDouble() * 100 >= e.Chance) return;
            m.Pieces.Retire(sim, t, hx, hy);
            int count = m.Horde.MakeRoom(sim, t, e.Count, hx, hy);
            if (count == 0) return;
            var kind = KindOf(e.Kind);
            var g = m.Begin(kind, m.RunClock + e.DurationS);
            if (g == null) return;
            int role = m.Content.RoleIndex(e.Role);
            var s = m.Spawner;
            double cx = hx + 0.5 * (s.ViewMinX + s.ViewMaxX), cy = hy + 0.5 * (s.ViewMinY + s.ViewMaxY);
            double rx = EntryShare * (s.ViewMaxX - s.ViewMinX), ry = EntryShare * (s.ViewMaxY - s.ViewMinY);
            double theta = sim.Random.Range(0, 2 * Math.PI);
            if (kind == SetPieceKind.FlowerWall)
            {
                int closeFrom = beat >= 0 ? beat : m.Horde.Beat;
                for (int k = 0; k < count; k++)
                {
                    // A readable 60-degree exit, at every radius.
                    double angle = theta + Math.PI / 6 + k * (5 * Math.PI / 3) / Math.Max(1, e.Count - 1);
                    double ox = Math.Cos(angle) * rx, oy = Math.Sin(angle) * ry, r = Math.Sqrt(ox * ox + oy * oy);
                    int i = m.Spawn(sim, g, t, role, hx + ox, hy + oy, flier: false);
                    if (i < 0) continue;
                    m.Horde.Scripts.Ring(i, hx, hy, ox / r, oy / r, r, flier: false);
                    m.Horde.Scripts.Close(i, WallCloseM, closeFrom, WallCloseBeats);
                }
            }
            else
            {
                double ex = cx + Math.Cos(theta) * rx, ey = cy + Math.Sin(theta) * ry;
                double dx = hx - ex, dy = hy - ey, d = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
                dx /= d;
                dy /= d;
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                double spacing = FlockSpacing * m.Content.Roles[role].RadiusM;
                for (int k = 0; k < count; k++)
                {
                    double across = (k % cols - (cols - 1) / 2.0) * spacing, back = (k / cols) * spacing;
                    int i = m.Spawn(sim, g, t, role, ex - dy * across - dx * back, ey + dx * across - dy * back, flier: true);
                    if (i >= 0) m.Horde.Scripts.Line(i, dx, dy, 1, flier: true);
                }
            }
            if (g.Count == 0)
            {
                g.Id = -1;
                return;
            }
            sim.Emit(SimEvent.SetPiece(t, kind, g.Count, double.IsNaN(a.Cause) ? t : a.Cause, double.IsNaN(a.Cause) ? t : a.Known));
        }
    }
}
