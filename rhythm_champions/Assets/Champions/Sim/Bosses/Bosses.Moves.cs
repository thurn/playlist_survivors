using System;

namespace Champions.Sim
{
    /// <summary>
    /// The bosses' moves (design.md, "Fat Ogre" and "Dragon"; <see cref="BossMoveRules"/>): each aims at the hero when
    /// it begins and lays its marks, which land on their beats.
    ///
    /// - <b>Smash</b>: a line from the Ogre's front, <see cref="BossMoveRules.LengthM"/> long; the club lands along it.
    /// - <b>Stomp</b>, <b>tail sweep</b>: a disc round the boss, landing on a downbeat.
    /// - <b>Charge</b>: a line through the hero, <see cref="BossMoveRules.LengthM"/> from the Ogre; on its beat it runs
    ///   the line (the hero on it then is hit).
    /// - <b>Boulders</b>: a circle on the hero and the rest round it, landing together.
    /// - <b>Fire breath</b>: a cone from the dragon's mouth that burns for <see cref="BossMoveRules.BurnBeats"/>: the hero in
    ///   it on its beat is hit, else the first time it walks in, and once more at the burn's end after a hit on the beat
    ///   (a dodge that stays in the fire). In phase two the cone sweeps <see cref="BossMoveRules.Sweep"/> across the burn,
    ///   its telegraph covering the whole sweep.
    /// - <b>Dive</b>: the hero's spot is marked as the dragon takes off; it flies there and lands on the mark's beat.
    /// - <b>Fireballs</b>: on each of <see cref="BossMoveRules.Count"/> beats a circle is marked where the hero is then,
    ///   each landing its telegraph's beats later.
    ///
    /// Every mark breaks the structures it covers as it lands. An attack reaches the hero when the hero is inside its shape
    /// on its beat, or is dashing then from a start inside it
    /// (a dash pressed early still dashes through it). In phase two a move with <see cref="BossMoveRules.ChainTwo"/> is
    /// followed by that move from the next beat (never chaining further).
    /// </summary>
    public sealed partial class Bosses
    {
        BossMoveRules move;
        bool chained;
        int turn, nextMark;
        double aimX, aimY;
        // The move's last landing, its telegraph, and when the move is done (a charge's run, a burn's end).
        double lastLand, doneAt;
        Telegraph lastTelegraph;
        // The fireballs still to mark, the next one's number, and the first's telegraph they count beats from.
        int fireLeft, fireNext;
        Telegraph fireFirst;
        // A charge's run or a dive's flight (to its mark's beat, as known then).
        bool moving, flight;
        double fromX, fromY, toX, toY, moveStart, moveEnd;
        Telegraph flightTelegraph;
        // A breath's burn: its span, the cone's apex and middle angle, its sweep, and the hits it made.
        bool burning, burnAtLand;
        int burnHits;
        double burnFrom, burnUntil, apexX, apexY, burnAngle, burnSweep;

        double BodyRadius => cast.Cast[Type.Cast].RadiusM;

        /// <summary>
        /// Begins the move in turn once the hero is in its reach, walking toward it meanwhile; after a bar out of reach, the
        /// next in turn that reaches.
        /// </summary>
        void Choose(double end)
        {
            sim.HeroWas(end, out double hx, out double hy);
            double d = Math.Sqrt((hx - x) * (hx - x) + (hy - y) * (hy - y));
            var moves = Type.Moves;
            bool patient = end < nextMoveAt + sim.Beats.Rules.BeatsPerBar * BeatS() - 1e-9;
            for (int k = 0; k < (patient ? 1 : moves.Length); k++)
            {
                var m = moves[(turn + k) % moves.Length];
                if (d > m.MaxM + 1e-9) continue;
                turn = (turn + k + 1) % moves.Length;
                Begin(m, end, end, false);
                return;
            }
        }

