using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 1, the Treasure Goblin: <i>The Chase</i> (boss-rush.md, The roster; ps-8z4.6). The gentle opener: it
    /// teaches telegraphs, the beat and shedding.
    ///
    /// - <b>The grove</b> (arenas.json <c>goblinGrove</c>): a 36 m clearing round a walled copse (its stash), the goblin's
    ///   track a loop that weaves between stumps and lanterns. It runs the loop away from the hero (4.5 m/s against the
    ///   hero's 6, so it is caught), reversing when the hero cuts across.
    /// - <b>Signature, the bomb trail</b>: while it flees it drops a bomb behind it every other beat (every beat in its panic),
    ///   each a 4-beat fuse popping on its beat, so the trail goes off in time behind it. Taught alone: nothing else
    ///   comes until it has dropped <see cref="TaughtBombs"/> bombs or fought <see cref="TaughtBeats"/> beats.
    /// - <b>Coin spray</b>: kept at a distance (left farther off, it comes back round the loop to taunt), it stops and flings an arc of coins (projectiles launched on the beat, with
    ///   walking gaps); in its panic a second arc follows a beat later, offset into the first's gaps.
    /// - <b>Dagger stab</b>: caught, it lunges with its dagger, a short line at the hero.
    /// - <b>Cornered, the ring of bombs</b>: held within <see cref="CorneredM"/> for <see cref="CorneredBeats"/> beats, it
    ///   rings itself with bombs and blows them on a downbeat: a shockwave outrunning the walk across the whole grove, the
    ///   fight's dash-through wave (two fronts in its panic).
    /// - <b>Panic</b> (one phase, below 40%): a two-beat cackle, untouchable, then it runs faster and its moves double up.
    /// - <b>Shedding</b>: each burst spills behind it along its trail, some among the bombs.
    ///
    /// It works without its arena (the fairness harness, a test sim): the grove's geometry is then placed as the arena
    /// would place it round the hero's start, and only the props are missing.
    /// </summary>
    public sealed class TreasureGoblinModule : BossModule
    {
        public const string ModuleId = "treasureGoblin";
        /// <summary>Its arena (arenas.json), and that arena's hero and boss starts: the arena forms with the hero on its start.</summary>
        public const string ArenaId = "goblinGrove";
        public const double HeroStartX = 0, HeroStartY = -14.5, BossStartX = 0, BossStartY = 10.5;

        /// <summary>The track: 12 points round the copse, alternately out at <see cref="TrackOutM"/> and in at <see cref="TrackInM"/>, from due north.</summary>
        public const int TrackPoints = 12;
        public const double TrackOutM = 10.5, TrackInM = 8.5;

        // ---- Tuning (boss-rush.md, Fair fights: act 1 warns at least 3 beats) ----
        /// <summary>It flees a hero nearer than this, and holds its ground (spraying coins) out to <see cref="SprayM"/>.</summary>
        public const double FleeM = 10, SprayM = 16;
        /// <summary>Its flight's speed in its panic, against its cast speed (enemies.json).</summary>
        public const double PanicSpeed = 1.15;
        /// <summary>Left alone past <see cref="SprayM"/>, it comes back round the loop at this share of its speed, to taunt.</summary>
        public const double TauntSpeed = 0.6;
        /// <summary>It reverses at most this often, and only for a way this much better.</summary>
        public const double ReverseS = 1.0, ReverseGainM = 1.0;

        public const int FuseBeats = 4;
        public const double BombRadiusM = 1.7, BombDamage = 10;
        public const int TaughtBombs = 8, TaughtBeats = 32;

        public const int SprayBeats = 3, SprayCoins = 7, SprayRestBeats = 8;
        public const double SprayArcDeg = 90, CoinSpeedMps = 9, CoinRadiusM = 0.45, CoinRangeM = 20, CoinDamage = 8;

        public const double StabM = 4.5, StabLengthM = 4.5, StabWidthM = 1.6, StabDamage = 15;
        public const int StabBeats = 3, StabRestBeats = 6;

        public const double CorneredM = 6;
        public const int CorneredBeats = 4, RingBeats = 4, RingRestBeats = 16, RingBombs = 8;
        public const double RingBombAtM = 2.6, RingBombRadiusM = 1.0, RingBombDamage = 12;
        public const double WaveRadiusM = 30, WaveSpeedMps = 13, WaveThickM = 1.0, WaveDamage = 16;

        /// <summary>Its one phase: panic below this share of its health.</summary>
        public const double PanicBelow = 0.4;

        readonly double[] trackX = new double[TrackPoints], trackY = new double[TrackPoints], trackS = new double[TrackPoints + 1];
        double originX, originY, trackLength, s, lastReverse = double.NegativeInfinity;
        double standUntil = double.NegativeInfinity, sprayReady, stabReady, ringReady;
        int dir = 1, bombs, fightingBeats, cornered, beats;
        double lastBeat = double.NegativeInfinity;
        bool fleeing;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, BossStartX - HeroStartX, BossStartY - HeroStartY) };
        public override double[] PhasesBelow => new[] { PanicBelow };
        public override int TransitionBeats => 2;

        /// <summary>Whether it is in its panic (phase 2).</summary>
        bool Panic(BossFight f) => f.Phase > 1;

        /// <summary>The grove's centre: its arena's, or where the arena would stand with its hero start on the fight's centre.</summary>
        public double OriginX => originX;
        public double OriginY => originY;

        /// <summary>Track point <paramref name="k"/> from the grove's centre (the arena's frame).</summary>
        public static void TrackPoint(int k, out double x, out double y)
        {
            double a = Math.PI / 2 + 2 * Math.PI * k / TrackPoints, r = k % 2 == 0 ? TrackOutM : TrackInM;
            x = r * SimMath.Cos(a);
            y = r * SimMath.Sin(a);
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
            trackLength = 0;
            for (int k = 0; k < TrackPoints; k++)
            {
                TrackPoint(k, out double x, out double y);
                trackX[k] = originX + x;
                trackY[k] = originY + y;
            }
            for (int k = 0; k < TrackPoints; k++)
            {
                trackS[k] = trackLength;
                int n = (k + 1) % TrackPoints;
                trackLength += Hypot(trackX[n] - trackX[k], trackY[n] - trackY[k]);
            }
            trackS[TrackPoints] = trackLength;
            // Onto its track over the entrance (in the run it arrives on it: the boss start is the track's first point).
            ref readonly var b = ref f.Bodies[0];
            s = Nearest(b.X, b.Y);
            At(s, out double sx, out double sy);
            if (Hypot(sx - b.X, sy - b.Y) > 0.05) f.MoveBody(0, sx, sy, t, t + 2 * f.BeatS);
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            // The cackle: it stops, then comes back faster with every move ready.
            standUntil = until;
            cornered = 0;
            sprayReady = stabReady = ringReady = until;
        }

        protected internal override void Tick(BossFight f, double end)
        {
            if (f.Sim.State.Beat != Reliability.Reliable && end >= lastBeat + f.BeatS - 1e-9)
            {
                lastBeat = end;
                OnBeat(f, end);
            }
            if (f.Moving(0)) return;
            ref var b = ref f.Body(0);
            f.HeroAt(end, out double hx, out double hy);
            if (end < standUntil - 1e-9)
            {
                f.Face(0, hx, hy);
                fleeing = false;
                return;
            }
            double d = Hypot(hx - b.X, hy - b.Y);
            fleeing = d < FleeM;
            double speed = f.TypeOf(0).SpeedMps * (Panic(f) ? PanicSpeed : 1);
            // Fleeing it runs away round the loop; held at a distance it holds its ground; left alone it saunters back to taunt.
            if (fleeing) Run(f, ref b, end, hx, hy, speed, away: true);
            else if (d > SprayM) Run(f, ref b, end, hx, hy, speed * TauntSpeed, away: false);
            else f.Face(0, hx, hy);
        }

        /// <summary>A tick round the loop, the way that takes it from (or, <paramref name="away"/> false, toward) the hero.</summary>
        void Run(BossFight f, ref FightBody b, double end, double hx, double hy, double speed, bool away)
        {
            // It keeps its way unless the other is clearly better.
            const double Look = 3;
            At(s + dir * Look, out double ax, out double ay);
            At(s - dir * Look, out double bx, out double by);
            double ahead = Hypot(hx - ax, hy - ay), back = Hypot(hx - bx, hy - by);
            if ((away ? back - ahead : ahead - back) > ReverseGainM && end - lastReverse >= ReverseS)
            {
                dir = -dir;
                lastReverse = end;
            }
            s = Wrap(s + dir * speed * Simulation.TickSeconds);
            At(s, out double x, out double y);
            double dx = x - b.X, dy = y - b.Y;
            if (dx * dx + dy * dy > 1e-12) b.Facing = SimMath.Atan2(dy, dx);
            b.X = x;
            b.Y = y;
            b.Walking = true;
        }

        protected internal override void Beat(BossFight f, double t, int beat, bool downbeat)
        {
            lastBeat = t;
            OnBeat(f, t);
        }

        /// <summary>Its beat: the song's, or with no reliable beat the half-second fallback's (boss-rush.md, "Silence breaks nothing").</summary>
        void OnBeat(BossFight f, double t)
        {
            if (f.Stage != FightStage.Fighting) return;
            beats++;
            fightingBeats++;
            if (t < standUntil - 1e-9 || f.Moving(0)) return;
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(t, out double hx, out double hy);
            double d = Hypot(hx - b.X, hy - b.Y);
            cornered = d < CorneredM ? cornered + 1 : 0;
            bool taught = bombs >= TaughtBombs || fightingBeats >= TaughtBeats;
            double beatS = f.BeatS;
            if (taught && cornered >= CorneredBeats && t >= ringReady - 1e-9)
            {
                Ring(f, b.X, b.Y, beatS);
                return;
            }
            if (taught && d < StabM && t >= stabReady - 1e-9)
            {
                var tel = f.Plan(StabBeats);
                BossKit.Smash(f, 0, StabLengthM, StabWidthM, StabDamage, StabBeats);
                standUntil = tel.Land + beatS;
                stabReady = tel.Land + StabRestBeats * beatS;
                return;
            }
            if (fleeing)
            {
                if (beats % (Panic(f) ? 1 : 2) == 0) Bomb(f, b.X, b.Y);
                return;
            }
            if (taught && d < SprayM && t >= sprayReady - 1e-9) Spray(f, hx, hy, beatS);
        }

        /// <summary>A bomb where it stands: a fuse of <see cref="FuseBeats"/> beats, popping on its beat.</summary>
        void Bomb(BossFight f, double x, double y)
        {
            f.Add(new Hazard
            {
                Attack = f.NextAttack(), Body = 0, Shape = HitShape.Circle(x, y, BombRadiusM), Telegraph = f.Plan(FuseBeats),
                Damage = BombDamage, Flags = HazardFlags.Signature,
            });
            bombs++;
        }

        /// <summary>The coin spray: an arc of coins at the hero (a second, offset into its gaps, a beat later in its panic).</summary>
        void Spray(BossFight f, double hx, double hy, double beatS)
        {
            ref readonly var b = ref f.Bodies[0];
            f.Face(0, hx, hy);
            double aim = SimMath.Atan2(hy - b.Y, hx - b.X), step = SprayArcDeg * Math.PI / 180 / (SprayCoins - 1);
            int attack = f.NextAttack();
            var tel = f.Plan(SprayBeats);
            double until = tel.Land;
            Arc(f, b.X, b.Y, aim - step * (SprayCoins - 1) / 2, step, SprayCoins, tel, attack, 0);
            if (Panic(f))
            {
                var second = f.Plan(SprayBeats + 1);
                Arc(f, b.X, b.Y, aim - step * (SprayCoins - 2) / 2, step, SprayCoins - 1, second, attack, SprayCoins);
                until = second.Land;
            }
            standUntil = until + beatS;
            sprayReady = until + SprayRestBeats * beatS;
        }

        static void Arc(BossFight f, double x, double y, double from, double step, int count, in Telegraph tel, int attack, int part)
        {
            for (int k = 0; k < count; k++)
            {
                double a = from + step * k;
                // Presented as the fans (its slice clip): coins flung in lanes.
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = part + k, Move = BossMove.Fans, Telegraph = tel, Shape = HitShape.Circle(x, y, CoinRadiusM),
                    Damage = CoinDamage, ActiveS = CoinRangeM / CoinSpeedMps, Vx = SimMath.Cos(a) * CoinSpeedMps, Vy = SimMath.Sin(a) * CoinSpeedMps,
                    Flags = HazardFlags.Projectile,
                });
            }
        }

        /// <summary>Cornered: a ring of bombs round it, blown on a downbeat, and the shockwave they send out (the dash wave).</summary>
        void Ring(BossFight f, double x, double y, double beatS)
        {
            int fronts = Panic(f) ? 2 : 1;
            int attack = BossKit.Wave(f, 0, x, y, WaveRadiusM, WaveSpeedMps, WaveThickM, WaveDamage, RingBeats, fronts: fronts, move: BossMove.Wave);
            // The same plan the wave's first front took (planned now, to the same downbeat).
            var tel = f.Plan(RingBeats, Landing.Downbeat);
            double turn = f.Random.NextDouble() * 2 * Math.PI;
            for (int k = 0; k < RingBombs; k++)
            {
                double a = turn + 2 * Math.PI * k / RingBombs;
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = fronts + k, Telegraph = tel,
                    Shape = HitShape.Circle(x + RingBombAtM * SimMath.Cos(a), y + RingBombAtM * SimMath.Sin(a), RingBombRadiusM), Damage = RingBombDamage,
                });
            }
            double last = f.BusyUntil;
            standUntil = tel.Land + beatS;
            cornered = 0;
            ringReady = Math.Max(tel.Land, double.IsNaN(last) ? tel.Land : last) + RingRestBeats * beatS;
        }

        /// <summary>Each burst spills behind it, along the trail it ran (some of it among its bombs).</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            ref readonly var b = ref f.Lead;
            const int n = 7;
            double back = b.Facing + Math.PI, spread = 70 * Math.PI / 180;
            for (int k = 0; k < n; k++)
            {
                double a = back - spread / 2 + spread * k / (n - 1), r = 2.5 + 3.5 * ((k * 3 + burst) % n) / (n - 1);
                into.Add(new GemSpot(b.X + r * SimMath.Cos(a), b.Y + r * SimMath.Sin(a), 1.0 / n));
            }
        }

        // ---- The track ----

        double Wrap(double v)
        {
            v %= trackLength;
            return v < 0 ? v + trackLength : v;
        }

        /// <summary>The point <paramref name="at"/> metres round the track (from its first point, the boss start).</summary>
        void At(double at, out double x, out double y)
        {
            at = Wrap(at);
            int k = 0;
            while (k < TrackPoints - 1 && trackS[k + 1] <= at) k++;
            int n = (k + 1) % TrackPoints;
            double u = (at - trackS[k]) / (trackS[k + 1] - trackS[k]);
            x = trackX[k] + (trackX[n] - trackX[k]) * u;
            y = trackY[k] + (trackY[n] - trackY[k]) * u;
        }

        /// <summary>The track's arc length nearest (<paramref name="x"/>, <paramref name="y"/>).</summary>
        double Nearest(double x, double y)
        {
            double best = double.MaxValue, at = 0;
            for (int k = 0; k < TrackPoints; k++)
            {
                int n = (k + 1) % TrackPoints;
                double ex = trackX[n] - trackX[k], ey = trackY[n] - trackY[k], len2 = ex * ex + ey * ey;
                double u = Math.Max(0, Math.Min(1, ((x - trackX[k]) * ex + (y - trackY[k]) * ey) / len2));
                double px = trackX[k] + ex * u, py = trackY[k] + ey * u, d = Hypot(x - px, y - py);
                if (d < best)
                {
                    best = d;
                    at = trackS[k] + u * (trackS[k + 1] - trackS[k]);
                }
            }
            return at;
        }

        static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
