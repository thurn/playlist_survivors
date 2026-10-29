using System;
using System.Globalization;
using System.IO;
using System.Text;
using Demo3D.Choreography;

namespace Demo3D.Choreography.Cli
{
    /// <summary>
    /// dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song [--seed N] [--out dump.json] [--from S --to S]
    ///                                                                        stress S3 [--n N] [--view W] [--load L] [--seconds S] [--out dump.json]
    ///                                                                       usage: song | stress <S1..S6> | placements | timing [--out timing.md]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) { Console.Error.WriteLine("usage: song | stress <S1..S5> | timing (see Program.cs)"); return 2; }
            string root = ChoreographyFiles.FindRepoRoot(Opt(args, "--root"));
            switch (args[0])
            {
                case "song":
                {
                    string seed = Opt(args, "--seed");
                    Timeline tl = ChoreographyFiles.BuildSong(root, seed == null ? (uint?)null : uint.Parse(seed, CultureInfo.InvariantCulture));
                    Console.Write(TimelineReport.Summary(tl));
                    Snapshot(tl, ChoreographyFiles.LoadData(root, ChoreographyFiles.LoadLevel(root).Layout ?? ChoreographyFiles.DefaultLayout), args);
                    if (Has(args, "--hero"))
                        for (double ht = 0; ht < tl.Duration; ht += 5)
                        {
                            System.Numerics.Vector2 hp = tl.PositionAt(0, ht);
                            Console.WriteLine("hero at " + ht.ToString("F0", CultureInfo.InvariantCulture) + " s: (" + hp.X.ToString("F1", CultureInfo.InvariantCulture) + ", " + hp.Y.ToString("F1", CultureInfo.InvariantCulture) + ") in " + tl.Sections[tl.Map.SectionIndexAt(ht)].Label);
                        }
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
                case "timing":
                {
                    string md = TimelineReport.TimingTables(root, IntOpt(args, "--repeat") > 0 ? IntOpt(args, "--repeat") : 1);
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
