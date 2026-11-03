using System;

namespace Champions.Sim
{
    /// <summary>How a mob is moving (<see cref="Mob.Motion"/>).</summary>
    public enum Motion : byte
    {
        /// <summary>Standing.</summary>
        Still = 0,
        /// <summary>A step on its beat: a sharp ease-out (<see cref="Horde.Ease"/>), settled at <see cref="Mob.End"/>.</summary>
        Step = 1,
        /// <summary>Gliding with no reliable beat: straight, over a segment of <see cref="Horde.GlideTicks"/> ticks.</summary>
        Glide = 2,
        /// <summary>A big ork's charge: the same ease-out as a step, over the charge's run.</summary>
        Charge = 3,
    }

    /// <summary>Where an elite's or mushroom's move stands (<see cref="Mob.Stage"/>).</summary>
    public enum MoveStage : byte
    {
        None = 0,
        /// <summary>Its telegraph fills; it stands.</summary>
        Telegraph = 1,
        /// <summary>A charge runs.</summary>
        Acting = 2,
    }

    /// <summary>
    /// A mob as the horde moves it, by enemy id (<see cref="Horde.Mobs"/>): its type and its current motion,
    /// from (FromX, FromY) at <see cref="Start"/> to (ToX, ToY) at <see cref="End"/> (<see cref="At"/>), its
    /// elite move and the rest of the horde's bookkeeping. The client draws a mob's position from it at any
    /// song time; before <see cref="Start"/> the mob stood at From, since every step settles before the next
    /// (a glide segment's From is where the one before it ended).
    /// </summary>
    public struct Mob
    {
        /// <summary>Its cast index (<see cref="EnemyRules.Cast"/>).</summary>
        public int Kind;
        /// <summary>The horde spawned it and it lives: the horde moves it.</summary>
        public bool Owned;
        public Motion Motion;
        public double FromX, FromY, ToX, ToY, Start, End;
        /// <summary>A formed line's march: its heading (a unit vector) until <see cref="HeadUntil"/>; then it closes on the hero.</summary>
        public double HeadX, HeadY, HeadUntil;
        public MoveStage Stage;
        /// <summary>The move's telegraph, while <see cref="Stage"/> is <see cref="MoveStage.Telegraph"/>.</summary>
        public Telegraph Telegraph;
        /// <summary>The move's aim, a unit vector (a charge's line), fixed when its telegraph starts.</summary>
        public double AimX, AimY;
        /// <summary>No new move before this.</summary>
        public double NextMoveAt;
        /// <summary>The latest touch bite, for the beatless bites' spacing.</summary>
        public double LastBite;
        /// <summary>
        /// Steering round a blocked way (structures, the crowd): the turn its move takes now (+1, -1, or 0 going
        /// straight), the side it turns to when blocked (kept, so it keeps going round), the turned moves left,
        /// and its turned moves in a row that were blocked too.
        /// </summary>
        public int Detour, Side, DetourLeft, Blocked;

        /// <summary>Where it is at song time <paramref name="t"/> in its current motion.</summary>
        public readonly void At(double t, out double x, out double y)
        {
            if (t >= End || End <= Start)
            {
                x = ToX;
                y = ToY;
                return;
            }
            if (t <= Start)
            {
                x = FromX;
                y = FromY;
                return;
            }
            double u = (t - Start) / (End - Start);
            double f = Motion == Motion.Glide ? u : Horde.Ease(u);
            x = FromX + (ToX - FromX) * f;
            y = FromY + (ToY - FromY) * f;
        }

        public readonly bool Settled(double t) => t >= End - 1e-9;
    }

