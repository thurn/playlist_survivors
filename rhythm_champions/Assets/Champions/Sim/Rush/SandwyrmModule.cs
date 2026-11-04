using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 6, the Sandwyrm, act 2's opener (boss-rush.md, The roster: <i>Ripples</i>; ps-8z4.17). A giant worm in
    /// a sunken basin of sand (arenas.json's <see cref="ArenaId"/>) that hunts beneath it: ripples run across the floor
    /// where it travels, and it erupts on the downbeat under where it was heading.
    ///
    /// - <b>The signature, the hunt</b>, taught alone first (three in a row): it sinks and travels under the sand to the
    ///   spot the hero stood on when it dived, its ripple running there across the telegraph, and bursts up through it on
    ///   the next downbeat at least <see cref="HuntBeats"/> beats on. Reading the ripple's heading is reading the hazard.
    /// - <b>Supporting moves</b>, made where it surfaced: the <see cref="Kind.Lash"/> (its Attack01, its body slammed down
    ///   in a line at the hero), the <see cref="Kind.Spit"/> (its Cast1, clods of sand lobbed round the hero's spot) and
    ///   the <see cref="Kind.Tunnel"/> (it tunnels straight through the hero's spot to the far side, the sand collapsing in
    ///   its wake behind it, one eruption a half beat: walked out of sideways).
    /// - <b>The dash wave</b>, the <see cref="Kind.Breach"/>: it travels to the basin's rim across from the hero and breaches
    ///   on a downbeat, its wave of sand rolling across the whole basin in an arc faster than any walk, crossed with a dash
    ///   (two arcs, a dash cooldown and a beat apart, in phase two).
    /// - <b>Phase two</b> below half health, after a four-beat untouchable dive to the basin's heart (the sand churns over
    ///   it): the hunt <i>chains</i> (three hunts, one a bar, each from where the last erupted), the tunnel crosses back
    ///   over itself, the spit throws more and it rests less.
    ///
    /// No memory or tracking test: every eruption's telegraph fills on the floor from the beat it is planned, and the
    /// ripple shows the worm under the sand all the way. Everything is scheduled on its beat: the song's, or without a
    /// reliable beat the half-second fallback (boss-rush.md, "Silence breaks nothing").
    /// </summary>
    public sealed class SandwyrmModule : BossModule
    {
        public const string ModuleId = "sandwyrm";
        /// <summary>Its arena in arenas.json: a round basin of <see cref="BasinM"/>; it travels no farther than <see cref="InnerM"/> from its centre.</summary>
        public const string ArenaId = "duneSea";
        public const double BasinM = 17, InnerM = 13.5;
        /// <summary>Its arena's hero and boss starts: the basin forms with the hero on its start.</summary>
        public const double HeroStartX = 0, HeroStartY = -8, BossStartX = 0, BossStartY = 6;

        // ---- Tuning (boss-rush.md, Fair fights: act 2 warns at least 3 beats) ----
        /// <summary>The hunt: at least this many beats from its dive to the downbeat it erupts on; the eruption's reach, linger and damage.</summary>
        public const int HuntBeats = 3, ChainHunts = 3;
        public const double HuntRadiusM = 3.0, HuntDamage = 16, LingerBeats = 0.5;

        /// <summary>The lash: its body slammed down in a line at the hero, past its own edge.</summary>
        public const double LashLengthM = 10, LashHalfWidthM = 1.2, LashDamage = 14;

        public const int SpitCount = 5, SpitCountTwo = 7;
        public const double SpitRadiusM = 1.8, SpitSpreadM = 4.5, SpitDamage = 12;

        /// <summary>The tunnel: eruptions every <see cref="TunnelStepM"/> from its start, one a half beat, then it bursts up at its end.</summary>
        public const double TunnelStepM = 2.0, TunnelRadiusM = 1.4, TunnelDamage = 12, TunnelEndRadiusM = 2.6, TunnelEndDamage = 14;
        /// <summary>A tunnel shorter than this aims through the basin's centre instead of the hero.</summary>
        public const double TunnelMinM = 8;

        /// <summary>The breach: its dive to the rim, and the wave of sand from there across the basin.</summary>
        public const int BreachBeats = 4;
        public const double BreachRadiusM = 3.0, BreachDamage = 16, WaveMps = 16, WaveThickM = 0.8, WaveDamage = 14, WaveReachM = 2 * BasinM + 2;

        /// <summary>Its moves (<see cref="Teach"/>, <see cref="PhaseOne"/>, <see cref="PhaseTwo"/>).</summary>
        public enum Kind : byte
        {
            Hunt, Lash, Spit, Tunnel, Breach,
            /// <summary>Phase two's: three hunts chained one a bar, and a tunnel there and back.</summary>
            Chain, CrossTunnel,
        }

        /// <summary>Phase one: the hunt taught alone (three times), then every move in turn, a hunt before each surfaced move.</summary>
        public static readonly Kind[] Teach = { Kind.Hunt, Kind.Hunt, Kind.Hunt };
        public static readonly Kind[] PhaseOne = { Kind.Lash, Kind.Hunt, Kind.Tunnel, Kind.Spit, Kind.Hunt, Kind.Breach, Kind.Hunt, Kind.Lash, Kind.Tunnel, Kind.Hunt, Kind.Spit };
        public static readonly Kind[] PhaseTwo = { Kind.Chain, Kind.Lash, Kind.CrossTunnel, Kind.Spit, Kind.Breach, Kind.Chain, Kind.Spit, Kind.Tunnel, Kind.Lash };

        int turn, rest, chainLeft, crossLeft;
        bool taught;
        double lastBeat = double.NegativeInfinity;
        /// <summary>Its time under the sand: sinking from, surfacing at (NaN: standing).</summary>
        double sinkFrom = double.NaN, surfaceAt = double.NaN;
        /// <summary>Its last run under the sand, from and to (its bursts' gems lie along it).</summary>
        double pathFromX = double.NaN, pathFromY, pathToX, pathToY;

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, BossStartX - HeroStartX, BossStartY - HeroStartY) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;
        /// <summary>The sub rumble tightens to each burst (boss-rush.md: "a low sub rumble that tightens to the burst").</summary>
        public override double TickRise => 0.12;

        /// <summary>The move it made last, and how many (the tests' and the review's).</summary>
        public Kind Last { get; private set; }
        public int Moves { get; private set; }

        /// <summary>Whether it is under the sand at <paramref name="t"/> (its ripple runs: the client's).</summary>
        public bool Under(double t) => !double.IsNaN(sinkFrom) && t >= sinkFrom && t < surfaceAt;

        /// <summary>When its current run under the sand began and ends (NaN while standing).</summary>
        public double SinkFrom => sinkFrom;
        public double SurfaceAt => surfaceAt;

        protected internal override void Begin(BossFight f, double t)
        {
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
            // It dives to the basin's heart, the sand churning over it, and rises there.
            Centre(f, out double cx, out double cy);
            Run(f.Bodies[0].X, f.Bodies[0].Y, cx, cy, t, until);
            f.MoveBody(0, cx, cy, t, until, true);
            chainLeft = crossLeft = 0;
            turn = 0;
            rest = 1;
        }

        /// <summary>How far under the sand it is at <paramref name="t"/> (0 to 1): sinking over a beat, rising over the half beat before it bursts.</summary>
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
            if (!double.IsNaN(surfaceAt) && end > surfaceAt && !f.Moving(0)) sinkFrom = surfaceAt = double.NaN;
            // Standing, it turns to keep its maw on the hero.
            if (double.IsNaN(sinkFrom) && !f.Moving(0))
            {
                f.HeroAt(end, out double hx, out double hy);
                f.Face(0, hx, hy);
            }
        }

        protected internal override void Beat(BossFight f, double t, int beat, bool downbeat)
        {
            lastBeat = t;
            OnBeat(f, t);
        }

        void OnBeat(BossFight f, double t)
        {
            if (f.Stage != FightStage.Fighting) return;
            if (f.Hazards.Length > 0 || f.Moving(0)) return;
            f.HeroAt(t, out double hx, out double hy);
            // A chain or a crossing goes on the beat its last part is done, before any rest.
            if (chainLeft > 0)
            {
                chainLeft--;
                Hunt(f, t, hx, hy);
                return;
            }
            if (crossLeft > 0)
            {
                crossLeft--;
                Tunnel(f, t, hx, hy);
                return;
            }
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
                case Kind.Hunt: Hunt(f, t, hx, hy); break;
                case Kind.Chain:
                    chainLeft = ChainHunts - 1;
                    Hunt(f, t, hx, hy);
                    break;
                case Kind.Lash: BossKit.Smash(f, 0, LashLengthM, LashHalfWidthM, LashDamage, 3); break;
                case Kind.Spit: BossKit.Boulders(f, 0, f.Phase >= 2 ? SpitCountTwo : SpitCount, SpitRadiusM, SpitSpreadM, SpitDamage, 3); break;
                case Kind.Tunnel: Tunnel(f, t, hx, hy); break;
                case Kind.CrossTunnel:
                    crossLeft = 1;
                    Tunnel(f, t, hx, hy);
                    break;
                case Kind.Breach: Breach(f, t, hx, hy); break;
            }
        }

        // ---- The hunt ----

        /// <summary>
        /// The hunt: it dives where it stands and travels under the sand to the hero's spot (<paramref name="hx"/>,
        /// <paramref name="hy"/>) across the telegraph, bursting up through it on the downbeat.
        /// </summary>
        void Hunt(BossFight f, double t, double hx, double hy)
        {
            var tel = f.Plan(HuntBeats, Landing.Downbeat);
            ref readonly var b = ref f.Bodies[0];
            Run(b.X, b.Y, hx, hy, tel.Start, tel.Land);
            f.Add(new Hazard
            {
                Attack = f.NextAttack(), Body = 0, Move = BossMove.Dive, Telegraph = tel, Shape = HitShape.Circle(hx, hy, HuntRadiusM),
                Damage = HuntDamage, ActiveS = LingerBeats * f.BeatS, Flags = HazardFlags.Signature,
                BodyMove = HazardBodyMove.DuringTelegraph, ToX = hx, ToY = hy,
            });
        }

        // ---- The tunnel ----

        /// <summary>
        /// The tunnel: it dives and runs straight through the hero's spot to the far side of the basin, under the sand across
        /// the telegraph; behind it the sand collapses into its tunnel, one eruption every <see cref="TunnelStepM"/> and a
        /// half beat, each planned when it dives and landing after it has passed; it bursts up at the far end on the last.
        /// </summary>
        void Tunnel(BossFight f, double t, double hx, double hy)
        {
            ref readonly var b = ref f.Bodies[0];
            Centre(f, out double cx, out double cy);
            double ux = hx - b.X, uy = hy - b.Y, d = Math.Sqrt(ux * ux + uy * uy);
            double reach = d > 1e-6 ? Exit(b.X - cx, b.Y - cy, ux / d, uy / d) : 0;
            if (reach < TunnelMinM)
            {
                // Too close to the rim that way: through the basin's centre instead.
                ux = cx - b.X;
                uy = cy - b.Y;
                d = Math.Sqrt(ux * ux + uy * uy);
                if (d < 1e-6)
                {
                    ux = SimMath.Cos(b.Facing);
                    uy = SimMath.Sin(b.Facing);
                    d = 1;
                }
                reach = Exit(b.X - cx, b.Y - cy, ux / d, uy / d);
            }
            ux /= d;
            uy /= d;
            int n = Math.Max(1, (int)Math.Floor(reach / TunnelStepM));
            int attack = f.NextAttack();
            for (int k = 0; k < n; k++)
            {
                double s = (k + 1) * TunnelStepM - TunnelStepM / 2;
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = k + 1, Telegraph = f.Plan(HuntBeats + k / 2, k % 2 == 1 ? Landing.HalfBeat : Landing.Beat),
                    Shape = HitShape.Circle(b.X + ux * s, b.Y + uy * s, TunnelRadiusM), Damage = TunnelDamage, ActiveS = LingerBeats * f.BeatS,
                    Flags = HazardFlags.Signature,
                });
            }
            double ex = b.X + ux * reach, ey = b.Y + uy * reach;
            var end = f.Plan(HuntBeats + n / 2, n % 2 == 1 ? Landing.HalfBeat : Landing.Beat);
            Run(b.X, b.Y, ex, ey, end.Start, end.Land);
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Part = 0, Move = BossMove.Dive, Telegraph = end, Shape = HitShape.Circle(ex, ey, TunnelEndRadiusM),
                Damage = TunnelEndDamage, ActiveS = LingerBeats * f.BeatS, BodyMove = HazardBodyMove.DuringTelegraph, ToX = ex, ToY = ey,
            });
        }

        /// <summary>How far from (<paramref name="x"/>, <paramref name="y"/>) (from the centre) along (<paramref name="ux"/>, <paramref name="uy"/>) its run reaches <see cref="InnerM"/>.</summary>
        static double Exit(double x, double y, double ux, double uy)
        {
            // |p + s u| = InnerM: s² + 2 (p·u) s + |p|² − InnerM² = 0, the far root.
            double pu = x * ux + y * uy, c = x * x + y * y - InnerM * InnerM, disc = pu * pu - c;
            return disc < 0 ? 0 : Math.Max(0, -pu + Math.Sqrt(disc));
        }

        // ---- The breach ----

        /// <summary>
        /// The breach, the dash wave: it travels under the sand to the rim across the basin from the hero and breaches there on
        /// a downbeat; its wave of sand rolls out from the breach across the whole basin, outrunning any walk.
        /// </summary>
        void Breach(BossFight f, double t, double hx, double hy)
        {
            ref readonly var b = ref f.Bodies[0];
            Centre(f, out double cx, out double cy);
            double dx = cx - hx, dy = cy - hy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1)
            {
                // A hero at the centre: the rim behind it.
                dx = b.X - cx;
                dy = b.Y - cy;
                d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) { dx = 0; dy = 1; d = 1; }
            }
            double rx = cx + dx / d * InnerM, ry = cy + dy / d * InnerM;
            int attack = f.NextAttack();
            var tel = f.Plan(BreachBeats, Landing.Downbeat);
            Run(b.X, b.Y, rx, ry, tel.Start, tel.Land);
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Move = BossMove.Dive, Telegraph = tel, Shape = HitShape.Circle(rx, ry, BreachRadiusM),
                Damage = BreachDamage, Flags = HazardFlags.Signature, BodyMove = HazardBodyMove.DuringTelegraph, ToX = rx, ToY = ry,
            });
            BossKit.Wave(f, 0, rx, ry, WaveReachM, WaveMps, WaveThickM, WaveDamage, BreachBeats, fronts: f.Phase >= 2 ? 2 : 1, attack: attack);
        }

        // ---- Under the sand ----

        /// <summary>A run under the sand from (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>), diving at <paramref name="from"/> and surfacing at <paramref name="to"/>.</summary>
        void Run(double x0, double y0, double x1, double y1, double from, double to)
        {
            sinkFrom = from;
            surfaceAt = to;
            pathFromX = x0;
            pathFromY = y0;
            pathToX = x1;
            pathToY = y1;
        }

        /// <summary>The basin's centre: its arena's in a run, else where the arena would stand with its hero start on the fight's centre.</summary>
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
                x = f.CentreX - HeroStartX;
                y = f.CentreY - HeroStartY;
            }
        }

        /// <summary>Its bursts: gems strewn along its last run under the sand (its ripple's track), else the ring.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            double len = double.IsNaN(pathFromX) ? 0 : Math.Sqrt((pathToX - pathFromX) * (pathToX - pathFromX) + (pathToY - pathFromY) * (pathToY - pathFromY));
            if (len < 3)
            {
                base.ShedSpots(f, burst, t, into);
                return;
            }
            const int n = 6;
            double nx = -(pathToY - pathFromY) / len, ny = (pathToX - pathFromX) / len;
            for (int k = 0; k < n; k++)
            {
                double u = (k + 0.5) / n, side = (k % 2 == 0 ? 1 : -1) * 1.2;
                into.Add(new GemSpot(pathFromX + (pathToX - pathFromX) * u + nx * side, pathFromY + (pathToY - pathFromY) * u + ny * side, 1.0 / n));
            }
        }
    }
}
