using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// The edge look's numbers (design.md, The Look; style.md, The Edge Look): MonsterEdge's and MonsterEdgeShard's
    /// gains, as the trailer's edge preset drew with them (the frozen look package's shader defaults and EdgeLook).
    /// </summary>
    public struct EdgeGains
    {
        /// <summary>The bodies: the rim's gain and falloff, the feature lines' gain, width (px at 2160 rows, the trailer's
        /// capture: MonsterLines.hlsl) and lift on the beat, the event rings' light, the core at rest and on the beat and its radius (share of the body's).</summary>
        public float Rim, RimPower, Edge, EdgePx, Beat, Ring, CoreGlow, CoreFlare, CoreSize;
        /// <summary>The motion layers: a step's and a kick's springs, the swarm's tilt and wobble.</summary>
        public bool Springs, Swarm;
        /// <summary>The shards: the rim's gain and falloff, the lines' gain and width, their lift while fresh.</summary>
        public float ShardRim, ShardRimPower, ShardEdge, ShardEdgePx, ShardBeat;

        /// <summary>
        /// `--look trailer`'s: exactly the trailer's (the look package as captured, git 138684f2), never changed
        /// (MonsterLookTests pins them); tune <see cref="Default"/> instead.
        /// </summary>
        public static EdgeGains Trailer => new EdgeGains
        {
            Rim = 2f, RimPower = 4f, Edge = 2.6f, EdgePx = 1.25f, Beat = 0.3f, Ring = 1.1f,
            CoreGlow = 0.1f, CoreFlare = 0.35f, CoreSize = 0.18f,
            Springs = true, Swarm = true,
            ShardRim = 2f, ShardRimPower = 4f, ShardEdge = 1.4f, ShardEdgePx = 1.25f, ShardBeat = 0.6f,
        };

        /// <summary>The game's: the trailer's until a change is judged to look better (design.md, Changing the look).</summary>
        public static EdgeGains Default => Trailer;

        public static EdgeGains Of(MonsterLook look) => look == MonsterLook.Trailer ? Trailer : Default;
    }

    /// <summary>
    /// The hero's numbers in the edge look (design.md, The Hero): HeroEdge's gains, as the trailer drew the knight with them
    /// (the frozen look package's HeroEdge.shader defaults at git 138684f2).
    /// </summary>
    public struct HeroGains
    {
        /// <summary>
        /// The glass's light, the rim's gain and falloff, the lines' gain, width (px at 2160 rows) and lift on the beat,
        /// the chest core's gain and radius (m), the blade's spine light.
        /// </summary>
        public float Glass, Rim, RimPower, Edge, EdgePx, Beat, CoreGlow, CoreSize, Glow;

        /// <summary>`--look trailer`'s: exactly the trailer's, never changed (MonsterLookTests pins them); tune <see cref="Default"/>.</summary>
        public static HeroGains Trailer => new HeroGains
        {
            Glass = 0.03f, Rim = 0.6f, RimPower = 3f, Edge = 1f, EdgePx = 1.4f, Beat = 0.15f, CoreGlow = 0.35f, CoreSize = 0.22f, Glow = 0.6f,
        };

        /// <summary>The game's: the trailer's until a change is judged to look better (design.md, Changing the look).</summary>
        public static HeroGains Default => Trailer;

        public static HeroGains Of(MonsterLook look) => look == MonsterLook.Trailer ? Trailer : Default;
    }

    /// <summary>
    /// The edge look's materials (plan.md, Rendering &gt; The edge look), ported by review from the frozen look package's
    /// EdgeLook: near-black glass bodies whose only light is their type's palette hue, levelled to one luminance, in a
    /// fresnel rim, in lines on the mesh's feature edges and in a small inner core that flares on the beat; shards keep
    /// the edges and light where they broke. Shaders Resources/Crowd/MonsterEdge and MonsterEdgeShard; colours from the
    /// game's palette (the type's hue, void.body for the glass, white for the flash). The caller owns each material.
    /// </summary>
    public static class EdgeLook
    {
        public const string BodyShader = "Crowd/MonsterEdge";
        public const string ShardShader = "Crowd/MonsterEdgeShard";
        public const string HeroShader = "Crowd/HeroEdge";

        /// <summary>The luminance every type's hue is drawn at (linear), so the post's bloom treats the hues alike.</summary>
        public const float HueLuminance = 0.4f;

        /// <summary>
        /// A hue scaled to <see cref="HueLuminance"/>: the palette's enemy hues span about 0.25 (blue, magenta) to 0.6
        /// (cyan, amber) in linear luminance, and drawn as they are an amber elite blooms to yellow-white while a blue
        /// demon stays dim.
        /// </summary>
        public static Color Level(Color linear)
        {
            float l = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            var c = linear * (HueLuminance / Mathf.Max(l, 0.05f));
            c.a = 1;
            return c;
        }

        /// <summary>A new material for the type's bodies in <paramref name="look"/>.</summary>
        public static Material Body(MonsterType type, MonsterLook look, Palette palette)
        {
            var g = EdgeGains.Of(look);
            var m = Make(BodyShader, type, palette, "_edge");
            m.SetFloat("_Rim", g.Rim);
            m.SetFloat("_RimPower", g.RimPower);
            m.SetFloat("_Edge", g.Edge);
            m.SetFloat("_EdgePx", g.EdgePx);
            m.SetFloat("_Beat", g.Beat);
            m.SetFloat("_Ring", g.Ring);
            m.SetFloat("_CoreGlow", g.CoreGlow);
            m.SetFloat("_CoreFlare", g.CoreFlare);
            m.SetFloat("_CoreSize", g.CoreSize);
            m.SetFloat("_Springs", g.Springs ? 1 : 0);
            m.SetFloat("_Swarm", g.Swarm ? 1 : 0);
            return m;
        }

        /// <summary>A new material for the type's shards in <paramref name="look"/>.</summary>
        public static Material Shards(MonsterType type, MonsterLook look, Palette palette)
        {
            var g = EdgeGains.Of(look);
            var m = Make(ShardShader, type, palette, "_edge_shards");
            m.SetFloat("_Rim", g.ShardRim);
            m.SetFloat("_RimPower", g.ShardRimPower);
            m.SetFloat("_Edge", g.ShardEdge);
            m.SetFloat("_EdgePx", g.ShardEdgePx);
            m.SetFloat("_Beat", g.ShardBeat);
            return m;
        }

        /// <summary>
        /// A new material for the hero's figure (<see cref="MonsterFigure"/>) in <paramref name="look"/>: HeroEdge in the
        /// palette's hero.body, its blade in hero.blade.
        /// </summary>
        public static Material Hero(MonsterType type, MonsterLook look, Palette palette)
        {
            var g = HeroGains.Of(look);
            var m = new Material(MonsterLibrary.Load<Shader>(HeroShader)) { name = type.Id + "_hero", enableInstancing = false };
            m.SetColor("_Tint", palette.HeroBody.linear);
            m.SetColor("_BladeTint", palette.HeroBlade.linear);
            m.SetFloat("_Glass", g.Glass);
            m.SetFloat("_Rim", g.Rim);
            m.SetFloat("_RimPower", g.RimPower);
            m.SetFloat("_Edge", g.Edge);
            m.SetFloat("_EdgePx", g.EdgePx);
            m.SetFloat("_Beat", g.Beat);
            m.SetFloat("_CoreGlow", g.CoreGlow);
            m.SetFloat("_CoreSize", g.CoreSize);
            m.SetFloat("_Glow", g.Glow);
            return m;
        }

        /// <summary>
        /// A type's hue as its bodies and shards draw it: a monster's palette colour levelled (<see cref="Level"/>); the
        /// hero's white as it is, the brightest thing on screen (design.md, Shatter: white shards from the hero).
        /// </summary>
        public static Color Hue(MonsterType type, Palette palette) =>
            type.Id == MonsterLibrary.HeroId ? palette.HeroBody.linear : Level(palette.ForType(type.Id).linear);

        static Material Make(string path, MonsterType type, Palette palette, string suffix)
        {
            var m = new Material(MonsterLibrary.Load<Shader>(path)) { name = type.Id + suffix, enableInstancing = false };
            m.SetColor("_Tint", Hue(type, palette));
            m.SetColor("_Core", palette.VoidBody.linear);
            m.SetColor("_Flash", Color.white);
            return m;
        }
    }
}
