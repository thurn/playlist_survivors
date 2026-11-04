using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>How <see cref="Fairness.Check"/> runs a fight.</summary>
    public sealed class FairnessOptions
    {
        /// <summary>The tempo it's played at: check every boss at the fastest it supports, and at its slowest.</summary>
        public double Bpm = 120;
        /// <summary>The act (its telegraph minimums) and the run's seed.</summary>
        public int Act = 1;
        public ulong Seed = 1;
        /// <summary>The boss is worn down evenly to nothing over this long, so every phase and burst plays.</summary>
        public double FightS = 90;
        /// <summary>The hero walks to a new random spot in the arena this often (0: it stands still).</summary>
        public double WanderS = 2;
        /// <summary>The share of those spots taken beside the boss instead (chasing it: its close-range moves play).</summary>
        public double ChaseShare;
        /// <summary>
        /// Where the hero may stand, from the fight's centre (x, y relative); null: a circle of
        /// <see cref="ArenaRadiusM"/>. The arena bead's outline goes here.
        /// </summary>
        public Func<double, double, bool> Inside;
        public double ArenaRadiusM = 20;
        /// <summary>Half the side of a square round the centre holding the whole arena (NaN: <see cref="ArenaRadiusM"/>).</summary>
        public double ExtentM = double.NaN;
        /// <summary>The screen round the hero, as the circle it contains (the boss's arrival ring's screen; boss-rush.md "No off-screen hits").</summary>
        public double ScreenRadiusM = 14;
        /// <summary>The solver's grid and step. The walk covers two cells a step.</summary>
        public double StepS = 1.0 / 12;
        /// <summary>Fight-specific setup: health, centre (the act comes from <see cref="Act"/>).</summary>
        public FightSetup Setup;
        /// <summary>
        /// The fight's arena (arenas.json), formed round the hero before the fight starts, as the Boss Rush run forms it: its
        /// walls and props then block the hero, and where it may stand is the arena's walkable floor (round the arena's
        /// centre, not the fight's), unless <see cref="Inside"/> says otherwise. Null: no arena.
        /// </summary>
        public string Arena;
    }

    /// <summary>One fight's fairness: every hazard it laid, the problems found, and its dash waves.</summary>
    public sealed class FairnessReport
    {
        /// <summary>Every hazard the fight laid that landed (those called off first never hurt).</summary>
        public readonly List<Hazard> Hazards = new List<Hazard>();
        public readonly List<string> Problems = new List<string>();
        /// <summary>The attack sequences proven (attacks overlapping in time are one).</summary>
        public int Sequences;
        /// <summary>Dash-wave attacks, and those walking alone can't escape from where the hero stood (real dash waves).</summary>
        public int DashWaves, DashWavesNeedingDash;
        /// <summary>The fight ended (every body fell) within the run.</summary>
        public bool Finished;

        public override string ToString() =>
            $"{Hazards.Count} hazards, {Sequences} sequences, {DashWaves} dash waves ({DashWavesNeedingDash} need a dash), finished {Finished}" +
            (Problems.Count == 0 ? "" : ":\n" + string.Join("\n", Problems.Take(30)));
    }

    /// <summary>
    /// The fairness harness (boss-rush.md, "Fair fights"): every fight bead's tests run their boss through it.
    ///
    /// <code>
    /// var report = Fairness.Check(() => new Bloom(), new FairnessOptions { Bpm = 180, Act = 1 });
    /// Fairness.AssertFair(report);
    /// Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0));
    /// </code>
    ///
    /// It plays the fight in a real sim on a fixed-tempo song: the hero immortal and wandering the arena, the boss worn
    /// down evenly over <see cref="FairnessOptions.FightS"/>. It records every hazard, then checks:
    ///
    /// - <b>Reaction time</b>: each telegraph at least the act's beats (3 in acts 1–2, 2 later) and 0.6 s.
    /// - <b>The grid</b>: each impact on a beat or half beat.
    /// - <b>No off-screen hits</b>: each hazard's ground or its body within the screen round the hero when it starts filling.
    /// - <b>Dash waves spaced</b> at least the dash cooldown plus a beat apart.
    /// - <b>A way out</b>: for each sequence of attacks overlapping in time, from <i>every</i> spot in the arena (a superset of
    ///   where the previous attack could leave the hero), a hero at the base 6 m/s with one baseline dash (its i-frames,
    ///   its 0.6 s cooldown, on cooldown at the start if the previous sequence's last impact was that recent) has a
    ///   hit-free path. It's a backward reachability over a grid (<see cref="FairnessOptions.StepS"/>): each hazard is swept
    ///   across the half step either side of each step against the hero's body grown by a cell, so nothing tunnels and
    ///   the proof is a little stricter than the game. The dash's i-frames beat any instant impact, as in the game.
    /// </summary>
    public static class Fairness
    {
        static GameContent content;
        public static GameContent Content => content ??= GameContent.Load(Checkout.In("Content"));

        public static void AssertFair(FairnessReport report)
        {
            Assert.IsEmpty(report.Problems, report.ToString());
            Assert.That(report.Sequences, Is.GreaterThan(0), "the fight laid no hazards");
        }

        public static FairnessReport Check(Func<BossModule> make, FairnessOptions o = null)
        {
            o ??= new FairnessOptions();
            var report = new FairnessReport();
            var run = new RunInputs { Seed = o.Seed, Loadout = new Upgrade[0] };
            var config = run.Build(Content);
            config.Hero.Health = 1e9;
            if (o.Arena != null) config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            double songS = o.FightS + 40;
            foreach (var k in Drive.Known(new FixtureSong(o.Seed).Beats(0, songS, o.Bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            if (o.Arena != null) sim.Arena.FormAroundHero(sim.Arena.Rules.Find(o.Arena) ?? throw new ArgumentException($"no arena {o.Arena}"), sim.Now);
            var setup = o.Setup ?? new FightSetup();
            setup.Act = o.Act;
            var fight = sim.Fight;
            fight.Start(make(), sim.Now, setup);
            var seen = new HashSet<int>();
            var heroAtStart = new Dictionary<int, (double X, double Y, double BX, double BY)>();
            var rng = new Random((int)o.Seed);
            double arrived = double.NaN, nextWander = 0, nextStrike = 0;
            // Where the hero may stand and wander, round (cx, cy): the arena's centre and floor, or the fight's centre and a circle.
            double cx = fight.CentreX, cy = fight.CentreY, wander = o.ArenaRadiusM * 0.7, extent = double.IsNaN(o.ExtentM) ? o.ArenaRadiusM : o.ExtentM;
            Func<double, double, bool> inside = o.Inside ?? ((x, y) => x * x + y * y <= o.ArenaRadiusM * o.ArenaRadiusM);
            if (o.Arena != null)
            {
                var arena = sim.Arena;
                cx = arena.OriginX;
                cy = arena.OriginY;
                arena.Def.Outline.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (double.IsNaN(o.ExtentM)) extent = Math.Max(Math.Max(-x0, x1), Math.Max(-y0, y1));
                wander = extent * 0.7;
                double r = config.Hero.RadiusM;
                if (o.Inside == null) inside = (x, y) => arena.Walkable(cx + x, cy + y, r);
            }
            while (sim.Now < songS - 1)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                foreach (var h in fight.Hazards)
                    if (seen.Add(h.Id))
                    {
                        report.Hazards.Add(h);
                        sim.HeroWas(Math.Max(h.Telegraph.Start, sim.Now - 0.5), out double hx, out double hy);
                        double bx = h.Body >= 0 ? fight.Bodies[h.Body].X : h.Shape.X, by = h.Body >= 0 ? fight.Bodies[h.Body].Y : h.Shape.Y;
                        heroAtStart[h.Id] = (hx, hy, bx, by);
                    }
                if (fight.Stage == FightStage.Dead)
                {
                    report.Finished = true;
                    break;
                }
                if (!fight.InFight) continue;
                if (double.IsNaN(arrived)) arrived = sim.Now;
                if (o.WanderS > 0 && sim.Now >= nextWander)
                {
                    nextWander = sim.Now + o.WanderS;
                    double x, y;
                    bool chase = rng.NextDouble() < o.ChaseShare;
                    ref readonly var lead = ref fight.Lead;
                    do
                    {
                        x = chase ? lead.X - cx + rng.NextDouble() * 4 - 2 : (rng.NextDouble() * 2 - 1) * wander;
                        y = chase ? lead.Y - cy + rng.NextDouble() * 4 - 2 : (rng.NextDouble() * 2 - 1) * wander;
                        chase = false;
                    } while (!inside(x, y));
                    sim.Target(cx + x, cy + y, sim.Now);
                }
                if (sim.Now >= nextStrike && !fight.Untouchable && fight.Stage == FightStage.Fighting)
                {
                    nextStrike = sim.Now + 0.25;
                    double want = Math.Max(0, 1 - (sim.Now - arrived) / o.FightS), share = fight.HealthShare;
                    if (share > want + 1e-6)
                    {
                        ref readonly var b = ref fight.Lead;
                        double damage = Math.Min(sim.Enemies[b.Enemy].Health + 1, (share - want) * fight.MaxHealth + (want == 0 ? 1 : 0));
                        sim.Strikes.Land(sim, new Blow
                        {
                            Shape = HitShape.Circle(b.X, b.Y, 0.1), T = sim.Now, Damage = damage, Tags = HitTags.Strike,
                            Source = BlowSource.SwordSlash, Owner = -1, Part = -1,
                        });
                    }
                }
            }
            // A hazard called off before landing (a phase, a death) never hurts: it isn't checked.
            var calledOff = new HashSet<int>(Drive.Events(sim, SimEventKind.BossMove).Where(e => e.I2 != 0).Select(e => e.I0));
            report.Hazards.RemoveAll(h => calledOff.Contains(h.Id));
            var beats = Drive.Events(sim, SimEventKind.Beat).Select(e => e.T).ToList();
            // One beat past the last heard, so a half beat landing after it (the fight died within the beat) is judged.
            if (beats.Count >= 2) beats.Add(2 * beats[^1] - beats[^2]);
            var grid = beats.ToArray();
            var dash = config.Dashes;
            var form = dash.Kinds[dash.IndexOf(dash.Start)].Ranks[0];
            var hero = new HeroModel
            {
                SpeedMps = config.Hero.SpeedMps, RadiusM = config.Hero.RadiusM, DashM = form.DistanceM,
                DashS = form.DurationS, IFramesAfterS = dash.IFramesAfterS, CooldownS = dash.CooldownS,
            };
            Timing(report, grid, fight.MinTelegraphBeats, o);
            foreach (var h in report.Hazards)
            {
                var at = heroAtStart[h.Id];
                bool seen2 = h.ShapeAt(h.Telegraph.Start).Touches(at.X, at.Y, o.ScreenRadiusM) ||
                    Math.Sqrt((at.BX - at.X) * (at.BX - at.X) + (at.BY - at.Y) * (at.BY - at.Y)) <= o.ScreenRadiusM;
                if (!seen2) report.Problems.Add($"hazard {h.Id} (attack {h.Attack}) starts off screen");
            }
            DashSpacing(report, grid, hero);
            var box = (cx - extent, cy - extent, cx + extent, cy + extent);
            WayOut(report, hero, (x, y) => inside(x - cx, y - cy), box, o, heroAtStart);
            return report;
        }

        sealed class HeroModel
        {
            public double SpeedMps, RadiusM, DashM, DashS, IFramesAfterS, CooldownS;
        }

        static double BeatNear(double[] beats, double t)
        {
            int i = Array.BinarySearch(beats, t);
            if (i < 0) i = ~i;
            i = Math.Max(1, Math.Min(beats.Length - 1, i));
            return beats[i] - beats[i - 1];
        }

        static void Timing(FairnessReport report, double[] beats, int minBeats, FairnessOptions o)
        {
            foreach (var h in report.Hazards)
            {
                double warn = h.Telegraph.Land - h.Telegraph.Start, beatS = BeatNear(beats, h.Telegraph.Land);
                if (warn < minBeats * beatS - 0.02 || warn < BossFight.MinTelegraphS - 1e-6)
                    report.Problems.Add($"hazard {h.Id} (attack {h.Attack}) warns {warn:F2} s, under {minBeats} beats ({minBeats * beatS:F2} s) or 0.6 s");
                int i = Array.BinarySearch(beats, h.Telegraph.Land);
                if (i < 0) i = ~i;
                bool onGrid = false;
                for (int k = Math.Max(0, i - 1); k <= Math.Min(beats.Length - 1, i); k++)
                {
                    if (Math.Abs(beats[k] - h.Telegraph.Land) < 0.002) onGrid = true;
                    if (k + 1 < beats.Length && Math.Abs((beats[k] + beats[k + 1]) / 2 - h.Telegraph.Land) < 0.002) onGrid = true;
                    if (k > 0 && Math.Abs((beats[k - 1] + beats[k]) / 2 - h.Telegraph.Land) < 0.002) onGrid = true;
                }
                if (!onGrid) report.Problems.Add($"hazard {h.Id} (attack {h.Attack}) lands at {h.Telegraph.Land:F3}, off the beat and half beat");
            }
        }

        static void DashSpacing(FairnessReport report, double[] beats, HeroModel hero)
        {
            report.DashWaves = report.Hazards.Where(h => h.DashWave).Select(h => h.Attack).Distinct().Count();
            // Every front counts: a wave of several rings asks for a dash per ring.
            var lands = report.Hazards.Where(h => h.DashWave).Select(h => h.Telegraph.Land).OrderBy(t => t).ToList();
            lands = lands.Where((t, i) => i == 0 || t - lands[i - 1] > 1e-6).ToList();
            for (int i = 1; i < lands.Count; i++)
            {
                double need = hero.CooldownS + BeatNear(beats, lands[i]);
                if (lands[i] - lands[i - 1] < need - 1e-6)
                    report.Problems.Add($"dash waves at {lands[i - 1]:F2} and {lands[i]:F2} s are {lands[i] - lands[i - 1]:F2} s apart, under the cooldown and a beat ({need:F2} s)");
            }
        }

        // ---- The way out ----

        static void WayOut(FairnessReport report, HeroModel hero, Func<double, double, bool> inside,
            (double X0, double Y0, double X1, double Y1) box, FairnessOptions o,
            Dictionary<int, (double X, double Y, double BX, double BY)> heroAtStart)
        {
            // Attacks, then sequences of attacks overlapping in time.
            var attacks = report.Hazards.GroupBy(h => h.Attack)
                .Select(g => (Start: g.Min(h => h.Telegraph.Start), Until: g.Max(h => h.Until), List: g.ToList())).OrderBy(a => a.Start).ToList();
            var sequences = new List<(double Start, double Until, List<Hazard> List)>();
            foreach (var a in attacks)
            {
                if (sequences.Count > 0 && a.Start < sequences[^1].Until - 1e-9)
                {
                    var last = sequences[^1];
                    last.List.AddRange(a.List);
                    sequences[^1] = (last.Start, Math.Max(last.Until, a.Until), last.List);
                }
                else sequences.Add((a.Start, a.Until, new List<Hazard>(a.List)));
            }
            report.Sequences = sequences.Count;
            double lastImpact = double.NegativeInfinity;
            foreach (var s in sequences)
            {
                double cooling = Math.Max(0, lastImpact + hero.CooldownS - s.Start);
                var solver = new Solver(s.List, s.Start, s.Until, hero, inside, box, o.StepS);
                if (!solver.EveryStartEscapes(cooling, out double fx, out double fy))
                    report.Problems.Add($"attacks {string.Join(",", s.List.Select(h => h.Attack).Distinct())} at {s.Start:F2}-{s.Until:F2} s: " +
                        $"no hit-free path from ({fx:F1}, {fy:F1}){(cooling > 0 ? $" with the dash {cooling:F2} s from ready" : "")}");
                if (s.List.Any(h => h.DashWave))
                {
                    var wave = s.List.First(h => h.DashWave);
                    var at = heroAtStart[wave.Id];
                    if (!solver.Escapes(at.X, at.Y, dash: false)) report.DashWavesNeedingDash++;
                }
                lastImpact = Math.Max(lastImpact, s.List.Max(h => h.Telegraph.Land));
            }
        }

        /// <summary>
        /// Backward reachability for one sequence: <c>tol[k][cell]</c> is the most steps of dash cooldown the hero may
        /// still have at step k in the cell and survive the rest (-1: none). Safe in cooldown is monotone (less is never
        /// worse), so one number per cell holds it. Cells outside the hazards' reach are always safe.
        /// </summary>
        sealed class Solver
        {
            readonly int nx, ny, steps, cool, dashSteps;
            readonly double x0, y0, cell, dt;
            readonly sbyte[][] tol;
            readonly bool[] valid;
            readonly (int dx, int dy)[] walk, dashes;
            readonly Func<double, double, bool> inside;
            readonly List<Hazard> hazards;
            readonly double start, heroR;
            bool[][] danger;

            public Solver(List<Hazard> hazards, double start, double until, HeroModel hero, Func<double, double, bool> inside,
                (double X0, double Y0, double X1, double Y1) box, double stepS)
            {
                this.hazards = hazards;
                this.inside = inside;
                this.start = start;
                heroR = hero.RadiusM;
                dt = stepS;
                cell = hero.SpeedMps * dt / 2;
                steps = (int)Math.Ceiling((until - start) / dt) + 2;
                cool = (int)Math.Ceiling(hero.CooldownS / dt - 1e-9);
                dashSteps = Math.Max(1, (int)Math.Floor((hero.DashS + hero.IFramesAfterS) / dt + 1e-9));
                // The hazards' reach: their shapes' bounds over their lives, and the hero's body.
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var h in hazards)
                    for (double t = h.Telegraph.Land; ; t += dt)
                    {
                        h.ShapeAt(Math.Min(t, h.Until)).Bounds(out double cx, out double cy, out double r);
                        r += heroR + 2 * cell;
                        minX = Math.Min(minX, cx - r); maxX = Math.Max(maxX, cx + r);
                        minY = Math.Min(minY, cy - r); maxY = Math.Max(maxY, cy + r);
                        if (t >= h.Until) break;
                    }
                // Never past the arena: the hero can't stand there.
                minX = Math.Max(minX, box.X0 - cell); minY = Math.Max(minY, box.Y0 - cell);
                maxX = Math.Min(maxX, box.X1 + cell); maxY = Math.Min(maxY, box.Y1 + cell);
                x0 = minX;
                y0 = minY;
                nx = (int)Math.Ceiling((maxX - minX) / cell) + 1;
                ny = (int)Math.Ceiling((maxY - minY) / cell) + 1;
                valid = new bool[nx * ny];
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++) valid[j * nx + i] = inside(X(i), Y(j));
                var w = new List<(int, int)>();
                for (int j = -2; j <= 2; j++)
                    for (int i = -2; i <= 2; i++)
                        if (i * i + j * j <= 4) w.Add((i, j));
                walk = w.ToArray();
                dashes = Enumerable.Range(0, 16).Select(k =>
                {
                    double a = 2 * Math.PI * k / 16;
                    return ((int)Math.Round(Math.Cos(a) * hero.DashM / cell), (int)Math.Round(Math.Sin(a) * hero.DashM / cell));
                }).ToArray();
                tol = new sbyte[steps + 1][];
                Danger();
                Solve(true);
            }

            double X(int i) => x0 + i * cell;
            double Y(int j) => y0 + j * cell;

            /// <summary>
            /// The cells each hazard touches at step k: anywhere in the half step either side of it, the hero's body grown by
            /// a cell (half a step's walk), so nothing tunnels between steps. An instant impact counts at the step nearest it.
            /// </summary>
            void Danger()
            {
                danger = new bool[steps + 1][];
                for (int k = 0; k <= steps; k++) danger[k] = new bool[nx * ny];
                double body = heroR + cell;
                foreach (var h in hazards)
                {
                    double land = h.Telegraph.Land, until = h.Until;
                    var at0 = h.ShapeAt(land);
                    at0.Bounds(out _, out _, out double r0);
                    // How far the shape's edge moves in a step: sample finely enough that it moves under a cell a sample.
                    double moves = (Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy) + Math.Abs(h.GrowMps) + Math.Abs(h.SpinRadPerS) * 2 * r0) * dt;
                    int samples = Math.Max(1, (int)Math.Ceiling(moves / cell)) + 1;
                    int first = Math.Max(0, (int)Math.Floor((land - start) / dt - 0.5)), last = Math.Min(steps, (int)Math.Ceiling((until - start) / dt + 0.5));
                    for (int k = first; k <= last; k++)
                    {
                        double tk = start + k * dt, a = Math.Max(land, tk - dt / 2), b = Math.Min(until, tk + dt / 2);
                        if (a > b + 1e-12) continue;
                        var d = danger[k];
                        for (int q = 0; q < samples; q++)
                        {
                            double t = samples == 1 ? a : a + (b - a) * q / (samples - 1);
                            var shape = h.ShapeAt(t);
                            shape.Bounds(out double cx, out double cy, out double r);
                            r += body + cell;
                            int i0 = Math.Max(0, (int)Math.Floor((cx - r - x0) / cell)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((cx + r - x0) / cell));
                            int j0 = Math.Max(0, (int)Math.Floor((cy - r - y0) / cell)), j1 = Math.Min(ny - 1, (int)Math.Ceiling((cy + r - y0) / cell));
                            for (int j = j0; j <= j1; j++)
                                for (int i = i0; i <= i1; i++)
                                    if (!d[j * nx + i] && shape.Touches(X(i), Y(j), body)) d[j * nx + i] = true;
                            if (b - a < 1e-12) break;
                        }
                    }
                }
            }

            /// <summary>Tolerance of the cell (i, j) at step k: off the grid, always safe if in the arena (-2 outside).</summary>
            int Tol(int k, int i, int j)
            {
                if (i < 0 || j < 0 || i >= nx || j >= ny)
                    return inside(X(i), Y(j)) ? cool : -2;
                if (!valid[j * nx + i]) return -2;
                return k > steps ? cool : tol[k][j * nx + i];
            }

            void Solve(bool dash)
            {
                for (int k = steps; k >= 0; k--)
                {
                    var t = tol[k] = new sbyte[nx * ny];
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                        {
                            int c = j * nx + i;
                            if (!valid[c] || danger[k][c]) { t[c] = -1; continue; }
                            if (k == steps) { t[c] = (sbyte)cool; continue; }
                            int best = -1;
                            foreach (var (dx, dy) in walk)
                            {
                                int n = Tol(k + 1, i + dx, j + dy);
                                if (n >= 0) best = Math.Max(best, Math.Min(cool, n + 1));
                                if (best == cool) break;
                            }
                            if (best < 0 && dash)
                                foreach (var (dx, dy) in dashes)
                                    if (Tol(k + dashSteps, i + dx, j + dy) >= Math.Max(0, cool - dashSteps)) { best = 0; break; }
                            t[c] = (sbyte)best;
                        }
                }
            }

            /// <summary>Every start in the arena survives with the dash <paramref name="coolingS"/> from ready; else the first that doesn't.</summary>
            public bool EveryStartEscapes(double coolingS, out double fx, out double fy)
            {
                int need = (int)Math.Ceiling(coolingS / dt - 1e-9);
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int c = j * nx + i;
                        if (valid[c] && tol[0][c] < need)
                        {
                            fx = X(i);
                            fy = Y(j);
                            return false;
                        }
                    }
                fx = fy = 0;
                return true;
            }

            /// <summary>Whether the hero at (x, y) at the start survives, with or without its dash.</summary>
            public bool Escapes(double x, double y, bool dash)
            {
                if (!dash) Solve(false);
                int i = (int)Math.Round((x - x0) / cell), j = (int)Math.Round((y - y0) / cell);
                bool ok = Tol(0, i, j) >= 0;
                if (!dash) Solve(true);
                return ok;
            }
        }
    }
}
