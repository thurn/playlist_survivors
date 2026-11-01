using NUnit.Framework;
using PS.Look.Effects;
using UnityEditor;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The effect runtime's palette roles: colours the selection names by role are drawn in the applied palette's.</summary>
    public class EffectPaletteTests
    {
        const string Shaders = "Packages/com.playlistsurvivors.look/Runtime/Effects/Shaders/";

        static Palette Palette(string name, string trail, string spark) => PS.Look.Palette.Parse($@"{{
          ""name"": ""{name}"",
          ""void"": {{ ""floor"": ""#05060a"", ""fog"": ""#0a0c18"" }},
          ""grid"": {{ ""line"": ""#1d3cff"", ""pulse"": ""#5f7dff"" }},
          ""hero"": {{ ""body"": ""#f4f1ff"", ""blade"": ""#ffffff"", ""trail"": ""{trail}"" }},
          ""enemies"": [""#ff2e88"", ""#28f0ff""],
          ""boss"": {{ ""body"": ""#ff3355"", ""telegraph"": ""#ff1030"" }},
          ""shatter"": {{ ""gem"": ""#ffe9a8"", ""core"": ""#ffffff"" }},
          ""spark"": ""{spark}""
        }}");

        // Two one-emitter effects: the first's heat ramp and light named by role, the second's literal.
        static EffectLibrary Library()
        {
            var lib = ScriptableObject.CreateInstance<EffectLibrary>();
            lib.Emitters = new Vector4[2 * EmitterSlots.Stride];
            for (int e = 0; e < 2; e++)
            {
                lib.Emitters[e * EmitterSlots.Stride + EmitterSlots.Heat0] = new Vector4(0.1f, 0.2f, 0.3f, 1f);
                lib.Emitters[e * EmitterSlots.Stride + EmitterSlots.Heat1] = new Vector4(0.4f, 0.5f, 0.6f, 0.12f);
            }
            lib.Emission = new EmitterEmission[2];
            var light = new LightCue { Color = new Color(0.2f, 0.3f, 0.4f), Range = 4, Intensity = 1, Duration = 0.3f, Curve = new float[16] };
            lib.Effects = new[]
            {
                new EffectEntry { Name = "role", FirstEmitter = 0, EmitterCount = 1, Light = light, LightRole = "spark" },
                new EffectEntry { Name = "literal", FirstEmitter = 1, EmitterCount = 1, Light = light },
            };
            lib.Roles = new[]
            {
                new RoleColor { Emitter = 0, Slot = EmitterSlots.Heat0, Role = "hero.trail" },
                new RoleColor { Emitter = 0, Slot = EmitterSlots.Heat1, Role = "enemies.1" },
            };
            lib.Simulate = AssetDatabase.LoadAssetAtPath<ComputeShader>(Shaders + "EffectSim.compute");
            lib.Sort = AssetDatabase.LoadAssetAtPath<ComputeShader>(Shaders + "EffectSort.compute");
            lib.Draw = Shader.Find("PS/Look/EffectParticles");
            return lib;
        }

        static void AssertRgb(Vector4 v, Color c, float w)
        {
            Assert.That(v.x, Is.EqualTo(c.r).Within(1e-5f));
            Assert.That(v.y, Is.EqualTo(c.g).Within(1e-5f));
            Assert.That(v.z, Is.EqualTo(c.b).Within(1e-5f));
            Assert.That(v.w, Is.EqualTo(w), "the slot's mix or gain is the selection's");
        }

        [Test]
        public void RoleColoursFollowTheAppliedPaletteAndLiteralsStay()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("no compute shaders on this device");
            var lib = Library();
            var fx = new EffectSystem(lib, 1 << 12, 4);
            try
            {
                foreach (var palette in new[] { Palette("cyan", "#8ff3ff", "#ffffff"), Palette("amber", "#ffb21e", "#ffe0a0") })
                {
                    fx.ApplyPalette(palette);
                    AssertRgb(fx.EmitterSlot(0, EmitterSlots.Heat0), palette.HeroTrail, 1f);
                    AssertRgb(fx.EmitterSlot(0, EmitterSlots.Heat1), palette.Enemies[1], 0.12f);
                    Assert.That(fx.LightColor(0), Is.EqualTo(palette.Spark.linear));
                    Assert.That(fx.EmitterSlot(1, EmitterSlots.Heat0), Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 1f)));
                    Assert.That(fx.LightColor(1), Is.EqualTo(new Color(0.2f, 0.3f, 0.4f)));
                }
            }
            finally
            {
                fx.Dispose();
                Object.DestroyImmediate(lib);
            }
        }

        [Test]
        public void AnEnemySlotIsARole()
        {
            var palette = Palette("p", "#8ff3ff", "#ffffff");
            Assert.That(palette.Role("enemies.0"), Is.EqualTo(palette.Enemies[0]));
            Assert.That(palette.TryRole("enemies.2", out _), Is.False, "past the palette's slots");
            Assert.That(palette.TryRole("enemies.x", out _), Is.False);
        }
    }
}
