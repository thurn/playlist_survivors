using System;
using System.Collections.Generic;

namespace PS.Game.Sim.Bots
{
    public enum BotKind
    {
        /// <summary>Native's positioning bot: kites the crowd on the grid, walks to chests and gems when clear, takes the first card.</summary>
        Positioning,
        /// <summary>Gives no input: the hero stands, and the cards are confirmed as a headless run's are (<see cref="AutoCards"/>).</summary>
        Idle,
    }

    /// <summary>
    /// A headless player of the run (unity-game.md, Tools, Checks and Evidence: bots and sweeps), the
    /// port of native's <c>SurvivorBot</c> (native/crates/players/src/survivors.rs). It plays as the
    /// client does: it reads only the sim's public state and the events it emits, never the stream,
    /// and gives keys, heard now.
    ///
    /// <code>
    /// bot.Act(sim);                      // before each frame's StepTo: the keys
    /// sim.StepTo(frontier);
    /// bot.Observe(sim.EventsSince(c));   // after it: the gems on the field
    /// </code>
    ///
    /// The positioning bot's aim is native's: away from the enemies within <see cref="ReachM"/>, each
    /// weighted 1 / (d² + <see cref="SoftM"/>²); while none is within <see cref="SafetyM"/>, toward the
    /// nearest chest, else toward the nearest gem bent <see cref="Bend"/> of the way along the escape.
    /// Native's lengths are its pixels at 27 px a metre. What the grid changes:
    ///
    /// - <b>Four directions and a route</b>: native holds one or two arrows (eight directions, the hero
    ///   gliding along what it touches); the grid hero takes one tile a stride step in one of four, and
    ///   structures and the arena's edge block whole tiles, so a greedy step pins it in a pocket. The
    ///   aim becomes a goal, the chest or gem pulled to, or <see cref="EscapeM"/> along the escape, and
    ///   the bot takes the first step of the shortest path to the open tile nearest it within
    ///   <see cref="RouteTiles"/> (a breadth-first search, the aim's larger axis tried first), which
    ///   zigzags a diagonal and walks round a structure.
    /// - <b>On the step</b>: native steers once a stride, as the hero sets off. The bot decides once a
    ///   step too, <see cref="DecideS"/> before the next one is due (the latest step plus its period),
    ///   so a turn's press takes that step as a player's on the beat does, and the held key strides
    ///   on the steps after, never a second time on one step.
    /// - <b>Cards</b>, as native's: frozen, it lets go of its keys; one beat after the third card has
    ///   flipped in (the reveals' last interval) it presses Left, the first card, and Enter, and
    ///   releases them on the next frame. After a freeze it presses again, held keys being let go.
    /// </summary>
    public sealed class SurvivorBot
    {
        public const double PxPerM = 27;
        public const double ReachM = 240 / PxPerM, SafetyM = 90 / PxPerM, SoftM = 24 / PxPerM;
        public const double Bend = 0.35;
        /// <summary>How far along the escape the bot's goal lies.</summary>
        public const double EscapeM = 6;
        /// <summary>How far, in tiles each way, the bot's route looks.</summary>
        public const int RouteTiles = 16;
        /// <summary>How long before the next stride step is due the bot steers: inside a press's stride, 0.11 s.</summary>
        public const double DecideS = 0.08;

        struct Gem
        {
            public int Id;
            public double X, Y;
        }

        const int Side = 2 * RouteTiles + 1;

        readonly BotKind kind;
        readonly List<Gem> gems = new List<Gem>();
        readonly List<Key> release = new List<Key>();
        // The route's search: each cell's first step from the hero (-1 unseen), and its queue.
        readonly int[] first = new int[Side * Side];
        readonly int[] queue = new int[Side * Side];
        double headingX, headingY;
        Key? held;
        int picked = -1;
        double steeredFor = double.NaN;

