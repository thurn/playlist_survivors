using System;
using System.Collections.Generic;
using UnityEngine;

namespace PS.Look.Hero
{
    /// <summary>
    /// A hero treatment (neon-look.md, The Axes &gt; Hero): how the knight and the crystal blade are drawn, matching one
    /// monster look but on the style guide's hero light rung, so the hero is the brightest single thing on screen
    /// without growing or an outline. One shader per look (Resources/Look/Shaders/HeroEdge, HeroSolid, HeroHologram,
    /// HeroGem, over Runtime/Hero/Shaders/HeroLook.hlsl) draws both: the knight from the hero bake's faceted meshes
    /// (<see cref="HeroFacets"/>), the blade from its own mesh with every facet edged, its spine light and blow flare
    /// kept (`_BladeFlare`, which both players' HeroBlade sets). Colours from the applied look's palette: hero.body
    /// for the knight, hero.blade for the blade, hero.trail as the faint second colour. The caller owns each
    /// material it gets and destroys it.
    /// </summary>
    public sealed class HeroLook
    {
        public readonly string Name;
        readonly string _shader;

        internal HeroLook(string name, string shader)
        {
            Name = name;
            _shader = shader;
        }

        /// <summary>Whether the look refracts the camera's opaque texture (the prism gem), which the renderers leave off.</summary>
        public bool Refracts => Name == HeroLooks.Prism;

        /// <summary>A new material for the knight.</summary>
        public Material Body() => Make(false);

        /// <summary>A new material for the crystal blade: the knight's look with every facet edged and the spine lit.</summary>
        public Material Blade() => Make(true);

        Material Make(bool blade)
        {
            var shader = Resources.Load<Shader>(_shader) ?? throw new InvalidOperationException($"HeroLook: no shader Resources/{_shader}");
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            var m = new Material(shader) { name = $"hero_{Name}{(blade ? "_blade" : "")}", enableInstancing = false };
            m.SetColor("_Body", (blade ? palette.HeroBlade : palette.HeroBody).linear);
            m.SetColor("_Accent", palette.HeroTrail.linear);
            m.SetFloat("_Blade", blade ? 1 : 0);
            m.SetFloat("_AllEdges", blade ? 1 : 0);
            if (LookFlags.Current.HeroTune is string tune) Tune(m, blade, tune);
            return m;
        }

        /// <summary>
        /// `--hero-tune key=value,...` (<see cref="LookFlags.HeroTuneFlag"/>): sets the shader's float property `_key` on
        /// the knight's and the blade's materials, or on one alone with a `body.` or `blade.` prefix (`Edge=1.6`,
        /// `blade.Glow=2`). A key the look's shader lacks fails, so a typo doesn't pass for a look.
        /// </summary>
        public static void Tune(Material m, bool blade, string pairs)
        {
            foreach (var pair in pairs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    throw new ArgumentException($"{LookFlags.HeroTuneFlag}: bad pair '{pair}'");
                string key = kv[0].Trim();
                if (key.StartsWith("body.")) { if (blade) continue; key = key.Substring(5); }
                else if (key.StartsWith("blade.")) { if (!blade) continue; key = key.Substring(6); }
                if (!m.HasProperty("_" + key)) throw new ArgumentException($"{LookFlags.HeroTuneFlag}: {m.shader.name} has no _{key}");
                m.SetFloat("_" + key, v);
            }
        }

        /// <summary>
        /// Dresses the hero in <paramref name="body"/>: every renderer under <paramref name="hero"/> the hero bake
        /// faceted (<see cref="HeroFacets"/>) draws its faceted mesh in it, on every submesh. Returns how many; 0 when
        /// the bake has none (one from before the hero looks), and then the knight keeps its own look, since the look
        /// draws its lines from the faceted mesh's barycentrics.
        /// </summary>
        public int Dress(GameObject hero, Material body)
        {
            int n = 0;
            foreach (var f in hero.GetComponentsInChildren<HeroFacets>(true))
            {
                if (f.faceted == null) continue;
                var r = f.GetComponent<Renderer>();
                if (r is SkinnedMeshRenderer skinned) skinned.sharedMesh = f.faceted;
                else if (f.TryGetComponent<MeshFilter>(out var filter)) filter.sharedMesh = f.faceted;
                else continue;
                var mats = new Material[f.faceted.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = body;
                r.sharedMaterials = mats;
                n++;
            }
            if (n == 0) Debug.LogWarning($"HeroLook: {hero.name} has no faceted meshes (a hero bake from before the hero looks): the knight keeps its own look");
            return n;
        }
    }

    /// <summary>
    /// The hero looks by option on the hero axis (<see cref="LookAxes.Hero"/>), one per preset, each named after it.
    /// An option without a look (classic, or one not built) draws the knight and blade as before the epic.
    /// </summary>
    public static class HeroLooks
    {
        public const string Edge = "edge", Solid = "solid", Hologram = "hologram", Prism = "prism";

        /// <summary>The options built, for the axis's file.</summary>
        public static readonly string[] Options = { Edge, Solid, Hologram, Prism };

        static readonly Dictionary<string, HeroLook> Built = new Dictionary<string, HeroLook>
        {
            [Edge] = new HeroLook(Edge, "Look/Shaders/HeroEdge"),
            [Solid] = new HeroLook(Solid, "Look/Shaders/HeroSolid"),
            [Hologram] = new HeroLook(Hologram, "Look/Shaders/HeroHologram"),
            [Prism] = new HeroLook(Prism, "Look/Shaders/HeroGem"),
        };

        /// <summary>The look for a hero-axis option, or null (the knight and blade as before) for null or an option without one.</summary>
        public static HeroLook Of(string option) => option != null && Built.TryGetValue(option, out var look) ? look : null;

        /// <summary>The applied preset's hero look (<see cref="ResolvedLook.Active"/>), or null before a look is applied or for classic.</summary>
        public static HeroLook Active => Of(ResolvedLook.Active?.Option(LookAxes.Hero));
    }
}
