using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>Palettes, presets and the look flags: what a player resolves from its command line.</summary>
    public class LookTests
    {
        const string Electric = @"{
          ""name"": ""electric"",
          ""void"": { ""floor"": ""#05060a"", ""fog"": ""#0a0c18"" },
          ""grid"": { ""line"": ""#1d3cff"", ""pulse"": ""#5f7dff"" },
          ""hero"": { ""body"": ""#f4f1ff"", ""blade"": ""#ffffff"", ""trail"": ""#9ff6ff"" },
          ""enemies"": [""#ff2e88"", ""#28f0ff""],
          ""boss"": { ""body"": ""#ff3355"", ""telegraph"": ""#ff1030"" },
          ""shatter"": { ""gem"": ""#ffe9a8"", ""core"": ""#ffffff"" },
          ""spark"": ""#ffffff"",
          ""zones"": [ { ""name"": ""cobalt"", ""tint"": ""#1d3cff"" } ]
        }";

        static string Preset(string name, string monsters = "wireframe", string extra = "") => $@"{{
          ""name"": ""{name}"", ""palette"": ""electric"", ""monsters"": ""{monsters}"", ""structures"": ""wireframe"",
          ""world"": ""pure-void"", ""post"": ""scanlines"", ""vfx"": ""light-streaks"", ""hero"": ""{name}"",
          ""motion"": {{ ""springs"": true, ""swarm"": false, ""trails"": true, ""beatRipple"": false }}{extra}
        }}";

        static LookLibrary Library(params (string Name, string Json)[] presets)
        {
            var files = new Dictionary<string, string>();
            foreach (var (name, json) in presets) files[name] = json;
            var dusk = Electric.Replace(@"""electric""", @"""dusk""");
            return new LookLibrary(files, new Dictionary<string, string> { ["electric"] = Electric, ["dusk"] = dusk });
        }

        static readonly LookLibrary Two = Library(("edge", Preset("edge", "dark-core-edges")), ("hologram", Preset("hologram")));

        static ResolvedLook Resolve(string flags, LookLibrary library = null)
        {
            library ??= Two;
            return ResolvedLook.Resolve(LookFlags.Parse(("player " + flags).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), library), library);
        }

        [Test]
        public void EveryPackagePresetResolvesOnThePackagesPalettes()
        {
            var library = LookLibrary.Shared;
            Assert.That(library.Presets, Is.EquivalentTo(new[] { "classic", "edge", "hologram", "prism", "solid" }));
            foreach (var name in library.Palettes) Assert.That(Palette.Parse(library.PaletteJson(name)).Name, Is.EqualTo(name));
            foreach (var name in library.Presets)
            {
                var look = ResolvedLook.Resolve(new LookFlags { Look = name });
                Assert.That(look.Name, Is.EqualTo(name));
                Assert.That(look.Palette.Enemies, Is.Not.Empty);
            }
        }

        [Test]
        public void APresetGivesEveryAxisAnOptionAndItsMotionLayers()
        {
            var look = Resolve("--look hologram");
            Assert.That(look.Named, Is.True);
            Assert.That(look.Requested(LookAxes.Monsters), Is.EqualTo("wireframe"));
            Assert.That(look.Requested(LookAxes.World), Is.EqualTo("pure-void"));
            Assert.That(look.MotionRequested("springs"), Is.True);
            Assert.That(look.MotionRequested("swarm"), Is.False);
            Assert.That(look.Describe(), Is.EqualTo(
                "look hologram, palette electric; monsters wireframe, structures wireframe, world pure-void, post scanlines, " +
                "vfx light-streaks, hero hologram; motion springs, trails"));
        }

        [Test]
        public void ALaunchWithoutLookDrawsTheFallbackAndSaysSo()
        {
            var look = Resolve("--fixed-time");
            Assert.That(look.Name, Is.EqualTo(ResolvedLook.Fallback));
            Assert.That(look.Named, Is.False);
            Assert.That(look.Describe(), Does.StartWith("look edge (no --look: the fallback), palette electric;"));
        }

        [Test]
        public void AxisFlagsOverrideThePresetAndAreMarked()
        {
            var look = Resolve("--look hologram --monster-look solid-emissive --hero-look edge --palette dusk --beat-ripple");
            Assert.That(look.Requested(LookAxes.Monsters), Is.EqualTo("solid-emissive"));
            Assert.That(look.Requested(LookAxes.Structures), Is.EqualTo("wireframe"), "an axis without a flag keeps the preset's");
            Assert.That(look.Palette.Name, Is.EqualTo("dusk"));
            Assert.That(look.MotionRequested("beatRipple"), Is.True);
            Assert.That(look.Describe(), Does.Contain("palette dusk (--palette); monsters solid-emissive (--monster-look)"));
            Assert.That(look.Describe(), Does.Contain("hero edge (--hero-look); motion springs, trails, beatRipple (--beat-ripple)"));
        }

        [Test]
        public void WhatIsntBuiltDrawsTodaysLook()
        {
            var look = Resolve("--look edge --monster-look not-a-look");
            foreach (var axis in LookAxes.All)
                if (!axis.IsBuilt(look.Requested(axis))) Assert.That(look.Option(axis), Is.Null, axis.Key);
            Assert.That(look.NotBuilt, Does.Contain("monsters not-a-look"));
            foreach (var layer in LookAxes.MotionLayers)
                if (!LookAxes.Motion.IsBuilt(layer)) Assert.That(look.MotionOn(layer), Is.False, layer);
        }

        [Test]
        public void ClassicDrawsTodaysLookOnEveryAxisWithoutAWarning()
        {
            var look = ResolvedLook.Resolve(new LookFlags { Look = "classic" });
            foreach (var axis in LookAxes.All)
            {
                Assert.That(look.Requested(axis), Is.EqualTo(LookAxis.Classic), axis.Key);
                Assert.That(look.Option(axis), Is.Null, axis.Key);
            }
            foreach (var layer in LookAxes.MotionLayers) Assert.That(look.MotionOn(layer), Is.False, layer);
            Assert.That(look.NotBuilt, Is.Empty, "classic is asked for, not missing");

            var mixed = Resolve("--look edge --post classic");
            Assert.That((mixed.Option(LookAxes.Post), mixed.Option(LookAxes.Monsters)), Is.EqualTo(((string)null, "dark-core-edges")));
            Assert.That(mixed.NotBuilt, Has.None.StartsWith("post"));
        }

        [Test]
        public void PaletteIsTheLooksOnlyWhenItNamesALookPalette()
        {
            var flags = LookFlags.Parse(new[] { "player", "--palette", "dusk" }, Two);
            Assert.That((flags.LookPalette, flags.ArenaPalette, flags.Any), Is.EqualTo(("dusk", (string)null, true)));
            flags = LookFlags.Parse(new[] { "player", "--palette", "lava" }, Two);
            Assert.That((flags.LookPalette, flags.ArenaPalette, flags.Any), Is.EqualTo(((string)null, "lava", false)));
            Assert.That(Resolve("--palette lava").Palette.Name, Is.EqualTo("electric"), "the preset's palette");
        }

        [Test]
        public void EveryLookFlagIsListedForPlayersThatClaimTheirFlags()
        {
            Assert.That(LookFlags.ValuedFlags, Is.EquivalentTo(new[]
                { "--look", "--palette", "--motion", "--hero-tune", "--monster-look", "--structure-look", "--world", "--post", "--vfx", "--hero-look" }));
            Assert.That(LookFlags.Switches, Is.EquivalentTo(new[] { "--beat-ripple" }));
        }

        [Test]
        public void BadPresetsFailWithWhatIsWrong()
        {
            Assert.That(Assert.Throws<ArgumentException>(() => Resolve("--look neon")).Message, Does.Contain("have: edge, hologram"));
            Assert.That(Assert.Throws<FormatException>(() => Resolve("--look x", Library(("x", Preset("x", extra: @", ""monster"": ""y""")))))
                .Message, Does.Contain("monster is not an axis"));
            Assert.That(Assert.Throws<FormatException>(() => Resolve("--look x", Library(("x", Preset("x").Replace(@"""vfx"": ""light-streaks"",", "")))))
                .Message, Does.Contain("no vfx"));
            Assert.That(Assert.Throws<FormatException>(() => Resolve("--look x", Library(("x", Preset("x").Replace("springs", "spring"))))).Message,
                Does.Contain("motion spring"));
            Assert.That(Assert.Throws<FormatException>(() => Resolve("--look x", Library(("x", Preset("y"))))).Message, Does.Contain("names it y"));
        }

        [Test]
        public void APaletteHasEveryRoleByName()
        {
            var p = Palette.Parse(Electric);
            Assert.That((Color32)p.HeroBody, Is.EqualTo(new Color32(0xf4, 0xf1, 0xff, 0xff)));
            Assert.That(p.Role("spark"), Is.EqualTo(Color.white));
            Assert.That(p.Enemies.Length, Is.EqualTo(2));
            Assert.That(p.Zones[0].Name, Is.EqualTo("cobalt"));
            Assert.That(Palette.GlobalName("hero.body"), Is.EqualTo("_LookHeroBody"));
            Assert.That(Palette.GlobalName("spark"), Is.EqualTo("_LookSpark"));
            Assert.That(Assert.Throws<FormatException>(() => Palette.Parse(Electric.Replace(@"""spark"": ""#ffffff"",", ""))).Message, Does.Contain("no spark"));
            Assert.That(Assert.Throws<FormatException>(() => Palette.Parse(Electric.Replace("#05060a", "05060a"))).Message, Does.Contain("void.floor"));
        }

        [Test]
        public void ShadersGetTheRolesInLinear()
        {
            var palette = Palette.Parse(Electric);
            palette.SetShaderGlobals();
            var floor = Shader.GetGlobalVector("_LookVoidFloor");
            var linear = palette.VoidFloor.linear;
            Assert.That(floor.x, Is.EqualTo(linear.r).Within(1e-6f));
            Assert.That(floor.z, Is.EqualTo(linear.b).Within(1e-6f));
            Assert.That(floor.z, Is.LessThan(palette.VoidFloor.b), "linear, not the file's sRGB");
        }

        [Test]
        public void ApplyingALookGivesShadersItsPalette()
        {
            var look = Resolve("--look hologram");
            look.Apply();
            Assert.That(ResolvedLook.Active, Is.SameAs(look));
            Assert.That(Shader.GetGlobalInt("_LookEnemyCount"), Is.EqualTo(2));
            var enemies = Shader.GetGlobalVectorArray("_LookEnemies");
            Assert.That(enemies.Length, Is.EqualTo(Palette.MaxSlots));
            Assert.That((Color)enemies[1], Is.EqualTo(look.Palette.Enemies[1].linear));
            Assert.That(Shader.GetGlobalColor("_LookGridLine"), Is.Not.EqualTo(Color.clear));
        }
    }
}
