using System;
using NUnit.Framework;

namespace Demo3D.Choreography.Tests
{
    /// <summary>TD10's checks on the song's timeline: slams on the ending's accents, ticks without knockback.</summary>
    public class PresentationTests
    {
        [Test]
        public void EveryAccentOfTheEndingGetsAHeroSlam()
        {
            Timeline tl = Fixtures.SongTimeline;
            SongMap map = tl.Map;
            SongSection ending = map.Sections[map.Sections.Length - 1];
            int slam = Fixtures.Data.Clips.Require("hero slam");
            int accents = 0;
            foreach (TimedStrength a in map.Accents)
            {
                if (a.Time < ending.Start || a.Time >= ending.End) continue;
                accents++;
                bool slammed = false;
                foreach (ActionEvent e in tl.ActionsOf(0))
                    if (e.Kind == ActionKind.Attack && e.Clip == slam && Math.Abs(e.Impact - a.Time) < 0.001) slammed = true;
                Assert.That(slammed, Is.True, "no hero slam on the ending's accent at " + a.Time.ToString("F3"));
            }
            Assert.That(accents, Is.GreaterThan(0));
        }

        [Test]
        public void TicksNeverKnockAMonsterBack()
        {
            Timeline tl = Fixtures.SongTimeline;
            int ticks = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Tick) continue;
                ticks++;
                // A knockback is a segment starting at the impact that moves the monster, with no
                // hit or death on it at the same moment.
                bool struck = false;
                foreach (ActionEvent b in tl.ActionsOf(a.Entity))
                    if ((b.Kind == ActionKind.Hit || b.Kind == ActionKind.Death) && Math.Abs(b.Impact - a.Impact) < 1e-6) struck = true;
                if (struck) continue;
                var segs = tl.SegmentsOf(a.Entity);
                for (int k = 0; k < segs.Count; k++)
                {
                    MotionSegment s = segs.Array[segs.Offset + k];
                    if (Math.Abs(s.T0 - a.Impact) < 1e-6 && s.Ease == Ease.Linear)
                        Assert.That(System.Numerics.Vector2.Distance(s.From, s.To), Is.LessThan(1e-4f),
                            "tick at " + a.Impact + " moved monster " + a.Entity);
                }
            }
            Assert.That(ticks, Is.GreaterThan(0), "the song has laser ticks");
        }
    }
}
