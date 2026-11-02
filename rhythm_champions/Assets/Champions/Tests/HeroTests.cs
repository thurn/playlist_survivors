using System;
using System.Collections.Generic;
using System.Linq;
using Champions.Client;
using Champions.Client.Hero;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Champions.Tests
{
    /// <summary>
    /// The hero's motion (design.md, The Hero &gt; Animation; Dashes) on the knight's clip annotations
    /// (<see cref="HeroClips.Knight"/>): Sword Slash's clips at natural speed with each impact frame on its beat, the legs
    /// under a twisted upper body, the dash poses and death; and the presenter's stand-in at the hero's feet.
    /// </summary>
    public sealed class HeroTests
    {
        const double Frame = 1.0 / 60, Windup = 0.35;

        static readonly ClipInfo[] Knight = HeroClips.Knight();

        static HeroMotion Motion() => new HeroMotion(new HeroClips(Knight), 6);

        /// <summary>
        /// Sword Slash's swings as the sim plans them: <paramref name="count"/> impacts <paramref name="period"/> apart, each
        /// known <paramref name="windup"/> before it, every <paramref name="spinEvery"/>th a spin, the motion shown each
        /// 60 Hz frame in between.
        /// </summary>
        static List<HeroMotion.SwingRecord> Play(HeroMotion m, double period, int count, double windup = Windup, int spinEvery = 0)
        {
            var pose = new HeroPose();
            var swings = new List<HeroMotion.SwingRecord>();
            int frame = 0;
            for (int k = 0; k < count; k++)
            {
                double impact = 1 + k * period;
                while (frame * Frame < impact - windup) m.Evaluate(frame++ * Frame, pose);
                bool spin = spinEvery > 0 && k % spinEvery == spinEvery - 1;
                swings.Add(m.Swing(spin ? SwingKind.Spin : SwingKind.Slash, impact, Vector2.right));
            }
            return swings;
        }

        static float ImpactOf(HeroMotion.SwingRecord s) => Knight[s.Clip].Impact;

        [TestCase(80, 1)]
        [TestCase(128, 2)]
        [TestCase(174, 2)]
        public void SwordSlashLandsEachImpactFrameOnItsBeatNeverFasterThanTheLimit(double bpm, int beats)
        {
            var m = Motion();
            var swings = Play(m, beats * 60 / bpm, 16, spinEvery: 4);
            for (int i = 0; i < swings.Count; i++)
            {
                var s = swings[i];
                Assert.AreEqual(ImpactOf(s), s.ClipTime(s.Impact), 1e-5, $"{bpm} BPM: swing {i}'s impact frame on its beat");
                Assert.LessOrEqual(s.Speed, HeroMotion.MaxSpeed, $"{bpm} BPM: swing {i}");
                if (s.Kind == SwingKind.Slash) Assert.AreEqual(1f, s.Speed, $"{bpm} BPM: a slash at natural speed with {Windup} s of notice");
                if (i > 0) Assert.GreaterOrEqual(s.Entry, swings[i - 1].End - 1e-9, $"{bpm} BPM: swing {i - 1} followed through first");
            }
            Assert.IsTrue(swings.Where(s => s.Kind == SwingKind.Spin).All(s => s.EntryClipTime > 0.9f),
                "the spin's flourish, longer than the notice, enters part-way");
        }

        /// <summary>A first slash at 1 s, known (and shown) <paramref name="notice"/> before.</summary>
        static HeroMotion.SwingRecord Noticed(double notice)
        {
            var m = Motion();
            m.Evaluate(1 - notice, new HeroPose());
            return m.Swing(SwingKind.Slash, 1, Vector2.right);
        }

        [Test]
        public void ShortNoticeSpeedsUpToTheLimitThenEntersPartWayAtNaturalSpeed()
        {
            var faster = Noticed(0.32);
            Assert.AreEqual(0.35 / 0.32, faster.Speed, 1e-3, "LightCombo01A's 0.35 s wind-up in 0.32 s");
            Assert.AreEqual(0f, faster.EntryClipTime, 1e-6, "from its first frame");
            Assert.AreEqual(0.35f, faster.ClipTime(faster.Impact), 1e-5);
            var late = Noticed(0.2);
            Assert.AreEqual(1f, late.Speed, "past the limit it keeps natural speed");
            Assert.Greater(late.EntryClipTime, 0.1f, "and enters part-way");
            Assert.AreEqual(0.35f, late.ClipTime(late.Impact), 1e-5);
        }

        [Test]
        public void SlashesAlternateFromLightCombo01AAndStartAgainAfterAPause()
        {
            var m = Motion();
            var pose = new HeroPose();
            var roles = new List<string>();
            foreach (double impact in new[] { 1, 1.75, 2.5, 3.25, 6, 6.75 })
            {
                m.Evaluate(impact - Windup, pose);
                roles.Add(Knight[m.Swing(SwingKind.Slash, impact, Vector2.up).Clip].Role);
            }
            CollectionAssert.AreEqual(new[] { "slashA", "slashB", "slashA", "slashB", "slashA", "slashB" }, roles);
        }

        [Test]
        public void WalkingTheLegsKeepTheRunAndTheUpperBodyTwistsToTheAim()
        {
            var m = Motion();
            var pose = new HeroPose();
            m.Walk(0, new Vector2(0, 6)); // north
            m.Evaluate(1 - Windup, pose);
            m.Swing(SwingKind.Slash, 1, Vector2.right); // east
            m.Evaluate(1, pose);
            Assert.AreEqual(1f, pose.Run);
            Assert.AreEqual(1, pose.LayerCount);
            Assert.AreEqual(0f, pose.Layers[0].Legs, "the legs keep the run");
            Assert.AreEqual(0f, pose.Yaw, 1e-4, "the legs face the travel");
            Assert.AreEqual(Math.PI / 2, pose.Twist, 1e-3, "the upper body faces the aim");
            Assert.AreEqual(1f, pose.Forward, 1e-4);

            var standing = Motion();
            standing.Evaluate(1 - Windup, pose);
            standing.Swing(SwingKind.Slash, 1, Vector2.right);
            standing.Evaluate(1, pose);
            Assert.AreEqual(1f, pose.Layers[0].Legs, "standing, the swing is the whole body");
            Assert.AreEqual(Math.PI / 2, pose.Yaw, 1e-4, "turned to the aim");
            Assert.AreEqual(0f, pose.Twist, 1e-6);
        }

        [Test]
        public void AnAimBehindTheTravelTurnsTheLegsToRunBackward()
        {
            var m = Motion();
            var pose = new HeroPose();
            m.Walk(0, new Vector2(0, 6));
            m.Evaluate(1 - Windup, pose);
            m.Swing(SwingKind.Slash, 1, Vector2.down);
            m.Evaluate(1, pose);
            Assert.AreEqual(Math.PI, Math.Abs(pose.Yaw), 1e-3, "the legs face the aim");
            Assert.AreEqual(0f, pose.Twist, 1e-3);
            Assert.AreEqual(1f, pose.Back, 1e-3, "running backward");
        }

        [Test]
        public void EachDashTakesItsPoseOnTheKeyDownFrame()
        {
            var m = Motion();
            var pose = new HeroPose();
            m.Dash(DashPose.Dash, 2, 2.15, Vector2.right, false);
            foreach (double t in new[] { 2, 2.1 })
            {
                m.Evaluate(t, pose);
                var top = pose.Layers[pose.LayerCount - 1];
                Assert.AreEqual(Array.FindIndex(Knight, c => c.Role == "dodge"), top.Clip);
                Assert.AreEqual(HeroMotion.DodgeStride, top.Time, 1e-6, "the long low stride, held");
                Assert.AreEqual(1f, top.Weight);
                Assert.AreEqual(1f, top.Legs);
                Assert.AreEqual(Math.PI / 2, pose.Yaw, 1e-4, "facing the dash at once");
                Assert.IsTrue(pose.TurnAtOnce);
            }

            m.Dash(DashPose.LeapSlam, 4, 4.35, Vector2.up, false);
            m.Evaluate(4.35, pose);
            Assert.AreEqual(1.1f, pose.Layers[pose.LayerCount - 1].Time, 1e-4, "the leap lands on the dash's end");
            m.Evaluate(4.175, pose);
            Assert.AreEqual(HeroMotion.LeapLiftM, pose.Lift, 1e-3, "at its arc's peak");

            m.Dash(DashPose.BlinkStrike, 6, 6.05, Vector2.up, false);
            m.Evaluate(6.02, pose);
            Assert.IsFalse(pose.Visible, "gone while it blinks");
            m.Evaluate(6.06, pose);
            Assert.IsTrue(pose.Visible);
            Assert.Greater(pose.Flare, 0.5f, "back with a flare");
        }

        [Test]
        public void HurtFlashesOnceAndDeathHidesTheHero()
        {
            var m = Motion();
            var pose = new HeroPose();
            m.Hurt(1);
            m.Evaluate(1, pose);
            Assert.AreEqual(1f, pose.Hurt);
            m.Evaluate(1 + HeroMotion.HurtS, pose);
            Assert.AreEqual(0f, pose.Hurt);
            m.Death(2, Vector2.zero);
            m.Evaluate(1.99, pose);
            Assert.IsTrue(pose.Visible);
            m.Evaluate(2, pose);
            Assert.IsFalse(pose.Visible, "gone on the killing frame");
        }

        [Test]
        public void TheStandInStandsAtTheFeetAndIsGoneOnTheKillingFrame()
        {
            var hero = new HeroPresenter(Palette.Load(GameContent.Find(Application.dataPath)), 6);
            try
            {
                hero.Present(1, new Vector2(2, 3), Frame);
                Assert.IsTrue(hero.Figure.activeSelf);
                Assert.AreEqual(new Vector3(2, 0, 3), hero.Figure.transform.position);
                hero.Death(2, Vector2.zero);
                hero.Present(1.99, new Vector2(2, 3), Frame);
                Assert.IsTrue(hero.Figure.activeSelf);
                hero.Present(2, new Vector2(2, 3), Frame);
                Assert.IsFalse(hero.Figure.activeSelf, "gone on the killing frame");
            }
            finally { hero.Dispose(); }
        }
    }
}
