using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Champions.EditorTools.Monsters;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The trailer's edge look as ported (plan.md, Rendering &gt; The edge look): `--look trailer` pinned to it, the
    /// feature edges its lines fall on, the shatter flinging what a monster carries, and a recipe's mesh reaching the
    /// default look only.
    /// </summary>
    public sealed class MonsterLookTests
    {
        /// <summary>
        /// --look trailer draws exactly the neon edge trailer's look: these files, as ported, and EdgeGains.Trailer. A
        /// change to the game's look goes to the default (EdgeGains.Default, a shader of its own for it), never here.
        /// </summary>
        static readonly (string File, string Sha256)[] TrailerFiles =
        {
            ("Render/Resources/Crowd/MonsterEdge.shader", "697593e859e26ac01f0bf8756d71dd4091888e83b19a7730179f2fe017aed34c"),
            ("Render/Resources/Crowd/MonsterEdgeShard.shader", "d1b06dcfa2ef93c9554e15922b4c12ef34da9b2b322518837716eeb93217c343"),
            ("Render/Crowd/Shaders/MonsterEdge.hlsl", "c1c4385e35921c700239d7bfacecce32f4b577c737beebfaf9e5ef3c3a899a75"),
            ("Render/Crowd/Shaders/MonsterLines.hlsl", "f5b0f16385b4614fc5d73504f505fe7c77f4439d0afcb1696e1c001abb1a492c"),
            ("Render/Crowd/Shaders/MonsterOutline.hlsl", "158d6f8455e9dfb9e5ca9fdcb7eab4c10ace1f47a055fa315efbe7ae2bcab09c"),
            ("Render/Crowd/Shaders/MonsterCommon.hlsl", "63ac09b7861ff693274ce6b02ae52fdfdd1e661feaaa9c552cab95246b807b50"),
            ("Render/Crowd/Shaders/CrowdCommon.hlsl", "3591455b4a4c0352093c6d2c4d62c8a4237bae654de2089fe7128323f8bd2904"),
        };

        [Test]
        public void LookTrailerIsPinnedToThePortedTrailer()
        {
            foreach (var (file, pinned) in TrailerFiles)
            {
                var bytes = File.ReadAllBytes(Path.Combine(Application.dataPath, "Champions", file));
                var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
                using var sha = SHA256.Create();
                string hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
                Assert.That(hash, Is.EqualTo(pinned), $"{file} draws --look trailer and must not change: give the default look its own shader");
            }
            var g = EdgeGains.Trailer;
            Assert.That(new[] { g.Rim, g.RimSmall, g.RimPower, g.Edge, g.EdgePx, g.Beat, g.Ring, g.CoreGlow, g.CoreFlare, g.CoreSize, g.OutlinePx, g.OutlineBack },
                Is.EqualTo(new[] { 2f, 6.5f, 4f, 2.6f, 1.25f, 0.3f, 1.1f, 0.1f, 0.35f, 0.18f, 4f, 0f }), "the trailer's body gains");
            Assert.That(new[] { g.ShardRim, g.ShardRimPower, g.ShardEdge, g.ShardEdgePx, g.ShardBeat }, Is.EqualTo(new[] { 2f, 4f, 1.4f, 1.25f, 0.6f }),
                "the trailer's shard gains");
            Assert.That(new[] { g.ToeKnee, g.ToePower }, Is.EqualTo(new[] { 0.6f, 3.2f }), "the trailer's post's void toe");
            Assert.That(g.Springs && g.Swarm, "the trailer's motion layers");
            Assert.That(StyleMesh.FeatureAngleDeg, Is.EqualTo(24f), "the trailer's crease angle");
        }

        [Test]
        public void ACubesTwelveEdgesAreItsFeatureEdgesAndItsFacesDiagonalsAreNot()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                var tris = mesh.GetIndices(0);
                var edges = StyleMesh.FeatureEdges(mesh.vertices, tris);
                Assert.That(edges.Length, Is.EqualTo(12));
                foreach (var e in edges) Assert.That(e.x + e.y + e.z, Is.EqualTo(2), "each triangle has two of the cube's edges and its face's diagonal");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        // ---------------------------------------------------------------------------------
        // The crowd and its shatter on the GPU, with a synthetic type: a tetrahedron body on one bone that never moves,
        // and a long blade beside it that it carries.

        Camera _camera;
        MonsterType _type;
        Palette _palette;

        [SetUp]
        public void MakeType()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _palette = Palette.Load(GameContent.Find(Application.dataPath));
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0, 12, -12);
            _camera.transform.LookAt(Vector3.zero);
            _type = Synthetic();
        }

        [TearDown]
        public void Clean()
        {
            if (_type != null)
            {
                foreach (var m in _type.Lods.Distinct()) Object.DestroyImmediate(m);
                Object.DestroyImmediate(_type.ShardMesh);
                Object.DestroyImmediate(_type.Bones);
                Object.DestroyImmediate(_type);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static MonsterType Synthetic()
        {
            var positions = new List<Vector3>
            {
                new Vector3(-0.5f, 0, -0.3f), new Vector3(0.5f, 0, -0.3f), new Vector3(0, 0, 0.6f), new Vector3(0, 2, 0), // the body
            };
            var tris = new List<int> { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 };
            // The blade: a box 0.1 m square and 1.6 m long, standing on its tip at the body's side.
            int b = positions.Count;
            for (int i = 0; i < 8; i++) positions.Add(new Vector3(0.8f + 0.1f * (i & 1), 1.6f * ((i >> 1) & 1), 0.1f * (i >> 2)));
            int[] box = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            tris.AddRange(box.Select(i => b + i));
            var welded = new Mesh();
            welded.SetVertices(positions);
            welded.SetUVs(1, Enumerable.Repeat(Vector4.zero, positions.Count).ToList());
            welded.SetUVs(2, Enumerable.Repeat(new Vector4(1, 0, 0, 0), positions.Count).ToList());
            welded.SetTriangles(tris, 0);
            welded.RecalculateNormals();
            var style = StyleMesh.Unweld(welded, 1);
            Object.DestroyImmediate(welded);
            var carried = Enumerable.Range(0, tris.Count / 3).Select(t => t >= 4).ToArray();
            var (shardMesh, shards) = MonsterShardBaker.Bake("synthetic", 2, style, 1, carried);
            var t = ScriptableObject.CreateInstance<MonsterType>();
            t.Id = "skeleton"; // drawn in the skeleton's palette colour
            t.Source = "vendor";
            t.Lods = new[] { style, style, style };
            t.Bones = new Texture2D(3, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var identity = new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
            t.Bones.SetPixelData(identity.Concat(identity).ToArray(), 0);
            t.Bones.Apply();
            t.BoneCount = 1;
            t.Clips = new[] { new MonsterClip { Role = "walk", FirstRow = 0, Frames = 2, Length = 1 / 30f, Loop = true, CenterY = 1, Radius = 1.5f, HeightM = 2 } };
            t.CenterY = 1;
            t.Radius = 1.5f;
            t.HeightM = 2;
            t.ShardMesh = shardMesh;
            t.Shards = shards;
            return t;
        }

        [Test]
        public void AShatterFlingsWhatTheMonsterCarriesAwayWithItsBody()
        {
            var lib = new MonsterLibrary(MonsterLook.Trailer, new[] { _type });
            using var crowd = new MonsterCrowd(lib, 4, _palette);
            using var shatter = new MonsterShatter(lib, crowd.Palette, _palette);
            var dying = new MonsterInstance { Position = new Vector3(5, 0, 5), Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Dying };
            var slots = crowd.Instances(0, 1);
            slots[0] = dying;
            crowd.Draw(_camera, 1.0);
            Assert.That(shatter.Break(0, 0, dying, 1, 1.0, 0, 1, 1.85));
            shatter.Frame(1.0, _camera);
            var start = shatter.ReadShards(0).Take(_type.Shards.Length).ToArray();
            shatter.Frame(1.6, _camera);
            var flown = shatter.ReadShards(0).Take(_type.Shards.Length).ToArray();
            shatter.Frame(1.0 + MonsterShatter.LingerSeconds - 0.05, _camera);
            var landed = shatter.ReadShards(0).Take(_type.Shards.Length).ToArray();

            var blade = Enumerable.Range(0, _type.Shards.Length).Where(i => _type.Shards[i].Carried > 0).ToList();
            Assert.That(blade, Is.Not.Empty, "the blade's chunks are marked carried");
            foreach (int i in blade)
            {
                var moved = flown[i].Position - start[i].Position;
                // Unflung, a chunk at the feet slumps where it stands (the trailer's swords and spears moved centimetres).
                Assert.That(new Vector2(moved.x, moved.z).magnitude, Is.GreaterThan(0.25f), $"blade chunk {i} flies off with the body");
                Assert.That(landed[i].Position.y, Is.LessThan(_type.Shards[i].Size), $"blade chunk {i} is down before it sinks");
            }
        }

        [Test]
        public void TheCrowdDrawsTheLivingInViewAndPosesTheDyingForTheirShards()
        {
            var lib = new MonsterLibrary(MonsterLook.Default, new[] { _type });
            using var crowd = new MonsterCrowd(lib, 8, _palette);
            var slots = crowd.Instances(0, 3);
            slots[0] = new MonsterInstance { Position = Vector3.zero, Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive };
            slots[1] = new MonsterInstance { Position = new Vector3(1, 0, 0), Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Dying };
            slots[2] = new MonsterInstance { Position = new Vector3(0, 0, -60), Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive }; // behind the camera
            crowd.Draw(_camera, 1.0);
            var palette = new Vector4[8 * 3];
            crowd.Palette(0).GetData(palette);
            Assert.That(palette[1 * 3], Is.EqualTo(new Vector4(1, 0, 0, 0)), "the dying monster's pose is blended for its shards");
            Assert.That(palette[1 * 3 + 1], Is.EqualTo(new Vector4(0, 1, 0, 0)));
            Assert.That(crowd.Written, Is.EqualTo(3));
        }

        // ---------------------------------------------------------------------------------
        // A recipe's mesh, baked from the vendor files under a root of the test's own.

        [Test]
        public void ARecipesMeshBecomesTheDefaultLookAndNeverTheTrailers()
        {
            if (!File.Exists(AssetDatabase.GUIDToAssetPath(MonsterModel.Of("slime").prefab)))
                Assert.Ignore("no vendor files: run `champions setup`");
            string root = MonsterBaker.Root;
            MonsterBaker.Root = "Assets/Baked/MonsterLookTests";
            try
            {
                // A recipe's tetrahedron on the slime's first bone.
                var dir = MonsterBaker.RecipeDir("slime");
                Directory.CreateDirectory(dir);
                void Floats(string name, float[] v) => File.WriteAllBytes(Path.Combine(dir, name), v.SelectMany(BitConverter.GetBytes).ToArray());
                void Ints(string name, int[] v) => File.WriteAllBytes(Path.Combine(dir, name), v.SelectMany(BitConverter.GetBytes).ToArray());
                Floats("positions.f32", new[] { -0.3f, 0, -0.2f, 0.3f, 0, -0.2f, 0, 0, 0.4f, 0, 0.9f, 0 });
                Ints("triangles.i32", new[] { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 });
                Ints("bones.i32", new int[16]);
                Floats("weights.f32", Enumerable.Range(0, 16).Select(i => i % 4 == 0 ? 1f : 0f).ToArray());
                File.WriteAllText(Path.Combine(dir, "mesh.json"), "{\"vertices\": 4, \"triangles\": 4}\n");

                Assert.That(MonsterBaker.Bake("slime", force: true));
                var standard = AssetDatabase.LoadAssetAtPath<MonsterType>(MonsterBaker.AssetPath("slime", MonsterLook.Default));
                var trailer = AssetDatabase.LoadAssetAtPath<MonsterType>(MonsterBaker.AssetPath("slime", MonsterLook.Trailer));
                Assert.That(standard.Source, Is.EqualTo("recipe"));
                Assert.That(standard.Lods[0].GetIndexCount(0) / 3, Is.EqualTo(4), "the default draws the recipe's tetrahedron");
                Assert.That(trailer.Source, Is.EqualTo("vendor"));
                Assert.That(trailer.Lods[0].GetIndexCount(0) / 3, Is.GreaterThan(100), "the trailer draws the vendor slime");
                Assert.That(standard.Bones, Is.SameAs(trailer.Bones), "both move with the vendor rig and clips");
            }
            finally
            {
                AssetDatabase.DeleteAsset(MonsterBaker.Root);
                if (Directory.Exists(MonsterBaker.Root)) Directory.Delete(MonsterBaker.Root, true);
                File.Delete(MonsterBaker.Root + ".meta");
                MonsterBaker.Root = root;
            }
        }
    }
}
