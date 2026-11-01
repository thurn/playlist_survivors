using NUnit.Framework;
using PS.Look.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The motion layers (CrowdMotion): the step spring the hero shares with the crowd's shader, the swarm's placement, the ripple and the flags.</summary>
    public class MotionTests
    {
        static ResolvedLook Resolve(params string[] flags)
        {
            var args = new string[flags.Length + 1];
            args[0] = "player";
            flags.CopyTo(args, 1);
            return ResolvedLook.Resolve(LookFlags.Parse(args));
        }

        [Test]
        public void AStepStretchesAndLeansThroughItsMoveThenSquashesOnItsPlantAndSettles()
        {
            CrowdMotion.StepSpring(-0.2f, 0.1f, 1, out float stretch, out float lean);
            Assert.That((stretch, lean), Is.EqualTo((0f, 0f)), "before its move");
            CrowdMotion.StepSpring(-0.05f, 0.1f, 1, out float midStretch, out float midLean);
            CrowdMotion.StepSpring(-0.001f, 0.1f, 1, out float lateStretch, out float lateLean);
            Assert.That(midStretch, Is.GreaterThan(0));
            Assert.That(lateStretch, Is.GreaterThan(midStretch));
            Assert.That(lateLean, Is.GreaterThan(midLean).And.GreaterThan(0));
            CrowdMotion.StepSpring(0, 0.1f, 1, out stretch, out lean);
            Assert.That(stretch, Is.EqualTo(-CrowdMotion.StepSquash).Within(1e-6), "the plant's frame is the squash's peak");
            Assert.That(lean, Is.EqualTo(CrowdMotion.StepLean).Within(1e-6), "the lean carries through the plant");
            float least = 1;
            for (float t = 0; t < 0.4f; t += 1 / 120f)
            {
                CrowdMotion.StepSpring(t, 0.1f, 1, out _, out lean);
                least = Mathf.Min(least, lean);
            }
            Assert.That(least, Is.LessThan(0), "the lean overshoots back");
            CrowdMotion.StepSpring(0.6f, 0.1f, 1, out stretch, out lean);
            Assert.That(Mathf.Abs(stretch) + Mathf.Abs(lean), Is.LessThan(0.01f), "settled well within a beat or two");
            CrowdMotion.StepSpring(5, 0.1f, 1, out stretch, out lean);
            Assert.That((stretch, lean), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public void AStepThatMovesAfterItsPlantIsKickedIntoItsLeanThere()
        {
            CrowdMotion.StepSpring(0, 0, 1, out float stretch, out float lean);
            Assert.That(stretch, Is.EqualTo(-CrowdMotion.StepSquash).Within(1e-6));
            Assert.That(lean, Is.EqualTo(0).Within(1e-6));
            CrowdMotion.StepSpring(0.08f, 0, 1, out _, out lean);
            Assert.That(lean, Is.GreaterThan(0.5f * CrowdMotion.StepLean), "leaning into the move that follows");
            CrowdMotion.StepSpring(-0.05f, 0, 1, out stretch, out lean);
            Assert.That((stretch, lean), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public void ASmallStepSpringsLess()
        {
            Assert.That(CrowdMotion.StepSize(CrowdMotion.StepFull * 2), Is.EqualTo(1));
            Assert.That(CrowdMotion.StepSize(CrowdMotion.StepFull / 2), Is.EqualTo(0.5f).Within(1e-6));
            CrowdMotion.StepSpring(0, 0.1f, 0.5f, out float stretch, out _);
            Assert.That(stretch, Is.EqualTo(-0.5f * CrowdMotion.StepSquash).Within(1e-6));
        }

        [Test]
        public void TheSwarmPlacesEachLifeALittleOffItsSpotTheSameEveryFrame()
        {
            var spread = 0f;
            for (uint seed = 0; seed < 200; seed++)
            {
                var m = new MonsterInstance { Position = new Vector3(3, 0.5f, 4), Yaw = 1, Scale = 2 };
                CrowdMotion.Place(ref m, seed);
                var again = new MonsterInstance { Position = new Vector3(3, 0.5f, 4), Yaw = 1, Scale = 2 };
                CrowdMotion.Place(ref again, seed);
                Assert.That((again.Position, again.Yaw, again.Scale), Is.EqualTo((m.Position, m.Yaw, m.Scale)));
                float off = Vector2.Distance(new Vector2(m.Position.x, m.Position.z), new Vector2(3, 4));
                Assert.That(off, Is.LessThanOrEqualTo(CrowdMotion.JitterM * 2 + 1e-5f));
                Assert.That(m.Position.y, Is.EqualTo(0.5f));
                Assert.That(Mathf.Abs(m.Yaw - 1), Is.LessThanOrEqualTo(CrowdMotion.YawJitter + 1e-5f));
                Assert.That(Mathf.Abs(m.Scale / 2 - 1), Is.LessThanOrEqualTo(CrowdMotion.ScaleJitter + 1e-5f));
                spread = Mathf.Max(spread, off);
            }
            Assert.That(spread, Is.GreaterThan(CrowdMotion.JitterM), "lives differ");
        }

        [Test]
        public void TheRippleDelaysAStepBySixteenthNoteAtMostByItsDistance()
        {
            Assert.That(CrowdMotion.RippleDelay(0, 0.5), Is.EqualTo(0));
            Assert.That(CrowdMotion.RippleDelay(CrowdMotion.RippleReachM / 2, 0.5), Is.EqualTo(0.0625).Within(1e-9));
            Assert.That(CrowdMotion.RippleDelay(CrowdMotion.RippleReachM, 0.5), Is.EqualTo(0.125).Within(1e-9));
            Assert.That(CrowdMotion.RippleDelay(1000, 0.5), Is.EqualTo(0.125).Within(1e-9));
        }

        [Test]
        public void EveryPresetSpringsAndSwarmsWithoutTheRipple()
        {
            foreach (var name in LookLibrary.Shared.Presets)
            {
                var look = Resolve("--look", name);
                Assert.That((look.MotionOn("springs"), look.MotionOn("swarm"), look.MotionOn("beatRipple")), Is.EqualTo((true, true, false)), name);
                Assert.That(Resolve("--look", name, "--beat-ripple").MotionOn("beatRipple"), Is.True, name);
            }
        }

        [Test]
        public void MotionSetsTheLayersInPlaceOfThePresets()
        {
            var off = Resolve("--look", "edge", "--motion", "none");
            Assert.That((off.MotionOn("springs"), off.MotionOn("swarm")), Is.EqualTo((false, false)));
            Assert.That(off.Describe(), Does.EndWith("motion none"));
            var springs = Resolve("--look", "edge", "--motion", "springs", "--beat-ripple");
            Assert.That((springs.MotionOn("springs"), springs.MotionOn("swarm"), springs.MotionOn("beatRipple")), Is.EqualTo((true, false, true)));
            Assert.That(springs.Describe(), Does.EndWith("motion springs (--motion), beatRipple (--beat-ripple)"));
            Assert.That(Assert.Throws<System.ArgumentException>(() => Resolve("--motion", "spring")).Message, Does.Contain("spring is not one of none"));
        }

        [Test]
        public void ApplyingALookSetsItsLayers()
        {
            Resolve("--look", "solid", "--motion", "swarm", "--beat-ripple").Apply();
            Assert.That((CrowdMotion.Springs, CrowdMotion.Swarm, CrowdMotion.Ripple), Is.EqualTo((false, true, true)));
            Assert.That(Shader.GetGlobalFloat("_LookSwarm"), Is.EqualTo(1));
            Assert.That(Shader.GetGlobalFloat("_LookSprings"), Is.EqualTo(0));
            Resolve("--look", "solid").Apply();
            Assert.That((CrowdMotion.Springs, CrowdMotion.Swarm, CrowdMotion.Ripple), Is.EqualTo((true, true, false)));
        }
    }
}
