using System;
using System.Collections.Generic;
using System.Globalization;

namespace Demo3D.Runtime
{
    /// <summary>
    /// TD11's cuts (performance.md, The Fidelity Floor): each makes a monster or an effect cheaper
    /// to draw, and each is on only if its A/B check passed (benchmarks.md, Fidelity A/B Checks).
    /// Accepted cuts are the defaults; every cut has a name to turn it off or on.
    /// <list type="bullet">
    /// <item><c>lod</c>: LOD1 (about half the triangles) below <see cref="LodPx1"/> projected pixels,
    /// LOD2 (about a quarter) below <see cref="LodPx2"/>, with hysteresis and a 0.2 s dithered
    /// crossfade (crowd.md).</item>
    /// <item><c>shadow-proxy</c>: the crowd's shadow-only draw uses LOD2 (<see cref="ShadowProxyLod"/>, <c>--shadow-lod 1</c> for LOD1).</item>
    /// <item><c>clips30</c>: the crowd samples clips baked at 30 Hz instead of 60 Hz, interpolated.</item>
    /// <item><c>light-cell</c>: effect lights cluster on <see cref="LightCellM"/> cells instead of 1.5 m.</item>
    /// <item><c>merge</c>: identical spawns of one effect in a frame within <see cref="MergePx"/>
    /// pixels of each other merge into one with their combined density.</item>
    /// <item><c>drop-glow</c>: effects drop their large soft glow quads, leaving bloom the halo.</item>
    /// </list>
    /// The command line: <c>--cuts default|none|all|name,name,-name</c> (a list starting with
    /// <c>-name</c> or <c>+name</c> edits the defaults), <c>--lod-px 120,60</c>, <c>--light-cell 2</c>,
    /// <c>--merge-px 8</c>, <c>--shadow-lod 2</c>. A bench scene's sceneOptions <c>cuts</c> (same syntax) overrides it.
    /// </summary>
    public sealed class Cuts
    {
        public const string Lod = "lod", ShadowProxy = "shadow-proxy", Clips30 = "clips30",
            LightCell = "light-cell", Merge = "merge", DropGlow = "drop-glow";

        public static readonly string[] All = { Lod, ShadowProxy, Clips30, LightCell, Merge, DropGlow };

        /// <summary>
        /// The accepted cuts (TD11's verdicts, docs/tech-demo-3d/plan.md, TD11): LOD at 120 / 60 px
        /// and 30 Hz clips, which pass the A/B check alone and together. light-cell (2 m) and
        /// drop-glow pass alone but not beside them; shadow-proxy and light-cell at 3 m fail; merge
        /// passes but saves nothing.
        /// </summary>
        public static readonly string[] Defaults = { Lod, Clips30 };

        readonly HashSet<string> _on = new HashSet<string>();

        public float LodPx1 = 120, LodPx2 = 60; // TD11 tried 60 / 30 (passes, saves less) and 120 / 60
        public int ShadowProxyLod = 2;
        public float LightCellM = 2f; // 3 m failed the A/B check; 2 m passes alone
        public float MergePx = 8f;

        public bool On(string cut) => _on.Contains(cut);
        public IEnumerable<string> Enabled => _on;

        public override string ToString() => _on.Count == 0 ? "none" : string.Join(",", _on);

        /// <summary>The cuts in force for this run (set by DemoMain or a bench scene before it builds its renderers).</summary>
        public static Cuts Active = FromCommandLine();

        public static Cuts FromCommandLine() => Parse(Args.Get("--cuts", "default"), true);

        /// <summary>The command line's cuts, with a bench scene's sceneOptions <c>cuts</c> (and <c>lodPx</c>, <c>lightCell</c>, <c>mergePx</c>) on top.</summary>
        public static Cuts FromOptions(IDictionary<string, object> options)
        {
            var c = options != null && options.TryGetValue("cuts", out var v) && v is string s ? Parse(s, true) : FromCommandLine();
            if (options == null) return c;
            if (options.TryGetValue("lodPx", out var px) && px is string pxs) c.ParseLodPx(pxs);
            if (options.TryGetValue("lightCell", out var lc) && lc != null) c.LightCellM = Convert.ToSingle(lc, CultureInfo.InvariantCulture);
            if (options.TryGetValue("mergePx", out var mp) && mp != null) c.MergePx = Convert.ToSingle(mp, CultureInfo.InvariantCulture);
            if (options.TryGetValue("shadowLod", out var sl) && sl != null) c.ShadowProxyLod = Convert.ToInt32(sl, CultureInfo.InvariantCulture);
            return c;
        }

        /// <summary>
        /// "default", "none", "all", or a comma list of cut names; a list whose first item starts
        /// with + or - edits the defaults ("-lod" is the defaults without LOD).
        /// </summary>
        public static Cuts Parse(string spec, bool readParameters = false)
        {
            var c = new Cuts();
            spec = (spec ?? "default").Trim();
            if (readParameters)
            {
                var px = Args.Get("--lod-px");
                if (px != null) c.ParseLodPx(px);
                c.LightCellM = Args.GetFloat("--light-cell", c.LightCellM);
                c.MergePx = Args.GetFloat("--merge-px", c.MergePx);
                c.ShadowProxyLod = Args.GetInt("--shadow-lod", c.ShadowProxyLod);
            }
            if (spec == "none" || spec.Length == 0) return c;
            if (spec == "default") { foreach (var d in Defaults) c._on.Add(d); return c; }
            if (spec == "all") { foreach (var d in All) c._on.Add(d); return c; }
            var items = spec.Split(',');
            if (items[0].StartsWith("+") || items[0].StartsWith("-"))
                foreach (var d in Defaults) c._on.Add(d);
            foreach (var raw in items)
            {
                var item = raw.Trim();
                bool off = item.StartsWith("-");
                var name = item.TrimStart('+', '-');
                if (name == "default") { foreach (var d in Defaults) c._on.Add(d); continue; }
                if (Array.IndexOf(All, name) < 0) throw new ArgumentException($"Cuts: no cut '{name}' (known: {string.Join(", ", All)})");
                if (off) c._on.Remove(name); else c._on.Add(name);
            }
            return c;
        }

        void ParseLodPx(string s)
        {
            var v = s.Split(',');
            LodPx1 = float.Parse(v[0], CultureInfo.InvariantCulture);
            if (v.Length > 1) LodPx2 = float.Parse(v[1], CultureInfo.InvariantCulture);
        }
    }
}
