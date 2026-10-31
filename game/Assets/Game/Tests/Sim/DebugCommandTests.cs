using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The debug commands (unity-game.md, Debug Surfaces): a forced section change lands on the next
    /// downbeat as a detected one, a forced accent makes the next main strike heavy, and structures break
    /// and regrow on demand; in the run, the clock jumps, enemies and bosses come, the arsenal grows and
    /// the hero can't be hurt; and a run starts at a minute built for it. Each is recorded like a key.
    /// </summary>
    public class DebugCommandTests
    {
        /// <summary>Beats and ticks at 120 BPM only: no onsets, forecasts, changes or jumps.</summary>
        static List<KnownEntry> Plain(double until) =>
            Drive.Known(new FixtureSong(3).Beats(0, until, 120).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        /// <summary>A 40 x 40 open arena (the hero on tile (20, 20)): a large 3 x 3 structure about 5 m east, a small one 20 m east.</summary>
        static ArenaData Arena()
        {
            var arena = ArenaData.Open(40, 40);
            var near = new List<Tile>();
            for (int y = 19; y < 22; y++)
                for (int x = 24; x < 27; x++) near.Add(new Tile(x, y));
            arena.Structures = new[]
            {
                new StructureSite { Tiles = near.ToArray(), Large = true },
                new StructureSite { Tiles = new[] { new Tile(34, 20) } },
            };
            return arena;
        }

        static Simulation Start(int until = 30)
        {
            var sim = new Simulation(HordeFixture.Config(minimum: 0, arena: Arena(), roster: new string[0], sword: 1, heroStands: true));
            foreach (var k in Plain(until)) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static double[] Downbeats(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Beat).Select(e => e.AsBeat).Where(b => b.Downbeat).Select(b => b.T).ToArray();

        static List<AttackEvent> HeroAttacks(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.ByHero).ToList();

        [Test]
        public void AForcedSectionChangeLandsOnTheNextDownbeatWithASlam()
        {
            var reference = Start();
            reference.StepTo(30);
            Assert.IsEmpty(Drive.Events(reference, SimEventKind.ZoneChange), "no change in the music, none before the fallback");
            double downbeat = Downbeats(reference).First(t => t > 6);

            var sim = Start();
            sim.StepTo(downbeat - 1);
            sim.Command(DebugCommand.SectionChange);
            sim.StepTo(30);
            var change = Drive.Events(sim, SimEventKind.ZoneChange).Select(e => e.AsZoneChange).Single();
            Assert.AreEqual(downbeat, change.T);
            Assert.AreEqual(ZoneCause.Change, change.Cause);
            Assert.AreEqual(1, change.Zone);
            var slams = HeroAttacks(sim).Where(a => a.SwordKind == HeroAttackKind.Slam).ToList();
            Assert.AreEqual(1, slams.Count);
            Assert.AreEqual(downbeat, slams[0].Impact, "the slam shares the zone change's downbeat");
        }

        [Test]
        public void AForcedAccentMakesTheNextMainStrikeHeavy()
        {
            var sim = Start();
            sim.StepTo(8.1);
            Assert.IsFalse(HeroAttacks(sim).Any(a => a.Heavy), "plain beats have no accents");
            sim.Command(DebugCommand.Accent);
            sim.StepTo(20);
            var after = HeroAttacks(sim).Where(a => a.WindupStart >= 8.1).ToList();
            var heavy = after.Where(a => a.Heavy).ToList();
            Assert.AreEqual(1, heavy.Count, "one heavy strike");
            Assert.AreEqual(after.First(a => a.SwordKind != HeroAttackKind.FollowUp).Impact, heavy[0].Impact,
                "the first main strike to wind up after the command");
            Assert.AreEqual(HeroAttackKind.Spin, heavy[0].SwordKind);
        }

        [Test]
        public void BreakingStructuresBreaksThoseNearTheHeroAtOnceAndRegrowingThemWaitsForADownbeat()
        {
            var sim = Start();
            sim.StepTo(3);
            Assert.AreEqual(3, sim.State.Structures[0].HitsToBreak);
            int cursor = sim.EventCount;
            sim.Command(DebugCommand.BreakStructures);
            var breaks = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.StructureBreak).Select(e => e.AsStructureBreak).ToList();
            Assert.AreEqual(1, breaks.Count, "the large structure in reach, in one hit; the far one stands");
            Assert.AreEqual((0, StructureHit.Debug, 3.0), (breaks[0].Structure, breaks[0].Source, breaks[0].T));
            Assert.IsFalse(sim.State.Structures[0].Standing);
            Assert.IsTrue(sim.State.Structures[1].Standing);
            Assert.IsTrue(sim.Arena.IsOpen(25, 20), "its tiles open at once");

            sim.StepTo(5.1);
            double regrow = sim.Now;
            sim.Command(DebugCommand.RegrowStructures);
            sim.StepTo(10);
            var regrowth = Drive.Events(sim, SimEventKind.Regrowth).Select(e => e.AsRegrowth).Single();
            Assert.AreEqual(Downbeats(sim).First(t => t > regrow), regrowth.T);
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.ZoneChange), "no zone change");
            Assert.IsTrue(sim.State.Structures[0].Standing);

            CollectionAssert.AreEqual(new[] { (DebugCommand.BreakStructures, 3.0), (DebugCommand.RegrowStructures, regrow) },
                sim.Commands.ToArray().Select(c => (c.Command, c.Effective)), "recorded at the sim's time, like keys");
        }

        static GameContent Content => HordeFixture.Content;
        static readonly double[] Footprint = { -18.5, -9.25, 18.5, -9.25, 24.75, 15.5, -24.75, 15.5 };

        /// <summary>The run's level on an open arena at 120 BPM, its cards taken as a run nobody plays takes them.</summary>
        static Simulation StartRun(int startMinute = 0, int until = 90)
        {
            var config = RunLevel.Config(Content, null, Footprint);
            config.StartMinute = startMinute;
            config.HeroStands = true;
            var sim = new Simulation(config);
            foreach (var k in Plain(until)) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>Steps the run to <paramref name="t"/> a frame at a time, confirming cards as they open.</summary>
        static void PlayTo(Simulation sim, double t)
        {
            while (sim.Now < t - 1e-9)
            {
                AutoCards.Confirm(sim);
                sim.StepTo(Math.Min(t, sim.Now + 1.0 / 60));
            }
        }

        /// <summary>The bosses that landed, but the signature minotaur, which comes from 1:30 of run clock whatever the minute.</summary>
        static List<SpawnEvent> Bosses(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Spawn).Select(e => e.AsSpawn)
                .Where(e => e.Boss && !e.Recycled && Content.Roles[e.Role].Id != Content.Schedule.Signature.Role).ToList();

        static int Rank(Simulation sim, string weapon) =>
            sim.State.WeaponSlots.ToArray().Where(w => w.Index == Content.WeaponIndex(weapon)).Select(w => w.Rank).FirstOrDefault();

        [Test]
        public void TheRunClockJumpsAheadAndOnlyTheNewMinutesBossComes()
        {
            var sim = StartRun();
            int wand = Content.WeaponIndex("magic-wand");
            sim.Command(DebugCommand.GrantWeapon, wand);
            PlayTo(sim, 5);
            double clock = sim.State.RunClock;
            int Shots() => HeroAttacks(sim).Count(a => a.Weapon == wand);
            int shots = Shots();
            Assert.Greater(shots, 2);
            sim.Command(DebugCommand.JumpClock, 600);
            Assert.AreEqual(600, sim.State.RunClock, 1e-9);
            PlayTo(sim, 15);
            Assert.That(sim.State.RunClock, Is.GreaterThan(600).And.LessThanOrEqualTo(610 + 1e-6), "on from 10:00 as the run plays (but for level-ups)");
            CollectionAssert.AreEqual(new[] { "boss-xlmantis" }, Bosses(sim).Select(b => Content.Roles[b.Role].Id),
                "minute 10's boss, none of the minutes jumped over");
            var boss = Bosses(sim)[0];
            Assert.That(Downbeats(sim), Has.Some.EqualTo(boss.T).Within(1e-9), "on a downbeat");
            Assert.Less(Shots() - shots, 2 * shots * 10 / clock, "the wand fires on at its pace: no burst of the shots jumped over");
            sim.Command(DebugCommand.JumpClock, 300);
            Assert.Greater(sim.State.RunClock, 600, "never back");
        }

        [Test]
        public void ARoleSpawnsInViewAtOnceAndABossLandsOnTheNextDownbeat()
        {
            var sim = StartRun();
            PlayTo(sim, 3);
            int cursor = sim.EventCount;
            sim.Command(DebugCommand.SpawnRole, Content.RoleIndex("werewolf"));
            var spawn = sim.EventsSince(cursor).ToArray().Single().AsSpawn;
            Assert.AreEqual(("werewolf", false, sim.Now), (Content.Roles[spawn.Role].Id, spawn.Boss, spawn.T));
            Assert.IsTrue(sim.Horde.Spawner.InView(spawn.X, spawn.Y, sim.State.Hero.X, sim.State.Hero.Y, 1), "in view");

            double asked = sim.Now;
            sim.Command(DebugCommand.SpawnBoss, Content.RoleIndex("boss-werewolf"));
            PlayTo(sim, 8);
            var boss = Bosses(sim).Single();
            Assert.AreEqual("boss-werewolf", Content.Roles[boss.Role].Id);
            Assert.AreEqual(Downbeats(sim).First(t => t > asked), boss.T, 1e-9, "on the next downbeat");
        }

        [Test]
        public void GrantsRankUpTheArsenalAndAnEvolutionBringsWhatItNeeds()
        {
            var sim = StartRun();
            PlayTo(sim, 1);
            sim.Command(DebugCommand.GrantWeapon, Content.WeaponIndex("magic-wand"));
            sim.Command(DebugCommand.GrantWeapon, Content.WeaponIndex("magic-wand"));
            sim.Command(DebugCommand.GrantPassive, Content.PassiveIndex("spinach"));
            sim.Command(DebugCommand.GrantWeapon, Content.WeaponIndex("holy-wand"));
            Assert.AreEqual(2, Rank(sim, "magic-wand"));
            Assert.AreEqual(0, Rank(sim, "holy-wand"), "an evolution isn't a card's");
            Assert.AreEqual(1, sim.Arsenal.PassiveRank(Content.PassiveIndex("spinach")));

            sim.Command(DebugCommand.GrantEvolution, Content.WeaponIndex("bloody-tear"));
            Assert.AreEqual(1, Rank(sim, "bloody-tear"));
            Assert.AreEqual(0, Rank(sim, "sword"), "the evolution takes the sword's slot");
            Assert.AreEqual(1, sim.Arsenal.PassiveRank(Content.PassiveIndex("hollow-heart")), "its partner");
            sim.Command(DebugCommand.GrantWeapon, Content.WeaponIndex("sword"));
            Assert.AreEqual(0, Rank(sim, "sword"), "nor is the base of an evolution owned");
            PlayTo(sim, 4);
            Assert.That(HeroAttacks(sim).Where(a => a.WindupStart > 1), Is.Not.Empty, "the evolved blade strikes");
        }

        [Test]
        public void AnInvulnerableHeroTakesHitsForNothing()
        {
            var sim = StartRun();
            PlayTo(sim, 1);
            var hero = sim.State.Hero;
            double full = hero.Health;
            sim.Command(DebugCommand.Invulnerable, 1);
            sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 30, 1, 0);
            Assert.AreEqual(full, hero.Health, "nothing taken");
            sim.Command(DebugCommand.Invulnerable, 0);
            PlayTo(sim, 2);
            sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 30, 1, 0);
            Assert.Less(hero.Health, full, "hurt again");
            CollectionAssert.AreEqual(new[] { (DebugCommand.Invulnerable, 1), (DebugCommand.Invulnerable, 0) },
                sim.Commands.ToArray().Select(c => (c.Command, c.Arg)), "recorded with their arguments");
        }

        [Test]
        public void ARunStartedAtMinute25IsBuiltToEvolveAndMeetsOnlyItsMinutesBoss()
        {
            var sim = StartRun(startMinute: 25);
            Assert.AreEqual(1500, sim.State.RunClock);
            Assert.AreEqual(Content.Progression.StartLevel(25), sim.State.Level);
            Assert.AreEqual(27, sim.State.Level, "progression.json's start level at minute 25");
            Assert.AreEqual(sim.State.Level, sim.State.Hero.Level, "bosses scale by it");
            Assert.AreEqual(Content.Progression.Experience.Total(26), sim.State.Experience, "at the level's start");
            Assert.AreEqual(Content.WeaponIndex("bloody-tear"), sim.Arsenal.EvolutionOf(Content.WeaponIndex("sword")));
            Assert.AreEqual(Content.WeaponIndex("soul-eater"), sim.Arsenal.EvolutionOf(Content.WeaponIndex("garlic")));
            var reward = sim.Chests.Item(evolves: true);
            Assert.IsTrue(reward.Item == CardItem.Weapon && Content.Weapons[reward.Index].IsEvolution, "a chest now evolves");

            PlayTo(sim, 12);
            Assert.AreEqual(RunPhase.Playing, sim.State.Phase);
            CollectionAssert.AreEqual(new[] { "boss-xlflower" }, Bosses(sim).Select(b => Content.Roles[b.Role].Id), "minute 25's boss");
            var boss = Bosses(sim)[0];
            double taken = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target == boss.Enemy && h.T >= boss.T).Sum(h => h.Damage);
            Assert.IsTrue(sim.State.Enemies[boss.Enemy].Alive);
            Assert.AreEqual(Content.Roles[boss.Role].Health * 27, sim.State.Enemies[boss.Enemy].Health + taken, 1e-6,
                "it landed with its health times the level");
            Assert.AreEqual(1512, sim.State.RunClock, 1e-6);
            Assert.AreEqual(0, sim.State.Offer.Serial, "the build opened no cards");
        }
    }
}
