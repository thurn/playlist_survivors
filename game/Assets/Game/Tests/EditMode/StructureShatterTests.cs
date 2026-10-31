using System.Collections.Generic;
using Game.Client.Look;
using Game.Client.Loop;
using Game.Client.Structures;
using NUnit.Framework;
using PS.Game.Sim;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    /// <summary>
    /// The structures' shatter (StructureShatter) on a made-up site (no baked arena or shards needed):
    /// a 2 × 3 × 2 m block cut into its eight octants, broken from its east side at a heavy strike's
    /// throw, then regrown, presented at a fixed 60 fps as the player's `--fixed-time` frames are.
    /// </summary>
    public class StructureShatterTests
    {
        const double Fps = 60, T = 10;

        GameObject _block;
        Mesh _octant;
        StructureShards _shards;
        StructureShatter _shatter;
        Renderer _site;
        Scene _physics;
        readonly List<Bounds> _redrawn = new List<Bounds>();

        [SetUp]
        public void SetUp()
        {
            _block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_block.GetComponent<Collider>());
            _block.transform.position = new Vector3(0, 1.5f, 0);
            _block.transform.localScale = new Vector3(2, 3, 2);
            _site = _block.GetComponent<MeshRenderer>();
            var cube = _block.GetComponent<MeshFilter>().sharedMesh;

            // The unit cube's octants: half-size cubes about their own centres.
            _octant = new Mesh { name = "octant" };
            var corners = cube.vertices;
            for (int i = 0; i < corners.Length; i++) corners[i] *= 0.5f;
            _octant.vertices = corners;
            _octant.triangles = cube.triangles;
            _octant.RecalculateBounds();
            var shards = new List<StructureShards.Shard>();
            for (int i = 0; i < 8; i++)
                shards.Add(new StructureShards.Shard
                {
                    Mesh = _octant, Volume = 0.125f, Size = 0.43f,
                    Center = new Vector3(i % 2 == 0 ? -0.25f : 0.25f, (i / 2) % 2 == 0 ? -0.25f : 0.25f, i / 4 == 0 ? -0.25f : 0.25f),
                });
            _shards = ScriptableObject.CreateInstance<StructureShards>();
            _shards.Meshes = new[] { new StructureShards.Set { Source = cube, Shards = shards.ToArray() } };

            _shatter = new StructureShatter(_shards);
            _redrawn.Clear();
            _physics = EditorSceneManager.NewPreviewScene();
            _shatter.Attach(new[] { new[] { _site } }, regions => _redrawn.AddRange(regions), _physics);
        }

        [TearDown]
        public void TearDown()
        {
            _shatter.Dispose();
            EditorSceneManager.ClosePreviewScene(_physics);
            Object.DestroyImmediate(_block);
            Object.DestroyImmediate(_octant);
            Object.DestroyImmediate(_shards);
        }

        void Present(double song) => _shatter.Present(new PresentFrame { Song = song });

        void Break() =>
            _shatter.OnEvent(SimEvent.StructureBreak(T, 0, StructureHit.HeavyStrike, 1.0, 0.0, -1, 0, FeelMoments.HeavyThrowM));

        [Test]
        public void TheShardsMoveOnTheImpactFrameAndAllFlyWithin50Ms()
        {
            Break(); // the sim's events arrive ahead of their frames
            Present(T - 2 / Fps);
            Present(T - 1 / Fps);
            Assert.IsTrue(_site.enabled, "nothing shows before the impact frame");
            Assert.AreEqual(0, _shatter.Shards);
            Assert.IsEmpty(_redrawn);

            Present(T);
            Assert.IsFalse(_site.enabled, "the structure is gone on the impact frame");
            Assert.AreEqual(8, _shatter.Shards);
            Assert.That(_shatter.Flying, Is.GreaterThan(0).And.LessThan(8), "the side nearest the strike launches first, no crack front before it");
            Assert.That(_shatter.MovedM, Is.GreaterThan(0.03f), "the impact frame already shows the shards moving");
            Assert.AreEqual(1, _redrawn.Count, "the static shadows redraw where the structure stood");
            Assert.IsTrue(_redrawn[0].Contains(new Vector3(0, 1.5f, 0)));

            for (int f = 1; f <= 3; f++) Present(T + f / Fps);
            Assert.AreEqual(8, _shatter.Flying, "the whole structure flies within 50 ms");
            Assert.AreEqual(1, _redrawn.Count);
        }

        [Test]
        public void ARegrowthEruptsOnItsDownbeatAndRedrawsTheShadowsOnceWhole()
        {
            const double Start = 12, End = 12.47;
            Break();
            _shatter.OnEvent(SimEvent.Regrowth(Start - 2, 0, Start, End));
            var erupted = new List<double>();
            _shatter.Erupted += (t, site, centre, size, tint) => erupted.Add(t);
            for (int f = (int)(T * Fps); f < Start * Fps; f++) Present(f / Fps);
            Assert.IsFalse(_site.enabled);
            Assert.IsEmpty(erupted);
            _redrawn.Clear();

            Present(Start);
            Assert.IsTrue(_site.enabled, "the structure is back on its downbeat");
            Assert.AreEqual(new[] { Start }, erupted.ToArray());
            for (int f = (int)(Start * Fps) + 1; f < End * Fps - 2; f++) Present(f / Fps);
            Assert.IsEmpty(_redrawn, "no static shadow redraw while it grows");
            for (int f = (int)(End * Fps) - 2; f <= End * Fps + 2; f++) Present(f / Fps);
            Assert.AreEqual(1, _redrawn.Count, "one redraw once it stands whole");

            // The break's shards sink as it comes up through them, and the eruption's burst settles.
            for (int f = (int)(End * Fps) + 3; f < 14.5 * Fps; f++) Present(f / Fps);
            Assert.AreEqual(0, _shatter.Shards);
        }
    }
}
