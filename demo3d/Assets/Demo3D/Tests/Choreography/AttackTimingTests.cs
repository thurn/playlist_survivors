using System;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// Attacks carry the clip start and speed the sim fitted, so the client plays exactly what was
    /// scheduled (the foundations review: the client used to refit them and drifted off the sim).
    /// </summary>
    public class AttackTimingTests
    {
        [Test]
        public void EveryAttackCarriesAFittedStartWithinTheSpeedLimits()
        {
            Timeline tl = Fixtures.SongTimeline;
            int n = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Attack) continue;
                Assert.That(a.HasClipTiming, Is.True, "attack at " + a.Impact);
                Assert.That(a.ClipSpeed, Is.InRange(ClipLibrary.MinSpeed - 1e-6f, ClipLibrary.MaxSpeed + 1e-6f));
                ClipAnnotation clip = Fixtures.Data.Clips[a.Clip];
                // The blow lands on the impact: start + annotated impact / speed = impact.
                Assert.That(a.ClipStart + clip.Impact / a.ClipSpeed, Is.EqualTo(a.Impact).Within(1e-6));
                n++;
            }
            Assert.That(n, Is.GreaterThan(50));
        }

        [Test]
        public void TheHerosRootMotionSegmentStartsWithItsComboClip()
        {
            Timeline tl = Fixtures.SongTimeline;
            var segs = tl.SegmentsOf(0);
            int checkedCombos = 0;
            foreach (ActionEvent a in tl.ActionsOf(0))
            {
                if (a.Kind != ActionKind.Attack) continue;
                bool found = false;
                for (int k = 0; k < segs.Count && !found; k++)
                {
                    MotionSegment s = segs.Array[segs.Offset + k];
                    found = s.Ease == Ease.Linear && Math.Abs(s.T0 - a.ClipStart) < 1e-9;
                }
                Assert.That(found, Is.True, "no root-motion segment starts at the combo's clip start " + a.ClipStart);
                checkedCombos++;
            }
            Assert.That(checkedCombos, Is.GreaterThan(20));
        }
    }
}
