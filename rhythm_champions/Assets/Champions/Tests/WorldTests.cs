using System;
using Champions.Client;
using Champions.EditorTools;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>The world (design.md, The World, The Look &gt; The beat wave, Camera, Restraint, Quality tiers).</summary>
    public sealed class WorldTests
    {
        const double Period = 0.5; // 120 BPM

        static Palette Palette => Palette.Load(GameContent.Find(Application.dataPath));

        static float LStar(Color srgb)
        {
            var c = srgb.linear;
            float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return y > 216f / 24389f ? 116f * Mathf.Pow(y, 1f / 3f) - 16f : y * 24389f / 27f;
        }

        [Test]
        public void ABeatRingTravelsElevenMetresABeatFromTheHerosFeet()
        {
            var wave = new BeatWave();
            wave.Beat(10, Period, downbeat: true);
            var hero = new Vector2(3, -2);
            wave.Publish(9.99, hero);
            Assert.AreEqual(0, wave.Published, "not before its beat");
            wave.Publish(10, hero);
            Assert.AreEqual(1, wave.Published);
            Assert.AreEqual(new Vector4(3, -2, 0, 1), wave.PublishedRing(0), "from the feet, at full strength on a downbeat");
            wave.Publish(10 + Period, new Vector2(9, 9));
            var ring = wave.PublishedRing(0);
            Assert.AreEqual(new Vector2(3, -2), new Vector2(ring.x, ring.y), "placed where the feet were when it set out");
            Assert.AreEqual(BeatWave.MetresPerBeat, ring.z, 1e-3, "11 m a beat");
            wave.Publish(10 + BeatWave.LifeBeats * Period, hero);
            Assert.AreEqual(0, wave.Published, "run out");
            Assert.AreEqual(0, wave.Count, "and forgotten");
        }

        [Test]
        public void DownbeatsPushHarderThanBeats()
        {
            var wave = new BeatWave();
            wave.Beat(1, Period, downbeat: false);
            wave.Beat(1.5, Period, downbeat: true);
            wave.Publish(1, Vector2.zero);
            float beat = wave.PublishedRing(0).w;
            wave.Publish(1.5, Vector2.zero);
            float downbeat = Mathf.Max(wave.PublishedRing(0).w, wave.PublishedRing(1).w);
            Assert.AreEqual(BeatWave.BeatStrength, beat, 1e-6);
            Assert.AreEqual(1f, downbeat, 1e-6);
        }

        [TestCase(80.0)]
        [TestCase(128.0)]
        [TestCase(174.0)]
        public void TheFloorSettlesWithinHalfABeatOfTheFrontPassing(double bpm)
        {
            double period = 60 / bpm;
            var wave = new BeatWave();
            wave.Beat(0, period, downbeat: true);
            // A point 4 m out: the front reaches it at 4/11 of a beat.
            var point = new Vector2(4, 0);
            double passes = 4 / BeatWave.MetresPerBeat * period;
            wave.Publish(passes, Vector2.zero);
            float peak = wave.StrengthAt(point);
            Assert.That(peak, Is.GreaterThan(0.5f), "lit as the front passes");
            wave.Publish(passes + period / 2, Vector2.zero);
            Assert.That(wave.StrengthAt(point), Is.LessThan(0.01f * peak), "settled half a beat later");
        }

        [Test]
        public void TheWavesLiftNeverTakesTheGridOutOfGroundLight()
        {
            var material = Resources.Load<Material>(GridFloor.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.AreEqual("Champions/GridFloor", material.shader.name);
            // The value ladder: ground light is L* 10-35; the grid's lines sit at its bottom, at the zone's line colour,
            // and a downbeat's front takes them to its pulse colour, inside ground light.
            Assert.AreEqual(1f, material.GetFloat("_ToneGain"), 0.1f, "the palette's colours land as the palette says");
            foreach (var zone in Palette.Zones)
            {
                Assert.That(LStar(zone.Line), Is.InRange(8f, 20f), zone.Name + "'s quiet lines");
                Assert.That(LStar(zone.Pulse), Is.LessThanOrEqualTo(35f), zone.Name + "'s lines under a downbeat's front");
            }
            // Two rings overlapping never sum past a downbeat's front: the shader saturates the wave.
            var wave = new BeatWave();
            wave.Beat(0, Period, true);
            wave.Beat(0, Period, true);
            wave.Publish(0.1, Vector2.zero);
            Assert.That(wave.StrengthAt(new Vector2(wave.PublishedRing(0).z, 0)), Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void BeatlessThereAreNoBeatRings()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var world = new World(camera, Palette);
            try
            {
                world.Beat(1, Period, downbeat: true, reliable: false);
                world.Present(1.1, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(0, world.Wave.Published, "an unreliable beat sends no ring");
                world.Ring(RingKind.DashOnBeat, Vector2.zero, 1.2, Color.white);
                world.Present(1.25, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(1, world.Rings.Published, "event rings still go out");
                Assert.AreEqual(0, world.Wave.Published);
            }
            finally
            {
                world.Dispose();
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void OnlySpecialMomentsBendTheGridAndFlashBriefly()
        {
            var field = new ImpulseField();
            int moments = 0;
            field.Moments = r => moments++;
            field.Add(ImpulseField.Ring(RingKind.Slam, Vector2.zero, 2, Color.white));
            var calm = ImpulseField.Ring(RingKind.Slam, new Vector2(20, 0), 2, Color.white);
            calm.Special = false;
            field.Add(calm);
            field.Publish(2);
            Assert.AreEqual(1, moments, "the special ring's cue, once");
            Assert.AreEqual(1f, field.Special, 1e-6, "full on its first frame");
            float special = 0, quiet = 0;
            for (int i = 0; i < field.Published; i++)
            {
                if (field.PublishedRing(i).x > 10) quiet = field.PublishedColour(i).w;
                else special = field.PublishedColour(i).w;
            }
            Assert.AreEqual(ImpulseField.CalmRing * special, quiet, 1e-5, "a calm ring is drawn under the grid's knee");
            var knee = Resources.Load<Material>(GridFloor.MaterialPath).GetFloat("_RingKnee");
            Assert.That(ImpulseField.CalmRing * 0.6f, Is.LessThan(knee), "a calm dash's ring doesn't bend the grid");
            field.Publish(2 + 0.1);
            Assert.AreEqual(1, moments);
            field.Publish(2 + ImpulseField.SpecialSeconds);
            Assert.AreEqual(0f, field.Special, "the flash is gone within a quarter second");
        }

        [Test]
        public void AMassKillIsAFewStrongRingsNotHundreds()
        {
            var field = new ImpulseField();
            for (int i = 0; i < 200; i++) field.Add(ImpulseField.Ring(RingKind.MassKill, new Vector2(i % 3 * 0.5f, i % 5 * 0.4f), 3, Color.white));
            Assert.AreEqual(1, field.Count);
            Assert.That(field[0].Strength, Is.LessThanOrEqualTo(ImpulseField.MergedCap));
        }

        [Test]
        public void AZoneChangeSweepsOutFromTheHeroOverItsBeat()
        {
            var wave = new BeatWave();
            var zones = new ZoneSweep(Palette.Zones);
            var hero = new Vector2(5, 5);
            zones.Change(12, 1, Period, wave);
            zones.Publish(11.9, hero);
            Assert.AreEqual(0, zones.Key, "not before its beat");
            Assert.AreEqual(1f, zones.NewAt(hero));
            zones.Publish(12 + Period / 2, hero);
            Assert.AreEqual(1, zones.Key);
            Assert.AreEqual(0, zones.Previous);
            Assert.AreEqual(ZoneSweep.ReachM / 2, zones.Radius, 1e-3, "halfway out at half the beat");
            Assert.AreEqual(1f, zones.NewAt(hero), "the new key behind the front");
            Assert.AreEqual(0f, zones.NewAt(hero + new Vector2(ZoneSweep.ReachM, 0)), "the old one ahead of it");
            wave.Publish(12 + Period / 2, hero);
            Assert.AreEqual(1, wave.Published, "the wave carries the front");
            Assert.AreEqual(zones.Radius, wave.PublishedRing(0).z, 1e-3);
            zones.Publish(12 + Period + 0.1, new Vector2(40, 40));
            Assert.That(float.IsPositiveInfinity(zones.Radius), "done: the new key everywhere");
            Assert.AreEqual(1f, zones.NewAt(new Vector2(500, 0)));
        }

        [Test]
        public void TheCameraLooksSixtyDegreesDownLeadsTheHeroAndEasesOutForDuels()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            try
            {
                var rig = new WorldCamera(camera);
                rig.Update(0, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(60f, camera.transform.eulerAngles.x, 1e-3);
                Assert.AreEqual(0f, camera.transform.eulerAngles.y, 1e-3, "it never rotates");
                Assert.AreEqual(WorldCamera.DistanceM, Vector3.Distance(camera.transform.position, Vector3.zero), 1e-3);
                // Walking east at 6 m/s for two seconds: the view leads the hero east.
                var hero = Vector2.zero;
                for (int i = 1; i <= 120; i++)
                {
                    hero += new Vector2(6f / 60f, 0);
                    rig.Update(i / 60.0, hero, new Vector2(6, 0), 1 / 60f);
                }
                Assert.That(rig.Focus.x - hero.x, Is.InRange(1.2f, WorldCamera.MaxLeadM), "a slight lead the way it moves");
                Assert.AreEqual(0f, rig.Focus.y - hero.y, 1e-3);
                rig.Duel(true);
                for (int i = 0; i < 120; i++) rig.Update(2 + i / 60.0, hero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(WorldCamera.DistanceM * 1.2f, rig.Distance, 1e-3, "eased out 20% for the duel");
                rig.Duel(false);
                for (int i = 0; i < 120; i++) rig.Update(4 + i / 60.0, hero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(WorldCamera.DistanceM, rig.Distance, 1e-3);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void TheCameraShakesOnlyOnSpecialMomentsAndBriefly()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            try
            {
                var rig = new WorldCamera(camera);
                rig.Update(0, Vector2.zero, Vector2.zero, 1 / 60f);
                var rest = camera.transform.position;
                rig.Update(1, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.AreEqual(rest, camera.transform.position, "calm: still");
                rig.Special(1.5, 1f);
                rig.Update(1.55, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.That(Vector3.Distance(rest, camera.transform.position), Is.GreaterThan(0.01f), "a special moment kicks it");
                rig.Update(1.5 + WorldCamera.KickS, Vector2.zero, Vector2.zero, 1 / 60f);
                Assert.That(Vector3.Distance(rest, camera.transform.position), Is.LessThan(1e-4f), "gone within a quarter second");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void TheFloorFollowsTheViewForever()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var floor = new GridFloor();
            try
            {
                var rig = new WorldCamera(camera);
                var far = new Vector2(10_000, -7_000);
                rig.Update(0, far, Vector2.zero, 1 / 60f);
                floor.Follow(camera);
                var p = floor.GameObject.transform.position;
                Assert.That(Mathf.Abs(p.x - far.x), Is.LessThanOrEqualTo(GridFloor.SnapM));
                Assert.That(Mathf.Abs(p.z - far.y), Is.LessThanOrEqualTo(GridFloor.SnapM));
                Assert.AreEqual(0f, p.x % GridFloor.SnapM, 1e-3, "on the grid's major cells, so the lines don't swim");
            }
            finally
            {
                floor.Dispose();
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test]
        public void TheTiersFollowThePlansTable()
        {
            var low = QualityTiers.Of(Tier.Low);
            var medium = QualityTiers.Of(Tier.Medium);
            var high = QualityTiers.Of(Tier.High);
            Assert.AreEqual((1, 2, 4), (low.Msaa, medium.Msaa, high.Msaa), "MSAA off, 2x, 4x");
            Assert.AreEqual((600, 1500, 3000), (low.ShardPieces, medium.ShardPieces, high.ShardPieces));
            Assert.AreEqual(BloomDownscaleMode.Quarter, low.BloomDownscale, "half resolution");
            Assert.AreEqual(BloomDownscaleMode.Half, medium.BloomDownscale, "full");
            Assert.AreEqual(BloomDownscaleMode.Half, high.BloomDownscale);
            Assert.That(high.BloomScatter, Is.GreaterThan(medium.BloomScatter), "wider");
            Assert.That(!low.ChromaticFlashes && !low.LineHalos && !low.SlashDistortion);
            Assert.That(!medium.ChromaticFlashes && !medium.LineHalos && !medium.SlashDistortion);
            Assert.That(high.ChromaticFlashes && high.LineHalos && high.SlashDistortion);
            Assert.AreEqual(Tier.High, QualityTiers.ForGpu("Apple M5 Max"));
            Assert.AreEqual(Tier.Medium, QualityTiers.ForGpu("Apple M3 Pro"));
            Assert.AreEqual(Tier.Low, QualityTiers.ForGpu("Apple M1"));
            Assert.That(QualityTiers.TryParse("LOW", out var t) && t == Tier.Low);
            Assert.That(QualityTiers.TryParse("ultra", out _), Is.False);
        }

        [Test]
        public void SwitchingTiersChangesTheirSettingsAndKeepsTheCoreLook()
        {
            var pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Tasks.PipelinePath));
            var post = new WorldPost();
            try
            {
                pipeline.volumeProfile.TryGet<Bloom>(out var basis);
                foreach (Tier tier in Enum.GetValues(typeof(Tier)))
                {
                    var s = QualityTiers.Of(tier);
                    QualityTiers.ApplyTo(pipeline, tier);
                    post.Apply(s);
                    Assert.AreEqual(Math.Max(1, s.Msaa), pipeline.msaaSampleCount, tier.ToString());
                    Assert.AreEqual(s.BloomDownscale, post.Bloom.downscale.value);
                    Assert.AreEqual(s.BloomScatter, post.Bloom.scatter.value);
                    Assert.AreEqual(s.BloomIterations, post.Bloom.maxIterations.value);
                    // The core look: bloom on, at the pipeline's threshold and intensity, on every tier.
                    Assert.That(post.Bloom.active && post.Bloom.intensity.overrideState);
                    Assert.AreEqual(basis.intensity.value, post.Bloom.intensity.value);
                    Assert.AreEqual(basis.threshold.value, post.Bloom.threshold.value);
                }
            }
            finally
            {
                post.Dispose();
                Object.DestroyImmediate(pipeline);
            }
        }

        [Test]
        public void TheFlashFollowsSpecialMomentsOnly()
        {
            var post = new WorldPost();
            try
            {
                post.Apply(QualityTiers.Of(Tier.High));
                post.Present(0);
                Assert.That(post.Profile.TryGet<ColorAdjustments>(out var colour));
                Assert.AreEqual(0f, colour.postExposure.value);
                post.Present(1);
                Assert.That(post.FlashEv, Is.InRange(0.1f, 0.5f), "a lift, never a white-out");
                Assert.AreEqual(post.FlashEv, colour.postExposure.value, 1e-6);
                Assert.That(post.ChromaticFlash, Is.GreaterThan(0f), "the flash profile keeps the chromatic variants in builds");
                Assert.That(post.Profile.TryGet<ChromaticAberration>(out var chromatic) && chromatic.active);
                post.Apply(QualityTiers.Of(Tier.Low));
                Assert.That(chromatic.active, Is.False, "chromatic flashes are a high-tier extra");
            }
            finally
            {
                post.Dispose();
            }
        }

        [TestCase("Assets/Champions/Render/World/Shaders/GridFloor.shader")]
        [TestCase("Assets/Champions/Render/World/Shaders/Glow.shader")]
        [TestCase("Assets/Champions/Render/Crowd/Shaders/EdgeLines.shader")]
        [TestCase("Assets/Champions/Render/Crowd/Shaders/EdgeBody.shader")]
        [TestCase("Assets/Champions/Render/Structures/Shaders/StructureFill.shader")]
        [TestCase("Assets/Champions/Render/Structures/Shaders/StructureLines.shader")]
        public void TheShadersReadingTheWorldCompile(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Assert.That(shader, Is.Not.Null, path);
            var messages = ShaderUtil.GetShaderMessages(shader);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False,
                string.Join("; ", Array.ConvertAll(messages, m => $"{m.message} ({m.file}:{m.line})")));
        }

        [Test]
        public void TheSimsZonesAreThePalettes()
        {
            var zones = GameContent.Load(GameContent.Find(Application.dataPath)).Zones;
            Assert.AreEqual(Palette.Zones.Length, zones.Keys, "a zone key per palette zone");
            Assert.AreEqual(Palette.WarmZone, zones.Warm, "the dragon's key is the warm zone");
            Assert.AreEqual(0, zones.Cycle[0], "the run starts on the key's zone");
        }

        [Test]
        public void LaunchOptionsReadTheTier()
        {
            var o = LaunchOptions.Parse(new[] { "player", "--tier", "medium" }, batchMode: true);
            Assert.AreEqual(Tier.Medium, o.Tier);
            Assert.That(o.Unrecognized, Is.Empty);
            Assert.IsNull(LaunchOptions.Parse(new[] { "player" }, false).Tier);
            Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "player", "--tier", "ultra" }, false));
        }
    }
}
