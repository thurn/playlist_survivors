using System.Collections.Generic;
using Champions.Client;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Champions.Tests
{
    /// <summary>
    /// The structure presenter's stand-ins: a structure stands from its spawn until its break's own frame, every kind
    /// on its footprint; breaks at one instant send one ring, a boss's none.
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
        StructurePresenter _presenter;

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
            _presenter = new StructurePresenter(_rules, Palette.Load(GameContent.Find(Application.dataPath)), _rings);
        }

        [TearDown]
        public void TearDown() => _presenter.Dispose();

        void Take(in SimEvent e)
        {
            _presenter.Take(e);
            _presenter.Flush();
        }

        [Test]
        public void AStructureStandsUntilItsBreaksFrame()
        {
            Take(SimEvent.StructureSpawn(0, 7, 1, 2, 3, 4, 0.5));
            Take(SimEvent.StructureSpawn(0, 8, 0, 1, 9, 9, 0));
            Assert.IsTrue(_presenter.TryGet(7, out var standing));
            Assert.AreEqual(new Vector3(3, 0, 4), standing);
            Assert.AreEqual(2, _presenter.Standing, "every kind stands");

            // The sim knows the break before the frame shows it.
            Take(SimEvent.StructureBreak(1.0, 7, 1, BreakCause.Blow, 1, 0, 2, 3, 4));
            _presenter.Present(0.98);
            Assert.AreEqual(0, _presenter.BrokeThisFrame);
            Assert.IsTrue(_presenter.TryGet(7, out _), "it stands until its frame");
            _presenter.Present(1.0);
            Assert.AreEqual(1, _presenter.BrokeThisFrame, "its impact frame");
            Assert.IsFalse(_presenter.TryGet(7, out _));
            _presenter.Present(1.0 + 1.0 / 60);
            Assert.AreEqual(0, _presenter.BrokeThisFrame, "a break shows once");
            Take(SimEvent.StructureGone(2.0, 8, 9, 9));
            Assert.AreEqual(0, _presenter.Standing, "unloaded");
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
