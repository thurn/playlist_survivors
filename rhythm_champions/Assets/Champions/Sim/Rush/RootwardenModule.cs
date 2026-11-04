using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 4, the Rootwarden (boss-rush.md, The roster: <i>Echo eruptions</i>; ps-8z4.12). An old treant in its
    /// hollow (arenas.json's <see cref="ArenaId"/>) that listens to the ground: roots erupt where the hero stood a few beats
    /// ago, so standing still is the one mistake and the hero's own trail becomes the hazard.
    ///
    /// - <b>The signature, the echo</b>, taught alone first: for <see cref="EchoMarks"/> beats running, each beat marks the
    ///   spot the hero stands on, and a root erupts there <see cref="EchoBeats"/> beats later, on its beat. Walking on the
    ///   beat leaves every eruption behind; the telegraphs trace the hero's own steps.
    /// - <b>Supporting moves</b>: the <see cref="Kind.Line"/> (its Root Attack: eruptions racing out from it toward the
    ///   hero's spot, one a half beat, walked out of sideways), the <see cref="Kind.Seeds"/> (seeds lobbed round the hero's
    ///   spot, sprouting together) and the <see cref="Kind.Burrow"/> (it sinks, travels under the floor to the hero's spot
    ///   and bursts up on the downbeat, a ring of roots spreading a beat later).
    /// - <b>The dash wave</b>: the <see cref="Kind.Quake"/>, its stomp sending a ring of roots out across the hollow faster
    ///   than walking, on a downbeat (two rings in phase two).
    /// - <b>Phase two</b> below half health, after a four-beat untouchable retreat into the hollow's heart: the echo
    ///   <i>chains</i> (each eruption a root running the hero's path from the last marked spot to this one), it marks
    ///   more beats, combines the chain with a line or seeds, fans its lines three wide and rests less.
    ///
    /// No memory or tracking test: every eruption's telegraph fills on the floor from the beat it is marked.
    /// </summary>
    public sealed class RootwardenModule : BossModule
    {
        public const string ModuleId = "rootwarden";
        /// <summary>Its arena in arenas.json: a round hollow of <see cref="HollowM"/>.</summary>
        public const string ArenaId = "rootHollow";
        public const double HollowM = 17;

        // ---- Tuning (boss-rush.md, Fair fights: act 1 warns at least 3 beats) ----
        /// <summary>The echo: beats marked in a run (phase one, two), the beats from mark to eruption, its reach and damage.</summary>
        public const int EchoMarks = 6, ChainMarks = 8, EchoBeats = 3;
        public const double EchoRadiusM = 1.5, ChainReachM = 1.0, EchoDamage = 10;
        /// <summary>An eruption's roots stay up this share of a beat after it lands.</summary>
        public const double LingerBeats = 0.5;

        /// <summary>The line: eruptions from <see cref="LineFromM"/> out, every <see cref="LineStepM"/>, to <see cref="LineRangeM"/>, one a half beat.</summary>
        public const double LineFromM = 2.5, LineStepM = 1.8, LineRangeM = 22, LineRadiusM = 1.3, LineDamage = 12;
        public const double FanDegrees = 32;

        public const int SeedCount = 5, SeedCountTwo = 7;
        public const double SeedRadiusM = 1.8, SeedSpreadM = 4.5, SeedDamage = 12;

        /// <summary>The burrow: its burst on the hero's marked spot, and the ring of roots spreading a beat after.</summary>
        public const int BurrowBeats = 4;
        public const double BurstRadiusM = 3.2, BurstDamage = 18, RingOuterM = 5.5, RingDamage = 10;

        public const int QuakeBeats = 4;
        public const double QuakeRadiusM = 34, QuakeSpeedMps = 14, QuakeThickM = 1.2, QuakeDamage = 16;

        /// <summary>It keeps this far from the hero while it walks between moves.</summary>
        public const double KeepM = 6;

        /// <summary>Its moves (<see cref="Teach"/>, <see cref="PhaseOne"/>, <see cref="PhaseTwo"/>).</summary>
        public enum Kind : byte
        {
            Echo, Line, Seeds, Burrow, Quake,
            /// <summary>Phase two's: the chained echo, three lines fanned, and the chain under a line or seeds.</summary>
            Chain, Fan, ChainLine, ChainSeeds,
        }

        /// <summary>Phase one: the echo taught alone (twice), then every move in turn, the echo between.</summary>
        public static readonly Kind[] Teach = { Kind.Echo, Kind.Echo };
        public static readonly Kind[] PhaseOne = { Kind.Line, Kind.Echo, Kind.Seeds, Kind.Burrow, Kind.Echo, Kind.Quake };
        public static readonly Kind[] PhaseTwo = { Kind.Chain, Kind.Fan, Kind.ChainSeeds, Kind.Burrow, Kind.Quake, Kind.ChainLine, Kind.Seeds, Kind.Chain };

        /// <summary>The hero's spot each beat (the shedding's trail), newest last.</summary>
        public const int Trail = 8;
        readonly double[] trailX = new double[Trail], trailY = new double[Trail];
        int trailCount, trailHead;

        int turn, beats, echoLeft, echoAttack = -1, rest;
        bool taught, chaining;
        double lastMarkX = double.NaN, lastMarkY = double.NaN, lastBeat = double.NegativeInfinity;
        /// <summary>The burrow under way: its sinking from, its travel's end, its surfacing.</summary>
        double sinkFrom = double.NaN, surfaceAt = double.NaN;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, 0, 13) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;
        /// <summary>The creak climbs to each eruption (boss-rush.md: "rumbles timed to the eruptions").</summary>
        public override double TickRise => 0.1;

        /// <summary>The move it made last, and how many (the tests' and the review's).</summary>
        public Kind Last { get; private set; }
        public int Moves { get; private set; }

        protected internal override void Begin(BossFight f, double t)
        {
            // In its hollow it starts at the arena's boss start, not the framework's default above the hero.
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid)
            {
                ref var b = ref f.Body(0);
                b.X = arena.BossStartX;
                b.Y = arena.BossStartY;
            }
            rest = 1;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            // It draws back into the hollow's heart; the echo running is called off with the rest.
            Centre(f, out double cx, out double cy);
            f.MoveBody(0, cx, cy, t, until, true);
            echoLeft = 0;
            echoAttack = -1;
            sinkFrom = surfaceAt = double.NaN;
            turn = 0;
            rest = 1;
        }

        /// <summary>How far under the floor its body is at <paramref name="t"/> (0 to 1): sinking over a beat as it burrows, rising over a half beat to burst.</summary>
        public override double Sunk(BossFight f, int body, double t)
        {
            if (double.IsNaN(sinkFrom) || t < sinkFrom || t > surfaceAt + 0.05) return 0;
            double beatS = f.BeatS;
            double down = (t - sinkFrom) / beatS, up = (surfaceAt - t) / (0.5 * beatS);
            return Math.Max(0, Math.Min(1, Math.Min(down, up)));
        }

        protected internal override void Tick(BossFight f, double end)
        {
            if (f.Sim.State.Beat != Reliability.Reliable && end >= lastBeat + f.BeatS - 1e-9)
            {
                lastBeat = end;
                OnBeat(f, end);
            }
            if (f.Moving(0) || !double.IsNaN(sinkFrom) && end <= surfaceAt) return;
            if (!double.IsNaN(surfaceAt)) sinkFrom = surfaceAt = double.NaN;
            f.Follow(0, end, KeepM);
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
            f.HeroAt(t, out double hx, out double hy);
            trailX[trailHead] = hx;
            trailY[trailHead] = hy;
            trailHead = (trailHead + 1) % Trail;
            trailCount = Math.Min(Trail, trailCount + 1);
            if (echoLeft > 0)
            {
                Mark(f, t, hx, hy);
                return;
            }
            if (f.Hazards.Length > 0 || f.Moving(0)) return;
            if (--rest > 0) return;
            Kind kind;
            if (f.Phase >= 2) kind = PhaseTwo[turn++ % PhaseTwo.Length];
            else if (!taught)
            {
                kind = Teach[turn++];
                if (turn >= Teach.Length) { taught = true; turn = 0; }
            }
            else kind = PhaseOne[turn++ % PhaseOne.Length];
            Use(f, kind, t, hx, hy);
            rest = f.Phase >= 2 ? 1 : 2;
        }

        void Use(BossFight f, Kind kind, double t, double hx, double hy)
        {
            Last = kind;
            Moves++;
            switch (kind)
            {
                case Kind.Echo: Echo(f, EchoMarks, false, t, hx, hy); break;
                case Kind.Chain: Echo(f, ChainMarks, true, t, hx, hy); break;
                case Kind.Line: Line(f, hx, hy, 1); break;
                case Kind.Fan: Line(f, hx, hy, 3); break;
                case Kind.Seeds: Seeds(f); break;
                case Kind.Burrow: Burrow(f, hx, hy); break;
                case Kind.Quake: Quake(f); break;
                case Kind.ChainLine:
                    Line(f, hx, hy, 1);
                    Echo(f, ChainMarks, true, t, hx, hy);
                    break;
                case Kind.ChainSeeds:
                    Seeds(f);
                    Echo(f, ChainMarks, true, t, hx, hy);
                    break;
            }
        }

        // ---- The echo ----

        /// <summary>Starts an echo of <paramref name="marks"/> beats, marking the first on beat <paramref name="t"/> (the hero at (<paramref name="hx"/>, <paramref name="hy"/>)).</summary>
        void Echo(BossFight f, int marks, bool chain, double t, double hx, double hy)
        {
            echoAttack = f.NextAttack();
            echoLeft = marks;
            chaining = chain;
            lastMarkX = lastMarkY = double.NaN;
            Mark(f, t, hx, hy);
        }

        /// <summary>Beat <paramref name="at"/>'s mark: a root erupting on the hero's spot <see cref="EchoBeats"/> beats on (chained: along its path from the last mark).</summary>
        void Mark(BossFight f, double at, double hx, double hy)
        {
            var shape = chaining && !double.IsNaN(lastMarkX)
                ? HitShape.Path(lastMarkX, lastMarkY, hx, hy, ChainReachM)
                : HitShape.Circle(hx, hy, EchoRadiusM);
            f.Add(new Hazard
            {
                Attack = echoAttack, Body = 0, Part = (chaining ? ChainMarks : EchoMarks) - echoLeft, Telegraph = f.Plan(EchoBeats, Landing.Beat, at),
                Shape = shape, Damage = EchoDamage, ActiveS = LingerBeats * f.BeatS, Flags = HazardFlags.Signature,
            });
            lastMarkX = hx;
            lastMarkY = hy;
            echoLeft--;
        }

        // ---- The supporting moves ----

        /// <summary>
        /// The line (its Root Attack): eruptions racing out from it toward the hero's spot, the k-th landing k half beats
        /// after the first; <paramref name="lines"/> of them fanned <see cref="FanDegrees"/> apart. Each stops at the walls.
        /// </summary>
        void Line(BossFight f, double hx, double hy, int lines)
        {
            ref readonly var b = ref f.Bodies[0];
            f.Face(0, hx, hy);
            double aim = Math.Abs(hx - b.X) + Math.Abs(hy - b.Y) < 1e-6 ? b.Facing : SimMath.Atan2(hy - b.Y, hx - b.X);
            int attack = f.NextAttack(), part = 0;
            for (int l = 0; l < lines; l++)
            {
                double a = aim + (l - (lines - 1) / 2.0) * FanDegrees * Math.PI / 180, ux = Math.Cos(a), uy = Math.Sin(a);
                int k = 0;
                for (double d = LineFromM; d <= LineRangeM; d += LineStepM, k++)
                {
                    double x = b.X + ux * d, y = b.Y + uy * d;
                    if (!InHollow(f, x, y)) break;
                    var tel = f.Plan(3 + k / 2, k % 2 == 1 ? Landing.HalfBeat : Landing.Beat);
                    f.Add(new Hazard
                    {
                        Attack = attack, Body = 0, Part = part++, Move = BossMove.Smash, Telegraph = tel,
                        Shape = HitShape.Circle(x, y, LineRadiusM), Damage = LineDamage, ActiveS = LingerBeats * f.BeatS,
                    });
                }
            }
        }

        /// <summary>The seeds (its Projectile Up Attack): lobbed round the hero's spot, sprouting together.</summary>
        void Seeds(BossFight f) =>
            BossKit.Boulders(f, 0, f.Phase >= 2 ? SeedCountTwo : SeedCount, SeedRadiusM, SeedSpreadM, SeedDamage, 3);

        /// <summary>
        /// The burrow: it sinks, travels under the floor to the hero's marked spot across the telegraph and bursts up there
        /// on the downbeat; a ring of roots spreads round the burst a beat later.
        /// </summary>
        void Burrow(BossFight f, double hx, double hy)
        {
            int attack = f.NextAttack();
            var tel = f.Plan(BurrowBeats, Landing.Downbeat);
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Move = BossMove.Dive, Telegraph = tel, Shape = HitShape.Circle(hx, hy, BurstRadiusM),
                Damage = BurstDamage, BodyMove = HazardBodyMove.DuringTelegraph, ToX = hx, ToY = hy,
            });
            // The beat after the burst's downbeat (beatless: a fallback beat after it).
            double next = tel.OnBeat ? f.Sim.Beats.PredictBeat(tel.LandBeat + 1) : double.NaN;
            if (double.IsNaN(next) || next <= tel.Land + 1e-6) next = tel.Land + f.BeatS;
            var ring = new Telegraph(tel.Start, next, tel.StartBeat, tel.OnBeat ? tel.LandBeat + 1 : -1, true);
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Part = 1, Telegraph = ring, Shape = HitShape.Ring(hx, hy, BurstRadiusM, RingOuterM),
                Damage = RingDamage, ActiveS = LingerBeats * f.BeatS,
            });
            sinkFrom = tel.Start;
            surfaceAt = tel.Land;
        }

        /// <summary>The quake (its stomp), the dash wave: a ring of roots out across the hollow, outrunning the walk.</summary>
        void Quake(BossFight f)
        {
            ref readonly var b = ref f.Bodies[0];
            BossKit.Wave(f, 0, b.X, b.Y, QuakeRadiusM, QuakeSpeedMps, QuakeThickM, QuakeDamage, QuakeBeats,
                fronts: f.Phase >= 2 ? 2 : 1, move: BossMove.Wave);
        }

        // ---- Geometry ----

        /// <summary>The hollow's centre: its arena's in a run, else the fight's.</summary>
        static void Centre(BossFight f, out double x, out double y)
        {
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid)
            {
                x = arena.OriginX;
                y = arena.OriginY;
            }
            else
            {
                x = f.CentreX;
                y = f.CentreY;
            }
        }

        /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is on the hollow's floor (its arena's, else <see cref="HollowM"/> round the centre).</summary>
        static bool InHollow(BossFight f, double x, double y)
        {
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid) return arena.Inside(x, y);
            Centre(f, out double cx, out double cy);
            return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= HollowM * HollowM;
        }

        /// <summary>Its bursts: gems strewn along the hero's last steps (its trail, where the roots have been), else the ring.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            if (trailCount < 3)
            {
                base.ShedSpots(f, burst, t, into);
                return;
            }
            int n = Math.Min(6, trailCount);
            for (int k = 0; k < n; k++)
            {
                int i = ((trailHead - 1 - k) % Trail + Trail) % Trail;
                double a = burst * 1.3 + k * 2.1;
                into.Add(new GemSpot(trailX[i] + 0.8 * SimMath.Cos(a), trailY[i] + 0.8 * SimMath.Sin(a), 1.0 / n));
            }
        }
    }
}