    /// <summary>
    /// The horde (design.md, "The Horde"; plan.md, "Systems"): the mobs' steps on their beats, gliding without
    /// a beat, dark gaps, steering round structures, touch bites, the elites' and mushrooms' moves
    /// (<see cref="EliteMoves"/>) and the spawner (<see cref="Spawner"/>, with a level). It moves the
    /// enemies it spawned in the one enemy store and its grid (<see cref="Enemies"/>), where a mob's
    /// <see cref="Enemy.Type"/> is its cast index plus one; placed dummies (type 0) and bosses are not its.
    ///
    /// - <b>Steps</b>: a beat fired with a reliable beat is a step moment, as is the half beat after it. A mob
    ///   steps at the moments of its <see cref="EnemyType.StepBeats"/> (half beats; every beat; every other
    ///   beat, counted from the bar), its motion starting on the moment with a sharp ease-out and settling
    ///   within <see cref="EnemyRules.SettleShare"/> of its interval. A step is its speed times its interval
    ///   (the step beats times the tempo, <see cref="BeatServices.TempoAt"/>), so its average speed never
    ///   depends on the tempo. Every mob steps on its moments wherever it is: nothing waits for the beat wave.
    /// - <b>Gliding</b>: with no reliable beat a mob moves straight at the same average speed in short
    ///   segments of <see cref="GlideTicks"/> ticks, a slice of the mobs planning theirs each tick. It never
    ///   stops.
    /// - <b>Dark gaps</b>: mobs move nearest the hero first, each going until it would come within its
    ///   radii's sum times one plus <see cref="EnemyRules.GapShare"/> of a neighbour's landing (or standing
    ///   place) and sliding round it. So the crowd packs round the hero as lit shapes with black between them,
    ///   never one mass, and flows round its front instead of queueing.
    /// - <b>Structures</b> stop a mob, which slides along them. Mobs never break them.
    /// - <b>Steering</b>: a mob whose move is blocked (by structures or the crowd) turns aside 60 degrees on its
    ///   next moves, trying the other side if that is blocked too, until it moves freely again.
    /// - <b>Touch</b>: a step landing against the hero bites on its moment; gliding, a touching mob bites every
    ///   <see cref="EnemyRules.TouchBeatlessS"/>. Bites go through <see cref="HeroHealth.Hurt"/>.
    /// - <b>Holds</b>: while the world is held nothing moves or attacks; becoming held stops every motion where
    ///   it is, cancels the moves' telegraphs and drops the spores in flight.
    /// - A mob left <see cref="LevelRules.DespawnM"/> behind goes (<see cref="SimEventKind.EnemyGone"/>), and
    ///   the ring brings another.
    ///
    /// The beats are taken when they fire and acted on at the tick's end, in time order, from the hero's
    /// path as walked by then (<see cref="Simulation.HeroWas"/>); motions start at their exact times.
    /// </summary>
    public sealed class Horde
    {
        /// <summary>A glide segment's ticks: a slice of the mobs plans its next segment each tick.</summary>
        public const int GlideTicks = 6;
        /// <summary>How many times a mob standing inside a neighbour's gap is pushed clear before it moves.</summary>
        const int GapTries = 4;
        /// <summary>How many times a move slides round what it meets.</summary>
        const int Bends = 3;
        /// <summary>A move meeting a neighbour head on slips aside this share of the rest of its way.</summary>
        const double SlipShare = 0.5;
        /// <summary>A blocked mob turns this many moves aside.</summary>
        const int DetourMoves = 4;
        /// <summary>Room for a landing's pushes beyond its neighbours' search.</summary>
        const double PushRoomM = 0.5;

        /// <summary>A step moment: a beat, or the half beat after one.</summary>
        struct Moment
        {
            public double T;
            /// <summary>The beat (the half beat's beat), and the tempo's period there.</summary>
            public int Beat;
            public double Period;
            public bool Half;
        }

        readonly Simulation sim;
        readonly EnemyRules rules;
        readonly Enemies enemies;
        readonly BeatServices beats;
        readonly double heroRadius;
        Mob[] mobs;
        readonly StructList<Moment> moments = new StructList<Moment>(16);
        readonly StructList<int> near;
        struct Neighbour { public double X, Y, Gap, GapSquared; }
        Neighbour[] neighbours = new Neighbour[257];
        // IDs are only needed for an exactly coincident push; keep hot geometry in 32-byte entries.
        int[] neighbourIds = new int[257];
        struct LandingBody { public double X, Y, Radius; }
        LandingBody[] landings = new LandingBody[16];
        // A moment's movers, nearest the hero first: distance in tenths of a millimetre above the id's bits.
        long[] order;
        int orderCount;
        const int IdBits = 21;
        long ticks;

