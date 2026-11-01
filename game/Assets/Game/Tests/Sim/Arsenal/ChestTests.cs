using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The bosses' chests (unity-game.md, Pickups, Level-ups and Chests; native's survivors/chests.rs and
    /// engine/chests.rs): the drop, taking it clear of the crowd, the roulette's eighths and its landing on
    /// a downbeat, its rewards and which weapons may evolve.
    /// </summary>
    public class ChestTests
    {
        const double Eps = 1e-9;
        /// <summary>A brute with 50 health and no knockback, which nothing here hurts.</summary>
        const string Brute = "xlmummy";

        static GameContent Content => HordeFixture.Content;

        static Simulation Start((string, int)[] weapons, (string, int)[] passives = null, double bpm = 120, double until = 30)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = weapons, Passives = passives ?? new (string, int)[0], Cards = false };
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, until, bpm).Entries()
                         .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>A chest of <paramref name="tier"/> from <paramref name="minute"/>'s treasure dropped at the hero's feet now.</summary>
        static void DropAtHero(Simulation sim, int tier, int minute)
        {
            sim.State.Hero.PositionAt(sim.Now, out double x, out double y);
            sim.Chests.Drop(sim, sim.Now, x, y, tier, minute);
        }

        static List<ChestEvent> Chests(Simulation sim, ChestPhase? phase = null) =>
            Drive.Events(sim, SimEventKind.Chest).Select(e => e.AsChest).Where(c => phase == null || c.Phase == phase).ToList();

        static List<BeatEvent> Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat).Select(e => e.AsBeat).ToList();

        static int Rank(Simulation sim, string id)
        {
            foreach (var w in sim.State.WeaponSlots)
                if (w.Index == Content.WeaponIndex(id)) return w.Rank;
            return 0;
        }

        [Test]
        public void ABossChestFallsOnItsTileAndTheHeroTakesItOnlyClearOfTheCrowd()
        {
            var sim = Start(new[] { ("magic-wand", 1) });
            sim.StepTo(0.5);
            double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;
            int boss = sim.Horde.Spawn(sim, sim.Now, Content.RoleIndex("boss-xlmantis"), hx + 0.3, hy - 0.2, boss: true, tier: 2, minute: 10);
            int cursor = sim.EventCount;
            sim.Horde.Kill(sim, boss, sim.Now, 1, 0, 0);
            var drop = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.Drop).Select(e => e.AsDrop)
                .Single(d => d.ItemKind == ItemKind.Chest);
            Assert.AreEqual((hx, hy), (drop.X, drop.Y), "on the centre of the tile it died on, the hero's");
            Assert.AreEqual(2, drop.Value);
            Assert.AreEqual(10, drop.Minute);

            // A brute at the hero's side: the hero stands on the chest but isn't clear of the crowd.
            int brute = HordeFixture.Place(sim, Brute, hx + 1.2, hy);
            sim.StepTo(1);
            Assert.AreEqual(1, sim.State.FieldChests.Length, "not taken in the crowd");
            Assert.IsFalse(sim.State.Frozen);

            sim.Horde.Leave(sim, brute, sim.Now);
            double left = sim.Now;
            sim.StepTo(1.1);
            var open = Chests(sim, ChestPhase.Open).Single();
            Assert.AreEqual(left + Simulation.TickSeconds, open.T, Eps, "taken at the first tick's end clear of it");
            Assert.AreEqual((drop.Item, 2, 10), (open.Chest, open.Tier, open.Minute));
            Assert.AreEqual(0, sim.State.FieldChests.Length);
            Assert.IsTrue(sim.State.FrozenFor(FreezeReason.Chest));
            Assert.IsTrue(sim.State.Chest.Open);
            Assert.AreEqual(3, sim.State.Chest.Rewards, "a tier-2 chest holds three");
        }

        [Test]
        public void AChestFallingInAPocketTheHeroCannotEnterLandsWhereItCanWalk()
        {
            // A 40 x 40 arena, the hero on tile (20, 20); tile (25, 20) is open but walled in on its four sides.
            var arena = ArenaData.Open(40, 40);
            arena.Blocked = new[] { new Tile(24, 20), new Tile(26, 20), new Tile(25, 19), new Tile(25, 21) };
            var config = HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("magic-wand", 1) }, Cards = false };
            var sim = new Simulation(config);
            var grid = sim.Arena;
            double px = grid.CentreX(25), py = grid.CentreY(20);
            int cursor = sim.EventCount;
            sim.Chests.Drop(sim, sim.Now, px, py, 1, 10);
            var drop = sim.EventsSince(cursor).ToArray().Select(e => e.AsDrop).Single();
            Assert.AreEqual(grid.TileM * Math.Sqrt(2), Math.Sqrt((drop.X - px) * (drop.X - px) + (drop.Y - py) * (drop.Y - py)), Eps,
                "a corner of the pocket's ring, the nearest open tiles joined to the hero's");
        }

        [Test]
        public void TheRouletteTicksOnEightEighthsFromTheNextDownbeatAndLandsOnTheDownbeatAfter()
        {
            var sim = Start(new[] { ("magic-wand", 1) });
            sim.StepTo(4.13);
            DropAtHero(sim, 3, 10);
            sim.StepTo(4.15);
            double opened = Chests(sim, ChestPhase.Open).Single().T;
            double clock = sim.State.RunClock;
            sim.StepTo(12);

            var beats = Beats(sim);
            double first = beats.First(b => b.T > opened && b.Downbeat).T;
            var ticks = Chests(sim, ChestPhase.Tick);
            Assert.AreEqual(Enumerable.Range(0, 8), ticks.Select(t => t.Tick));
            for (int k = 0; k < 8; k++) Assert.AreEqual(first + 0.25 * k, ticks[k].T, 1e-6, $"tick {k} on an eighth from the next downbeat");
            var land = Chests(sim, ChestPhase.Land).Single();
            Assert.AreEqual(first + 2, land.T, 1e-6, "two two-beat bars after the first tick");
            Assert.IsTrue(beats.Single(b => Math.Abs(b.T - land.T) < 1e-6).Downbeat, "it lands on a downbeat");

            var freezes = Drive.Events(sim, SimEventKind.Freeze).Select(e => e.AsFreeze).Where(f => f.Reason == FreezeReason.Chest).ToList();
            Assert.AreEqual(2, freezes.Count);
            Assert.AreEqual((opened, true), (freezes[0].T, freezes[0].Starts));
            Assert.AreEqual((land.T, false), (freezes[1].T, freezes[1].Starts), "play resumes on the landing's own beat");
            Assert.AreEqual(clock + (12 - land.T), sim.State.RunClock, 1e-6, "the run clock stood still through the spin");
            Assert.IsFalse(sim.State.Chest.Open);
            Assert.AreEqual(land.T, sim.State.Chest.Landed, Eps);
        }

        [Test]
        public void ATierThreeChestGivesFiveRewardsInTurnAtItsLanding()
        {
            // native's chest_two_bars test: five ranks of the one weapon owned, 1 to 6.
            var sim = Start(new[] { ("magic-wand", 1) });
            sim.StepTo(0.13);
            DropAtHero(sim, 3, 3);
            sim.StepTo(6);
            var land = Chests(sim, ChestPhase.Land).Single();
            Assert.AreEqual(5, land.Rewards);
            var rewards = Chests(sim, ChestPhase.Reward);
            Assert.AreEqual(Enumerable.Range(0, 5), rewards.Select(r => r.Place));
            Assert.IsTrue(rewards.All(r => r.T == land.T));
            int wand = Content.WeaponIndex("magic-wand");
            for (int k = 0; k < 5; k++)
            {
                var card = rewards[k].Reward;
                Assert.AreEqual((CardItem.Weapon, wand, 1 + k, 2 + k), (card.Item, card.Index, card.Before, card.After));
                Assert.AreEqual(card.Index, sim.State.Chest.Reward(k).Index);
            }
            Assert.AreEqual(6, Rank(sim, "magic-wand"));
        }

        [Test]
        public void RewardsRankTheLowestOwnedWeaponThenPassiveThenHealAndNeverBringANewItem()
        {
            var sim = Start(new[] { ("magic-wand", 8), ("king-bible", 7) }, new[] { ("spinach", 4), ("armour", 1) });
            int bible = Content.WeaponIndex("king-bible"), armor = Content.PassiveIndex("armour"), spinach = Content.PassiveIndex("spinach");
            var chests = sim.Chests;
            Assert.AreEqual((CardItem.Weapon, bible, 7), (chests.Item(false).Item, chests.Item(false).Index, chests.Item(false).Before));
            sim.Arsenal.Learn(bible);
            Assert.AreEqual((CardItem.Passive, armor), (chests.Item(false).Item, chests.Item(false).Index), "the lowest-ranked passive");
            for (int r = 1; r < Content.Passives[armor].MaxRank; r++) sim.Arsenal.LearnPassive(0, armor);
            Assert.AreEqual((CardItem.Passive, spinach), (chests.Item(false).Item, chests.Item(false).Index));
            sim.Arsenal.LearnPassive(0, spinach);
            Assert.AreEqual(CardItem.Heal, chests.Item(true).Item, "everything owned is at its top, and no partner is owned");
        }

        [Test]
        public void OnlyAnEvolvingMinutesChestEvolvesAWeaponAtItsTopRankWithItsPartnerInEvolutionOrder()
        {
            // The sword and Garlic at their top; only Garlic's partner (Pummarola) owned; the wand below its top with its partner.
            var sim = Start(new[] { ("sword", 8), ("garlic", 8), ("magic-wand", 7) }, new[] { ("pummarola", 1), ("empty-tome", 1) });
            int garlic = Content.WeaponIndex("garlic"), soul = Content.WeaponIndex("soul-eater"), wand = Content.WeaponIndex("magic-wand");
            int holy = Content.WeaponIndex("holy-wand");
            Assert.AreEqual(-1, sim.Arsenal.EvolutionOf(Content.WeaponIndex("sword")), "no Hollow Heart");
            Assert.AreEqual(soul, sim.Arsenal.EvolutionOf(garlic));
            Assert.AreEqual(-1, sim.Arsenal.EvolutionOf(wand), "below its top rank");
            Assert.AreEqual(wand, sim.Chests.Item(false).Index, "a chest that may not evolve ranks the wand");

            sim.StepTo(0.13);
            DropAtHero(sim, 1, 10);
            sim.StepTo(6);
            var reward = Chests(sim, ChestPhase.Reward).Single().Reward;
            Assert.AreEqual((CardItem.Weapon, soul, 0, 1), (reward.Item, reward.Index, reward.Before, reward.After), "minute 10's chest evolves Garlic");
            Assert.AreEqual(soul, sim.State.WeaponSlots[1].Index, "in Garlic's own slot");
            Assert.AreEqual(1, sim.State.WeaponSlots[1].Rank);
            Assert.IsTrue(sim.Arsenal.OwnsEvolutionOf(garlic));
            Assert.AreEqual(-1, sim.Arsenal.EvolutionOf(garlic), "evolved once");

            // The wand reaches its top: the next evolving chest takes it, the sword still waiting for its partner.
            sim.Arsenal.Learn(wand);
            Assert.AreEqual(holy, sim.Chests.Item(true).Index);
            Assert.AreEqual(CardItem.Passive, sim.Chests.Item(false).Item, "nothing evolves from minute 3's");
        }

        [Test]
        public void ARouletteHeldByAPauseGoesOnFromTheNextEighthAfterIt()
        {
            var sim = Start(new[] { ("magic-wand", 1) });
            sim.StepTo(4.13);
            DropAtHero(sim, 1, 3);
            sim.StepTo(5.6);
            int before = Chests(sim, ChestPhase.Tick).Count;
            Assert.Greater(before, 0);
            sim.Control(RunControl.Pause);
            sim.StepTo(7.3);
            Assert.AreEqual(before, Chests(sim, ChestPhase.Tick).Count, "no ticks while paused");
            sim.Control(RunControl.Resume);
            double resumed = sim.Now;
            sim.StepTo(12);
            var ticks = Chests(sim, ChestPhase.Tick);
            Assert.AreEqual(8, ticks.Count);
            double replanned = resumed + Simulation.TickSeconds;
            Assert.AreEqual(Math.Ceiling(replanned / 0.25 - 1e-6) * 0.25, ticks[before].T, 1e-6, "the first eighth after the pause lets go");
            foreach (var t in ticks) Assert.AreEqual(Math.Round(t.T / 0.25), t.T / 0.25, 1e-6, "every tick on an eighth");
            var land = Chests(sim, ChestPhase.Land).Single();
            Assert.IsTrue(Beats(sim).Single(b => Math.Abs(b.T - land.T) < 1e-6).Downbeat);
        }

        [Test]
        public void TheDebugChestIsATierThreeChestOfTheLastEvolvingMinuteTwoTilesAheadOfTheHero()
        {
            var sim = Start(new[] { ("magic-wand", 1) });
            sim.StepTo(1);
            double x = sim.State.Hero.X, y = sim.State.Hero.Y;
            sim.Command(DebugCommand.Chest);
            sim.StepTo(1.5);
            var chest = sim.State.FieldChests.ToArray().Single();
            Assert.AreEqual((3, 25), (chest.Tier, chest.Minute), "schedule.json's last evolving minute");
            Assert.AreEqual((x, y - 2 * sim.Arena.TileM), (chest.X, chest.Y), "the hero faces down at the start");
            Assert.AreEqual(0, Chests(sim, ChestPhase.Open).Count, "it lies there until the hero walks onto it");
        }

        [Test]
        public void LevelsReachedBeforeALandingOpenTheirCardsAtIt()
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("magic-wand", 1) } };
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 30, 120).Entries()
                         .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(0.13);
            DropAtHero(sim, 1, 3);
            sim.StepTo(0.5);
            Assert.IsTrue(sim.State.Chest.Open);
            // A level reached while the roulette spins (as a gem already in flight would bring) waits for the landing.
            sim.Arsenal.LevelUps.Gain(sim, sim.Now, 1000);
            Assert.IsFalse(sim.State.Offer.Open, "no cards over the roulette");
            sim.StepTo(6);
            var land = Chests(sim, ChestPhase.Land).Single();
            var levelUp = Drive.Events(sim, SimEventKind.LevelUp).First();
            Assert.AreEqual(land.T, levelUp.T, Eps, "its cards open at the landing");
            Assert.IsTrue(sim.State.FrozenFor(FreezeReason.LevelUp));
            Assert.IsFalse(sim.State.FrozenFor(FreezeReason.Chest));
        }
    }
}
