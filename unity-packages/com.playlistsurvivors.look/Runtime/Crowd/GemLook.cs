using System;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// The refractive-gem monster look (neon-look.md, The Axes &gt; Monsters; the prism preset's, and the stand-in for
    /// an option without a look): clear gem bodies showing the void and the grid behind them through their facets,
    /// split into their type's palette hue (<see cref="MonsterSlots"/>) and its two neighbours, mostly dark, with
    /// bright reflected bands that move between facets as the view turns, a rim and fine lines in the slot's hue and a
    /// core of trapped light that flares on the beat; shards keep the gem, their fresh faces lit. Shaders
    /// Resources/Look/Shaders/MonsterGem and MonsterGemShard; colours from the applied look's palette (else the
    /// fallback preset's), read when a type's material is made. The gem reads the camera's opaque texture, so a
    /// player drawing it turns that on (<see cref="MonsterLooks.Refracts"/>).
    /// </summary>
    public sealed class GemLook : IMonsterLook
    {
        public const string Option = "refractive-gem";
        public const string BodyShader = "Look/Shaders/MonsterGem";
        public const string ShardShader = "Look/Shaders/MonsterGemShard";

        /// <summary>How far each dispersion fringe turns from the slot's hue, in degrees: its family's neighbours, not a rainbow.</summary>
        public const float FringeDeg = 28f;

        public string Name => Option;

        public Material Body(CrowdLibrary lib, CrowdType type) => Make(BodyShader, type, "_gem");

        public Material Shards(CrowdLibrary lib, CrowdType type) => Make(ShardShader, type, "_gem_shards");

        static Material Make(string path, CrowdType type, string suffix)
        {
            var shader = Resources.Load<Shader>(path) ?? throw new InvalidOperationException($"GemLook: no shader Resources/{path}");
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            var hue = MonsterSlots.Colour(palette, type.Id);
            var m = new Material(shader) { name = type.Id + suffix, enableInstancing = false };
            m.SetColor("_Tint", EdgeLook.Level(hue.linear));
            m.SetColor("_FringeA", EdgeLook.Level(Turn(hue, -FringeDeg).linear));
            m.SetColor("_FringeB", EdgeLook.Level(Turn(hue, FringeDeg).linear));
            m.SetColor("_Core", palette.VoidFloor.linear);
            m.SetColor("_Flash", palette.ShatterCore.linear);
            return m;
        }

        /// <summary><paramref name="gamma"/> with its hue turned by <paramref name="degrees"/>, its saturation and value kept.</summary>
        public static Color Turn(Color gamma, float degrees)
        {
            Color.RGBToHSV(gamma, out float h, out float s, out float v);
            var c = Color.HSVToRGB(Mathf.Repeat(h + degrees / 360f, 1f), s, v);
            c.a = 1;
            return c;
        }
    }
}
