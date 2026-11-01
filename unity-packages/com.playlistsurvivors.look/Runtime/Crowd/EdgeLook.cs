using System;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// The dark-core-edges monster look (neon-look.md, The Axes &gt; Monsters; the edge preset's): near-black glass
    /// bodies whose only light is their type's palette hue (<see cref="MonsterSlots"/>) in a fresnel rim, in lines on
    /// the model's feature edges (the crowd baker's, StyleMesh.FeatureEdges) and in a small inner core that flares on
    /// the beat; shards keep the edges and light where they broke. Shaders Resources/Look/Shaders/MonsterEdge and
    /// MonsterEdgeShard; colours from the applied look's palette (else the fallback preset's), read when a type's
    /// material is made, which both crowds do once at start.
    /// </summary>
    public sealed class EdgeLook : IMonsterLook
    {
        public const string Option = "dark-core-edges";
        public const string BodyShader = "Look/Shaders/MonsterEdge";
        public const string ShardShader = "Look/Shaders/MonsterEdgeShard";

        public string Name => Option;

        public Material Body(CrowdLibrary lib, CrowdType type) => Make(BodyShader, type, "_edge");

        public Material Shards(CrowdLibrary lib, CrowdType type) => Make(ShardShader, type, "_edge_shards");

        static Material Make(string path, CrowdType type, string suffix)
        {
            var shader = Resources.Load<Shader>(path) ?? throw new InvalidOperationException($"EdgeLook: no shader Resources/{path}");
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            var m = new Material(shader) { name = type.Id + suffix, enableInstancing = false };
            m.SetColor("_Tint", Level(MonsterSlots.Colour(palette, type.Id).linear));
            m.SetColor("_Core", palette.VoidFloor.linear);
            m.SetColor("_Flash", palette.ShatterCore.linear);
            return m;
        }

        /// <summary>The luminance every slot's hue is drawn at (linear), so the post's bloom treats the hues alike.</summary>
        public const float HueLuminance = 0.4f;

        /// <summary>
        /// A slot's hue scaled to <see cref="HueLuminance"/>: the palettes' enemy hues span 0.25 (cobalt, magenta) to
        /// 0.5-0.6 (amber, cyan) in linear luminance, and drawn as they are, an amber elite bloomed to yellow-white
        /// while a cobalt demon stayed dim.
        /// </summary>
        public static Color Level(Color linear)
        {
            float l = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            var c = linear * (HueLuminance / Mathf.Max(l, 0.05f));
            c.a = 1;
            return c;
        }
    }
}
