using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace PS.Choreography
{
    /// <summary>One shot's row of the trailer report (trailer.md, "Checks and Evidence").</summary>
    public sealed class ShotReport
    {
        public int Number;
        public string Environment;
        public int Target;
        /// <summary>The mean of the density check's samples (live monsters in view on each beat), NaN with none.</summary>
        public double OnScreen;
        public double SwingsPerSecond, MeanKillsPerSwing;
        public int PeakKillsPerSwing;
        /// <summary>The share of the visible span's beat intervals the hero acted in.</summary>
        public double ActedShare;
        /// <summary>The hero's speed over the horde's, up to any final slam (NaN for a hero holding ground).</summary>
        public double KiteRatio;
        /// <summary>Monster liveness violations: monster beat intervals in view without a move or a clip.</summary>
        public int LivenessViolations;
        /// <summary>The mean share of a visible monster's neighbours of its own type (NaN with none): 1 is pure blocks.</summary>
        public double TypeCoherence;
        /// <summary>The narrowest the hero's open lane gets, in degrees: the least, over the beats, of the widest gap round them (360 with no monster near).</summary>
        public double OpenLaneDeg;
    }

    /// <summary>
    /// The trailer's timeline checks (trailer.md, "Checks and Evidence"), run on each shot's timeline. Each returns its
    /// problems, naming the shot, the time and the monster; an empty list passes.
    /// </summary>
    public static class TrailerChecks
    {
        const double Eps = 1e-6;
        /// <summary>A bar's mean on screen stays within this fraction of the shot's target.</summary>
        public const double DensityTolerance = 0.15;
        /// <summary>The duel's hero, holding ground, strides at least this far in a beat without a blow.</summary>
        public const float HoldStepM = 0.3f;
        /// <summary>A visible monster moves at least this fraction of its gait step in each beat interval, unless it plays a clip.</summary>
        public const float LiveStepFraction = 0.25f;
        /// <summary>The liveness check's view grows by this margin, where monsters spawn.</summary>
        public const float LiveMarginM = 2f;
        /// <summary>A visible monster's neighbours are the visible monsters within this radius.</summary>
        public const float NeighbourRadiusM = 3f;
        /// <summary>A horde shot's monsters' neighbours are of their own type at least this share of the time, on average (a random mix of the shots' scores about 0.25).</summary>
        public const double MinTypeCoherence = 0.6;
        /// <summary>In a pulsed section, the hero's blows land at least this many beats apart.</summary>
        public const double MinBlowGapBeats = 2;
        /// <summary>The encirclement check looks at the monsters within this radius of the hero.</summary>
        public const float EncircleRadiusM = 6f;
        /// <summary>... and wants a gap round the hero free of their bodies at least this wide, on every beat: an escape lane.</summary>
        public const double MinOpenLaneDeg = 60;

        /// <summary>Every check, by name, on one shot's timeline (<paramref name="data"/>: the shot's roster and clips).</summary>
        public static List<KeyValuePair<string, List<string>>> All(Trailer trailer, int index, Timeline tl, ChoreographyData data)
        {
            TrailerShot s = trailer.Shots[index];
            Roster roster = data.Roster;
            return new List<KeyValuePair<string, List<string>>>
            {
                Check("cuts on the grid", CutOnTheGrid(s, tl)),
                Check("opening blows", OpeningBlow(s, tl)),
                Check("the hero moves continuously", HeroMovesContinuously(s, tl)),
                Check("impacts on the grid", ImpactsOnTheGrid(s, tl)),
                Check("the hero acts every beat", HeroActsEveryBeat(trailer, s, tl)),
                Check("monster liveness", MonstersLive(s, tl, data)),
                Check("type blocks", TypeBlocks(trailer, s, tl)),
                Check("blows on the major beats", BlowsOnMajorBeats(trailer, s, tl)),
                Check("an open lane", OpenLane(trailer, s, tl, roster)),
                Check("one hit kills", OneHitKills(s, tl)),
                Check("the boss", Boss(trailer, s, tl, roster)),
                Check("the final slam", FinalSlam(trailer, s, tl)),
                Check("no visible spawns", NoVisibleSpawns(s, tl)),
                Check("density", Density(trailer, s, tl)),
            };
        }

        static KeyValuePair<string, List<string>> Check(string name, List<string> problems) => new KeyValuePair<string, List<string>>(name, problems);

        /// <summary>A shot after the first cuts exactly on a beat of the grid.</summary>
        public static List<string> CutOnTheGrid(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            if (s.Number == 1) return problems;
            GridPoint p = tl.Grid.Points[tl.Grid.Nearest(s.Cut)];
            if (Math.Abs(p.Time - s.Cut) > Eps || !p.Has(GridFlags.Beat)) problems.Add(Where(s, s.Cut) + "the cut is not on a beat");
            return problems;
        }

        /// <summary>A shot after the first opens with the hero's blow on its cut; the first, on its first anchor.</summary>
        public static List<string> OpeningBlow(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            double at = s.Number > 1 ? s.Cut : FirstAnchor(tl.Map, s.Cut);
            if (double.IsNaN(at)) { problems.Add(Where(s, s.Cut) + "no anchor for the opening blow"); return problems; }
            foreach (ActionEvent a in tl.ActionsOf(0)) if (a.Kind == ActionKind.Attack && Math.Abs(a.Impact - at) <= Eps) return problems;
            problems.Add(Where(s, at) + "no hero blow lands on the " + (s.Number > 1 ? "cut" : "first anchor"));
            return problems;
        }

        /// <summary>The hero never jumps: each of its motion segments starts where the hero stands as it begins (within 5 cm).</summary>
        public static List<string> HeroMovesContinuously(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            ArraySegment<MotionSegment> segs = tl.SegmentsOf(0);
            for (int i = 1; i < segs.Count; i++)
            {
                MotionSegment prev = segs.Array[segs.Offset + i - 1], seg = segs.Array[segs.Offset + i];
                float jump = Vector2.Distance(prev.PositionAt(seg.T0), seg.From);
                if (jump > 0.05f && problems.Count < 20) problems.Add(Where(s, seg.T0) + "the hero jumps " + jump.ToString("F2", CultureInfo.InvariantCulture) + " m");
            }
            return problems;
        }

        /// <summary>Every impact lies on the grid (an anchor in a free section): <see cref="TimelineChecks.ImpactsOnGrid"/>.</summary>
        public static List<string> ImpactsOnTheGrid(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            foreach (string p in TimelineChecks.ImpactsOnGrid(tl, out _)) problems.Add("shot " + s.Number + ": " + p);
            return problems;
        }

        /// <summary>
        /// In every beat interval of the visible span (the partial one before the first beat counting as one) the hero
        /// strides (half its path's stride, or <see cref="HoldStepM"/> holding ground) or plays an attack's windup or
        /// impact. After the final slam the hero plays out its recovery.
        /// </summary>
        public static List<string> HeroActsEveryBeat(Trailer trailer, TrailerShot s, Timeline tl) => HeroIntervals(trailer, s, tl, out _);

        static List<string> HeroIntervals(Trailer trailer, TrailerShot s, Timeline tl, out double share)
        {
            var problems = new List<string>();
            float stride = s.Path != null ? 0.5f * s.Path.StepM : HoldStepM;
            double end = Math.Min(s.End, s.HasFinal ? trailer.FinalSlamTime(tl.Map) + Eps : s.End);
            List<double> bounds = Intervals(tl.Map, s.Cut, end);
            int acted = 0, n = 0;
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                double a = bounds[i], b = bounds[i + 1];
                n++;
                bool act = Moved(tl, 0, a, b) >= stride;
                foreach (ActionEvent e in tl.ActionsOf(0))
                {
                    if (e.Kind != ActionKind.Attack) continue;
                    double c0 = e.HasClipTiming ? e.ClipStart : e.Impact;
                    if (c0 < b - Eps && e.Impact >= a - Eps) { act = true; break; }
                }
                if (act) acted++;
                else if (problems.Count < 20) problems.Add(Where(s, a) + "the hero neither strides nor attacks until " + Fmt(b));
            }
            share = n > 0 ? acted / (double)n : double.NaN;
            return problems;
        }

        /// <summary>
        /// In every beat interval of the visible span, every live monster in the view grown by <see cref="LiveMarginM"/> at
        /// the interval's start moves at least <see cref="LiveStepFraction"/> of its gait step, or plays an attack, hit,
        /// death or spawn clip. A free section's beats are silent, but its grid still steps every 0.387 s.
        /// </summary>
        public static List<string> MonstersLive(TrailerShot s, Timeline tl, ChoreographyData data) => LivenessViolations(s, tl, data, out _);

        static List<string> LivenessViolations(TrailerShot s, Timeline tl, ChoreographyData data, out int violations)
        {
            var problems = new List<string>();
            violations = 0;
            double[] death = DeathTimes(tl);
            List<double> bounds = Intervals(tl.Map, s.Cut, s.End);
            for (int e = 1; e < tl.EntityCount; e++)
            {
                if (tl.Entities[e].Kind != EntityKind.Monster) continue;
                float step = LiveStepFraction * data.Roster.Monsters[tl.Entities[e].Type].StepM;
                for (int i = 0; i + 1 < bounds.Count; i++)
                {
                    double a = bounds[i], b = bounds[i + 1];
                    EntityInfo info = tl.Entities[e];
                    if (info.Spawn > a + Eps || info.Despawn <= a + Eps || death[e] < a - Eps) continue;
                    FramingSample f = tl.FramingAt(a);
                    if (!tl.View(f.ViewWidth).Contains(f.Focus, tl.PositionAt(e, a), LiveMarginM)) continue;
                    if (PlaysAClip(tl, data.Clips, e, a, b) || Moved(tl, e, a, b) >= step) continue;
                    violations++;
                    if (problems.Count < 20) problems.Add(Where(s, a) + "monster " + e + " (" + TypeOf(tl, e) + ") neither moves nor plays a clip until " + Fmt(b));
                }
            }
            return problems;
        }

        /// <summary>Does monster <paramref name="e"/> play an attack (from its windup to its clip's end), hit, death or spawn clip between <paramref name="a"/> and <paramref name="b"/>?</summary>
        static bool PlaysAClip(Timeline tl, ClipLibrary clips, int e, double a, double b)
        {
            foreach (ActionEvent act in tl.ActionsOf(e))
            {
                if (act.Kind == ActionKind.Attack)
                {
                    double c0 = act.HasClipTiming ? act.ClipStart : act.Impact;
                    double c1 = act.HasClipTiming && act.Clip >= 0 ? act.ClipStart + clips[act.Clip].End / act.ClipSpeed : act.Impact;
                    if (c0 < b - Eps && c1 >= a - Eps) return true;
                }
                else if (act.Kind != ActionKind.Parry && act.Impact >= a - Eps && act.Impact < b - Eps) return true;
            }
            return false;
        }

        /// <summary>
        /// In a horde shot, waves come in blocks of one type (trailer.md, "Kiting and formations"): on every beat of the
        /// visible span up to any final slam, each live monster in view scores the share of the visible monsters within
        /// <see cref="NeighbourRadiusM"/> that share its type, and the mean over the shot is at least
        /// <see cref="MinTypeCoherence"/>.
        /// </summary>
        public static List<string> TypeBlocks(Trailer trailer, TrailerShot s, Timeline tl) => Coherence(trailer, s, tl, out _);

        static List<string> Coherence(Trailer trailer, TrailerShot s, Timeline tl, out double mean)
        {
            var problems = new List<string>();
            mean = double.NaN;
            if (s.Duel != null) return problems;
            double[] death = DeathTimes(tl);
            double end = s.HasFinal ? trailer.FinalSlamTime(tl.Map) : s.End, sum = 0;
            int n = 0;
            var visible = new List<int>();
            float r2 = NeighbourRadiusM * NeighbourRadiusM;
            foreach (double t in tl.Map.Beats)
            {
                if (t < s.Cut - Eps || t >= end - Eps) continue;
                FramingSample f = tl.FramingAt(t);
                ViewGeometry view = tl.View(f.ViewWidth);
                visible.Clear();
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind == EntityKind.Monster && t >= info.Spawn && t < info.Despawn && t < death[e] && view.Contains(f.Focus, tl.PositionAt(e, t))) visible.Add(e);
                }
                var at = new Vector2[visible.Count];
                for (int i = 0; i < at.Length; i++) at[i] = tl.PositionAt(visible[i], t);
                for (int i = 0; i < at.Length; i++)
                {
                    int same = 0, all = 0;
                    for (int j = 0; j < at.Length; j++)
                    {
                        if (i == j || Vector2.DistanceSquared(at[i], at[j]) > r2) continue;
                        all++;
                        if (tl.Entities[visible[j]].Type == tl.Entities[visible[i]].Type) same++;
                    }
                    if (all == 0) continue;
                    sum += same / (double)all;
                    n++;
                }
            }
            if (n == 0) return problems;
            mean = sum / n;
            if (mean < MinTypeCoherence)
                problems.Add("shot " + s.Number + ": a monster's neighbours share its type " + mean.ToString("P0", CultureInfo.InvariantCulture) + " of the time, under " + MinTypeCoherence.ToString("P0", CultureInfo.InvariantCulture));
            return problems;
        }

        /// <summary>
        /// The hero's blows land on the major beats (trailer.md, "The hero's moves"): every impact of the visible span is on
        /// a strong beat (4/4's beats 1 and 3), an accent, the shot's cut or a free section's anchor, and in a pulsed section
        /// each lands at least <see cref="MinBlowGapBeats"/> beats after the one before, so a blow gets its windup and
        /// follow-through at a natural rate.
        /// </summary>
        public static List<string> BlowsOnMajorBeats(Trailer trailer, TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            double last = double.NaN;
            foreach (ActionEvent a in tl.ActionsOf(0))
            {
                if (a.Kind != ActionKind.Attack || a.Impact < s.Cut - Eps || a.Impact >= s.End - Eps) continue;
                GridPoint p = tl.Grid.Points[tl.Grid.Nearest(a.Impact)];
                bool free = p.Has(GridFlags.Free);
                bool major = Math.Abs(p.Time - a.Impact) <= Eps && (free || Math.Abs(a.Impact - s.Cut) <= Eps || (p.Flags & (GridFlags.Strong | GridFlags.Accent)) != 0);
                if (!major && problems.Count < 20) problems.Add(Where(s, a.Impact) + "a hero blow off the major beats");
                double gap = tl.Map.BeatIndexAt(a.Impact) - tl.Map.BeatIndexAt(last);
                if (!free && !double.IsNaN(last) && gap < MinBlowGapBeats - 1e-3 && problems.Count < 20)
                    problems.Add(Where(s, a.Impact) + "a hero blow " + gap.ToString("F1", CultureInfo.InvariantCulture) + " beats after the last");
                last = a.Impact;
            }
            return problems;
        }

        /// <summary>
        /// The hero is never surrounded (trailer.md, "Motion"): on every beat of the visible span up to any final slam, the
        /// bodies of the live monsters within <see cref="EncircleRadiusM"/> of the hero leave a gap round them at least
        /// <see cref="MinOpenLaneDeg"/> wide.
        /// </summary>
        public static List<string> OpenLane(Trailer trailer, TrailerShot s, Timeline tl, Roster roster) => Lanes(trailer, s, tl, roster, out _);

        static List<string> Lanes(Trailer trailer, TrailerShot s, Timeline tl, Roster roster, out double narrowest)
        {
            var problems = new List<string>();
            narrowest = 360;
            double[] death = DeathTimes(tl);
            double end = s.HasFinal ? trailer.FinalSlamTime(tl.Map) : s.End;
            var arcs = new List<KeyValuePair<double, double>>();
            foreach (double t in tl.Map.Beats)
            {
                if (t < s.Cut - Eps || t >= end - Eps) continue;
                Vector2 h = tl.PositionAt(0, t);
                arcs.Clear();
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn || t >= death[e]) continue;
                    Vector2 d = tl.PositionAt(e, t) - h;
                    float l = d.Length(), r = roster.Monsters[info.Type].Radius;
                    if (l > EncircleRadiusM + r) continue;
                    double half = l <= r ? Math.PI : Math.Asin(r / l), mid = Math.Atan2(d.Y, d.X);
                    arcs.Add(new KeyValuePair<double, double>(mid - half, mid + half));
                }
                double gap = WidestGap(arcs) * 180 / Math.PI;
                narrowest = Math.Min(narrowest, gap);
                if (gap < MinOpenLaneDeg && problems.Count < 20)
                    problems.Add(Where(s, t) + "the hero is hemmed in: the widest gap round them is " + gap.ToString("F0", CultureInfo.InvariantCulture) + " degrees");
            }
            return problems;
        }

        /// <summary>The widest angle (radians) the arcs leave uncovered round the circle; 2π with none.</summary>
        static double WidestGap(List<KeyValuePair<double, double>> arcs)
        {
            if (arcs.Count == 0) return 2 * Math.PI;
            var list = new List<KeyValuePair<double, double>>();
            foreach (KeyValuePair<double, double> a in arcs)
            {
                if (a.Value - a.Key >= 2 * Math.PI) return 0;
                double lo = a.Key - 2 * Math.PI * Math.Floor(a.Key / (2 * Math.PI)), hi = lo + (a.Value - a.Key);
                list.Add(new KeyValuePair<double, double>(lo, hi));
            }
            list.Sort((x, y) => x.Key.CompareTo(y.Key));
            double best = 0, reach = list[0].Value;
            for (int i = 1; i < list.Count; i++)
            {
                best = Math.Max(best, list[i].Key - reach);
                reach = Math.Max(reach, list[i].Value);
            }
            // Round the circle, from the furthest any arc reaches back to the first arc's start.
            return Math.Max(best, list[0].Key + 2 * Math.PI - reach);
        }

        /// <summary>No monster takes a hit (or a laser's tick) and survives: every strike kills.</summary>
        public static List<string> OneHitKills(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Hit && a.Kind != ActionKind.Tick) continue;
                if (problems.Count < 20) problems.Add(Where(s, a.Impact) + "monster " + a.Entity + " (" + TypeOf(tl, a.Entity) + ") takes a " + a.Kind.ToString().ToLowerInvariant() + " and survives");
            }
            return problems;
        }

        /// <summary>A shot with the boss rule: the boss takes exactly one hit, the final slam's, which kills it. Others have no boss.</summary>
        public static List<string> Boss(Trailer trailer, TrailerShot s, Timeline tl, Roster roster)
        {
            var problems = new List<string>();
            var bosses = new List<int>();
            for (int e = 1; e < tl.EntityCount; e++)
                if (tl.Entities[e].Kind == EntityKind.Monster && roster.Monsters[tl.Entities[e].Type].Rank == MonsterRank.Boss) bosses.Add(e);
            if (s.Boss == null)
            {
                foreach (int b in bosses) problems.Add(Where(s, tl.Entities[b].Spawn) + "boss " + b + " in a shot without the boss rule");
                return problems;
            }
            if (bosses.Count != 1) { problems.Add(Where(s, s.Cut) + bosses.Count + " bosses, wants 1"); return problems; }
            double final = trailer.FinalSlamTime(tl.Map);
            int boss = bosses[0];
            bool died = false;
            foreach (ActionEvent a in tl.ActionsOf(boss))
            {
                if (a.Kind == ActionKind.Death && Math.Abs(a.Impact - final) <= Eps) { died = true; continue; }
                if (a.Kind == ActionKind.Death || a.Kind == ActionKind.Hit || a.Kind == ActionKind.Tick)
                    problems.Add(Where(s, a.Impact) + "boss " + boss + " takes a " + a.Kind.ToString().ToLowerInvariant() + " before the final slam");
            }
            if (!died) problems.Add(Where(s, final) + "boss " + boss + " doesn't die to the final slam");
            return problems;
        }

        /// <summary>A shot with the final slam: every monster alive in view then dies on it.</summary>
        public static List<string> FinalSlam(Trailer trailer, TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            if (!s.HasFinal) return problems;
            double final = trailer.FinalSlamTime(tl.Map);
            double[] death = DeathTimes(tl);
            FramingSample f = tl.FramingAt(final);
            ViewGeometry view = tl.View(f.ViewWidth);
            for (int e = 1; e < tl.EntityCount; e++)
            {
                if (tl.Entities[e].Kind != EntityKind.Monster || tl.Entities[e].Spawn > final + Eps || death[e] < final - Eps) continue;
                if (!view.Contains(f.Focus, tl.PositionAt(e, final))) continue;
                if (Math.Abs(death[e] - final) > Eps && problems.Count < 20)
                    problems.Add(Where(s, final) + "monster " + e + " (" + TypeOf(tl, e) + ") in view survives the final slam");
            }
            return problems;
        }

        /// <summary>Every spawn in the visible span lies outside the view (turned by the shot's heading).</summary>
        public static List<string> NoVisibleSpawns(TrailerShot s, Timeline tl)
        {
            var problems = new List<string>();
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster || info.Spawn < s.Cut - Eps || info.Spawn >= s.End - Eps) continue;
                FramingSample f = tl.FramingAt(info.Spawn);
                float outside = tl.View(f.ViewWidth).OutsideDistance(f.Focus, tl.PositionAt(e, info.Spawn));
                if (outside <= 0 && problems.Count < 20) problems.Add(Where(s, info.Spawn) + "monster " + e + " (" + TypeOf(tl, e) + ") spawns in view");
            }
            return problems;
        }

        /// <summary>
        /// From the shot's second beat, the live monsters in view are counted on every beat, and each bar's mean of those
        /// counts stays within <see cref="DensityTolerance"/> of the target. The duel and the beats from the final slam on are exempt.
        /// </summary>
        public static List<string> Density(Trailer trailer, TrailerShot s, Timeline tl) => DensitySamples(trailer, s, tl, out _);

        static List<string> DensitySamples(Trailer trailer, TrailerShot s, Timeline tl, out double mean)
        {
            var problems = new List<string>();
            mean = double.NaN;
            if (s.Duel != null) return problems;
            double final = s.HasFinal ? trailer.FinalSlamTime(tl.Map) : double.PositiveInfinity;
            double[] death = DeathTimes(tl);
            SongMap map = tl.Map;
            var bars = new SortedDictionary<int, List<int>>();
            double sum = 0;
            int count = 0;
            for (int b = 0; b < map.Beats.Length; b++)
            {
                double t = map.Beats[b];
                if (t <= s.Cut + Eps || t >= s.End - Eps || t >= final - Eps) continue;
                int c = LiveInView(tl, death, t);
                int bar = BarOf(map, b);
                if (!bars.TryGetValue(bar, out List<int> list)) bars[bar] = list = new List<int>();
                list.Add(c);
                sum += c;
                count++;
            }
            if (count > 0) mean = sum / count;
            int target = s.TargetOnScreen;
            foreach (KeyValuePair<int, List<int>> kv in bars)
            {
                double m = 0;
                foreach (int c in kv.Value) m += c;
                m /= kv.Value.Count;
                if (Math.Abs(m / target - 1) > DensityTolerance)
                    problems.Add("shot " + s.Number + ", bar " + kv.Key + ": " + m.ToString("F0", CultureInfo.InvariantCulture) + " on screen against " + target);
            }
            return problems;
        }

        /// <summary>The shot's row of the report.</summary>
        public static ShotReport Report(Trailer trailer, int index, Timeline tl, ChoreographyData data)
        {
            TrailerShot s = trailer.Shots[index];
            Roster roster = data.Roster;
            var r = new ShotReport { Number = s.Number, Environment = s.Environment, Target = s.TargetOnScreen };
            DensitySamples(trailer, s, tl, out r.OnScreen);
            HeroIntervals(trailer, s, tl, out r.ActedShare);
            LivenessViolations(s, tl, data, out r.LivenessViolations);
            Coherence(trailer, s, tl, out r.TypeCoherence);
            Lanes(trailer, s, tl, roster, out r.OpenLaneDeg);
            double dur = s.End - s.Cut;
            var deaths = new Dictionary<double, int>();
            foreach (ActionEvent a in tl.Actions)
                if (a.Kind == ActionKind.Death && a.Impact >= s.Cut - Eps && a.Impact < s.End)
                {
                    deaths.TryGetValue(a.Impact, out int k);
                    deaths[a.Impact] = k + 1;
                }
            int swings = 0, kills = 0;
            foreach (ActionEvent a in tl.ActionsOf(0))
            {
                if (a.Kind != ActionKind.Attack || a.Impact < s.Cut - Eps || a.Impact >= s.End) continue;
                swings++;
                // Deaths on the blow's instant (a spell landing with it counts with it).
                deaths.TryGetValue(a.Impact, out int k);
                kills += k;
                r.PeakKillsPerSwing = Math.Max(r.PeakKillsPerSwing, k);
            }
            r.SwingsPerSecond = swings / dur;
            r.MeanKillsPerSwing = swings > 0 ? kills / (double)swings : 0;
            r.KiteRatio = double.NaN;
            if (s.Path != null && s.Formation != null)
            {
                double beat = tl.Map.Beats[1] - tl.Map.Beats[0], w = 0, gait = 0;
                foreach (MixEntry e in s.Formation.Mix)
                {
                    int t = roster.MonsterIndex(e.Type);
                    if (t < 0 || roster.Monsters[t].Rank == MonsterRank.Boss) continue;
                    w += e.Weight;
                    gait += e.Weight * roster.Monsters[t].StepM;
                }
                double horde = w > 0 ? gait / w / beat : double.NaN;
                // Up to the final slam: after it the hero plays out its recovery.
                List<double> bounds = Intervals(tl.Map, s.Cut, s.HasFinal ? trailer.FinalSlamTime(tl.Map) : s.End);
                double travelled = 0;
                for (int i = 0; i + 1 < bounds.Count; i++) travelled += Vector2.Distance(tl.PositionAt(0, bounds[i]), tl.PositionAt(0, bounds[i + 1]));
                r.KiteRatio = travelled / (bounds[bounds.Count - 1] - bounds[0]) / horde;
            }
            return r;
        }

        /// <summary>The report's table, one row per shot.</summary>
        public static string Table(IList<ShotReport> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness  blocks  lane");
            foreach (ShotReport r in rows)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,4}  {1,-11}  {2,9} / {3,-6}  {4,8:F2}  {5,11:F1}, {6,-12}  {7,9:P0}  {8,4}  {9,8}  {10,6}  {11,4:F0}",
                    r.Number, r.Environment, double.IsNaN(r.OnScreen) ? "-" : r.OnScreen.ToString("F0", CultureInfo.InvariantCulture), r.Target, r.SwingsPerSecond,
                    r.MeanKillsPerSwing, r.PeakKillsPerSwing, r.ActedShare, double.IsNaN(r.KiteRatio) ? "-" : r.KiteRatio.ToString("F1", CultureInfo.InvariantCulture),
                    r.LivenessViolations, double.IsNaN(r.TypeCoherence) ? "-" : r.TypeCoherence.ToString("P0", CultureInfo.InvariantCulture), r.OpenLaneDeg));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static string Where(TrailerShot s, double t) => "shot " + s.Number + " at " + Fmt(t) + " s: ";
        static string Fmt(double t) => t.ToString("F3", CultureInfo.InvariantCulture);
        static string TypeOf(Timeline tl, int e) => tl.Entities[e].Type >= 0 && tl.Entities[e].Type < tl.TypeNames.Length ? tl.TypeNames[tl.Entities[e].Type] : "?";

        static double FirstAnchor(SongMap map, double from)
        {
            double best = double.NaN;
            foreach (SongSection sec in map.Sections)
                if (sec.Feel == SectionFeel.Free)
                    foreach (TimedStrength a in sec.Anchors)
                        if (a.Time >= from - Eps && (double.IsNaN(best) || a.Time < best)) best = a.Time;
            return best;
        }

        /// <summary>The visible span's beat intervals: its start, every beat inside it, and its end.</summary>
        static List<double> Intervals(SongMap map, double from, double to)
        {
            var list = new List<double> { from };
            foreach (double b in map.Beats) if (b > from + Eps && b < to - Eps) list.Add(b);
            list.Add(to);
            return list;
        }

        /// <summary>How far entity <paramref name="e"/> moves between <paramref name="a"/> and <paramref name="b"/>, sampled every 1/60 s.</summary>
        static float Moved(Timeline tl, int e, double a, double b)
        {
            Vector2 p0 = tl.PositionAt(e, a);
            float best = 0;
            for (double t = a; t <= b + Eps; t += 1.0 / 60) best = Math.Max(best, Vector2.Distance(p0, tl.PositionAt(e, Math.Min(t, b))));
            return best;
        }

        static double[] DeathTimes(Timeline tl)
        {
            var d = new double[tl.EntityCount];
            for (int e = 0; e < d.Length; e++)
            {
                d[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e)) if (a.Kind == ActionKind.Death) { d[e] = a.Impact; break; }
            }
            return d;
        }

        static int LiveInView(Timeline tl, double[] death, double t)
        {
            FramingSample f = tl.FramingAt(t);
            ViewGeometry view = tl.View(f.ViewWidth);
            int n = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn || t >= death[e]) continue;
                if (view.Contains(f.Focus, tl.PositionAt(e, t))) n++;
            }
            return n;
        }

        static int BarOf(SongMap map, int beat)
        {
            int bar = 1;
            while (map.DownbeatIndexOfBar(bar + 1) <= beat) bar++;
            return bar;
        }
    }
}
