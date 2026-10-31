using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The bosses (unity-game.md, Enemies): a telegraph on a main beat, then a charge or a slam as
    /// a step on a later main beat; a slam breaks structures; a boss's death drops its chest, and
    /// on Easy its chickens.
    /// </summary>
    public class BossTests
    {
        const double Eps = 1e-9;

        static Simulation Start(double bpm, double until, ArenaData arena = null, bool easy = false)
        {
            var config = HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], heroStands: true);
            config.Easy = easy;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, until, bpm).Entries()
                         .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static int BeatIndex(Simulation sim, double t) => Array.FindIndex(sim.Stream.Beats.ToArray(), b => Math.Abs(b.T - t) < Eps);

        static List<SimEvent> Of(Simulation sim, int enemy, SimEventKind kind) =>
            Drive.Events(sim, kind).Where(e => e.I0 == enemy).ToList();

        [Test]
        public void ABossTelegraphsOnAMainBeatThenChargesAsAStepOnALaterOne()
        {
            var sim = Start(120, 20);
            var hero = sim.State.Hero;
            // About 8 m off when it telegraphs: beyond the slam, within the charge.
            int boss = HordeFixture.Place(sim, "boss-werewolf", hero.X + 9.5, hero.Y, boss: true);
            sim.StepTo(12);

            var rules = HordeFixture.Content.Schedule.BossMoves;
            var attacks = Of(sim, boss, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.EnemyKind != EnemyAttackKind.Lunge).ToList();
            Assert.IsNotEmpty(attacks);
            var charge = attacks.First();
            Assert.AreEqual(EnemyAttackKind.Charge, charge.EnemyKind, "the hero is beyond the slam");
            int telegraph = BeatIndex(sim, charge.T), impact = BeatIndex(sim, charge.Impact);
            Assert.GreaterOrEqual(telegraph, 0, "a telegraph on a main beat's footplant");
            Assert.AreEqual(telegraph + rules.TelegraphBeats, impact, "delivered TelegraphBeats later");

            // Every step plants on a main beat after a sharp move; none while the telegraph holds.
            var steps = Of(sim, boss, SimEventKind.EnemyStep).Select(e => e.AsEnemyStep).ToList();
            foreach (var s in steps)
            {
                int i = BeatIndex(sim, s.Footplant);
                Assert.GreaterOrEqual(i, 0, $"a footplant at {s.Footplant} on no main beat");
                Assert.LessOrEqual(s.Footplant - s.MoveStart, Math.Min(0.1, 0.2 * sim.Beats.PeriodOf(i)) + Eps);
                Assert.IsFalse(i > telegraph && i < impact, "no step while it telegraphs");
            }
            var dash = steps.Single(s => Math.Abs(s.Footplant - charge.Impact) < Eps);
            double len = Math.Sqrt((dash.ToX - dash.FromX) * (dash.ToX - dash.FromX) + (dash.ToY - dash.FromY) * (dash.ToY - dash.FromY));
            Assert.Greater(len, 3, "a charge covers far more than a step");

            // It stops at contact with the standing hero, and hits it on the beat.
            var hit = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.OnHero && h.EnemyKind == EnemyAttackKind.Charge).ToList();
            Assert.AreEqual(1, hit.Count(h => Math.Abs(h.T - charge.Impact) < Eps));
            Assert.Greater(Math.Abs(dash.ToX - hero.X), 0.5, "it stops at contact rather than running through");

            // The next telegraph comes a cycle later.
            if (attacks.Count > 1) Assert.AreEqual(telegraph + rules.EveryBeats, BeatIndex(sim, attacks[1].T));
        }

        [Test]
        public void ASlamOnItsBeatBreaksStructuresAndHurtsTheHeroInReach()
        {
            // A 40 x 40 tile arena, the hero on tile (20, 20) and a small structure two tiles east.
            var arena = ArenaData.Open(40, 40);
            arena.Structures = new[] { new StructureSite { Tiles = new[] { new Tile(22, 20) } } };
            var sim = Start(120, 20, arena);
            var hero = sim.State.Hero;
            int boss = HordeFixture.Place(sim, "boss-xlmummy", hero.X + 1.5, hero.Y, boss: true);
            sim.StepTo(10);

            var slam = Of(sim, boss, SimEventKind.Attack).Select(e => e.AsAttack).First(a => a.EnemyKind != EnemyAttackKind.Lunge);
            Assert.AreEqual(EnemyAttackKind.Slam, slam.EnemyKind, "the hero is within the slam");
            var breaks = Drive.Events(sim, SimEventKind.StructureBreak).Select(e => e.AsStructureBreak).ToList();
            Assert.AreEqual(1, breaks.Count);
            Assert.AreEqual(StructureHit.BossSlam, breaks[0].Source);
            Assert.AreEqual(slam.Impact, breaks[0].T, Eps, "on the slam's beat");
            Assert.IsFalse(sim.State.Structures[0].Standing);
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.OnHero && h.EnemyKind == EnemyAttackKind.Slam);
            Assert.That(hits.Select(h => h.T), Has.Some.EqualTo(slam.Impact).Within(Eps));
        }

        [Test]
        public void AMoveWhoseBeatComesFrozenFizzles()
        {
            var sim = Start(120, 20);
            var hero = sim.State.Hero;
            int boss = HordeFixture.Place(sim, "boss-werewolf", hero.X + 14, hero.Y, boss: true);
            AttackEvent charge = default;
            while (charge.Enemy != boss || charge.T == 0)
            {
                int cursor = sim.EventCount;
                sim.StepTo(sim.Now + Simulation.TickSeconds);
                foreach (var e in sim.EventsSince(cursor))
                    if (e.Kind == SimEventKind.Attack && e.I0 == boss) charge = e.AsAttack;
                Assert.Less(sim.Now, 10, "a telegraph comes");
            }
            sim.Control(RunControl.Pause);
            int from = sim.EventCount;
            sim.StepTo(charge.Impact + 0.5);
            Assert.IsFalse(sim.EventsSince(from).ToArray().Any(e => (e.Kind == SimEventKind.EnemyStep && e.I0 == boss) ||
                                                                     (e.Kind == SimEventKind.Hit && e.I0 < 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ABossDeathDropsItsChestAndOnEasyItsChickens(bool easy)
        {
            var sim = Start(120, 10, easy: easy);
            int before = sim.EventCount;
            sim.Horde.Spawn(sim, sim.Now, HordeFixture.Content.RoleIndex("boss-xlmantis"), 5, 5, boss: true, tier: 2, minute: 10);
            int boss = sim.EventsSince(before)[0].AsSpawn.Enemy;
            sim.Horde.Kill(sim, boss, sim.Now, 1, 0, 0);
            var drops = sim.EventsSince(before).ToArray().Where(e => e.Kind == SimEventKind.Drop).Select(e => e.AsDrop).ToList();
            var chest = drops.Single(d => d.ItemKind == ItemKind.Chest);
            Assert.AreEqual(2, chest.Value, "its tier");
            Assert.AreEqual(10, chest.Minute);
            Assert.AreEqual(0, drops.Count(d => d.ItemKind == ItemKind.Gem), "no gem without an arsenal");
            Assert.AreEqual(easy ? (int)HordeFixture.Content.Schedule.Easy.BossChickens : 0, drops.Count(d => d.ItemKind == ItemKind.Chicken));
        }
    }
}