        /// <summary>
        /// Begins <paramref name="m"/> at <paramref name="t"/>, aimed at the hero then: its marks, their telegraphs planned
        /// from <paramref name="from"/> (now, or a chained move's next beat).
        /// </summary>
        void Begin(BossMoveRules m, double t, double from, bool chain)
        {
            move = m;
            chained = chain;
            Stage = BossStage.Acting;
            doneAt = t;
            sim.HeroWas(t, out double hx, out double hy);
            double dx = hx - x, dy = hy - y, d = Math.Sqrt(dx * dx + dy * dy);
            aimX = d > 1e-9 ? dx / d : SimMath.Cos(Facing);
            aimY = d > 1e-9 ? dy / d : SimMath.Sin(Facing);
            Facing = SimMath.Atan2(aimY, aimX);
            int beats = m.Beats(Phase);
            double r = BodyRadius;
            switch (m.Move)
            {
                case BossMove.Wave:
                case BossMove.Wall:
                {
                    var tel = Plan(from, beats);
                    // Locked to the hero's location when warned. Walking cannot outrun the front;
                    // a dash crosses it using the same physical invulnerability as any other attack.
                    var shape = m.Move == BossMove.Wave
                        ? HitShape.Ring(hx, hy, 0, 0.8)
                        : HitShape.Path(hx - m.RadiusM, hy - (tel.Land - t) * maxWalkSpeed - 2, hx + m.RadiusM, hy - (tel.Land - t) * maxWalkSpeed - 2, 0.6);
                    int spacingBeats = (int)Math.Ceiling(Math.Max(1.1, sim.Dashes.Rules.CooldownS) / BeatS());
                    double spacing = spacingBeats * BeatS();
                    for (int k = 0; k < m.Count; k++)
                        Mark(k, new Telegraph(tel.Start, tel.Land + k * spacing, tel.StartBeat, tel.OnBeat ? tel.LandBeat + k * spacingBeats : -1, true), shape);
                    break;
                }
                case BossMove.Fans:
                {
                    var tel = Plan(from, beats);
                    // Three narrow baton lanes with walking-width gaps, alternating each turn.
                    for (int k = -1; k <= 1; k++)
                    {
                        double a = Facing + k * 0.65 + (turn % 2 == 0 ? 0.2 : -0.2);
                        Mark(k + 1, tel, HitShape.Cone(x, y, SimMath.Cos(a), SimMath.Sin(a), m.RangeM, m.HalfArc));
                    }
                    break;
                }
                case BossMove.Smash:
                {
                    double near = r * 0.5, far = r + m.LengthM;
                    Mark(0, Plan(from, beats), HitShape.Path(x + aimX * near, y + aimY * near, x + aimX * far, y + aimY * far, m.WidthM));
                    break;
                }
                case BossMove.Stomp:
                case BossMove.Tail:
                    Mark(0, Plan(from, beats, true), HitShape.Circle(x, y, m.RadiusM));
                    break;
                case BossMove.Charge:
                    Mark(0, Plan(from, beats), HitShape.Path(x, y, x + aimX * m.LengthM, y + aimY * m.LengthM, r));
                    break;
                case BossMove.Boulders:
                {
                    var tel = Plan(from, beats);
                    Mark(0, tel, HitShape.Circle(hx, hy, m.RadiusM));
                    double turnFrom = sim.Random.NextDouble() * 2 * Math.PI;
                    for (int k = 1; k < m.Count; k++)
                    {
                        double a = turnFrom + 2 * Math.PI * (k - 1) / (m.Count - 1);
                        Mark(k, tel, HitShape.Circle(hx + SimMath.Cos(a) * m.SpreadM, hy + SimMath.Sin(a) * m.SpreadM, m.RadiusM));
                    }
                    break;
                }
                case BossMove.Breath:
                    apexX = x + aimX * r * 0.8;
                    apexY = y + aimY * r * 0.8;
                    burnAngle = Facing;
                    burnSweep = m.Sweep[Math.Min(Phase, 2) - 1];
                    Mark(0, Plan(from, beats), HitShape.Cone(apexX, apexY, aimX, aimY, m.RangeM, m.HalfArc + burnSweep / 2));
                    break;
                case BossMove.Dive:
                {
                    var tel = Plan(from, beats);
                    Mark(0, tel, HitShape.Circle(hx, hy, m.RadiusM));
                    moving = flight = true;
                    flightTelegraph = tel;
                    fromX = x;
                    fromY = y;
                    toX = hx;
                    toY = hy;
                    moveStart = tel.Start;
                    break;
                }
                case BossMove.Fireballs:
                    fireFirst = Plan(from, beats);
                    Mark(0, fireFirst, HitShape.Circle(hx, hy, m.RadiusM));
                    fireNext = 1;
                    fireLeft = m.Count - 1;
                    break;
            }
        }

        void Mark(int part, in Telegraph telegraph, in HitShape shape)
        {
            var mark = new BossMark { Id = nextMark++, Move = move.Move, Part = part, Telegraph = telegraph, Shape = shape };
            marks.Add(mark);
            sim.Emit(SimEvent.BossTelegraph(mark));
        }

