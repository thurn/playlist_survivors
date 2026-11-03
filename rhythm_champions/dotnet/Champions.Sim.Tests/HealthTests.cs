using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The hero's health (design.md, "The Hero", Decisions 3 and 15): 100, 0.5 s of invulnerability after a hit,
    /// a dash's i-frames, perfect dodges on the beat, heals and death; hits settling in heard time.
    /// </summary>
    public class HealthTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly double P = 60.0 / 128;

        static Simulation Sim(FixtureSong song = null) => DashTests.Sim(song);

        /// <summary>An attack landing on the hero at <paramref name="t"/>, given when the sim reaches it.</summary>
        static void Hit(Simulation sim, double t, double damage, double beat = double.NaN, HitSource source = HitSource.Touch)
        {
            sim.StepTo(t);
            sim.Health.Hurt(sim, new HeroHit { T = t, Damage = damage, Source = source, Owner = 7, Beat = beat, FromX = 1, FromY = 0 });
        }

        static List<SimEvent> Hits(Simulation sim) => Drive.Events(sim, SimEventKind.HeroHit);

        static HitOutcome Outcome(in SimEvent e) => (HitOutcome)e.I0;

        [Test]
        public void TheHeroHasAHundredAndCantBeHurtForHalfASecondAfterAHit()
        {
            var sim = Sim();
            Assert.AreEqual(100, sim.State.Hero.Health);
            Assert.AreEqual(100, sim.Snapshot().HeroMaxHealth);
            Hit(sim, 1, 5);
            Hit(sim, 1.3, 5);
            Hit(sim, 1.51, 20);
            sim.StepTo(2);
            var hits = Hits(sim);
            Assert.AreEqual(new[] { HitOutcome.Hurt, HitOutcome.Immune, HitOutcome.Hurt }, hits.Select(h => Outcome(h)).ToArray());
            Assert.AreEqual(5, hits[0].D0);
            Assert.AreEqual(95, hits[0].D1);
            Assert.AreEqual(0, hits[1].D0);
            Assert.AreEqual(75, sim.State.Hero.Health);
            Assert.AreEqual(1.51, sim.State.Hero.HurtAt, 1e-12);
            Assert.AreEqual(1.51, sim.Snapshot().HeroHurtAt, 1e-12);
            Assert.AreEqual((int)HitSource.Touch, hits[0].I1);
            Assert.AreEqual(7, hits[0].I2);
        }

        [Test]
        public void ADashsIFramesCoverItAndATenthOfASecondAfter()
        {
            foreach (var (kind, until) in new[] { ("dash", 1.25), ("leapSlam", 1.45) })
            {
                var sim = Sim();
                sim.Dashes.Equip(sim, DashTests.Content.Dashes.IndexOf(kind), 1);
                DashTests.Dash(sim, 1.0, lead: 0);
                Hit(sim, 1.0, 5);
                Hit(sim, until - 0.05, 5);
                Hit(sim, until, 5);
                Hit(sim, until + Tick, 5);
                sim.StepTo(2);
                var hits = Hits(sim);
                Assert.AreEqual(new[] { HitOutcome.Dodged, HitOutcome.Dodged, HitOutcome.Dodged, HitOutcome.Hurt }, hits.Select(h => Outcome(h)).ToArray(), kind);
                Assert.AreEqual(0, hits[0].D4, "the dash that dodged it");
                Assert.AreEqual(-1, hits[3].D4);
                Assert.AreEqual(95, sim.State.Hero.Health, kind);
            }
        }

        [Test]
        public void AHitSettlesOnceTheInputsHeardBeforeItAreIn()
        {
            // The dash was heard before the bite but reached the sim after it: it still dodges.
            var sim = Sim();
            Hit(sim, 2.0, 10);
            sim.StepTo(2.06);
            Assert.AreEqual(1, sim.Health.Pending);
            sim.Press(Key.Space, 1.98);
            sim.StepTo(3);
            Assert.AreEqual(HitOutcome.Dodged, Outcome(Hits(sim).Single()));
            Assert.AreEqual(100, sim.State.Hero.Health);
            // On Bluetooth the inputs lag 0.25 s: the settle follows them.
            sim = Sim();
            sim.StepTo(1);
            sim.Press(Key.Right, 0.75);
            sim.Release(Key.Right, 0.75);
            Assert.AreEqual(0.25 + Tick, sim.Health.SettleS, 1e-9);
            Hit(sim, 2.0, 10);
            sim.StepTo(2.23);
            sim.Press(Key.Space, 1.98);
            sim.StepTo(3);
            Assert.AreEqual(HitOutcome.Dodged, Outcome(Hits(sim).Single()));
            // A dash heard after the bite doesn't.
            sim = Sim();
            Hit(sim, 2.0, 10);
            sim.StepTo(2.06);
            sim.Press(Key.Space, 2.01);
            sim.StepTo(3);
            Assert.AreEqual(HitOutcome.Hurt, Outcome(Hits(sim).Single()));
            Assert.AreEqual(2.0, Hits(sim).Single().T, 1e-12, "it lands at its own time");
        }

        [Test]
        public void ADashCoveringABossAttacksLandingPressedOnItsBeatIsAPerfectDodge()
        {
            var song = new FixtureSong().Beats(0, 20, 128, onsets: false);
            double beat = 16 * P;
            HitOutcome Dodge(double heard, bool touch = false)
            {
                var sim = Sim(song);
                var settled = new List<HitOutcome>();
                sim.Health.Settled += (s, h, o) => settled.Add(o);
                Hit(sim, beat, 20, touch ? double.NaN : beat, touch ? HitSource.Touch : HitSource.Boss);
                sim.StepTo(Math.Max(sim.Now, heard + 0.05));
                sim.Press(Key.Space, heard);
                sim.StepTo(beat + 1);
                var outcome = Outcome(Hits(sim).Single());
                Assert.AreEqual(outcome, settled.Single(), "the bosses hear it");
                return outcome;
            }
            Assert.AreEqual(HitOutcome.PerfectDodge, Dodge(beat - 0.03), "on its beat, covering it");
            Assert.AreEqual(HitOutcome.PerfectDodge, Dodge(beat - 0.079));
            Assert.AreEqual(HitOutcome.Dodged, Dodge(beat - 0.15), "covering it, off the beat");
            Assert.AreEqual(HitOutcome.Hurt, Dodge(beat + 0.03), "on its beat, but the i-frames start after it lands");
            Assert.AreEqual(HitOutcome.Dodged, Dodge(beat - 0.03, touch: true), "a touch lands on no beat");
            // With no beat every press is on the beat: covering the landing is enough.
            var beatless = Sim();
            var plan = beatless.Telegraphs.Plan(1, 2);
            Hit(beatless, plan.Land, 20, plan.Land, HitSource.Boss);
            beatless.Press(Key.Space, plan.Land - 0.2);
            beatless.StepTo(4);
            Assert.AreEqual(HitOutcome.PerfectDodge, Outcome(Hits(beatless).Single()));
        }

        [Test]
        public void PickupHealingClampsAndABossKillHealsFully()
        {
            var sim = Sim();
            Hit(sim, 1, 30);
            Hit(sim, 2, 30);
            sim.StepTo(2.5);
            Assert.AreEqual(40, sim.State.Hero.Health);
            sim.Health.Heal(sim, 25, HealReason.Pickup);
            Assert.AreEqual(65, sim.State.Hero.Health);
            sim.Health.Heal(sim, 25, HealReason.Pickup);
            sim.Health.Heal(sim, 25, HealReason.Pickup);
            Assert.AreEqual(100, sim.State.Hero.Health, "never exceeds full health");
            Hit(sim, 3, 50);
            sim.StepTo(3.5);
            sim.Health.HealBossKill(sim);
            Assert.AreEqual(100, sim.State.Hero.Health);
            var heals = Drive.Events(sim, SimEventKind.HeroHeal);
            Assert.AreEqual(new[] { 25.0, 25.0, 10.0, 50.0 }, heals.Select(h => h.D0).ToArray(), "pickup and boss healing clamp");
            Assert.AreEqual((int)HealReason.BossKill, heals.Last().I0);
            Assert.AreEqual(0, sim.Health.Heal(sim, 5, HealReason.Mod), "a mod's heal at full gains nothing");
            Assert.AreEqual(4, Drive.Events(sim, SimEventKind.HeroHeal).Count);
        }

        [Test]
        public void TheLastOfItsHealthKillsTheHeroAndEndsTheRun()
        {
            var sim = Sim();
            sim.Press(Key.Right, 0);
            for (int i = 0; i < 5; i++) Hit(sim, 1 + i, 20);
            Hit(sim, 6.5, 20);
            sim.StepTo(8);
            var hits = Hits(sim);
            Assert.AreEqual(HitOutcome.Killed, Outcome(hits.Last()));
            Assert.AreEqual(5, hits.Count, "the hit after death settles nothing");
            Assert.AreEqual(0, sim.State.Hero.Health);
            Assert.IsTrue(sim.State.Hero.Dead);
            Assert.AreEqual(RunEnd.Death, sim.State.End);
            var death = Drive.Events(sim, SimEventKind.HeroDeath).Single();
            Assert.AreEqual(5.0, death.T, 1e-12, "on the killing hit's frame");
            Assert.AreEqual(6 * 5.0, death.D0, 1e-9, "where it was then");
            sim.State.Hero.PositionAt(sim.Now, out double x, out _);
            Assert.Less(x, 6 * 5.2, "it stands once dead");
            sim.Health.Heal(sim, 25, HealReason.Pickup);
            Assert.AreEqual(0, sim.State.Hero.Health, "a dead hero doesn't heal");
        }
    }
}
