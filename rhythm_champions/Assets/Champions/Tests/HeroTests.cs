using System;
using System.Collections.Generic;
using System.Linq;
using Champions.Client;
using Champions.Client.Hero;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The hero's motion and rig (design.md, The Hero &gt; Animation; Dashes) on the knight's clip annotations
    /// (<see cref="HeroClips.Knight"/>) and a three-bone stand-in rig, so they run without the vendor files: Sword Slash's
    /// clips at natural speed with each impact frame on its beat, the legs under a twisted upper body, the dash poses and
    /// death, the rig's composing, twisting and layering; and the presenter at the hero's feet, broken on its killing frame.
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
        public void TheHeroStandsAtItsFeetAndBreaksOnceOnTheKillingFrame()
        {
            var hero = new HeroPresenter(Palette.Load(GameContent.Find(Application.dataPath)), 6);
            try
            {
                hero.Present(1, new Vector2(2, 3), Frame, null);
                Assert.IsTrue(hero.Drawn);
                Assert.AreEqual(new Vector3(2, 0, 3), hero.Instance.Position);
                if (!hero.Baked) Assert.AreEqual(new Vector3(2, 0, 3), hero.StandInRoot.transform.position, "the stand-in, with no baked hero");
                hero.Death(2, Vector2.zero);
                hero.Present(1.99, new Vector2(2, 3), Frame, null);
                Assert.IsTrue(hero.Drawn);
                hero.Present(2, new Vector2(2, 3), Frame, null);
                Assert.IsFalse(hero.Drawn, "gone on the killing frame");
                hero.Present(2 + Frame, new Vector2(2, 3), Frame, null);
                Assert.IsFalse(hero.Drawn);
                if (hero.Baked)
                {
                    Assert.That(hero.Shatter.Breaks, Is.EqualTo(1), "its white shards, once");
                    Assert.That(hero.Shatter.LiveShards, Is.GreaterThan(0));
                }
                else Assert.IsFalse(hero.StandInRoot.activeSelf);
            }
            finally { hero.Dispose(); }
        }

        // ---- The rig: hips (legs), a spine bone and an arm, one entry each, binds from the rest pose ----

        static readonly Vector3[] Rest = { new Vector3(0, 1, 0), new Vector3(0, 0.3f, 0), new Vector3(0.3f, 0.3f, 0) };

        /// <summary>A rig of two clips: the rest pose, and <paramref name="other"/>'s local rotations on the same offsets.</summary>
        static MonsterRig Rig(params Quaternion[] other)
        {
            var rig = ScriptableObject.CreateInstance<MonsterRig>();
            rig.name = "stand-in";
            rig.Nodes = new[] { "Hips", "Spine", "Arm" };
            rig.Parents = new[] { -1, 0, 1 };
            rig.Layers = new[] { (byte)RigLayer.Legs, (byte)RigLayer.Spine, (byte)RigLayer.Arms };
            rig.Scales = new[] { Vector3.one, Vector3.one, Vector3.one };
            rig.Entries = new[] { 0, 1, 2 };
            rig.Binds = new Matrix4x4[3];
            var world = Vector3.zero;
            for (int n = 0; n < 3; n++) rig.Binds[n] = Matrix4x4.Translate(-(world += Rest[n]));
            rig.SpineRoot = 1;
            rig.Clips = new[]
            {
                new RigClip { Role = "rest", FirstFrame = 0, Frames = 2, Length = 1 },
                new RigClip { Role = "other", FirstFrame = 2, Frames = 2, Length = 1 },
            };
            var frames = new List<float>();
            for (int f = 0; f < 4; f++)
                for (int n = 0; n < 3; n++)
                {
                    var q = f < 2 ? Quaternion.identity : other[n];
                    frames.AddRange(new[] { q.x, q.y, q.z, q.w, Rest[n].x, Rest[n].y, Rest[n].z });
                }
            rig.Frames = frames.ToArray();
            return rig;
        }

        static Vector3 Skin(Vector4[] rows, int entry, Vector3 p)
        {
            var m = Matrix4x4.identity;
            for (int r = 0; r < 3; r++) m.SetRow(r, rows[3 * entry + r]);
            return m.MultiplyPoint3x4(p);
        }

        static readonly Vector3 Hand = new Vector3(0.3f, 1.6f, 0);

        [Test]
        public void TheRestPoseComposesToTheRestAndTheTwistTurnsTheUpperBodyAboutTheVertical()
        {
            var rig = Rig(Quaternion.identity, Quaternion.identity, Quaternion.identity);
            try
            {
                var pose = new RigPose(rig);
                var rows = new Vector4[9];
                pose.Sample(0, 0.5);
                pose.Palette(rows, 0);
                Assert.That(Vector3.Distance(Hand, Skin(rows, 2, Hand)), Is.LessThan(1e-5f), "the rest pose skins to itself");
                pose.Palette(rows, Mathf.PI / 2);
                // A quarter turn clockwise from above about the spine bone (at 1.3 m): the hand, 0.3 m to its right, goes behind it.
                Assert.That(Vector3.Distance(new Vector3(0, 1.6f, -0.3f), Skin(rows, 2, Hand)), Is.LessThan(1e-4f), "the arm turned");
                Assert.That(Vector3.Distance(Vector3.up, Skin(rows, 0, Vector3.up)), Is.LessThan(1e-5f), "the hips not");
            }
            finally { Object.DestroyImmediate(rig); }
        }

        [Test]
        public void BladeExtensionKeepsTheGripInThePosedHand()
        {
            var rig = Rig(Quaternion.identity, Quaternion.Euler(0, 45, 0), Quaternion.Euler(25, 0, 65));
            try
            {
                var pose = new RigPose(rig);
                var rows = new Vector4[9];
                pose.Sample(1, .5); pose.Palette(rows, .4f);
                var grip = Hand + Vector3.up * HeroPresenter.GripY;
                var tip = Hand + Vector3.up * (HeroPresenter.GuardY + HeroPresenter.BladeM);
                var fist = Skin(rows, 2, grip);
                var reach = Vector3.Distance(fist, Skin(rows, 2, tip));
                pose.Extend(rows, 2, HeroPresenter.GripY, HeroPresenter.GuardY + HeroPresenter.BladeM, 3);
                Assert.That(Vector3.Distance(fist, Skin(rows, 2, grip)), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(fist, Skin(rows, 2, tip)), Is.EqualTo(reach + 3).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(rig); }
        }

        [Test]
        public void AClipLaidOverTheLegsKeepsItsSpinesTurnWhateverItsHipsDo()
        {
            // The clip turns its hips 45 degrees and its spine back again: its upper body faces ahead.
            var rig = Rig(Quaternion.Euler(0, 45, 0), Quaternion.Euler(0, -45, 0), Quaternion.identity);
            try
            {
                var basePose = new RigPose(rig);
                var clip = new RigPose(rig);
                var rows = new Vector4[9];
                basePose.Sample(0, 0);
                clip.Sample(1, 0);
                basePose.Blend(clip, 0, 1, 1); // the legs keep the base's hips
                basePose.Palette(rows, 0);
                Assert.That(Vector3.Distance(Hand, Skin(rows, 2, Hand)), Is.LessThan(1e-4f), "the upper body still faces ahead");

                var whole = new RigPose(rig);
                whole.Sample(0, 0);
                whole.Blend(clip, 1, 1, 1);
                whole.Palette(rows, 0);
                Assert.That(Vector3.Distance(Hand, Skin(rows, 2, Hand)), Is.LessThan(1e-4f), "and so it does as the whole body");
            }
            finally { Object.DestroyImmediate(rig); }
        }
        [TestCase(false)] [TestCase(true)]
        public void WalkingCutReachesFromTheGripAtImpactAndRecoversAcrossPause(bool backhand)
        {
            var m = Motion(); var p = new HeroPose();
            m.Walk(0, Vector2.up * 6); m.Evaluate(.98, p);
            m.Swing(SwingKind.Slash, 1.12, Vector2.right);
            m.WalkingCleave(1, 1.15, Vector2.up, 4.2f, backhand);
            m.Evaluate(1.05, p);
            Assert.That(p.BladeExtension, Is.GreaterThan(0));
            m.Hold(1.05, true); m.Hold(2.05, false); m.Evaluate(2.15, p);
            Assert.That(p.Run, Is.EqualTo(1));
            Assert.That(p.BladeExtension, Is.EqualTo(2.45f).Within(.001f));
            Assert.That(p.Layers.Take(p.LayerCount).Select(l => l.Clip),
                Is.EqualTo(new[] { backhand ? m.Clips.SlashB.Index : m.Clips.SlashA.Index }));
            m.Evaluate(2.38, p);
            Assert.That(p.BladeExtension, Is.Zero);
            var next = m.Swing(SwingKind.Slash, 2.40, Vector2.up);
            m.Evaluate(2.40, p);
            Assert.That(p.Layers.Take(p.LayerCount).Last().Time, Is.EqualTo(Knight[next.Clip].Impact).Within(1e-5));
        }

        [TestCase(true)] [TestCase(false)]
        public void LandingSpinOverridesAnAutomaticWindupAndOwnsTheExitAtNaturalSpeed(bool travel)
        {
            var m = Motion(); var pose = new HeroPose();
            m.Swing(SwingKind.Slash, 2.5, Vector2.right);
            m.Evaluate(2.35, pose);
            if (travel) m.Dash(DashPose.Dash, 2.4, 2.55, Vector2.up, false);
            m.LandingSpin(2.4, 2.55, Vector2.up);
            var spin = m.SwingAt(m.SwingCount - 1);
            Assert.That(spin.Speed, Is.EqualTo(1));
            Assert.That(spin.ClipTime(2.55), Is.EqualTo(m.Clips.Spin.Impact).Within(1e-6));
            m.Evaluate(2.55, pose);
            Assert.That(pose.Layers.Take(pose.LayerCount).Any(l => l.Clip == m.Clips.Spin.Index), Is.True);
            Assert.That(pose.Layers.Take(pose.LayerCount).Any(l => l.Clip == m.Clips.Dodge.Index), Is.False);
            Assert.That(pose.Layers.Take(pose.LayerCount).Any(l => l.Clip == m.Clips.SlashA.Index), Is.False);
        }
        [TestCase(true)] [TestCase(false)]
        public void PausePreservesThePendingDashExitPose(bool travel)
        {
            var m = Motion(); var pose = new HeroPose();
            if (travel) m.Dash(DashPose.Dash, 1, 1.15, Vector2.up, false);
            m.LandingSpin(1, 1.15, Vector2.up);
            m.Hold(1.1, true); m.Evaluate(10, pose);
            m.Hold(11.1, false);
            m.Evaluate(11.15, pose);
            var spin = m.SwingAt(m.SwingCount - 1);
            Assert.That(spin.Impact, Is.EqualTo(11.15).Within(1e-9));
            Assert.That(spin.ClipTime(11.15), Is.EqualTo(m.Clips.Spin.Impact).Within(1e-6));
        }
        [Test]
        public void CancelledWindupDoesNotDelayTheFirstBodyStrikeAfterResume()
        {
            var m = Motion(); var pose = new HeroPose();
            m.Evaluate(.15, pose);
            m.Swing(SwingKind.Slash, .5, Vector2.right);
            m.Hold(.3, true); m.Evaluate(10.3, pose); m.Hold(10.3, false);
            var next = m.Swing(SwingKind.Slash, 10.5, Vector2.right);
            m.Evaluate(10.5, pose);
            Assert.That(pose.Layers.Take(pose.LayerCount).Any(l =>
                l.Clip == next.Clip && l.Weight > .9f &&
                Math.Abs(l.Time - Knight[next.Clip].Impact) < 1e-5), Is.True,
                "the first resumed damage must have a visible body impact, not wait for a cancelled windup's end");
        }
    }
}
