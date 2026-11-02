using System.IO;
using System.Linq;
using Champions.EditorTools.EdgeCuts;
using Champions.Render;
using Champions.Render.Crowd;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Champions.Tests
{
    /// <summary>
    /// The edge crowd and its shatter on the GPU, with a synthetic cut (one bone, a tetrahedron body, one line), so
    /// they run without the vendor files: what the culling pass draws, the pose a dying monster leaves for its
    /// shards, and how the pieces fly.
    /// </summary>
    public sealed class EdgeCrowdTests
    {
        EdgeCutLibrary _lib;
        Camera _camera;

        [SetUp]
        public void MakeLibrary()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const string dir = "Assets/Champions/Render/Crowd/Shaders/";
            _lib = ScriptableObject.CreateInstance<EdgeCutLibrary>();
            _lib.Body = AssetDatabase.LoadAssetAtPath<Shader>(dir + "EdgeBody.shader");
            _lib.Lines = AssetDatabase.LoadAssetAtPath<Shader>(dir + "EdgeLines.shader");
            _lib.Cull = AssetDatabase.LoadAssetAtPath<ComputeShader>(dir + "CrowdCull.compute");
            _lib.Palette = AssetDatabase.LoadAssetAtPath<ComputeShader>(dir + "CrowdPalette.compute");
            _lib.Shards = AssetDatabase.LoadAssetAtPath<ComputeShader>(dir + "EdgeShards.compute");
            _lib.Types = new[] { Tetrahedron() };
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.transform.position = new Vector3(0, 10, -10);
            _camera.transform.LookAt(Vector3.zero);
        }

        [TearDown]
        public void Clean()
        {
            foreach (var t in _lib.Types)
            {
                Object.DestroyImmediate(t.Body);
                Object.DestroyImmediate(t.Lines);
                Object.DestroyImmediate(t.Bones);
                Object.DestroyImmediate(t);
            }
            Object.DestroyImmediate(_lib);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>A 2 m tetrahedron on one bone that never moves, with one line up its front and one piece.</summary>
        static EdgeCutType Tetrahedron()
        {
            var t = ScriptableObject.CreateInstance<EdgeCutType>();
            t.Id = "tetra";
            var body = new Mesh();
            body.SetVertices(new[] { new Vector3(-0.5f, 0, -0.3f), new Vector3(0.5f, 0, -0.3f), new Vector3(0, 0, 0.6f), new Vector3(0, 2, 0) });
            body.SetUVs(1, Enumerable.Repeat(Vector4.zero, 4).ToList());
            body.SetUVs(2, Enumerable.Repeat(new Vector4(1, 0, 0, 0), 4).ToList());
            body.SetUVs(3, Enumerable.Repeat(Vector4.zero, 4).ToList());
            body.SetTriangles(new[] { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 }, 0);
            t.Body = body;
            var lines = new Mesh();
            Vector3 a = new Vector3(0, 0, -0.3f), b = new Vector3(0, 2, 0);
            lines.SetVertices(new[] { a, a, b, b });
            lines.SetUVs(0, new[] { new Vector4(b.x, b.y, b.z, -1), new Vector4(b.x, b.y, b.z, 1), new Vector4(a.x, a.y, a.z, 1), new Vector4(a.x, a.y, a.z, -1) }.ToList());
            for (int ch = 1; ch <= 4; ch++)
                lines.SetUVs(ch, Enumerable.Repeat(ch % 2 == 0 ? new Vector4(1, 0, 0, 0) : Vector4.zero, 4).ToList());
            lines.SetUVs(5, Enumerable.Repeat(new Vector4(1, 0, 0, 0), 4).ToList());
            lines.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            t.Lines = lines;
            t.Bones = new Texture2D(3, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var identity = new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
            t.Bones.SetPixelData(identity.Concat(identity).ToArray(), 0);
            t.Bones.Apply();
            t.BoneCount = 1;
            t.Clips = new[] { new EdgeClip { Role = "walk", FirstRow = 0, Frames = 2, Length = 1 / 30f, Loop = true, CenterY = 1, Radius = 1.2f, HeightM = 2 } };
            t.CenterY = 1;
            t.Radius = 1.2f;
            t.HeightM = 2;
            t.Pieces = new[] { new EdgeShardPiece { Pivot = new Vector3(0, 1, 0), Size = 1, Bones = Vector4.zero, Weights = new Vector4(1, 0, 0, 0) } };
            return t;
        }

        static MonsterInstance At(Vector3 p, CrowdFlags flags) =>
            new MonsterInstance { Position = p, Blend = 1, Scale = 1, Tint = MonsterInstance.Pack(new Color32(90, 220, 255, 255)), Flags = (uint)flags };

        [Test]
        public void TheCrowdDrawsTheLivingInViewAndPosesTheDyingWithoutDrawingThem()
        {
            using var crowd = new EdgeCrowd(_lib, 8);
            var slots = crowd.Instances(0, 4);
            slots[0] = At(Vector3.zero, CrowdFlags.Alive);
            slots[1] = At(new Vector3(1, 0, 0), CrowdFlags.Hidden);
            slots[2] = At(new Vector3(-1, 0, 0), CrowdFlags.Dying);
            slots[3] = At(new Vector3(0, 0, -60), CrowdFlags.Alive); // behind the camera
            crowd.Draw(_camera);

            var args = new uint[10];
            crowd.Args(0).GetData(args);
            Assert.That(args[1], Is.EqualTo(1), "bodies drawn");
            Assert.That(args[6], Is.EqualTo(1), "lines drawn");
            var palette = new Vector4[8 * 3];
            crowd.Palette(0).GetData(palette);
            Assert.That(palette[2 * 3], Is.EqualTo(new Vector4(1, 0, 0, 0)), "the dying monster's pose is blended for its shards");
            Assert.That(palette[2 * 3 + 1], Is.EqualTo(new Vector4(0, 1, 0, 0)));
        }

        [Test]
        public void AKillsPiecesStartWhereItsBodyWasAndFlyAlongTheBlowAndUpThenSink()
        {
            using var crowd = new EdgeCrowd(_lib, 4);
            using var shatter = new EdgeShatter(_lib, crowd.Palette);
            var dying = At(new Vector3(5, 0, 5), CrowdFlags.Dying);
            var slots = crowd.Instances(0, 1);
            slots[0] = dying;
            crowd.Draw(_camera);
            Assert.That(shatter.Break(0, 0, dying, 1, 1.0, 1, 0, 1.85));
            shatter.Frame(1.0, _camera);

            var start = shatter.ReadShards(0)[0];
            Assert.That(Vector3.Distance(start.Position, new Vector3(5, 1, 5)), Is.LessThan(1e-4f), "the piece starts at its posed pivot");
            Assert.That(start.Velocity.x, Is.GreaterThan(0), "along the blow");
            Assert.That(start.Velocity.y, Is.GreaterThan(0), "and up");

            shatter.Frame(1.2, _camera);
            var flown = shatter.ReadShards(0)[0];
            Assert.That(flown.Position.x, Is.GreaterThan(start.Position.x));
            Assert.That(Quaternion.Angle(flown.Rotation, Quaternion.identity), Is.GreaterThan(1), "tumbling");
            Assert.That(shatter.LiveDeaths, Is.EqualTo(1));

            shatter.Frame(1.0 + EdgeShatter.LifeSeconds - 0.01, _camera);
            Assert.That(shatter.ReadShards(0)[0].Position.y, Is.LessThan(0), "sunk under the floor by the end of its life");
            shatter.Frame(1.0 + EdgeShatter.LifeSeconds + 0.01, _camera);
            Assert.That(shatter.LiveDeaths, Is.EqualTo(0));
        }

        [Test]
        public void AStillsIdPassMarksTheCrowdWithItsTypesCode()
        {
            using var crowd = new EdgeCrowd(_lib, 8);
            crowd.SetIdCode(0, IdPass.Monster(0));
            var slots = crowd.Instances(0, 1);
            slots[0] = At(Vector3.zero, CrowdFlags.Alive);
            crowd.Draw(_camera);
            string dir = Path.Combine(Path.GetTempPath(), "champions-crowd-ids-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (var stills = new Stills(_camera, dir, new double[0]) { Requeue = () => crowd.Queue(_camera) })
                    stills.Keep("crowd", new Stills.View());
                var tex = new Texture2D(2, 2);
                Assert.That(tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, "id-crowd.png"))));
                _camera.aspect = (float)Stills.Width / Stills.Height;
                var middle = _camera.WorldToViewportPoint(new Vector3(0, 1, 0));
                _camera.ResetAspect();
                var ids = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                // Viewport and texture rows both start at the bottom.
                int Code(float x, float y) => ids[(int)(y * Stills.Height) * Stills.Width + (int)(x * Stills.Width)].r;
                Assert.That(Code(middle.x, middle.y), Is.EqualTo(IdPass.Monster(0)), "the tetrahedron's middle");
                Assert.That(Code(0.02f, 0.02f), Is.EqualTo(0), "the empty corner");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void BlackShareCountsTheSilhouetteDarkerThanGroundLight()
        {
            var white = new Color32(255, 255, 255, 255);
            var none = new Color32(0, 0, 0, 255);
            var mask = new[] { white, white, white, white, none };
            var beauty = new[] { none, new Color32(20, 20, 20, 255), new Color32(40, 40, 40, 255), white, white };
            var (share, silhouette, _, _) = EdgeCutReview.BlackShare(mask, beauty, 5);
            Assert.That(silhouette, Is.EqualTo(4));
            Assert.That(share, Is.EqualTo(0.5f), "sRGB 20 is under L* 10, sRGB 40 over it");
        }
    }
}
