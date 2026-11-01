using System;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// The wireframe hologram monster look (neon-look.md, The Axes &gt; Monsters; the hologram preset's): bodies of
    /// light, not glass. Lines in the type's palette hue (<see cref="MonsterSlots"/>, levelled as
    /// <see cref="EdgeLook.Level"/> does) over a faint fill that thickens toward the silhouette, added over the world
    /// with a light veil, so the grid shows through a monster and a clump reads as outlines, front surfaces only.
    /// Option <see cref="Option"/> draws the model's feature edges (the crowd baker's, StyleMesh.FeatureEdges);
    /// <see cref="AllEdgesOption"/> every triangle's too, dimmer. Shards are line fragments. Shaders Resources/Look/Shaders/MonsterHologram and
    /// MonsterHologramShard; colours from the applied look's palette (else the fallback preset's), read when a type's
    /// material is made, which both crowds do once at start.
    /// </summary>
    public sealed class HologramLook : IMonsterLook
    {
        public const string Option = "wireframe";
        public const string AllEdgesOption = "wireframe-all";
        public const string BodyShader = "Look/Shaders/MonsterHologram";
        public const string ShardShader = "Look/Shaders/MonsterHologramShard";

        readonly bool _allEdges;

        public HologramLook(bool allEdges) => _allEdges = allEdges;

        public string Name => _allEdges ? AllEdgesOption : Option;

        public Material Body(CrowdLibrary lib, CrowdType type) => Make(BodyShader, type, "_hologram");

        public Material Shards(CrowdLibrary lib, CrowdType type) => Make(ShardShader, type, "_hologram_shards");

        Material Make(string path, CrowdType type, string suffix)
        {
            var shader = Resources.Load<Shader>(path) ?? throw new InvalidOperationException($"HologramLook: no shader Resources/{path}");
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            var m = new Material(shader) { name = type.Id + suffix, enableInstancing = false };
            m.SetColor("_Tint", EdgeLook.Level(MonsterSlots.Colour(palette, type.Id).linear));
            m.SetColor("_Flash", palette.ShatterCore.linear);
            m.SetFloat("_AllEdges", _allEdges ? 1 : 0);
            return m;
        }
    }
}
