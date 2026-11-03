using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Champions.Client.Loop;
using Champions.Client.UI;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// Click-to-move (design.md, Controls; plan.md, The Client > Input) through the run's camera into the real sim: a press
    /// gives the sim the ground point under the pointer, a held button follows the pointer one target a frame, and nothing
    /// reaches the world through the UI, a menu over the run or the cards.
    /// </summary>
    public sealed class PointerInputTests
    {
        static string ContentDir => GameContent.Find(Application.dataPath);

        /// <summary>A press's song time: its real time, 50 ms back.</summary>
        sealed class LagClock : IGameClock
        {
            public const double Lag = 0.05;
            public bool Ready => true;
            public FrameTimes Frame(in ClockReadings now) => default;
            public double KeySong(double real) => real - Lag;
            public bool TryDspAt(double song, out double dsp) { dsp = 0; return false; }
            public double EarliestDsp => 0;
        }

        GameContent _content;
        Camera _camera;
        Mouse _mouse;
        PointerInput _pointer;
        readonly LagClock _clock = new LagClock();
        readonly List<IDisposable> _disposables = new List<IDisposable>();
        GameObject _host;

        [SetUp]
        public void Make()
        {
            _content = GameContent.Load(ContentDir);
            _content.Dashes.LegacyForms = true;
            _host = new GameObject("Pointer tests");
            _camera = new GameObject("Pointer tests camera").AddComponent<Camera>();
            new WorldCamera(_camera).Update(0, Vector2.zero, Vector2.zero, 0); // the run's view of the hero at the origin
            _mouse = InputSystem.AddDevice<Mouse>("Pointer tests mouse");
            _pointer = Keep(new PointerInput());
        }

        [TearDown]
        public void Clean()
        {
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            InputSystem.RemoveDevice(_mouse);
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_camera.gameObject);
        }

        T Keep<T>(T d) where T : IDisposable
        {
            _disposables.Add(d);
            return d;
        }

        /// <summary>A run playing, a second in.</summary>
        Simulation Playing()
        {
            var sim = new Simulation(new RunInputs().Build(_content));
            sim.StepTo(1);
            return sim;
        }

        /// <summary>
        /// The mouse's state, the pointer at <paramref name="at"/> with the left button <paramref name="down"/>, at real time
        /// <paramref name="real"/>, as an event straight into the pointer input: a batch-mode editor runs no native input,
        /// so the Input System's queue never dispatches there.
        /// </summary>
        void Mouse(Vector2 at, bool down, double real)
        {
            using (StateEvent.From(_mouse, out var ev))
            {
                _mouse.position.WriteValueIntoEvent(at, ev);
                _mouse.leftButton.WriteValueIntoEvent(down ? 1f : 0f, ev);
                ev.time = real;
                _pointer.OnEvent(ev, _mouse);
            }
        }

        /// <summary>Where the camera shows ground point (<paramref name="x"/>, <paramref name="y"/>), in screen pixels.</summary>
        Vector2 Over(float x, float y) => _camera.WorldToScreenPoint(new Vector3(x, 0, y));

        /// <summary>The screen point (the Input System's, from the bottom left) over <paramref name="element"/>'s centre.</summary>
        static Vector2 Over(VisualElement element)
        {
            var panel = element.panel;
            var p = element.worldBound.center;
            var origin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            var unit = RuntimePanelUtils.ScreenToPanel(panel, Vector2.one) - origin;
            return new Vector2((p.x - origin.x) / unit.x, Screen.height - (p.y - origin.y) / unit.y);
        }

        static List<InputRecord> Targets(Simulation sim) => sim.Inputs.ToArray().Where(i => i.Kind == InputKind.Target).ToList();

        int Drain(Simulation sim, double real, bool suspended = false) => _pointer.Drain(sim, _clock, _camera, suspended, real);

        static void AssertAt(InputRecord target, float x, float y, string what)
        {
            Assert.That(target.X, Is.EqualTo(x).Within(1e-3), what);
            Assert.That(target.Y, Is.EqualTo(y).Within(1e-3), what);
        }

        [Test]
        public void APressGivesTheSimTheGroundPointUnderThePointer()
        {
            var sim = Playing();
            Mouse(Over(3, -2), true, 0.9);
            Assert.That(Drain(sim, 0.91), Is.EqualTo(1));
            var target = Targets(sim).Single();
            AssertAt(target, 3, -2, "the clicked ground point");
            Assert.That(target.Heard, Is.EqualTo(0.9 - LagClock.Lag).Within(1e-9), "heard at keySong of the press's timestamp");
            Mouse(Over(3, -2), false, 0.95);
            Assert.That(Drain(sim, 0.96), Is.Zero, "a release is no target");
            sim.StepTo(1.3);
            var s = sim.Snapshot();
            s.HeroAt(sim.Now, out double hx, out double hy);
            Assert.That(hx > 0.5 && hy < -0.3, $"the hero walks toward it: at ({hx:F2}, {hy:F2})");
        }

        [Test]
        public void HoldingTheButtonFollowsThePointerOneTargetAFrame()
        {
            var sim = Playing();
            Mouse(Over(1, 1), true, 0.9);
            Assert.That(Drain(sim, 0.91), Is.EqualTo(1), "the press's");
            Mouse(Over(2, 1), true, 0.92);
            Assert.That(Drain(sim, 0.93), Is.EqualTo(1), "a frame the pointer moved in");
            AssertAt(Targets(sim).Last(), 2, 1, "the pointer's new ground point");
            Assert.That(Targets(sim).Last().Heard, Is.EqualTo(0.93 - LagClock.Lag).Within(1e-9), "heard at the frame's time");
            Mouse(Over(3, 0), true, 0.94);
            Mouse(Over(4, -1), true, 0.945);
            Assert.That(Drain(sim, 0.95), Is.EqualTo(1), "one target a frame however often it moved");
            AssertAt(Targets(sim).Last(), 4, -1, "the latest");
            var under = Over(4, -1);
            _camera.transform.position += new Vector3(1.5f, 0, 0.5f);
            Assert.That(Drain(sim, 0.97), Is.EqualTo(1), "a frame the pointer stood still in");
            AssertAt(Targets(sim).Last(), 5.5f, -0.5f, "the ground under the still pointer, the camera moved on");
            Mouse(under, false, 0.98);
            Assert.That(Drain(sim, 0.99), Is.Zero, "let go");
            Assert.That(Targets(sim), Has.Count.EqualTo(4));
        }

        /// <summary>The run's UI with the first offer's cards up.</summary>
        RunUi CardsUp()
        {
            var ui = Keep(new RunUi(_host.transform, Palette.Load(ContentDir), _content, CardFaces.Load(ContentDir, _content)));
            var offer = new Simulation(new RunInputs { Start = RunStart.FirstOffer }.Build(_content));
            offer.StepTo(0.5);
            foreach (var e in offer.EventsSince(0)) ui.Take(e);
            return ui;
        }

        [UnityTest]
        public IEnumerator APressOverACardOrAMenuRowIsNoTarget()
        {
            var ui = CardsUp();
            var menus = Keep(new UiPanel(_host.transform, "Pointer tests menus", Flow.Order));
            var title = new TitleScreen(() => { }, () => { }, () => { });
            menus.Root.Add(title);
            yield return null; // the panels' layout
            var sim = Playing(); // the UI's cards over a world that is not held: only the pick keeps them from the ground
            var card = ui.Cards.CardAt(1);
            var row = title.Menu.LabelAt(0);
            double t = 0.9;
            foreach (var (what, over) in new[] { ("a card", card), ("a menu row", (VisualElement)row) })
            {
                Mouse(Over(over), true, t);
                Assert.That(Drain(sim, t + 0.005), Is.Zero, what);
                Mouse(Over(over) + new Vector2(0, -200), true, t + 0.01);
                Assert.That(Drain(sim, t + 0.015), Is.Zero, $"the hold from {what} onto the ground");
                Mouse(Over(over), false, t + 0.02);
                t += 0.03;
            }
            Mouse(Over(-6, 0), true, t);
            Assert.That(UiPanel.TakesPointer(Over(-6, 0)), Is.False, "the ground left of the cards and the rows");
            Assert.That(Drain(sim, t + 0.005), Is.EqualTo(1), "the ground itself");
        }

        [Test]
        public void NoTargetsWhileAMenuIsOverTheRunOrTheCardsAreUp()
        {
            var sim = Playing();
            Mouse(Over(1, 0), true, 0.9);
            Assert.That(Drain(sim, 0.91), Is.EqualTo(1));
            Assert.That(Drain(sim, 0.92, suspended: true), Is.Zero, "the pause menu opened over the hold");
            Assert.That(Drain(sim, 0.93), Is.Zero, "the button held across the menu walks nobody after it");
            Mouse(Over(1, 0), false, 0.94);
            Mouse(Over(2, 0), true, 0.95);
            Assert.That(Drain(sim, 0.96), Is.EqualTo(1), "the next press walks again");
            Mouse(Over(2, 0), false, 0.97);
            var cards = new Simulation(new RunInputs { Start = RunStart.FirstOffer }.Build(_content));
            cards.StepTo(0.5);
            Assert.That(cards.State.Offer.Up, "the first offer's cards");
            Mouse(Over(1, 0), true, 0.3);
            Assert.That(Drain(cards, 0.31) + Drain(cards, 0.32), Is.Zero, "cards up: neither the press nor its hold");
            Assert.That(Targets(cards), Is.Empty);
        }
    }
}
