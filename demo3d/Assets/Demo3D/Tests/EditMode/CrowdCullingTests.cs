using System.Collections.Generic;
using System.Linq;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Crowd.Bench;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Demo3D.Tests
{
    /// <summary>TD5's culling: the CPU arithmetic, and the compute pass checked against it.</summary>
    public class CrowdCullingTests
    {
        static float4[] Box(float half)
        {
            // An axis-aligned box [-half, half]³ as six inward planes.
            return new[]
            {
                new float4(1, 0, 0, half), new float4(-1, 0, 0, half), new float4(0, 1, 0, half),
                new float4(0, -1, 0, half), new float4(0, 0, 1, half), new float4(0, 0, -1, half),
            };
        }

        [Test]
        public void ASphereSweptTowardTheViewCountsAsVisible()
        {
            var box = Box(10);
            Assert.That(CrowdCulling.InsideSwept(box, new float3(0, 0, 0), float3.zero, 1), Is.True);
            Assert.That(CrowdCulling.InsideSwept(box, new float3(10.5f, 0, 0), float3.zero, 1), Is.True, "overlapping the edge");
            Assert.That(CrowdCulling.InsideSwept(box, new float3(12, 0, 0), float3.zero, 1), Is.False);
            Assert.That(CrowdCulling.InsideSwept(box, new float3(12, 0, 0), new float3(-3, 0, 0), 1), Is.True, "its shadow reaches in");
            Assert.That(CrowdCulling.InsideSwept(box, new float3(12, 0, 0), new float3(3, 0, 0), 1), Is.False, "its shadow points away");
        }

        [Test]
        public void TheShadowSweepIsTheLongestShadowAlongTheLight()
        {
            var light = math.normalize(new float3(0, -1, 1)); // 45° elevation
            float sin = CrowdCulling.SinElevation(light);
            var d = CrowdCulling.ShadowSweep(light, sin, new float3(0, 1, 0), 1);
            // The sphere's top (2 m) reaches the ground 2 m away horizontally at 45°.
            Assert.That(d.z, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(d.y, Is.EqualTo(-2f).Within(1e-4f));
            Assert.That(CrowdCulling.SinElevation(new float3(1, 0, 0)), Is.EqualTo(0.1f), "a horizon sun stays finite");
        }

        [Test]
        public void DepthBinsSplitTheRangeIntoEight()
        {
            Assert.That(CrowdCulling.Bin(10, 10, 90), Is.EqualTo(0));
            Assert.That(CrowdCulling.Bin(20.01f, 10, 90), Is.EqualTo(1));
            Assert.That(CrowdCulling.Bin(89.9f, 10, 90), Is.EqualTo(7));
            Assert.That(CrowdCulling.Bin(5, 10, 90), Is.EqualTo(0), "clamped near");
            Assert.That(CrowdCulling.Bin(500, 10, 90), Is.EqualTo(7), "clamped far");
        }

        [Test]
        public void LodSelectionHasHysteresis()
        {
            var t = new float4(60, 30, 0, 0);
            Assert.That(CrowdCulling.SelectLod(100, 0, t, 3), Is.EqualTo(0));
            Assert.That(CrowdCulling.SelectLod(57, 0, t, 3), Is.EqualTo(0), "within 10% below the threshold: stays");
            Assert.That(CrowdCulling.SelectLod(50, 0, t, 3), Is.EqualTo(1));
            Assert.That(CrowdCulling.SelectLod(63, 1, t, 3), Is.EqualTo(1), "within 10% above: stays");
            Assert.That(CrowdCulling.SelectLod(70, 1, t, 3), Is.EqualTo(0));
            Assert.That(CrowdCulling.SelectLod(10, 0, t, 3), Is.EqualTo(2));
            Assert.That(CrowdCulling.SelectLod(10, 0, t, 1), Is.EqualTo(0), "LOD0 only until TD11");
        }

        [Test]
        public void ACrossfadesSphereHoldsBothClips()
        {
            var a = new float3(1, 1, 2);
            var b = new float3(0.3f, 2, 1.5f);
            Assert.That(CrowdCulling.ClipSphere(a, b, 1), Is.EqualTo(a));
            var s = CrowdCulling.ClipSphere(a, b, 0.5f);
            Assert.That(s.y, Is.GreaterThanOrEqualTo(math.abs(s.x - a.x) + a.y - 1e-5f));
            Assert.That(s.y, Is.GreaterThanOrEqualTo(math.abs(s.x - b.x) + b.y - 1e-5f));
            Assert.That(s.z, Is.EqualTo(2));
        }

        [Test]
        public void TheScenarioPlacesCirclesInsideTheFootprint()
        {
            var quad = new[] { new Vector2(-10, 0), new Vector2(10, 0), new Vector2(15, 20), new Vector2(-15, 20) };
            var slots = CrowdScenario.Place(300, 30, quad, 6, 7);
            Assert.That(slots.Length, Is.EqualTo(330));
            for (int i = 0; i < 300; i++)
                Assert.That(CrowdScenario.EdgeDistance(quad, slots[i].Center), Is.GreaterThan(0), "on screen");
            for (int i = 300; i < 330; i++)
                Assert.That(CrowdScenario.EdgeDistance(quad, slots[i].Center), Is.LessThan(0), "just off screen");
            Assert.That(CrowdScenario.Area(quad), Is.EqualTo(500).Within(1e-3f));
        }

        [Test]
        public void TheScenarioIsContinuousWhileAMonsterWalks()
        {
            var slot = new CrowdScenario.Slot { Center = float2.zero, Radius = 4, Direction = 1, Scale = 1, Seed = 3 };
            var motion = new CrowdScenario.Motion { Walk = 0, Attack = 1, Hit = 2, Death = 3, WalkLength = 1, Window = 1.5f, DeathEnd = 1, Speed = 1.2f };
            var prev = CrowdScenario.Evaluate(slot, motion, 0, false);
            for (int f = 1; f < 600; f++)
            {
                var m = CrowdScenario.Evaluate(slot, motion, f / 60f, false);
                Assert.That(Vector3.Distance(m.Position, prev.Position), Is.LessThan(1.2f / 60f + 1e-4f));
                prev = m;
            }
        }

        /// <summary>
        /// The compute pass on the GPU against the CPU arithmetic: the same instances under the
        /// same camera land in the same depth bins and the same shadow list.
        /// </summary>
        [TearDown]
        public void CleanScene() => UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

        [Test]
        public void TheCullingPassAgreesWithTheCpu()
        {
            var cull = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.CullPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(CrowdBaker.ShaderPath);
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.CullShader = cull;
            lib.PaletteShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.PalettePath);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            var tex = new Texture2D(3, 2, TextureFormat.RGBAFloat, false, true);
            var spheres = new[] { new float3(1, 0.8f, 2), new float3(0.5f, 2f, 1) };
            lib.Types = new[]
            {
                new CrowdType
                {
                    Id = "test", Mesh = mesh, Materials = new[] { new Material(shader) }, Bones = tex, BoneCount = 1,
                    Clips = new[]
                    {
                        new CrowdClip { Role = "walk", FirstRow = 0, Frames = 2, Length = 1 / 60f, Loop = true, CenterY = 1, Radius = 0.8f, HeightM = 2 },
                        new CrowdClip { Role = "death", FirstRow = 0, Frames = 2, Length = 1 / 60f, CenterY = 0.5f, Radius = 2f, HeightM = 1 },
                    },
                },
            };
            var camGo = new GameObject("TestCamera");
            var cam = camGo.AddComponent<Camera>();
            var rt = new RenderTexture(1920, 1080, 24);
            var sunGo = new GameObject("TestSun");
            var sun = sunGo.AddComponent<Light>();
            var oldSun = RenderSettings.sun;
            var crowd = new BoneTextureCrowd(lib);
            try
            {
                cam.targetTexture = rt;
                cam.fieldOfView = 30;
                cam.nearClipPlane = 1;
                cam.farClipPlane = 400;
                var rot = Quaternion.Euler(55, 0, 0);
                cam.transform.SetPositionAndRotation(-(rot * Vector3.forward) * 42, rot);
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(30, 20, 0);
                RenderSettings.sun = sun;

                const int n = 1000;
                crowd.Initialize(new[] { "test" }, 2048);
                var rng = new Unity.Mathematics.Random(11);
                var data = new MonsterInstance[n];
                for (int i = 0; i < n; i++)
                {
                    data[i] = new MonsterInstance
                    {
                        Position = new Vector3(rng.NextFloat(-45, 45), 0, rng.NextFloat(-30, 45)),
                        Yaw = rng.NextFloat(0, 6.28f), ClipA = (uint)rng.NextInt(2), ClipB = 0, Blend = rng.NextBool() ? 1 : 0.5f,
                        Scale = rng.NextFloat(0.9f, 1.1f), Flags = i % 17 == 0 ? (uint)CrowdFlags.Hidden : (uint)CrowdFlags.Alive,
                    };
                }
                var arr = crowd.Instances(0, n);
                arr.CopyFrom(data);
                crowd.Draw(cam);

                // The CPU's answer.
                var planes = new float4[6];
                CrowdCulling.Planes(cam, new Plane[6], planes);
                var light = (float3)sun.transform.forward;
                float sin = CrowdCulling.SinElevation(light);
                var range = CrowdCulling.BinRange(cam, 5f, 2f);
                var bins = Enumerable.Range(0, CrowdCulling.Bins).Select(_ => new HashSet<int>()).ToArray();
                var shadow = new HashSet<int>();
                int borderline = 0;
                for (int i = 0; i < n; i++)
                {
                    var m = data[i];
                    if ((m.Flags & (uint)CrowdFlags.Hidden) != 0) continue;
                    var s = CrowdCulling.ClipSphere(spheres[m.ClipA], spheres[m.ClipB], m.Blend);
                    var c = (float3)m.Position + new float3(0, s.x * m.Scale, 0);
                    float r = s.y * m.Scale;
                    bool camera = CrowdCulling.InsideSwept(planes, c, float3.zero, r);
                    bool shadowed = camera || CrowdCulling.InsideSwept(planes, c, CrowdCulling.ShadowSweep(light, sin, c, r), r);
                    // Float differences can flip a sphere grazing a plane: tolerate those.
                    if (camera != CrowdCulling.InsideSwept(planes, c, float3.zero, r * 1.001f)) { borderline++; continue; }
                    if (camera)
                    {
                        float depth = math.dot(c - (float3)cam.transform.position, cam.transform.forward);
                        bins[CrowdCulling.Bin(depth, range.x, range.y)].Add(i);
                    }
                    if (shadowed) shadow.Add(i);
                }

                // The GPU's answer.
                var args = new uint[crowd.DebugArgs(0).count];
                crowd.DebugArgs(0).GetData(args);
                var list = new uint[crowd.DebugVisibleList(0).count];
                crowd.DebugVisibleList(0).GetData(list);
                var shadowArgs = new uint[crowd.DebugShadowArgs(0).count];
                crowd.DebugShadowArgs(0).GetData(shadowArgs);
                var shadowList = new uint[crowd.DebugShadowList(0).count];
                crowd.DebugShadowList(0).GetData(shadowList);
                int total = 0;
                for (int b = 0; b < CrowdCulling.Bins; b++)
                {
                    int count = (int)args[b * 5 + 1];
                    Assert.That(args[b * 5 + 4], Is.EqualTo((uint)(b * crowd.Capacity)), "each bin's command starts at its own range");
                    var gpu = new HashSet<int>(list.Skip(b * crowd.Capacity).Take(count).Select(x => (int)x));
                    Assert.That(gpu.Count, Is.EqualTo(count), $"bin {b} lists each instance once");
                    var missing = bins[b].Except(gpu).Count();
                    var extra = gpu.Except(bins[b]).Count();
                    Assert.That(missing + extra, Is.LessThanOrEqualTo(borderline), $"bin {b}: {missing} missing, {extra} extra");
                    total += count;
                }
                Assert.That(total, Is.GreaterThan(100), "a real share of the crowd is on screen");
                Assert.That(bins.Count(x => x.Count > 0), Is.GreaterThan(4), "the view spans most bins");
                var gpuShadow = new HashSet<int>(shadowList.Take((int)shadowArgs[1]).Select(x => (int)x));
                Assert.That(gpuShadow.SetEquals(shadow) || gpuShadow.Except(shadow).Count() + shadow.Except(gpuShadow).Count() <= borderline,
                    $"shadow list: {gpuShadow.Count} on the GPU, {shadow.Count} on the CPU");
                Assert.That(shadow.Count, Is.GreaterThan(total), "the shadow extension adds monsters just off screen");
            }
            finally
            {
                crowd.Dispose();
                RenderSettings.sun = oldSun;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(sunGo);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(lib.Types[0].Materials[0]);
                Object.DestroyImmediate(lib);
            }
        }
    }
}
