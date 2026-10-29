using System;
using System.Collections.Generic;
using System.Globalization;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The arena's rendering rungs (environment.md, Environment rendering; TD14's ladder) as one set
    /// of switches, read from a sweep's sceneOptions or the demo's command line:
    /// - <see cref="Rung"/>: the baked scene's lighting, "e0" (stock: realtime shadows from all) or
    ///   "e2" (lightmaps, the static set out of the cascades, the static shadow map);
    /// - <see cref="Grd"/> (E1): the GPU Resident Drawer, "off", "on" or "occlusion" (with its GPU
    ///   occlusion culling in cameras);
    /// - <see cref="Merged"/> (within E1): the scene variant whose small props are merged into one
    ///   mesh per 16 m cell and material;
    /// - <see cref="TexCap"/> (E5): the kit's textures capped at this size (0: as imported, 2048);
    /// - <see cref="Prepass"/> (E6): the static set in the depth-normals prepass (true) or out of it;
    /// - <see cref="Without"/> (the bounds of E3 and E4, bench only): "small" hides the small props
    ///   (E3's decimated LODs can save at most what they cost), "forest" the forest's interior
    ///   (E4's impostors likewise). Neither is a look anyone ships.
    /// </summary>
    public sealed class ArenaOptions
    {
        public const string GrdOff = "off", GrdOn = "on", GrdOcclusion = "occlusion";

        public string Rung = ArenaIndex.E2;
        public float Density = 1f;
        public string Grd = GrdOff;
        public bool Merged;
        public int TexCap;
        public bool Prepass = true;
        public string Without = "";
        public const string WithoutSmall = "small", WithoutForest = "forest";
        /// <summary>A small prop: its bounds' largest side under this (ArenaBaker.MergeMaxM too), outside landmarks and structures.</summary>
        public const float SmallPropM = 4f;

        /// <summary>
        /// The ladder's final rung (TD14): the demo's and the stress scenes' default. S0 keeps the
        /// stock baseline (<see cref="Stock"/>) unless a sweep names its rungs.
        /// </summary>
        public static ArenaOptions Final() => new ArenaOptions { Rung = ArenaIndex.E2, Density = AuthoredDensity, Grd = GrdOff, Merged = false, TexCap = 0, Prepass = true };

        static float _authored = -1;

        /// <summary>
        /// The authored decoration density (TD15's headroom step): the arena layout's
        /// <c>decorationDensity</c>, which the choreography's placement list (what monsters walk
        /// round) is generated at, so the scene the player loads by default is the one the timeline
        /// navigates. 1 if the playback data isn't there (the editor's tools pass their own).
        /// </summary>
        public static float AuthoredDensity
        {
            get
            {
                if (_authored > 0) return _authored;
                _authored = 1f;
                try
                {
                    var layout = Playback.PlaybackData.Load()?.Layout("arena");
                    if (layout != null) _authored = Choreography.JsonValue.Parse(layout.text)["decorationDensity"].AsFloat(1f);
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("ArenaOptions: no authored density (" + e.Message + "); 1"); }
                return _authored;
            }
        }

        /// <summary>E0, the stock baseline.</summary>
        public static ArenaOptions Stock() => new ArenaOptions { Rung = ArenaIndex.E0 };

        public ArenaOptions Clone() => (ArenaOptions)MemberwiseClone();

        /// <summary>
        /// Reads sceneOptions over <paramref name="defaults"/>: <paramref name="rungKey"/> ("rung" for
        /// S0, "arena" for S1–S6), <c>density</c>, <c>grd</c>, <c>merged</c>, <c>texCap</c>, <c>prepass</c>
        /// ("in" or "out", or a bool).
        /// </summary>
        public static ArenaOptions FromBench(IReadOnlyDictionary<string, object> o, string rungKey, ArenaOptions defaults)
        {
            var a = defaults.Clone();
            if (o == null) return a;
            if (o.TryGetValue(rungKey, out var r) && r is string rs) a.Rung = rs.ToLowerInvariant();
            if (o.TryGetValue("density", out var d) && d is double dd) a.Density = (float)dd;
            if (o.TryGetValue("grd", out var g))
                a.Grd = g is bool gb ? (gb ? GrdOn : GrdOff) : ParseGrd(g as string);
            if (o.TryGetValue("merged", out var m) && m is bool mb) a.Merged = mb;
            if (o.TryGetValue("texCap", out var t) && t is double td) a.TexCap = (int)td;
            if (o.TryGetValue("prepass", out var p))
                a.Prepass = p is bool pb ? pb : ParsePrepass(p as string);
            if (o.TryGetValue("without", out var w) && w is string ws) a.Without = ws.ToLowerInvariant();
            a.Validate();
            return a;
        }

        /// <summary>
        /// The demo's flags over <paramref name="defaults"/>: <c>--arena</c>, <c>--density</c>, <c>--grd
        /// off|on|occlusion</c>, <c>--merged</c> / <c>--no-merged</c>, <c>--tex-cap N</c>, <c>--prepass in|out</c>.
        /// </summary>
        public static ArenaOptions FromCommandLine(ArenaOptions defaults)
        {
            var a = defaults.Clone();
            a.Rung = Args.Get("--arena", a.Rung).ToLowerInvariant();
            a.Density = Args.GetFloat("--density", a.Density);
            var g = Args.Get("--grd");
            if (g != null) a.Grd = ParseGrd(g);
            if (Args.Has("--merged")) a.Merged = true;
            if (Args.Has("--no-merged")) a.Merged = false;
            a.TexCap = Args.GetInt("--tex-cap", a.TexCap);
            var p = Args.Get("--prepass");
            if (p != null) a.Prepass = ParsePrepass(p);
            a.Without = Args.Get("--without", a.Without).ToLowerInvariant();
            if (a.Rung != "off") a.Validate();
            return a;
        }

        static string ParseGrd(string s)
        {
            s = (s ?? GrdOff).ToLowerInvariant();
            if (s != GrdOff && s != GrdOn && s != GrdOcclusion)
                throw new ArgumentException($"ArenaOptions: grd must be off, on or occlusion, not {s}");
            return s;
        }

        static bool ParsePrepass(string s)
        {
            s = (s ?? "in").ToLowerInvariant();
            if (s == "in") return true;
            if (s == "out") return false;
            throw new ArgumentException($"ArenaOptions: prepass must be in or out, not {s}");
        }

        void Validate()
        {
            if (Rung != ArenaIndex.E0 && Rung != ArenaIndex.E2 && Rung != "off")
                throw new ArgumentException($"ArenaOptions: rung must be e0, e2 or off, not {Rung}");
            if (TexCap != 0 && (TexCap < 256 || TexCap > 2048 || (TexCap & (TexCap - 1)) != 0))
                throw new ArgumentException($"ArenaOptions: texCap must be 0 or a power of two from 256 to 2048, not {TexCap}");
            if (Without != "" && Without != WithoutSmall && Without != WithoutForest)
                throw new ArgumentException($"ArenaOptions: without must be small or forest, not {Without}");
        }

        /// <summary>A short label: "e2 grd:on merged tex:1024 prepass:out" (defaults left out).</summary>
        public string Label()
        {
            var parts = new List<string> { Rung + " " + Density.ToString("0.###", CultureInfo.InvariantCulture) + "x" };
            if (Grd != GrdOff) parts.Add("grd:" + Grd);
            if (Merged) parts.Add("merged");
            if (TexCap > 0) parts.Add("tex:" + TexCap);
            if (!Prepass) parts.Add("prepass:out");
            if (Without != "") parts.Add("without:" + Without);
            return string.Join(" ", parts);
        }
    }
}
