using System;
using Demo3D.EditorTools.Effects;
using Demo3D.Runtime.Effects;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Slots = Demo3D.Runtime.Effects.EmitterSlots;

namespace Demo3D.Tests
{
    /// <summary>TD8: the converter's scope, the CPU side of spawning, the light pool, and the runtime's shaders.</summary>
    public class EffectTests
    {
        [Test]
        public void SpikeBurstConvertsToOneAdditiveBillboardBurst()
        {
            var go = EffectConverter.SpikeBurst();
            try
            {
                var e = EffectConverter.ConvertOne(go, "{}", out var em, out var emission, out var errors);
                Assert.That(errors, Is.Empty);
                Assert.That(e.EmitterCount, Is.EqualTo(1));
                Assert.That(em.Length, Is.EqualTo(Slots.Stride));
                Assert.That(em[Slots.Life].x, Is.EqualTo(0.35f).Within(1e-5f));
                Assert.That(em[Slots.Life].y, Is.EqualTo(0.8f).Within(1e-5f));
                Assert.That(em[Slots.Render0].x, Is.EqualTo((float)Slots.RenderMode.Billboard));
                Assert.That(em[Slots.Mat1].x, Is.EqualTo((float)Slots.Blend.Additive));
                Assert.That(emission[0].Bursts.Length, Is.EqualTo(1));
                Assert.That(emission[0].Bursts[0].CountMax, Is.EqualTo(20));
                var flags = (Slots.Flags)(uint)BitConverter.SingleToInt32Bits(em[Slots.Time].w);
                Assert.That(flags.HasFlag(Slots.Flags.ColorOverLife) && flags.HasFlag(Slots.Flags.SizeOverLife), Is.True);
                Assert.That(flags.HasFlag(Slots.Flags.LocalSpace), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void UnsupportedModulesFailLoudlyUnlessDropped()
        {
            var go = EffectConverter.SpikeBurst();
            try
            {
                var col = go.GetComponent<ParticleSystem>().collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.World;
                Assert.That(EffectConverter.ConvertOne(go, "{}", out _, out _, out var errors), Is.Null);
                StringAssert.Contains("collision (World) on child 'spike-burst'", errors);
                StringAssert.Contains("fix: drop the module", errors);
                Assert.That(EffectConverter.ConvertOne(go, "{ \"drop\": [\"collision\"] }", out _, out _, out errors), Is.Not.Null);
                Assert.That(errors, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static EffectEntry Entry(float duration = 1) => new EffectEntry { Name = "t", ScaleRef = 1, Duration = duration };

        [Test]
        public void SegmentsCountBurstsRatesAndDistance()
        {
            var seg = new Vector4[4];
            var burst = new EmitterEmission { Duration = 1, Bursts = new[] { new Burst { Time = 0.1f, CountMin = 20, CountMax = 20, Cycles = 1, Probability = 1 } }, MaxParticles = 100, LifeMax = 1 };
            Assert.That(EffectSystem.Segments(burst, default, Entry(), 7, seg), Is.EqualTo(20));
            Assert.That(seg[0], Is.EqualTo(new Vector4(0, 20, 0.1f, 1)));

            var cycles = burst;
            cycles.Bursts = new[] { new Burst { Time = 0, CountMin = 5, CountMax = 5, Cycles = 3, Interval = 0.2f, Probability = 1 } };
            Assert.That(EffectSystem.Segments(cycles, default, Entry(), 7, seg), Is.EqualTo(15));
            Assert.That(seg[0].w, Is.EqualTo(0.2f).Within(1e-6f));

            var rate = new EmitterEmission { Duration = 2, RateOverTime = 10, MaxParticles = 1000, LifeMax = 1, Bursts = new Burst[0] };
            Assert.That(EffectSystem.Segments(rate, default, Entry(), 7, seg), Is.InRange(20, 21));
            Assert.That(seg[0].w, Is.EqualTo(0.1f).Within(1e-6f));

            var dist = new EmitterEmission { Duration = 1, Loop = true, RateOverDistanceMin = 4, RateOverDistanceMax = 4, MaxParticles = 1000, LifeMax = 0.5f, Bursts = new Burst[0] };
            var moving = new EffectSystem.Spawn { Velocity = new Vector3(0, 0, 5), Duration = 2 };
            Assert.That(EffectSystem.Segments(dist, moving, Entry(), 7, seg), Is.InRange(40, 41)); // 4 per m x 5 m/s x 2 s

            // Deterministic in the seed.
            var randomCount = burst;
            randomCount.Bursts = new[] { new Burst { Time = 0, CountMin = 5, CountMax = 50, Cycles = 1, Probability = 1 } };
            Assert.That(EffectSystem.Segments(randomCount, default, Entry(), 99, seg), Is.EqualTo(EffectSystem.Segments(randomCount, default, Entry(), 99, seg)));
        }

        static LightCue Cue(float intensity) => new LightCue
        {
            Color = Color.white, Range = 4, Intensity = intensity, Duration = 1, Curve = new[] { 1f, 1f },
        };

        [Test]
        public void LightsClusterOnTheGridAndCountDrops()
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0, 30, -20), Quaternion.Euler(55, 0, 0));
            var lights = new EffectLights(1);
            try
            {
                // Two cues in one 1.5 m cell merge; a third far away is its own cluster.
                lights.Add(new Vector3(0.1f, 1, 0.1f), Cue(2), 0, 1, Vector3.zero);
                lights.Add(new Vector3(0.5f, 1, 0.4f), Cue(2), 0, 1, Vector3.zero);
                lights.Add(new Vector3(8.2f, 1, 3.1f), Cue(1), 0, 1, Vector3.zero);
                lights.Update(0.5, cam);
                Assert.That(lights.ActiveCues, Is.EqualTo(3));
                Assert.That(lights.Clusters, Is.EqualTo(2));
                Assert.That(lights.VisibleClusters, Is.EqualTo(2));
                Assert.That(lights.LightsUsed, Is.EqualTo(1));
                // The pool of one keeps the strong cluster (intensity 4): 1 of 5 dropped.
                Assert.That(lights.DropShare, Is.EqualTo(0.2).Within(1e-6));
                // Expired cues leave.
                lights.Update(2.0, cam);
                Assert.That(lights.ActiveCues, Is.EqualTo(0));
                Assert.That(lights.LightsUsed, Is.EqualTo(0));
                // A cluster behind the camera is culled, not dropped.
                lights.Add(new Vector3(0, 1, -60), Cue(3), 2.0, 1, Vector3.zero);
                lights.Update(2.2, cam);
                Assert.That(lights.VisibleClusters, Is.EqualTo(0));
                Assert.That(lights.DropShare, Is.EqualTo(0));
            }
            finally
            {
                lights.Dispose();
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }

        [Test]
        public void RuntimeShadersCompile()
        {
            foreach (var name in new[] { "EffectSim", "EffectSort" })
            {
                var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>($"Assets/Demo3D/Runtime/Effects/Shaders/{name}.compute");
                Assert.That(cs, Is.Not.Null, name);
                foreach (var m in ShaderUtil.GetComputeShaderMessages(cs))
                    Assert.That(m.severity, Is.Not.EqualTo(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), $"{name}: {m.message}");
            }
            var shader = Shader.Find("Demo3D/EffectParticles");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }

        [Test]
        public void RejectionsSumCpuAndGpuIgnoreStaleReadbacksAndExcuseResets()
        {
            var r = new RejectionLedger();
            r.AddCpu(2, false);
            r.AddGpu(r.Epoch, 3, false);
            r.AddGpu(r.Epoch, 1, false);           // per-frame deltas add up, never a max
            Assert.That(r.Total, Is.EqualTo(6));
            Assert.That(r.Take(), Is.EqualTo(6));
            int old = r.Epoch;
            r.NewEpoch();                           // a seek or loop wrap
            r.AddGpu(old, 50, false);               // a readback issued before it: ignored
            Assert.That(r.Total, Is.EqualTo(6));
            r.AddCpu(7, true);                      // the reset frame's overflow: excused, logged apart
            r.AddGpu(r.Epoch, 4, true);
            Assert.That(r.Excused, Is.EqualTo(11));
            Assert.That(r.ExcusedCpuThisReset, Is.EqualTo(7));
            Assert.That(r.Take(), Is.EqualTo(0));
            r.AddGpu(r.Epoch, 1, false);            // and counting resumes after it
            Assert.That(r.Take(), Is.EqualTo(1));
            Assert.That(r.Total, Is.EqualTo(7));
        }

        [Test]
        public void SeeksRespawnOnlyEffectsStillAlive()
        {
            var fx = new EffectEntry { Name = "t", Lifetime = 0.8f, Duration = 0.3f };
            Assert.That(EffectDriver.StillAlive(fx, 5.5, 0, 6.0), Is.True);
            Assert.That(EffectDriver.StillAlive(fx, 5.0, 0, 6.0), Is.False);
            // A longer requested duration (the laser's bar) keeps it alive for longer.
            Assert.That(EffectDriver.StillAlive(fx, 4.0, 1.6f, 6.0), Is.True);
            Assert.That(EffectDriver.StillAlive(fx, 3.0, 1.6f, 6.0), Is.False);
        }

        sealed class Collect : ISpawnSink
        {
            public readonly System.Collections.Generic.List<EffectSystem.Spawn> Spawns = new System.Collections.Generic.List<EffectSystem.Spawn>();
            public bool Add(in EffectSystem.Spawn s) { Spawns.Add(s); return true; }
            public void Reset(double origin) { }
            public void Update(double now, float dt, Camera camera) { }
            public int Particles => 0;
            public void Dispose() { }
        }

        /// <summary>
        /// TD15's ambient particles: each 2 s period spawns the selection's ambientPerPeriod emitters,
        /// cycling through its list, inside the view at that moment, the same from a seek as in play.
        /// </summary>
        [Test]
        public void AmbientParticlesFillTheViewDeterministically()
        {
            var lib = ScriptableObject.CreateInstance<EffectLibrary>();
            try
            {
                lib.Effects = new[] { new EffectEntry { Name = "ambient-fireflies" }, new EffectEntry { Name = "ambient-wisps" } };
                lib.Selection = "{\"client\": {\"ambient\": [\"ambient-fireflies\", \"ambient-fireflies\", \"ambient-wisps\"], \"ambientPerPeriod\": 9}}";
                (Vector2, float) View(double t) => (new Vector2(10, -20), 40f);
                var a = new Collect();
                var amb = new AmbientEffects(lib, a);
                amb.Reset(0);
                for (int f = 0; f <= 600; f++) amb.Spawn(f / 60.0, View);
                Assert.That(a.Spawns.Count, Is.EqualTo(5 * 9), "five periods by t = 10 s");
                Assert.That(a.Spawns.FindAll(x => x.Effect == 1).Count, Is.EqualTo(5 * 3), "a third of them wisps");
                foreach (var s in a.Spawns)
                {
                    Assert.That(Mathf.Abs(s.Position.x - 10f), Is.LessThanOrEqualTo(20f));
                    Assert.That(s.Position.z, Is.InRange(-20f - 0.4f * 24f, -20f + 0.6f * 24f));
                    Assert.That(s.Duration, Is.EqualTo(2 * AmbientEffects.Period).Within(1e-5));
                }
                var b = new Collect();
                var seek = new AmbientEffects(lib, b);
                seek.Reset(6.5);
                seek.Spawn(10.0, View);
                var tail = a.Spawns.FindAll(x => x.Time > 6.5);
                Assert.That(b.Spawns.Count, Is.EqualTo(tail.Count));
                for (int k = 0; k < tail.Count; k++)
                {
                    Assert.That(b.Spawns[k].Seed, Is.EqualTo(tail[k].Seed));
                    Assert.That(b.Spawns[k].Position, Is.EqualTo(tail[k].Position));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lib);
            }
        }
    }
}
