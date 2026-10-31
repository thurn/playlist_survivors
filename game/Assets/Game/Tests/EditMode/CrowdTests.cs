using System;
using Game.Client.Crowd;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The crowd's event-to-clip mapping (MonsterTrack): what a monster shows at song times from the
    /// sim's events, on a made-up clip table (no baked library needed).
    /// </summary>
    public class CrowdTests
    {
        const uint Idle = 0, Walk = 1, Attack = 2, Hit = 3;
        const float WalkLength = 1.0f, AttackImpact = 0.75f, AttackEnd = 1.5f, HitOnset = 0.05f;

        static readonly MonsterClips Clips = new MonsterClips
        {
            Idle = new MonsterClip((int)Idle, 2.0f, -1, -1),
            Walk = new MonsterClip((int)Walk, WalkLength, -1, -1),
            Attack = new MonsterClip((int)Attack, 1.6f, AttackImpact, AttackEnd),
            Hit = new MonsterClip((int)Hit, 1.0f, HitOnset, 0.8f),
        };

        /// <summary>A monster spawned at (0, 0) at t = 1, facing the hero to the north.</summary>
        static MonsterTrack Spawned()
        {
            var m = new MonsterTrack();
            m.Begin(7, 1, 0, Clips, 1.0, 0, 0, 0, 10);
            return m;
        }

        [Test]
        public void ASpawnedMonsterIdlesWhereItSpawnedFacingTheHero()
        {
            var m = Spawned();
            Assert.That(m.Visible(0.99), Is.False);
            var p = m.Sample(1.5);
            Assert.That(p.ClipA, Is.EqualTo(Idle));
            Assert.That(p.Blend, Is.EqualTo(1f));
            Assert.That((p.X, p.Y), Is.EqualTo((0.0, 0.0)));
            Assert.That(p.Yaw, Is.EqualTo(0f).Within(1e-6));
        }

        [Test]
        public void AStepSqueezesHalfAWalkCycleIntoItsMoveWindowAndPlantsOnTheBeat()
        {
            var m = Spawned();
            m.Step(0, 0, 1, 0, 2.0, 2.1); // east, a 100 ms window, the footplant on the beat at 2.1
            m.Step(1, 0, 2, 0, 2.5, 2.6);

            var before = m.Sample(1.99);
            Assert.That(before.ClipA, Is.EqualTo(Idle), "a step shows nothing before its window");
            Assert.That(before.X, Is.EqualTo(0.0));

            var mid = m.Sample(2.05);
            Assert.That(mid.ClipA, Is.EqualTo(Walk));
            Assert.That(mid.TimeA, Is.EqualTo(0.25f * WalkLength).Within(1e-5));
            Assert.That(mid.X, Is.EqualTo(0.5).Within(1e-9));

            var plant = m.Sample(2.1);
            Assert.That(plant.ClipA, Is.EqualTo(Walk));
            Assert.That(plant.TimeA, Is.EqualTo(0.5f * WalkLength).Within(1e-5), "the footfall lands on the beat");
            Assert.That(plant.Blend, Is.EqualTo(1f));
            Assert.That((plant.X, plant.Y), Is.EqualTo((1.0, 0.0)));
            Assert.That(plant.Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5), "it turns to face the step");

            var held = m.Sample(2.2);
            Assert.That(held.ClipA, Is.EqualTo(Walk), "the pose holds after the footplant");
            Assert.That(held.TimeA, Is.EqualTo(0.5f * WalkLength).Within(1e-5));

            var easing = m.Sample(2.1 + MonsterTrack.StepHold + MonsterTrack.IdleFade / 2);
            Assert.That((easing.ClipA, easing.ClipB), Is.EqualTo((Idle, Walk)));
            Assert.That(easing.Blend, Is.EqualTo(0.5f).Within(1e-4));

            var next = m.Sample(2.6);
            Assert.That(next.TimeA, Is.EqualTo(WalkLength).Within(1e-5), "the next step is the other foot's half cycle");
            Assert.That(next.X, Is.EqualTo(2.0));
        }

        [Test]
        public void ALungeLandsItsContactOnTheImpactAndReturns()
        {
            var m = Spawned();
            m.Lunge(3.0, 3.25, 0, 1); // the notice half a beat ahead of the contact, toward the hero

            var notice = m.Sample(3.0);
            Assert.That(notice.ClipA, Is.EqualTo(Attack));
            Assert.That(notice.TimeA, Is.EqualTo(0f).Within(1e-5), "the wind-up fills the notice");

            var contact = m.Sample(3.25);
            Assert.That(contact.ClipA, Is.EqualTo(Attack));
            Assert.That(contact.TimeA, Is.EqualTo(AttackImpact).Within(1e-5), "the clip's contact lands on the beat");
            Assert.That(contact.Y, Is.EqualTo(MonsterTrack.LungeReach).Within(1e-9), "the lunge reaches toward the hero at contact");

            double speed = AttackImpact / 0.25;
            double end = 3.25 + (AttackEnd - AttackImpact) / speed;
            var done = m.Sample(end + MonsterTrack.IdleFade + 0.01);
            Assert.That(done.ClipA, Is.EqualTo(Idle));
            Assert.That(done.Blend, Is.EqualTo(1f));
            Assert.That(done.Y, Is.EqualTo(0.0).Within(1e-9), "back where it stood");
        }

        [Test]
        public void AHitFlinchesAndFlashesWhiteForAMoment()
        {
            var m = Spawned();
            m.Hit(4.0);
            m.Knock(0, 0, 0, -2, 4.0, 4.12);
            Assert.That(m.Sample(3.99).HitFlash, Is.EqualTo(0f));

            var struck = m.Sample(4.0);
            Assert.That(struck.HitFlash, Is.EqualTo(1f));
            Assert.That(struck.ClipA, Is.EqualTo(Hit));
            Assert.That(struck.TimeA, Is.EqualTo(HitOnset).Within(1e-5), "the flinch starts at its onset");

            var knocked = m.Sample(4.06);
            Assert.That(knocked.HitFlash, Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(knocked.Y, Is.EqualTo(-1.0).Within(1e-9), "the knock carries it back between beats");
            Assert.That(m.Sample(4.2).HitFlash, Is.EqualTo(0f));
            Assert.That(m.Sample(4.2).Y, Is.EqualTo(-2.0));
        }

        [Test]
        public void AStepAfterAHitCutsInAndKeepsTheFlash()
        {
            var m = Spawned();
            m.Hit(5.0);
            m.Step(0, 0, 0, 1, 5.02, 5.1);
            var p = m.Sample(5.05);
            Assert.That((p.ClipA, p.ClipB), Is.EqualTo((Walk, Hit)), "the step fades in over the flinch");
            Assert.That(p.HitFlash, Is.GreaterThan(0.5f));
        }

        [Test]
        public void ADeathEndsItsLifeOnTheImpactFrameWithItsBlow()
        {
            var m = Spawned();
            m.Die(6.0, 0.6, 0.8, 4.63);
            Assert.That(m.Visible(5.999), Is.True);
            Assert.That(m.Visible(6.0), Is.False);
            Assert.That(m.Died, Is.True, "it shatters");
            Assert.That((m.BlowX, m.BlowY, m.Force), Is.EqualTo((0.6, 0.8, 4.63)));
        }

        [Test]
        public void ARecycledReentryEndsALifeWithoutAShatter()
        {
            var m = Spawned();
            m.End(5.0); // its id spawns again elsewhere
            m.Die(6.0, 1, 0, 1.85);
            Assert.That(m.EndTime, Is.EqualTo(5.0));
            Assert.That(m.Died, Is.False);

            var d = Spawned();
            d.Die(6.0, 1, 0, 1.85);
            d.End(5.5); // a re-entry before the death: it leaves whole
            Assert.That(d.Died, Is.False);
        }
    }
}
