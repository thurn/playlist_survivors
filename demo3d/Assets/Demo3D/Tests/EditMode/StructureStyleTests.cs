using System;
using System.Collections.Generic;
using System.Linq;
using Demo3D.EditorTools.Arena;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Arena;
using Demo3D.Runtime.Crowd;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Tests
{
    /// <summary>`--structure-style` (environment.md, Structure styles; epic ps-suy): the flag, match, the one post look,
    /// the structure shaders, which renderers are structures and the material swap on the baked scenes.</summary>
    public class StructureStyleTests
    {
        static readonly string[] Styles = { "painted", "fire", "crystal", "spectrum", "hologram", "chrome", "void" };

        [Test]
        public void TheFlagTakesEveryMonsterStyleButMixAndMatchAndDefaultsToBaseline()
        {
            var horde = MonsterStyle.Baseline();
            Assert.That(StructureStyle.FromArgs(new[] { "player" }, horde).IsBaseline);
            foreach (var name in MonsterStyle.Names.Where(n => n != "mix"))
                Assert.That(StructureStyle.FromArgs(new[] { "--structure-style", name, "--fixed-time" }, horde).Name, Is.EqualTo(name));
            Assert.That(StructureStyle.Names, Does.Contain("match"));
            Assert.That(StructureStyle.Names, Does.Not.Contain("mix"));
            var e = Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-style", "neon" }, horde));
            Assert.That(e.Message, Does.Contain("painted").And.Contain("match"), "an unknown name lists the known ones");
            Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-style", "mix" }, horde));
            Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-style" }, horde));
            Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-style", "--fixed-time" }, horde));
        }

        [Test]
        public void MatchTakesTheHordesStyleAndTheMixLeavesTheSetAsItIs()
        {
            foreach (var name in Styles)
                Assert.That(StructureStyle.Parse("match", MonsterStyle.Parse(name)).Name, Is.EqualTo(name));
            Assert.That(StructureStyle.Parse("match", MonsterStyle.Parse("mix")).IsBaseline, "mix has no one style for the set");
            Assert.That(StructureStyle.Parse("match", MonsterStyle.Baseline()).IsBaseline);
        }

        [Test]
        public void OnePostLookReachesTheNightVolumeTheHordesFirst()
        {
            var night = new PostLook { neutral = true, exposure = 0.6f, contrast = 12f, saturation = 18f, bloom = 0.5f, bloomThreshold = 1.5f, vignette = 0.25f };
            Assert.That(StructureStyle.PostFor(MonsterStyle.Baseline(), StructureStyle.Baseline(), night, night), Is.Null, "the night's own");
            var set = StructureStyle.PostFor(MonsterStyle.Baseline(), StructureStyle.Parse("hologram"), night, night).Value;
            Assert.That(set.contrast, Is.EqualTo(night.contrast), "the baseline horde keeps the night's grade");
            Assert.That(set.exposure, Is.EqualTo(night.exposure));
            Assert.That(set.saturation, Is.EqualTo(night.saturation));
            Assert.That(set.chroma, Is.GreaterThan(0f).And.LessThan(MonsterStyle.Parse("hologram").Post.Value.chroma), "a lighter fringe");
            Assert.That(set.bloom, Is.GreaterThan(night.bloom), "hologram's brighter bloom");
            var fire = MonsterStyle.Parse("fire");
            Assert.That(StructureStyle.PostFor(fire, StructureStyle.Parse("hologram"), night, night).Value.contrast, Is.EqualTo(fire.Post.Value.contrast), "the horde's wins");
            Assert.That(StructureStyle.Parse("crystal").NeedsOpaqueTexture);
            Assert.That(Styles.Where(n => n != "crystal").Any(n => StructureStyle.Parse(n).NeedsOpaqueTexture), Is.False);
        }

        [Test]
        public void TheNightsLookReadsBackFromItsVolume()
        {
            var lighting = ArenaLighting.Parse(System.IO.File.ReadAllText(ArenaBaker.LayoutPath));
            var profile = NightLook.CreateProfile(lighting);
            try
            {
                var night = PostLook.From(profile);
                var copy = UnityEngine.Object.Instantiate(profile);
                night.ApplyTo(copy);
                copy.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments a);
                profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments b);
                Assert.That(a.contrast.value, Is.EqualTo(b.contrast.value));
                Assert.That(a.postExposure.value, Is.EqualTo(b.postExposure.value));
                Assert.That(night.bloomThreshold, Is.EqualTo(lighting.BloomThreshold));
                UnityEngine.Object.DestroyImmediate(copy);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void StructureTuneOverridesTheStructuresGradeAndPostAlone()
        {
            var horde = MonsterStyle.Parse("fire");
            var tuned = StructureStyle.FromArgs(new[] { "--structure-tune", "gain=0.3,peak=0.05,exposure=0.4", "--structure-style", "fire" }, horde);
            Assert.That(tuned.Gain, Is.EqualTo(0.3f));
            Assert.That(tuned.Peak, Is.EqualTo(0.05f));
            Assert.That(tuned.PostOver(PostLook.Stylized()).Value.exposure, Is.EqualTo(0.4f));
            Assert.That(horde.Gain("demon"), Is.EqualTo(1f), "the horde keeps its values");
            Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-tune", "gain=0.3" }, horde), "baseline has nothing to tune");
            Assert.Throws<ArgumentException>(() => StructureStyle.FromArgs(new[] { "--structure-style", "fire", "--structure-tune", "glow=2" }, horde));
        }

        [Test]
        public void TheSetStaysUnderTheHordeAndTheBloom()
        {
            foreach (var name in Styles)
            {
                var s = StructureStyle.Parse(name);
                Assert.That(s.Peak, Is.LessThan(MonsterStyle.Parse(name).Peak("skeleton")), $"{name}: a structure peaks below the horde");
                Assert.That(s.Peak, Is.LessThan(s.PostOver(PostLook.Stylized()).Value.bloomThreshold), name);
            }
        }

        [Test]
        public void EveryStructureShaderCompilesWithTheKitsPassesAndNoMotionVectors()
        {
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { CrowdBaker.StructuresDir });
            var names = new HashSet<string>();
            foreach (var guid in guids)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                names.Add(shader.name);
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, shader.name);
                var m = new Material(shader);
                try
                {
                    // A still structure has only the camera's motion; an opaque one needs depth and normals for SSAO,
                    // and casts as the kit does at E0. Projected light (hologram, the flames) has only its colour pass.
                    Assert.That(m.FindPass("MotionVectors"), Is.LessThan(0), shader.name);
                    bool light = shader.name == "Demo3D/Structures/Hologram" || shader.name == "Demo3D/Structures/FireFlames";
                    var passes = light ? new string[0]
                        : m.renderQueue > (int)RenderQueue.GeometryLast ? new[] { "ShadowCaster" }
                        : new[] { "ShadowCaster", "DepthOnly", "DepthNormals" };
                    foreach (var pass in passes)
                        Assert.That(m.FindPass(pass), Is.GreaterThanOrEqualTo(0), $"{shader.name} has no {pass} pass");
                    Assert.That(m.HasProperty("_Cull") && m.HasProperty("_Gain") && m.HasProperty("_Peak"), shader.name);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(m);
                }
            }
            foreach (var name in Styles)
                Assert.That(names, Does.Contain(StructureStyle.Parse(name).ShaderName), name);
            Assert.That(names, Does.Contain("Demo3D/Structures/FireFlames"));
        }

        /// <summary>Each environment's authored E2 scene (the final rung's), opened and set up as the player does.</summary>
        static IEnumerable<(string environment, ArenaRuntime arena)> Arenas()
        {
            var index = AssetDatabase.LoadAssetAtPath<ArenaIndex>($"{ArenaBaker.Dir}/Resources/{ArenaIndex.ResourcePath}.asset");
            if (index == null || index.entries.Length == 0) Assert.Ignore("the arena isn't baked: run demo3d unity BakeArena");
            foreach (var environment in new[] { "forest", "crypt" })
            {
                var e = index.Find(environment, ArenaIndex.E2, ArenaOptions.AuthoredDensity);
                if (e == null) Assert.Ignore($"no {environment} E2 scene: run demo3d unity BakeArena");
                var env = ArenaEnvironment.Load(environment);
                var scene = EditorSceneManager.OpenScene($"{env.Dir}/{e.scene}.unity", OpenSceneMode.Single);
                var options = ArenaOptions.Final();
                options.Environment = environment;
                // Dispose restores the kit's texture group (ArenaPipeline.Restore), which would write the project's settings.
                var textures = QualitySettings.GetTextureMipmapLimitSettings(ArenaPipeline.KitTextureGroup);
                var arena = ArenaRuntime.Attach(scene, options, ArenaLighting.Parse(env.Json), null);
                try { yield return (environment, arena); }
                finally
                {
                    arena.Dispose();
                    QualitySettings.SetTextureMipmapLimitSettings(ArenaPipeline.KitTextureGroup, textures);
                }
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void StructuresAreThePlacedPiecesAndTheTreesButNeverSmallProps()
        {
            foreach (var (environment, arena) in Arenas())
            {
                var structures = new HashSet<Renderer>(arena.Structures);
                Assert.That(structures, Is.Not.Empty, environment);
                Assert.That(structures.Count, Is.LessThan(arena.Static.Count), $"{environment}: small props stay the kit");
                foreach (var r in arena.Static)
                {
                    var group = r.transform;
                    while (group.parent != null && group.parent.name != "Static") group = group.parent;
                    bool placed = group.name == "Structures" || group.name == "Landmarks";
                    var size = r.bounds.size;
                    bool large = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= ArenaOptions.SmallPropM;
                    bool opaque = r.sharedMaterials.All(m => m != null && m.renderQueue < (int)RenderQueue.Transparent);
                    Assert.That(structures.Contains(r), Is.EqualTo(opaque && r is MeshRenderer && group.name != "Merged" && (placed || large)),
                        $"{environment}: {r.name} in {group.name}");
                }
                if (environment == "forest")
                    Assert.That(arena.Structures.Any(r => r.sharedMaterials.Any(m => m.shader.name == "Demo3D/LitCanopy")), "the forest's trees are structures");
                Assert.That(arena.Structures.Any(r => InGroup(r, "Structures")), $"{environment}: the courtyard's walls");
            }
        }

        static bool InGroup(Renderer r, string group)
        {
            for (var t = r.transform; t != null; t = t.parent)
                if (t.name == group && t.parent != null && t.parent.name == "Static") return true;
            return false;
        }

        [Test]
        public void AStyleSwapsEveryStructuresMaterialsAndPutsTheKitBack()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null || lib.StructureShaders.Length == 0) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            foreach (var (environment, arena) in Arenas())
            {
                var kit = arena.Static.ToDictionary(r => r, r => r.sharedMaterials);
                var structures = new HashSet<Renderer>(arena.Structures);
                foreach (var name in Styles)
                {
                    var style = StructureStyle.Parse(name);
                    Assert.That(style.Apply(arena, lib), Is.EqualTo(arena.Structures.Count), $"{environment} {name}");
                    var block = new MaterialPropertyBlock();
                    foreach (var r in arena.Static)
                    {
                        if (!structures.Contains(r))
                        {
                            Assert.That(r.sharedMaterials, Is.EqualTo(kit[r]), $"{environment} {name}: {r.name} keeps the kit");
                            continue;
                        }
                        var mats = r.sharedMaterials;
                        for (int k = 0; k < mats.Length; k++)
                        {
                            Assert.That(mats[k].shader.name, Is.EqualTo(style.ShaderName), $"{environment} {name}: {r.name}");
                            bool canopy = kit[r][k].IsKeywordEnabled("_CANOPY_FADE") || kit[r][k].shader.name == "Demo3D/LitCanopy";
                            Assert.That(mats[k].GetFloat("_SL_Canopy"), Is.EqualTo(canopy ? 1f : 0f), $"{r.name}: the canopy fade follows the kit");
                            Assert.That(mats[k].GetFloat("_SL_Cutoff") > 0, Is.EqualTo(kit[r][k].IsKeywordEnabled("_ALPHATEST_ON")), $"{r.name}: alpha clip follows the kit");
                            Assert.That(mats[k].GetFloat("_Peak"), Is.EqualTo(style.Peak));
                        }
                        Assert.That(r.HasPropertyBlock(), r.name);
                        r.GetPropertyBlock(block);
                        Assert.That(block.GetFloat("_HeightM"), Is.EqualTo(Mathf.Max(r.bounds.size.y, 0.1f)).Within(1e-4f), r.name);
                        Assert.That(block.GetVector("_SL_Piece").y, Is.EqualTo(r.bounds.min.y).Within(1e-4f), r.name);
                        var flames = r.transform.Find("Flames");
                        Assert.That(flames != null, Is.EqualTo(name == "fire"), $"{name}: {r.name}'s flames");
                        if (flames != null)
                        {
                            var fr = flames.GetComponent<MeshRenderer>();
                            Assert.That(fr.sharedMaterial.shader.name, Is.EqualTo("Demo3D/Structures/FireFlames"));
                            Assert.That(fr.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), "flames cast no shadow");
                        }
                    }
                    style.Dispose();
                    foreach (var r in arena.Static)
                    {
                        Assert.That(r.sharedMaterials, Is.EqualTo(kit[r]), $"{environment} {name}: {r.name} gets the kit back");
                        Assert.That(r.transform.Find("Flames"), Is.Null);
                    }
                }
                using var baseline = StructureStyle.Baseline();
                Assert.That(baseline.Apply(arena, lib), Is.EqualTo(0), "baseline leaves the arena as it is");
                foreach (var r in arena.Static) Assert.That(r.sharedMaterials, Is.EqualTo(kit[r]));
            }
        }
    }
}
