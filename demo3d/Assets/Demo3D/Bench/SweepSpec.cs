using System;
using System.Collections.Generic;
using System.Globalization;

namespace Demo3D.Bench
{
    /// <summary>The pass lines on Cygnus (performance.md, Performance Budget).</summary>
    public sealed class Lines
    {
        public double GpuMs = 4.2;
        public double MainMs = 10.4;
        public double RenderMs = 10.4;
        /// <summary>No single frame may exceed this multiple of its line.</summary>
        public double MaxFactor = 2.0;
    }

    /// <summary>The quiet-machine rules (benchmarks.md, Quiet machine).</summary>
    public sealed class QuietRules
    {
        /// <summary>Other processes' share of total CPU that counts as busy.</summary>
        public double CpuPct = 10;
        /// <summary>The window, in seconds, over which their CPU is averaged.</summary>
        public double WindowS = 5;
        /// <summary>The longest wait for a quiet machine before a sweep runs anyway.</summary>
        public double MaxWaitS = 600;
        /// <summary>
        /// Other processes' share of the GPU's 3D engine (any 1 s sample) that marks a run noisy: another
        /// session's player or editor rendering beside a sweep (TD7's addition to the design's rules).
        /// </summary>
        public double GpuPct = 5;
        /// <summary>The wait for a quiet machine before repeating a noisy run.</summary>
        public double RepeatWaitS = 60;
        /// <summary>The GPU must be under this temperature before a sweep starts.</summary>
        public double GpuMaxC = 50;
        /// <summary>The longest wait for the GPU to cool under <see cref="GpuMaxC"/>.</summary>
        public double CoolWaitS = 180;
        /// <summary>How many times a noisy run is repeated.</summary>
        public int Repeats = 2;
        /// <summary>
        /// Whether the player's window must hold the foreground: refused at the start without it, and
        /// a run that loses it is noisy. False only to measure while something the harness may not
        /// dismiss (a system dialog) holds the foreground; focus loss is then recorded, not noise.
        /// </summary>
        public bool RequireForeground = true;
    }

    /// <summary>One combination of the swept axes other than N.</summary>
    public sealed class Point
    {
        public double ViewWidthM;
        public string Load;
        public string Aa;

        public string Key => string.Format(CultureInfo.InvariantCulture, "{0}m/{1}/{2}", ViewWidthM, Load, Aa);

        /// <summary>How far apart two points are, for seeding a search from its nearest neighbour.</summary>
        public double Distance(Point o) =>
            (Load == o.Load ? 0 : 1) + (Aa == o.Aa ? 0 : 1) + Math.Abs(Math.Log(ViewWidthM / o.ViewWidthM));
    }

    /// <summary>
    /// A sweep: a JSON spec of axes (benchmarks.md, The sweep). For each combination of the other
    /// axes the harness searches for the largest N that passes, or with <c>n.fixed</c> measures the
    /// listed counts only.
    /// </summary>
    public sealed class SweepSpec
    {
        public string Scene = "synthetic";
        public double Seconds = 20;
        public double Warmup = 5;
        public double Rewarm = 3;
        public double ProbeSeconds = 8;
        public int NLo = 100;
        public int NHi = 12000;
        public double Resolution = 0.05;
        public List<int> FixedN;
        public List<double> ViewWidthM = new List<double> { 40 };
        public List<string> Load = new List<string> { "demo" };
        public List<string> Aa = new List<string> { "msaa4" };
        public int JobWorkers = 6;
        public int ScreenWidth = 1920;
        public int ScreenHeight = 1080;
        /// <summary>The roster's densest packing, monsters per m² (performance.md).</summary>
        public double PackingPerM2 = 2.4;
        /// <summary>Overdraw and on-screen counters run on every this-many frames (and after downbeats).</summary>
        public int CounterEvery = 60;
        public Lines Lines = new Lines();
        public QuietRules Quiet = new QuietRules();
        public Dictionary<string, object> SceneOptions = new Dictionary<string, object>();