        /// <summary>The move in hand to <paramref name="end"/>: fireballs marked on their beats, marks landing, its run, flight and burn.</summary>
        void Act(double end)
        {
            while (fireLeft > 0)
            {
                double at = !sim.Dashes.Rules.LegacyForms
                    ? fireFirst.Start + fireNext * BeatOf(fireFirst)
                    : fireFirst.OnBeat ? sim.Beats.PredictBeat(fireFirst.StartBeat + fireNext) : fireFirst.Start + fireNext * beatlessBeatS;
                if (double.IsNaN(at) || at > end + 1e-9) break;
                sim.HeroWas(at, out double hx, out double hy);
                Mark(fireNext++, Plan(at, move.Beats(Phase)), HitShape.Circle(hx, hy, move.RadiusM));
                fireLeft--;
            }
            int kept = 0;
            for (int i = 0; i < marks.Count; i++)
            {
                var mark = marks[i];
                double land = sim.Telegraphs.LandTime(mark.Telegraph);
                if (!sim.Dashes.Rules.LegacyForms && IsWave(mark.Move))
                {
                    if (land <= end + 1e-9)
                    {
                        if (end - Simulation.TickSeconds < land + 1e-9)
                            sim.Emit(SimEvent.BossMove(land, mark, false, x, y, x, y, move.DurationS, 0));
                        double at = Math.Min(end, land + move.DurationS);
                        var shape = WaveShape(mark, at);
                        if (ReachesHero(shape, at)) Hurt(at, at);
                        sim.StructureField?.Shatter(sim, shape, at, BreakCause.Boss);
                    }
                    if (end < land + move.DurationS - 1e-9) marks[kept++] = mark;
                    else { lastLand = land; lastTelegraph = mark.Telegraph; doneAt = land + move.DurationS; }
                }
                else if (land <= end + 1e-9) Land(mark, land);
                else marks[kept++] = mark;
            }
            while (marks.Count > kept) marks.RemoveLast();
            if (moving)
            {
                double until = flight ? sim.Telegraphs.LandTime(flightTelegraph) : moveEnd;
                double u = until > moveStart ? Math.Max(0, Math.Min(1, (end - moveStart) / (until - moveStart))) : 1;
                double f = flight ? u : Horde.Ease(u);
                x = fromX + (toX - fromX) * f;
                y = fromY + (toY - fromY) * f;
                if (u >= 1) moving = false;
            }
            if (burning) Burn(end);
            if (marks.Count == 0 && fireLeft == 0 && !moving && !burning) Finish();
        }

        /// <summary><paramref name="mark"/> lands at <paramref name="land"/>, its beat.</summary>
        void Land(in BossMark mark, double land)
        {
            lastLand = land;
            lastTelegraph = mark.Telegraph;
            doneAt = Math.Max(doneAt, land);
            double endX = x, endY = y, duration = 0, sweep = 0;
            var shape = mark.Shape;
            switch (mark.Move)
            {
                case BossMove.Charge:
                    moving = true;
                    flight = false;
                    fromX = x;
                    fromY = y;
                    toX = endX = shape.X2;
                    toY = endY = shape.Y2;
                    moveStart = land;
                    moveEnd = land + move.DurationS;
                    duration = move.DurationS;
                    doneAt = Math.Max(doneAt, moveEnd);
                    break;
                case BossMove.Dive:
                    moving = false;
                    x = endX = shape.X;
                    y = endY = shape.Y;
                    break;
                case BossMove.Breath:
                    burning = true;
                    burnHits = 0;
                    burnFrom = land;
                    burnUntil = land + move.BurnBeats * BeatOf(mark.Telegraph);
                    duration = burnUntil - land;
                    sweep = burnSweep;
                    doneAt = Math.Max(doneAt, burnUntil);
                    shape = BurnShape(land);
                    break;
            }
            bool hit = ReachesHero(shape, land);
            if (hit) Hurt(land, land);
            if (mark.Move == BossMove.Breath)
            {
                burnAtLand = hit;
                burnHits = hit ? 1 : 0;
            }
            sim.Emit(SimEvent.BossMove(land, mark, false, x, y, endX, endY, duration, sweep));
            sim.StructureField?.Shatter(sim, mark.Shape, land, BreakCause.Boss);
        }

        /// <summary>The breath's cone at <paramref name="t"/> in its burn: still, or swept from one side to the other.</summary>
        HitShape BurnShape(double t)
        {
            double u = burnUntil > burnFrom ? Math.Max(0, Math.Min(1, (t - burnFrom) / (burnUntil - burnFrom))) : 0;
            double a = burnAngle - burnSweep / 2 + burnSweep * u;
            return HitShape.Cone(apexX, apexY, SimMath.Cos(a), SimMath.Sin(a), move.RangeM, move.HalfArc);
        }

