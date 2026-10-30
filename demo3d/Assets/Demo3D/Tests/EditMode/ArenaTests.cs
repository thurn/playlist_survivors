using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PS.Choreography;
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
        public void TheKeyLightIsWarmAndKeysTheHordeAndFlamesAreCapped()
        {
            var l = ArenaLighting.Parse(LayoutJson);
            Assert.That(l.MoonElevationDeg, Is.InRange(30f, 55f), "a key light high enough to shade the horde, low enough to model it");
            Assert.That(l.MoonColor.r, Is.GreaterThanOrEqualTo(l.MoonColor.b), "a warm-neutral key (epic ps-e8r, Decision 1)");
            Assert.That(l.CrowdKey, Is.GreaterThan(1f), "the horde is keyed above the set");
            Assert.That(l.CrowdFill, Is.LessThan(1f), "flat fill no longer outweighs the key");
            Assert.That(l.MaxFlameLights, Is.InRange(1, 8), "at most 8 flickering flame lights");
            Assert.That(l.Flames, Is.Not.Empty);
            var fp = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            foreach (var f in l.Flames.Concat(l.Glow)) Assert.That(fp[f.Prop], Is.Not.Null, "lit piece " + f.Prop + " is a kit piece");
        }

        [Test]
        public void ARosterSetsLookChangesThePostBasesAndAPaletteStillAppliesOverIt()
        {
            var night = ArenaLighting.Parse(LayoutJson);
            Assert.That(night.RosterLook, Is.Null);
            Assert.That(ArenaLighting.Parse(LayoutJson, null, "default").RosterLook, Is.Null, "the default roster keeps the night");
            var profile = NightLook.CreateProfile(night);
            Assert.That(profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments color), Is.True);
            Assert.That(color.saturation.value, Is.EqualTo(night.PostSaturation), "the profile's bases are the lighting's post");
            foreach (var set in new[] { "forest", "heroic" })
            {
                var l = ArenaLighting.Parse(LayoutJson, null, set);
                Assert.That(l.RosterLook, Is.EqualTo(set));
                Assert.That(l.PostSaturation, Is.LessThan(night.PostSaturation), set + " grades its own materials less saturated than the dyed horde");
                Assert.That(l.PostFeel.BeatBloom, Is.EqualTo(night.PostFeel.BeatBloom), set + " keeps the post feel riding on top");
                Assert.That(l.MoonElevationDeg, Is.EqualTo(night.MoonElevationDeg), set + " keeps the baked moon's direction");
                var both = ArenaLighting.Parse(LayoutJson, "snow", set);
                Assert.That(both.Palette, Is.EqualTo("snow"));
                Assert.That(both.PostSaturation, Is.EqualTo(l.PostSaturation), "a palette leaves the set's grade");
            }
        }

        [Test]
        public void APaletteRecoloursTheNightAndKeepsWhatItLeavesOut()
        {
            var night = ArenaLighting.Parse(LayoutJson);
            Assert.That(night.Palette, Is.Null);
            Assert.That(night.GroundTint, Is.EqualTo(Color.white), "the authored night leaves the ground as baked");
            var names = ArenaLighting.Palettes(LayoutJson);
            Assert.That(names, Is.SupersetOf(new[] { "snow", "desert", "lava", "neon", "blossom", "crypt" }));
            foreach (var name in names)
            {
                var p = ArenaLighting.Parse(LayoutJson, name);
                Assert.That(p.Palette, Is.EqualTo(name));
                Assert.That(p.FogColor, Is.Not.EqualTo(night.FogColor), name + " recolours the fog and clear colour");
                Assert.That(p.GroundTint, Is.Not.EqualTo(Color.white), name + " tints the ground");
                Assert.That(p.MoonElevationDeg, Is.EqualTo(night.MoonElevationDeg), name + " keeps the baked moon's direction");
                Assert.That(p.CrowdKey, Is.EqualTo(night.CrowdKey), name + " keeps the horde's lighting");
                Assert.That(p.PostFeel.BeatBloom, Is.EqualTo(night.PostFeel.BeatBloom), name + " keeps the post feel");
                Assert.That(p.Flames.Count, Is.EqualTo(night.Flames.Count), name + " keeps the flames");
            }
            var crypt = ArenaLighting.Parse(LayoutJson, "crypt");
            Assert.That(crypt.HeroIntensity, Is.EqualTo(night.HeroIntensity), "a key a palette leaves out keeps its value");
            Assert.That(crypt.HeroColor, Is.EqualTo(Color.white));
            var e = Assert.Throws<ArgumentException>(() => ArenaLighting.Parse(LayoutJson, "nope"));
            StringAssert.Contains("snow", e.Message, "an unknown palette lists the known ones");
        }

        [Test]
        public void TheNightLutIsAStripThatWarmsTheStageWithoutLiftingBlacks()
        {
            var lut = NightLook.Lut();
            Assert.That(lut.width, Is.EqualTo(NightLook.LutSize * NightLook.LutSize));
            Assert.That(lut.height, Is.EqualTo(NightLook.LutSize));
            var black = NightLook.Grade(Color.black);
            Assert.That(Mathf.Max(black.r, Mathf.Max(black.g, black.b)), Is.LessThan(0.002f), "no shadow lift");
            var mid = NightLook.Grade(new Color(0.45f, 0.45f, 0.45f));
            Assert.That(mid.r, Is.GreaterThan(mid.b), "warm midtones: the lit stage");
            var white = NightLook.Grade(Color.white);
            Assert.That(white.r, Is.GreaterThan(0.95f));
            Assert.That(white.g, Is.GreaterThan(0.95f));
            // Monotonic in luminance along the grey axis, and darker shadows: the S-curve adds contrast.
            Assert.That(NightLook.Grade(new Color(0.1f, 0.1f, 0.1f)).g, Is.LessThan(0.1f));
            float last = -1;
            for (int i = 0; i <= 20; i++)
            {
                var g = NightLook.Grade(new Color(i / 20f, i / 20f, i / 20f));
                float lum = 0.2126f * g.r + 0.7152f * g.g + 0.0722f * g.b;
                Assert.That(lum, Is.GreaterThanOrEqualTo(last));
                last = lum;
            }
        }

        /// <summary>Saturated primaries and the effects' colours keep their own hue through the grade (within 10°).</summary>
        [Test]
        public void TheGradeKeepsSaturatedHues()
        {
            var colours = new Dictionary<string, Color>
            {
                ["red"] = new Color(1, 0, 0), ["green"] = new Color(0, 1, 0), ["blue"] = new Color(0, 0, 1),
                ["cyan"] = new Color(0, 1, 1), ["magenta"] = new Color(1, 0, 1), ["yellow"] = new Color(1, 1, 0),
                ["dim red"] = new Color(0.4f, 0, 0), ["dim cyan"] = new Color(0, 0.3f, 0.35f),
                ["spark"] = new Color(1f, 0.55f, 0.15f), ["ember"] = new Color(0.9f, 0.3f, 0.1f),
                ["flame light"] = new Color(1f, 0.52f, 0.22f), ["glowing tree"] = new Color(0.35f, 0.85f, 1f),
                ["rune"] = new Color(0.5f, 0.42f, 1f), ["hit flash"] = new Color(1f, 0.96f, 0.88f),
            };
            foreach (var kv in colours)
            {
                var c = kv.Value;
                Color.RGBToHSV(c, out float h0, out float s0, out _);
                Color.RGBToHSV(NightLook.Grade(c), out float h1, out _, out _);
                if (s0 < 0.3f) continue; // near-white: no hue to keep
                float dh = Mathf.Abs(Mathf.DeltaAngle(h0 * 360, h1 * 360));
                Assert.That(dh, Is.LessThanOrEqualTo(10f), $"{kv.Key}'s hue moved {dh:F1} degrees");
            }
        }

        /// <summary>
        /// ps-e8r.5's lit stage: a warm light over the hero whose pool covers the 6 m around it and ends
        /// inside the song's widest frame (30 m wide), so the frame's edges stay dark; the vignette,
        /// tinted cool, darkens them further.
        /// </summary>
        [Test]
        public void TheHeroLightsAWarmStageThatFadesBeforeTheFramesEdge()
        {
            var l = ArenaLighting.Parse(LayoutJson);
            Assert.That(l.HeroIntensity, Is.GreaterThan(0f));
            Assert.That(l.HeroColor.r, Is.GreaterThan(l.HeroColor.b), "a warm stage (epic ps-e8r, Decision 1)");
            float reach = Mathf.Sqrt(Mathf.Max(0, l.HeroRangeM * l.HeroRangeM - l.HeroHeightM * l.HeroHeightM));
            Assert.That(reach, Is.InRange(6f, 30f / 2), "the pool's reach on the ground, in metres");
            Assert.That(l.VignetteIntensity, Is.GreaterThan(0.26f), "stronger than before the stage");
            Assert.That(l.VignetteColor.b, Is.GreaterThan(l.VignetteColor.r), "the edges lean cool");
        }

        /// <summary>
        /// The fog leaves the fighting ground clear: at the frame's centre it is at most 5% at every song
        /// view width (22.5-30 m) and every beauty shot's, by camera-and-sound.md's geometry (55° pitch,
        /// 30° vertical field of view at 16:9), and the top of the song's widest frame still fades.
        /// </summary>
        [Test]
        public void TheFogClearsTheFrameCentreAndFadesItsFarEdge()
        {
            var l = ArenaLighting.Parse(LayoutJson);
            const float pitch = 55f, halfVfov = 15f;
            float halfHfov = Mathf.Atan(Mathf.Tan(halfVfov * Mathf.Deg2Rad) * 16f / 9f);
            var widths = new List<float> { 22.5f, 25f, 27.5f, 30f };
            foreach (var shot in JsonValue.Parse(LayoutJson)["shots"].Items) widths.Add(shot["viewWidthM"].AsFloat());
            Assert.That(widths.Count, Is.GreaterThan(4), "the beauty shots");
            foreach (float w in widths)
            {
                float focus = w / (2 * Mathf.Tan(halfHfov));
                float centre = NightLook.FogWeight(l, focus, focus);
                Assert.That(centre, Is.LessThanOrEqualTo(0.05f), $"fog at the centre of a {w} m view");
            }
            // The top edge of the frame: the ray 15° above the axis meets the ground at this view depth.
            float widest = 30f / (2 * Mathf.Tan(halfHfov));
            float height = widest * Mathf.Sin(pitch * Mathf.Deg2Rad);
            float topDepth = height / Mathf.Sin((pitch - halfVfov) * Mathf.Deg2Rad) * Mathf.Cos(halfVfov * Mathf.Deg2Rad);
            Assert.That(NightLook.FogWeight(l, widest, topDepth), Is.GreaterThanOrEqualTo(0.08f), "the far edge fades");
            Assert.That(NightLook.FogWeight(l, widest, NightLook.FogRange(l, widest).y), Is.EqualTo(1f), "and ends in the clear colour");
        }

        [Test]
        public void TheStaticShadowMapCoversTheWholeArenaUnderSevenCentimetresOnTheGround()
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
            Assert.That(Mathf.Max(texel.x, texel.y), Is.LessThan(0.065f), "texel size in metres, in light space");
            // On the ground a texel stretches along the moon's azimuth by 1 / sin(elevation) (the cold
            // review's item 6): light-space y tilts toward the light, x stays horizontal.
            float sinElev = Mathf.Sin(l.MoonElevationDeg * Mathf.Deg2Rad);
            var groundTexel = new Vector2(texel.x, texel.y / sinElev);
            TestContext.WriteLine($"static shadow map texel: light space {texel.x * 100:F1} x {texel.y * 100:F1} cm, " +
                                  $"on the ground {groundTexel.x * 100:F1} x {groundTexel.y * 100:F1} cm");
            Assert.That(Mathf.Max(groundTexel.x, groundTexel.y), Is.LessThan(0.13f), "ground-projected texel in metres");
            // A point nearer the moon along its ray compares as nearer the light than the ground.
            var toLight = -(l.MoonRotation * Vector3.forward);
            var ground = m.MultiplyPoint(Vector3.zero);
            var above = m.MultiplyPoint(toLight * 10f);
            Assert.That(above.x, Is.EqualTo(ground.x).Within(1e-4f));
            Assert.That(above.y, Is.EqualTo(ground.y).Within(1e-4f));
            Assert.That(SystemInfo.usesReversedZBuffer ? above.z > ground.z : above.z < ground.z, Is.True, "depth order toward the light");
        }

        /// <summary>
        /// The cold review's item 6: the static shadow map end to end on the GPU (the projection's
        /// y-flip and reversed Z included). One cube is rendered into a small map from the moon; a
        /// ground quad in Demo3D/LitStaticShadow (the hero's and the E2 kit's shader) is then drawn
        /// top-down with no realtime shadows: where the cube's shadow falls (projected along the moon)
        /// the ground must be dark, and lit where it doesn't.
        /// </summary>
        [Test]
        public void TheStaticShadowMapShadesWhereItsCasterShadowsOnTheGpu()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var l = ArenaLighting.Parse(LayoutJson);
            var objects = new List<UnityEngine.Object>();
            StaticShadowMap map = null;
            RenderTexture rt = null;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // a cold editor would draw placeholder shaders while compiling
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.fog = false;
                var sun = new GameObject("Moon").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = l.MoonRotation;
                sun.intensity = 1f;
                sun.shadows = LightShadows.None;
                RenderSettings.sun = sun;
                objects.Add(sun.gameObject);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(0, 4, 0);
                cube.transform.localScale = new Vector3(2, 2, 2);
                var litShader = Shader.Find("Universal Render Pipeline/Lit");
                var cubeMat = new Material(litShader);
                cube.GetComponent<Renderer>().sharedMaterial = cubeMat;
                objects.Add(cube);
                objects.Add(cubeMat);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90, 0, 0));
                ground.transform.localScale = new Vector3(40, 40, 1);
                var groundMat = new Material(Shader.Find("Demo3D/LitStaticShadow"));
                groundMat.SetColor("_BaseColor", Color.white);
                groundMat.SetFloat("_Smoothness", 0f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMat;
                ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                objects.Add(ground);
                objects.Add(groundMat);

                var cam = new GameObject("Top").AddComponent<Camera>();
                objects.Add(cam.gameObject);
                cam.orthographic = true;
                cam.orthographicSize = 20;
                cam.transform.SetPositionAndRotation(new Vector3(0, 50, 0), Quaternion.Euler(90, 0, 0));
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 1;
                cam.farClipPlane = 100;
                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                // One frame first: a cold editor sets the pipeline up on its first camera, which must
                // not come after the map is published.
                cube.GetComponent<Renderer>().enabled = false;
                cam.Render();
                cube.GetComponent<Renderer>().enabled = true;
                map = StaticShadowMap.Render(new[] { cube.GetComponent<Renderer>() }, l.MoonRotation,
                    new Bounds(new Vector3(0, 5, 0), new Vector3(40, 10, 40)), 512);
                cube.GetComponent<Renderer>().enabled = false; // only its shadow, through the map
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                objects.Add(tex);

                float Lum(Vector3 world)
                {
                    var c = tex.GetPixel(Mathf.RoundToInt((world.x + 20) / 40 * 256), Mathf.RoundToInt((world.z + 20) / 40 * 256));
                    return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                }
                var toLight = -(l.MoonRotation * Vector3.forward);
                var shade = cube.transform.position - toLight * (cube.transform.position.y / toLight.y);
                shade.y = 0;
                var lit = new Vector3(-shade.x, 0, -shade.z); // the other side of the cube
                var off = shade + Vector3.Cross(Vector3.up, new Vector3(toLight.x, 0, toLight.z).normalized) * 5f; // beside the shadow
                float inShade = Lum(shade), outside = Lum(lit), beside = Lum(off);
                TestContext.WriteLine($"shadow at {shade}: {inShade:F3}; opposite {outside:F3}; beside {beside:F3}");
                Assert.That(outside, Is.GreaterThan(0.1f), "the ground is lit away from the cube");
                Assert.That(beside, Is.GreaterThan(0.1f), "and beside its shadow");
                Assert.That(inShade, Is.LessThan(0.35f * outside), "the cube's shadow falls where the moon throws it");
            }
            finally
            {
                map?.Dispose();
                ShaderUtil.allowAsyncCompilation = async;
                if (rt != null) rt.Release();
                foreach (var o in objects) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>
        /// ps-bop: the crypt is the arena's plan with its own kit, ground and light: the area plan
        /// comes from arena-layout.json, the overrides apply and what it leaves out is the forest's.
        /// </summary>
        [Test]
        public void AnEnvironmentInheritsTheArenasPlanAndAppliesItsOverrides()
        {
            var forest = ArenaEnvironment.Load(LayoutEnvironment.Default);
            var crypt = ArenaEnvironment.Load("crypt");
            Assert.That(crypt.Dir, Is.EqualTo(ArenaBaker.Dir + "/crypt"), "its derived files beside the forest's");
            Assert.That(forest.Json, Is.EqualTo(LayoutJson), "the forest is the arena's layout itself");
            ArenaLayout a = ArenaLayout.Parse(forest.Json), c = ArenaLayout.Parse(crypt.Json);
            Assert.That(c.Name, Is.EqualTo(a.Name));
            Assert.That(c.Size, Is.EqualTo(a.Size));
            Assert.That(c.HeroStart, Is.EqualTo(a.HeroStart));
            Assert.That(c.DecorationDensity, Is.EqualTo(a.DecorationDensity));
            Assert.That(c.Areas.Select(x => (x.Type, x.Name, x.Center, x.Radius, x.Width)), Is.EqualTo(a.Areas.Select(x => (x.Type, x.Name, x.Center, x.Radius, x.Width))));
            Assert.That(c.Shots.Select(x => (x.Name, x.At, x.ViewWidthM)), Is.EqualTo(a.Shots.Select(x => (x.Name, x.At, x.ViewWidthM))));
            Assert.That(c.Area("glowing-tree").Pieces.Select(p => p.Prop), Does.Not.Contain("elven/SM_Env_Tree_Glowing_01"), "its pieces replace the landmark's");
            Assert.That(c.Area("graveyard").Pieces.Select(p => p.Prop), Is.EqualTo(a.Area("graveyard").Pieces.Select(p => p.Prop)), "a landmark it leaves alone");
            Assert.That(c.Groups["forest-trees"].Props, Has.None.StartsWith("adventure/"), "a prop group replaces whole");

            var fl = ArenaLighting.Parse(forest.Json);
            var cl = ArenaLighting.Parse(crypt.Json);
            Assert.That(cl.MoonColor.r, Is.EqualTo(cl.MoonColor.b).Within(1e-4f), "a neutral moon");
            Assert.That(cl.MoonElevationDeg, Is.EqualTo(fl.MoonElevationDeg), "the moon's direction is the arena's");
            Assert.That(cl.CrowdKey, Is.EqualTo(fl.CrowdKey), "the horde's lighting is the arena's");
            Assert.That(cl.PostFeel.BeatBloom, Is.EqualTo(fl.PostFeel.BeatBloom), "the post feel rides on top");
            Assert.That(cl.Flames.Count, Is.EqualTo(fl.Flames.Count), "the candles, braziers and pyres keep their flames");
            Assert.That(cl.Glow, Is.Empty, "null removes the glowing tree's light");
            Assert.That(cl.GradeMid, Is.EqualTo(Vector3.one), "its own grade");
            var fg = ArenaBaker.GroundColors.Parse(forest.Root["ground"]);
            var cg = ArenaBaker.GroundColors.Parse(crypt.Root["ground"]);
            Assert.That(cg.Clearing, Is.Not.EqualTo(fg.Clearing), "its own ground");
            Assert.That(crypt.Root["lighting"]["kitValue"]["PolygonDarkFantasy"].AsFloat(), Is.EqualTo(0.7f), "the kit's value scales are inherited");
            var fp = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            foreach (var g in c.Groups.Values) foreach (var prop in g.Props) Assert.That(fp[prop], Is.Not.Null, prop + " is a measured kit piece");

            var e = Assert.Throws<ArgumentException>(() => ArenaEnvironment.Load("nope"));
            StringAssert.Contains("forest, crypt", e.Message, "an unknown environment lists the known ones");
        }

        /// <summary>The player's path (PlaybackData.EnvironmentLayout) resolves an environment as the bakers do, and fails on an unknown one.</summary>
        [Test]
        public void ThePlayerResolvesAnEnvironmentAsTheBakersDo()
        {
            var data = ScriptableObject.CreateInstance<Demo3D.Runtime.Playback.PlaybackData>();
            try
            {
                data.layouts = new[] { new TextAsset(LayoutJson) { name = "arena-layout" } };
                data.environments = new[] { new TextAsset(File.ReadAllText(ArenaBaker.DataDir + "/environment-crypt.json")) { name = "environment-crypt" } };
                Assert.That(data.EnvironmentNames(), Is.EqualTo(new[] { "forest", "crypt" }));
                Assert.That(data.EnvironmentLayout("crypt"), Is.EqualTo(ArenaEnvironment.Load("crypt").Json));
                Assert.That(data.EnvironmentLayout("forest"), Is.EqualTo(LayoutJson));
                var e = Assert.Throws<ArgumentException>(() => data.EnvironmentLayout("lava"));
                StringAssert.Contains("forest, crypt", e.Message);
            }
            finally
            {
                foreach (var t in data.layouts.Concat(data.environments)) UnityEngine.Object.DestroyImmediate(t);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        /// <summary>Epic ps-bop's decisions 4 and 5: the horde keeps a neutral key under a coloured moon, and an environment grades its own way.</summary>
        [Test]
        public void TheHordesKeyStaysNeutralUnderAColouredMoonAndAGradeIsTheEnvironments()
        {
            var night = ArenaLighting.Parse(LayoutJson);
            Assert.That(NightLook.KeyColorScale(night).w, Is.EqualTo(0f), "the forest's horde takes the moon's own colour");
            var l = ArenaLighting.Parse(LayoutJson);
            l.MoonColor = new Color(1f, 0.5f, 0.25f);
            l.CrowdKeyColor = Color.white;
            var k = NightLook.KeyColorScale(l);
            Assert.That(k.w, Is.EqualTo(1f));
            var onHorde = new Vector3(l.MoonColor.r * k.x, l.MoonColor.g * k.y, l.MoonColor.b * k.z);
            Assert.That(onHorde.x, Is.EqualTo(onHorde.z).Within(1e-4f), "neutral on the horde");
            float Luma(Vector3 v) => 0.2126f * v.x + 0.7152f * v.y + 0.0722f * v.z;
            Assert.That(Luma(onHorde), Is.EqualTo(Luma(new Vector3(1f, 0.5f, 0.25f))).Within(1e-4f), "at the moon's luminance");

            var crypt = ArenaLighting.Parse(ArenaEnvironment.Load("crypt").Json);
            var grey = NightLook.Grade(new Color(0.45f, 0.45f, 0.45f), crypt);
            Assert.That(grey.r, Is.EqualTo(grey.b).Within(1e-4f), "the crypt's grade leaves greys grey");
            var warm = NightLook.Grade(new Color(0.45f, 0.45f, 0.45f));
            Assert.That(warm.r, Is.GreaterThan(warm.b), "the night's still warms them");
            Assert.That(NightLook.Lut(crypt), Is.Not.SameAs(NightLook.Lut()), "one LUT per grade");
        }

        [Test]
        public void TheGroundIsDarkerInTheForestThanOnFightingGround()
        {
            var layout = ArenaLayout.Parse(LayoutJson);
            var opening = layout.Area("opening");
            var colors = ArenaBaker.GroundColors.Parse(JsonValue.Parse(LayoutJson)["ground"]);
            Color32 clearing = ArenaBaker.GroundColor(layout, colors, opening.Center.X, opening.Center.Y);
            Color32 forest = ArenaBaker.GroundColor(layout, colors, -70, 70);
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

        /// <summary>The kit piece (a child of a Static group) that a renderer belongs to.</summary>
        static Transform PieceOf(Transform t, Transform stat)
        {
            while (t.parent != null && t.parent.parent != stat) t = t.parent;
            return t;
        }

        /// <summary>TD15's kit value scales a converted material's RGB by its pack's entry and leaves alpha (alpha clipping) alone.</summary>
        [Test]
        public void KitValueDarkensThePaleAtlasesButKeepsAlpha()
        {
            var values = JsonValue.Parse(LayoutJson)["lighting"]["kitValue"];
            var c = ArenaDressing.KitValue(values, new Color(1f, 0.8f, 0.6f, 0.5f), "PolygonElven_Mat_01_A");
            Assert.That(c.r, Is.LessThan(1f));
            Assert.That(c.g / c.r, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(c.a, Is.EqualTo(0.5f));
            Assert.That(ArenaDressing.KitValue(values, Color.white, "SomeOtherPack_Mat"), Is.EqualTo(Color.white));
            Assert.That(ArenaDressing.IsTree("SM_Env_Tree_Round_01"), Is.True);
            Assert.That(ArenaDressing.IsTree("SM_Env_TreeLog_01"), Is.False);
        }

        [Test]
        public void EveryDensityIsBakedForTheBaselineAndEachEnvironmentAtTheAuthoredOne()
        {
            var index = Index;
            const string forest = LayoutEnvironment.Default;
            foreach (var d in ArenaBaker.E0Densities)
                Assert.That(index.Find(forest, ArenaIndex.E0, d), Is.Not.Null, $"E0 at {d}x");
            Assert.That(index.Find(forest, ArenaIndex.E2, 1f), Is.Not.Null, "E2 at 1x (the demo's)");
            foreach (var d in ArenaBaker.LitDensities)
                Assert.That(index.Find(forest, ArenaIndex.E2, d), Is.Not.Null, $"E2 at {d}x (TD14's density sweep)");
            foreach (var d in ArenaBaker.MergedDensities)
            {
                Assert.That(index.Find(forest, ArenaIndex.E0, d, true), Is.Not.Null, $"E0 merged at {d}x");
                Assert.That(index.Find(forest, ArenaIndex.E2, d, true), Is.Not.Null, $"E2 merged at {d}x");
            }
            float authored = ArenaLayout.Parse(LayoutJson).DecorationDensity;
            foreach (var name in ArenaEnvironment.Names().Where(n => n != forest))
            {
                var e = index.Find(name, ArenaIndex.E2, authored);
                Assert.That(e, Is.Not.Null, $"{name}: E2 at the authored {authored}x (the player's default)");
                Assert.That(e.scene, Is.EqualTo($"Arena-{name}-E2-{authored}x"));
                Assert.That(index.Find(name, ArenaIndex.E0, authored), Is.Not.Null, $"{name}: E0 at {authored}x (S0's stock rung)");
            }
        }

        [Test]
        public void ArenaOptionsReadTheRungSwitches()
        {
            var o = ArenaOptions.FromBench(new Dictionary<string, object>
            {
                ["rung"] = "e2", ["density"] = 2.0, ["grd"] = "occlusion", ["merged"] = true, ["texCap"] = 1024.0, ["prepass"] = "out",
            }, "rung", ArenaOptions.Stock());
            Assert.That(o.Rung, Is.EqualTo(ArenaIndex.E2));
            Assert.That(o.Density, Is.EqualTo(2f));
            Assert.That(o.Grd, Is.EqualTo(ArenaOptions.GrdOcclusion));
            Assert.That(o.Merged, Is.True);
            Assert.That(o.TexCap, Is.EqualTo(1024));
            Assert.That(o.Prepass, Is.False);
            Assert.That(o.Label(), Is.EqualTo("e2 2x grd:occlusion merged tex:1024 prepass:out"));
            var stock = ArenaOptions.FromBench(new Dictionary<string, object>(), "rung", ArenaOptions.Stock());
            Assert.That(stock.Label(), Is.EqualTo("e0 1x"), "S0's default is the stock baseline");
            Assert.That(ArenaOptions.FromBench(new Dictionary<string, object> { ["grd"] = true }, "rung", ArenaOptions.Stock()).Grd,
                Is.EqualTo(ArenaOptions.GrdOn));
            Assert.Throws<ArgumentException>(() => ArenaOptions.FromBench(new Dictionary<string, object> { ["grd"] = "maybe" }, "rung", ArenaOptions.Stock()));
            Assert.Throws<ArgumentException>(() => ArenaOptions.FromBench(new Dictionary<string, object> { ["texCap"] = 1000.0 }, "rung", ArenaOptions.Stock()));
            Assert.Throws<ArgumentException>(() => ArenaOptions.FromBench(new Dictionary<string, object> { ["rung"] = "e9" }, "rung", ArenaOptions.Stock()));
            Assert.That(ArenaOptions.FromBench(new Dictionary<string, object> { ["without"] = "forest" }, "rung", ArenaOptions.Stock()).Label(),
                Is.EqualTo("e0 1x without:forest"));
            Assert.Throws<ArgumentException>(() => ArenaOptions.FromBench(new Dictionary<string, object> { ["without"] = "trees" }, "rung", ArenaOptions.Stock()));
            var crypt = ArenaOptions.FromBench(new Dictionary<string, object> { ["environment"] = "Crypt" }, "arena", ArenaOptions.Final());
            Assert.That(crypt.Environment, Is.EqualTo("crypt"));
            Assert.That(crypt.Label(), Does.StartWith("crypt e2 "), "a sweep's label names a non-default environment");
            Assert.That(ArenaOptions.Final().Environment, Is.EqualTo(LayoutEnvironment.Default));
        }

        [Test]
        public void TheKitsTexturesImportIntoTheCapGroup()
        {
            Assert.That(UnityEngine.TextureMipmapLimitGroups.HasGroup(ArenaPipeline.KitTextureGroup), Is.True, "ProjectSetup creates the group");
            Assert.That(KitTextureImport.IsKit("Assets/Vendor/PolygonElvenRealm/Textures/Alts/PolygonElven_Texture_01_A.png"), Is.True);
            Assert.That(KitTextureImport.IsKit("Assets/Vendor/PolygonKnights/Textures/PolygonKnights_Texture_01.png"), Is.False, "the hero's pack");
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Vendor/PolygonElvenRealm" });
            if (guids.Length == 0) Assert.Ignore("vendor assets not synced");
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(imp.mipmapLimitGroupName, Is.EqualTo(ArenaPipeline.KitTextureGroup));
        }

        [Test]
        public void TheBakedScenesHoldThePlacementList()
        {
            var index = Index;
            var table = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            try
            {
                foreach (var e in index.entries)
                {
                    // Each environment's scenes hold its own placement list (ps-bop), with its own materials.
                    var env = ArenaEnvironment.Load(ArenaIndex.EnvironmentOf(e));
                    var layout = ArenaLayout.Parse(env.Json);
                    var lighting = ArenaLighting.Parse(env.Json);
                    var list = layout.WithDensity(e.density).Generate(table);
                    Assert.That(e.placements, Is.EqualTo(list.Items.Length), e.scene + " placements against the list");
                    var scene = EditorSceneManager.OpenScene($"{env.Dir}/{e.scene}.unity", OpenSceneMode.Single);
                    var root = scene.GetRootGameObjects().Single(g => g.name == "Arena").transform;
                    var stat = root.Find("Static");
                    int pieces = stat.Cast<Transform>().Where(g => g.name != "Merged").Sum(g => g.childCount);
                    Assert.That(pieces, Is.EqualTo(list.Items.Length), e.scene + " pieces");
                    Assert.That(stat.GetComponentsInChildren<Collider>(true), Is.Empty, e.scene + " colliders");
                    Assert.That(stat.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, e.scene + " vendor scripts");
                    // Every piece where its placement says (a sample).
                    int checkedPieces = 0;
                    var groups = stat.Cast<Transform>().Where(g => g.name != "Merged").ToList();
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
                        Assert.That(AssetDatabase.GetAssetPath(m), Does.StartWith(env.MaterialDir + "/"), r.name + " uses its environment's converted material");
                        // At E2 the trees take TD15's canopy fade (Demo3D/LitCanopy, its keyword on); the rock is TD14's triplanar port.
                        bool canopy = e.rung == ArenaIndex.E2 && ArenaDressing.IsTree(PieceOf(r.transform, stat).name);
                        string expect = m.name.StartsWith("RockTriplanar") ? "Demo3D/RockTriplanar"
                            : canopy ? "Demo3D/LitCanopy" : e.rung == ArenaIndex.E2 ? "Demo3D/LitStaticShadow" : "Universal Render Pipeline/Lit";
                        Assert.That(m.shader.name, Is.EqualTo(expect), r.name);
                        if (canopy)
                        {
                            Assert.That(m.IsKeywordEnabled("_CANOPY_FADE"), Is.True, r.name + " fades");
                            // The twin follows its converted material (the kit value included), bake after bake.
                            var source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(m).Replace("_Canopy.mat", ".mat"));
                            Assert.That(source, Is.Not.Null, m.name + "'s source");
                            Assert.That(m.GetColor("_BaseColor"), Is.EqualTo(source.GetColor("_BaseColor")), m.name + " colour");
                        }
                    }
                    // TD15's atmosphere: each of mist, runes and glow pools one shadowless draw outside the static set.
                    foreach (var name in new[] { "Mist", "Runes", "GlowPools" })
                    {
                        var go = root.Find(name);
                        Assert.That(go, Is.Not.Null, e.scene + " " + name);
                        var mr = go.GetComponent<MeshRenderer>();
                        Assert.That(mr.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), name);
                        Assert.That(go.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThanOrEqualTo(4), name);
                    }
                    // Every scene draws the placement list's triangles, merged or not.
                    long tris = 0;
                    foreach (var r in renderers)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
                    }
                    Assert.That(tris, Is.EqualTo(e.triangles), e.scene + " triangles");
                    if (e.merged)
                    {
                        Assert.That(e.mergedRenderers, Is.GreaterThan(0), e.scene + " merged renderers");
                        Assert.That(stat.Find("Merged").childCount, Is.EqualTo(e.mergedRenderers), e.scene);
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
