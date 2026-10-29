using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Choreography;
using Demo3D.EditorTools.Arena;
using Demo3D.Runtime.Arena;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Tests
{
    /// <summary>TD9's arena: the lighting parameters, the night LUT, the static shadow map's
    /// projection, and the baked scenes against the placement list.</summary>
    public class ArenaTests
    {
        static string LayoutJson => File.ReadAllText(ArenaBaker.LayoutPath);

        [Test]
        public void TheMoonIsCoolAndLowAndFlamesAreCapped()
        {
            var l = ArenaLighting.Parse(LayoutJson);
            Assert.That(l.MoonElevationDeg, Is.InRange(25f, 35f), "environment.md: the moon at 25 to 35 degrees");
            Assert.That(l.MoonColor.b, Is.GreaterThan(l.MoonColor.r), "cool blue moonlight");
            Assert.That(l.MaxFlameLights, Is.InRange(1, 8), "at most 8 flickering flame lights");
            Assert.That(l.Flames, Is.Not.Empty);
            var fp = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            foreach (var f in l.Flames.Concat(l.Glow)) Assert.That(fp[f.Prop], Is.Not.Null, "lit piece " + f.Prop + " is a kit piece");
        }

        [Test]
        public void TheNightLutIsAStripThatCoolsShadowsAndKeepsWarmHighlights()
        {
            var lut = NightLook.Lut();
            Assert.That(lut.width, Is.EqualTo(NightLook.LutSize * NightLook.LutSize));
            Assert.That(lut.height, Is.EqualTo(NightLook.LutSize));
            var dark = NightLook.Grade(new Color(0.1f, 0.1f, 0.1f));
            Assert.That(dark.b, Is.GreaterThan(dark.r), "shadows lean blue");
            var white = NightLook.Grade(Color.white);
            Assert.That(white.r, Is.GreaterThan(0.95f));
            Assert.That(white.g, Is.GreaterThan(0.95f));
            var spark = NightLook.Grade(new Color(1f, 0.55f, 0.15f));
            Assert.That(spark.r, Is.GreaterThan(spark.g));
            Assert.That(spark.g, Is.GreaterThan(spark.b), "warm effects stay warm");
            // Monotonic in each channel along the grey axis.
            float last = -1;
            for (int i = 0; i <= 20; i++)
            {
                var g = NightLook.Grade(new Color(i / 20f, i / 20f, i / 20f));
                float lum = 0.2126f * g.r + 0.7152f * g.g + 0.0722f * g.b;
                Assert.That(lum, Is.GreaterThanOrEqualTo(last));
                last = lum;
            }
        }

        [Test]
        public void TheStaticShadowMapCoversTheWholeArenaAtAboutFourCentimetres()
        {
            var l = ArenaLighting.Parse(LayoutJson);
            var bounds = new Bounds(new Vector3(0, 12, 0), new Vector3(160, 24, 160));
            StaticShadowMap.Compute(l.MoonRotation, bounds, out var view, out var proj, out var texel);
            var m = StaticShadowMap.ShadowTransform(proj, view);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(i % 2 == 0 ? -80 : 80, (i / 2) % 2 == 0 ? 0 : 24, i / 4 == 0 ? -80 : 80);
                var s = m.MultiplyPoint(c);
                Assert.That(s.x, Is.InRange(0f, 1f), "corner " + c);
                Assert.That(s.y, Is.InRange(0f, 1f), "corner " + c);
                Assert.That(s.z, Is.InRange(0f, 1f), "corner " + c);
            }
            Assert.That(Mathf.Max(texel.x, texel.y), Is.LessThan(0.065f), "texel size in metres");
            // A point nearer the moon along its ray compares as nearer the light than the ground.
            var toLight = -(l.MoonRotation * Vector3.forward);
            var ground = m.MultiplyPoint(Vector3.zero);
            var above = m.MultiplyPoint(toLight * 10f);
            Assert.That(above.x, Is.EqualTo(ground.x).Within(1e-4f));
            Assert.That(above.y, Is.EqualTo(ground.y).Within(1e-4f));
            Assert.That(SystemInfo.usesReversedZBuffer ? above.z > ground.z : above.z < ground.z, Is.True, "depth order toward the light");
        }

        [Test]
        public void TheGroundIsDarkerInTheForestThanOnFightingGround()
        {
            var layout = ArenaLayout.Parse(LayoutJson);
            var opening = layout.Area("opening");
            Color32 clearing = ArenaBaker.GroundColor(layout, opening.Center.X, opening.Center.Y);
            Color32 forest = ArenaBaker.GroundColor(layout, -70, 70);
            Assert.That(clearing.g, Is.GreaterThan(forest.g));
        }

        static ArenaIndex Index
        {
            get
            {
                var index = AssetDatabase.LoadAssetAtPath<ArenaIndex>($"{ArenaBaker.Dir}/Resources/{ArenaIndex.ResourcePath}.asset");
                if (index == null || index.entries.Length == 0) Assert.Ignore("the arena isn't baked: run demo3d unity BakeArena");
                return index;
            }
        }

        [Test]
        public void EveryDensityIsBakedForTheBaseline()
        {
            var index = Index;
            foreach (var d in ArenaBaker.E0Densities)
                Assert.That(index.Find(ArenaIndex.E0, d), Is.Not.Null, $"E0 at {d}x");
            Assert.That(index.Find(ArenaIndex.E2, 1f), Is.Not.Null, "E2 at 1x (the demo's)");
        }

        [Test]
        public void TheBakedScenesHoldThePlacementList()
        {
            var index = Index;
            var layout = ArenaLayout.Parse(LayoutJson);
            var table = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            var lighting = ArenaLighting.Parse(LayoutJson);
            try
            {
                foreach (var e in index.entries)
                {
                    var list = layout.WithDensity(e.density).Generate(table);
                    Assert.That(e.placements, Is.EqualTo(list.Items.Length), e.scene + " placements against the list");
                    var scene = EditorSceneManager.OpenScene($"{ArenaBaker.Dir}/{e.scene}.unity", OpenSceneMode.Single);
                    var root = scene.GetRootGameObjects().Single(g => g.name == "Arena").transform;
                    var stat = root.Find("Static");
                    int pieces = stat.Cast<Transform>().Sum(g => g.childCount);
                    Assert.That(pieces, Is.EqualTo(list.Items.Length), e.scene + " pieces");
                    Assert.That(stat.GetComponentsInChildren<Collider>(true), Is.Empty, e.scene + " colliders");
                    Assert.That(stat.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, e.scene + " vendor scripts");
                    // Every piece where its placement says (a sample).
                    int checkedPieces = 0;
                    var groups = stat.Cast<Transform>().ToList();
                    var all = groups.SelectMany(g => g.Cast<Transform>()).ToList();
                    for (int i = 0; i < list.Items.Length; i += 37)
                    {
                        var p = list.Items[i];
                        var at = new Vector3(p.Position.X, 0, p.Position.Y);
                        Assert.That(all.Any(t => (t.position - at).sqrMagnitude < 1e-4f), Is.True, $"{e.scene}: a piece at {at}");
                        checkedPieces++;
                    }
                    Assert.That(checkedPieces, Is.GreaterThan(10));
                    var renderers = stat.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList();
                    foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                    {
                        Assert.That(m, Is.Not.Null, r.name);
                        Assert.That(AssetDatabase.GetAssetPath(m), Does.StartWith(ArenaBaker.MaterialDir), r.name + " uses a converted material");
                        string expect = e.rung == ArenaIndex.E2 ? "Demo3D/LitStaticShadow" : "Universal Render Pipeline/Lit";
                        Assert.That(m.shader.name, Is.EqualTo(expect), r.name);
                    }
                    if (e.rung == ArenaIndex.E0)
                        Assert.That(renderers.All(r => r.shadowCastingMode == ShadowCastingMode.On), Is.True, "E0: every piece casts realtime shadows");
                    var lights = root.GetComponentsInChildren<Light>(true);
                    Assert.That(lights.Count(l => l.type == LightType.Directional), Is.EqualTo(1), "one moon");
                    Assert.That(lights.Count(l => l.GetComponent<FlameFlicker>() != null), Is.InRange(1, lighting.MaxFlameLights), "flame lights");
                    if (e.rung == ArenaIndex.E2)
                        Assert.That(Lightmapping.lightingDataAsset, Is.Not.Null, e.scene + " has baked lighting");
                }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
