using System;
using System.Collections.Generic;
using System.Linq;
using Game.Client.Crowd;
using Game.EditorTools.Crowd;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The GPU monster shatter (MonsterShatter) on a made-up one-bone monster (no baked library
    /// needed): the shard baker cuts it, and the compute passes seed and move its shards on the
    /// editor's GPU, read back. Each crowd slot's palette holds a different pose (its one bone
    /// turned and moved by the slot), so a death seeded from the wrong slot's pose shows.
    /// </summary>
    public class ShatterTests
    {
        const float HeightM = 1.8f;
        const double T = 10.0;
        const int CrowdSlots = 64;

        CrowdLibrary _lib;
        GraphicsBuffer _palette;
        MonsterShatter _shatter;
        CrowdShard[] _shards;
        int _skinCorners;

        [SetUp]
        public void SetUp()
        {
            var blob = Blob();
            _skinCorners = blob.vertexCount;
            var (mesh, shards) = CrowdShardBaker.Bake("blob", HeightM, blob, 7);
            UnityEngine.Object.DestroyImmediate(blob);
            _shards = shards;
            _lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            _lib.Types = new[] { new CrowdType { Id = "blob", BoneCount = 1, HeightM = HeightM, ShardMesh = mesh, Shards = shards } };
            _lib.ShardCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Game/Client/Crowd/Shaders/MonsterShards.compute");
            _lib.ShardShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Client/Crowd/Shaders/MonsterShard.shader");
            var rows = new Vector4[CrowdSlots * 3];
            for (int i = 0; i < CrowdSlots; i++)
            {
                var m = Pose(i);
                for (int r = 0; r < 3; r++) rows[3 * i + r] = m.GetRow(r);
            }
            _palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 16);
            _palette.SetData(rows);
            _shatter = new MonsterShatter(_lib, _ => _palette);
        }

        [TearDown]
        public void TearDown()
        {
            _shatter.Dispose();
            _palette.Dispose();
            UnityEngine.Object.DestroyImmediate(_lib.Types[0].ShardMesh);
            UnityEngine.Object.DestroyImmediate(_lib);
        }

        /// <summary>An unwelded ellipsoid standing on the ground, 1.8 m tall, on one bone (the style mesh's layout).</summary>
        static Mesh Blob()
        {
            const int rings = 16, segments = 24;
            Vector3 At(int r, int s)
            {
                float v = Mathf.PI * r / rings, u = 2 * Mathf.PI * s / segments;
                return new Vector3(0.45f * Mathf.Sin(v) * Mathf.Cos(u), 0.9f - 0.9f * Mathf.Cos(v), 0.45f * Mathf.Sin(v) * Mathf.Sin(u));
            }
            var pos = new List<Vector3>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) return;
                pos.Add(a); pos.Add(b); pos.Add(c);
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    Vector3 a = At(r, s), b = At(r + 1, s), c = At(r + 1, s + 1), d = At(r, s + 1);
                    Tri(a, c, b);
                    Tri(a, d, c);
                }
            var normals = pos.Select(p => (p - new Vector3(0, 0.9f, 0)).normalized).ToList();
            var bary = new List<Vector4>();
            for (int i = 0; i < pos.Count; i++) bary.Add(new Vector4(i % 3 == 0 ? 1 : 0, i % 3 == 1 ? 1 : 0, i % 3 == 2 ? 1 : 0, (i / 3) * 0.37f % 1f));
            var mesh = new Mesh();
            mesh.SetVertices(pos);
            mesh.SetNormals(normals);
            mesh.SetUVs(1, Enumerable.Repeat(Vector4.zero, pos.Count).ToList());
            mesh.SetUVs(2, Enumerable.Repeat(new Vector4(1, 0, 0, 0), pos.Count).ToList());
            mesh.SetUVs(3, bary);
            mesh.SetTriangles(Enumerable.Range(0, pos.Count).ToArray(), 0);
            return mesh;
        }

        static MonsterInstance Body(Vector3 at, float yaw = 0.7f, float scale = 1.5f) => new MonsterInstance
        {
            Position = at, Yaw = yaw, Scale = scale, Tint = MonsterInstance.Pack(new Color32(80, 200, 255, 255)),
            Flags = (uint)(CrowdFlags.Alive | CrowdFlags.Dying),
        };

        /// <summary>Crowd slot <paramref name="slot"/>'s pose of the one bone: a turn and a lean of its own, and a shift.</summary>
        static Matrix4x4 Pose(int slot) =>
            Matrix4x4.TRS(new Vector3(0.05f * slot, 0.02f * slot, -0.03f * slot), Quaternion.Euler(4f * slot, 17f * slot, 0), Vector3.one);

        /// <summary>Where the crowd's shader puts a root-space point of a body posed by its slot's palette.</summary>
        static Vector3 Placed(in MonsterInstance m, int slot, Vector3 p) =>
            m.Position + Quaternion.Euler(0, m.Yaw * Mathf.Rad2Deg, 0) * (Pose(slot).MultiplyPoint3x4(p) * m.Scale);

        MonsterShatter.ShardRecord[] Death(int ring) =>
            _shatter.ReadShards(0).Skip(ring * _shards.Length).Take(_shards.Length).ToArray();

        [Test]
        public void TheBakerCutsTheRestPoseIntoTwentyToFortyClosedChunksCoveringTheSkin()
        {
            Assert.That(_shards.Length, Is.InRange(CrowdShardBaker.MinShards, CrowdShardBaker.MaxShards));
            var mesh = _lib.Types[0].ShardMesh;
            var chunk = new List<Vector2>();
            mesh.GetUVs(4, chunk);
            var skin = Enumerable.Range(0, chunk.Count).Where(i => chunk[i].x < 0.5f).ToList();
            Assert.That(skin.Count, Is.EqualTo(_skinCorners), "every skin triangle in exactly one chunk");
            Assert.That(chunk.Select(c => (int)c.y).Distinct().Count(), Is.EqualTo(_shards.Length));
            Assert.That(chunk.Count, Is.GreaterThan(skin.Count), "inside faces close each chunk");
            Assert.That(_shards.All(s => s.Size > 0 && Mathf.Abs(s.Weights.x - 1) < 1e-5f), Is.True);
        }

        [Test]
        public void ShardsStartWhereTheBodyWasAndBreakOnTheImpactFrame()
        {
            var body = Body(new Vector3(3, 0, 4));
            _shatter.Break(0, 5, body, 0, 1, T, 1, 0, 1.85);
            _shatter.Frame(T, null);
            var start = Death(0);
            for (int i = 0; i < _shards.Length; i++)
            {
                var expected = Placed(body, 5, _shards[i].Pivot);
                Assert.That(Vector3.Distance(start[i].Origin, expected), Is.LessThan(1e-4f), $"shard {i}'s pivot on the pose");
                Assert.That(Vector3.Distance(start[i].Position, expected), Is.LessThan(1e-4f), $"shard {i} not yet moved at the break");
                Assert.That(Quaternion.Angle(start[i].Rotation, Quaternion.identity), Is.LessThan(0.01f));
            }
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(1));

            _shatter.Frame(T + 1 / 60.0, null);
            var next = Death(0);
            for (int i = 0; i < _shards.Length; i++)
                Assert.That(Vector3.Distance(next[i].Position, start[i].Origin), Is.GreaterThan(1e-3f), $"shard {i} moves on its first frame");
        }

        [Test]
        public void ShardsFlyAlongTheBlowBounceOnTheGroundThenSinkAndEnd()
        {
            _shatter.Break(0, 5, Body(new Vector3(-2, 0, 1)), 0, 3, T, 0, 1, 1.85);
            for (int f = 0; f <= 30; f++) _shatter.Frame(T + f / 60.0, null);
            var flying = Death(0);
            Assert.That(flying.All(s => s.Position.y >= s.Radius - 1e-4f), Is.True, "no shard under the ground while it lingers");
            float along = flying.Average(s => s.Position.z - s.Origin.z);
            Assert.That(along, Is.GreaterThan(0.3f), "kicked along the blow (+y on the ground, Unity's +z)");

            for (int f = 31; f <= 107; f++) _shatter.Frame(T + f / 60.0, null); // to 1.783 s: deep in the sink
            Assert.That(Death(0).All(s => s.Position.y < 0), Is.True, "sunk under the floor");
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(1));
            _shatter.Frame(T + MonsterShatter.LifeSeconds, null);
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(0));
        }

        [Test]
        public void AHeavierBlowThrowsTheShardsFarther()
        {
            _shatter.Break(0, 1, Body(Vector3.zero), 0, 9, T, 1, 0, 1.85);
            _shatter.Break(0, 2, Body(new Vector3(0, 0, 20)), 0, 9, T, 1, 0, 4.63);
            for (int f = 0; f <= 24; f++) _shatter.Frame(T + f / 60.0, null);
            float normal = Death(0).Average(s => s.Position.x - s.Origin.x);
            float heavy = Death(1).Average(s => s.Position.x - s.Origin.x);
            Assert.That(heavy, Is.GreaterThan(1.3f * normal));
        }

        /// <summary>Death i's crowd slot, unlike its ring slot (i), so the two can't be confused.</summary>
        static int CrowdSlotOf(int i) => (7 * i + 3) % CrowdSlots;

        [Test]
        public void FiftyDeathsInOneFrameEachBreakFromTheirOwnBody()
        {
            var bodies = new MonsterInstance[50];
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i] = Body(new Vector3(i % 10 * 2.5f, 0, i / 10 * 2.5f), yaw: i * 0.4f, scale: 0.8f + i * 0.02f);
                _shatter.Break(0, CrowdSlotOf(i), bodies[i], 0, i, T, 1, 1, 1.85);
            }
            _shatter.Frame(T, null);
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(50));
            for (int i = 0; i < bodies.Length; i++)
            {
                var shards = Death(i);
                for (int k = 0; k < _shards.Length; k += 7)
                    Assert.That(Vector3.Distance(shards[k].Origin, Placed(bodies[i], CrowdSlotOf(i), _shards[k].Pivot)), Is.LessThan(1e-4f), $"death {i} shard {k}");
            }

            // The draw skins each death from its own frozen pose, which later frames' crowd palettes
            // (its slot reused by a new monster) leave alone.
            _palette.SetData(new Vector4[CrowdSlots * 3]);
            _shatter.Frame(T + 1 / 60.0, null);
            var frozen = _shatter.ReadFrozen(0);
            for (int i = 0; i < bodies.Length; i++)
                for (int r = 0; r < 3; r++)
                    Assert.That(Vector4.Distance(frozen[3 * i + r], Pose(CrowdSlotOf(i)).GetRow(r)), Is.LessThan(1e-5f), $"death {i}'s frozen pose, row {r}");
        }

        [Test]
        public void AFullRingGivesUpItsOldestAndASeekBackClears()
        {
            for (int i = 0; i < MonsterShatter.RingCapacity + 10; i++)
            {
                _shatter.Break(0, i % CrowdSlots, Body(Vector3.zero), 0, i, T + i * 1e-3, 1, 0, 1.85);
                if (i % 20 == 19) _shatter.Frame(T + i * 1e-3, null);
            }
            _shatter.Frame(T + 0.5, null);
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(MonsterShatter.RingCapacity));
            _shatter.Frame(T - 1, null);
            Assert.That(_shatter.LiveDeaths, Is.EqualTo(0));
        }

        [Test]
        public void EachBreakRaisesItsBurstAtTheHitPoint()
        {
            var bursts = new List<ShatterBurst>();
            _shatter.Broke += bursts.Add;
            _shatter.Break(0, 3, Body(new Vector3(1, 0, 2), scale: 2f), 4, 1, T, 3, 4, 4.63);
            Assert.That(bursts.Count, Is.EqualTo(1));
            var b = bursts[0];
            Assert.That(b.Time, Is.EqualTo(T));
            Assert.That(b.Role, Is.EqualTo(4));
            Assert.That(b.Force, Is.EqualTo(4.63f).Within(1e-5));
            Assert.That(b.Height, Is.EqualTo(2f * HeightM).Within(1e-5));
            Assert.That(Vector3.Distance(b.Blow, new Vector3(0.6f, 0, 0.8f)), Is.LessThan(1e-5f));
            var hit = new Vector3(1, 0, 2) + Vector3.up * (0.6f * 3.6f) - b.Blow * (0.12f * 3.6f);
            Assert.That(Vector3.Distance(b.Hit, hit), Is.LessThan(1e-4f));
            Assert.That(b.Shards, Is.EqualTo(_shards.Length));
        }
    }
}
