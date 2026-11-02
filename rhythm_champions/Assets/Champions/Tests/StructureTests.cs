using System.Collections.Generic;
using System.Linq;
using Champions.Client;
using Champions.EditorTools.Structures;
using Champions.Render;
using Champions.Render.Structures;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Champions.Tests
{
    /// <summary>
    /// The structures without the vendor files: the presenter over stand-ins and over the edge look on a synthetic bake (a
    /// box baked as a Synty model would be), so a structure stands until its break's own frame, then its pieces fly in their
    /// own physics, sink and go; breaks at one instant send one ring, a boss's none; and the baker's combine stands a model
    /// on its footprint's middle at its height, its foundation dropped.
    /// </summary>
    public sealed class StructureTests
    {
        sealed class Rings : IWorldCommands
        {
            public readonly List<(RingKind Kind, Vector2 At, double T)> Sent = new List<(RingKind, Vector2, double)>();
            public void Beat(double t, double period, bool downbeat, bool reliable) { }
            public void Beatless(double t) { }
            public void Ring(RingKind kind, Vector2 at, double t, Color colour, float weight = 1f) => Sent.Add((kind, at, t));
            public void Zone(double t, int key, double fadeS) { }
            public void Duel(bool on) { }
        }

        StructureRules _rules;
        Rings _rings;
        Camera _camera;
        readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _rules = new StructureRules
            {
                Kinds = new[]
                {
                    new StructureKind { Id = "disc", Shape = FootprintShape.Circle, Radius = 1 },
                    new StructureKind { Id = "box", Shape = FootprintShape.Box, HalfX = 1, HalfY = 0.5f },
                },
            };
            _rings = new Rings();
            _camera = new GameObject("StructureTests camera").AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_camera.gameObject);
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        StructurePresenter StandIns() =>
            new StructurePresenter(_rules, new StandInStructures(Palette.Load(GameContent.Find(Application.dataPath))), _rings);

        static void Take(StructurePresenter p, in SimEvent e)
        {
            p.Take(e);
            p.Flush();
        }

        /// <summary>A box 2 m by 1 m and 1 m tall, its foundation 0.5 m under the ground, standing off the origin.</summary>
        GameObject BoxModel()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(2, 1.5f, 1);
            go.transform.position = new Vector3(3, 0.25f, -2);
            var root = new GameObject("box model");
            go.transform.SetParent(root.transform, true);
            _made.Add(root);
            return root;
        }

        [Test]
        public void TheBakersCombineStandsAModelOnItsFootprintsMiddleAtItsHeight()
        {
            var model = new StructureModel { id = "box", heightM = 2 };
            var mesh = model.Combine(BoxModel());
            _made.Add(mesh);
            var b = mesh.bounds;
            Assert.AreEqual(2f, b.max.y, 1e-4f, "its top at its height");
            Assert.AreEqual(0f, b.center.x, 1e-4f, "its middle over the origin");
            Assert.AreEqual(0f, b.center.z, 1e-4f);
            Assert.AreEqual(4f, b.size.x, 1e-3f, "scaled uniformly: twice as tall, twice as long");
            // The cube's bottom face lies wholly under the ground: it goes. Its sides cross the ground and stay.
            Assert.AreEqual(10, mesh.GetIndexCount(0) / 3);
        }

        [Test]
        public void AStructureStandsUntilItsBreaksFrameThenItsPiecesFlySinkAndGo()
        {
            var model = new StructureModel { id = "box", heightM = 1 };
            var welded = model.Combine(BoxModel());
            var type = StructureBaker.Build("box", "vendor", welded, 24, 4);
            Object.DestroyImmediate(welded);
            _made.AddRange(new Object[] { type.Body, type.ShardMesh, type });
            Assert.AreEqual(12, type.Lines, "a box draws its twelve edges, not its faces' diagonals");
            Assert.That(type.Pieces.Length, Is.InRange(2, 4));

            var physics = EditorSceneManager.NewPreviewScene();
            var view = new EdgeStructures(new StructureLibrary(new[] { type }), _rules, physics);
            var presenter = new StructurePresenter(_rules, view, _rings);
            try
            {
                Take(presenter, SimEvent.StructureSpawn(0, 7, 1, 2, 3, 4, 0.5));
                Take(presenter, SimEvent.StructureSpawn(0, 8, 0, 1, 9, 9, 0));
                Assert.AreEqual(2, presenter.Standing);
                Assert.AreEqual(1, view.Field.Standing, "a kind without a bake stands unseen");
                Assert.IsTrue(view.Field.TryGet(7, out _, out var standing));
                Assert.AreEqual(new Vector3(3, 0, 4), standing.Position);

                // The sim knows the break before the frame shows it.
                Take(presenter, SimEvent.StructureBreak(1.0, 7, 1, BreakCause.Blow, 1, 0, 2, 3, 4));
                presenter.Present(0.98, _camera);
                Assert.AreEqual(0, presenter.BrokeThisFrame);
                Assert.IsTrue(view.Field.TryGet(7, out _, out _), "it stands until its frame");
                presenter.Present(1.0, _camera);
                Assert.AreEqual(1, presenter.BrokeThisFrame, "its impact frame");
                Assert.IsFalse(view.Field.TryGet(7, out _, out _));
                Assert.AreEqual(1, view.Shatter.Flying);
                Assert.AreEqual(type.Pieces.Length, view.Shatter.Pieces);
                presenter.Present(1.0 + 1.0 / 60, _camera);
                Assert.AreEqual(0, presenter.BrokeThisFrame, "a break shows once");
                double t = 1.0;
                while (t < 1.0 + StructureShatter.LingerSeconds + StructureShatter.SinkSeconds + 0.1)
                {
                    t += 1.0 / 60;
                    presenter.Present(t, _camera);
                }
                Assert.AreEqual(0, view.Shatter.Flying, "sunk and gone");
                Assert.AreEqual(0, view.Shatter.Pieces);
                Take(presenter, SimEvent.StructureGone(5.0, 8, 9, 9));
                Assert.AreEqual(0, presenter.Standing, "unloaded");
            }
            finally
            {
                presenter.Dispose();
                EditorSceneManager.ClosePreviewScene(physics);
            }
        }

        [Test]
        public void StandInsStandUntilTheirBreaksFrame()
        {
            var presenter = StandIns();
            try
            {
                Take(presenter, SimEvent.StructureSpawn(0, 7, 1, 2, 3, 4, 0.5));
                Take(presenter, SimEvent.StructureSpawn(0, 8, 0, 1, 9, 9, 0));
                Assert.IsTrue(presenter.TryGet(7, out var standing));
                Assert.AreEqual(new Vector3(3, 0, 4), standing);
                Assert.AreEqual(2, presenter.Standing, "every kind stands");
                Take(presenter, SimEvent.StructureBreak(1.0, 7, 1, BreakCause.Blow, 1, 0, 2, 3, 4));
                presenter.Present(0.98, _camera);
                Assert.IsTrue(presenter.TryGet(7, out _), "it stands until its frame");
                presenter.Present(1.0, _camera);
                Assert.AreEqual(1, presenter.BrokeThisFrame, "its impact frame");
                Assert.IsFalse(presenter.TryGet(7, out _));
            }
            finally
            {
                presenter.Dispose();
            }
        }

        [Test]
        public void BreaksAtOneInstantSendOneRingAndABossesNone()
        {
            var presenter = StandIns();
            try
            {
                for (int id = 0; id < 3; id++) presenter.Take(SimEvent.StructureSpawn(0, id, 1, 1, 2 * id, 0, 0));
                presenter.Take(SimEvent.StructureBreak(2.0, 0, 1, BreakCause.Blow, 1, 0, 1, 0, 0));
                presenter.Take(SimEvent.StructureBreak(2.0, 1, 1, BreakCause.Blow, 1, 0, 1, 2, 0));
                presenter.Flush();
                Assert.AreEqual(1, _rings.Sent.Count, "one ring for the instant");
                Assert.AreEqual(RingKind.StructureBreak, _rings.Sent[0].Kind);
                Assert.AreEqual(new Vector2(1, 0), _rings.Sent[0].At, "from the breaks' middle");
                Assert.AreEqual(2.0, _rings.Sent[0].T);
                presenter.Take(SimEvent.StructureBreak(3.0, 2, 1, BreakCause.Entrance, 1, 0, 0, 4, 0));
                presenter.Flush();
                Assert.AreEqual(1, _rings.Sent.Count, "a boss's entrance rings for itself");
            }
            finally
            {
                presenter.Dispose();
            }
        }
    }
}
