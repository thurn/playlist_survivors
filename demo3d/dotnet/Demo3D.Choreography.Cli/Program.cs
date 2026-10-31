using System;
using System.Globalization;
using System.IO;
using System.Text;
using PS.Choreography;

namespace Demo3D.Choreography.Cli
{
    /// <summary>
    /// dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song [--level action-level-forest.json] [--intensity overdrive] [--seed N] [--out dump.json] [--from S --to S] [--hero] [--onscreen] [--ring] [--checks]
    ///                                                                        stress S3 [--n N] [--view W] [--load L] [--seconds S] [--out dump.json]
    ///                                                                        trailer [--name action] [--roster R] [--environment E] [--shot N] [--checks] [--out-dir D]
    ///                                                                       usage: song | stress <S1..S6> | placements | trailer | timing [--out timing.md]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) { Console.Error.WriteLine("usage: song | stress <S1..S5> | trailer | timing (see Program.cs)"); return 2; }
            string root = ChoreographyFiles.FindRepoRoot(Opt(args, "--root"));
            switch (args[0])
            {
                case "song":
                {
                    string seed = Opt(args, "--seed");
                    string level = Opt(args, "--level") ?? ChoreographyFiles.LevelFile;
                    Timeline tl = ChoreographyFiles.BuildSong(root, seed == null ? (uint?)null : uint.Parse(seed, CultureInfo.InvariantCulture), level, Opt(args, "--intensity"));
                    Console.Write(TimelineReport.Summary(tl));
                    if (Has(args, "--checks")) Checks(tl, ChoreographyFiles.LoadLevel(root, level, Opt(args, "--intensity")), root);
                    Snapshot(tl, ChoreographyFiles.LoadData(root, ChoreographyFiles.LoadLevel(root, level).Layout ?? ChoreographyFiles.DefaultLayout), args);
                    if (Has(args, "--hero"))
                        for (double ht = 0; ht < tl.Duration; ht += 5)
                        {
                            System.Numerics.Vector2 hp = tl.PositionAt(0, ht);
                            Console.WriteLine("hero at " + ht.ToString("F0", CultureInfo.InvariantCulture) + " s: (" + hp.X.ToString("F1", CultureInfo.InvariantCulture) + ", " + hp.Y.ToString("F1", CultureInfo.InvariantCulture) + ") in " + tl.Sections[tl.Map.SectionIndexAt(ht)].Label);
                        }
                    if (Has(args, "--onscreen")) OnScreenSeries(tl, DoubleOpt(args, "--from", 0), DoubleOpt(args, "--to", tl.Duration));
                    if (Has(args, "--ring")) RingSeries(tl);
                    Dump(tl, args);
                    return 0;
                }
                case "stress":
                {
                    StressScript s = Opt(args, "--script") != null ? StressScript.Parse(File.ReadAllText(Opt(args, "--script")))
                        : ChoreographyFiles.LoadStress(root, args.Length > 1 ? args[1] : "S3");
                    s = s.With(n: IntOpt(args, "--n"), viewWidthM: (float)DoubleOpt(args, "--view"), load: Opt(args, "--load"), seconds: DoubleOpt(args, "--seconds"));
                    if (Opt(args, "--layout") != null) { s.Layout = Opt(args, "--layout"); s.Hold = Opt(args, "--hold") ?? s.Hold; }
                    ChoreographyData data = ChoreographyFiles.LoadData(root, s.Layout ?? ChoreographyFiles.DefaultLayout);
                    Timeline tl = Choreographer.RunStress(s, data);
                    Console.Write(TimelineReport.Summary(tl));
                    Snapshot(tl, data, args);
                    if (Opt(args, "--trace") != null)
                    {
                        int te = IntOpt(args, "--trace");
                        foreach (MotionSegment sg in tl.SegmentsOf(te))
                            Console.WriteLine("seg " + sg.T0.ToString("F2", CultureInfo.InvariantCulture) + "-" + sg.T1.ToString("F2", CultureInfo.InvariantCulture) + " " + sg.From + " -> " + sg.To + " " + sg.Ease);
                        foreach (ActionEvent ae in tl.ActionsOf(te)) Console.WriteLine("act " + ae.Kind + " " + ae.Impact.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    Dump(tl, args);
                    return 0;
                }
                case "placements":
                {
                    // placements [--layout arena] [--density D] [--out list.json] [--png map.png [--field] [--px 4]]
                    string layoutName = Opt(args, "--layout") ?? ChoreographyFiles.DefaultLayout;
                    float density = (float)DoubleOpt(args, "--density");
                    ArenaLayout layout = ChoreographyFiles.LoadLayout(root, layoutName, density);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    PlacementList list = layout.Generate(ChoreographyFiles.LoadFootprints(root));
                    double genMs = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    var nav = new NavGrid(list);
                    double navMs = sw.Elapsed.TotalMilliseconds;
                    var field = new FlowField(nav, 0.4f);
                    sw.Restart();
                    field.Begin(list.HeroStart);
                    field.BuildAll();
                    double flowMs = sw.Elapsed.TotalMilliseconds;
                    Console.WriteLine(layoutName + " at density " + layout.DecorationDensity.ToString("0.##", CultureInfo.InvariantCulture) + ": " + list.Items.Length + " placements ("
                        + list.BlockingCount + " blocking) of " + list.Props.Length + " pieces, generated in " + genMs.ToString("F1", CultureInfo.InvariantCulture) + " ms; obstacle map "
                        + nav.W + "x" + nav.H + " cells (" + nav.BlockedCells + " blocked, " + nav.ShapeCount + " footprints) in " + navMs.ToString("F1", CultureInfo.InvariantCulture)
                        + " ms; a whole flow field from the hero's start settles " + field.Settled + " cells in " + flowMs.ToString("F2", CultureInfo.InvariantCulture) + " ms");
                    foreach (GoalArea g in list.Goals)
                        Console.WriteLine("  goal " + g.Name + " at (" + g.Center.X.ToString(CultureInfo.InvariantCulture) + ", " + g.Center.Y.ToString(CultureInfo.InvariantCulture) + ") r " + g.Radius.ToString(CultureInfo.InvariantCulture)
                            + ": " + (field.Reached(g.Center) ? field.CostAt(g.Center).ToString("F1", CultureInfo.InvariantCulture) + " m by the field from the hero's start" : "unreached"));
                    string outPath = Opt(args, "--out");
                    if (outPath != null) File.WriteAllText(outPath, list.ToJson(), new UTF8Encoding(false));
                    string png = Opt(args, "--png");
                    if (png != null) LayoutPng.Write(png, layout, list, DoubleOpt(args, "--px") > 0 ? (float)DoubleOpt(args, "--px") : 4f, Has(args, "--field") ? field : null);
                    return 0;
                }
                case "field":
                {
                    // field --layout L --sx X --sy Y --px X --py Y: the flow field from (sx, sy) and its heading at (px, py)
                    PlacementList list = ChoreographyFiles.LoadPlacements(root, Opt(args, "--layout") ?? ChoreographyFiles.DefaultLayout);
                    var nav = new NavGrid(list);
                    var f = new FlowField(nav, 0.4f);
                    f.Begin(new System.Numerics.Vector2((float)DoubleOpt(args, "--sx"), (float)DoubleOpt(args, "--sy")));
                    f.BuildAll();
                    var p = new System.Numerics.Vector2((float)DoubleOpt(args, "--px"), (float)DoubleOpt(args, "--py"));
                    bool ok = f.Direction(p, out System.Numerics.Vector2 dir);
                    Console.WriteLine("cost " + f.CostAt(p) + " dir " + dir + " ok " + ok + " clearance " + nav.ClearanceAt(p) + " exact " + nav.Distance(p) + " clear line " + nav.Clear(p, f.Source, 0.38f));
                    for (int dy = 2; dy >= -2; dy--)
                    {
                        var sb = new StringBuilder();
                        for (int dx = -2; dx <= 2; dx++) { float c = f.CostAt(p + new System.Numerics.Vector2(dx * 0.5f, dy * 0.5f)); sb.Append(float.IsInfinity(c) ? "   inf" : c.ToString("F2", CultureInfo.InvariantCulture).PadLeft(6)); }
                        Console.WriteLine(sb);
                    }
                    return 0;
                }
                case "trailer":
                {
                    // trailer [--name action] [--roster forest] [--environment snow] [--shot N] [--checks] [--out-dir dir]: every shot's
                    // simulation, the per-shot table, and with --checks every failed rule (trailer.md, "Checks and Evidence").
                    Trailer trailer = ChoreographyFiles.LoadTrailer(root, Opt(args, "--name") ?? "action", out var environments, Opt(args, "--environment"));
                    var rows = new System.Collections.Generic.List<ShotReport>();
                    int failures = 0;
                    int only = IntOpt(args, "--shot");
                    for (int i = 0; i < trailer.Shots.Length; i++)
                    {
                        if (only > 0 && i + 1 != only) continue;
                        TrailerShot shot = trailer.Shots[i];
                        ChoreographyData data = environments[shot.Environment];
                        Timeline tl = ChoreographyFiles.BuildShot(root, trailer, i, data, Opt(args, "--roster"));
                        rows.Add(TrailerChecks.Report(trailer, i, tl, data.Roster));
                        if (Opt(args, "--out-dir") != null)
                        {
                            Directory.CreateDirectory(Opt(args, "--out-dir"));
                            TimelineJson.WriteFile(tl, Path.Combine(Opt(args, "--out-dir"), "shot-" + shot.Number + ".json"), shot.SpanFrom, shot.End);
                        }
                        if (!Has(args, "--checks")) continue;
                        foreach (var check in TrailerChecks.All(trailer, i, tl, data.Roster))
                        {
                            if (check.Value.Count == 0) continue;
                            failures += check.Value.Count;
                            Console.WriteLine("shot " + shot.Number + " fails " + check.Key + ": " + check.Value.Count + " problems");
                            for (int k = 0; k < Math.Min(check.Value.Count, 10); k++) Console.WriteLine("  " + check.Value[k]);
                        }
                    }
                    Console.Write(TrailerChecks.Table(rows));
                    if (Has(args, "--checks")) Console.WriteLine(failures == 0 ? "trailer checks: ok" : "trailer checks: " + failures + " problems");
                    return failures == 0 ? 0 : 1;
                }
                case "timing":
                {
                    string md = TimingTables.Build(root, IntOpt(args, "--repeat") > 0 ? IntOpt(args, "--repeat") : 1);
                    Console.Write(md);
                    string outPath = Opt(args, "--out");
                    if (outPath != null) File.WriteAllText(outPath, md, new UTF8Encoding(false));
                    return 0;
                }
                default:
                    Console.Error.WriteLine("unknown command " + args[0]);
                    return 2;
            }
        }

        /// <summary>--png map.png --at T [--span M] [--px P]: the layout around the hero with every monster at song time T.</summary>
        static void Snapshot(Timeline tl, ChoreographyData data, string[] args)
        {
            string png = Opt(args, "--png");
            if (png == null || data.Layout.Placements == null) return;
            double t = DoubleOpt(args, "--at", 30);
            var dots = new System.Collections.Generic.List<System.Numerics.Vector2>();
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind == EntityKind.Monster && t >= info.Spawn && t < info.Despawn) dots.Add(tl.PositionAt(e, t));
            }
            System.Numerics.Vector2 hero = tl.PositionAt(0, t);
            float span = DoubleOpt(args, "--span") > 0 ? (float)DoubleOpt(args, "--span") : 80f;
            float px = DoubleOpt(args, "--px") > 0 ? (float)DoubleOpt(args, "--px") : 8f;
            System.Numerics.Vector2 centre = Opt(args, "--cx") != null ? new System.Numerics.Vector2((float)DoubleOpt(args, "--cx"), (float)DoubleOpt(args, "--cy")) : tl.FramingAt(t).Focus;
            LayoutPng.Write(png, data.Layout.Arena, data.Layout.Placements, px, null, dots.ToArray(), hero, centre, span);
            Console.WriteLine("snapshot: " + png + " at " + t.ToString("F2", CultureInfo.InvariantCulture) + " s, " + dots.Count + " monsters");
        }

        /// <summary>--onscreen [--from S] [--to S]: monsters on screen (alive / dying) each second, against the section's target.</summary>
        static void OnScreenSeries(Timeline tl, double from, double to)
        {
            var deathAt = new double[tl.EntityCount];
            for (int e = 0; e < tl.EntityCount; e++)
            {
                deathAt[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e)) if (a.Kind == ActionKind.Death) { deathAt[e] = a.Impact; break; }
            }
            for (double t = Math.Ceiling(from); t < to; t += 1)
            {
                FramingSample f = tl.FramingAt(t);
                var view = new ViewGeometry(f.ViewWidth);
                int alive = 0, dying = 0;
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn) continue;
                    if (!view.Contains(f.Focus, tl.PositionAt(e, t))) continue;
                    if (t >= deathAt[e]) dying++; else alive++;
                }
                SectionStats s = tl.Sections[tl.Map.SectionIndexAt(t)];
                System.Numerics.Vector2 h = tl.PositionAt(0, t);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,6:F0} s {1,-11} on screen {2,4} + {3,3} dying, target {4,4}, view {5,4:F1} m, hero ({6:F1}, {7:F1})",
                    t, s.Label, alive, dying, s.TargetOnScreen, f.ViewWidth, h.X, h.Y));
            }
        }

        /// <summary>
        /// Per section, how close living monsters come to the hero (centre to centre), sampled every
        /// 0.25 s: the 5th percentile and least of the nearest, and the mean count within 2 m.
        /// </summary>
        static void RingSeries(Timeline tl)
        {
            var deathAt = new double[tl.EntityCount];
            for (int e = 0; e < tl.EntityCount; e++)
            {
                deathAt[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e)) if (a.Kind == ActionKind.Death) { deathAt[e] = a.Impact; break; }
            }
            for (int si = 0; si < tl.Sections.Length; si++)
            {
                SectionStats s = tl.Sections[si];
                var nearest = new System.Collections.Generic.List<double>();
                double within = 0;
                int samples = 0;
                for (double t = s.Start; t < s.End; t += 0.25)
                {
                    System.Numerics.Vector2 h = tl.PositionAt(0, t);
                    double best = double.PositiveInfinity;
                    for (int e = 1; e < tl.EntityCount; e++)
                    {
                        EntityInfo info = tl.Entities[e];
                        if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn || t >= deathAt[e]) continue;
                        double d = System.Numerics.Vector2.Distance(tl.PositionAt(e, t), h);
                        if (d < best) best = d;
                        if (d < 2) within++;
                    }
                    samples++;
                    if (!double.IsInfinity(best)) nearest.Add(best);
                }
                nearest.Sort();
                double p5 = nearest.Count > 0 ? nearest[nearest.Count / 20] : double.NaN, min = nearest.Count > 0 ? nearest[0] : double.NaN;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-11} nearest p5 {1,4:F2} m, min {2,4:F2} m; within 2 m {3,5:F2} on average",
                    s.Label, p5, min, within / Math.Max(1, samples)));
            }
        }

        /// <summary>
        /// The song's timeline checks (TimelineRulesTests and IntensityTests run them on the default
        /// script), on any level script and intensity: one line per check, then its problems.
        /// </summary>
        static void Checks(Timeline tl, LevelScript level, string root)
        {
            ChoreographyData data = ChoreographyFiles.LoadData(root, level.Layout ?? ChoreographyFiles.DefaultLayout);
            void Show(string name, System.Collections.Generic.List<string> problems)
            {
                Console.WriteLine("check " + name + ": " + (problems.Count == 0 ? "ok" : problems.Count + " problems"));
                for (int i = 0; i < Math.Min(problems.Count, 10); i++) Console.WriteLine("  " + problems[i]);
            }
            Show("entities and order", TimelineChecks.EntitiesAndOrder(tl));
            Show("impacts on the grid", TimelineChecks.ImpactsOnGrid(tl, out _));
            Show("gait steps on beats", TimelineChecks.GaitStepsOnBeats(tl, out _));
            Show("free sections move smoothly", TimelineChecks.FreeSectionsMoveSmoothly(tl, out _));
            Show("section death rates", TimelineChecks.SectionDeathRates(tl, level));
            Show("sections hold their targets", TimelineChecks.SectionsHoldTheirTargets(tl, TimelineChecks.SectionOnScreenTolerance, out _));
            Show("overlaps", TimelineChecks.Overlaps(tl, out _, out _));
            Show("hits and deaths have clip and effect", TimelineChecks.HitsAndDeathsHaveClipAndEffect(tl, out _));
            Show("spawns outside the view", TimelineChecks.SpawnsOutsideTheView(tl, out _));
            Show("sounds aggregated", TimelineChecks.SoundsAggregated(tl, out _));
            Show("attacks carry their fit", TimelineChecks.AttacksCarryTheirFit(tl, data.Clips, out _, level.Tuning.ComboMaxSpeed));
            Show("framing eases the zoom", TimelineChecks.FramingEasesTheZoom(tl, level.EnvelopeViewWidthM));
        }

        static void Dump(Timeline tl, string[] args)
        {
            string outPath = Opt(args, "--out");
            if (outPath == null) return;
            double from = DoubleOpt(args, "--from", double.NegativeInfinity), to = DoubleOpt(args, "--to", double.PositiveInfinity);
            TimelineJson.WriteFile(tl, outPath, from, to);
            Console.WriteLine("dump: " + outPath + " (" + new FileInfo(outPath).Length / 1024 + " KiB)");
        }

        static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        static string Opt(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static int IntOpt(string[] args, string name) { string v = Opt(args, name); return v == null ? 0 : int.Parse(v, CultureInfo.InvariantCulture); }

        static double DoubleOpt(string[] args, string name, double fallback = 0)
        {
            string v = Opt(args, name);
            return v == null ? fallback : double.Parse(v, CultureInfo.InvariantCulture);
        }
    }
}
