using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 7, the Bandaged King, act 2: <i>Sandstorm</i> (boss-rush.md, The roster; ps-8z4.19). A mummy king in
    /// his walled tomb court (arenas.json <c>kingsTomb</c>) whose sandstorm comes and goes in waves on the song's bars. While
    /// it blows, what the hero can see shrinks to a radius round them (<see cref="SightM"/>): the King, his telegraphs and
    /// the walls always glow, but the court's props and floor fade into the dark (the client's storm,
    /// <see cref="Storm"/>). Each cycle, counted in the beats its moves land on:
    ///
    /// - <b>The storm</b> (cycle beat 0, a downbeat, to <see cref="StormBeats"/>): it rolls in behind a <b>sand front</b>
    ///   the court's width, sweeping from beyond the far wall through the hero's side faster than any walk, the fight's
    ///   dash-through wave (its bank's signature, a wind swell), rolling in square to the court along the wall behind the
    ///   hero's side. The King glides back into the dark and his <b>lashes</b>
    ///   reach out of it every other beat: a long bandage-whip line from him through where the hero stood three beats
    ///   before, cracking on its beat.
    /// - <b>The calm</b> (the rest of the cycle): the sand settles and the King stalks the hero, a <b>whip combo</b> at a
    ///   hero in reach (three shorter lashes fanned left, right and centre on the beat, the half beat and the next beat) and
    ///   a reaching lash at one who keeps away.
    ///
    /// The first storm teaches the lashes alone: it drifts in with no front, and nothing else lands until it settles.
    /// Below half health the King wakes fully (a four-beat untouchable transition): the storm holds three bars of every
    /// four, a second front rolls through its middle, and every other lash is crossed, two lines either side of the
    /// hero's mark, the second on the half beat. His XP bursts unravel in a line behind him.
    ///
    /// Everything is scheduled on its beat (the song's, or the half-second fallback without a reliable beat): each beat
    /// plans what lands <see cref="LeadBeats"/> beats later, so the storm's waves keep the song's bars. It works without
    /// its arena (the fairness harness, a test sim): the court is then placed as the arena would place it round the
    /// hero's start.
    /// </summary>
    public sealed class BandagedKingModule : BossModule
    {
        public const string ModuleId = "bandagedKing";
        /// <summary>Its arena (arenas.json), and that arena's hero and boss starts: the arena forms with the hero on its start.</summary>
        public const string ArenaId = "kingsTomb";
        public const double HeroStartX = 0, HeroStartY = -8, BossStartX = 0, BossStartY = 7;

        /// <summary>Every move is planned this many beats before it lands (act 2's least telegraph).</summary>
        public const int LeadBeats = 3;
        /// <summary>The cycle's beats (two bars of storm and two of calm; three and one below half health), by phase.</summary>
        public static readonly int[] CycleBeats = { 32, 32 };
        public static readonly int[] StormBeats = { 16, 24 };
        /// <summary>The storm's lashes: from this cycle beat, every this many beats; the second front below half health.</summary>
        public const int FirstLashBeat = 4, LashEveryBeats = 2, SecondFrontBeat = 12;
        /// <summary>The storm's rise and its settling, in beats.</summary>
        public const double RiseBeats = 2, SettleBeats = 2;

        /// <summary>What the hero can see in a full storm: this far round them, fading over the feather (the client's).</summary>
        public const double SightM = 6.5, SightFeatherM = 3;

        /// <summary>Its bounds in the court: no walk takes its body farther than this from the court's centre.</summary>
        public const double InnerM = 11;
        /// <summary>How far it withdraws from the hero as the storm rises, and the gap it closes to in the calm.</summary>
        public const double StormKeepM = 10, CalmKeepM = 2;

        /// <summary>A lash: its length from the King's body (across the court), half-width and damage.</summary>
        public const double LashLengthM = 24, LashWidthM = 0.9, LashDamage = 14;
        /// <summary>A crossed lash: each line's turn either side of the aim at the hero's mark.</summary>
        public const double CrossTurn = 0.22;

        /// <summary>The whip combo: the gap within which it swipes, its lashes' length, turn and damage.</summary>
        public const double ComboGapM = 7, ComboLengthM = 10, ComboTurn = 0.32, ComboDamage = 12;
        /// <summary>Beats after any calm move before the next.</summary>
        public const int CalmRestBeats = 2;

        /// <summary>
        /// The sand front: how far beyond the court's far wall it starts (so it fills where it can be seen and leaves nowhere
        /// behind it), its speed (beyond any walk), half-width, thickness and damage.
        /// </summary>
        public const double FrontBeyondM = 0.6, FrontMps = 22, FrontHalfM = 21, FrontThickM = 0.7, FrontDamage = 16;

        /// <summary>His court's outline from its centre (arenas.json's <c>kingsTomb</c>, counter-clockwise): the front's start and the lashes' reach.</summary>
        public static readonly (double X, double Y)[] Court =
        {
            (-17, -9), (-11, -14), (11, -14), (17, -9), (17, 9), (11, 14), (-11, 14), (-17, 9),
        };

        /// <summary>A cycle beat's move.</summary>
        enum Move : byte { None, Front, Lash, Cross, Calm }

        /// <summary>The court's centre: its arena's, or where the arena would stand with its hero start on the fight's centre.</summary>
        double originX, originY;
        /// <summary>Beats counted (the song's or the fallback's); the count of cycle beat 0 (-1 until aligned); cycles begun; the last downbeat's count.</summary>
        int beats, cycleStart = -1, cycles, lastDown = -1;
        bool align = true;
        double lastBeat = double.NegativeInfinity, lastBeatS = 0.5;
        int calmAttack = -1;
        double calmUntil = double.NegativeInfinity;
        /// <summary>A phase's transition clears the storm from where it stood: from when, and how thick it was.</summary>
        double clearFrom = double.NegativeInfinity, clearLevel;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, BossStartX - HeroStartX, BossStartY - HeroStartY) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;

        /// <summary>The court's centre.</summary>
        public double OriginX => originX;
        public double OriginY => originY;

        int Cycle(BossFight f) => CycleBeats[Math.Min(f.Phase, CycleBeats.Length) - 1];
        int StormOf(BossFight f) => StormBeats[Math.Min(f.Phase, StormBeats.Length) - 1];

        /// <summary>The cycle beat of beat count <paramref name="n"/> (negative before the first cycle).</summary>
        int CycleBeat(BossFight f, int n) => cycleStart < 0 ? -1 : n < cycleStart ? n - cycleStart : (n - cycleStart) % Cycle(f);

        /// <summary>
        /// The storm now, 0 (still air) to 1 (full): rising over the beat either side of cycle beat 0, full through the
        /// storm's beats and settling over <see cref="SettleBeats"/> after; a phase's transition clears it over two beats.
        /// Between beats it moves on by the share of the beat gone, so the client's sight closes and opens smoothly.
        /// </summary>
        public double Storm(BossFight f, double t)
        {
            if (cycleStart < 0)
            {
                double gone = (t - clearFrom) / (SettleBeats * Math.Max(0.2, f.BeatS));
                return gone >= 1 || gone < 0 ? 0 : clearLevel * (1 - gone);
            }
            double u = beats - cycleStart + Math.Max(0, Math.Min(1, (t - lastBeat) / lastBeatS));
            int cycle = Cycle(f), storm = StormOf(f);
            if (u < -RiseBeats / 2) return 0;
            double c = ((u % cycle) + cycle) % cycle;
            if (c >= cycle - RiseBeats / 2) return (c - (cycle - RiseBeats / 2)) / RiseBeats;
            if (c < RiseBeats / 2) return (c + RiseBeats / 2) / RiseBeats;
            if (c <= storm) return 1;
            if (c < storm + SettleBeats) return 1 - (c - storm) / SettleBeats;
            return 0;
        }

        /// <summary>Whether the storm blows at cycle beat <paramref name="c"/> (its front through its last lash).</summary>
        bool Storming(BossFight f, int c) => cycleStart >= 0 && c >= 0 && c < StormOf(f);

        protected internal override void Begin(BossFight f, double t)
        {
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid && arena.Def?.Id == ArenaId)
            {
                originX = arena.OriginX;
                originY = arena.OriginY;
            }
            else
            {
                originX = f.CentreX - HeroStartX;
                originY = f.CentreY - HeroStartY;
            }
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            // He wakes fully: the storm clears through the transition and a fresh cycle's storm rolls in after it.
            clearLevel = Storm(f, t);
            clearFrom = t;
            align = true;
            cycleStart = -1;
            calmAttack = -1;
            calmUntil = until;
        }

        protected internal override void Beat(BossFight f, double t, int beat, bool downbeat) => OnBeat(f, t, downbeat, t);

        protected internal override void Tick(BossFight f, double end)
        {
            if (f.Sim.State.Beat != Reliability.Reliable && end >= lastBeat + f.BeatS - 1e-9)
                OnBeat(f, end, (beats + 1) % f.Sim.Beats.Rules.BeatsPerBar == 0, double.NaN);
            int c = CycleBeat(f, beats);
            ref var b = ref f.Body(0);
            f.HeroAt(end, out double hx, out double hy);
            if (f.Moving(0) || f.Pending(calmAttack)) return;
            // In the storm he stands in the dark where he withdrew to, facing the hero; in the calm he stalks.
            if (cycleStart >= 0 && !Storming(f, c) && end >= calmUntil - 1e-9)
            {
                f.Follow(0, end, CalmKeepM);
                Bound(ref b);
            }
            else f.Face(0, hx, hy);
        }

        /// <summary>
        /// Its beat (<paramref name="from"/>: the beat's time to plan from, NaN for now): the count moves on, and what lands
        /// <see cref="LeadBeats"/> beats later is planned.
        /// </summary>
        void OnBeat(BossFight f, double t, bool downbeat, double from)
        {
            if (t > lastBeat) lastBeatS = Math.Max(0.2, Math.Min(1.5, double.IsInfinity(lastBeat) ? f.BeatS : t - lastBeat));
            lastBeat = t;
            beats++;
            if (downbeat) lastDown = beats;
            if (f.Stage != FightStage.Fighting) return;
            if (align)
            {
                // Cycle beat 0 (the storm's) is the first downbeat the next beat can plan for.
                if (lastDown < 0) return;
                align = false;
                int bar = f.Sim.Beats.Rules.BeatsPerBar, first = beats + 1 + LeadBeats;
                cycleStart = lastDown + (first - lastDown + bar - 1) / bar * bar;
                return;
            }
            int land = beats + LeadBeats, c = CycleBeat(f, land);
            if (c < 0) return;
            if (c == 0)
            {
                cycles++;
                Withdraw(f, from);
            }
            switch (MoveAt(f, c))
            {
                case Move.Front: Front(f, from); break;
                case Move.Lash: Lash(f, from, crossed: false); break;
                case Move.Cross: Lash(f, from, crossed: true); break;
                case Move.Calm: Calm(f, from, c); break;
            }
        }

        /// <summary>Whether this cycle is the lesson (phase 1's first: the lashes alone).</summary>
        bool Lesson(BossFight f) => f.Phase == 1 && cycles <= 1;

        /// <summary>The move landing on cycle beat <paramref name="c"/>.</summary>
        Move MoveAt(BossFight f, int c)
        {
            int storm = StormOf(f);
            if (c < storm)
            {
                if (c == 0) return Lesson(f) ? Move.None : Move.Front;
                if (f.Phase > 1 && c == SecondFrontBeat) return Move.Front;
                if (c < FirstLashBeat || (c - FirstLashBeat) % LashEveryBeats != 0) return Move.None;
                // Below half health every other lash is crossed.
                if (f.Phase > 1 && (c - FirstLashBeat) / LashEveryBeats % 2 == 1) return Move.Cross;
                return Move.Lash;
            }
            return Move.Calm;
        }

        /// <summary>
        /// The sand front: the storm rolls in from behind the King, a front the court's width sweeping from beyond the far
        /// wall through its heart to the hero's wall: nowhere to walk to, a dash through it.
        /// </summary>
        void Front(BossFight f, double from)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            double ax = hx - originX, ay = hy - originY, ad = Math.Sqrt(ax * ax + ay * ay);
            if (ad > 1) { ax /= ad; ay /= ad; }
            else
            {
                // At the heart of the court: from the King's side.
                ref readonly var b = ref f.Bodies[0];
                ax = hx - b.X; ay = hy - b.Y; ad = Math.Sqrt(ax * ax + ay * ay);
                if (ad > 1e-6) { ax /= ad; ay /= ad; } else { ax = 0; ay = -1; }
            }
            // Square to the court: it rolls in along a wall (the top or bottom, or a side), in frame as it fills.
            if (Math.Abs(ax) >= Math.Abs(ay)) { ax = Math.Sign(ax); ay = 0; }
            else { ax = 0; ay = Math.Sign(ay); }
            // From beyond the court's farthest wall behind the hero's side, across it all to the far wall on theirs.
            double back = Support(-ax, -ay) + FrontBeyondM, across = back + Support(ax, ay);
            double px = -ay, py = ax, fx = originX - ax * back, fy = originY - ay * back;
            f.Add(new Hazard
            {
                Attack = f.NextAttack(), Body = 0, Move = BossMove.Stomp, Telegraph = f.Plan(LeadBeats, Landing.Beat, from), Damage = FrontDamage,
                Shape = HitShape.Path(fx - px * FrontHalfM, fy - py * FrontHalfM, fx + px * FrontHalfM, fy + py * FrontHalfM, FrontThickM),
                Vx = ax * FrontMps, Vy = ay * FrontMps, ActiveS = across / FrontMps,
                Flags = HazardFlags.DashWave | HazardFlags.Projectile | HazardFlags.Signature,
            });
        }

        /// <summary>
        /// As the storm rises he glides back into the dark over its front's telegraph: straight away from the hero to
        /// <see cref="StormKeepM"/> from them, or, where his bounds stop him short, to the nearest place on them that far away.
        /// </summary>
        void Withdraw(BossFight f, double from)
        {
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(f.Now, out double hx, out double hy);
            double dx = b.X - hx, dy = b.Y - hy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) { dx = originX - hx; dy = originY - hy; d = Math.Sqrt(dx * dx + dy * dy); }
            if (d < 1e-6) { dx = 0; dy = 1; d = 1; }
            double tx = hx + dx / d * StormKeepM, ty = hy + dy / d * StormKeepM;
            double ox = tx - originX, oy = ty - originY;
            if (ox * ox + oy * oy > InnerM * InnerM)
            {
                // The nearest of his bounds' points far enough from the hero (or the farthest, in a small court).
                double best = double.PositiveInfinity, far = -1;
                for (int k = 0; k < WithdrawSteps; k++)
                {
                    double a = 2 * Math.PI * k / WithdrawSteps, px = originX + InnerM * SimMath.Cos(a), py = originY + InnerM * SimMath.Sin(a);
                    double fromHero = Math.Sqrt((px - hx) * (px - hx) + (py - hy) * (py - hy));
                    double fromHim = (px - b.X) * (px - b.X) + (py - b.Y) * (py - b.Y);
                    if (fromHero >= StormKeepM && fromHim < best) { best = fromHim; tx = px; ty = py; }
                    else if (double.IsInfinity(best) && fromHero > far) { far = fromHero; tx = px; ty = py; }
                }
            }
            var tel = f.Plan(LeadBeats, Landing.Beat, from);
            f.MoveBody(0, tx, ty, f.Now, tel.Land, eased: true);
            f.Face(0, hx, hy);
        }

        const int WithdrawSteps = 48;

        /// <summary>The aim from the King's body at the hero's spot now (toward the court's centre for a hero on top of him).</summary>
        void Aim(BossFight f, out double ax, out double ay)
        {
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(f.Now, out double hx, out double hy);
            double dx = hx - b.X, dy = hy - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1)
            {
                dx = originX - b.X;
                dy = originY - b.Y;
                d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) { dx = 0; dy = -1; d = 1; }
            }
            ax = dx / d;
            ay = dy / d;
        }

        /// <summary>A bandage-whip line from the King's body along the aim turned by <paramref name="turn"/>, stopping at the court's walls.</summary>
        HitShape Whip(BossFight f, double ax, double ay, double turn, double lengthM, double widthM)
        {
            ref readonly var b = ref f.Bodies[0];
            double c = SimMath.Cos(turn), s = SimMath.Sin(turn), wx = c * ax - s * ay, wy = s * ax + c * ay, r = f.TypeOf(0).RadiusM;
            double reach = Math.Max(1, Math.Min(lengthM, Exit(b.X - originX, b.Y - originY, wx, wy) - r));
            return BossKit.SmashLine(b.X, b.Y, wx, wy, r, reach, widthM);
        }

        /// <summary>How far the court reaches from its centre along (<paramref name="ux"/>, <paramref name="uy"/>): its outline's farthest corner that way.</summary>
        static double Support(double ux, double uy)
        {
            double best = double.NegativeInfinity;
            foreach (var (x, y) in Court) best = Math.Max(best, x * ux + y * uy);
            return best;
        }

        /// <summary>How far from (<paramref name="x"/>, <paramref name="y"/>), from the court's centre, a line along (<paramref name="ux"/>, <paramref name="uy"/>) runs before it meets the court's outline.</summary>
        static double Exit(double x, double y, double ux, double uy)
        {
            double t = double.PositiveInfinity;
            for (int i = 0; i < Court.Length; i++)
            {
                var (ax, ay) = Court[i];
                var (bx, by) = Court[(i + 1) % Court.Length];
                // The edge's outward normal (counter-clockwise outline): n.p <= n.a inside.
                double nx = by - ay, ny = ax - bx, toward = nx * ux + ny * uy;
                if (toward <= 1e-12) continue;
                t = Math.Min(t, (nx * (ax - x) + ny * (ay - y)) / toward);
            }
            return Math.Max(0, t);
        }

        /// <summary>A lash out of the dark through the hero's mark; crossed, two either side of it, the second on the half beat.</summary>
        void Lash(BossFight f, double from, bool crossed)
        {
            Aim(f, out double ax, out double ay);
            f.Face(0, f.Bodies[0].X + ax, f.Bodies[0].Y + ay);
            int attack = f.NextAttack();
            if (!crossed)
            {
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Move = BossMove.Smash, Telegraph = f.Plan(LeadBeats, Landing.Beat, from),
                    Shape = Whip(f, ax, ay, 0, LashLengthM, LashWidthM), Damage = LashDamage,
                });
                return;
            }
            for (int k = 0; k < 2; k++)
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = k, Move = BossMove.Boulders,
                    Telegraph = f.Plan(LeadBeats, k == 0 ? Landing.Beat : Landing.HalfBeat, from),
                    Shape = Whip(f, ax, ay, k == 0 ? CrossTurn : -CrossTurn, LashLengthM, LashWidthM), Damage = LashDamage,
                });
        }

        /// <summary>
        /// The calm's move landing on cycle beat <paramref name="c"/>, between moves: the whip combo at a hero in reach, else
        /// a reaching lash; each done before the next storm's front is planned.
        /// </summary>
        void Calm(BossFight f, double from, int c)
        {
            if (f.Pending(calmAttack) || f.Moving(0) || f.Now < calmUntil - 1e-9) return;
            // Every lash lands in the calm, before he withdraws for the next storm (its planning, LeadBeats before it).
            if (c + 1 >= Cycle(f) - LeadBeats) return;
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(f.Now, out double hx, out double hy);
            double gap = Math.Sqrt((hx - b.X) * (hx - b.X) + (hy - b.Y) * (hy - b.Y)) - f.TypeOf(0).RadiusM - f.HeroRadius;
            Aim(f, out double ax, out double ay);
            f.Face(0, b.X + ax, b.Y + ay);
            calmAttack = f.NextAttack();
            var last = default(Telegraph);
            if (gap <= ComboGapM)
            {
                // Left on the beat, right on the half beat, centre on the next beat.
                for (int k = 0; k < 3; k++)
                {
                    last = f.Plan(LeadBeats + k / 2, k % 2 == 0 ? Landing.Beat : Landing.HalfBeat, from);
                    double turn = k == 0 ? ComboTurn : k == 1 ? -ComboTurn : 0;
                    f.Add(new Hazard
                    {
                        Attack = calmAttack, Body = 0, Part = k, Move = BossMove.Fans, Telegraph = last,
                        Shape = Whip(f, ax, ay, turn, ComboLengthM, LashWidthM), Damage = ComboDamage,
                    });
                }
            }
            else
            {
                last = f.Plan(LeadBeats, Landing.Beat, from);
                f.Add(new Hazard
                {
                    Attack = calmAttack, Body = 0, Move = BossMove.Smash, Telegraph = last,
                    Shape = Whip(f, ax, ay, 0, LashLengthM, LashWidthM), Damage = LashDamage,
                });
            }
            calmUntil = last.Land + CalmRestBeats * f.BeatS;
        }

        /// <summary>Holds a walking body within its bounds.</summary>
        void Bound(ref FightBody b)
        {
            double dx = b.X - originX, dy = b.Y - originY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= InnerM) return;
            b.X = originX + dx / d * InnerM;
            b.Y = originY + dy / d * InnerM;
        }

        /// <summary>Each burst unravels in a line behind him, away from the hero, a loosed bandage.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            ref readonly var b = ref f.Lead;
            f.HeroAt(t, out double hx, out double hy);
            double away = SimMath.Atan2(b.Y - hy, b.X - hx) + (burst % 2 == 0 ? 0.12 : -0.12);
            double ux = SimMath.Cos(away), uy = SimMath.Sin(away);
            const int n = 7;
            for (int k = 0; k < n; k++)
            {
                double r = 2.2 + 0.75 * k, sway = 0.35 * Math.Sin(k * 1.3 + burst);
                double x = b.X + ux * r - uy * sway, y = b.Y + uy * r + ux * sway;
                double ox = x - originX, oy = y - originY, od = Math.Sqrt(ox * ox + oy * oy);
                if (od > InnerM + 2) { x = originX + ox / od * (InnerM + 2); y = originY + oy / od * (InnerM + 2); }
                into.Add(new GemSpot(x, y, 1.0 / n));
            }
        }
    }
}
