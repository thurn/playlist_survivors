using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Bots;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The sweeps' bots (unity-game.md, Tools, Checks and Evidence: bots and sweeps): native's
    /// positioning bot on the grid, and the idle bot.
    /// </summary>
    public class BotTests
    {
        static List<KeyRecord> Presses(Simulation sim) => sim.Keys.ToArray().Where(k => k.Down).ToList();

        /// <summary>The run's level on the first 20 s of house-128, played by <paramref name="kind"/> to its end.</summary>
        static BotReport Play(BotKind kind, out Simulation sim)
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var config = RunLevel.Config(HordeFixture.Content, HordeFixture.GameArena(), RunLevel.GameFootprint);
            config.Start = RunStart.AfterClearBeat;
            return BotRun.Play(config, entries, new SurvivorBot(kind, 1, HordeFixture.Content), duration, double.PositiveInfinity, out sim);
        }

        [Test]
        public void ThePositioningBotFleesTheCloseCrowdEvenFromAGem()
        {
            var sim = HordeFixture.Empty();
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var hero = sim.State.Hero;
            HordeFixture.Place(sim, "zombie", hero.X + 2, hero.Y + 0.5);
            bot.Observe(new[] { SimEvent.Drop(0, 1, ItemKind.Gem, hero.X + 4, hero.Y, 1) });
            bot.Act(sim);
            Assert.AreEqual(new[] { Key.Left }, Presses(sim).Select(k => k.Key).ToArray());
        }

        [Test]
        public void ClearOfTheCrowdItWalksToTheNearestGemUntilItTakesOff()
        {
            var sim = HordeFixture.Empty();
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var hero = sim.State.Hero;
            bot.Observe(new[]
            {
                SimEvent.Drop(0, 1, ItemKind.Gem, hero.X + 1, hero.Y + 6, 1),
                SimEvent.Drop(0, 2, ItemKind.Gem, hero.X - 9, hero.Y, 1),
            });
            bot.Act(sim);
            Assert.AreEqual(Key.Up, Presses(sim).Last().Key);
            bot.Observe(new[] { SimEvent.GemFlight(0, 1, hero.X + 1, hero.Y + 6) });
            sim.StepTo(1); // the next stride step
            bot.Act(sim);
            Assert.AreEqual(Key.Left, Presses(sim).Last().Key, "the other gem, once the first took off");
        }

        [Test]
        public void ItStepsOffTheMinotaursMarkBeforeItsSlamLands()
        {
            var sim = HordeFixture.Empty();
            var bot = new SurvivorBot(BotKind.Positioning, 1, HordeFixture.Content);
            var hero = sim.State.Hero;
            int minotaur = HordeFixture.Place(sim, "minotaur", hero.X + 15, hero.Y); // out of the crowd's reach
            // A gem under the hero would keep it standing, as a slam's mark finds it.
            bot.Observe(new[] { SimEvent.Drop(0, 1, ItemKind.Gem, hero.X, hero.Y, 1) });
            double t = 0;
            for (; t < 2; t += 1.0 / 60)
            {
                bot.Act(sim);
                sim.StepTo(t);
            }
            Assert.AreEqual(0, Presses(sim).Count, "it stands on the gem");
            double markX = hero.X, markY = hero.Y, impact = t + 1;
            bot.Observe(new[] { SimEvent.EnemyAttack(t, minotaur, EnemyAttackKind.Slam, t, impact, 1, 0, markX, markY) });
            for (; t < impact; t += 1.0 / 60)
            {
                bot.Act(sim);
                sim.StepTo(t);
            }
            hero.PositionAt(impact, out double x, out double y);
            double reach = HordeFixture.Content.Schedule.Signature.SlamRadiusM + Horde.HeroHurtRadiusM;
            Assert.That(Math.Sqrt((x - markX) * (x - markX) + (y - markY) * (y - markY)), Is.GreaterThan(reach), "off the mark as the slam lands");
        }

        [Test]
        public void ABlockedTileGivesWayToTheAimsOtherAxis()
        {
            var arena = ArenaData.Open();
            arena.Blocked = new[] { new Tile(arena.HeroStart.X - 1, arena.HeroStart.Y) };
            var sim = HordeFixture.Empty(arena);
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var hero = sim.State.Hero;
            HordeFixture.Place(sim, "zombie", hero.X + 2, hero.Y + 1);
            bot.Act(sim);
            Assert.AreEqual(new[] { Key.Down }, Presses(sim).Select(k => k.Key).ToArray(), "left is blocked: round it, away from the enemy's side");
        }

        [Test]
        public void ItWalksOutOfAPocketAndRoundItToAGemBehindItsBack()
        {
            // The hero's tile is open only to the right; the gem lies 9 m to the left, behind the wall.
            var arena = ArenaData.Open();
            int x0 = arena.HeroStart.X, y0 = arena.HeroStart.Y;
            arena.Blocked = new[]
            {
                new Tile(x0 - 1, y0 - 1), new Tile(x0 - 1, y0), new Tile(x0 - 1, y0 + 1),
                new Tile(x0, y0 - 1), new Tile(x0, y0 + 1),
            };
            var sim = HordeFixture.Empty(arena);
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var hero = sim.State.Hero;
            bot.Observe(new[] { SimEvent.Drop(0, 1, ItemKind.Gem, hero.X - 9, hero.Y, 1) });
            for (double t = 1.0 / 60; t < 10; t += 1.0 / 60)
            {
                bot.Act(sim);
                sim.StepTo(t);
            }
            Assert.AreEqual((x0 - 6, y0), (hero.TileX, hero.TileY), "on the gem's tile, round the wall");
        }

        [Test]
        public void ItForgetsAGemOutOfReachAndWalksToTheNextOne()
        {
            // The nearer gem lies inside a walled block; standing still at the wall's foot is the
            // nearest it can get, so it takes the farther one instead.
            var arena = ArenaData.Open();
            int x0 = arena.HeroStart.X, y0 = arena.HeroStart.Y;
            var blocked = new List<Tile>();
            for (int x = x0 - 6; x <= x0 - 2; x++)
                for (int y = y0 - 2; y <= y0 + 2; y++)
                    blocked.Add(new Tile(x, y));
            arena.Blocked = blocked.ToArray();
            var sim = HordeFixture.Empty(arena);
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var hero = sim.State.Hero;
            bot.Observe(new[]
            {
                SimEvent.Drop(0, 1, ItemKind.Gem, sim.Arena.CentreX(x0 - 4), sim.Arena.CentreY(y0), 1),
                SimEvent.Drop(0, 2, ItemKind.Gem, sim.Arena.CentreX(x0 + 6), sim.Arena.CentreY(y0), 1),
            });
            for (double t = 1.0 / 60; t < 10; t += 1.0 / 60)
            {
                bot.Act(sim);
                sim.StepTo(t);
            }
            Assert.AreEqual((x0 + 6, y0), (hero.TileX, hero.TileY), "on the reachable gem's tile");
        }

        [Test]
        public void ItSteersOnceAStepJustBeforeItAndStridesOnceAStep()
        {
            var entries = EntryDump.Load(EntryDump.House128, out _);
            var config = RunLevel.Config(HordeFixture.Content, HordeFixture.GameArena(), RunLevel.GameFootprint);
            config.Start = RunStart.AfterClearBeat;
            var sim = new Simulation(config);
            var bot = new SurvivorBot(BotKind.Positioning, 1);
            var moves = new List<HeroStepEvent>();
            var steps = new List<double>();
            int cursor = 0;
            Drive.Feed(sim, entries, 20, eachFrame: s =>
            {
                var events = s.EventsSince(cursor);
                bot.Observe(events);
                foreach (var e in events)
                {
                    if (e.Kind == SimEventKind.HeroStep) moves.Add(e.AsHeroStep);
                    else if (e.Kind == SimEventKind.StrideStep) steps.Add(e.T);
                }
                cursor = s.EventCount;
                bot.Act(s);
            });
            Assert.That(moves.Count, Is.GreaterThan(20), "it moves");
            Assert.That(moves.Count(m => m.Pressed), Is.GreaterThan(0), "and turns");
            Assert.AreEqual(moves.Count, moves.Select(m => m.Step).Distinct().Count(), "never twice on one step");
            var presses = Presses(sim);
            var cards = presses.Where(k => k.Key == Key.Enter).Select(k => k.Effective).ToList();
            foreach (var press in presses.Where(k => !cards.Contains(k.Effective)))
            {
                double next = steps.FirstOrDefault(t => t >= press.Effective - 1e-9);
                if (next > 0) Assert.That(next - press.Effective, Is.LessThanOrEqualTo(Hero.StrideSeconds), "a turn takes the coming step");
            }
        }

        [Test]
        public void ThePositioningBotPlaysTheRunAndTakesTheFirstCardOfEachLevel()
        {
            var report = Play(BotKind.Positioning, out var sim);
            var again = Play(BotKind.Positioning, out _);
            Assert.AreEqual(report.Hash, again.Hash, "the same run twice");
            Assert.IsFalse(report.Fallen);
            Assert.That(report.Level, Is.GreaterThan(1), "it levelled up");
            var keys = sim.Keys.ToArray();
            var enters = keys.Where(k => k.Down && k.Key == Key.Enter).ToList();
            Assert.That(enters.Count, Is.GreaterThan(0));
            Assert.AreEqual(report.Level - 1 - sim.State.PendingLevels, enters.Count, "one card a level, but those still to come");
            foreach (var enter in enters)
                Assert.IsTrue(keys.Any(k => k.Down && k.Key == Key.Left && k.Effective == enter.Effective), "the first card, with Left");
            // Never two arrows down at once.
            var down = new HashSet<Key>();
            foreach (var k in keys)
            {
                if (k.Key == Key.Enter) continue;
                if (k.Down) down.Add(k.Key);
                else down.Remove(k.Key);
                Assert.That(down.Count, Is.LessThanOrEqualTo(1));
            }
        }

        [Test]
        public void TheIdleBotNeverMovesAndOnlyConfirmsCards()
        {
            Play(BotKind.Idle, out var sim);
            Assert.IsTrue(sim.Keys.ToArray().All(k => k.Key == Key.Enter));
            Assert.AreEqual((sim.Arena.HeroStart.X, sim.Arena.HeroStart.Y), (sim.State.Hero.TileX, sim.State.Hero.TileY));
        }
    }
}
