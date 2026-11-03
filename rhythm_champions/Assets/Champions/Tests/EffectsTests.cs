using System;
using System.IO;
using System.Linq;
using Champions.EditorTools.Effects;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The weapon effects (design.md, The Look &gt; Weapon effects) on synthetic stand-ins for Hovl's meshes (a cupped ring
    /// and a box sword under Hovl's names), so they run without the vendor files: the recipes, the effects' lives, their
    /// white in a still and its type-ID pass, and the meshes' frames.
    /// </summary>
    public sealed class EffectsTests
    {
        static string ContentDir => GameContent.Find(Application.dataPath);

        EffectLibrary _lib;
        MeleeLibrary _weapons;
        Camera _camera;

        [SetUp]
        public void MakeLibrary()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _lib = ScriptableObject.CreateInstance<EffectLibrary>();
            _lib.Meshes = EffectBaker.Rings.Select(n => EffectBaker.Measure(n, Ring(1f, 2.5f), solid: false))
                .Append(EffectBaker.Measure(EffectBaker.Sword, Sword(), solid: true)).ToArray();
            _lib.Slash = AssetDatabase.LoadAssetAtPath<Shader>(EffectBaker.ShaderDir + "Slash.shader");
            _lib.Spark = AssetDatabase.LoadAssetAtPath<Shader>(EffectBaker.ShaderDir + "Spark.shader");
            _lib.Sparks = AssetDatabase.LoadAssetAtPath<ComputeShader>(EffectBaker.ShaderDir + "Sparks.compute");
            // Hold tests use synthetic rigid weapons rather than a host's optional private bake.
            _weapons = ScriptableObject.CreateInstance<MeleeLibrary>();
            _weapons.Sword = Sword();
            _weapons.Hammer = Sword();
            _weapons.Edge = AssetDatabase.LoadAssetAtPath<Shader>(EffectBaker.ShaderDir + "MeleeEdge.shader");
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.fieldOfView = WorldCamera.FovDeg;
            _camera.transform.rotation = Quaternion.Euler(WorldCamera.PitchDeg, 0, 0);
            _camera.transform.position = -_camera.transform.forward * WorldCamera.DistanceM;
        }

        [TearDown]
        public void Clean()
        {
            foreach (var m in _lib.Meshes) Object.DestroyImmediate(m.Mesh);
            Object.DestroyImmediate(_lib);
            Object.DestroyImmediate(_weapons.Sword);
            Object.DestroyImmediate(_weapons.Hammer);
            Object.DestroyImmediate(_weapons);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>A ring in the xy plane (thinnest along z), cupped like Hovl's: its outer edge raised.</summary>
        static Mesh Ring(float inner, float outer, int segments = 44, float spanDeg = 360)
        {
            var v = new Vector3[(segments + 1) * 2];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var t = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = (spanDeg * i / segments - spanDeg / 2) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v[2 * i] = d * inner;
                v[2 * i + 1] = d * outer + new Vector3(0, 0, 0.1f);
                n[2 * i] = n[2 * i + 1] = Vector3.forward;
                uv[2 * i] = new Vector2((float)i / segments, 0);
                uv[2 * i + 1] = new Vector2((float)i / segments, 1);
                if (i == segments) break;
                int k = 6 * i, b = 2 * i;
                t[k] = b; t[k + 1] = b + 2; t[k + 2] = b + 1; t[k + 3] = b + 1; t[k + 4] = b + 2; t[k + 5] = b + 3;
            }
            var mesh = new Mesh { vertices = v, normals = n, uv = uv, triangles = t };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A 2 m sword along +z from its grip a little behind the origin, flat in x.</summary>
        static Mesh Sword()
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var mesh = Object.Instantiate(cube);
            mesh.vertices = cube.vertices.Select(p => new Vector3(p.x * 0.2f, p.y * 0.05f, (p.z + 0.5f) * 2f - 0.2f)).ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        Effects Make() => new Effects(_lib, EffectRecipes.Load(ContentDir), Palette.Load(ContentDir), _weapons);

        [Test]
        public void EveryCueHasARecipe()
        {
            var recipes = EffectRecipes.Load(ContentDir);
            foreach (EffectCue cue in Enum.GetValues(typeof(EffectCue)))
                Assert.That(recipes.Cues[(int)cue].Length > 0 || MagicVisuals.Handles(cue), Is.True, EffectRecipes.Name(cue));
            Assert.That(recipes.Orbit, Is.Not.Empty);
            Assert.That(recipes.Halo, Is.Not.Empty);
        }

        [Test]
        public void RepeatedSummonVolleysReplaceOneBoundedRosterAndExpire()
        {
            using var constructs = new ConstructVisuals(Palette.Load(ContentDir));
            var shot = new EffectShot { T = 1, At = Vector2.zero, Facing = Vector2.up, Radius = 2.3f, Duration = 2.4f };
            for (int i = 0; i < 100; i++) constructs.Fire(EffectCue.SentinelFormation, shot);
            constructs.Present(1.1, Vector2.zero);
            Assert.That(constructs.Shapes, Is.LessThanOrEqualTo(7), "three posed knights, three swords and retained close guard");
            if (Champions.Render.Crowd.MonsterLibrary.LoadFigure(Champions.Render.Crowd.MonsterLook.Default, "hero") != null)
                Assert.That(constructs.Shapes, Is.GreaterThanOrEqualTo(3));
            constructs.Present(4, Vector2.zero);
            Assert.That(constructs.Shapes, Is.Zero, "finite roster disappears without new volleys");
        }

        [Test]
        public void MagicFieldsFreezeAcrossHoldsAndReleaseAfterFiniteDuration()
        {
            MagicBaker.Bake();
            using var fx = new MagicVisuals();
            fx.Fire(EffectCue.EmberProcession, new EffectShot { T = 1, Radius = 2.4f, Duration = 3, Facing = Vector2.up, Advance = 1.5f });
            fx.Present(1.1); Assert.That(fx.Draws, Is.GreaterThan(0));
            int draws = fx.Draws;
            fx.Hold(1.1, true); fx.Present(20);
            Assert.That(fx.Draws, Is.EqualTo(draws));
            fx.Hold(20, false); fx.Present(20.1);
            Assert.That(fx.Draws, Is.GreaterThan(0));
            fx.Present(23); Assert.That(fx.Draws, Is.Zero);
        }

        [Test]
        public void RepeatedFieldPulsesDoNotAccumulateIdleGeometryAndClearDropsEverything()
        {
            MagicBaker.Bake();
            using var fx = new MagicVisuals();
            var shot = new EffectShot { T = 1, Radius = 2.4f, Duration = 3, Facing = Vector2.up };
            fx.Fire(EffectCue.EmberBrazier, shot); fx.Present(1.1);
            int draws = fx.Draws;
            for (int k = 0; k < 30; k++) fx.Fire(EffectCue.EmberBrazier, shot);
            fx.Present(1.1); Assert.That(fx.Draws, Is.EqualTo(draws));
            fx.Clear(); fx.Present(1.1); Assert.That(fx.Draws, Is.Zero);
        }

        [Test]
        public void MeleeMotionFreezesAcrossCardsThenFinishesItsRemainingSettle()
        {
            using var fx = new MeleeVisuals(_weapons);
            fx.Fire(EffectCue.MeleeSweep, new EffectShot { T = 1, Facing = Vector2.up, Radius = 3 });
            fx.Present(1.1);
            Assert.That(fx.Draws, Is.GreaterThan(0));
            fx.Hold(1.1, true); fx.Present(11.1);
            Assert.That(fx.Draws, Is.GreaterThan(0));
            fx.Hold(11.1, false); fx.Present(11.2);
            Assert.That(fx.Draws, Is.GreaterThan(0));
            fx.Present(11.5);
            Assert.That(fx.Draws, Is.Zero);
        }

        [Test]
        public void CardHoldPreservesMeleeAndSentinelUntilTheirRemainingLifetimeEnds()
        {
            using var fx = Make();
            fx.Fire(EffectCue.MeleeSweep, new EffectShot { T = 1, Facing = Vector2.up, Radius = 3 });
            fx.Fire(EffectCue.SentinelFormation, new EffectShot { T = 1, Facing = Vector2.up, Radius = 2.3f, Duration = 2.4f });
            fx.Present(1.1, Vector2.zero, 0, null);
            Assert.That(fx.LiveShapes, Is.GreaterThan(0));
            fx.Hold(1.1, true);
            fx.Present(11.1, Vector2.zero, 0, null);
            Assert.That(fx.LiveShapes, Is.GreaterThan(0), "the retained roster stays visible during cards");
            fx.Hold(11.1, false);
            fx.Present(11.2, Vector2.zero, 0, null);
            Assert.That(fx.LiveShapes, Is.GreaterThan(0));
            fx.Present(13.5, Vector2.zero, 0, null);
            Assert.That(fx.LiveShapes, Is.Zero);
        }

        [Test]
        public void ARecipesUnknownFieldFailsWithItsPath()
        {
            string json = File.ReadAllText(Path.Combine(ContentDir, EffectRecipes.File))
                .Replace("\"swordSlash\": [\n      {\"shape\": \"arc\",", "\"swordSlash\": [\n      {\"shape\": \"arc\", \"glow\": 1,");
            var e = Assert.Throws<ContentException>(() => EffectRecipes.Read(JsonValue.Parse(json)));
            Assert.That(e.Message, Does.Contain("cues.swordSlash[0].glow"));
        }

        [Test]
        public void TheGalleryShowsEveryCue()
        {
            var shown = Client.Scenes.GalleryScene.Entries.Where(x => x.Cue.HasValue).Select(x => x.Cue.Value).ToHashSet();
            foreach (EffectCue cue in Enum.GetValues(typeof(EffectCue)))
                Assert.That(shown, Does.Contain(cue));
            Assert.That(Client.Scenes.GalleryScene.Entries.Count(x => x.Cue == null), Is.EqualTo(2), "Orbit Blades and Blade Halo");
        }

        [Test]
        public void ACuesShapesAndSparksLiveTheirLifeAndEnd()
        {
            using var fx = Make();
            fx.Fire(EffectCue.SwordSlash, new EffectShot { T = 1, Facing = Vector2.up, Radius = 3, ArcDeg = 150 });
            fx.Present(1.05, Vector2.zero, 1 / 60f, _camera);
            var cost = fx.Cost(EffectCue.SwordSlash);
            Assert.That(fx.LiveShapes, Is.EqualTo(cost.Shapes));
            Assert.That(fx.LiveSparks, Is.EqualTo(cost.Sparks));
            Assert.That(fx.Draws, Is.EqualTo(cost.Draws), "one draw per mesh, one for the sparks");
            fx.Present(3, Vector2.zero, 1 / 60f, _camera);
            Assert.That(fx.LiveShapes, Is.EqualTo(0));
            Assert.That(fx.LiveSparks, Is.EqualTo(0));
            Assert.That(fx.Draws, Is.EqualTo(0));
        }

        [Test]
        public void OrbitBladesRingFollowsTheHeroUntilItGoes()
        {
            using var fx = Make();
            var recipes = EffectRecipes.Load(ContentDir);
            fx.Orbit(0, 3, 0, Math.PI, 2.5f);
            fx.Present(0.5, new Vector2(4, 4), 1 / 60f, _camera);
            Assert.That(fx.LiveShapes, Is.EqualTo(3 * recipes.Orbit.Length));
            fx.Orbit(1, 6, 0, Math.PI, 3.5f, evolved: true);
            fx.Present(1.1, Vector2.zero, 1 / 60f, _camera);
            Assert.That(fx.LiveShapes, Is.EqualTo(6 * recipes.Halo.Length));
            fx.Orbit(2, 0, 0, 0, 0);
            fx.Present(2.1, Vector2.zero, 1 / 60f, _camera);
            Assert.That(fx.LiveShapes, Is.EqualTo(0));
        }

        [Test]
        public void TheHerosEffectsDrawWhiteAndTheIdPassMarksThemAsEffects()
        {
            using var fx = Make();
            fx.Fire(EffectCue.SwordSlash, new EffectShot { T = 1, Facing = Vector2.up, Radius = 3, ArcDeg = 150, Power = EffectPower.Special });
            fx.Fire(EffectCue.LeapSlam, new EffectShot { T = 1, At = new Vector2(-4, -2), Radius = 3 });
            fx.Present(1.1, Vector2.zero, 1 / 60f, _camera);
            var (rgb, ids) = Keep(fx);
            int lit = 0, colourful = 0, unmarked = 0;
            for (int i = 0; i < rgb.Length; i++)
            {
                var (l, c) = Lab(rgb[i]);
                if (l < 40) continue;
                lit++;
                if (c >= 30) colourful++;
                if (ids[i].r != IdPass.Effect) unmarked++;
            }
            Assert.That(lit, Is.GreaterThan(2000), "the slash and the shockwave light the frame");
            Assert.That(colourful, Is.EqualTo(0), "white is the hero's: no pixel at C* 30 or more");
            Assert.That(unmarked, Is.LessThan(lit / 4), "the ID pass marks the lit shapes as effects (bloom aside)");
        }

        [Test]
        public void OnTheBeatIsBrighterAndASpecialMomentBrighterStill()
        {
            float Light(EffectPower power)
            {
                using var fx = Make();
                fx.Fire(EffectCue.Dash, new EffectShot { T = 1, At = new Vector2(-2, -1), To = new Vector2(2, 1), Duration = 0.15f, Power = power });
                fx.Present(1.17, Vector2.zero, 1 / 60f, _camera);
                return Keep(fx).Rgb.Sum(p => Lab(p).L);
            }
            float plain = Light(EffectPower.Plain), onBeat = Light(EffectPower.OnBeat), special = Light(EffectPower.Special);
            Assert.That(onBeat, Is.GreaterThan(plain * 1.1f));
            Assert.That(special, Is.GreaterThan(onBeat * 1.1f));
        }

        [Test]
        public void TheFrameMakesARingAUnitRingAndASwordsTipTheOrigin()
        {
            var ring = EffectBaker.Measure("ring", Ring(1f, 2.5f), solid: false);
            Assert.That(ring.Inner, Is.EqualTo(0.4f).Within(1e-3f));
            var p = ring.Frame.MultiplyPoint3x4(new Vector3(0, 2.5f, 0.1f));
            Assert.That(new Vector2(p.x, p.z).magnitude, Is.EqualTo(1f).Within(1e-3f), "the outer edge at radius 1 in the xz plane");
            Assert.That(Mathf.Abs(p.y), Is.LessThan(0.05f), "the thin axis is +y");

            var crescent = EffectBaker.Measure("crescent", Ring(1f, 2f, 16, 120), solid: false);
            var middle = crescent.Frame.MultiplyPoint3x4(new Vector3(2f, 0, 0.1f));
            Assert.That(middle.x, Is.EqualTo(1f).Within(1e-2f), "a crescent's middle along +x");
            Assert.That(middle.z, Is.EqualTo(0f).Within(1e-2f));

            var sword = EffectBaker.Measure("sword", Sword(), solid: true);
            Assert.That(sword.Frame.MultiplyPoint3x4(new Vector3(0, 0, 1.8f)).x, Is.EqualTo(0f).Within(1e-3f), "the tip at 0");
            Assert.That(sword.Frame.MultiplyPoint3x4(new Vector3(0, 0, -0.2f)).x, Is.EqualTo(-1f).Within(1e-3f), "the grip at -1");
        }

        /// <summary>A still of the frame (Stills: the camera drawn by hand, then its ID pass), read back.</summary>
        (Color32[] Rgb, Color32[] Ids) Keep(Effects fx)
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-effects-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stills = new Stills(_camera, dir, new double[0]) { Requeue = () => fx.Queue(_camera) })
                    stills.Keep("fx", new Stills.View());
                return (Read(Path.Combine(dir, "still-fx.png")), Read(Path.Combine(dir, "id-fx.png")));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static Color32[] Read(string path)
        {
            var tex = new Texture2D(2, 2);
            Assert.That(tex.LoadImage(File.ReadAllBytes(path)));
            var pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>CIELAB L* and C*ab (D65) of an sRGB pixel.</summary>
        static (float L, float C) Lab(Color32 p)
        {
            var c = ((Color)p).linear;
            float x = 0.4124f * c.r + 0.3576f * c.g + 0.1805f * c.b, y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b,
                z = 0.0193f * c.r + 0.1192f * c.g + 0.9505f * c.b;
            static float F(float t) => t > 216f / 24389f ? Mathf.Pow(t, 1f / 3f) : (24389f / 27f * t + 16f) / 116f;
            float fx = F(x / 0.95047f), fy = F(y), fz = F(z / 1.08883f);
            float a = 500 * (fx - fy), b = 200 * (fy - fz);
            return (116 * fy - 16, Mathf.Sqrt(a * a + b * b));
        }
    }
}
