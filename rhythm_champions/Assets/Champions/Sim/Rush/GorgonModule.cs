using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Boss Rush boss 8, the Gorgon, in act 2 (boss-rush.md, The roster: <i>Line of sight</i>; ps-8z4.21). A serpent-tailed
    /// gorgon in a ruined temple of columns (arenas.json's <see cref="ArenaId"/>) whose stare turns whatever it sees to stone.
    ///
    /// - <b>The signature, the gaze</b>, taught alone first (twice): she holds still and stares at the hero for a bar,
    ///   striking on the downbeat. Everything in her line of sight inside a wide cone is hit, and nothing behind cover is:
    ///   the cone is laid as its lit slices, each column's shadow cut out of it (<see cref="Sightlines"/>), so the red fill
    ///   on the floor is exactly where her stare reaches and the gaps in it are where to stand. Walking out of the cone works
    ///   too (it is fair from anywhere without cover); cover is the close, calm answer.
    /// - <b>The gaze cracks the stone</b> the hero hid behind: the first strike petrifies a column (it goes grey), the second
    ///   crumbles it (<see cref="CracksToCrumble"/>), so cover runs out and the hero keeps relocating. She glides between the
    ///   temple's heart and three perches (<see cref="Perches"/>) before each gaze, so the shadows fall somewhere new.
    /// - <b>Supporting moves</b>: the <see cref="Kind.Volley"/> (her snake heads spit venom round the hero's spot, cover or
    ///   none: hiding too long is punished), the <see cref="Kind.Coil"/> (a tail lash round her, in place of a volley when
    ///   the hero is close) and the dash wave, the <see cref="Kind.Petrify"/>: a ring of stone rolls out from her across the
    ///   whole temple on a downbeat, through every column, outrunning any walk.
    /// - <b>Phase two</b> below half health, four untouchable beats at the temple's heart: her victims rise as grey statues
    ///   round the temple (fresh cover, which cracks like the columns), her gaze widens, every gaze is followed by a volley
    ///   and the stone ring rolls twice.
    ///
    /// No memory or tracking test: every gaze's lit slices fill on the floor from the beat she starts staring, fixed where
    /// she stands, and the shadows don't move. Everything is scheduled on its beat: the song's, or without a reliable beat
    /// the half-second fallback (boss-rush.md, "Silence breaks nothing").
    /// </summary>
    public sealed class GorgonModule : BossModule
    {
        public const string ModuleId = "gorgon";
        /// <summary>Its arena in arenas.json: a round temple of <see cref="TempleM"/>, columns in two rings, spires by the wall.</summary>
        public const string ArenaId = "gorgonTemple";
        public const double TempleM = 17;
        /// <summary>Its arena's hero and boss starts (the boss starts at the temple's heart).</summary>
        public const double HeroStartX = 0, HeroStartY = -11, BossStartX = 0, BossStartY = 0;
        /// <summary>Her perches, from the temple's centre: she glides out to one and back to the heart, in turn.</summary>
        public static readonly (double X, double Y)[] Perches = { (9, 0), (-4.5, 7.794), (-4.5, -7.794) };
        /// <summary>Where her victims rise as statues in phase two, from the centre (round, <see cref="StatueRadiusM"/>).</summary>
        public static readonly (double X, double Y)[] Statues = { (4.5, 7.794), (-9, 0), (4.5, -7.794), (0, 9.5), (8.227, -4.75), (-8.227, -4.75) };
        public const int StatueCount = 4;
        public const double StatueRadiusM = 0.9;

        // ---- Tuning (boss-rush.md, Fair fights: act 2 warns at least 3 beats) ----
        /// <summary>The gaze: at least a bar to a downbeat, and never under <see cref="GazeMinS"/>; its cone and damage.</summary>
        public const int GazeBeats = 4;
        public const double GazeMinS = 2.0, GazeReachM = 2 * TempleM + 4, GazeHalfDeg = 35, GazeHalfDegTwo = 40, GazeDamage = 20;
        /// <summary>Her stare holds this many beats after it strikes: longer than a dash's invulnerability, so a dash in place doesn't beat it.</summary>
        public const double StareBeats = 1;
        /// <summary>A gaze cracks the cover it was stopped by this many times before it crumbles.</summary>
        public const int CracksToCrumble = 2;
        /// <summary>Her glide to a perch or back to the heart, before each gaze.</summary>
        public const int GlideBeats = 2;

        public const int VolleyCount = 5, VolleyCountTwo = 7;
        public const double VolleyRadiusM = 1.7, VolleySpreadM = 3.6, VolleyDamage = 12;

        /// <summary>The coil: a lash of her tail round her, when the hero is within <see cref="CoilReachM"/> of her body's edge.</summary>
        public const double CoilRadiusM = 6.0, CoilReachM = 4.0, CoilDamage = 14;

        /// <summary>The stone ring, the dash wave.</summary>
        public const int PetrifyBeats = 4;
        public const double RingMps = 16, RingThickM = 0.8, RingDamage = 14, RingReachM = 2 * TempleM + 4;

        public enum Kind : byte { Gaze, Volley, Coil, Petrify }

        /// <summary>Phase one: the gaze taught alone (twice), then its moves in turn, a gaze between each.</summary>
        public static readonly Kind[] Teach = { Kind.Gaze, Kind.Gaze };
        public static readonly Kind[] PhaseOne = { Kind.Volley, Kind.Gaze, Kind.Volley, Kind.Gaze, Kind.Petrify, Kind.Gaze };
        public static readonly Kind[] PhaseTwo = { Kind.Gaze, Kind.Petrify, Kind.Gaze, Kind.Volley, Kind.Gaze };

        /// <summary>One piece of cover a gaze is stopped by: an arena prop, or a statue she raised (an arena obstacle).</summary>
        public struct Cover
        {
            public bool Statue;
            /// <summary>Its prop index, or its obstacle id.</summary>
            public int Id;
            public double X, Y, Radius;
        }

        /// <summary>A shadow a gaze casts: the cover's, its angles either side of the gaze's facing, and where its stone begins.</summary>
        public struct Shadow
        {
            public Cover Cover;
            public double From, To, NearM;
        }

        int turn, rest, perch, perches, gazeAttack = -1, followUps;
        bool taught, glided;
        double lastBeat = double.NegativeInfinity;
        double originX, originY;
        double gazeX, gazeY, gazeFacing, gazeHalf;
        readonly List<Shadow> shadows = new List<Shadow>();
        readonly Dictionary<long, int> cracks = new Dictionary<long, int>();
        readonly List<int> statues = new List<int>();
        /// <summary>Stone cracked through, crumbling as the stare that cracked it ends.</summary>
        readonly List<(Cover Cover, double At)> crumbling = new List<(Cover, double)>();

        public override string Id => ModuleId;
        public override BodySpec[] Bodies => new[] { new BodySpec(ModuleId, BossStartX - HeroStartX, BossStartY - HeroStartY) };
        public override double[] PhasesBelow => new[] { 0.5 };
        public override int TransitionBeats => 4;
        /// <summary>The hiss climbs to the stone crack (boss-rush.md: "hissing that crescendos to a stone crack").</summary>
        public override double TickRise => 0.1;

        /// <summary>The move it made last, and how many (the tests' and the review's).</summary>
        public Kind Last { get; private set; }
        public int Moves { get; private set; }
        /// <summary>The temple's centre (world metres).</summary>
        public double OriginX => originX;
        public double OriginY => originY;
        /// <summary>The attack number of the gaze laid last (-1 before the first): its slices' <see cref="Hazard.Attack"/>.</summary>
        public int GazeAttack => gazeAttack;
        /// <summary>The last gaze's eye, facing and shadows (the client draws its sightlines from them).</summary>
        public double GazeX => gazeX;
        public double GazeY => gazeY;
        public double GazeFacing => gazeFacing;
        /// <summary>The last gaze's half arc, radians.</summary>
        public double GazeHalf => gazeHalf;
        public IReadOnlyList<Shadow> Shadows => shadows;
        /// <summary>The statues she raised (arena obstacle ids).</summary>
        public IReadOnlyList<int> RaisedStatues => statues;

        /// <summary>How many times her gaze has cracked prop <paramref name="prop"/> (or statue obstacle <paramref name="prop"/>).</summary>
        public int Cracks(int prop, bool statue = false) => cracks.TryGetValue(Key(prop, statue), out int n) ? n : 0;

        static long Key(int id, bool statue) => statue ? (1L << 32) | (uint)id : id;

        protected internal override void Begin(BossFight f, double t)
        {
            var arena = f.Sim.Arena;
            if (arena != null && arena.Solid)
            {
                originX = arena.OriginX;
                originY = arena.OriginY;
                ref var b = ref f.Body(0);
                b.X = arena.BossStartX;
                b.Y = arena.BossStartY;
            }
            else
            {
                originX = f.CentreX - HeroStartX;
                originY = f.CentreY - HeroStartY;
            }
            perch = -1;
            glided = true;
            rest = 1;
        }

        protected internal override void PhaseBegins(BossFight f, int phase, double t, double until)
        {
            // Stone cracked through by a stare the transition cut short crumbles now.
            foreach (var c in crumbling) Crumble(f, c.Cover, t);
            crumbling.Clear();
            // She coils at the temple's heart while her victims rise round it.
            f.MoveBody(0, originX, originY, t, t + Math.Min(until - t, GlideBeats * f.BeatS), true);
            perch = -1;
            RaiseStatues(f, t, until);
            turn = 0;
            followUps = 0;
            glided = true;
            rest = 1;
        }

        protected internal override void Tick(BossFight f, double end)
        {
            for (int i = crumbling.Count - 1; i >= 0; i--)
            {
                if (end < crumbling[i].At - 1e-9) continue;
                Crumble(f, crumbling[i].Cover, crumbling[i].At);
                crumbling.RemoveAt(i);
            }
            if (f.Sim.State.Beat != Reliability.Reliable && end >= lastBeat + f.BeatS - 1e-9)
            {
                lastBeat = end;
                OnBeat(f, end);
            }
            // Between moves she keeps her eyes on the hero; staring, she holds the gaze where it was laid.
            if (f.Hazards.Length == 0 && !f.Moving(0))
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
            // Phase two's volley on the heels of each gaze.
            if (followUps > 0)
            {
                followUps--;
                Use(f, Kind.Volley, t, hx, hy);
                return;
            }
            if (--rest > 0) return;
            Kind kind;
            if (f.Phase >= 2) kind = PhaseTwo[turn % PhaseTwo.Length];
            else if (!taught) kind = Teach[turn];
            else kind = PhaseOne[turn % PhaseOne.Length];
            // A gaze waits for her glide to its spot: the glide first, the stare on the beat she has arrived.
            if (kind == Kind.Gaze && !glided)
            {
                Glide(f, t);
                return;
            }
            turn++;
            if (f.Phase < 2 && !taught && turn >= Teach.Length) { taught = true; turn = 0; }
            Use(f, kind, t, hx, hy);
            rest = f.Phase >= 2 ? 1 : 2;
        }

        void Use(BossFight f, Kind kind, double t, double hx, double hy)
        {
            ref readonly var b = ref f.Bodies[0];
            if (kind == Kind.Volley)
            {
                // Close enough to lash, she lashes instead of spitting.
                double reach = f.TypeOf(0).RadiusM + f.HeroRadius + CoilReachM;
                if ((hx - b.X) * (hx - b.X) + (hy - b.Y) * (hy - b.Y) <= reach * reach) kind = Kind.Coil;
            }
            Last = kind;
            Moves++;
            switch (kind)
            {
                case Kind.Gaze:
                    Gaze(f, t, hx, hy);
                    glided = false;
                    if (f.Phase >= 2) followUps = 1;
                    break;
                case Kind.Volley: BossKit.Boulders(f, 0, f.Phase >= 2 ? VolleyCountTwo : VolleyCount, VolleyRadiusM, VolleySpreadM, VolleyDamage, 3); break;
                case Kind.Coil:
                {
                    var tel = f.Plan(3, Landing.Downbeat);
                    f.Add(new Hazard
                    {
                        Attack = f.NextAttack(), Body = 0, Move = BossMove.Tail, Telegraph = tel, Shape = HitShape.Circle(b.X, b.Y, CoilRadiusM),
                        Damage = CoilDamage,
                    });
                    break;
                }
                case Kind.Petrify:
                    BossKit.Wave(f, 0, b.X, b.Y, RingReachM, RingMps, RingThickM, RingDamage, PetrifyBeats, fronts: f.Phase >= 2 ? 2 : 1, move: BossMove.Wave);
                    break;
            }
        }

        /// <summary>Her glide before a gaze: out to the next perch from the heart, or back to the heart from a perch.</summary>
        void Glide(BossFight f, double t)
        {
            double tx = originX, ty = originY;
            if (perch < 0)
            {
                perch = perches++ % Perches.Length;
                tx += Perches[perch].X;
                ty += Perches[perch].Y;
            }
            else perch = -1;
            f.MoveBody(0, tx, ty, t, t + GlideBeats * f.BeatS, true);
            glided = true;
        }

        // ---- The gaze ----

        /// <summary>
        /// The gaze: from where she stands at the hero's spot (<paramref name="hx"/>, <paramref name="hy"/>), a cone of
        /// <see cref="GazeHalfDeg"/> either side laid as its lit slices, every cover's shadow cut out, filling over a bar to
        /// the downbeat.
        /// </summary>
        void Gaze(BossFight f, double t, double hx, double hy)
        {
            ref readonly var b = ref f.Bodies[0];
            double dx = hx - b.X, dy = hy - b.Y;
            double facing = dx * dx + dy * dy > 1e-9 ? SimMath.Atan2(dy, dx) : b.Facing;
            f.Face(0, b.X + SimMath.Cos(facing), b.Y + SimMath.Sin(facing));
            double half = (f.Phase >= 2 ? GazeHalfDegTwo : GazeHalfDeg) * Math.PI / 180;
            int beats = Math.Max(GazeBeats, (int)Math.Ceiling(GazeMinS / f.BeatS - 1e-9));
            var tel = f.Plan(beats, Landing.Downbeat);
            gazeX = b.X;
            gazeY = b.Y;
            gazeFacing = facing;
            gazeHalf = half;
            Sightlines(b.X, b.Y, facing, half, GazeReachM, Covers(f), shadows);
            int attack = gazeAttack = f.NextAttack();
            var slices = new List<(double From, double To, double Reach)>();
            Slices(half, GazeReachM, shadows, slices);
            for (int k = 0; k < slices.Count; k++)
            {
                var s = slices[k];
                double mid = facing + (s.From + s.To) / 2;
                // Out to the temple's wall at most (its farthest point across the slice, and a little more).
                double wall = 0;
                for (int j = 0; j <= 4; j++) wall = Math.Max(wall, Exit(b.X - originX, b.Y - originY, facing + s.From + (s.To - s.From) * j / 4));
                s.Reach = Math.Min(s.Reach, wall + 0.5);
                f.Add(new Hazard
                {
                    Attack = attack, Body = 0, Part = k, Move = BossMove.Breath, Telegraph = tel,
                    Shape = HitShape.Cone(b.X, b.Y, SimMath.Cos(mid), SimMath.Sin(mid), s.Reach, (s.To - s.From) / 2),
                    Damage = GazeDamage, ActiveS = StareBeats * f.BeatS, Flags = k == 0 ? HazardFlags.Signature : HazardFlags.None,
                });
            }
        }

        /// <summary>The cover standing now: the arena's props that block bodies, and the statues she raised.</summary>
        List<Cover> Covers(BossFight f)
        {
            var covers = new List<Cover>();
            var arena = f.Sim.Arena;
            if (arena == null || !arena.Solid) return covers;
            for (int i = 0; i < arena.PropCount; i++)
            {
                ref readonly var p = ref arena.Prop(i);
                if (!p.Standing || !p.Blocks) continue;
                var k = arena.Rules.Props[p.Kind];
                // A box casts no wider a shadow than the disc inside it.
                double r = k.Shape == FootprintShape.Circle ? k.Radius : Math.Min(k.HalfX, k.HalfY);
                covers.Add(new Cover { Id = i, X = p.X, Y = p.Y, Radius = r });
            }
            foreach (int id in statues)
            {
                var o = arena.Obstacle(id);
                if (!o.Standing) continue;
                o.Shape.Centre(out double cx, out double cy);
                // Its polygon's inscribed circle (a hexagon's).
                covers.Add(new Cover { Statue = true, Id = id, X = cx, Y = cy, Radius = StatueRadiusM * Math.Cos(Math.PI / 6) });
            }
            return covers;
        }

        /// <summary>
        /// The shadows a gaze from (<paramref name="x"/>, <paramref name="y"/>) facing <paramref name="facing"/> casts within
        /// <paramref name="half"/> either side: each cover's, its angles relative to the facing and where its stone begins.
        /// </summary>
        public static void Sightlines(double x, double y, double facing, double half, double reach, List<Cover> covers, List<Shadow> into)
        {
            into.Clear();
            foreach (var c in covers)
            {
                double dx = c.X - x, dy = c.Y - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= c.Radius + 0.05 || d - c.Radius >= reach) continue;
                double a = Wrap(SimMath.Atan2(dy, dx) - facing), w = SimMath.Asin(c.Radius / d);
                double from = Math.Max(-half, a - w), to = Math.Min(half, a + w);
                if (to - from < 1e-6) continue;
                into.Add(new Shadow { Cover = c, From = from, To = to, NearM = d - c.Radius });
            }
        }

        /// <summary>
        /// A gaze's lit slices: [-<paramref name="half"/>, <paramref name="half"/>] cut at every shadow's edge, each slice
        /// reaching <paramref name="reach"/> or, in a shadow, the nearest stone; neighbours with the same reach merged.
        /// </summary>
        public static void Slices(double half, double reach, List<Shadow> shadows, List<(double From, double To, double Reach)> into)
        {
            into.Clear();
            var cuts = new List<double> { -half, half };
            foreach (var s in shadows) { cuts.Add(s.From); cuts.Add(s.To); }
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                if (b - a < 1e-6) continue;
                double mid = (a + b) / 2, r = reach;
                foreach (var s in shadows) if (mid > s.From && mid < s.To) r = Math.Min(r, s.NearM);
                if (into.Count > 0 && Math.Abs(into[into.Count - 1].Reach - r) < 1e-9 && Math.Abs(into[into.Count - 1].To - a) < 1e-9)
                    into[into.Count - 1] = (into[into.Count - 1].From, b, r);
                else into.Add((a, b, r));
            }
        }

        /// <summary>How far from (<paramref name="x"/>, <paramref name="y"/>) (from the centre) at <paramref name="angle"/> the temple's wall stands.</summary>
        static double Exit(double x, double y, double angle)
        {
            double ux = SimMath.Cos(angle), uy = SimMath.Sin(angle), pu = x * ux + y * uy, c = x * x + y * y - TempleM * TempleM, disc = pu * pu - c;
            return disc < 0 ? 0 : Math.Max(0, -pu + Math.Sqrt(disc));
        }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        protected internal override void Landed(BossFight f, in Hazard h, double t)
        {
            if (h.Attack != gazeAttack || h.Part != 0) return;
            // The stone the hero hid behind cracks: whichever cover's shadow holds it (the nearest, if they overlap).
            f.HeroAt(t, out double hx, out double hy);
            double dx = hx - gazeX, dy = hy - gazeY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) return;
            double a = Wrap(SimMath.Atan2(dy, dx) - gazeFacing);
            int best = -1;
            for (int i = 0; i < shadows.Count; i++)
            {
                var s = shadows[i];
                if (a < s.From || a > s.To || d < s.NearM) continue;
                if (best < 0 || s.NearM < shadows[best].NearM) best = i;
            }
            if (best >= 0) Crack(shadows[best].Cover, h.Until);
        }

        /// <summary>A crack in <paramref name="c"/>; cracked through, it crumbles at <paramref name="until"/>, when the stare ends.</summary>
        void Crack(in Cover c, double until)
        {
            long key = Key(c.Id, c.Statue);
            cracks.TryGetValue(key, out int n);
            cracks[key] = ++n;
            if (n == CracksToCrumble) crumbling.Add((c, until));
        }

        void Crumble(BossFight f, in Cover c, double t)
        {
            var arena = f.Sim.Arena;
            if (arena == null || !arena.Solid) return;
            if (c.Statue) arena.Clear(c.Id, t);
            else
            {
                double dx = c.X - gazeX, dy = c.Y - gazeY, d = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
                arena.Crumble(c.Id, t, BreakCause.Boss, dx / d, dy / d);
            }
        }

        // ---- Phase two ----

        /// <summary>Her victims rise as statues round the temple across the transition, clear of the hero and of her.</summary>
        void RaiseStatues(BossFight f, double t, double until)
        {
            var arena = f.Sim.Arena;
            if (arena == null || !arena.Solid) return;
            f.HeroAt(t, out double hx, out double hy);
            int raised = 0;
            foreach (var s in Statues)
            {
                if (raised >= StatueCount) break;
                double x = originX + s.X, y = originY + s.Y;
                if ((x - hx) * (x - hx) + (y - hy) * (y - hy) < 3.0 * 3.0) continue;
                if (!arena.Walkable(x, y, StatueRadiusM + 0.1)) continue;
                statues.Add(arena.Grow(ArenaShape.Circle(x, y, StatueRadiusM, 6), ArenaObstacleKind.Wall, t, until - t));
                raised++;
            }
        }

        /// <summary>Its bursts: a gem in the shadow behind each of the covers nearest her (hiding pays), else the ring.</summary>
        protected internal override void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            double bx = f.Lead.X, by = f.Lead.Y;
            var covers = Covers(f);
            covers.Sort((p, q) => ((p.X - bx) * (p.X - bx) + (p.Y - by) * (p.Y - by)).CompareTo((q.X - bx) * (q.X - bx) + (q.Y - by) * (q.Y - by)));
            const int n = 6;
            int count = 0;
            var arena = f.Sim.Arena;
            for (int i = 0; i < covers.Count && count < n; i++)
            {
                var c = covers[i];
                double dx = c.X - bx, dy = c.Y - by, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) continue;
                double out_ = c.Radius + 1.3;
                double x = c.X + dx / d * out_, y = c.Y + dy / d * out_;
                if (arena != null && !arena.Inside(x, y)) continue;
                into.Add(new GemSpot(x, y, 1.0 / n));
                count++;
            }
            if (count == n) return;
            into.Clear();
            base.ShedSpots(f, burst, t, into);
        }
    }
}
