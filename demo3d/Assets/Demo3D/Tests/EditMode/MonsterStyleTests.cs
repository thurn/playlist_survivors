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
                    foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals", "MotionVectors" })
                        Assert.That(m.FindPass(pass), Is.GreaterThanOrEqualTo(0), $"{shader.name} has no {pass} pass");
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
            Assert.Throws<NotSupportedException>(() => MonsterStyle.Parse("fire").Materials(lib, lib.Types[0]), "unported styles fail loudly");
        }
    }
}