        public SurvivorBot(BotKind kind, ulong seed)
        {
            this.kind = kind;
            double angle = new SimRandom(seed ^ 0x5355525649564F52UL).NextDouble() * 2 * Math.PI;
            headingX = Math.Cos(angle);
            headingY = Math.Sin(angle);
        }

        /// <summary>The bot's keys for this frame, given at the sim's time before it steps.</summary>
        public void Act(Simulation sim)
        {
            var state = sim.State;
            foreach (var key in release) sim.Release(key, sim.Now);
            release.Clear();
            if (kind == BotKind.Idle)
            {
                AutoCards.Confirm(sim);
                return;
            }
            if (state.Frozen)
            {
                Let(sim, null);
                steeredFor = double.NaN;
                var offer = state.Offer;
                double beat = Math.Max(0.001, offer.Reveal2 - offer.Reveal1);
                if (offer.Open && offer.Serial != picked && sim.Now >= offer.Reveal2 + beat - 1e-9)
                {
                    sim.Press(Key.Left, sim.Now);
                    sim.Press(Key.Enter, sim.Now);
                    release.Add(Key.Left);
                    release.Add(Key.Enter);
                    picked = offer.Serial;
                }
                return;
            }
            if (state.Phase != RunPhase.Playing || state.Hero.Fallen) return;
            double due = state.StrideStep + state.StridePeriod;
            if (sim.Now < due - DecideS || steeredFor == state.StrideStep) return;
            steeredFor = state.StrideStep;
            state.Hero.PositionAt(sim.Now, out double hx, out double hy);
            double range = Aim(sim, hx, hy, out headingX, out headingY);
            Let(sim, Route(sim, hx + headingX * range, hy + headingY * range, headingX, headingY));
        }

        /// <summary>Takes in a frame's events: the gems dropped, merged and taken off.</summary>
        public void Observe(ReadOnlySpan<SimEvent> events)
        {
            foreach (var e in events)
            {
                if (e.Kind == SimEventKind.Drop && e.AsDrop.ItemKind == ItemKind.Gem)
                {
                    var d = e.AsDrop;
                    int i = gems.FindIndex(g => g.Id == d.Item);
                    var gem = new Gem { Id = d.Item, X = d.X, Y = d.Y };
                    if (i >= 0) gems[i] = gem;
                    else gems.Add(gem);
                }
                else if (e.Kind == SimEventKind.GemFlight)
                {
                    int id = e.AsGemFlight.Gem;
                    gems.RemoveAll(g => g.Id == id);
                }
            }
        }

        /// <summary>Holds <paramref name="key"/> alone (none: no key), pressing it only when it changes.</summary>
        void Let(Simulation sim, Key? key)
        {
            if (held == key) return;
            if (held is Key old) sim.Release(old, sim.Now);
            if (key is Key k) sim.Press(k, sim.Now);
            held = key;
        }

        /// <summary>
        /// Native's aim from the hero at (hx, hy), a unit vector (the heading when nothing pulls or
        /// pushes); returns how far its goal lies: the chest or gem pulled to, else <see cref="EscapeM"/>.
        /// </summary>
        double Aim(Simulation sim, double hx, double hy, out double ax, out double ay)
        {
            var state = sim.State;
            double x = 0, y = 0, weight = 0;
            bool danger = false;
            foreach (var enemy in state.Enemies)
            {
                if (!enemy.Alive) continue;
                enemy.PositionAt(sim.Now, out double ex, out double ey);
                double dx = ex - hx, dy = ey - hy, d2 = dx * dx + dy * dy;
                if (!(d2 <= ReachM * ReachM)) continue;
                double w = 1 / (d2 + SoftM * SoftM);
                x -= dx * w;
                y -= dy * w;
                weight += w;
                danger |= d2 < SafetyM * SafetyM;
            }
            if (!Unit(x, y, out double escX, out double escY))
            {
                escX = headingX;
                escY = headingY;
            }
            ax = escX;
            ay = escY;
            if (danger) return EscapeM;
            // A chest or gem under the hero: stand on it.
            double best = double.PositiveInfinity, tx = 0, ty = 0;
            foreach (var chest in state.FieldChests) Nearest(chest.X, chest.Y, hx, hy, ref best, ref tx, ref ty);
            if (best < double.PositiveInfinity)
            {
                if (Unit(tx - hx, ty - hy, out double cx, out double cy)) (ax, ay) = (cx, cy);
                return Math.Sqrt(best);
            }
            foreach (var gem in gems) Nearest(gem.X, gem.Y, hx, hy, ref best, ref tx, ref ty);
            if (best < double.PositiveInfinity)
            {
                // XP only while clear of contact; the wider crowd still bends the path.
                double bend = weight > 0 ? Bend : 0;
                if (Unit(tx - hx, ty - hy, out double px, out double py) && !Unit(px + bend * escX, py + bend * escY, out ax, out ay))
                    (ax, ay) = (escX, escY);
                return Math.Sqrt(best);
            }
            return EscapeM;
        }

