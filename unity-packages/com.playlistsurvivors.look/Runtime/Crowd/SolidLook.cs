using System;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// The solid-emissive monster look (neon-look.md, The Axes &gt; Monsters; the solid preset's): saturated self-lit
    /// bodies in their type's palette hue (<see cref="MonsterSlots"/>), shaded so they show form (a light from above
    /// and a darker falloff toward the silhouette) and drawn with a thin dark outline in screen space (an inverted
    /// hull), so two touching monsters of one type read as two. The beat lights a small hot spot facing the camera,
    /// a passing impulse ring lifts and lights them, and shards keep the surface and the outline. Shaders
    /// Resources/Look/Shaders/MonsterSolid and MonsterSolidShard; colours from the applied look's palette (else the
    /// fallback preset's), read when a type's material is made, which both crowds do once at start.
    /// </summary>
    public sealed class SolidLook : IMonsterLook
    {
        public const string Option = "solid-emissive";
        public const string BodyShader = "Look/Shaders/MonsterSolid";
        public const string ShardShader = "Look/Shaders/MonsterSolidShard";

        public string Name => Option;

        public Material Body(CrowdLibrary lib, CrowdType type) => Make(BodyShader, type, "_solid");

        public Material Shards(CrowdLibrary lib, CrowdType type) => Make(ShardShader, type, "_solid_shards");

        static Material Make(string path, CrowdType type, string suffix)
        {
            var shader = Resources.Load<Shader>(path) ?? throw new InvalidOperationException($"SolidLook: no shader Resources/{path}");
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            var m = new Material(shader) { name = type.Id + suffix, enableInstancing = false };
            m.SetColor("_Tint", MonsterSlots.Colour(palette, type.Id).linear);
            m.SetColor("_Outline", palette.VoidFloor.linear);
            m.SetColor("_Flash", palette.ShatterCore.linear);
            return m;
        }
    }
}