        internal Horde(Simulation sim, SimConfig config, Enemies enemies)
        {
            this.sim = sim;
            this.enemies = enemies;
            rules = config.Enemies;
            beats = sim.Beats;
            heroRadius = config.Hero.RadiusM;
            int capacity = Math.Max(16, config.EnemyCapacity);
            mobs = new Mob[capacity];
            order = new long[capacity];
            near = new StructList<int>(256);
            Moves = new EliteMoves(sim, this, rules, config.Beat);
            if (config.Level != null) Spawner = new Spawner(sim, this, rules, config.Level);
        }

        /// <summary>The ease every step and charge follows: fast off the beat, settling at u = 1.</summary>
        public static double Ease(double u)
        {
            double v = 1 - u;
            return 1 - v * v * v * v;
        }

        public EnemyRules Rules => rules;

        /// <summary>Every enemy slot's mob, by enemy id (<see cref="Mob.Owned"/> marks the horde's).</summary>
        public ReadOnlySpan<Mob> Mobs => new ReadOnlySpan<Mob>(mobs, 0, enemies.Count);

        /// <summary>The horde's mobs alive now.</summary>
        public int Alive { get; private set; }

        /// <summary>Mobs spawned this run.</summary>
        public int Spawned { get; private set; }

        /// <summary>The level's spawner; null without a level.</summary>
        public Spawner Spawner { get; }

        public EliteMoves Moves { get; }

        /// <summary>Is enemy <paramref name="id"/> one of the horde's, alive?</summary>
        public bool Owns(int id) => id >= 0 && id < enemies.Count && mobs[id].Owned && enemies[id].Alive;

        /// <summary>Where enemy <paramref name="id"/> was at song time <paramref name="t"/> (its current motion; a placed enemy where it stands).</summary>
        public void PositionAt(int id, double t, out double x, out double y)
        {
            if (Owns(id))
            {
                mobs[id].At(t, out x, out y);
                return;
            }
            x = enemies[id].X;
            y = enemies[id].Y;
        }

        internal ref Mob Edit(int id) => ref mobs[id];

        // Fixed-store scans only: a spawn can resize the backing array.
        internal Span<Mob> EditMobs => new Span<Mob>(mobs, 0, enemies.Count);

        internal Enemies Enemies => enemies;

        internal double HeroRadius => heroRadius;

        /// <summary>
        /// A new mob of cast type <paramref name="kind"/> standing at (<paramref name="x"/>, <paramref name="y"/>),
        /// with <paramref name="health"/> (the spawner's toughness; NaN: the type's at the start).
        /// </summary>
        internal int Spawn(int kind, double x, double y, double health = double.NaN)
        {
            var type = rules.Cast[kind];
            if (type.IsBoss) throw new ArgumentException(type.Id + " is a boss: the bosses' bead spawns it");
            if (double.IsNaN(health)) health = type.Toughness;
            int id = enemies.Spawn(sim, x, y, type.RadiusM, health, kind + 1);
            if (id >= mobs.Length) Grow(enemies.Count * 2);
            double now = sim.Now;
            mobs[id] = new Mob
            {
                Kind = kind, Owned = true, Motion = Motion.Still,
                FromX = x, FromY = y, ToX = x, ToY = y, Start = now, End = now,
                LastBite = double.NegativeInfinity, NextMoveAt = now,
            };
            Alive++;
            Spawned++;
            return id;
        }

        /// <summary>Mob <paramref name="id"/> was left behind: it goes without a shatter.</summary>
        internal void Despawn(int id, double t)
        {
            ref var m = ref mobs[id];
            m.At(t, out double x, out double y);
            Moves.Dropped(id, t);
            m.Owned = false;
            enemies.Kill(id);
            Alive--;
            sim.Emit(SimEvent.EnemyGone(t, id, x, y));
        }

        void Grow(int capacity)
        {
            Array.Resize(ref mobs, capacity);
            Array.Resize(ref order, capacity);
        }

        // ---- The beat ----

        /// <summary>Beat <paramref name="beat"/> fired at <paramref name="t"/>: with a reliable beat, a step moment and the half beat after it.</summary>
        internal void Beat(double t, int beat)
        {
            if (sim.State.Held || sim.State.Beat != Reliability.Reliable) return;
            double period = beats.TempoAt(beat);
            Queue(new Moment { T = t, Beat = beat, Period = period });
            double half = beats.BeatTime(beat + 0.5);
            if (double.IsNaN(half) || half <= t) half = t + period / 2;
            Queue(new Moment { T = half, Beat = beat, Period = period, Half = true });
        }