        public static SweepSpec Parse(string json)
        {
            if (!(Json.Parse(json) is Dictionary<string, object> d))
                throw new FormatException("sweep spec: not a JSON object");
            var s = new SweepSpec();
            foreach (var kv in d)
            {
                switch (kv.Key)
                {
                    case "scene": s.Scene = (string)kv.Value; break;
                    case "seconds": s.Seconds = Num(kv); break;
                    case "warmup": s.Warmup = Num(kv); break;
                    case "rewarm": s.Rewarm = Num(kv); break;
                    case "probeSeconds": s.ProbeSeconds = Num(kv); break;
                    case "n":
                        var n = Obj(kv);
                        foreach (var nk in n)
                        {
                            if (nk.Key == "search")
                            {
                                var r = List(nk);
                                if (r.Count != 2) throw new FormatException("sweep spec: n.search needs [lo, hi]");
                                s.NLo = (int)(double)r[0];
                                s.NHi = (int)(double)r[1];
                            }
                            else if (nk.Key == "resolution") s.Resolution = Num(nk);
                            else if (nk.Key == "fixed") s.FixedN = List(nk).ConvertAll(o => (int)(double)o);
                            else throw new FormatException($"sweep spec: unknown key n.{nk.Key}");
                        }
                        break;
                    case "viewWidthM": s.ViewWidthM = List(kv).ConvertAll(o => (double)o); break;
                    case "load": s.Load = List(kv).ConvertAll(o => (string)o); break;
                    case "aa": s.Aa = List(kv).ConvertAll(o => (string)o); break;
                    case "jobWorkers": s.JobWorkers = (int)Num(kv); break;
                    case "screen":
                        var sc = List(kv);
                        s.ScreenWidth = (int)(double)sc[0];
                        s.ScreenHeight = (int)(double)sc[1];
                        break;
                    case "packingPerM2": s.PackingPerM2 = Num(kv); break;
                    case "counterEvery": s.CounterEvery = (int)Num(kv); break;
                    case "lines":
                        foreach (var lk in Obj(kv))
                        {
                            if (lk.Key == "gpuMs") s.Lines.GpuMs = Num(lk);
                            else if (lk.Key == "mainMs") s.Lines.MainMs = Num(lk);
                            else if (lk.Key == "renderMs") s.Lines.RenderMs = Num(lk);
                            else if (lk.Key == "maxFactor") s.Lines.MaxFactor = Num(lk);
                            else throw new FormatException($"sweep spec: unknown key lines.{lk.Key}");
                        }
                        break;
                    case "quiet":
                        foreach (var qk in Obj(kv))
                        {
                            switch (qk.Key)
                            {
                                case "cpuPct": s.Quiet.CpuPct = Num(qk); break;
                                case "gpuPct": s.Quiet.GpuPct = Num(qk); break;
                                case "windowS": s.Quiet.WindowS = Num(qk); break;
                                case "maxWaitS": s.Quiet.MaxWaitS = Num(qk); break;
                                case "repeatWaitS": s.Quiet.RepeatWaitS = Num(qk); break;
                                case "gpuMaxC": s.Quiet.GpuMaxC = Num(qk); break;
                                case "coolWaitS": s.Quiet.CoolWaitS = Num(qk); break;
                                case "repeats": s.Quiet.Repeats = (int)Num(qk); break;
                                case "requireForeground":
                                    s.Quiet.RequireForeground = qk.Value is bool b ? b
                                        : throw new FormatException("sweep spec: quiet.requireForeground must be true or false");
                                    break;
                                default: throw new FormatException($"sweep spec: unknown key quiet.{qk.Key}");
                            }
                        }
                        break;
                    case "sceneOptions": s.SceneOptions = Obj(kv); break;
                    default:
                        if (!kv.Key.StartsWith("_", StringComparison.Ordinal)) // "_comment" etc.
                            throw new FormatException($"sweep spec: unknown key {kv.Key}");
                        break;
                }
            }
            if (s.NLo < 1 || s.NHi < s.NLo) throw new FormatException("sweep spec: n.search must be 1 <= lo <= hi");
            if (s.Resolution <= 0 || s.Resolution >= 1) throw new FormatException("sweep spec: n.resolution must be in (0, 1)");
            if (s.ViewWidthM.Count == 0 || s.Load.Count == 0 || s.Aa.Count == 0)
                throw new FormatException("sweep spec: viewWidthM, load and aa need at least one value");
            return s;
        }

        /// <summary>
        /// Every point, ordered so that consecutive points differ in one axis (view width varies
        /// fastest), which keeps each search's nearest measured neighbour close.
        /// </summary>
        public List<Point> Points()
        {
            var list = new List<Point>();
            foreach (var aa in Aa)
            foreach (var load in Load)
            foreach (var w in ViewWidthM)
                list.Add(new Point { ViewWidthM = w, Load = load, Aa = aa });
            return list;
        }

        static double Num(KeyValuePair<string, object> kv) =>
            kv.Value is double v ? v : throw new FormatException($"sweep spec: {kv.Key} must be a number");

        static Dictionary<string, object> Obj(KeyValuePair<string, object> kv) =>
            kv.Value as Dictionary<string, object> ?? throw new FormatException($"sweep spec: {kv.Key} must be an object");

        static List<object> List(KeyValuePair<string, object> kv) =>
            kv.Value as List<object> ?? throw new FormatException($"sweep spec: {kv.Key} must be an array");
    }
}
