using System;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>TD10's checks on the song's timeline: slams on the ending's accents, ticks without knockback, one-hit kills.</summary>
    public class PresentationTests
    {
        [TestCase("song")]
        [TestCase("song-overdrive")]
        public void EveryAccentOfTheEndingGetsAHeroSlam(string name)
        {
            Timeline tl = Fixtures.Named(name);
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

        /// <summary>
        /// ps-442: the hero winds up for the ending's first slam only when it must, so it keeps fighting through
        /// the outro instead of standing frozen for seconds with the windup booked: its last blow before the slam
        /// lands within a bar of the slam's windup.
        /// </summary>
        [TestCase("song")]
        [TestCase("song-overdrive")]
        public void TheHeroFightsUpToTheEndingsFirstSlam(string name)
        {
            Timeline tl = Fixtures.Named(name);
            SongMap map = tl.Map;
            SongSection ending = map.Sections[map.Sections.Length - 1];
            double bar = 4 * (map.Beats[1] - map.Beats[0]);
            ActionEvent prev = default, slam = default;
            foreach (ActionEvent e in tl.ActionsOf(0))
            {
                if (e.Kind != ActionKind.Attack) continue;
                if (e.Impact >= ending.Start) { slam = e; break; }
                prev = e;
            }
            Assert.That(slam.Kind, Is.EqualTo(ActionKind.Attack), "the ending has a slam");
            Assert.That(slam.ClipStart - prev.Impact, Is.LessThan(bar),
                "the hero stands from its blow at " + prev.Impact.ToString("F2") + " to the slam's windup at " + slam.ClipStart.ToString("F2"));
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

        /// <summary>
        /// ps-7n4: with the level script's one-hit kills, normal and overdrive alike, every monster but the boss dies
        /// to the first blow or spell that strikes it, the laser's sweep included: none takes a hit or a tick and lives.
        /// </summary>
        [TestCase("song")]
        [TestCase("song-overdrive")]
        public void EveryMonsterButTheBossDiesToItsFirstStrike(string name)
        {
            Timeline tl = Fixtures.Named(name);
            int bossType = Fixtures.Data.Roster.MonsterIndex("boss");
            int deaths = 0, bossHits = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                EntityInfo e = tl.Entities[a.Entity];
                if (e.Kind != EntityKind.Monster) continue;
                if (a.Kind == ActionKind.Death) deaths++;
                if (a.Kind != ActionKind.Hit && a.Kind != ActionKind.Tick) continue;
                if (e.Type == bossType) { bossHits++; continue; }
                Assert.Fail(tl.TypeNames[e.Type] + " " + a.Entity + " takes a " + a.Kind + " at " + a.Impact.ToString("F2") + " and lives");
            }
            Assert.That(deaths, Is.GreaterThan(1000));
            Assert.That(bossHits, Is.GreaterThan(0), "the boss still takes blows until its last bar");
        }

        /// <summary>
        /// ps-ffx: the hero's cleaves, not the spells, reap the song's drops, as native's sword spin does: it takes at
        /// least half of each drop's kills, and outside the build (which travels) swings at least once a second.
        /// </summary>
        [Test]
        public void TheHerosCleavesReapTheDrops()
        {
            foreach (SectionStats s in Fixtures.SongTimeline.Sections)
            {
                if (Array.IndexOf(new[] { "drop-1", "groove-2", "build", "drop-2", "final-peak" }, s.Label) < 0) continue;
                double swings = s.HeroSwings / s.Duration, share = (double)s.HeroKills / Math.Max(1, s.HeroKills + s.SpellKills);
                TestContext.WriteLine(s.Label + ": " + swings.ToString("F2") + " swings/s, hero's share " + share.ToString("F2"));
                if (s.Label != "build") Assert.That(swings, Is.GreaterThanOrEqualTo(1.0), s.Label + " swings/s");
                Assert.That(share, Is.GreaterThanOrEqualTo(0.5), s.Label + " hero's share of the kills");
            }
        }

        /// <summary>Without one-hit kills a laser tick is a flash only: it never knocks its monster back.</summary>
        [Test]
        public void TicksNeverKnockAMonsterBack()
        {
            Timeline tl = Fixtures.MultiHitTimeline;
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

        /// <summary>
        /// ps-tfn.9: the boss enters at the final peak, walks into the view while the section has
        /// most of it left, stays in view to the section's end, and is struck before the ending.
        /// </summary>
        [Test]
        public void TheBossWalksIntoViewAndIsStruck()
        {
            Timeline tl = Fixtures.SongTimeline;
            int bossType = Fixtures.Data.Roster.MonsterIndex("boss");
            EntityInfo boss = Array.Find(tl.Entities, e => e.Kind == EntityKind.Monster && e.Type == bossType);
            SectionStats peak = Array.Find(tl.Sections, s => s.Label == "final-peak");
            SectionStats ending = Array.Find(tl.Sections, s => s.Label == "ending");
            Assert.That(boss.Spawn, Is.EqualTo(peak.Start).Within(1e-6));

            bool InView(double t)
            {
                FramingSample f = tl.FramingAt(t);
                return tl.View(f.ViewWidth).Contains(f.Focus, tl.PositionAt(boss.Id, t));
            }
            double inView = peak.End;
            for (double t = boss.Spawn; t < peak.End; t += 0.1) if (InView(t)) { inView = t; break; }
            TestContext.WriteLine("boss in view at " + inView.ToString("F1") + " s");
            Assert.That(inView, Is.LessThan(peak.Start + (peak.End - peak.Start) / 3), "in view in the section's first third");
            for (double t = inView; t < peak.End; t += 0.1) Assert.That(InView(t), Is.True, "boss out of view at " + t.ToString("F1"));

            int blows = 0;
            foreach (ActionEvent a in tl.ActionsOf(boss.Id))
                if ((a.Kind == ActionKind.Attack || a.Kind == ActionKind.Hit) && a.Impact < ending.Start) blows++;
            TestContext.WriteLine(blows + " boss attacks and hits before the ending");
            Assert.That(blows, Is.GreaterThan(0), "the boss attacks or is struck before the ending");
        }
    }
}
