using System;
using System.IO;
using Demo3D.EditorTools.Arena;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Arena;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Tests
{
    /// <summary>`--monster-style` (crowd.md, Monster styles): the flag, the post look, the beat globals and the painted materials.</summary>
    public class MonsterStyleTests
    {
        [Test]
        public void TheFlagNamesAStyleAndDefaultsToBaseline()
        {
            Assert.That(MonsterStyle.FromArgs(new[] { "player" }).Name, Is.EqualTo("baseline"));
            Assert.That(MonsterStyle.FromArgs(new[] { "player", "--monster-style", "painted", "--fixed-time" }).Name, Is.EqualTo("painted"));
            foreach (var name in MonsterStyle.Names)
                Assert.That(MonsterStyle.FromArgs(new[] { "--monster-style", name }).Name, Is.EqualTo(name));
            Assert.That(MonsterStyle.Names, Has.Length.EqualTo(9));
            Assert.That(MonsterStyle.FromArgs(new[] { "--monster-style", "baseline" }).IsBaseline);
        }

        [Test]
        public void UnknownOrMissingStylesFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style", "neon" }));
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style", "Painted" }));
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style" }));
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style", "--fixed-time" }));
        }

        [Test]
        public void StyleTuneOverridesTheGainAndPostLookForARun()
        {
            var tuned = MonsterStyle.FromArgs(new[] { "--style-tune", "gain=0.5,exposure=0.8,neutral=1", "--monster-style", "fire" });
            var plain = MonsterStyle.Parse("fire");
            Assert.That(tuned.Gain("demon"), Is.EqualTo(0.5f));
            Assert.That(tuned.Post.Value.exposure, Is.EqualTo(0.8f));
            Assert.That(tuned.Post.Value.neutral, Is.True);
            Assert.That(tuned.Post.Value.bloom, Is.EqualTo(plain.Post.Value.bloom), "untuned values stay");
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style", "fire", "--style-tune", "glow=2" }));
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--monster-style", "fire", "--style-tune", "gain=x" }));
            Assert.Throws<ArgumentException>(() => MonsterStyle.FromArgs(new[] { "--style-tune", "gain=0.5" }), "baseline has nothing to tune");
        }

        [Test]
        public void TheStylesPostLookIsPostFeelsBase()
        {
            Assert.That(MonsterStyle.Baseline().Post, Is.Null, "baseline keeps the night's post");
            var look = MonsterStyle.Parse("painted").Post.Value;
            var map = SongMap.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));
            var lighting = ArenaLighting.Parse(File.ReadAllText(ArenaBaker.LayoutPath));
            var go = new GameObject("post");
            try
            {
                var volume = go.AddComponent<Volume>();
                volume.sharedProfile = NightLook.CreateProfile(lighting);
                look.ApplyTo(volume.sharedProfile);
                var feel = new PostFeel(new Timeline { Map = map, Actions = new ActionEvent[0] }, lighting.PostFeel);
                double t = map.Beats[200] + 1e-4;
                feel.Apply(volume, t);
                volume.sharedProfile.TryGet(out ColorAdjustments color);
                volume.sharedProfile.TryGet(out Tonemapping tonemap);
                Assert.That(color.postExposure.value, Is.EqualTo(look.exposure + feel.At(t).Exposure).Within(1e-4f));
                Assert.That(color.contrast.value, Is.EqualTo(look.contrast + feel.At(t).Contrast).Within(1e-4f));
                Assert.That(tonemap.mode.value, Is.EqualTo(TonemappingMode.ACES));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheBeatGlobalsPulseOnEveryBeatAndDownbeat()
        {
            var map = SongMap.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));
            var beat = new StyleBeat(map);
            int b = map.DownbeatIndexOfBar(20);
            var on = beat.At(map.Beats[b] + 1e-4);
            var later = beat.At(0.5 * (map.Beats[b + 1] + map.Beats[b + 2]));
            Assert.That(on.x, Is.EqualTo(b).Within(0.01f), "the beat count");
            Assert.That(later.x, Is.EqualTo(b + 1.5f).Within(0.01f));
            Assert.That(on.y, Is.GreaterThan(0.99f), "the pulse peaks on the beat");
            Assert.That(on.z, Is.GreaterThan(0.99f), "and the bar pulse on the downbeat");
            Assert.That(later.y, Is.LessThan(on.y));
            Assert.That(later.z, Is.LessThan(on.z));
            Assert.That(beat.At(map.Beats[0] - 1).y, Is.EqualTo(0f), "no pulse before the first beat");
        }

        [Test]
        public void EveryStyleShaderHasTheCrowdsSkinnedPasses()
        {
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { CrowdBaker.StylesDir });
            Assert.That(guids, Is.Not.Empty);
            foreach (var guid in guids)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, shader.name);
                var m = new Material(shader);
                try
                {
                    // A missing pass would silently drop the style's shadows, depth (SSAO) or motion vectors (TAA).
                    // Shells cast no shadow (the body does); an opaque shell still needs depth and motion
                    // vectors, while an additive one (the flames) has only its colour pass.
                    string[] passes;
                    if (!shader.name.StartsWith("Demo3D/Styles/Shells/", StringComparison.Ordinal))
                        passes = shader.name == "Demo3D/Styles/Hologram" // a projection of light casts no shadow
                            ? new[] { "ForwardLit", "DepthOnly", "DepthNormals", "MotionVectors" }
                            : new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals", "MotionVectors" };
                    else if (m.renderQueue < (int)RenderQueue.Transparent)
                        passes = new[] { "DepthOnly", "DepthNormals", "MotionVectors" };
                    else passes = new string[0];
                    foreach (var pass in passes)
                        Assert.That(m.FindPass(pass), Is.GreaterThanOrEqualTo(0), $"{shader.name} has no {pass} pass");
                    Assert.That(m.passCount, Is.GreaterThan(0), shader.name);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(m);
                }
            }
        }

        [Test]
        public void PaintedGivesEveryTypeItsOwnMaterialsAndBaselineTheBakedOnes()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            using var painted = MonsterStyle.Parse("painted");
            using var baseline = MonsterStyle.Baseline();
            foreach (var t in lib.Types)
            {
                Assert.That(baseline.Materials(lib, t), Is.SameAs(t.Materials));
                var mats = painted.Materials(lib, t);
                Assert.That(mats, Has.Length.EqualTo(t.Materials.Length));
                foreach (var m in mats)
                {
                    Assert.That(m.shader.name, Is.EqualTo("Demo3D/Styles/Painted"));
                    Assert.That(m.GetTexture("_Ramp"), Is.Not.Null, t.Id);
                    Assert.That(m.GetVector("_LumRange").y, Is.GreaterThan(m.GetVector("_LumRange").x), t.Id);
                }
                Assert.That(t.ValueRange.y - t.ValueRange.x, Is.GreaterThanOrEqualTo(0.05f - 1e-5f), t.Id);
            }
        }

        [Test]
        public void FireAndPaintedDrawAShellAndChromeAndVoidDoNot()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            var shells = new[] { ("painted", "Demo3D/Styles/Shells/Outline"), ("fire", "Demo3D/Styles/Shells/FireShell"), ("chrome", null), ("void", null) };
            var bodies = new[] { "Demo3D/Styles/Painted", "Demo3D/Styles/FireBody", "Demo3D/Styles/Chrome", "Demo3D/Styles/Void" };
            for (int i = 0; i < shells.Length; i++)
            {
                using var style = MonsterStyle.Parse(shells[i].Item1);
                Assert.That(style.Post, Is.Not.Null, style.Name);
                foreach (var t in lib.Types)
                {
                    foreach (var m in style.Materials(lib, t)) Assert.That(m.shader.name, Is.EqualTo(bodies[i]), t.Id);
                    var shell = style.Shell(lib, t);
                    if (shells[i].Item2 == null) Assert.That(shell, Is.Null, $"{style.Name} {t.Id}");
                    else
                    {
                        Assert.That(shell.shader.name, Is.EqualTo(shells[i].Item2), t.Id);
                        Assert.That(shell.GetFloat("_HeightM"), Is.EqualTo(t.HeightM), t.Id);
                    }
                }
            }
            using var fire = MonsterStyle.Parse("fire");
            foreach (var t in lib.Types)
                Assert.That(fire.Shell(lib, t).GetFloat("_Intensity"), Is.EqualTo(t.Kind == "boss" ? 0.45f : 0.7f).Within(1e-5f), $"{t.Id}: the dragon burns lower");
            using var plain = MonsterStyle.Parse("painted");
            plain.Outlines = false;
            Assert.That(plain.Shell(lib, lib.Types[0]), Is.Null, "the ink line can be turned off");
            Assert.That(MonsterStyle.Parse("baseline").Shell(lib, lib.Types[0]), Is.Null);
        }

        [Test]
        public void CrystalSpectrumAndHologramDrawTheStyleMeshInTheirOwnQueues()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            Assert.That(MonsterStyle.Baseline().UsesStyleMesh("slime") || MonsterStyle.Parse("painted").UsesStyleMesh("slime"), Is.False,
                "the default path keeps the welded mesh");
            Assert.That(MonsterStyle.Baseline().NeedsOpaqueTexture, Is.False);
            var expected = new[]
            {
                ("crystal", "_Tint", (int)RenderQueue.Transparent - 10),
                ("spectrum", "_Hue", (int)RenderQueue.Geometry),
                ("hologram", "_Color", (int)RenderQueue.Transparent),
            };
            foreach (var (name, property, queue) in expected)
            {
                using var style = MonsterStyle.Parse(name);
                Assert.That(style.UsesStyleMesh(lib.Types[0].Id), name);
                Assert.That(style.NeedsOpaqueTexture, Is.EqualTo(name == "crystal"), name);
                Assert.That(style.Post, Is.Not.Null, name);
                foreach (var t in lib.Types)
                {
                    foreach (var m in style.Materials(lib, t))
                    {
                        Assert.That(m.renderQueue, Is.EqualTo(queue), $"{name} {t.Id}");
                        Assert.That(m.HasProperty(property), $"{name} {t.Id}");
                        Assert.That(m.GetFloat("_HeightM"), Is.EqualTo(t.HeightM), $"{name} {t.Id}");
                    }
                }
            }
            Assert.That(MonsterStyle.Parse("spectrum").Post.Value.chroma, Is.GreaterThan(0f));
            Assert.That(MonsterStyle.Parse("hologram").Post.Value.chroma, Is.GreaterThan(MonsterStyle.Parse("spectrum").Post.Value.chroma));
        }
    
        [Test]
        public void TheMixGivesEachTypeTheShowcasesStyle()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            using var mix = MonsterStyle.Parse("mix");
            var expected = new System.Collections.Generic.Dictionary<string, string>
            {
                ["skeleton"] = "Demo3D/Styles/Painted", ["goblin"] = "Demo3D/Styles/Painted", ["mushroom"] = "Demo3D/Styles/Painted",
                ["slime"] = "Demo3D/Styles/Crystal", ["bat"] = "Demo3D/Styles/Spectrum", ["demon"] = "Demo3D/Styles/FireBody",
                ["bigork"] = "Demo3D/Styles/Chrome", ["troll"] = "Demo3D/Styles/Void", ["dragon"] = "Demo3D/Styles/FireBody",
            };
            int seen = 0;
            foreach (var t in lib.Types)
            {
                if (!expected.TryGetValue(t.Id, out var shader)) continue;
                seen++;
                foreach (var m in mix.Materials(lib, t)) Assert.That(m.shader.name, Is.EqualTo(shader), t.Id);
                Assert.That(mix.UsesStyleMesh(t.Id), Is.EqualTo(t.Id == "slime" || t.Id == "bat"), t.Id);
                var shell = mix.Shell(lib, t);
                if (shader == "Demo3D/Styles/Spectrum" || shader == "Demo3D/Styles/Crystal" || shader == "Demo3D/Styles/Chrome" || shader == "Demo3D/Styles/Void")
                    Assert.That(shell, Is.Null, t.Id);
                else
                    Assert.That(shell.shader.name, Is.EqualTo(shader == "Demo3D/Styles/FireBody" ? "Demo3D/Styles/Shells/FireShell" : "Demo3D/Styles/Shells/Outline"), t.Id);
            }
            Assert.That(seen, Is.EqualTo(expected.Count), "the roster has every showcase type");
            Assert.That(mix.NeedsOpaqueTexture, "the slimes refract");
            Assert.That(mix.Post, Is.Not.Null);
        }
    }
}
