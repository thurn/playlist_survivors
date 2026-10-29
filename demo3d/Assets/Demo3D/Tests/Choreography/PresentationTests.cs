using System;
using NUnit.Framework;
using PS.Choreography;

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

        /// <summary>TD13: the ending's last accent slam leaves no monster alive on screen, to the song's end.</summary>
        [Test]
        public void TheEndingClearsTheScreen()
        {
            Timeline tl = Fixtures.SongTimeline;
            SongMap map = tl.Map;
            SongSection ending = map.Sections[map.Sections.Length - 1];
            double last = -1;
            foreach (TimedStrength a in map.Accents) if (a.Time >= ending.Start && a.Time < ending.End) last = a.Time;
            int before = 0;
            for (double t = last - 1e-3; t < tl.Duration; t += 0.25)
            {
                FramingSample f = tl.FramingAt(t);
                var view = new ViewGeometry(f.ViewWidth);
                int alive = 0;
                foreach (EntityInfo e in tl.Entities)
                {
                    if (e.Kind != EntityKind.Monster || t < e.Spawn || t >= e.Despawn) continue;
                    bool dead = false;
                    foreach (ActionEvent a in tl.ActionsOf(e.Id)) if (a.Kind == ActionKind.Death && a.Impact <= t) dead = true;
                    if (!dead && view.Contains(f.Focus, tl.PositionAt(e.Id, t))) alive++;
                }
                if (t < last) { before = alive; continue; }
                Assert.That(alive, Is.EqualTo(0), "monsters alive on screen at " + t.ToString("F2") + " after the last slam");
            }
            Assert.That(before, Is.GreaterThan(50), "the last slam has a horde to clear");
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