        /// <summary>Adds a moment in time order.</summary>
        void Queue(in Moment moment)
        {
            moments.Add(moment);
            for (int i = moments.Count - 1; i > 0 && moments[i - 1].T > moment.T; i--)
            {
                moments[i] = moments[i - 1];
                moments[i - 1] = moment;
            }
        }

        /// <summary>The world was just held or let go: becoming held, every motion stops where it is and every move is called off.</summary>
        internal void HeldChanged()
        {
            if (!sim.State.Held) return;
            if (!sim.Dashes.Rules.LegacyForms)
            {
                for (int id = 0; id < enemies.Count; id++)
                {
                    if (!Owns(id)) continue;
                    ref var m = ref mobs[id];
                    if (m.Stage == MoveStage.Telegraph) m.Telegraph = sim.Telegraphs.Freeze(m.Telegraph);
                }
                return;
            }
            double now = sim.Now;
            moments.Clear();
            int n = enemies.Count;
            for (int id = 0; id < n; id++)
            {
                if (!Owns(id)) continue;
                ref var m = ref mobs[id];
                m.At(now, out double x, out double y);
                Stand(ref m, x, y, now);
                ref var e = ref enemies.Edit(id);
                e.X = x;
                e.Y = y;
            }
            Moves.CallOff(now);
            Moved();
        }

        internal void Resume(double elapsed)
        {
            moments.Clear();
            for (int id = 0; id < enemies.Count; id++)
            {
                if (!Owns(id)) continue;
                ref var m = ref mobs[id];
                m.Start += elapsed; m.End += elapsed; m.NextMoveAt += elapsed;
                m.LastBite += elapsed; m.HeadUntil += elapsed;
                if (m.Stage == MoveStage.Telegraph) m.Telegraph = sim.Telegraphs.Resume(m.Telegraph, elapsed);
            }
            Moves.Resume(elapsed);
        }

        static void Stand(ref Mob m, double x, double y, double t)
        {
            m.FromX = m.ToX = x;
            m.FromY = m.ToY = y;
            m.Start = m.End = t;
            m.Motion = Motion.Still;
        }

        // ---- The tick ----

        /// <summary>The tick ending at <paramref name="end"/>: spawning, step moments, gliding, the moves, spores, bites and despawns.</summary>
        internal void Tick(double end)
        {
            ticks++;
            Forget();
            if (sim.State.Held)
            {
                moments.Clear();
                return;
            }
            Spawner?.Tick(end);
            bool beatless = sim.State.Beat != Reliability.Reliable;
            if (beatless) moments.Clear();
            while (moments.Count > 0 && moments[0].T <= end + 1e-9)
            {
                var moment = moments[0];
                moments.RemoveFront(1);
                Step(moment);
            }
            if (beatless) Glide(end);
            Publish(end);
            Moves.Tick(end);
            if (Spawner != null) LeaveBehind(end);
        }

        /// <summary>
        /// Mobs killed since the last tick (by the hero's blows, a boss's entrance) are no longer the horde's, nor
        /// are their slots if something else spawned into them since.
        /// </summary>
        void Forget()
        {
            var all = enemies.All;
            var actors = new Span<Mob>(mobs, 0, all.Length);
            for (int id = 0; id < actors.Length; id++)
            {
                ref var m = ref actors[id];
                if (!m.Owned) continue;
                ref readonly var e = ref all[id];
                if (e.Alive && !e.Boss && e.Type == m.Kind + 1) continue;
                Moves.Dropped(id, sim.Now);
                m.Owned = false;
                Alive--;
            }
        }

        /// <summary>Writes every moving mob's position at <paramref name="end"/> into the store.</summary>
        void Publish(double end)
        {
            int n = enemies.Count;
            bool any = false;
            double from = end - Simulation.TickSeconds;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref mobs[id];
                if (!m.Owned || m.End < from - 1e-9) continue;
                m.At(end, out double x, out double y);
                ref var e = ref enemies.Edit(id);
                if (e.X == x && e.Y == y) continue;
                e.X = x;
                e.Y = y;
                any = true;
            }
            if (any) enemies.Moved();
        }

        internal void Moved() => enemies.Moved();

