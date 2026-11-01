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
        const uint Idle = 0, Walk = 1, Attack = 2, Hit = 3, Telegraph = 4, Charge = 5;
        const float WalkLength = 1.0f, AttackImpact = 0.75f, AttackEnd = 1.5f, HitOnset = 0.05f, TelegraphLength = 2.0f;

        static readonly MonsterClips Clips = new MonsterClips
        {
            Idle = new MonsterClip((int)Idle, 2.0f, -1, -1),
            Walk = new MonsterClip((int)Walk, WalkLength, -1, -1),
            Attack = new MonsterClip((int)Attack, 1.6f, AttackImpact, AttackEnd),
            Hit = new MonsterClip((int)Hit, 1.0f, HitOnset, 0.8f),
            Telegraph = new MonsterClip((int)Telegraph, TelegraphLength, -1, -1),
            Charge = new MonsterClip((int)Charge, 0.5f, -1, -1),
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
        public void ABossTelegraphsInPlaceThenItsChargeRunsOnTheDeliveryStep()
        {
            var m = Spawned();
            m.Telegraph(3.0, 4.0, false, 1, 0); // two beats at 120 BPM, toward the hero to the east
            m.Step(0, 0, 8, 0, 3.9, 4.0);       // the charge: the step planting on the delivery beat
            m.Step(8, 0, 8.5, 0, 4.4, 4.5);     // an ordinary step after it

            var mid = m.Sample(3.45);
            Assert.That(mid.ClipA, Is.EqualTo(Telegraph));
            Assert.That(mid.TimeA, Is.EqualTo(0.45f * TelegraphLength).Within(1e-4), "the clip fills the warning");
            Assert.That((mid.X, mid.Y), Is.EqualTo((0.0, 0.0)), "in place");
            Assert.That(mid.Yaw, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5), "facing the hero");

            var dash = m.Sample(3.95);
            Assert.That(dash.ClipA, Is.EqualTo(Charge), "the charge runs, not walks");
            Assert.That(dash.X, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(m.Sample(4.0 + MonsterTrack.ChargeHold - 0.01).ClipA, Is.EqualTo(Charge), "held a moment after it lands");
            Assert.That(m.Sample(4.45).ClipA, Is.EqualTo(Walk), "later steps walk");
        }

        [Test]
        public void ABossSlamLandsTheAttacksContactOnTheDeliveryBeat()
        {
            var m = Spawned();
            m.Telegraph(3.0, 4.0, true, 0, 1);
            double blow = 4.0 - AttackImpact / MonsterTrack.SlamSpeed;
            Assert.That(m.Sample(blow - 0.05).ClipA, Is.EqualTo(Telegraph));
            var contact = m.Sample(4.0);
            Assert.That(contact.ClipA, Is.EqualTo(Attack));
            Assert.That(contact.TimeA, Is.EqualTo(AttackImpact).Within(1e-5), "the blow lands on the beat");
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

        /// <summary>
        /// A free press's hit and knock arrive in heard time, after the sim had settled the step that followed: the hit
        /// shows from its time, and the knock replaces that step, as the sim's does.
        /// </summary>
        [Test]
        public void AHeardTimeHitShowsFromItsTimeAndItsKnockReplacesTheStepAfterIt()
        {
            var m = Spawned();
            m.Step(0, 0, 1, 0, 5.05, 5.15); // settled first, a lead ahead
            m.Hit(5.0);
            m.Knock(0, 0, 0, -1, 5.0, 5.12);
            var struck = m.Sample(5.0);
            Assert.That((struck.ClipA, struck.HitFlash), Is.EqualTo((Hit, 1f)));
            var p = m.Sample(5.1);
            Assert.That(p.ClipA, Is.EqualTo(Hit), "the flinch, not the step");
            Assert.That(p.X, Is.EqualTo(0.0));
            Assert.That(p.Y, Is.EqualTo(-0.1 / 0.12).Within(1e-9), "carried back by the knock");
            Assert.That((m.Sample(5.3).X, m.Sample(5.3).Y), Is.EqualTo((0.0, -1.0)), "the step never happens");

            // A hit in heard time on a boss, too heavy to knock: its step still walks after it.
            var b = Spawned();
            b.Step(0, 0, 1, 0, 5.05, 5.15);
            b.Hit(5.0);
            Assert.That(b.Sample(5.02).ClipA, Is.EqualTo(Hit));
            Assert.That(b.Sample(5.1).ClipA, Is.EqualTo(Walk), "the step cuts in at its own time");
            Assert.That(b.Sample(5.1).X, Is.EqualTo(0.5).Within(1e-9));
        }

        /// <summary>The motion layers' clocks (MonsterTrack.Motion, neon-look.md's Motion): the latest step's plant and travel, the latest kick.</summary>
        [Test]
        public void TheMotionClocksFollowTheLatestStepAndKick()
        {
            var m = Spawned();
            var still = m.Sample(1.5).Motion;
            Assert.That((still.StepAge, still.KickAge, still.Seed), Is.EqualTo((PS.Look.Crowd.MonsterMotion.Long, PS.Look.Crowd.MonsterMotion.Long, 1u)));
            m.Step(0, 0, 1, 0, 2.0, 2.1);
            m.Lunge(3.0, 3.25, 0, 1);
            Assert.That(m.Sample(1.99).Motion.StepAge, Is.EqualTo(PS.Look.Crowd.MonsterMotion.Long), "nothing before the move");
            var moving = m.Sample(2.05).Motion;
            Assert.That(moving.StepAge, Is.EqualTo(-0.05f).Within(1e-5), "negative through the move");
            Assert.That(moving.StepMove, Is.EqualTo(0.1f).Within(1e-5));
            Assert.That((moving.Travel.x, moving.Travel.y), Is.EqualTo((1f, 0f)), "metres until the view scales it");
            Assert.That(m.Sample(2.1).Motion.StepAge, Is.EqualTo(0f).Within(1e-5), "the plant on the beat");
            var winding = m.Sample(3.1).Motion;
            Assert.That(winding.KickAge, Is.EqualTo(-0.15f).Within(1e-5));
            Assert.That(winding.KickWind, Is.EqualTo(0.25f).Within(1e-5));
            Assert.That((winding.Kick.x, winding.Kick.y), Is.EqualTo((0f, PS.Look.Crowd.CrowdMotion.LungeKick)), "toward the hero");
            Assert.That(winding.StepAge, Is.EqualTo(1.0f).Within(1e-5), "the step keeps settling through the lunge");

            m.Hit(4.0);
            var hit = m.Sample(4.0).Motion;
            Assert.That((hit.KickAge, hit.KickWind), Is.EqualTo((0f, 0f)));
            Assert.That(hit.Kick.y, Is.EqualTo(-PS.Look.Crowd.CrowdMotion.HitKick).Within(1e-5), "thrown back from the way it faces (the hero's)");
            Assert.That(hit.Kick.x, Is.EqualTo(0f).Within(1e-5));
        }

        [Test]
        public void AKnockKicksAlongItsPushAndDropsTheStepsItReplaces()
        {
            var m = Spawned();
            m.Step(0, 0, 1, 0, 2.0, 2.1);
            m.Step(1, 0, 2, 0, 5.05, 5.15);
            m.Hit(5.0);
            m.Knock(1, 0, 1, -2, 5.0, 5.12);
            var p = m.Sample(5.1).Motion;
            Assert.That(p.StepAge, Is.EqualTo(3.0f).Within(1e-5), "the replaced step never springs");
            Assert.That((p.Kick.x, p.Kick.y), Is.EqualTo((0f, -PS.Look.Crowd.CrowdMotion.KnockKick)), "along the push");
            Assert.That(p.KickAge, Is.EqualTo(0.1f).Within(1e-5));
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
