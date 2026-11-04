using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 2, the Hive Queen (boss-rush.md, The roster: <i>Swarm formations</i>). Her drones fly in beat-locked
    /// formations across her honeycomb clearing (arenas.json's <see cref="ArenaId"/>, a hexagon) and the hero walks through
    /// the gaps; she dives on the downbeat; her dash wave is a solid wall of drones.
    ///
    /// - <b>The signature</b>, taught alone first: a <see cref="Kind.Sweep"/>, a row of drones the width of the clearing with
    ///   walking-width gaps, launched from one wall on the beat and crossing in <see cref="CrossBeats"/> beats; then the
    ///   <see cref="Kind.Chevron"/>, the same row bent into a V with its point leading and a gap at the point.
    /// - <b>Supporting moves</b>: the <see cref="Kind.Dive"/> (her body flies to the hero's marked spot and stings it on the
    ///   downbeat), the <see cref="Kind.Spiral"/> (rings of drones out of her body, one a beat, each turned on from the
    ///   last) and the <see cref="Kind.Net"/> (six rows round the hero's spot, open at the corners, closing on it).
    /// - <b>The dash wave</b>: the <see cref="Kind.Wall"/>, a gapless row faster than walking, signalled by a longer
    ///   telegraph, its own move (the cast clip) and no gaps at all.
    /// - <b>Phase two</b> below half health, after a four-beat untouchable swarm (she flies to the clearing's centre): the
    ///   moves combine (a sweep under a dive, two sweeps crossing, the net round a dive), the rows cross faster and she
    ///   rests less.
    ///
    /// Every row is a travelling line hazard per stretch of drones between two gaps (its telegraph fills where it launches);
    /// the client draws the drones on them (HiveSwarmPresenter). No memory or tracking: every gap is in front of the hero as
    /// its row fills.
    /// </summary>
    public sealed class HiveQueenModule : BossModule
    {
        public const string ModuleId = "hiveQueen";
        /// <summary>Her arena in arenas.json: a flat-topped hexagon of <see cref="CircumM"/> (its vertices at 0°, 60° ...).</summary>
        public const string ArenaId = "hiveClearing";
        public const double CircumM = 17;
        /// <summary>The hexagon's apothem: the centre to each wall.</summary>
        public static readonly double ApothemM = CircumM * Math.Cos(Math.PI / 6);

        /// <summary>A drone row's half thickness (its capsule's reach), the gaps' clear width and their spacing along the row.</summary>
        public const double DroneReachM = 0.55, GapM = 3.4, GapEveryM = 8.5;
        /// <summary>The chevron's arms trail back this much per metre out from its point.</summary>
        public const double ChevronSlope = 0.4;
        /// <summary>The beats a row takes to cross the clearing, wall to wall, in phase one and two.</summary>
        public const int CrossBeats = 6, CrossBeatsTwo = 5;
        public const double DroneDamage = 12, DiveDamage = 18, SpiralDamage = 10, NetDamage = 12, WallDamage = 15;
        public const double DiveRadiusM = 3, KeepM = 6;
        public const int SpiralRings = 5, SpiralRingsTwo = 7, SpiralArms = 5;
        public const double SpiralSpeedMps = 7, SpiralRadiusM = 0.5, SpiralRangeM = 26;
        public const double NetApothemM = 10;
        public const int NetCloseBeats = 4, WallBeats = 4;
        public const double WallMinMps = 16;
        /// <summary>A row is laid in pieces of at most this, and a piece may wait this many half beats to be inside the clearing.</summary>
        public const double PieceM = 2;
        /// <summary>A row's half width (its ends just short of the side corners) and how far inside the walls it starts.</summary>
        public const double RowHalfM = CircumM - 0.5, RowInsetM = 1.3;
        public const int MaxDelayHalfBeats = 16;

        /// <summary>Her moves (<see cref="Script"/>'s entries).</summary>
        public enum Kind : byte
        {
            Sweep, Chevron, Dive, Spiral, Net, Wall,
            /// <summary>Phase two's combinations.</summary>
            SweepDive, Cross, NetDive,
        }

        /// <summary>Phase one: the sweep taught alone, then the chevron, the dive, then everything in turn.</summary>
        public static readonly Kind[] Teach = { Kind.Sweep, Kind.Sweep, Kind.Chevron, Kind.Dive };
        public static readonly Kind[] PhaseOne = { Kind.Sweep, Kind.Spiral, Kind.Dive, Kind.Wall, Kind.Chevron, Kind.Net, Kind.Dive, Kind.Sweep };
        public static readonly Kind[] PhaseTwo = { Kind.SweepDive, Kind.Cross, Kind.Wall, Kind.Spiral, Kind.NetDive, Kind.Chevron, Kind.Wall, Kind.SweepDive, Kind.Dive };

        int turn;
        bool taught;
        double nextMoveAt;

        public override string Id => ModuleId;
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;
        /// <summary>Her hum climbs to each impact (boss-rush.md: "a hum whose pitch rises to the impact").</summary>
        public override double TickRise => 0.15;

        /// <summary>The move she made last (the tests' and the review's).</summary>
        public Kind Last { get; private set; }
        public int Moves { get; private set; }

        protected internal override void Begin(BossFight f, double t)
        {
            // In her arena she starts at its boss start, not the framework's default above the hero.
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid)
            {
                ref var b = ref f.Body(0);
                b.X = arena.BossStartX;
                b.Y = arena.BossStartY;
            }
            nextMoveAt = t;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            Centre(f, out double cx, out double cy);
            f.MoveBody(0, cx, cy, t, until, true);
            turn = 0;
            nextMoveAt = until;
        }

        protected internal override void Tick(BossFight f, double end)
        {
            if (f.Hazards.Length > 0 || f.Moving(0)) return;
            if (double.IsNaN(nextMoveAt))
            {
                int rest = f.Phase >= 2 ? 1 : 2;
                nextMoveAt = end + Math.Max(f.Sim.Dashes.Rules.CooldownS, (rest - 0.5) * f.BeatS);
            }
            f.Follow(0, end, KeepM);
            if (end < nextMoveAt - 1e-9) return;
            Kind kind;
            if (f.Phase >= 2) kind = PhaseTwo[turn++ % PhaseTwo.Length];
            else if (!taught)
            {
                kind = Teach[turn++];
                if (turn >= Teach.Length) { taught = true; turn = 0; }
            }
            else kind = PhaseOne[turn++ % PhaseOne.Length];
            Use(f, kind);
            nextMoveAt = double.NaN;
        }

        void Use(BossFight f, Kind kind)
        {
            Last = kind;
            Moves++;
            switch (kind)
            {
                case Kind.Sweep: Row(f, Toward(f), false, Offset(f), Signature); break;
                case Kind.Chevron: Row(f, Toward(f), true, 0, Signature); break;
                case Kind.Dive: Dive(f); break;
                case Kind.Spiral: Spiral(f, f.Phase >= 2 ? SpiralRingsTwo : SpiralRings); break;
                case Kind.Net: Net(f); break;
                case Kind.Wall: Wall(f); break;
                case Kind.SweepDive:
                    Row(f, Toward(f), false, Offset(f), Signature);
                    Dive(f);
                    break;
                case Kind.Cross:
                {
                    // The second row two beats behind the first, from a wall a third of the way round.
                    int dir = Toward(f);
                    Row(f, dir, false, Offset(f), Signature);
                    Row(f, (dir + (f.Random.NextDouble() < 0.5 ? 2 : 4)) % 6, false, Offset(f), Signature, beats: 5);
                    break;
                }
                case Kind.NetDive:
                    Net(f);
                    Dive(f);
                    break;
            }
        }

        const HazardFlags Signature = HazardFlags.Signature | HazardFlags.Projectile;

        // ---- Geometry ----

        /// <summary>The clearing's centre: her arena's in a run, else the fight's.</summary>
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

        /// <summary>The way a row from wall <paramref name="dir"/> travels (0 to 5: up the screen, then each next wall anticlockwise).</summary>
        public static double Heading(int dir) => Math.PI / 2 + dir * Math.PI / 3;

        /// <summary>The wall whose rows fly most nearly from her toward the hero (the drones pour out behind her).</summary>
        static int Toward(BossFight f)
        {
            ref readonly var b = ref f.Bodies[0];
            f.HeroAt(f.Now, out double hx, out double hy);
            double a = Math.Atan2(hy - b.Y, hx - b.X);
            if (Math.Abs(hx - b.X) + Math.Abs(hy - b.Y) < 1e-6) a = b.Facing;
            int dir = (int)Math.Round((a - Math.PI / 2) / (Math.PI / 3));
            return ((dir % 6) + 6) % 6;
        }

        /// <summary>Where a sweep's gaps fall along its row: a fresh place each time, never the same twice.</summary>
        double Offset(BossFight f) => (f.Random.NextDouble() - 0.5) * GapEveryM;

        /// <summary>The row's beats to cross the clearing now.</summary>
        static int Cross(BossFight f) => f.Phase >= 2 ? CrossBeatsTwo : CrossBeats;

        /// <summary>
        /// The stretches of drones along a row of half width <paramref name="half"/>, as lateral spans between gaps of
        /// <see cref="GapM"/> every <see cref="GapEveryM"/> from <paramref name="offset"/> (no gaps with NaN).
        /// </summary>
        public static List<(double From, double To)> Stretches(double half, double offset)
        {
            var spans = new List<(double, double)>();
            if (double.IsNaN(offset))
            {
                spans.Add((-half, half));
                return spans;
            }
            int k0 = (int)Math.Floor((-half - offset) / GapEveryM) - 1, k1 = (int)Math.Ceiling((half - offset) / GapEveryM) + 1;
            for (int k = k0; k < k1; k++)
            {
                double from = Math.Max(-half, offset + k * GapEveryM + GapM / 2), to = Math.Min(half, offset + (k + 1) * GapEveryM - GapM / 2);
                if (to - from > 2 * DroneReachM) spans.Add((from, to));
            }
            return spans;
        }

        /// <summary>
        /// A row of drones from wall <paramref name="dir"/>: launched on the beat <paramref name="beats"/> on, flying across
        /// the clearing in <see cref="Cross"/> beats, shaped by <see cref="Depth"/>; <paramref name="offset"/> NaN is a
        /// gapless row (the wall).
        /// </summary>
        int Row(BossFight f, int dir, bool chevron, double offset, HazardFlags flags, int beats = 3, double speedMps = 0,
            BossMove move = BossMove.Fans, double damage = DroneDamage)
        {
            Centre(f, out double cx, out double cy);
            double a = Heading(dir), dx = Math.Cos(a), dy = Math.Sin(a), px = -dy, py = dx;
            double half = RowHalfM, slope = chevron ? ChevronSlope : 0;
            // The row starts just inside the walls it launches from: along the near wall, its ends bent forward along the
            // two walls beside it (so no part of it fills outside the clearing), a chevron's point further forward.
            double rear = -(ApothemM - RowInsetM);
            double v = speedMps > 0 ? speedMps : 2 * ApothemM / (Cross(f) * f.BeatS);
            double travel = 2 * ApothemM + 1;
            int attack = f.NextAttack(), part = 0;
            foreach (var (s0, s1) in Stretches(half, offset))
            {
                // Each stretch in pieces of at most PieceM, so each joins the row as soon as it is inside the clearing.
                int n = Math.Max(1, (int)Math.Ceiling((s1 - s0) / PieceM - 1e-9));
                for (int i = 0; i < n; i++)
                {
                    double from = s0 + (s1 - s0) * i / n, to = s0 + (s1 - s0) * (i + 1) / n;
                    double x0 = from + DroneReachM, x1 = to - DroneReachM;
                    double d0 = rear + Depth(x0, slope), d1 = rear + Depth(x1, slope);
                    Launch(f, attack, part++, move, beats, cx + dx * d0 + px * x0, cy + dy * d0 + py * x0,
                        cx + dx * d1 + px * x1, cy + dy * d1 + py * x1, dx * v, dy * v, travel, damage, flags);
                }
            }
            return attack;
        }

        /// <summary>
        /// How far ahead of its rear a row stands at lateral <paramref name="x"/>: along the near wall in the middle, along the
        /// walls beside it past the near wall's corners (the hexagon's sides at 60°), a chevron's point leading by
        /// <paramref name="slope"/> per metre in from its ends.
        /// </summary>
        public static double Depth(double x, double slope)
        {
            double ax = Math.Abs(x);
            return Math.Max(Math.Max(0, (ax - CircumM / 2) * Math.Sqrt(3)), slope * (RowHalfM - ax));
        }

        /// <summary>
        /// Lays one stretch of drones (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>,
        /// <paramref name="y1"/>) flying at (<paramref name="vx"/>, <paramref name="vy"/>) for up to <paramref name="travel"/>
        /// metres. It launches on the first beat or half beat from <paramref name="beats"/> on at which, moved on in lockstep
        /// with its row, it lies wholly inside the clearing, so no part of a formation fills or flies outside the walls; it
        /// ends once it has left the clearing. False when it never enters.
        /// </summary>
        bool Launch(BossFight f, int attack, int part, BossMove move, int beats, double x0, double y0, double x1, double y1,
            double vx, double vy, double travel, double damage, HazardFlags flags)
        {
            Centre(f, out double cx, out double cy);
            double v = Math.Sqrt(vx * vx + vy * vy), ux = vx / v, uy = vy / v;
            var first = f.Plan(beats);
            for (int k = 0; k <= MaxDelayHalfBeats; k++)
            {
                var tel = k == 0 ? first : f.Plan(beats + k / 2, k % 2 == 1 ? Landing.HalfBeat : Landing.Beat);
                double d = v * (tel.Land - first.Land);
                if (d >= travel) return false;
                double ax = x0 + ux * d - cx, ay = y0 + uy * d - cy, bx = x1 + ux * d - cx, by = y1 + uy * d - cy;
                if (!InClearing(ax, ay, DroneReachM) || !InClearing(bx, by, DroneReachM)) continue;
                double left = Leaves(ax, ay, bx, by, ux, uy, travel - d);
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = part, Move = move, Telegraph = tel, Damage = damage, Flags = flags,
                    Shape = HitShape.Path(ax + cx, ay + cy, bx + cx, by + cy, DroneReachM), ActiveS = left / v, Vx = vx, Vy = vy,
                });
                return true;
            }
            return false;
        }

        /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) from the clearing's centre lies <paramref name="margin"/> inside its walls (negative: outside by at most that).</summary>
        public static bool InClearing(double x, double y, double margin = 0)
        {
            for (int k = 0; k < 6; k++)
            {
                double a = Math.PI / 6 + k * Math.PI / 3;
                if (x * Math.Cos(a) + y * Math.Sin(a) > ApothemM - margin) return false;
            }
            return true;
        }

        /// <summary>
        /// How far a segment moving along (<paramref name="ux"/>, <paramref name="uy"/>) goes
        /// before its whole line of drones is past the walls (where they break off), at most <paramref name="most"/>.
        /// </summary>
        static double Leaves(double ax, double ay, double bx, double by, double ux, double uy, double most)
        {
            for (double s = 0.25; s < most; s += 0.25)
            {
                bool inside = false;
                for (int i = 0; i <= 8 && !inside; i++)
                {
                    double u = i / 8.0;
                    inside = InClearing(ax + (bx - ax) * u + ux * s, ay + (by - ay) * u + uy * s);
                }
                if (!inside) return s;
            }
            return most;
        }

        /// <summary>The dive: the hero's spot marked as she takes off; she flies there across the telegraph and stings it on the downbeat.</summary>
        int Dive(BossFight f)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            int attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = 0, Move = BossMove.Smash, Telegraph = f.Plan(3, Landing.Downbeat),
                Shape = HitShape.Circle(hx, hy, DiveRadiusM), Damage = DiveDamage, BodyMove = HazardBodyMove.DuringTelegraph, ToX = hx, ToY = hy,
            });
            return attack;
        }

        /// <summary>The spiral: <paramref name="rings"/> rings of <see cref="SpiralArms"/> drones out of her body, one a beat, each turned a share of the gap on.</summary>
        int Spiral(BossFight f, int rings)
        {
            ref readonly var b = ref f.Bodies[0];
            Centre(f, out double cx, out double cy);
            int attack = f.NextAttack();
            double turnFrom = f.Random.NextDouble() * 2 * Math.PI, step = 2 * Math.PI / SpiralArms * 0.3;
            for (int r = 0; r < rings; r++)
            {
                var tel = f.Plan(3 + r);
                for (int k = 0; k < SpiralArms; k++)
                {
                    double ang = turnFrom + r * step + 2 * Math.PI * k / SpiralArms;
                    // Each drone flies until it leaves the clearing.
                    double ux = Math.Cos(ang), uy = Math.Sin(ang);
                    double range = Leaves(b.X - cx, b.Y - cy, b.X - cx, b.Y - cy, ux, uy, SpiralRangeM);
                    BossKit.Shot(f, 0, b.X, b.Y, ang, SpiralSpeedMps, SpiralRadiusM, Math.Max(range, 0.5), SpiralDamage, tel, attack, r * SpiralArms + k);
                }
            }
            return attack;
        }

        /// <summary>The net: six rows round the hero's spot, the middle half of each side of a hexagon, open at its corners, closing on it over <see cref="NetCloseBeats"/>.</summary>
        int Net(BossFight f)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            double side = 2 * NetApothemM * Math.Tan(Math.PI / 6), v = NetApothemM / (NetCloseBeats * f.BeatS);
            int attack = f.NextAttack();
            for (int k = 0; k < 6; k++)
            {
                double a = Heading(k), nx = -Math.Cos(a), ny = -Math.Sin(a), px = -ny, py = nx;
                double mx = hx + nx * NetApothemM, my = hy + ny * NetApothemM, h = side / 4;
                Launch(f, attack, k, BossMove.Fans, 3, mx - px * h, my - py * h, mx + px * h, my + py * h, -nx * v, -ny * v,
                    NetApothemM - DroneReachM, NetDamage, HazardFlags.Projectile);
            }
            return attack;
        }

        /// <summary>The dash wave: a gapless row from the wall behind her, faster than walking, on a longer telegraph.</summary>
        int Wall(BossFight f)
        {
            double beatS = f.BeatS;
            double v = Math.Max(WallMinMps, 2 * ApothemM / (3 * beatS));
            return Row(f, Toward(f), false, double.NaN, HazardFlags.DashWave | HazardFlags.Projectile, WallBeats, v, BossMove.Wall, WallDamage);
        }

        /// <summary>A beat's length by <paramref name="tel"/>'s own beats, or the fight's beat now.</summary>
        static double BeatOf(BossFight f, in Telegraph tel) => tel.OnBeat && tel.Beats > 0 ? (tel.Land - tel.Start) / tel.Beats : f.BeatS;

        /// <summary>Her bursts: a honeycomb cell of gems between her and the hero, so collecting them is a walk toward her.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            ref readonly var b = ref f.Lead;
            f.HeroAt(t, out double hx, out double hy);
            double mx = (b.X + hx) / 2, my = (b.Y + hy) / 2;
            const int n = 6;
            for (int k = 0; k < n; k++)
            {
                double a = burst * 0.4 + 2 * Math.PI * k / n;
                into.Add(new GemSpot(mx + 2.5 * SimMath.Cos(a), my + 2.5 * SimMath.Sin(a), 1.0 / n));
            }
        }
    }
}
