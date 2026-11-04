using System;

namespace Champions.Sim
{
    /// <summary>Where an arena is in its life: none, drawing itself in, standing, or retracting.</summary>
    public enum ArenaPhase : byte
    {
        None = 0,
        Forming = 1,
        Standing = 2,
        Retracting = 3,
    }

    /// <summary>A persistent obstacle a fight grows (boss-rush.md, Arenas): a wall stops bodies and shots; a web and lava only mark the floor, for the fight's own rule (a slow, a burn).</summary>
    public enum ArenaObstacleKind : byte
    {
        Wall = 0,
        Web = 1,
        Lava = 2,
    }

    /// <summary>A prop of the arena standing now: its placement, turned, and the hero's blows it takes still.</summary>
    public struct ArenaProp
    {
        public double X, Y, Angle, Cos, Sin;
        public int Kind, Hits;
        public bool Standing, Blocks, BlocksShots;
    }

    /// <summary>An obstacle a fight grew: its shape (world metres), kind, growth and whether it still stands.</summary>
    public sealed class ArenaObstacle
    {
        public ArenaShape Shape;
        public ArenaObstacleKind Kind;
        /// <summary>It grows over [GrowFrom, GrowTo] (song time) as drawn, and counts from GrowTo.</summary>
        public double GrowFrom, GrowTo;
        public bool Standing;
    }

    /// <summary>
    /// The Boss Rush arena (boss-rush.md, Arenas; the arenas bead, ps-8z4.2): one bounded arena at a time, formed round the
    /// hero, its walls, floor zones, props and grown obstacles blocking bodies and shots, changed mid-fight by the fight.
    ///
    /// - <b>Forming</b> (<see cref="Form"/>): an arena is placed so its <see cref="ArenaDef.HeroStartX"/> lands where the hero
    ///   stands; it draws itself in over <see cref="ArenaRules.FormBeats"/> beats (the beat's tempo, or the beatless
    ///   <see cref="BeatRules.TelegraphBeatlessBeatS"/>), blocking from the start: the hero is inside it and its start is
    ///   clear by construction (<see cref="ArenaRules"/> checks). <see cref="Retract"/> lets everything go at once while its
    ///   lines retract over <see cref="ArenaRules.RetractBeats"/>.
    /// - <b>Blocking</b> (<see cref="Move"/>, the <see cref="IStructures"/> the hero, its dashes and the horde's adds already
    ///   move through): bodies stay inside the walls and out of raised, sunk and broken zones, wall obstacles and the props
    ///   that block, sliding along them. A body whose middle is already on blocked ground (a zone sank under it, an
    ///   obstacle grew on it) walks out of that one freely, so a change never traps it: hurting it there is the fight's.
    /// - <b>Shots</b> (<see cref="Cast"/>): walls, raised zones, wall obstacles and the props that stop shots stop them;
    ///   pits, holes and the marking obstacles don't.
    /// - <b>Props</b> take the hero's blows (<see cref="Strike"/>): a breakable one crumbles after its hits; a boss's attack
    ///   crumbles any (<see cref="Crumble"/>). Broken props stay broken until the arena retracts.
    /// - <b>Changes</b> (<see cref="Raise"/>, <see cref="Sink"/>, <see cref="Level"/>, <see cref="Break"/>, <see cref="Grow"/>,
    ///   <see cref="Clear"/>, <see cref="Crumble"/>) take effect at the time given, which the fight puts on a beat or half beat
    ///   and telegraphs first. Moving zones travel their loops in playing time (<see cref="SimState.RunClock"/>), still while
    ///   the world is held.
    /// - A visibility radius (the sandstorm) and a mirror axis are fight-level, not here.
    /// All positions are world metres. Every change is an event (<see cref="SimEventKind.ArenaForm"/> and the rest) the client
    /// draws from; its shapes it reads here.
    /// </summary>
    public sealed class BossArena : IStructures
    {
        const double Skin = 1e-4;
        /// <summary>A body deeper than this inside a disc prop walks out of it freely (m), as the structures' rule.</summary>
        const double Deep = 0.25;
        const int Slides = 3;

        enum SolidKind : byte { Outline, Zone, Obstacle, Prop }

        /// <summary>One piece that blocks: a polygon (its vertices in <see cref="vx"/>, <see cref="vy"/>) or a disc.</summary>
        struct Piece
        {
            public SolidKind Kind;
            public int Index, First, Count;
            public bool Disc, Shots, Bodies;
            /// <summary>A bounding circle: a disc's own, a polygon's round its vertices.</summary>
            public double Cx, Cy, R;
        }