        // ---- Steps ----

        /// <summary>Does a mob stepping every <paramref name="stepBeats"/> beats step at <paramref name="moment"/>?</summary>
        bool StepsAt(double stepBeats, in Moment moment)
        {
            if (stepBeats == 0.5) return true;
            if (moment.Half) return false;
            if (stepBeats == 1) return true;
            // Every other beat, counted from the bar (or from the first beat with no bars yet).
            int origin = beats.FirstAfterLock(moment.T);
            int since = origin == int.MaxValue ? moment.Beat : moment.Beat - origin;
            return since % 2 == 0;
        }

        void Step(in Moment moment)
        {
            double t = moment.T;
            sim.HeroWas(t, out double hx, out double hy);
            orderCount = 0;
            int n = enemies.Count;
            double longest = 0;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref mobs[id];
                if (!m.Owned || m.Stage != MoveStage.None) continue;
                var type = rules.Cast[m.Kind];
                if (!StepsAt(type.StepBeats, moment)) continue;
                Queue(id, hx, hy);
                longest = Math.Max(longest, type.SpeedMps * type.StepBeats * moment.Period);
            }
            if (orderCount == 0) return;
            Array.Sort(order, 0, orderCount);
            PrepareLandings();
            double touch = rules.TouchSlackM;
            for (int k = 0; k < orderCount; k++)
            {
                int id = (int)(order[k] & ((1L << IdBits) - 1));
                ref var m = ref mobs[id];
                var type = rules.Cast[m.Kind];
                double interval = type.StepBeats * moment.Period;
                m.At(t, out double x, out double y);
                Aim(ref m, type, x, y, hx, hy, t, type.SpeedMps * interval, out double qx, out double qy);
                Land(id, ref m, type.RadiusM, x, y, ref qx, ref qy, hx, hy, longest);
                m.FromX = x;
                m.FromY = y;
                m.ToX = qx;
                m.ToY = qy;
                landings[id].X = qx;
                landings[id].Y = qy;
                m.Start = t;
                m.End = t + rules.SettleShare * interval;
                m.Motion = Motion.Step;
                double hdx = qx - hx, hdy = qy - hy, reach = heroRadius + type.RadiusM + touch;
                if (hdx * hdx + hdy * hdy <= reach * reach) Bite(id, ref m, t, qx, qy);
            }
        }

        /// <summary>Adds mob <paramref name="id"/> to the moment's movers, keyed by its distance from the hero.</summary>
        void Queue(int id, double hx, double hy)
        {
            ref readonly var e = ref enemies[id];
            double dx = e.X - hx, dy = e.Y - hy;
            long key = (long)(Math.Sqrt(dx * dx + dy * dy) * 1e4);
            order[orderCount++] = key << IdBits | (long)id;
        }

        /// <summary>
        /// Settles a move from (<paramref name="x"/>, <paramref name="y"/>) to (<paramref name="qx"/>, <paramref name="qy"/>):
        /// clear of the others' landings (<see cref="KeepGaps"/>), then through the structures.
        /// </summary>
        void Land(int id, ref Mob m, double r, double x, double y, ref double qx, ref double qy, double hx, double hy, double slack)
        {
            double want = (qx - x) * (qx - x) + (qy - y) * (qy - y);
            KeepGaps(id, r, x, y, ref qx, ref qy, hx, hy, slack);
            Structures(r, x, y, ref qx, ref qy);
            Steer(id, ref m, want, (qx - x) * (qx - x) + (qy - y) * (qy - y));
        }

        /// <summary>
        /// Steering round a blocked way: a straight move that made under a quarter of its way (squared lengths
        /// <paramref name="want"/>, <paramref name="made"/>) turns its next <see cref="DetourMoves"/> moves aside, to
        /// the mob's side; turned moves that are blocked too, twice running, switch its side.
        /// </summary>
        static void Steer(int id, ref Mob m, double want, double made)
        {
            if (want < 1e-12) return;
            if (m.Detour != 0)
            {
                if (made < 0.0625 * want && ++m.Blocked >= 2)
                {
                    m.Side = -m.Side;
                    m.Detour = m.Side;
                    m.Blocked = 0;
                    m.DetourLeft = DetourMoves;
                    return;
                }
                if (made >= 0.0625 * want) m.Blocked = 0;
                if (--m.DetourLeft <= 0) m.Detour = 0;
                return;
            }
            if (made >= 0.25 * want) return;
            if (m.Side == 0) m.Side = (id & 1) == 0 ? 1 : -1;
            m.Detour = m.Side;
            m.DetourLeft = DetourMoves;
            m.Blocked = 0;
        }

        // Between planning moves, only each mover's landing changes. Keep the compact
        // geometry together rather than re-reading the full mob and enemy for every neighbour.
        void PrepareLandings()
        {
            int n = enemies.Count;
            if (landings.Length < n) Array.Resize(ref landings, mobs.Length);
            var all = enemies.All;
            for (int id = 0; id < n; id++)
            {
                ref readonly var e = ref all[id];
                ref readonly var m = ref mobs[id];
                landings[id] = new LandingBody
                {
                    X = m.Owned ? m.ToX : e.X,
                    Y = m.Owned ? m.ToY : e.Y,
                    Radius = e.Radius,
                };
            }
        }

        /// <summary>
        /// Dark gaps: the move of mob <paramref name="id"/> from (<paramref name="px"/>, <paramref name="py"/>) to
        /// (<paramref name="qx"/>, <paramref name="qy"/>), kept clear of every neighbour's landing (<see cref="Landing"/>)
        /// by their radii's sum times one plus the gap share, and of the hero's body. It first leaves any neighbour
        /// it stands inside (a crowded spawn), then goes along its move until it would come inside one, and slides
        /// round it with the rest, up to <see cref="Bends"/> times. <paramref name="slack"/> is how far a
        /// neighbour's landing may lie from where the grid holds it.
        /// </summary>
        void KeepGaps(int id, double r, double px, double py, ref double qx, ref double qy, double hx, double hy, double slack)
        {
            double gap = 1 + rules.GapShare;
            double vx = qx - px, vy = qy - py, length = Math.Sqrt(vx * vx + vy * vy);
            // These distinct mover circles are invalidated by publication after the batch.
            enemies.Near((px + qx) / 2, (py + qy) / 2, length / 2 + r * gap + enemies.MaxRadius * rules.GapShare + slack + PushRoomM, near, cacheCircle: false);
            // Landings stay fixed throughout this move's pushes and bends. Snapshot once instead
            // of resolving mob ownership, position and radius again for every collision pass.
            if (neighbours.Length <= near.Count)
            {
                Array.Resize(ref neighbours, near.Capacity + 1);
                Array.Resize(ref neighbourIds, near.Capacity + 1);
            }
            // Reserve zero for the hero in the slide list; gap pushes only visit enemy landings.
            int neighbourCount = 1;
            var candidates = near.AsSpan();
            for (int j = 0; j < candidates.Length; j++)
            {
                int other = candidates[j];
                if (other == id) continue;
                ref readonly var landing = ref landings[other];
                double min = (r + landing.Radius) * gap;
                neighbourIds[neighbourCount] = other;
                neighbours[neighbourCount++] = new Neighbour { X = landing.X, Y = landing.Y, Gap = min, GapSquared = min * min };
            }
            var bodies = neighbours;
            double x = px, y = py;
            for (int attempt = 0; attempt < GapTries; attempt++)
            {
                double startX = x, startY = y;
                bool clear = true;
                for (int j = 1; j < neighbourCount; j++)
                {
                    ref readonly var body = ref bodies[j];
                    double ox = body.X, oy = body.Y, min = body.Gap;
                    double dx = x - ox;
                    if (Math.Abs(dx) >= min) continue;
                    double dy = y - oy;
                    if (Math.Abs(dy) >= min) continue;
                    double d2 = dx * dx + dy * dy;
                    if (d2 >= body.GapSquared) continue;
                    clear = false;
                    double d = Math.Sqrt(d2);
                    if (d < 1e-6)
                    {
                        // Exactly on top: part along a direction fixed by the pair.
                        int other = neighbourIds[j];
                        double a = (id * 7919 + other * 104729) % 6283 / 1000.0;
                        dx = SimMath.Cos(a);
                        dy = SimMath.Sin(a);
                        d = 1;
                    }
                    x = ox + dx / d * min;
                    y = oy + dy / d * min;
                }
                if (clear) break;
                // The ordered pass is a pure function of this point and fixed neighbours.
                // At a bit-identical fixed point, every remaining pass would do the same work.
                if (BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(startX) &&
                    BitConverter.DoubleToInt64Bits(y) == BitConverter.DoubleToInt64Bits(startY)) break;
            }
            // Pushes may travel arbitrarily far through a crowded spawn, so they must see
            // every query result. From here each bend only spends the remaining move length:
            // a landing beyond that distance plus its gap cannot meet any of the slides.
            int bendCount = 0;
            double heroGap = heroRadius + r, heroReach = length + heroGap + 1e-6;
            double heroDx = x - hx, heroDy = y - hy;
            if (heroDx * heroDx + heroDy * heroDy <= heroReach * heroReach)
                bodies[bendCount++] = new Neighbour { X = hx, Y = hy, Gap = heroGap, GapSquared = heroGap * heroGap };
            for (int j = 1; j < neighbourCount; j++)
            {
                ref readonly var body = ref bodies[j];
                double dx = x - body.X, dy = y - body.Y;
                double reach = length + body.Gap + 1e-6;
                if (dx * dx + dy * dy > reach * reach) continue;
                bodies[bendCount++] = body;
            }
            var slides = new ReadOnlySpan<Neighbour>(bodies, 0, bendCount);
            for (int bend = 0; bend < Bends; bend++)
            {
                double l = Math.Sqrt(vx * vx + vy * vy);
                if (l < 1e-9) break;
                double ux = vx / l, uy = vy / l, hit = l, cx = 0, cy = 0;
                bool met = false;
                for (int j = 0; j < slides.Length; j++)
                {
                    ref readonly var body = ref slides[j];
                    double ox = body.X, oy = body.Y, minSquared = body.GapSquared;
                    // The first time along the move it would come inside min of (ox, oy).
                    double fx = x - ox, fy = y - oy, along = fx * ux + fy * uy;
                    if (along >= 0) continue;
                    double c = fx * fx + fy * fy - minSquared, disc = along * along - c;
                    if (disc < 0) continue;
                    double t = Math.Max(0, -along - Math.Sqrt(disc));
                    if (t >= hit) continue;
                    hit = t;
                    cx = ox;
                    cy = oy;
                    met = true;
                    // Contact times are clamped to zero. No later body can beat this
                    // first zero-time contact, including in a crowded spawn.
                    if (hit == 0) break;
                }
                hit = Math.Max(0, hit - 1e-9);
                x += ux * hit;
                y += uy * hit;
                if (!met) break;
                // Slide: the rest of the move, less its part into what it met.
                double rest = l - hit, nx = x - cx, ny = y - cy, nl = Math.Sqrt(nx * nx + ny * ny);
                vx = ux * rest;
                vy = uy * rest;
                if (nl < 1e-12) break;
                nx /= nl;
                ny /= nl;
                double into = vx * nx + vy * ny;
                if (into < 0)
                {
                    vx -= nx * into;
                    vy -= ny * into;
                }
                // Met head on, it slips aside (each mob its own way), as a crowd does, instead of jamming.
                if (vx * vx + vy * vy < SlipShare * SlipShare * rest * rest)
                {
                    double side = vx * -ny + vy * nx;
                    double sign = side > 1e-12 ? 1 : side < -1e-12 ? -1 : (id & 1) == 0 ? 1 : -1;
                    vx = -ny * sign * SlipShare * rest;
                    vy = nx * sign * SlipShare * rest;
                }
            }
            qx = x;
            qy = y;
        }

        /// <summary>
        /// Where a mob at (<paramref name="x"/>, <paramref name="y"/>) means to go in a move of up to
        /// <paramref name="length"/>: along its march, else at the hero, stopping against it (a mushroom at its
        /// range), turned aside while a structure blocks it.
        /// </summary>
        void Aim(ref Mob m, EnemyType type, double x, double y, double hx, double hy, double t, double length, out double qx, out double qy)
        {
            double dx, dy, go;
            if (t < m.HeadUntil)
            {
                dx = m.HeadX;
                dy = m.HeadY;
                go = length;
            }
            else
            {
                dx = hx - x;
                dy = hy - y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double stop = type.Move == EliteMove.Spores ? Math.Max(rules.Spores.KeepAwayM, heroRadius + type.RadiusM) : heroRadius + type.RadiusM;
                go = Math.Max(0, Math.Min(length, d - stop));
                if (d > 1e-9)
                {
                    dx /= d;
                    dy /= d;
                }
                else
                {
                    dx = 1;
                    dy = 0;
                }
            }
            if (m.Detour != 0)
            {
                // Turned 60 degrees aside, the way it turned when blocked.
                const double c = 0.5, s = 0.8660254037844386;
                double sx = m.Detour * s;
                double rx = dx * c - dy * sx, ry = dx * sx + dy * c;
                dx = rx;
                dy = ry;
                if (go < 1e-9) go = length * 0.5;
            }
            qx = x + dx * go;
            qy = y + dy * go;
        }

        /// <summary>A move from (<paramref name="x"/>, <paramref name="y"/>) to (<paramref name="qx"/>, <paramref name="qy"/>) through the structures: stopped by one, it slides along it.</summary>
        void Structures(double r, double x, double y, ref double qx, ref double qy)
        {
            var structures = sim.Structures;
            if (structures == null) return;
            double dx = qx - x, dy = qy - y;
            if (dx == 0 && dy == 0) return;
            structures.Move(x, y, r, dx, dy, out qx, out qy, out _);
        }

        void Bite(int id, ref Mob m, double t, double x, double y)
        {
            m.LastBite = t;
            sim.Health.Hurt(sim, new HeroHit
            {
                T = t, Damage = rules.TouchDamage, Source = HitSource.Touch, Owner = id, Beat = double.NaN, FromX = x, FromY = y,
            });
        }

        // ---- Gliding ----

        /// <summary>
        /// With no reliable beat: this tick's slice of the mobs plans its next segment, nearest the hero first, and
        /// every gliding mob touching the hero bites if its last bite is old enough.
        /// </summary>
        void Glide(double end)
        {
            double span = GlideTicks * Simulation.TickSeconds;
            sim.HeroWas(end, out double hx, out double hy);
            int slice = (int)(ticks % GlideTicks);
            orderCount = 0;
            int n = enemies.Count;
            double longest = 0;
            for (int id = slice; id < n; id += GlideTicks)
            {
                ref var m = ref mobs[id];
                if (!m.Owned || m.Stage != MoveStage.None) continue;
                Queue(id, hx, hy);
                longest = Math.Max(longest, rules.Cast[m.Kind].SpeedMps * span);
            }
            Array.Sort(order, 0, orderCount);
            PrepareLandings();
            for (int k = 0; k < orderCount; k++)
            {
                int id = (int)(order[k] & ((1L << IdBits) - 1));
                ref var m = ref mobs[id];
                var type = rules.Cast[m.Kind];
                m.At(end, out double x, out double y);
                Aim(ref m, type, x, y, hx, hy, end, type.SpeedMps * span, out double qx, out double qy);
                Land(id, ref m, type.RadiusM, x, y, ref qx, ref qy, hx, hy, longest);
                m.FromX = x;
                m.FromY = y;
                m.ToX = qx;
                m.ToY = qy;
                landings[id].X = qx;
                landings[id].Y = qy;
                m.Start = end;
                m.End = end + span;
                m.Motion = Motion.Glide;
            }
            double slack = heroRadius + rules.TouchSlackM;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref mobs[id];
                if (!m.Owned || m.Motion != Motion.Glide || end - m.LastBite < rules.TouchBeatlessS - 1e-9) continue;
                m.At(end, out double x, out double y);
                double r = rules.Cast[m.Kind].RadiusM + slack, dx = x - hx, dy = y - hy;
                if (dx * dx + dy * dy <= r * r) Bite(id, ref m, end, x, y);
            }
        }

        // ---- Leaving behind ----

        void LeaveBehind(double end)
        {
            sim.HeroWas(end, out double hx, out double hy);
            double far = Spawner.Rules.DespawnM, far2 = far * far;
            int n = enemies.Count;
            for (int id = 0; id < n; id++)
            {
                ref var m = ref mobs[id];
                if (!m.Owned || end < m.HeadUntil) continue;
                ref readonly var e = ref enemies[id];
                double dx = e.X - hx, dy = e.Y - hy;
                if (dx * dx + dy * dy > far2) Despawn(id, end);
            }
        }
    }
}