        /// <summary>The burn to <paramref name="end"/>: the first time the hero is in the fire, and once more at its end after a hit on its beat.</summary>
        void Burn(double end)
        {
            double t = Math.Min(end, burnUntil);
            bool last = end >= burnUntil - 1e-9;
            if (t > burnFrom + 1e-9 && (burnHits == 0 || (last && burnAtLand && burnHits == 1)) && ReachesHero(BurnShape(t), t))
            {
                Hurt(t, burnHits == 0 ? burnFrom : double.NaN);
                burnHits++;
            }
            if (last) burning = false;
        }

        /// <summary>
        /// The move is done: in phase two its chained move follows, its telegraph from the beat after the landing (so a dash
        /// cooled down since the landing's can meet it); else the boss rests and walks.
        /// </summary>
        void Finish()
        {
            if (sim.Dashes.Rules.LegacyForms && Phase == 2 && !chained && move.ChainTwo != BossMove.None)
            {
                double now = sim.Now;
                Begin(Type.Find(move.ChainTwo), now, lastTelegraph.OnBeat ? lastLand + 1e-6 : Math.Max(now, lastLand + beatlessBeatS), true);
                return;
            }
            move = null;
            Stage = BossStage.Walking;
            // Resting whole beats from the landing: half a beat short, so the next telegraph starts on the beat itself.
            double beatS = BeatOf(lastTelegraph);
            nextMoveAt = doneAt + Math.Max(sim.Dashes.Rules.LegacyForms ? 0 : sim.Dashes.Rules.CooldownS, (Type.Rest(Phase) - (lastTelegraph.OnBeat ? 0.5 : 0)) * beatS);
        }

        /// <summary>A beat's length by <paramref name="telegraph"/>'s own beats, or the beatless fallback.</summary>
        double BeatOf(in Telegraph telegraph) =>
            telegraph.OnBeat && telegraph.Beats > 0 ? (telegraph.Land - telegraph.Start) / telegraph.Beats : beatlessBeatS;

        /// <summary>Moves with a continuously traveling damage front.</summary>
        public static bool IsWave(BossMove move) => move == BossMove.Wave || move == BossMove.Wall;

        /// <summary>The traveling front at song time t; shared by collision and presentation.</summary>
        public HitShape WaveShape(in BossMark mark, double t)
        {
            var shape = mark.Shape;
            double elapsed = Math.Max(0, t - sim.Telegraphs.LandTime(mark.Telegraph));
            if (mark.Move == BossMove.Wave)
            {
                shape.Radius = 0.8 + elapsed * move.RadiusM / move.DurationS;
                shape.Inner = Math.Max(0, shape.Radius - 1.2);
            }
            else
            {
                double warning = mark.Telegraph.Land - mark.Telegraph.Start;
                double speed = Math.Max(18, (2 * (warning * maxWalkSpeed + 2) + 2 * maxWalkSpeed * move.DurationS + 4) / move.DurationS);
                shape.Y += elapsed * speed; shape.Y2 += elapsed * speed;
            }
            return shape;
        }

        /// <summary>Physical position at impact; archived fixtures also account for the dash start.</summary>
        bool ReachesHero(in HitShape shape, double t)
        {
            sim.HeroWas(t, out double hx, out double hy);
            if (shape.Touches(hx, hy, heroRadius)) return true;
            if (!sim.Dashes.Rules.LegacyForms) return false;
            var hero = sim.State.Hero;
            if (!(hero.DashStart <= t + 1e-9 && t <= hero.IFramesUntil + 1e-9)) return false;
            sim.HeroWas(hero.DashStart, out double sx, out double sy);
            return shape.Touches(sx, sy, heroRadius);
        }

        void Hurt(double t, double beat) =>
            sim.Health.Hurt(sim, new HeroHit { T = t, Damage = move.Damage, Source = HitSource.Boss, Owner = Id, Beat = beat, FromX = x, FromY = y });

        /// <summary>Calls off the move in hand at <paramref name="t"/>: its marks not landed, its fireballs, run, flight and burn.</summary>
        void CallOff(double t)
        {
            for (int i = 0; i < marks.Count; i++) sim.Emit(SimEvent.BossMove(t, marks[i], true, x, y, x, y, 0, 0));
            marks.Clear();
            fireLeft = 0;
            moving = burning = false;
            move = null;
        }
    }
}
