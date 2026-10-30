using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>
    /// PX8, the hero and presentation: the beam's ticks, the ending's slams, the boss's entry and
    /// attacks, and the client's hit-stop and knockback hops on the song's timeline.
    /// </summary>
    public class PresentationTests
    {
        static Timeline Song => Fixtures.SongTimeline;
        static ClipLibrary Clips => Fixtures.Data.Clips;

        static bool StruckAt(int e, double t) =>
            Song.ActionsOf(e).Any(a => (a.Kind == ActionKind.Hit || a.Kind == ActionKind.Death) && Math.Abs(a.Impact - t) < 1e-9);

        /// <summary>
        /// Each of the beam's ticks flashes the monster it sweeps, briefly and at half strength, and
        /// knocks nothing back: no push starts on the tick and the sprite doesn't hop (ticks where
        /// another blow lands on the same monster at the same instant are left out).
        /// </summary>
        [Test]
        public void BeamTicksFlashMonstersWithoutKnockback()
        {
            var eval = SpriteEvaluatorTests.Evaluator();
            var wrong = new List<string>();
            int ticks = 0;
            foreach (ActionEvent a in Song.Actions)
            {
                if (a.Kind != ActionKind.Tick || StruckAt(a.Entity, a.Impact)) continue;
                ticks++;
                foreach (MotionSegment s in Song.SegmentsOf(a.Entity))
                    if (Math.Abs(s.T0 - a.Impact) < 1e-9 && s.T1 - s.T0 < 0.2 && s.From != s.To)
                        wrong.Add($"tick on {a.Entity} at {a.Impact:F3} pushes it {s.From} -> {s.To}");
                SpriteEvaluator.State st = eval.Evaluate(a.Entity, a.Impact + 0.02);
                if (st.Flash < 0.3f || st.Flash > ClipSchedule.TickFlash) wrong.Add($"tick on {a.Entity} at {a.Impact:F3} flashes {st.Flash:F2}");
                if (eval.Evaluate(a.Entity, a.Impact + SpriteEvaluator.HopSeconds / 2).Lift != 0) wrong.Add($"tick on {a.Entity} at {a.Impact:F3} hops");
                if (wrong.Count > 10) break;
            }
            TestContext.WriteLine(ticks + " ticks");
            Assert.That(ticks, Is.GreaterThan(50));
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
        }

        /// <summary>A hit's knockback hops: halfway through, the struck monster's sprite is off the ground.</summary>
        [Test]
        public void HitsHopTheirMonsterOverItsShadow()
        {
            var eval = SpriteEvaluatorTests.Evaluator();
            int hits = 0, grounded = 0;
            foreach (ActionEvent a in Song.Actions)
            {
                if (a.Kind != ActionKind.Hit || Song.Entities[a.Entity].Kind != EntityKind.Monster) continue;
                // A later blow inside the hop starts a new one.
                if (Song.ActionsOf(a.Entity).Any(b => b.Impact > a.Impact && b.Impact <= a.Impact + SpriteEvaluator.HopSeconds)) continue;
                hits++;
                if (eval.Evaluate(a.Entity, a.Impact + SpriteEvaluator.HopSeconds / 2).Lift <= 0) grounded++;
            }
            Assert.That(hits, Is.GreaterThan(500));
            Assert.That(grounded, Is.Zero);
            Assert.That(eval.Evaluate(0, 100).Lift, Is.Zero, "the hero never hops");
        }

        /// <summary>The ending clears the horde on its mapped accents: each one is a hero slam.</summary>
        [Test]
        public void TheEndingsAccentsEachGetAHeroSlam()
        {
            SectionStats ending = Array.Find(Song.Sections, s => s.Label == "ending");
            int slam = Clips.Require("hero slam");
            var accents = Song.Map.Accents.Where(x => x.Time >= ending.Start - 1e-6 && x.Time < ending.End).ToList();
            Assert.That(accents, Is.Not.Empty);
            foreach (var accent in accents)
                Assert.That(Song.ActionsOf(0).Any(a => a.Kind == ActionKind.Attack && a.Clip == slam && Math.Abs(a.Impact - accent.Time) < 1e-6),
                    "a hero slam on the ending's accent at " + accent.Time.ToString("F3"));
        }

        /// <summary>
        /// The boss enters at the final peak from the cliff ledge's side, walks into the view while
        /// the section has most of it left, casts its hellfire on beats, and falls to the ending's
        /// last slam.
        /// </summary>
        [Test]
        public void TheBossWalksInCastsAndFallsToTheLastSlam()
        {
            int bossType = Fixtures.Data.Roster.MonsterIndex("boss-devil");
            EntityInfo boss = Song.Entities.Single(e => e.Kind == EntityKind.Monster && e.Type == bossType);
            SectionStats peak = Array.Find(Song.Sections, s => s.Label == "final-peak");
            Assert.That(boss.Spawn, Is.EqualTo(peak.Start).Within(1e-6));

            double inView = double.NaN;
            for (double t = boss.Spawn; t < peak.End; t += 0.1)
            {
                FramingSample f = Song.FramingAt(t);
                if (Song.View(f.ViewWidth).Contains(f.Focus, Song.PositionAt(boss.Id, t))) { inView = t; break; }
            }
            TestContext.WriteLine("boss in view at " + inView.ToString("F1") + " s");
            Assert.That(inView, Is.LessThan(peak.Start + (peak.End - peak.Start) / 3), "in view in the section's first third");

            var attacks = Song.ActionsOf(boss.Id).Where(a => a.Kind == ActionKind.Attack).ToList();
            TestContext.WriteLine(attacks.Count + " boss attacks");
            Assert.That(attacks.Count, Is.GreaterThanOrEqualTo(5));
            int hellfire = Array.IndexOf(Song.ProjectileNames, "hellfire");
            Assert.That(Song.Entities.Count(e => e.Kind == EntityKind.Projectile && e.Type == hellfire), Is.GreaterThanOrEqualTo(5));

            ActionEvent death = Song.ActionsOf(boss.Id).Single(a => a.Kind == ActionKind.Death);
            SectionStats ending = Array.Find(Song.Sections, s => s.Label == "ending");
            double lastAccent = Song.Map.Accents.Where(x => x.Time >= ending.Start - 1e-6 && x.Time < ending.End).Max(x => x.Time);
            Assert.That(death.Impact, Is.EqualTo(lastAccent).Within(1e-6));
        }

        /// <summary>
        /// The hero's big blows (spins and slams) hold their impact frame through the hit-stop, where
        /// a slash has already moved on.
        /// </summary>
        [Test]
        public void TheHerosBigBlowsHoldTheirImpactFrame()
        {
            var eval = SpriteEvaluatorTests.Evaluator();
            int rate = SpriteEvaluatorTests.FrameRate;
            int heavy = 0, light = 0;
            for (int i = 0; i < Song.Actions.Length; i++)
            {
                ActionEvent a = Song.Actions[i];
                if (a.Entity != 0 || a.Kind != ActionKind.Attack || eval.Schedule.Interrupted(i)) continue;
                int impactFrame = (int)Math.Round(Clips[a.Clip].Impact * rate);
                if ((Clips[a.Clip].End - Clips[a.Clip].Impact) * rate < 2) continue;
                SpriteEvaluator.State st = eval.Evaluate(0, a.Impact + 0.09);
                if (st.Clip != a.Clip) continue;
                if (a.Strength >= ClipSchedule.HeavyStrength)
                {
                    Assert.That(st.Frame, Is.EqualTo(impactFrame), $"{Song.ClipNames[a.Clip]} at {a.Impact:F3} holds");
                    heavy++;
                }
                else if (Math.Abs(eval.Schedule.LocalTime(i, a.Impact) - Clips[a.Clip].Impact) < 1e-3)
                {
                    Assert.That(st.Frame, Is.EqualTo(impactFrame + 1), $"{Song.ClipNames[a.Clip]} at {a.Impact:F3} moves on");
                    light++;
                }
            }
            TestContext.WriteLine(heavy + " big blows held, " + light + " slashes moved on");
            Assert.That(heavy, Is.GreaterThan(3));
            Assert.That(light, Is.GreaterThan(50));
        }
    }
}
