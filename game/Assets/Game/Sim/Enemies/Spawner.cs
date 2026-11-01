using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Who enters the horde, when and where (unity-game.md, Enemies; native's survivors spawning.rs
    /// and engine/spawning.rs). The run clock decides who spawns; the ring decides where.
    ///
    /// - <b>Waves</b>: every interval of run clock one of each roster role spawns, then whole
    ///   rosters while fewer than the minimum are alive. A pass the horde's room cuts short starts
    ///   one role further along the roster each wave (native's always starts at its first), so a
    ///   crowd at the cap stays the roster's mix. In a run (<see cref="HordeConfig.Schedule"/>)
    ///   the roster, interval and minimum are the current minute's of schedule.json, the minimum
    ///   scaled by the loudness level as native's <c>levelScale</c> (quiet 0.6, loud 1.3, blended
    ///   over the last 2 s), and a quiet wave is skipped once the horde is that full. Otherwise
    ///   they are the slice's fixed roster.
    /// - <b>Clumps</b>: each role's share of a wave lands as one clump, and a role keeps landing
    ///   beside the same point of the ring for <see cref="Schedule.ClumpS"/> after choosing it,
    ///   so a trickle of one a wave still gathers into a mass of one colour.
    /// - <b>Bosses</b>: a minute's <c>bosses</c> are due at its start and land on the next
    ///   downbeat (native's boss bar), or at a tick end when no downbeat comes within
    ///   <see cref="BossFallbackS"/> of run clock, just inside the view's edge so they are seen to
    ///   arrive. Waves leave room for bosses still waiting; a boss with no room waits for one.
    /// - <b>Health</b>: a boss's, and a level-scaled role's, is its role's times the hero's level.
    /// - <b>The ring</b>: new enemies land just outside the camera's footprint in walkable cells,
    ///   and enemies standing far out of sight are recycled onto it (native's).
    /// Nothing spawns while the world is frozen or after the run clock's end.
    /// </summary>
    internal sealed class Spawner
    {
        public const double SpawnMarginM = 2;
        public const double SpawnSpreadM = 1.5;
        /// <summary>Enemies this many view widths from the hero are recycled onto the ring (native's).</summary>
        public const double RecycleViews = 1.5;
        /// <summary>How far toward the footprint's edge a boss lands: inside the quiet zoom's narrower view too.</summary>
        public const double BossEdgeShare = 0.7;
        /// <summary>A due boss lands at a tick end once this much run clock passes without a downbeat.</summary>
        public const double BossFallbackS = 4;
        /// <summary>How far from its clump's point, in radians along the ring, a role's spawns land.</summary>
        const double ClumpWander = 0.12;
        const int SpawnTries = 16;
        /// <summary>The lineup's distance ahead of the hero and the clear ground between its bodies, metres.</summary>
        public const double LineupAheadM = 5, LineupGapM = 2.5;

        readonly Horde horde;
        readonly BeliefLog log;
        readonly SimState state;
        readonly EnemyRole[] roles;
        readonly Schedule schedule;
        readonly bool run;
        readonly int[] sliceRoster, lineup;
        readonly double sliceInterval;
        readonly int sliceMinimum;
        readonly int[][] minuteRosters, minuteBosses;
        readonly double[] footprint;
        readonly double durationS;
        // Each role's clump: its point's angle around the hero and the run clock it was chosen at.
        readonly double[] clumpAngle, clumpBorn;
        // Bosses due and waiting to land: their roles and minutes.
        int[] bossRole, bossMinute;
        int bossCount;
        double bossDue;
        // The minutes whose treasure a boss already carries (native's one chest a minute).
        long treasureMinutes;
        double nextWave;
        // Whose turn it is to lead a wave's last, short pass.
        int waveTurn;
        int minute = -1;

        public Spawner(HordeConfig config, GameContent content, Horde horde, BeliefLog log, SimState state)
        {
            this.horde = horde;
            this.log = log;
            this.state = state;
            roles = content.Roles;
            schedule = content.Schedule;
            run = config.Schedule;
            durationS = run ? schedule.DurationS : double.PositiveInfinity;
            sliceRoster = new int[config.Roster.Length];
            for (int i = 0; i < sliceRoster.Length; i++) sliceRoster[i] = content.RoleIndex(config.Roster[i]);
            lineup = Array.ConvertAll(config.Lineup, content.RoleIndex);
            if (Array.IndexOf(lineup, -1) >= 0) throw new ArgumentException("a lineup of the content's roles");
            if (config.SpawnIntervalS <= 0) throw new ArgumentException("a positive spawn interval");
            sliceInterval = config.SpawnIntervalS;
            sliceMinimum = config.Minimum;
            minuteRosters = new int[schedule.Minutes.Length][];
            minuteBosses = new int[schedule.Minutes.Length][];
            int bosses = 0;
            for (int m = 0; m < minuteRosters.Length; m++)
            {
                var row = schedule.Minutes[m];
                minuteRosters[m] = Array.ConvertAll(row.Enemies, content.RoleIndex);
                minuteBosses[m] = Array.ConvertAll(row.Bosses, content.RoleIndex);
                bosses += row.Bosses.Length;
            }
            if (minuteRosters.Length > 64) throw new ArgumentException("at most 64 minutes");
            bossRole = new int[bosses];
            bossMinute = new int[bosses];
            footprint = (double[])config.CameraFootprint.Clone();
            if (footprint.Length < 6 || footprint.Length % 2 != 0) throw new ArgumentException("a camera footprint of three or more points");
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            for (int k = 0; k < footprint.Length; k += 2)
            {
                minX = Math.Min(minX, footprint[k]);
                maxX = Math.Max(maxX, footprint[k]);
                minY = Math.Min(minY, footprint[k + 1]);
                maxY = Math.Max(maxY, footprint[k + 1]);
            }
            ViewMinX = minX;
            ViewMaxX = maxX;
            ViewMinY = minY;
            ViewMaxY = maxY;
            ViewWidth = maxX - minX;
            clumpAngle = new double[roles.Length];
            clumpBorn = new double[roles.Length];
            for (int i = 0; i < roles.Length; i++) clumpBorn[i] = double.NegativeInfinity;
        }

        /// <summary>The footprint's width: the view the recycling distance is measured in.</summary>
        public double ViewWidth { get; }
        /// <summary>The footprint's bounds around the hero, metres.</summary>
        public double ViewMinX { get; }
        public double ViewMaxX { get; }
        public double ViewMinY { get; }
        public double ViewMaxY { get; }

        /// <summary>Scheduled bosses due and waiting to land, whose room waves and events leave free.</summary>
        public int BossesWaiting => bossCount;

        /// <summary>Is (x, y) beyond the footprint around the hero at (hx, hy), by the spawn margin?</summary>
        public bool OutOfSight(double x, double y, double hx, double hy)
        {
            double dx = x - hx, dy = y - hy, d = Math.Sqrt(dx * dx + dy * dy);
            return d > 1e-9 && d > FootprintEdge(dx / d, dy / d) + SpawnMarginM;
        }

        /// <summary>Is (x, y) within <paramref name="share"/> of the way from the hero at (hx, hy) to the footprint's edge?</summary>
        public bool InView(double x, double y, double hx, double hy, double share)
        {
            double dx = x - hx, dy = y - hy, d = Math.Sqrt(dx * dx + dy * dy);
            return d < 1e-9 || d <= share * FootprintEdge(dx / d, dy / d);
        }

        bool Spawning => !state.Frozen && state.RunClock < durationS - 1e-9;

        /// <summary>At the tick's end: the minute's bosses come due, waves spawn, bosses past the fallback land, and far enemies recycle.</summary>
        public void Tick(Simulation sim, double end, double hx, double hy)
        {
            if (Spawning)
            {
                if (run) EnterMinutes();
                if (bossCount > 0 && state.RunClock >= bossDue + BossFallbackS - 1e-9) LandBosses(sim, end, hx, hy);
                while (state.RunClock >= nextWave - 1e-9)
                {
                    Wave(sim, end, hx, hy);
                    nextWave += run ? schedule.Minutes[minute].IntervalS : sliceInterval;
                }
            }
            Recycle(sim, end, hx, hy);
        }

        /// <summary>On a committed downbeat: the bosses due land on it.</summary>
        public void Downbeat(Simulation sim, double t)
        {
            if (bossCount == 0 || !Spawning) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            LandBosses(sim, t, hx, hy);
        }

        /// <summary>
        /// The run clock jumped to <paramref name="runClock"/> (<see cref="RunRules.JumpTo"/>): the minutes passed bring
        /// no bosses, the new minute's come due at the next tick, and waves go on from now.
        /// </summary>
        public void Skip(double runClock)
        {
            if (run) minute = Math.Max(minute, Math.Min(schedule.Minutes.Length - 1, (int)Math.Floor(runClock / 60 + 1e-9)) - 1);
            nextWave = Math.Max(nextWave, runClock);
        }

        /// <summary>
        /// The debug surfaces' boss (<see cref="DebugCommand.SpawnBoss"/>): <paramref name="role"/> due now, at the current
        /// minute, landing as the schedule's bosses do, with that minute's treasure if no boss carried it yet.
        /// </summary>
        public void Summon(int role)
        {
            if (role < 0 || role >= roles.Length) return;
            if (bossCount == bossRole.Length)
            {
                Array.Resize(ref bossRole, bossRole.Length + 4);
                Array.Resize(ref bossMinute, bossMinute.Length + 4);
            }
            if (bossCount == 0) bossDue = state.RunClock;
            bossRole[bossCount] = role;
            bossMinute[bossCount++] = minute;
        }

        /// <summary>
        /// The lineup (<see cref="HordeConfig.Lineup"/>) at the sim's start: a row across the hero's
        /// view <see cref="LineupAheadM"/> ahead of it (+y), each body clear of the next, schedule.json's
        /// boss-move roles as bosses.
        /// </summary>
        public void PlaceLineup(Simulation sim)
        {
            if (lineup.Length == 0) return;
            var moverRoles = schedule.BossMoves.Roles;
            double width = 0;
            foreach (int role in lineup) width += LineupGapM + 2 * roles[role].RadiusM;
            double x = state.Hero.X - width / 2;
            foreach (int role in lineup)
            {
                double half = LineupGapM / 2 + roles[role].RadiusM;
                x += half;
                horde.Spawn(sim, sim.Now, role, x, state.Hero.Y + LineupAheadM, boss: Array.IndexOf(moverRoles, roles[role].Id) >= 0);
                x += half;
            }
        }

        /// <summary>The run clock reaches a new minute: its bosses come due (a minute is never skipped).</summary>
        void EnterMinutes()
        {
            int now = Math.Min(schedule.Minutes.Length - 1, (int)Math.Floor(state.RunClock / 60 + 1e-9));
            while (minute < now)
            {
                minute++;
                if (minuteBosses[minute].Length > 0 && bossCount == 0) bossDue = state.RunClock;
                foreach (int role in minuteBosses[minute])
                {
                    bossRole[bossCount] = role;
                    bossMinute[bossCount++] = minute;
                }
            }
        }

        void LandBosses(Simulation sim, double t, double hx, double hy)
        {
            int landed = 0;
            while (landed < bossCount && horde.FreeSlots > 0)
            {
                int role = bossRole[landed];
                InViewPoint(sim.Random, hx, hy, out double x, out double y);
                int m = bossMinute[landed], tier = 0;
                var treasure = m >= 0 ? schedule.Minutes[m].Treasure : null;
                if (treasure != null && (treasureMinutes & (1L << m)) == 0)
                {
                    treasureMinutes |= 1L << m;
                    tier = TreasureTier(sim.Random, treasure);
                }
                horde.Spawn(sim, t, role, x, y, boss: true, tier: tier, minute: m);
                landed++;
            }
            if (landed == 0) return;
            bossCount -= landed;
            Array.Copy(bossRole, landed, bossRole, 0, bossCount);
            Array.Copy(bossMinute, landed, bossMinute, 0, bossCount);
            bossDue = state.RunClock;
        }

        /// <summary>
        /// Where a boss lands: <see cref="BossEdgeShare"/> of the way to the footprint's edge from the hero
        /// at (hx, hy), in a random direction on walkable ground (the hero's own place if none is found),
        /// so it is seen to arrive.
        /// </summary>
        public void InViewPoint(SimRandom random, double hx, double hy, out double x, out double y)
        {
            double a = random.Range(-Math.PI, Math.PI);
            x = hx;
            y = hy;
            for (int tries = 0; tries < SpawnTries; tries++, a = random.Range(-Math.PI, Math.PI))
            {
                double c = Math.Cos(a), s = Math.Sin(a);
                double d = BossEdgeShare * FootprintEdge(c, s);
                if (!horde.Nav.Walkable(hx + c * d, hy + s * d)) continue;
                x = hx + c * d;
                y = hy + s * d;
                return;
            }
        }

        /// <summary>A chest's tier (native's treasure_tier): the scripted one, else tier 3, 2 and 1's chances rolled in turn; 0 for none.</summary>
        static int TreasureTier(SimRandom random, Treasure t)
        {
            if (t.Tier > 0) return t.Tier;
            if (random.NextDouble() * 100 < t.Tier3) return 3;
            if (random.NextDouble() * 100 < t.Tier2) return 2;
            return random.NextDouble() * 100 < t.Tier1 ? 1 : 0;
        }

        /// <summary>
        /// One of each roster role, then whole rosters while fewer than the minimum are alive
        /// (Vampire Survivors' spawning); slots kept for waiting bosses stay free.
        /// </summary>
        void Wave(Simulation sim, double t, double hx, double hy)
        {
            int[] roster = run ? minuteRosters[minute] : sliceRoster;
            if (roster.Length == 0) return;
            int target = sliceMinimum;
            if (run)
            {
                double scale = Math.Max(0.6, Math.Min(1.3, LevelScale(t)));
                target = (int)Math.Ceiling(schedule.Minutes[minute].Minimum * scale - 1e-9);
                if (scale < 0.99 && state.EnemiesAlive >= Math.Min(target, horde.FreeSlots + state.EnemiesAlive - bossCount)) return;
            }
            // Whole passes over the roster, the last one cut short by the room left: its places go round the
            // roster from wave to wave, so at the cap no role takes every free slot by its place in the list.
            int shortfall = target - state.EnemiesAlive - roster.Length;
            int passes = 1 + (shortfall > 0 ? (shortfall + roster.Length - 1) / roster.Length : 0);
            int total = Math.Min(passes * roster.Length, Math.Max(0, horde.FreeSlots - bossCount));
            int first = waveTurn++ % roster.Length;
            for (int j = 0; j < roster.Length; j++)
            {
                int role = roster[(first + j) % roster.Length];
                int n = total / roster.Length + (j < total % roster.Length ? 1 : 0);
                if (n == 0) return;
                double clump = Math.Max(1.5, 0.5 * Math.Sqrt(n));
                if (!ClumpPoint(sim.Random, role, hx, hy, clump + roles[role].RadiusM, out double cx, out double cy)) continue;
                for (int k = 0; k < n; k++)
                {
                    double x = cx, y = cy;
                    for (int tries = 0; tries < 4; tries++)
                    {
                        double a = sim.Random.Range(-Math.PI, Math.PI), r = clump * Math.Sqrt(sim.Random.NextDouble());
                        double px = cx + Math.Cos(a) * r, py = cy + Math.Sin(a) * r;
                        if (!horde.Nav.Walkable(px, py)) continue;
                        x = px;
                        y = py;
                        break;
                    }
                    horde.Spawn(sim, t, role, x, y);
                }
            }
        }

        /// <summary>
        /// The loudness level's scale on a wave's minimum at song time <paramref name="t"/>: native's
        /// blend of the levels revealed over the last <see cref="Schedule.LevelSmoothS"/>, medium
        /// before the first tick and for a tick without a level.
        /// </summary>
        double LevelScale(double t) => Blend(t, schedule.QuietScale, schedule.MediumScale, schedule.LoudScale);

        /// <summary>The loudness level at song time <paramref name="t"/> as a number, quiet 0, medium 1 and loud 2, blended as <see cref="LevelScale"/> is (the set pieces' cue).</summary>
        public double MeanLevel(double t) => Blend(t, 0, 1, 2);

        /// <summary>The levels revealed over the last <see cref="Schedule.LevelSmoothS"/> before <paramref name="t"/>, each as its value, blended over time.</summary>
        double Blend(double t, double quiet, double medium, double loud)
        {
            var ticks = log.Ticks;
            double window = schedule.LevelSmoothS, t0 = t - window, hi = t, sum = 0;
            int i = ticks.Length - 1;
            while (i >= 0 && ticks[i].T > t + 1e-9) i--;
            if (i < 0) return medium;
            for (; i >= 0 && hi > t0 + 1e-12; i--)
            {
                double lo = Math.Max(ticks[i].T, t0);
                var level = ticks[i].AsTick.Level;
                sum += (hi - lo) * (level == LoudnessLevel.Quiet ? quiet : level == LoudnessLevel.Loud ? loud : medium);
                hi = lo;
            }
            if (hi > t0 + 1e-12) sum += (hi - t0) * medium;
            return sum / window;
        }

        /// <summary>
        /// Where role <paramref name="role"/>'s clump lands: beside its point on the ring for
        /// <see cref="Schedule.ClumpS"/> after the point was chosen, then at a fresh point
        /// (<see cref="RingPoint"/>), so the masses come from all round.
        /// </summary>
        bool ClumpPoint(SimRandom random, int role, double hx, double hy, double extra, out double x, out double y)
        {
            if (state.RunClock - clumpBorn[role] <= schedule.ClumpS &&
                RingPointAt(random, clumpAngle[role] + random.Range(-ClumpWander, ClumpWander), hx, hy, extra, out x, out y))
                return true;
            for (int tries = 0; tries < SpawnTries; tries++)
            {
                double a = random.Range(-Math.PI, Math.PI);
                if (!RingPointAt(random, a, hx, hy, extra, out x, out y)) continue;
                clumpAngle[role] = a;
                clumpBorn[role] = state.RunClock;
                return true;
            }
            x = y = 0;
            return false;
        }

        /// <summary>Enemies standing far out of sight re-enter on the ring with their health (native's recycling).</summary>
        void Recycle(Simulation sim, double t, double hx, double hy)
        {
            double far = RecycleViews * ViewWidth;
            var enemies = state.EnemyStore;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || e.Motion != EnemyMotion.Still || horde.Scripts.Scripted(i)) continue;
                double dx = e.X - hx, dy = e.Y - hy;
                if (dx * dx + dy * dy < far * far) continue;
                if (!RingPoint(sim.Random, hx, hy, roles[e.Role].RadiusM, out double x, out double y)) continue;
                e.X = x;
                e.Y = y;
                e.LungeBeat = -1;
                horde.Placed(i, t, x, y);
                sim.Emit(SimEvent.Spawn(t, i, e.Role, x, y, recycled: true, boss: e.Boss));
            }
        }

        /// <summary>
        /// A point just outside the camera's footprint around the hero at (hx, hy), <paramref name="extra"/>
        /// beyond the margin, in a walkable cell: a random direction, retried when the point falls
        /// outside the arena or in a structure, so a visible arena edge leaves the in-bounds arc.
        /// </summary>
        bool RingPoint(SimRandom random, double hx, double hy, double extra, out double x, out double y)
        {
            for (int tries = 0; tries < SpawnTries; tries++)
                if (RingPointAt(random, random.Range(-Math.PI, Math.PI), hx, hy, extra, out x, out y)) return true;
            x = y = 0;
            return false;
        }

        bool RingPointAt(SimRandom random, double a, double hx, double hy, double extra, out double x, out double y)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            double d = FootprintEdge(c, s) + SpawnMarginM + extra + random.Range(0, SpawnSpreadM);
            x = hx + c * d;
            y = hy + s * d;
            return horde.Nav.Walkable(x, y);
        }

        /// <summary>How far the ray from the hero along (c, s) runs to the footprint's edge.</summary>
        public double FootprintEdge(double c, double s)
        {
            double best = 0;
            int n = footprint.Length / 2;
            for (int k = 0; k < n; k++)
            {
                double ax = footprint[2 * k], ay = footprint[2 * k + 1];
                double ex = footprint[2 * ((k + 1) % n)] - ax, ey = footprint[2 * ((k + 1) % n) + 1] - ay;
                double denom = c * ey - s * ex;
                if (Math.Abs(denom) < 1e-12) continue;
                double r = (ax * ey - ay * ex) / denom;
                double u = (ax * s - ay * c) / denom;
                if (r > 0 && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Max(best, r);
            }
            return best;
        }
    }
}
