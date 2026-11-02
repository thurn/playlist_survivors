using System.Linq;
using Champions.Client;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Champions.Tests
{
    /// <summary>
    /// The bosses' client (ps-6y2.17): the ground telegraphs a boss's and an elite's marks make, fill, land and fade; and
    /// the clips a boss plays, its attack's impact frame on the move's landing beat and its dive's touchdown on the mark's.
    /// </summary>
    public sealed class BossTests
    {
        static string ContentDir => GameContent.Find(Application.dataPath);

        static SimEvent Mark(int id, BossMove move, double start, double land, HitShape shape) =>
            SimEvent.BossTelegraph(new BossMark { Id = id, Move = move, Telegraph = new Telegraph(start, land, -1, -1), Shape = shape });

        [Test]
        public void ABossMarkShowsFromItsStartUntilItsLandingHasFaded()
        {
            var content = GameContent.Load(ContentDir);
            using (var marks = new TelegraphPresenter(Palette.Load(ContentDir), content.Enemies))
            {
                var camera = new GameObject("camera").AddComponent<Camera>();
                try
                {
                    var smash = Mark(1, BossMove.Smash, 2.0, 3.0, HitShape.Path(0, 0, 0, 8, 1.25));
                    marks.Take(smash);
                    Assert.AreEqual(BossMove.Smash, smash.MarkMove);
                    Assert.AreEqual(ShapeKind.Path, smash.MarkGround().Kind, "the ground a telegraph event carries");
                    Assert.AreEqual(8.0, smash.MarkGround().Y2, 1e-9);
                    marks.Present(1.9, camera);
                    Assert.AreEqual(0, marks.Marks.Showing, "not before its start");
                    marks.Present(2.5, camera);
                    Assert.AreEqual(1, marks.Marks.Showing, "filling");
                    var mark = new BossMark { Id = 1, Move = BossMove.Smash };
                    marks.Take(SimEvent.BossMove(3.0, mark, false, 0, 0, 0, 0, 0, 0));
                    marks.Present(3.1, camera);
                    Assert.AreEqual(1, marks.Marks.Showing, "flashing as it lands");
                    marks.Present(3.0 + Render.Telegraphs.TelegraphRenderer.FadeS + 0.01, camera);
                    Assert.AreEqual(0, marks.Marks.Showing, "faded");
                    // Called off: gone at once.
                    marks.Take(Mark(2, BossMove.Stomp, 4.0, 5.0, HitShape.Circle(0, 0, 6)));
                    marks.Present(4.2, camera);
                    marks.Take(SimEvent.BossMove(4.3, new BossMark { Id = 2, Move = BossMove.Stomp }, true, 0, 0, 0, 0, 0, 0));
                    marks.Present(4.3 + Render.Telegraphs.TelegraphRenderer.CallOffS + 0.01, camera);
                    Assert.AreEqual(0, marks.Marks.Showing);
                    // An ork's charge, in the warning yellow, numbered apart from the bosses'.
                    marks.Take(SimEvent.EliteTelegraph(6.0, 1, EliteMove.Charge, new Telegraph(6.0, 7.0, -1, -1), 0, 0, 1, 0));
                    marks.Take(Mark(1, BossMove.Smash, 6.0, 7.0, HitShape.Path(0, 0, 0, 8, 1.25)));
                    marks.Present(6.5, camera);
                    Assert.AreEqual(2, marks.Marks.Showing);
                }
                finally
                {
                    Object.DestroyImmediate(camera.gameObject);
                }
            }
        }

        static ClipInfo Clip(string role, float length, float impact = -1, bool loop = false, float stride = -1) =>
            new ClipInfo(role, role, length, impact, -1, loop, stride);

        static readonly ClipInfo[] Clips =
        {
            Clip("idle", 2, loop: true), Clip("walk", 1.33f, loop: true, stride: 2.13f), Clip("breath", 2.667f, 0.4f),
            Clip("tail", 2.167f, 1.15f), Clip("takeoff", 2.667f), Clip("fly", 1, loop: true), Clip("land", 2.5f, 1.27f),
            Clip("hit", 0.833f, 0.067f),
        };

        static int Role(string role) => System.Array.FindIndex(Clips, c => c.Role == role);

        [Test]
        public void AnAttacksImpactFrameLandsOnItsBeatAndADiveTouchesDownOnItsMark()
        {
            var a = new BossAnimator(Clips);
            a.Arrived(0);
            Assert.AreEqual(Role("idle"), a.At(1, 0).ClipA, "standing");
            Assert.AreEqual(Role("walk"), a.At(1.1, 1.8f).ClipA, "walking");
            a.Telegraph(BossMove.Tail, 0, 4, 5);
            var pose = a.At(5, 0);
            Assert.AreEqual(Role("tail"), pose.ClipA);
            Assert.AreEqual(1.15f, pose.TimeA, 1e-4, "the impact frame on the landing");
            Assert.AreEqual(1.15f - 1f, a.At(4, 0).TimeA, 1e-4, "a wind-up longer than its telegraph starts partway in");
            Assert.AreEqual(Role("idle"), a.At(7, 0).ClipA, "done");
            a.Telegraph(BossMove.Dive, 0, 10, 12);
            Assert.AreEqual(Role("takeoff"), a.At(10.1, 0).ClipA);
            var touch = a.At(12, 0);
            Assert.AreEqual((Role("land"), 1.27f), (touch.ClipA, touch.TimeA), "touching down on its beat");
            Assert.Less(a.At(11.5, 0).TimeA, 1.27f, "coming down before it");
            a.Stagger(14, 16);
            Assert.AreEqual(Role("hit"), a.At(14.5, 0).ClipA, "staggered");
            Assert.AreEqual(0.833f, a.At(15.5, 0).TimeA, 1e-4, "held at its end");
            Assert.AreEqual(Role("idle"), a.At(16.5, 0).ClipA);
            Assert.Less(a.At(16.55, 0).Blend, 1f, "crossfading after a change");
        }
    }
}
