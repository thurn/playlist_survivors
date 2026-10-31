using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The crystal structures (unity-game.md, "Crystal Structures"): obstacles from the level data
    /// that block the hero's grid and the enemies' flow field, break on big moments and regrow on
    /// zone changes.
    ///
    /// - <b>Hits</b> come as a disc around a centre (<see cref="Strike"/>): every standing
    ///   structure with a tile in reach is hit, at its nearest point. The sword's heavy strike and
    ///   its cleave wave break structures, as will M3's boss slam, finale blast and an evolved
    ///   weapon's hit on an accent; a small structure breaks in one such hit, a large one in two,
    ///   or three when it covers <see cref="LargeThreeHitTiles"/> tiles or more. Other hits, and
    ///   breaking hits short of the break, only glint. One heavy strike and its wave hit a
    ///   structure once.
    /// - <b>A break</b> opens its tiles to the hero at once and patches the enemies' nav grid
    ///   (<see cref="Horde.PatchNav"/>), on the hit's frame.
    /// - <b>Regrowth</b>: a zone change (<see cref="Zones"/>) marks the structures broken then;
    ///   on the next downbeat they regrow at their sites over one beat, blocking their tiles from
    ///   its start, unless the hero is within <see cref="RegrowClearM"/>: that site waits for the
    ///   next zone change. Enemies on a regrown site are pushed out (<see cref="Horde.PushOff"/>).
    ///   A structure still growing only glints. While the world is frozen, regrowth waits for the
    ///   next downbeat after the thaw.
    /// </summary>
    internal sealed class Structures
    {
        public const double RegrowClearM = 3;
        public const int LargeThreeHitTiles = 9;

        readonly ArenaGrid arena;
        readonly SimState state;
        readonly Horde horde;
        readonly Tile[][] tiles;
        readonly StructureState[] sites;
        // Each site's tiles' bounds, ground metres, to skip far sites quickly.
        readonly double[] minX, minY, maxX, maxY;
        // The latest cleave wave to hit each site, so a heavy strike and its wave hit it once.
        readonly int[] lastWave;
        readonly bool[] regrowDue;
        bool anyDue;

        public Structures(ArenaData data, ArenaGrid arena, SimState state, Horde horde)
        {
            this.arena = arena;
            this.state = state;
            this.horde = horde;
            int n = data.Structures.Length;
            tiles = new Tile[n][];
            sites = new StructureState[n];
            state.StructureStore = sites;
            minX = new double[n];
            minY = new double[n];
            maxX = new double[n];
            maxY = new double[n];
            lastWave = new int[n];
            regrowDue = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var site = data.Structures[i];
                tiles[i] = (Tile[])site.Tiles.Clone();
                minX[i] = minY[i] = double.PositiveInfinity;
                maxX[i] = maxY[i] = double.NegativeInfinity;
                double sx = 0, sy = 0;
                foreach (var t in tiles[i])
                {
                    double x0 = arena.OriginX + t.X * arena.TileM, y0 = arena.OriginY + t.Y * arena.TileM;
                    minX[i] = Math.Min(minX[i], x0);
                    minY[i] = Math.Min(minY[i], y0);
                    maxX[i] = Math.Max(maxX[i], x0 + arena.TileM);
                    maxY[i] = Math.Max(maxY[i], y0 + arena.TileM);
                    sx += arena.CentreX(t.X);
                    sy += arena.CentreY(t.Y);
                }
                lastWave[i] = -1;
                sites[i] = new StructureState
                {
                    Standing = true,
                    Large = site.Large,
                    HitsToBreak = !site.Large ? 1 : tiles[i].Length >= LargeThreeHitTiles ? 3 : 2,
                    CentreX = sx / tiles[i].Length,
                    CentreY = sy / tiles[i].Length,
                    BrokenAt = double.NegativeInfinity,
                    GrowStart = double.NegativeInfinity,
                    GrowEnd = double.NegativeInfinity,
                };
            }
        }

        static bool Breaks(StructureHit source) => source != StructureHit.Strike;

        /// <summary>
        /// A hit at <paramref name="t"/> over the disc of radius <paramref name="reach"/> around
        /// (x, y): every standing structure with a tile in it glints or takes a breaking hit.
        /// <paramref name="wave"/> names the heavy strike's cleave wave (-1 none), so its strike and
        /// bands hit each structure once; <paramref name="force"/> is the break's throw, metres.
        /// </summary>
        internal void Strike(Simulation sim, double t, double x, double y, double reach, StructureHit source, double force, int wave = -1)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                ref var s = ref sites[i];
                if (!s.Standing) continue;
                if (x < minX[i] - reach || x > maxX[i] + reach || y < minY[i] - reach || y > maxY[i] + reach) continue;
                if (!Nearest(i, x, y, out double px, out double py, out double d) || d > reach) continue;
                if (wave >= 0)
                {
                    if (lastWave[i] == wave) continue;
                    lastWave[i] = wave;
                }
                double dx = px - x, dy = py - y;
                if (d < 1e-9)
                {
                    // The hit's centre is inside the structure: outward through its middle.
                    dx = s.CentreX - x;
                    dy = s.CentreY - y;
                }
                double l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9)
                {
                    dx = 1;
                    l = 1;
                }
                dx /= l;
                dy /= l;
                if (!Breaks(source) || t < s.GrowEnd)
                {
                    sim.Emit(SimEvent.Glint(t, i, source, 0, px, py, dx, dy));
                    continue;
                }
                if (++s.Hits < s.HitsToBreak && source != StructureHit.Debug)
                {
                    sim.Emit(SimEvent.Glint(t, i, source, s.Hits, px, py, dx, dy));
                    continue;
                }
                s.Standing = false;
                s.Hits = 0;
                s.BrokenAt = t;
                arena.SetStructure(tiles[i], false);
                horde?.PatchNav(tiles[i], false);
                sim.Emit(SimEvent.StructureBreak(t, i, source, px, py, dx, dy, force));
            }
        }

        /// <summary>The point of structure <paramref name="i"/> nearest (x, y), and how far it is (0 inside).</summary>
        bool Nearest(int i, double x, double y, out double px, out double py, out double d)
        {
            px = py = 0;
            double best = double.PositiveInfinity;
            foreach (var t in tiles[i])
            {
                double x0 = arena.OriginX + t.X * arena.TileM, y0 = arena.OriginY + t.Y * arena.TileM;
                double cx = Math.Max(x0, Math.Min(x, x0 + arena.TileM)), cy = Math.Max(y0, Math.Min(y, y0 + arena.TileM));
                double d2 = (cx - x) * (cx - x) + (cy - y) * (cy - y);
                if (d2 >= best) continue;
                best = d2;
                px = cx;
                py = cy;
            }
            d = Math.Sqrt(best);
            return best < double.PositiveInfinity;
        }

        /// <summary>A zone change: the structures broken now regrow on the next downbeat.</summary>
        internal void ZoneChanged()
        {
            anyDue = false;
            for (int i = 0; i < sites.Length; i++)
            {
                regrowDue[i] = !sites[i].Standing;
                anyDue |= regrowDue[i];
            }
        }

        /// <summary>
        /// A downbeat at <paramref name="t"/>, <paramref name="period"/> long: the structures a zone
        /// change marked regrow, but for those the hero is near, which wait for the next change.
        /// </summary>
        internal void Downbeat(Simulation sim, double t, double period)
        {
            if (!anyDue || state.Frozen) return;
            anyDue = false;
            var hero = state.Hero;
            hero.PositionAt(t, out double hx, out double hy);
            bool grew = false;
            for (int i = 0; i < sites.Length; i++)
            {
                if (!regrowDue[i]) continue;
                regrowDue[i] = false;
                // The hero's place now, and the tile it stands on or is striding to.
                Nearest(i, hx, hy, out _, out _, out double d);
                Nearest(i, hero.X, hero.Y, out _, out _, out double dTile);
                if (Math.Min(d, dTile) <= RegrowClearM + 1e-9) continue;
                ref var s = ref sites[i];
                s.Standing = true;
                s.Hits = 0;
                s.GrowStart = t;
                s.GrowEnd = t + period;
                arena.SetStructure(tiles[i], true);
                horde?.PatchNav(tiles[i], true);
                sim.Emit(SimEvent.Regrowth(t, i, t, t + period));
                grew = true;
            }
            if (grew) horde?.PushOff(sim, t);
        }
    }
}
