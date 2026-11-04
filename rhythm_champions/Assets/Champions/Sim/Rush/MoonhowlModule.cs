using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 5, Moonhowl, act 1's finale: <i>The moon cycle</i> (boss-rush.md, The roster; ps-8z4.14). A werewolf in a
    /// ring of standing stones (arenas.json <c>moonGlade</c>) whose fight runs on a moon that waxes and wanes over a cycle of
    /// bars, so the cycle makes the song's tempo visible. Each cycle, counted in the beats its moves land on:
    ///
    /// - <b>The howl</b> (cycle beat 0, a downbeat): it rears and howls, a disc round it; the moon is full. The howl marks
    ///   the turn of the cycle (its bank's signature).
    /// - <b>The frenzy</b> (beats 1 to 8, the signature): under the full moon it lunges on the beat, each lunge a slash line
    ///   from where the last one left it through where the hero stood three beats before, running it on its beat. Some are
    ///   <b>moon lunges</b> (on a downbeat): it bounds right across the glade, its claws throwing a front of moonlight the
    ///   width of the glade that outruns any walk, the fight's dash-through wave.
    /// - <b>The stalk</b> (the rest of the cycle): the moon wanes and waxes again while it stalks the hero (slower than the
    ///   walk), swiping a claw combo at a hero in reach (two cones, the second on the half beat) and leaping onto one that
    ///   keeps away.
    ///
    /// The first cycle teaches the frenzy alone: plain lunges on every beat, no moon lunge, nothing else until it wanes.
    /// Below half health the hunter's moon rises (a four-beat untouchable howl): a shorter cycle, two moon lunges a frenzy
    /// and a third claw in the combo. Its XP bursts fall in a crescent behind it.
    ///
    /// Everything is scheduled on its beat (the song's, or the half-second fallback without a reliable beat): each beat
    /// plans what lands <see cref="LeadBeats"/> beats later, so the moon's phase is the timer of what is coming.
    /// It works without its arena (the fairness harness, a test sim): the glade is then placed as the arena would place it
    /// round the hero's start.
    /// </summary>
    public sealed class MoonhowlModule : BossModule
    {
        public const string ModuleId = "moonhowl";
        /// <summary>Its arena (arenas.json), and that arena's hero and boss starts: the arena forms with the hero on its start.</summary>
        public const string ArenaId = "moonGlade";
        public const double HeroStartX = 0, HeroStartY = -8, BossStartX = 0, BossStartY = 6;

        /// <summary>Every move is planned this many beats before it lands (act 1's least telegraph).</summary>
        public const int LeadBeats = 3;
        /// <summary>The cycle's beats by phase (a whole number of bars), and where its frenzy ends.</summary>
        public static readonly int[] CycleBeats = { 32, 24 };
        public const int FrenzyFrom = 1, FrenzyTo = 8;

        /// <summary>Its bounds in the glade: no move takes its body farther than this from the glade's centre; and the glade's wall (arenas.json).</summary>
        public const double InnerM = 12, WallM = 17;

        /// <summary>The howl: a disc round it.</summary>
        public const double HowlRadiusM = 4.5, HowlDamage = 10;

        /// <summary>A lunge: the slash line's half-width, how far past the hero's mark it runs (its length clamped), its run's time and damage.</summary>
        public const double LungeWidthM = 0.9, LungePastM = 2.5, LungeMinM = 4, LungeMaxM = 10, LungeRunS = 0.22, LungeDamage = 14;

        /// <summary>A moon lunge's front: its speed (beyond any walk), half-width (the glade's), thickness and damage.</summary>
        public const double FrontMps = 20, FrontHalfM = WallM + 0.5, FrontThickM = 0.6, FrontDamage = 16;
        /// <summary>A moon lunge's bound, in beats (its body crosses ahead of its front, done before the next lunge lands).</summary>
        public const double BoundBeats = 0.8;

        /// <summary>The stalk: the gap it keeps, the reach of its combo (who's nearer is swiped at) and its claws, the leap's trigger and disc.</summary>
        public const double KeepM = 0.8, SwipeM = 5.5, ClawRangeM = 5.0, ClawHalfArc = 0.95, ClawDamage = 12;
        public const double LeapM = 9, LeapRadiusM = 2.6, LeapDamage = 16, LeapAirS = 0.45;
        /// <summary>Beats after a leap lands before the next, and the rest after any stalk move.</summary>
        public const int LeapEveryBeats = 8, StalkRestBeats = 1;

        /// <summary>A cycle beat's move.</summary>
        enum Move : byte { None, Howl, Lunge, MoonLunge, Stalk }

        /// <summary>Each phase's frenzy, by cycle beat from <see cref="FrenzyFrom"/>; the lesson (phase 1's first frenzy) lunges on every beat.</summary>
        static readonly Move[][] Frenzies =
        {
            new[] { Move.Lunge, Move.Lunge, Move.None, Move.MoonLunge, Move.None, Move.Lunge, Move.Lunge, Move.Lunge },
            new[] { Move.Lunge, Move.Lunge, Move.None, Move.MoonLunge, Move.None, Move.Lunge, Move.None, Move.MoonLunge },
        };

        /// <summary>The glade's centre: its arena's, or where the arena would stand with its hero start on the fight's centre.</summary>
        double originX, originY;
        /// <summary>Beats counted (the song's or the fallback's); the count of cycle beat 0 (-1 until aligned); cycles begun; the last downbeat's count.</summary>
        int beats, cycleStart = -1, cycles, lastDown = -1;
        bool align = true;
        double lastBeat = double.NegativeInfinity, lastBeatS = 0.5;
        /// <summary>Where the last planned lunge leaves its body (the next lunge starts there).</summary>
        double chainX, chainY;
        int stalkAttack = -1, lungeAttack = -1, leapReadyBeat;
        double stalkUntil = double.NegativeInfinity;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, BossStartX - HeroStartX, BossStartY - HeroStartY) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;

        /// <summary>The glade's centre.</summary>
        public double OriginX => originX;
        public double OriginY => originY;

        int Cycle(BossFight f) => CycleBeats[Math.Min(f.Phase, CycleBeats.Length) - 1];

        /// <summary>The cycle beat of beat count <paramref name="n"/> (negative before the first cycle).</summary>
        int CycleBeat(BossFight f, int n) => cycleStart < 0 ? -1 : n < cycleStart ? n - cycleStart : (n - cycleStart) % Cycle(f);

        /// <summary>
        /// The moon now, 0 (new) to 1 (full): full from the howl through the frenzy, waning to new halfway through the stalk
        /// and waxing back to full at the next howl; before the first cycle and through the hunter's moon's rising, rising.
        /// Between beats it moves on by the share of the beat gone, so the disc turns smoothly.
        /// </summary>
        public double Moon(BossFight f, double t)
        {
            // The hunter's moon stands full from its rising to its first howl.
            if (f.Phase > 1 && (cycleStart < 0 || beats < cycleStart)) return 1;
            if (cycleStart < 0) return 0;
            double u = beats - cycleStart + Math.Max(0, Math.Min(1, (t - lastBeat) / lastBeatS));
            int cycle = Cycle(f);
            if (u < 0) return Math.Max(0, 1 + u / LeadBeats);
            double c = u % cycle;
            if (c <= FrenzyTo + 1) return 1;
            double s = (c - FrenzyTo - 1) / (cycle - FrenzyTo - 1);
            return Math.Abs(1 - 2 * s);
        }

        /// <summary>Whether the moon is full now (the howl through the frenzy: its afterimages trail).</summary>
        public bool FullMoon(BossFight f)
        {
            if (f.Phase > 1 && (cycleStart < 0 || beats < cycleStart)) return true;
            int c = CycleBeat(f, beats);
            return cycleStart >= 0 && c >= 0 && c <= FrenzyTo + 1;
        }

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
            ref readonly var b = ref f.Bodies[0];
            chainX = b.X;
            chainY = b.Y;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            // The hunter's moon rises: a fresh cycle, its howl on the first downbeat it can plan for after the transition.
            align = true;
            cycleStart = -1;
            stalkAttack = lungeAttack = -1;
            stalkUntil = until;
            ref readonly var b = ref f.Bodies[0];
            chainX = b.X;
            chainY = b.Y;
        }

        protected internal override void Beat(BossFight f, double t, int beat, bool downbeat) => OnBeat(f, t, downbeat, t);

        protected internal override void Tick(BossFight f, double end)
        {
            if (f.Sim.State.Beat != Reliability.Reliable && end >= lastBeat + f.BeatS - 1e-9)
                OnBeat(f, end, (beats + 1) % f.Sim.Beats.Rules.BeatsPerBar == 0, double.NaN);
            int c = CycleBeat(f, beats);
            // It stalks only in the stalk, between moves; it stands for its howl and is moved by its lunges.
            ref var b = ref f.Body(0);
            if (Stalking(f, c) && end >= stalkUntil - 1e-9 && !f.Pending(stalkAttack) && !f.Pending(lungeAttack) && !f.Moving(0))
            {
                f.Follow(0, end, KeepM);
                Bound(ref b);
            }
            else if (!f.Moving(0) && !f.Pending(stalkAttack))
            {
                f.HeroAt(end, out double hx, out double hy);
                f.Face(0, hx, hy);
            }
        }

        /// <summary>Whether cycle beat <paramref name="c"/> (now) is the stalk's, the howl's telegraph not yet begun.</summary>
        bool Stalking(BossFight f, int c) => cycleStart >= 0 && c > FrenzyTo && c < Cycle(f) - LeadBeats;

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
                // Cycle beat 0 (the howl) is the first downbeat the next beat can plan for.
                if (lastDown < 0) return;
                align = false;
                int bar = f.Sim.Beats.Rules.BeatsPerBar, first = beats + 1 + LeadBeats;
                cycleStart = lastDown + (first - lastDown + bar - 1) / bar * bar;
                return;
            }
            int land = beats + LeadBeats, c = CycleBeat(f, land);
            if (c < 0) return;
            if (c == 0) cycles++;
            switch (MoveAt(f, c))
            {
                case Move.Howl: Howl(f, from); break;
                case Move.Lunge: Lunge(f, from, moon: false); break;
                case Move.MoonLunge: Lunge(f, from, moon: true); break;
                case Move.Stalk: Stalk(f, from, c); break;
            }
        }

        /// <summary>The move landing on cycle beat <paramref name="c"/>.</summary>
        Move MoveAt(BossFight f, int c)
        {
            if (c == 0) return Move.Howl;
            if (c >= FrenzyFrom && c <= FrenzyTo)
            {
                bool lesson = f.Phase == 1 && cycles <= 1;
                if (lesson) return Move.Lunge;
                return Frenzies[Math.Min(f.Phase, Frenzies.Length) - 1][c - FrenzyFrom];
            }
            return Move.Stalk;
        }

        /// <summary>The howl: it rears where it stands, a disc round it on the downbeat; the lunges start from here.</summary>
        void Howl(BossFight f, double from)
        {
            ref readonly var b = ref f.Bodies[0];
            chainX = b.X;
            chainY = b.Y;
            f.Add(new Hazard
            {
                Attack = f.NextAttack(), Body = 0, Move = BossMove.Tail, Telegraph = f.Plan(LeadBeats, Landing.Beat, from),
                Shape = HitShape.Circle(b.X, b.Y, HowlRadiusM), Damage = HowlDamage, Flags = HazardFlags.Signature,
            });
        }

        /// <summary>A lunge from where the last left it, through the hero's spot now; a moon lunge bounds across the glade behind its front.</summary>
        void Lunge(BossFight f, double from, bool moon)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            double dx = hx - chainX, dy = hy - chainY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1)
            {
                // On top of it: lunge toward the glade's centre (or anywhere, at the centre).
                dx = originX - chainX;
                dy = originY - chainY;
                d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) { dx = 0; dy = -1; d = 1; }
            }
            double ax = dx / d, ay = dy / d;
            var tel = f.Plan(LeadBeats, Landing.Beat, from);
            int attack = f.NextAttack();
            if (!moon)
            {
                double len = Reach(ax, ay, Math.Max(LungeMinM, Math.Min(LungeMaxM, d + LungePastM)));
                double ex = chainX + ax * len, ey = chainY + ay * len;
                lungeAttack = attack;
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Move = BossMove.Smash, Telegraph = tel, Damage = LungeDamage,
                    Shape = HitShape.Path(chainX, chainY, ex, ey, LungeWidthM),
                    BodyMove = HazardBodyMove.AtLand, ToX = ex, ToY = ey, MoveS = LungeRunS,
                });
                chainX = ex;
                chainY = ey;
                return;
            }
            // The moon lunge: it bounds to the hero's side of the glade, and the moonlight floods after it, a front the
            // glade's width sweeping it from the far wall through its heart to the hero's wall: nowhere to walk to, a dash
            // through it.
            double ox = hx - originX, oy = hy - originY, od = Math.Sqrt(ox * ox + oy * oy);
            if (od > 1) { ax = ox / od; ay = oy / od; }
            double px = -ay, py = ax, beatS = tel.OnBeat && tel.Beats > 0 ? (tel.Land - tel.Start) / tel.Beats : f.BeatS;
            double bx = originX + ax * InnerM, by = originY + ay * InnerM, fx = originX - ax * WallM, fy = originY - ay * WallM;
            lungeAttack = attack;
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Move = BossMove.Stomp, Telegraph = tel, Damage = FrontDamage,
                Shape = HitShape.Path(fx - px * FrontHalfM, fy - py * FrontHalfM, fx + px * FrontHalfM, fy + py * FrontHalfM, FrontThickM),
                Vx = ax * FrontMps, Vy = ay * FrontMps, ActiveS = 2 * WallM / FrontMps,
                Flags = HazardFlags.DashWave | HazardFlags.Projectile,
                BodyMove = HazardBodyMove.AtLand, ToX = bx, ToY = by, MoveS = BoundBeats * beatS,
            });
            chainX = bx;
            chainY = by;
        }

        /// <summary>How far along (<paramref name="ax"/>, <paramref name="ay"/>) from the chain's end it may run, up to <paramref name="want"/>, staying within <see cref="InnerM"/> of the glade's centre.</summary>
        double Reach(double ax, double ay, double want)
        {
            // |c + a s| = R: s = -(c.a) + sqrt((c.a)^2 - |c|^2 + R^2).
            double cx = chainX - originX, cy = chainY - originY, ca = cx * ax + cy * ay, disc = ca * ca - (cx * cx + cy * cy) + InnerM * InnerM;
            if (disc <= 0) return 0;
            return Math.Max(0, Math.Min(want, -ca + Math.Sqrt(disc)));
        }

        /// <summary>Holds a stalking body within its bounds.</summary>
        void Bound(ref FightBody b)
        {
            double dx = b.X - originX, dy = b.Y - originY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= InnerM) return;
            b.X = originX + dx / d * InnerM;
            b.Y = originY + dy / d * InnerM;
        }

        /// <summary>
        /// The stalk's move landing on cycle beat <paramref name="c"/>, between moves: the claw combo at a hero in reach (its
        /// last claw before the howl's telegraph), else a leap at one keeping away.
        /// </summary>
        void Stalk(BossFight f, double from, int c)
        {
            // Only once the frenzy's lunges are done and the body is its own again.
            if (!Stalking(f, CycleBeat(f, beats)) || f.Pending(lungeAttack) || f.Pending(stalkAttack) || f.Moving(0) || f.Now < stalkUntil - 1e-9) return;
            int claws = f.Phase > 1 ? 3 : 2;
            // Every claw lands within the stalk, before the next howl's cycle beat.
            if (c + (claws + 1) / 2 >= Cycle(f)) return;
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(f.Now, out double hx, out double hy);
            double dx = hx - b.X, dy = hy - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double gap = d - f.TypeOf(0).RadiusM - f.HeroRadius;
            if (gap <= SwipeM)
            {
                double ax = d > 1e-9 ? dx / d : SimMath.Cos(b.Facing), ay = d > 1e-9 ? dy / d : SimMath.Sin(b.Facing);
                f.Face(0, hx, hy);
                stalkAttack = f.NextAttack();
                var last = default(Telegraph);
                for (int k = 0; k < claws; k++)
                {
                    // The claws on the beat, the half beat, the next beat: left, right, left.
                    last = f.Plan(LeadBeats + k / 2, k % 2 == 0 ? Landing.Beat : Landing.HalfBeat, from);
                    double turn = (k % 2 == 0 ? 0.25 : -0.25);
                    double cx = SimMath.Cos(turn) * ax - SimMath.Sin(turn) * ay, cy = SimMath.Sin(turn) * ax + SimMath.Cos(turn) * ay;
                    f.Add(new Hazard
                    {
                        Attack = stalkAttack, Body = 0, Part = k, Move = BossMove.Fans, Telegraph = last,
                        Shape = HitShape.Cone(b.X, b.Y, cx, cy, ClawRangeM, ClawHalfArc), Damage = ClawDamage,
                    });
                }
                stalkUntil = last.Land + StalkRestBeats * f.BeatS;
                return;
            }
            // A leap moves its body: it lands before the howl's telegraph begins, where the howl stands.
            if (d >= LeapM && beats >= leapReadyBeat && c <= Cycle(f) - LeadBeats)
            {
                // The leap: onto the hero's spot (held within its bounds), in the air for the clip's jump.
                double tx = hx, ty = hy, ox = tx - originX, oy = ty - originY, od = Math.Sqrt(ox * ox + oy * oy);
                if (od > InnerM) { tx = originX + ox / od * InnerM; ty = originY + oy / od * InnerM; }
                var tel = f.Plan(LeadBeats, Landing.Beat, from);
                f.Face(0, tx, ty);
                stalkAttack = f.NextAttack();
                f.Add(new Hazard
                {
                    Attack = stalkAttack, Body = 0, Move = BossMove.Boulders, Telegraph = tel,
                    Shape = HitShape.Circle(tx, ty, LeapRadiusM), Damage = LeapDamage,
                });
                f.MoveBody(0, tx, ty, Math.Max(tel.Start, tel.Land - LeapAirS), tel.Land, eased: true);
                stalkUntil = tel.Land + StalkRestBeats * f.BeatS;
                leapReadyBeat = beats + LeadBeats + LeapEveryBeats;
            }
        }

        /// <summary>Each burst falls in a crescent behind it, away from the hero, a new moon's sliver.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            ref readonly var b = ref f.Lead;
            f.HeroAt(t, out double hx, out double hy);
            double away = SimMath.Atan2(b.Y - hy, b.X - hx);
            const int n = 7;
            for (int k = 0; k < n; k++)
            {
                double a = away + (k - (n - 1) / 2.0) * 0.4, r = 3.6 - 0.9 * Math.Abs(k - (n - 1) / 2.0) / ((n - 1) / 2.0);
                double x = b.X + r * SimMath.Cos(a), y = b.Y + r * SimMath.Sin(a);
                double ox = x - originX, oy = y - originY, od = Math.Sqrt(ox * ox + oy * oy);
                if (od > InnerM + 2) { x = originX + ox / od * (InnerM + 2); y = originY + oy / od * (InnerM + 2); }
                into.Add(new GemSpot(x, y, 1.0 / n));
            }
        }
    }
}