        static void Nearest(double x, double y, double hx, double hy, ref double best, ref double bx, ref double by)
        {
            double d2 = (x - hx) * (x - hx) + (y - hy) * (y - hy);
            if (d2 >= best) return;
            best = d2;
            bx = x;
            by = y;
        }

        static bool Unit(double x, double y, out double ux, out double uy)
        {
            double d = Math.Sqrt(x * x + y * y);
            if (!(d > 1e-9) || double.IsInfinity(d))
            {
                ux = uy = 0;
                return false;
            }
            ux = x / d;
            uy = y / d;
            return true;
        }

        /// <summary>
        /// The first key of the shortest path to the open tile nearest the goal (gx, gy) within
        /// <see cref="RouteTiles"/>, the aim (ax, ay)'s larger axis tried first; none if the hero's own tile is nearest.
        /// </summary>
        Key? Route(Simulation sim, double gx, double gy, double ax, double ay)
        {
            var hero = sim.State.Hero;
            var arena = sim.Arena;
            Key alongX = ax < 0 ? Key.Left : Key.Right, alongY = ay < 0 ? Key.Down : Key.Up;
            Span<Key> order = stackalloc Key[4];
            bool xFirst = Math.Abs(ax) >= Math.Abs(ay);
            order[0] = xFirst ? alongX : alongY;
            order[1] = xFirst ? alongY : alongX;
            order[2] = Opposite(order[1]);
            order[3] = Opposite(order[0]);
            Array.Fill(first, -1);
            int start = RouteTiles * Side + RouteTiles, head = 0, tail = 0;
            first[start] = 4; // the hero's own tile
            queue[tail++] = start;
            int best = start;
            double bestD = double.PositiveInfinity;
            while (head < tail)
            {
                int cell = queue[head++];
                int cx = cell % Side - RouteTiles, cy = cell / Side - RouteTiles;
                double dx = arena.CentreX(hero.TileX + cx) - gx, dy = arena.CentreY(hero.TileY + cy) - gy;
                double d = dx * dx + dy * dy;
                if (d < bestD - 1e-9)
                {
                    bestD = d;
                    best = cell;
                }
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (order[k] == Key.Right ? 1 : order[k] == Key.Left ? -1 : 0);
                    int ny = cy + (order[k] == Key.Up ? 1 : order[k] == Key.Down ? -1 : 0);
                    if (nx < -RouteTiles || nx > RouteTiles || ny < -RouteTiles || ny > RouteTiles) continue;
                    int next = (ny + RouteTiles) * Side + nx + RouteTiles;
                    if (first[next] >= 0 || !arena.IsOpen(hero.TileX + nx, hero.TileY + ny)) continue;
                    first[next] = cell == start ? (int)order[k] : first[cell];
                    queue[tail++] = next;
                }
            }
            return best == start ? (Key?)null : (Key)first[best];
        }

        static Key Opposite(Key k) => k == Key.Left ? Key.Right : k == Key.Right ? Key.Left : k == Key.Up ? Key.Down : Key.Up;
    }
}
