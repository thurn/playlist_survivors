using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>The clients' shared clip schedule, on a small hand-built timeline at 120 bpm (a beat is 0.5 s).</summary>
    public class ClipScheduleTests
    {
        const int Hero = 0, Monster = 1;
        const int Attack = 0, HitClip = 1, DeathClip = 2;
        // Action indices: the hero's heavy blow, then the monster's flinch, tick, killing blow and death.
        const int Blow = 0, Flinch = 1, Tick = 2, KillingFlinch = 3, Death = 4;

        static readonly ClipLibrary Clips = new ClipLibrary(new[]
        {
            new ClipAnnotation("g", "Attack", "hero attack", 0.4f, 0.8f, 0f, 0f),
            new ClipAnnotation("g", "Hit", "skeleton hit", 0.1f, 0.4f, 0f, 0f),
            new ClipAnnotation("g", "Death", "skeleton death", 0.1f, 0.6f, 0f, 0f),
        });

        static Timeline Build() => new Timeline
        {
            Map = SongMap.Synthetic(120, 10),
            Entities = new[]
            {
                new EntityInfo(Hero, -1, EntityKind.Hero, 0, 10, 1),
                new EntityInfo(Monster, 0, EntityKind.Monster, 0, 6, 2),
            },
            // The monster walks 5 m in its first second, then stands.
            Segments = new[]
            {
                new MotionSegment(Hero, 0, 10, Vector2.Zero, Vector2.Zero, 0, 0, Ease.Linear),
                new MotionSegment(Monster, 0, 1, Vector2.Zero, new Vector2(3, 4), 0, 0, Ease.Linear),
                new MotionSegment(Monster, 1, 10, new Vector2(3, 4), new Vector2(3, 4), 0, 0, Ease.Linear),
            },
            SegmentStart = new[] { 0, 1, 3 },
            Actions = new[]
            {
                new ActionEvent(Hero, ActionKind.Attack, 2.0, Attack, Monster, 1f, clipStart: 1.6, clipSpeed: 1f),
                new ActionEvent(Monster, ActionKind.Hit, 2.0, HitClip, -1, 1f),
                new ActionEvent(Monster, ActionKind.Tick, 3.0, -1, -1, 0.5f),
                new ActionEvent(Monster, ActionKind.Hit, 4.0, HitClip, -1, 1f),
                new ActionEvent(Monster, ActionKind.Death, 4.0, DeathClip, -1, 1f),
            },
            ActionStart = new[] { 0, 1, 5 },
        };

        static ClipSchedule Schedule(bool slowHeavyBlows = true) => new ClipSchedule(Build(), Clips, slowHeavyBlows);

        [Test]
        public void ADeathsFadeStartsABeatAfterItsClipComesToRest()
        {
            var s = Schedule();
            double fade = s.FadeStart(Death);
            Assert.That(fade, Is.EqualTo(s.Start(Death) + 0.6 + 0.5).Within(1e-6), "the clip's end, then a beat at 120 bpm");
            Assert.That(s.FeedbackAt(Monster, fade).Fade, Is.Zero);
            Assert.That(s.FeedbackAt(Monster, fade + 0.01).Fade, Is.GreaterThan(0f), "the dissolve begins there");
        }

        [Test]
        public void HitStopHoldsThenCatchesUpWithoutAJump([Values(0f, 0.6f)] float rate)
        {
            const double impact = 10.0;
            double prev = double.NegativeInfinity;
            for (double t = impact - 0.1; t < impact + 0.5; t += 0.001)
            {
                double local = ClipSchedule.HoldThenCatchUp(t, impact, rate);
                Assert.That(local, Is.GreaterThanOrEqualTo(prev - 1e-9), "never runs backwards");
                if (prev > double.NegativeInfinity)
                    Assert.That(local - prev, Is.LessThan(0.003), $"no jump at {t}");
                prev = local;
            }
            Assert.That(ClipSchedule.HoldThenCatchUp(impact, impact, rate), Is.EqualTo(impact));
            Assert.That(ClipSchedule.HoldThenCatchUp(impact + 0.3, impact, rate), Is.EqualTo(impact + 0.3).Within(1e-9));
        }

        [Test]
        public void AFlinchHoldsThroughTheHitStopThenPlaysOn()
        {
            var s = Schedule();
            Assert.That(s.ActiveAction(Monster, 2.0, out _), Is.EqualTo(Flinch));
            Assert.That(s.LocalTime(Flinch, 2.0), Is.EqualTo(0.1).Within(1e-6), "the flinch's onset at the blow");
            Assert.That(s.LocalTime(Flinch, 2.04), Is.EqualTo(0.1).Within(1e-6), "held");
            Assert.That(s.LocalTime(Flinch, 2.3), Is.EqualTo(0.4).Within(1e-6), "caught up");
            Assert.That(s.ActiveAction(Monster, 2.31, out double since), Is.EqualTo(-1), "walking again after its end");
            Assert.That(since, Is.EqualTo(2.3).Within(1e-6));
        }

        [Test]
        public void OnlyTheHerosHeavyBlowsSlowWhenAsked()
        {
            Assert.That(Schedule().LocalTime(Blow, 2.025), Is.EqualTo(0.4 + 0.025 * ClipSchedule.HeavySlow).Within(1e-6));
            Assert.That(Schedule(slowHeavyBlows: false).LocalTime(Blow, 2.025), Is.EqualTo(0.425).Within(1e-6));
        }

        [Test]
        public void AKillingBlowShowsTheDeathNotTheFlinch()
        {
            var s = Schedule();
            Assert.That(s.ActiveAction(Monster, 4.0, out _), Is.EqualTo(Death));
            Assert.That(s.ActiveAction(Monster, 9.0, out _), Is.EqualTo(Death), "a death never ends");
            Assert.That(s.Interrupted(KillingFlinch), Is.True);
            Assert.That(s.Interrupted(Death), Is.False);
            Assert.That(s.Interrupted(Tick), Is.True, "a tick plays no clip");
        }

        [Test]
        public void HitsTicksAndDeathsFlashAndADeathFadesAfterABeat()
        {
            var s = Schedule();
            Assert.That(s.FeedbackAt(Monster, 2.0).Flash, Is.EqualTo(1f));
            Assert.That(s.FeedbackAt(Monster, 2.06).Flash, Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(s.FeedbackAt(Monster, 2.2).Flash, Is.EqualTo(0f));
            Assert.That(s.FeedbackAt(Monster, 3.0).Flash, Is.EqualTo(ClipSchedule.TickFlash), "a tick flashes at half strength");
            Assert.That(s.FeedbackAt(Monster, 3.1).Flash, Is.EqualTo(0f), "and briefly");

            // The death's clip starts at 3.9 and ends at 4.5; it holds a beat, then fades over one.
            var dying = s.FeedbackAt(Monster, 4.0);
            Assert.That(dying.Dying, Is.True);
            Assert.That(dying.Flash, Is.EqualTo(1f));
            Assert.That(s.FeedbackAt(Monster, 4.99).Fade, Is.EqualTo(0f));
            Assert.That(s.FeedbackAt(Monster, 5.25).Fade, Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(s.FeedbackAt(Monster, 5.6).Fade, Is.EqualTo(1f));
            Assert.That(s.FeedbackAt(Monster, 1.9).Dying, Is.False);
        }

        [Test]
        public void WalkDistanceFollowsTheSegments()
        {
            var s = Schedule();
            Assert.That(s.DistanceAt(Monster, 0.5), Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(s.DistanceAt(Monster, 6.0), Is.EqualTo(5f).Within(1e-5), "standing still adds nothing");
            Assert.That(s.BeatLength(4.2), Is.EqualTo(0.5).Within(1e-9));
        }
    }
}
