using System;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// How the void world's crystal props and formations are drawn (neon-look.md, The Axes &gt; World; the style guide's
    /// Crystal props): one material in CrystalProp.shader, in the palette's <c>shatter.gem</c>, self-lit, its facets'
    /// edges drawn from the crystal kit's edge data. The options, one per preset, follow the monster looks:
    /// - `edge-glass` (edge): near-black glass, the gem in a fresnel rim and lit facet edges that flare on the beat;
    /// - `solid-glass` (solid): the gem as a bright body falling off toward the silhouette, its facets cut by dark lines;
    /// - `wireframe` (hologram): lit edges over a fill at the void;
    /// - `refractive-gem` (prism): the void and the grid behind seen through the gem, bent by its facets, under fainter
    ///   edges (it turns on the camera's opaque texture: <see cref="Refracts"/>).
    /// A blow lights a piece toward the hero's rung; at rest its faces sit at enemy light. A shot's formations, the set
    /// pieces that shatter on the beat, draw in <see cref="Featured"/>: the same look lit from inside, brighter than
    /// the props around them (the formations people loved were the white-gold glow). A hit's flash and a break's glow
    /// are full only in a special moment (CrystalProp.shader's _FlashCalm; neon-look.md, Restraint and special moments).
    /// The impassable ground's hedges (<see cref="BlockedGround"/>) draw in <see cref="Terrain"/>: the same look dimmer,
    /// in the grid's hue, so the world's walls read as the floor's and apart from the gem of what breaks.
    /// </summary>
    public sealed class StructureLook : IDisposable
    {
        /// <summary>The props' material in the package's Resources (it carries the shader into both builds).</summary>
        public const string MaterialPath = "Look/CrystalProp";
        public const string EdgeGlass = "edge-glass", SolidGlass = "solid-glass", Wireframe = "wireframe", RefractiveGem = "refractive-gem";

        struct Settings
        {
            public float Body, Shade, Rim, RimPower, Edge, EdgePx, EdgeDark, Refraction, Pulse;
        }

        static Settings Of(string option)
        {
            switch (option)
            {
                // Calm (ps-wnj.24): resting edges at enemy light, about half of the first tuning, and a third of its beat flare.
                case EdgeGlass: return new Settings { Body = 0.03f, Shade = 0.3f, Rim = 0.3f, RimPower = 3f, Edge = 0.6f, EdgePx = 1.3f, Pulse = 0.12f };
                case SolidGlass: return new Settings { Body = 0.4f, Shade = 0.6f, Rim = -0.4f, RimPower = 2f, EdgePx = 1.6f, EdgeDark = 1f };
                case Wireframe: return new Settings { Body = 0.02f, Rim = 0.06f, RimPower = 2f, Edge = 0.55f, EdgePx = 1.2f, Pulse = 0.12f };
                case RefractiveGem: return new Settings { Body = 0.03f, Shade = 0.4f, Rim = 0.25f, RimPower = 4f, Edge = 0.35f, EdgePx = 1.1f, Refraction = 0.035f, Pulse = 0.1f };
                default: throw new ArgumentException($"StructureLook: no structure look '{option}'");
            }
        }

        public readonly string Option;
        public readonly Material Material;
        /// <summary>The formations' material: the look with its faces, rim and edges brighter.</summary>
        public readonly Material Featured;
        /// <summary>The impassable ground's material: the look's faces, rim and edges dimmer, in <c>grid.pulse</c> (<see cref="TintTerrain"/>).</summary>
        public readonly Material Terrain;
        /// <summary>The palette's <c>shatter.gem</c>, linear: the props' colour and their breaks' bursts.</summary>
        public readonly Color Gem;

        /// <summary>Whether the look refracts the scene behind it, so the camera needs its opaque texture.</summary>
        public bool Refracts => Option == RefractiveGem;

        /// <summary>The structure look <paramref name="look"/> names, or null when it names none built (the arena's own structures).</summary>
        public static StructureLook For(ResolvedLook look)
        {
            string option = look?.Option(LookAxes.Structures);
            return option == null ? null : new StructureLook(option, look.Palette);
        }

        public StructureLook(string option, Palette palette)
        {
            var s = Of(option);
            Option = option;
            var shared = Resources.Load<Material>(MaterialPath)
                         ?? throw new InvalidOperationException($"StructureLook: no material at Resources/{MaterialPath} in the look package");
            Gem = palette != null ? palette.ShatterGem.linear : CrystalFormations.Gem;
            Gem.a = 1;
            Material = Make(shared, "Crystal prop (" + option + ")", s);
            var f = s;
            f.Body = Mathf.Clamp(FeaturedBody * s.Body, FeaturedBodyMin, 0.9f);
            f.Rim = s.Rim > 0 ? FeaturedRim * s.Rim : s.Rim;
            f.Edge = FeaturedEdge * s.Edge;
            Featured = Make(shared, "Crystal formation (" + option + ")", f);
            var t = s;
            t.Body = TerrainLight * s.Body;
            t.Rim = TerrainLight * s.Rim;
            t.Edge = s.Edge > 0 ? Mathf.Max(TerrainLight * s.Edge, TerrainEdgeMin) : 0f;
            Terrain = Make(shared, "Crystal terrain (" + option + ")", t);
            TintTerrain(palette != null ? palette.GridPulse : new Color(0.31f, 0.49f, 1f));
        }

        /// <summary>The terrain's light against the props': its faces', rim's and edges'.</summary>
        public const float TerrainLight = 0.6f;
        /// <summary>The least light of the terrain's edges where the look draws lit edges (refractive-gem's faint ones would vanish), under every look's props'.</summary>
        public const float TerrainEdgeMin = 0.3f;

        /// <summary>The terrain in <paramref name="hue"/> (sRGB; a zone's tint under `hued-zones`), at full value.</summary>
        public void TintTerrain(Color hue)
        {
            var linear = hue.linear;
            float max = Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b), 1e-4f);
            Terrain.SetColor("_Tint", new Color(linear.r / max, linear.g / max, linear.b / max, 1f));
        }

        /// <summary>
        /// The formations' lift over the props: their faces' light (at least a third of the gem, at most 0.9: the style
        /// guide's crystal gem on the hero's rung), rim and edges.
        /// </summary>
        public const float FeaturedBody = 3f, FeaturedBodyMin = 0.35f, FeaturedRim = 1.4f, FeaturedEdge = 1.4f;

        Material Make(Material shared, string name, Settings s)
        {
            var m = new Material(shared) { name = name, enableInstancing = false };
            m.SetColor("_Tint", Gem);
            m.SetFloat("_Body", s.Body);
            m.SetFloat("_Shade", s.Shade);
            m.SetFloat("_Rim", s.Rim);
            m.SetFloat("_RimPower", s.RimPower);
            m.SetFloat("_Edge", s.Edge);
            m.SetFloat("_EdgePx", s.EdgePx);
            m.SetFloat("_EdgeDark", s.EdgeDark);
            m.SetFloat("_Refraction", s.Refraction);
            m.SetFloat("_Pulse", s.Pulse);
            if (Refracts) m.EnableKeyword("_REFRACT");
            return m;
        }

        public void Dispose()
        {
            foreach (var m in new[] { Material, Featured, Terrain })
            {
                if (m == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(m);
                else UnityEngine.Object.DestroyImmediate(m);
            }
        }
    }
}
