using System.Collections.Generic;
using Champions.Client;
using Champions.Render;
using Champions.Render.Structures;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Champions.Tests
{
    /// <summary>
    /// The structure presenter over a synthetic cut (a box in two pieces, one edge), so it runs without the vendor files:
    /// a structure stands until its break's own frame, then its pieces fly in their own physics, sink and go; breaks at
    /// one instant send one ring, a boss's none.
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

        StructureLibrary _lib;
        StructureRules _rules;
        Scene _physics;
        Camera _camera;
        Rings _rings;
        StructurePresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            const string dir = "Assets/Champions/Render/Structures/Shaders/";
            var type = ScriptableObject.CreateInstance<StructureType>();
            type.Id = "box";
            type.Fill = BoxFill();
            type.Lines = OneLine();
            type.Pieces = new[]
            {
                new StructurePiece { Pivot = new Vector3(-0.5f, 0.5f, 0), HalfSize = new Vector3(0.5f, 0.5f, 0.5f), Volume = 1 },
                new StructurePiece { Pivot = new Vector3(0.5f, 0.5f, 0), HalfSize = new Vector3(0.5f, 0.5f, 0.5f), Volume = 1 },
            };
            type.HeightM = 1;
            type.ExtentM = new Vector2(2, 1);
            _lib = ScriptableObject.CreateInstance<StructureLibrary>();
            _lib.Types = new[] { type };
            _lib.Fill = AssetDatabase.LoadAssetAtPath<Shader>(dir + "StructureFill.shader");
            _lib.Lines = AssetDatabase.LoadAssetAtPath<Shader>(dir + "StructureLines.shader");
            _rules = new StructureRules
            {
                Kinds = new[]
                {
                    new StructureKind { Id = "ghost", Shape = FootprintShape.Circle, Radius = 1 },
                    new StructureKind { Id = "box", Shape = FootprintShape.Box, HalfX = 1, HalfY = 0.5f },
                },
            };
            _physics = EditorSceneManager.NewPreviewScene();
            _camera = new GameObject("StructureTests camera").AddComponent<Camera>();
            _rings = new Rings();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no baked cut for the structure ghost"));
            _presenter = new StructurePresenter(_lib, _rules, _rings, _physics);
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            EditorSceneManager.ClosePreviewScene(_physics);
            Object.DestroyImmediate(_camera.gameObject);
            Object.DestroyImmediate(_lib.Types[0].Fill);
            Object.DestroyImmediate(_lib.Types[0].Lines);
            Object.DestroyImmediate(_lib.Types[0]);
            Object.DestroyImmediate(_lib);
        }

        static Mesh BoxFill()
        {
            var corners = new[] { new Vector3(-1, 0, -0.5f), new Vector3(1, 0, -0.5f), new Vector3(1, 1, -0.5f), new Vector3(-1, 1, -0.5f),
                                  new Vector3(-1, 0, 0.5f), new Vector3(1, 0, 0.5f), new Vector3(1, 1, 0.5f), new Vector3(-1, 1, 0.5f) };
            int[] quads = { 0, 1, 2, 3, 5, 4, 7, 6, 4, 0, 3, 7, 1, 5, 6, 2, 3, 2, 6, 7, 4, 5, 1, 0 };
            var pos = new List<Vector3>();
            var piece = new List<Vector4>();
            var tris = new List<int>();
            for (int q = 0; q < 6; q++)
                foreach (int k in new[] { 0, 1, 2, 0, 2, 3 })
                {
                    var p = corners[quads[4 * q + k]];
                    tris.Add(pos.Count);
                    pos.Add(p);
                    piece.Add(new Vector4(p.x < 0 ? 0 : 1, 0, 0, 0));
                }
            var m = new Mesh { name = "box_fill" };
            m.SetVertices(pos);
            m.SetUVs(1, piece);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            return m;
        }

        static Mesh OneLine()
        {
            var m = new Mesh { name = "box_lines" };
            var a = new Vector3(-1, 1, 0.5f);
            var b = new Vector3(1, 1, 0.5f);
            m.SetVertices(new[] { a, a, b, b });
            m.SetUVs(0, new[] { new Vector4(b.x, b.y, b.z, -1), new Vector4(b.x, b.y, b.z, 1), new Vector4(a.x, a.y, a.z, 1), new Vector4(a.x, a.y, a.z, -1) });
            m.SetUVs(1, new[] { Vector4.one, Vector4.one, Vector4.one, Vector4.one });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            return m;
        }

        void Take(in SimEvent e)
        {
            _presenter.Take(e);
            _presenter.Flush();
        }

        [Test]
        public void AStructureStandsUntilItsBreaksFrameThenItsPiecesFlySinkAndGo()
        {
            Take(SimEvent.StructureSpawn(0, 7, 1, 2, 3, 4, 0.5));
            Take(SimEvent.StructureSpawn(0, 8, 0, 1, 9, 9, 0));
            Assert.IsTrue(_presenter.Field.TryGet(7, out _, out var standing));
            Assert.AreEqual(new Vector3(3, 0, 4), standing.Position);
            Assert.AreEqual(1, _presenter.Field.Standing, "a kind without a cut stands unseen");

            // The sim knows the break before the frame shows it.
            Take(SimEvent.StructureBreak(1.0, 7, 1, BreakCause.Blow, 1, 0, 2, 3, 4));
            _presenter.Present(0.98, _camera);
            Assert.AreEqual(0, _presenter.BrokeThisFrame);
            Assert.IsTrue(_presenter.Field.TryGet(7, out _, out _), "it stands until its frame");
            _presenter.Present(1.0, _camera);
            Assert.AreEqual(1, _presenter.BrokeThisFrame, "its impact frame");
            Assert.IsFalse(_presenter.Field.TryGet(7, out _, out _));
            Assert.AreEqual(1, _presenter.Shatter.Flying);
            Assert.AreEqual(2, _presenter.Shatter.Pieces);
            _presenter.Present(1.0 + 1.0 / 60, _camera);
            Assert.AreEqual(0, _presenter.BrokeThisFrame, "a break shows once");
            double t = 1.0;
            while (t < 1.0 + StructureShatter.LingerSeconds + StructureShatter.SinkSeconds + 0.1)
            {
                t += 1.0 / 60;
                _presenter.Present(t, _camera);
            }
            Assert.AreEqual(0, _presenter.Shatter.Flying, "sunk and gone");
            Assert.AreEqual(0, _presenter.Shatter.Pieces);
        }

        [Test]
        public void BreaksAtOneInstantSendOneRingAndABossesNone()
        {
            for (int id = 0; id < 3; id++) _presenter.Take(SimEvent.StructureSpawn(0, id, 1, 1, 2 * id, 0, 0));
            _presenter.Take(SimEvent.StructureBreak(2.0, 0, 1, BreakCause.Blow, 1, 0, 1, 0, 0));
            _presenter.Take(SimEvent.StructureBreak(2.0, 1, 1, BreakCause.Blow, 1, 0, 1, 2, 0));
            _presenter.Flush();
            Assert.AreEqual(1, _rings.Sent.Count, "one ring for the instant");
            Assert.AreEqual(RingKind.StructureBreak, _rings.Sent[0].Kind);
            Assert.AreEqual(new Vector2(1, 0), _rings.Sent[0].At, "from the breaks' middle");
            Assert.AreEqual(2.0, _rings.Sent[0].T);
            _presenter.Take(SimEvent.StructureBreak(3.0, 2, 1, BreakCause.Entrance, 1, 0, 0, 4, 0));
            _presenter.Flush();
            Assert.AreEqual(1, _rings.Sent.Count, "a boss's entrance rings for itself");
        }
    }
}