        readonly ArenaRules rules;
        readonly Simulation sim;
        readonly StructList<Piece> solids = new StructList<Piece>(256);
        readonly StructList<double> vx = new StructList<double>(2048), vy = new StructList<double>(2048);
        readonly System.Collections.Generic.List<ArenaObstacle> obstacles = new System.Collections.Generic.List<ArenaObstacle>(64);
        readonly StructList<int> obstacleIds = new StructList<int>(64);
        ArenaZoneState[] zoneStates = new ArenaZoneState[0];
        double[] zoneX = new double[0], zoneY = new double[0];
        ArenaProp[] props = new ArenaProp[0];
        bool dirty, moving;
        double runClockAtForm;
        int scriptNext;

        internal BossArena(Simulation sim, ArenaRules rules)
        {
            this.sim = sim;
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public ArenaRules Rules => rules;

        /// <summary>The arena formed (or retracting), null when none.</summary>
        public ArenaDef Def { get; private set; }
        public int DefIndex { get; private set; } = -1;
        public ArenaPhase Phase { get; private set; }

        /// <summary>Does the arena block now (forming or standing)?</summary>
        public bool Solid => Phase == ArenaPhase.Forming || Phase == ArenaPhase.Standing;

        /// <summary>Where the arena's own origin is in the world.</summary>
        public double OriginX { get; private set; }
        public double OriginY { get; private set; }
        /// <summary>The latest form's span, and the latest retraction's (song time; NaN before).</summary>
        public double FormFrom { get; private set; } = double.NaN;
        public double FormTo { get; private set; } = double.NaN;
        public double RetractFrom { get; private set; } = double.NaN;
        public double RetractTo { get; private set; } = double.NaN;
        /// <summary>How many arenas formed, and every change since the first (zones, obstacles, props).</summary>
        public int Forms { get; private set; }
        public int Changes { get; private set; }

        /// <summary>The hero's and the boss's starts in the world.</summary>
        public double HeroStartX => OriginX + (Def?.HeroStartX ?? 0);
        public double HeroStartY => OriginY + (Def?.HeroStartY ?? 0);
        public double BossStartX => OriginX + (Def?.BossStartX ?? 0);
        public double BossStartY => OriginY + (Def?.BossStartY ?? 0);

        /// <summary>The share of the forming drawn at song time <paramref name="t"/> (0 to 1; 1 once standing), and of the retraction (1: gone).</summary>
        public double FormedAt(double t) => double.IsNaN(FormFrom) ? 0 : Share(t, FormFrom, FormTo);
        public double RetractedAt(double t) => double.IsNaN(RetractFrom) ? 0 : Share(t, RetractFrom, RetractTo);

        static double Share(double t, double from, double to) => to <= from ? (t >= from ? 1 : 0) : Math.Max(0, Math.Min(1, (t - from) / (to - from)));

        public int ZoneCount => zoneStates.Length;
        public ArenaZoneState ZoneState(int zone) => zoneStates[zone];

        /// <summary>Zone <paramref name="zone"/>'s offset from where its definition puts it (a moving zone's place in its loop).</summary>
        public void ZoneOffset(int zone, out double x, out double y)
        {
            x = zoneX[zone];
            y = zoneY[zone];
        }

        /// <summary>Is the zone's ground blocked to bodies?</summary>
        public bool ZoneBlocks(int zone) => zoneStates[zone] != ArenaZoneState.Open;

        public int PropCount => props.Length;
        public ref readonly ArenaProp Prop(int id) => ref props[id];

        public int ObstacleCount => obstacles.Count;
        public ArenaObstacle Obstacle(int id) => obstacles[id];

        // ---- Forming ----

        /// <summary>
        /// Forms arena <paramref name="def"/> at song time <paramref name="t"/>, placed so its hero start is at
        /// (<paramref name="heroX"/>, <paramref name="heroY"/>): any arena standing goes at once.
        /// </summary>
        internal void Form(ArenaDef def, double t, double heroX, double heroY)
        {
            int index = Array.IndexOf(rules.Arenas, def);
            if (index < 0) throw new ArgumentException($"arena {def?.Id} isn't in the rules");
            Def = def;
            DefIndex = index;
            OriginX = heroX - def.HeroStartX;
            OriginY = heroY - def.HeroStartY;
            Phase = ArenaPhase.Forming;
            FormFrom = t;
            FormTo = t + rules.FormBeats * BeatS();
            RetractFrom = RetractTo = double.NaN;
            runClockAtForm = sim.State.RunClock;
            Forms++;
            zoneStates = new ArenaZoneState[def.Zones.Length];
            zoneX = new double[def.Zones.Length];
            zoneY = new double[def.Zones.Length];
            moving = false;
            for (int i = 0; i < def.Zones.Length; i++)
            {
                zoneStates[i] = def.Zones[i].State;
                moving |= def.Zones[i].Path;
            }
            PlaceZones(sim.State.RunClock);
            props = new ArenaProp[def.Props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                var p = def.Props[i];
                double a = p.Degrees * Math.PI / 180;
                props[i] = new ArenaProp
                {
                    X = OriginX + p.X, Y = OriginY + p.Y, Angle = a, Cos = SimMath.Cos(a), Sin = SimMath.Sin(a), Kind = p.Kind,
                    Hits = rules.Props[p.Kind].Hits, Standing = true, Blocks = p.Blocks, BlocksShots = p.BlocksShots,
                };
            }
            obstacles.Clear();
            dirty = true;
            sim.Emit(SimEvent.ArenaForm(t, index, OriginX, OriginY, FormFrom, FormTo));
        }

        /// <summary>Forms <paramref name="def"/> round the hero where it stands at <paramref name="t"/>.</summary>
        internal void FormAroundHero(ArenaDef def, double t)
        {
            sim.HeroWas(t, out double x, out double y);
            Form(def, t, x, y);
        }

        /// <summary>The arena stops blocking at <paramref name="t"/> and its lines retract.</summary>
        internal void Retract(double t)
        {
            if (!Solid) return;
            Phase = ArenaPhase.Retracting;
            RetractFrom = t;
            RetractTo = t + rules.RetractBeats * BeatS();
            dirty = true;
            sim.Emit(SimEvent.ArenaRetract(t, DefIndex, RetractFrom, RetractTo));
        }

        /// <summary>A beat's length now: the tempo with a beat, else the beatless telegraphs' beat.</summary>
        double BeatS()
        {
            var s = sim.State;
            return s.Beat == Reliability.Reliable && s.LastBeat >= 0 ? sim.Beats.TempoAt(s.LastBeat) : sim.Beats.Rules.TelegraphBeatlessBeatS;
        }

        /// <summary>At the tick's end: the phases move on, the moving zones travel and a scripted arena's steps fire.</summary>
        internal void Tick(double end)
        {
            var script = sim.ArenaScript;
            if (script != null) RunScript(script, end);
            if (Phase == ArenaPhase.Forming && end >= FormTo) Phase = ArenaPhase.Standing;
            if (Phase == ArenaPhase.Retracting && end >= RetractTo)
            {
                Phase = ArenaPhase.None;
                Def = null;
                DefIndex = -1;
                dirty = true;
            }
            if (moving && Solid)
            {
                PlaceZones(sim.State.RunClock);
                for (int i = 0; i < zoneStates.Length && !dirty; i++) dirty = Def.Zones[i].Path && zoneStates[i] != ArenaZoneState.Open;
            }
        }

        void PlaceZones(double runClock)
        {
            for (int i = 0; i < zoneStates.Length; i++)
            {
                var z = Def.Zones[i];
                if (!z.Path) continue;
                Along(z, (runClock - runClockAtForm) * z.SpeedMps, out zoneX[i], out zoneY[i]);
            }
        }

        /// <summary>The point <paramref name="d"/> metres round zone <paramref name="z"/>'s closed loop of offsets.</summary>
        static void Along(ArenaZone z, double d, out double x, out double y)
        {
            int n = z.PathX.Length;
            double length = 0;
            for (int i = 0; i < n; i++) length += Hypot(z.PathX[(i + 1) % n] - z.PathX[i], z.PathY[(i + 1) % n] - z.PathY[i]);
            x = z.PathX[0];
            y = z.PathY[0];
            if (!(length > 1e-9)) return;
            d %= length;
            if (d < 0) d += length;
            for (int i = 0; i < n; i++)
            {
                double ax = z.PathX[i], ay = z.PathY[i], bx = z.PathX[(i + 1) % n], by = z.PathY[(i + 1) % n], leg = Hypot(bx - ax, by - ay);
                if (d <= leg || i == n - 1)
                {
                    double u = leg > 1e-12 ? Math.Min(1, d / leg) : 0;
                    x = ax + (bx - ax) * u;
                    y = ay + (by - ay) * u;
                    return;
                }
                d -= leg;
            }
        }

        void RunScript(ArenaScript script, double end)
        {
            if (scriptNext == 0 && script.FormAtS <= end)
            {
                var def = rules.Find(script.Arena) ?? throw new InvalidOperationException($"the arena script's arena {script.Arena} isn't in {ArenaRules.File}");
                FormAroundHero(def, Math.Max(sim.Now - Simulation.TickSeconds, script.FormAtS));
                scriptNext = 1;
            }
            while (scriptNext > 0 && scriptNext - 1 < script.Steps.Length && script.Steps[scriptNext - 1].AtS <= end)
            {
                var step = script.Steps[scriptNext - 1];
                double t = Math.Max(end - Simulation.TickSeconds, step.AtS);
                switch (step.Op)
                {
                    case ArenaOp.Raise: Raise(Zone(step.Zone), t); break;
                    case ArenaOp.Sink: Sink(Zone(step.Zone), t); break;
                    case ArenaOp.Level: Level(Zone(step.Zone), t); break;
                    case ArenaOp.Break: Break(Zone(step.Zone), t); break;
                    case ArenaOp.Grow:
                        var shape = new ArenaShape { X = new double[step.Shape.Count], Y = new double[step.Shape.Count] };
                        for (int i = 0; i < shape.Count; i++)
                        {
                            shape.X[i] = OriginX + step.Shape.X[i];
                            shape.Y[i] = OriginY + step.Shape.Y[i];
                        }
                        Grow(shape, step.Obstacle, t, step.GrowS);
                        break;
                    case ArenaOp.Clear: Clear(step.Index, t); break;
                    case ArenaOp.Crumble: Crumble(step.Index, t, BreakCause.Boss, 0, 0); break;
                    case ArenaOp.Retract: Retract(t); break;
                }
                scriptNext++;
            }
        }

        int Zone(string id)
        {
            int i = Def?.ZoneIndex(id) ?? -1;
            if (i < 0) throw new InvalidOperationException($"the arena has no zone {id}");
            return i;
        }

        // ---- Changes ----

        /// <summary>Raises zone <paramref name="zone"/> into a block at <paramref name="t"/>.</summary>
        internal void Raise(int zone, double t) => SetZone(zone, ArenaZoneState.Raised, t);

        /// <summary>Sinks zone <paramref name="zone"/> into a pit at <paramref name="t"/>.</summary>
        internal void Sink(int zone, double t) => SetZone(zone, ArenaZoneState.Sunk, t);

        /// <summary>Brings a raised or sunk zone back to open floor at <paramref name="t"/> (a broken one stays broken).</summary>
        internal void Level(int zone, double t) => SetZone(zone, ArenaZoneState.Open, t);

        /// <summary>Breaks a breakable zone away for the rest of the fight at <paramref name="t"/> (islands).</summary>
        internal void Break(int zone, double t)
        {
            if (Def.Zones[zone].Kind != ArenaZoneKind.Breakable) throw new InvalidOperationException($"zone {Def.Zones[zone].Id} isn't breakable");
            SetZone(zone, ArenaZoneState.Broken, t);
        }

        void SetZone(int zone, ArenaZoneState state, double t)
        {
            if (!Solid) throw new InvalidOperationException("no arena stands");
            var from = zoneStates[zone];
            if (from == state || from == ArenaZoneState.Broken) return;
            zoneStates[zone] = state;
            dirty = true;
            Changes++;
            sim.Emit(SimEvent.ArenaZone(t, zone, state, from, BeatS()));
        }

        /// <summary>
        /// Grows obstacle <paramref name="shape"/> (world metres) of <paramref name="kind"/> from <paramref name="t"/>, there
        /// in full and counting from <paramref name="t"/> + <paramref name="growS"/>; returns its id.
        /// </summary>
        internal int Grow(ArenaShape shape, ArenaObstacleKind kind, double t, double growS)
        {
            if (!Solid) throw new InvalidOperationException("no arena stands");
            if (shape.Area < 0) shape = ArenaShape.Of(Reversed(shape.X), Reversed(shape.Y));
            var o = new ArenaObstacle { Shape = shape, Kind = kind, GrowFrom = t, GrowTo = t + Math.Max(0, growS), Standing = true };
            int id = obstacles.Count;
            obstacles.Add(o);
            if (growS <= 0) dirty = true;
            else obstacleIds.Add(id);
            Changes++;
            shape.Centre(out double cx, out double cy);
            sim.Emit(SimEvent.ArenaObstacle(t, id, kind, true, o.GrowFrom, o.GrowTo, cx, cy));
            return id;
        }

        static double[] Reversed(double[] a)
        {
            var r = (double[])a.Clone();
            Array.Reverse(r);
            return r;
        }

        /// <summary>Clears obstacle <paramref name="id"/> at <paramref name="t"/> (a web cut).</summary>
        internal void Clear(int id, double t)
        {
            var o = obstacles[id];
            if (!o.Standing) return;
            o.Standing = false;
            dirty = true;
            Changes++;
            o.Shape.Centre(out double cx, out double cy);
            sim.Emit(SimEvent.ArenaObstacle(t, id, o.Kind, false, o.GrowFrom, o.GrowTo, cx, cy));
        }

        /// <summary>Crumbles prop <paramref name="id"/> at <paramref name="t"/>, thrown along (<paramref name="dx"/>, <paramref name="dy"/>): a boss's attack, whatever its hits.</summary>
        internal void Crumble(int id, double t, BreakCause cause, double dx, double dy)
        {
            ref var p = ref props[id];
            if (!p.Standing) return;
            p.Standing = false;
            p.Hits = 0;
            dirty = true;
            Changes++;
            sim.Emit(SimEvent.ArenaProp(t, id, 0, (int)cause, dx, dy));
        }

        /// <summary>Crumbles every standing prop <paramref name="shape"/> reaches at <paramref name="t"/>: a boss's attack. Returns how many.</summary>
        internal int Shatter(in HitShape shape, double t)
        {
            int n = 0;
            for (int i = 0; i < props.Length; i++)
            {
                if (!props[i].Standing || !Touches(shape, props[i])) continue;
                shape.Away(props[i].X, props[i].Y, out double dx, out double dy);
                Crumble(i, t, BreakCause.Boss, dx, dy);
                n++;
            }
            return n;
        }

        bool Touches(in HitShape shape, in ArenaProp p)
        {
            var k = rules.Props[p.Kind];
            return k.Shape == FootprintShape.Circle ? shape.Touches(p.X, p.Y, k.Radius) : shape.TouchesBox(p.X, p.Y, p.Cos, p.Sin, k.HalfX, k.HalfY);
        }

        // ---- Questions ----

        /// <summary>Is (<paramref name="x"/>, <paramref name="y"/>) inside the walls? (Everywhere with no arena standing.)</summary>
        public bool Inside(double x, double y) => !Solid || Def.Outline.Contains(x, y, OriginX, OriginY);

        /// <summary>The last listed zone holding (<paramref name="x"/>, <paramref name="y"/>) where it is now, or -1.</summary>
        public int ZoneAt(double x, double y)
        {
            if (!Solid) return -1;
            for (int i = zoneStates.Length - 1; i >= 0; i--)
                if (Def.Zones[i].Shape.Contains(x, y, OriginX + zoneX[i], OriginY + zoneY[i])) return i;
            return -1;
        }

        /// <summary>The latest standing, fully grown obstacle holding (<paramref name="x"/>, <paramref name="y"/>) at <paramref name="t"/>, or -1.</summary>
        public int ObstacleAt(double x, double y, double t)
        {
            if (!Solid) return -1;
            for (int i = obstacles.Count - 1; i >= 0; i--)
            {
                var o = obstacles[i];
                if (o.Standing && t >= o.GrowTo && o.Shape.Contains(x, y)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Could a body of <paramref name="radius"/> stand at (<paramref name="x"/>, <paramref name="y"/>) now: inside the
        /// walls by its radius, clear of everything that blocks bodies? (A fight's spawn points, a fairness test's path.)
        /// </summary>
        public bool Walkable(double x, double y, double radius)
        {
            if (!Solid) return true;
            Rebuild();
            for (int i = 0; i < solids.Count; i++)
            {
                ref readonly var s = ref solids[i];
                if (!s.Bodies) continue;
                if (s.Disc)
                {
                    if (Hypot(x - s.Cx, y - s.Cy) < s.R + radius) return false;
                    continue;
                }
                bool inside = Contains(s, x, y);
                if (s.Kind == SolidKind.Outline ? !inside : inside) return false;
                if (EdgeDistance(s, x, y) < radius) return false;
            }
            return true;
        }

        // ---- Bodies and shots ----

        public void Move(double x, double y, double radius, double dx, double dy, out double nx, out double ny, out int blocker)
        {
            blocker = -1;
            if (Solid)
            {
                Rebuild();
                for (int pass = 0; pass < Slides; pass++)
                {
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-12) break;
                    int hit = First(x, y, radius, dx, dy, shots: false, out double best, out double hnx, out double hny);
                    if (hit < 0)
                    {
                        x += dx;
                        y += dy;
                        break;
                    }
                    if (blocker < 0) blocker = Id(hit);
                    double go = Math.Max(0, best - Skin / len);
                    x += dx * go;
                    y += dy * go;
                    double rx = dx * (1 - go), ry = dy * (1 - go), into = rx * hnx + ry * hny;
                    if (into < 0)
                    {
                        rx -= hnx * into;
                        ry -= hny * into;
                    }
                    dx = rx;
                    dy = ry;
                }
            }
            else
            {
                x += dx;
                y += dy;
            }
            nx = x;
            ny = y;
        }

        /// <summary>
        /// A shot of <paramref name="radius"/> flying from (<paramref name="x0"/>, <paramref name="y0"/>) to
        /// (<paramref name="x1"/>, <paramref name="y1"/>): the share of the way it gets before what stops shots stops it (1:
        /// clear), and what stopped it (<see cref="Move"/>'s ids, -1 for none).
        /// </summary>
        public double Cast(double x0, double y0, double x1, double y1, double radius, out int blocker)
        {
            blocker = -1;
            if (!Solid) return 1;
            Rebuild();
            int hit = First(x0, y0, radius, x1 - x0, y1 - y0, shots: true, out double at, out _, out _);
            if (hit < 0) return 1;
            blocker = Id(hit);
            return Math.Max(0, at);
        }

        /// <summary>A solid's id as <see cref="Move"/> reports it: a prop's index, or past the props for the rest.</summary>
        int Id(int solid) => solids[solid].Kind == SolidKind.Prop ? solids[solid].Index : props.Length + solid;

        /// <summary>Is <paramref name="id"/> (a <see cref="Move"/> blocker) a prop the hero's blows can still break?</summary>
        public bool Breakable(int id) => id >= 0 && id < props.Length && props[id].Standing && props[id].Hits > 0;

        /// <summary>The first solid the moving body meets, at what share of the move, and the way out of it there.</summary>
        int First(double x, double y, double r, double dx, double dy, bool shots, out double best, out double hnx, out double hny)
        {
            best = double.PositiveInfinity;
            hnx = hny = 0;
            int hit = -1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            for (int i = 0; i < solids.Count; i++)
            {
                ref readonly var s = ref solids[i];
                if (shots ? !s.Shots : !s.Bodies) continue;
                if (s.Kind != SolidKind.Outline && HitShape.DistanceToSegment(s.Cx, s.Cy, x, y, x + dx, y + dy) > s.R + r + Skin) continue;
                double at, ox, oy;
                if (s.Disc)
                {
                    double ex = x - s.Cx, ey = y - s.Cy, d = Math.Sqrt(ex * ex + ey * ey), gap = d - s.R - r;
                    if (gap < -Deep) continue;
                    if (gap <= Skin)
                    {
                        ox = d > 1e-12 ? ex / d : 1;
                        oy = d > 1e-12 ? ey / d : 0;
                        if ((dx * ox + dy * oy) / len >= -1e-9) continue;
                        at = 0;
                    }
                    else
                    {
                        at = StructureField.SweepDisc(ex, ey, dx, dy, s.R + r);
                        if (!(at <= 1)) continue;
                        double cx = ex + dx * at, cy = ey + dy * at, c = Math.Sqrt(cx * cx + cy * cy);
                        ox = cx / c;
                        oy = cy / c;
                    }
                }
                else
                {
                    // On the blocked side already (outside the walls, inside a zone): it walks out of this one freely.
                    bool inside = Contains(s, x, y);
                    if (s.Kind == SolidKind.Outline ? !inside : inside) continue;
                    at = PolygonSweep(s, x, y, r, dx, dy, len, out ox, out oy);
                    if (!(at <= 1)) continue;
                }
                if (at < best)
                {
                    best = at;
                    hit = i;
                    hnx = ox;
                    hny = oy;
                }
            }
            return hit;
        }

        /// <summary>The share of the move at which the body first touches a polygon solid's edges moving into them, and the way out there.</summary>
        double PolygonSweep(in Piece s, double x, double y, double r, double dx, double dy, double len, out double ox, out double oy)
        {
            double best = double.PositiveInfinity;
            ox = oy = 0;
            for (int k = 0; k < s.Count; k++)
            {
                int a = s.First + k, b = s.First + (k + 1) % s.Count;
                double ax = vx[a], ay = vy[a], bx = vx[b], by = vy[b];
                Nearest(x, y, ax, ay, bx, by, out double qx, out double qy);
                double ex = x - qx, ey = y - qy, d = Math.Sqrt(ex * ex + ey * ey), gap = d - r;
                double at, nx, ny;
                if (gap <= Skin)
                {
                    if (d > 1e-9)
                    {
                        nx = ex / d;
                        ny = ey / d;
                    }
                    else
                    {
                        // On the line itself: out is toward the open side.
                        double ex2 = bx - ax, ey2 = by - ay, el = Math.Sqrt(ex2 * ex2 + ey2 * ey2), sign = s.Kind == SolidKind.Outline ? -1 : 1;
                        nx = sign * ey2 / el;
                        ny = -sign * ex2 / el;
                    }
                    if ((dx * nx + dy * ny) / len >= -1e-9) continue;
                    at = 0;
                }
                else
                {
                    at = Math.Min(StructureField.SweepDisc(x - ax, y - ay, dx, dy, r), StructureField.SweepDisc(x - bx, y - by, dx, dy, r));
                    double sx = bx - ax, sy = by - ay, sl = Math.Sqrt(sx * sx + sy * sy);
                    if (sl > 1e-12)
                    {
                        double mx = sy / sl, my = -sx / sl, s0 = (x - ax) * mx + (y - ay) * my, vn = dx * mx + dy * my;
                        double t = s0 > 0 ? (vn < 0 ? (s0 - r) / -vn : double.PositiveInfinity) : (vn > 0 ? (-s0 - r) / vn : double.PositiveInfinity);
                        if (t >= 0 && t < at)
                        {
                            double u = ((x + dx * t - ax) * sx + (y + dy * t - ay) * sy) / (sl * sl);
                            if (u >= 0 && u <= 1) at = t;
                        }
                    }
                    if (!(at <= 1)) continue;
                    double px = x + dx * at, py = y + dy * at;
                    Nearest(px, py, ax, ay, bx, by, out qx, out qy);
                    double cx = px - qx, cy = py - qy, c = Math.Sqrt(cx * cx + cy * cy);
                    if (c < 1e-12) continue;
                    nx = cx / c;
                    ny = cy / c;
                    if (dx * nx + dy * ny >= 0) continue;
                }
                if (at < best)
                {
                    best = at;
                    ox = nx;
                    oy = ny;
                }
            }
            return best;
        }

        static void Nearest(double px, double py, double ax, double ay, double bx, double by, out double qx, out double qy)
        {
            double sx = bx - ax, sy = by - ay, l2 = sx * sx + sy * sy;
            double u = l2 > 1e-24 ? Math.Max(0, Math.Min(1, ((px - ax) * sx + (py - ay) * sy) / l2)) : 0;
            qx = ax + sx * u;
            qy = ay + sy * u;
        }

        bool Contains(in Piece s, double px, double py)
        {
            bool inside = false;
            for (int i = 0, j = s.Count - 1; i < s.Count; j = i++)
            {
                double xi = vx[s.First + i], yi = vy[s.First + i], xj = vx[s.First + j], yj = vy[s.First + j];
                if ((yi > py) == (yj > py)) continue;
                if (px < xj + (py - yj) * (xi - xj) / (yi - yj)) inside = !inside;
            }
            return inside;
        }

        double EdgeDistance(in Piece s, double px, double py)
        {
            double best = double.PositiveInfinity;
            for (int i = 0, j = s.Count - 1; i < s.Count; j = i++)
                best = Math.Min(best, HitShape.DistanceToSegment(px, py, vx[s.First + j], vy[s.First + j], vx[s.First + i], vy[s.First + i]));
            return best;
        }

        /// <summary>Gathers what blocks now into the solids, when something changed or an obstacle finished growing.</summary>
        void Rebuild()
        {
            double now = sim.Now;
            for (int i = 0; i < obstacleIds.Count; i++)
            {
                if (obstacles[obstacleIds[i]].GrowTo > now) continue;
                dirty = true;
                obstacleIds[i] = obstacleIds[obstacleIds.Count - 1];
                obstacleIds.RemoveLast();
                i--;
            }
            if (!dirty) return;
            dirty = false;
            solids.Clear();
            vx.Clear();
            vy.Clear();
            if (!Solid) return;
            AddPolygon(SolidKind.Outline, 0, Def.Outline, OriginX, OriginY, true);
            for (int i = 0; i < zoneStates.Length; i++)
                if (zoneStates[i] != ArenaZoneState.Open)
                    AddPolygon(SolidKind.Zone, i, Def.Zones[i].Shape, OriginX + zoneX[i], OriginY + zoneY[i], zoneStates[i] == ArenaZoneState.Raised);
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (o.Standing && o.Kind == ArenaObstacleKind.Wall && o.GrowTo <= now) AddPolygon(SolidKind.Obstacle, i, o.Shape, 0, 0, true);
            }
            for (int i = 0; i < props.Length; i++)
            {
                ref readonly var p = ref props[i];
                if (!p.Standing || !(p.Blocks || p.BlocksShots)) continue;
                var k = rules.Props[p.Kind];
                if (k.Shape == FootprintShape.Circle)
                {
                    solids.Add(new Piece { Kind = SolidKind.Prop, Index = i, Disc = true, Cx = p.X, Cy = p.Y, R = k.Radius, Shots = p.BlocksShots, Bodies = p.Blocks });
                    continue;
                }
                var box = ArenaShape.Box(p.X, p.Y, 2 * k.HalfX, 2 * k.HalfY, p.Angle * 180 / Math.PI);
                AddPolygon(SolidKind.Prop, i, box, 0, 0, p.BlocksShots, p.Blocks);
            }
        }

        void AddPolygon(SolidKind kind, int index, ArenaShape shape, double ox, double oy, bool shots, bool bodies = true)
        {
            var s = new Piece { Kind = kind, Index = index, First = vx.Count, Count = shape.Count, Shots = shots, Bodies = bodies };
            double cx = 0, cy = 0;
            for (int i = 0; i < shape.Count; i++)
            {
                vx.Add(shape.X[i] + ox);
                vy.Add(shape.Y[i] + oy);
                cx += shape.X[i] + ox;
                cy += shape.Y[i] + oy;
            }
            s.Cx = cx / shape.Count;
            s.Cy = cy / shape.Count;
            for (int i = 0; i < shape.Count; i++) s.R = Math.Max(s.R, Hypot(vx[s.First + i] - s.Cx, vy[s.First + i] - s.Cy));
            solids.Add(s);
        }

        static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

        // ---- The hero's blows ----

        public void Reach(in HitShape shape, double t, StructList<int> into)
        {
            if (!Solid) return;
            for (int i = 0; i < props.Length; i++)
                if (props[i].Standing && props[i].Hits > 0 && Touches(shape, props[i])) into.Add(i);
        }

        public bool Strike(Simulation sim, int id, in Blow blow)
        {
            if (!Breakable(id)) return false;
            ref var p = ref props[id];
            blow.Shape.Away(p.X, p.Y, out double dx, out double dy);
            if (!blow.Shatter && --p.Hits > 0)
            {
                Changes++;
                sim.Emit(SimEvent.ArenaProp(blow.T, id, p.Hits, (int)BreakCause.Blow, dx, dy));
                return false;
            }
            Crumble(id, blow.T, BreakCause.Blow, dx, dy);
            return true;
        }

        /// <summary>Folds the arena into the run's hash (once any formed).</summary>
        internal ulong Fold(ulong h)
        {
            h = Hashing.Fold(h, (ulong)(uint)Forms | (ulong)Phase << 32 | (ulong)(uint)(DefIndex + 1) << 40);
            h = Hashing.Fold(h, (ulong)(uint)Changes | (ulong)(uint)obstacles.Count << 32);
            h = Hashing.Fold(h, Hashing.Bits(OriginX));
            return Hashing.Fold(h, Hashing.Bits(OriginY));
        }
    }

    /// <summary>A scripted arena change (<see cref="ArenaScript"/>).</summary>
    public enum ArenaOp : byte { Raise, Sink, Level, Break, Grow, Clear, Crumble, Retract }

    /// <summary>One step of a scripted arena: at song time <see cref="AtS"/>, <see cref="Op"/> on a zone (by id), an obstacle or prop (by index), or a grown shape (arena metres).</summary>
    public struct ArenaStep
    {
        public double AtS;
        public ArenaOp Op;
        public string Zone;
        public int Index;
        public ArenaShape Shape;
        public ArenaObstacleKind Obstacle;
        public double GrowS;
    }

    /// <summary>
    /// A scripted arena (stills and tests, as <see cref="SimConfig.Duel"/> scripts a duel): arena <see cref="Arena"/> forms
    /// round the hero at <see cref="FormAtS"/>, then its steps fire at their times, in order. Fights drive arenas from
    /// their own modules instead.
    /// </summary>
    public sealed class ArenaScript
    {
        public string Arena;
        public double FormAtS;
        public ArenaStep[] Steps = new ArenaStep[0];
    }
}
