using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The grid hero (unity-game.md, "Hero Movement on the Grid", "Key Presses and Late Steps" and
    /// "Speeds Follow the Tempo"): stride steps, presses and late steps, held keys, stomps, bumps,
    /// freezes, the tempo scaling, and presses replaying from their (heard, effective) pairs.
    /// </summary>
    public class HeroTests
    {
        const double Tile = 1.5;

        /// <summary>A sim on 120 BPM beats known well ahead: stride steps every 0.25 s, on 10.00, 10.25, ...</summary>
        static Simulation At120(ArenaData arena = null)
        {
            var sim = new Simulation(new SimConfig { Arena = arena });
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 30, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<HeroStepEvent> Moves(Simulation sim)
        {
            var list = new List<HeroStepEvent>();
            foreach (var e in Drive.Events(sim, SimEventKind.HeroStep)) list.Add(e.AsHeroStep);
            return list;
        }

        static List<StrideStepEvent> Steps(Simulation sim)
        {
            var list = new List<StrideStepEvent>();
            foreach (var e in Drive.Events(sim, SimEventKind.StrideStep)) list.Add(e.AsStrideStep);
            return list;
        }

        static StrideStepEvent StepAt(Simulation sim, double t) => Steps(sim).Find(s => Math.Abs(s.T - t) < 1e-6);

        [Test]
        public void TheDocsWorkedLateStep()
        {
            // Press heard at 10.02, reaching the sim at F = 10.08 (10.075 in whole ticks): g = 0.10,
            // so it belongs to the step at 10.00, which the hero stood still on. It strides at once.
            var sim = At120();
            sim.StepTo(10.08);
            double x = sim.State.Hero.X, y = sim.State.Hero.Y;
            sim.Press(Key.Right, 10.02);
            sim.Release(Key.Right, 10.06);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count);
            var m = moves[0];
            Assert.IsTrue(m.Late);
            Assert.AreEqual(StepAt(sim, 10.0).Index, m.Step, "it belongs to the missed step");
            Assert.AreEqual(10.075, m.T, 1e-9);
            Assert.AreEqual(10.075, m.Start, 1e-9);
            Assert.AreEqual(10.185, m.End, 1e-9);
            Assert.AreEqual(x, m.FromX);
            Assert.AreEqual(x + Tile, m.ToX, 1e-9);
            Assert.AreEqual(y, m.ToY, 1e-9);
            Assert.AreEqual(Direction.Right, sim.State.Hero.Facing);
            Assert.AreEqual(x + Tile, sim.State.Hero.X, 1e-9);
        }

        [Test]
        public void APressHeardBeforeAStepWaitsForIt()
        {
            // Heard at 10.15: the step at 10.25 is still to come, so the press is queued for it.
            var sim = At120();
            sim.StepTo(10.2);
            sim.Press(Key.Up, 10.15);
            sim.Release(Key.Up, 10.18);
            Assert.AreEqual(0, Moves(sim).Count);
            sim.StepTo(11);
            var m = Moves(sim)[0];
            Assert.AreEqual(1, Moves(sim).Count);
            Assert.IsFalse(m.Late);
            Assert.AreEqual(10.25, m.T, 1e-9);
            Assert.AreEqual(10.25 + 0.110, m.End, 1e-9);
            Assert.AreEqual(m.FromY + Tile, m.ToY, 1e-9);
        }

        [Test]
        public void APressOnAStepTheHeroAlreadyTookGoesToTheNextOne()
        {
            var sim = At120();
            sim.StepTo(9.95);
            sim.Press(Key.Left, 9.9); // queued for 10.00
            sim.Release(Key.Left, 9.92);
            sim.StepTo(10.08);
            sim.Press(Key.Up, 10.02); // its step, 10.00, has a stride: the first step after F, 10.25
            sim.Release(Key.Up, 10.05);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count);
            Assert.AreEqual(10.0, moves[0].T, 1e-9);
            Assert.AreEqual(Direction.Left, moves[0].Facing);
            Assert.AreEqual(10.25, moves[1].T, 1e-9);
            Assert.AreEqual(Direction.Up, moves[1].Facing);
            Assert.IsFalse(moves[0].Late || moves[1].Late);
        }

        [Test]
        public void ALateStrideIsCompressedToEndBeforeTheNextStep()
        {
            // Heard 0.06 s after the step at 10.00 and taken at 10.20: 0.05 s remain before 10.25.
            var sim = At120();
            sim.StepTo(10.2);
            sim.Press(Key.Down, 10.06);
            var m = Moves(sim)[0];
            Assert.IsTrue(m.Late);
            Assert.AreEqual(10.2, m.Start, 1e-9);
            Assert.AreEqual(10.25, m.End, 1e-9);
        }

        [Test]
        public void HeldKeysStrideOnEveryStepAndTheLastPressedWins()
        {
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.86); // queued for 10.00, then held
            sim.StepTo(10.6);
            sim.Press(Key.Right, 10.56); // queued for 10.75, then held over Up
            sim.StepTo(11.1);
            sim.Release(Key.Right, 11.06);
            sim.StepTo(11.6);
            var facings = Moves(sim).ConvertAll(m => m.Facing);
            CollectionAssert.AreEqual(new[]
            {
                Direction.Up, Direction.Up, Direction.Up, // 10.00, 10.25, 10.50
                Direction.Right, Direction.Right,         // 10.75, 11.00
                Direction.Up, Direction.Up,               // 11.25, 11.50
            }, facings);
            var times = Moves(sim).ConvertAll(m => m.T);
            for (int i = 0; i < times.Count; i++) Assert.AreEqual(10.0 + 0.25 * i, times[i], 1e-9);
            Assert.IsTrue(Moves(sim).TrueForAll(m => !m.Late && !m.Bump));
            Assert.AreEqual(sim.Arena.HeroStart.X + 2, sim.State.Hero.TileX);
            Assert.AreEqual(sim.Arena.HeroStart.Y + 5, sim.State.Hero.TileY);
        }

        [Test]
        public void SpaceStompsInPlaceOnItsStep()
        {
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Space, 9.86);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count, "Space is a press, never held");
            Assert.IsTrue(moves[0].Stomp);
            Assert.AreEqual(10.0, moves[0].T, 1e-9);
            Assert.AreEqual(moves[0].FromX, moves[0].ToX);
            Assert.AreEqual(moves[0].FromY, moves[0].ToY);
            Assert.AreEqual(sim.Arena.HeroStart.X, sim.State.Hero.TileX);
        }

        [Test]
        public void BlockedTilesAndTheEdgeBumpInPlaceAndSpendTheStep()
        {
            // A 5 x 3 arena: the hero starts on its east edge, with a ruin two tiles west.
            var arena = ArenaData.Parse(@"{ ""tileM"": 1.5, ""origin"": [0, 0], ""size"": [5, 3], ""heroStart"": [4, 1],
                ""blocked"": [[2, 1]], ""structures"": [] }");
            var sim = At120(arena);
            sim.StepTo(9.9);
            sim.Press(Key.Right, 9.86); // the edge
            sim.Release(Key.Right, 9.88);
            sim.StepTo(10.4);
            sim.Press(Key.Left, 10.36); // to (3, 1) on 10.50, then held into the ruin on 10.75
            sim.StepTo(10.9);
            sim.Release(Key.Left, 10.86);
            sim.StepTo(12);
            var moves = Moves(sim);
            Assert.AreEqual(3, moves.Count);
            Assert.IsTrue(moves[0].Bump);
            Assert.AreEqual(Direction.Right, moves[0].Facing);
            Assert.AreEqual(moves[0].FromX, moves[0].ToX);
            Assert.AreEqual(10.5, moves[1].T, 1e-9, "the bump spent 10.00, and nothing moved on 10.25");
            Assert.IsFalse(moves[1].Bump);
            Assert.AreEqual(4.5 + 0.75, moves[1].ToX, 1e-9);
            Assert.AreEqual(10.75, moves[2].T, 1e-9);
            Assert.IsTrue(moves[2].Bump);
            Assert.AreEqual(Direction.Left, moves[2].Facing);
            Assert.AreEqual(3, sim.State.Hero.TileX);
            Assert.AreEqual(1, sim.State.Hero.TileY);
        }

        [Test]
        public void StructureSitesBlockTheHero()
        {
            var arena = ArenaData.Parse(@"{ ""tileM"": 1.5, ""origin"": [0, 0], ""size"": [5, 5], ""heroStart"": [2, 2],
                ""blocked"": [], ""structures"": [{ ""tiles"": [[2, 3], [3, 3]], ""large"": true }] }");
            var sim = At120(arena);
            Assert.IsFalse(sim.Arena.IsOpen(3, 3));
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.86);
            sim.Release(Key.Up, 9.88);
            sim.StepTo(11);
            Assert.IsTrue(Moves(sim)[0].Bump);
            Assert.AreEqual(2, sim.State.Hero.TileY);
        }

        [Test]
        public void AFrozenWorldDoesNotMoveTheHero()
        {
            var sim = At120();
            sim.State.Frozen = true;
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.86);
            sim.StepTo(10.2);
            sim.Press(Key.Left, 10.06); // a step it stood still on, but frozen: no late stride
            sim.Release(Key.Left, 10.1);
            sim.StepTo(11);
            Assert.AreEqual(0, Moves(sim).Count);
            Assert.AreEqual(sim.Arena.HeroStart.Y, sim.State.Hero.TileY);
            sim.State.Frozen = false;
            sim.StepTo(11.6);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count, "Up is still held: 11.25 and 11.50");
            Assert.AreEqual(11.25, moves[0].T, 1e-9);
            Assert.IsTrue(moves.TrueForAll(m => m.Facing == Direction.Up));
        }

        [Test]
        public void APressMadeWhileFrozenDoesNotStrideAfterTheThaw()
        {
            // A tap (or Space, a card's confirm) during a freeze that ends before the next step.
            var sim = At120();
            sim.State.Frozen = true;
            sim.StepTo(10.1);
            sim.Press(Key.Left, 10.06);
            sim.Release(Key.Left, 10.08);
            sim.Press(Key.Space, 10.08);
            sim.State.Frozen = false;
            sim.StepTo(11);
            Assert.AreEqual(0, Moves(sim).Count);
        }

        [Test]
        public void TwoPressesOnAMissedStepStrideLateAndThenOnTheNextStep()
        {
            var sim = At120();
            sim.StepTo(10.08);
            sim.Press(Key.Right, 10.02); // 10.00 had no stride: late
            sim.Release(Key.Right, 10.03);
            sim.Press(Key.Up, 10.04); // 10.00 now has one: the first step after F
            sim.Release(Key.Up, 10.05);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count);
            Assert.IsTrue(moves[0].Late);
            Assert.AreEqual(Direction.Right, moves[0].Facing);
            Assert.IsFalse(moves[1].Late);
            Assert.AreEqual(10.25, moves[1].T, 1e-9);
            Assert.AreEqual(Direction.Up, moves[1].Facing);
            Assert.AreEqual(moves[0].ToX, moves[1].FromX, 1e-9, "the second starts where the first ended");
        }

        [Test]
        public void WithALongLeadTheFirstStepAfterThePressCanAlreadyBeMissed()
        {
            // Headphones: heard at 10.15, reaching the sim at 10.30. No step in [10.05, 10.15], so the
            // press belongs to the first step after it, 10.25, which the sim has already passed.
            var sim = At120();
            sim.StepTo(10.3);
            sim.Press(Key.Down, 10.15);
            sim.Release(Key.Down, 10.2);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count);
            Assert.IsTrue(moves[0].Late);
            Assert.AreEqual(StepAt(sim, 10.25).Index, moves[0].Step);
            Assert.AreEqual(10.3, moves[0].Start, 1e-9);
            Assert.AreEqual(10.41, moves[0].End, 1e-9);
        }

        [TestCase(100.0, 1, 5.0, 0.84)]
        [TestCase(140.0, 1, 7.0, 0.84)]
        [TestCase(155.0, 0, 3.875, 0.42)]
        public void EnemySpeedsFollowTheTempo(double bpm, int halvings, double heroMps, double skeletonStepM)
        {
            var content = GameContent.Load(Path.GetDirectoryName(Checkout.File("game", "Content", "enemies.json")));
            double r = Tempo.Ratio(content.Role("skeleton").RefSpeedMps, content.Progression.Hero.SpeedMps);
            Assert.AreEqual(0.28, r, 0.001);

            var sim = Drive.Replay(Drive.Known(new FixtureSong().Beats(0, 20, bpm).Entries()), 20);
            var step = Steps(sim).Find(s => s.T > 10);
            double beat = 60.0 / bpm;
            Assert.AreEqual(halvings, step.Halvings);
            Assert.AreEqual(beat / (1 << halvings), step.Period, 1e-9);

            double hero = Tempo.HeroSpeed(Tile, step.Period);
            double distance = Tempo.EnemyStepDistance(r, Tile, Tempo.StridesPerStep(step.Halvings));
            Assert.AreEqual(heroMps, hero, 1e-9);
            Assert.AreEqual(skeletonStepM, distance, 0.001);
            // A skeleton steps every beat: it moves at r times the hero's speed whatever the tempo.
            Assert.AreEqual(r, distance / beat / hero, 1e-9);
            // A gait of "x." steps every other beat, twice as far.
            Assert.AreEqual(2 * distance, Tempo.EnemyStepDistance(r, Tile, Tempo.StridesPerStep(step.Halvings, 2)), 1e-12);
        }

        [Test]
        public void WithNoBeatEnemiesHoldWhatever()
        {
            Assert.AreEqual(0, Tempo.StridesPerStep(-1));
        }

        /// <summary>
        /// Feeds a stream as the client does and presses keys on a script: taps and holds of every
        /// key, heard 20 to 140 ms before the frame takes them, so some land as late steps.
        /// </summary>
        static Simulation Scripted(List<StreamEntry> entries, double until, ulong seed)
        {
            var rng = new SimRandom(seed);
            var sim = new Simulation(new SimConfig());
            var releaseAt = new Dictionary<Key, double>();
            Drive.Feed(sim, entries, until, eachFrame: s =>
            {
                foreach (var key in new List<Key>(releaseAt.Keys))
                    if (releaseAt[key] <= s.Now)
                    {
                        s.Release(key, s.Now - 0.05);
                        releaseAt.Remove(key);
                    }
                if (rng.NextDouble() >= 0.06) return;
                var k = (Key)rng.Below(5);
                if (releaseAt.ContainsKey(k)) return;
                s.Press(k, s.Now - 0.02 - 0.12 * rng.NextDouble());
                // Mostly taps; one in four held for a second or two.
                releaseAt[k] = s.Now + (rng.Below(4) == 0 ? 1 + rng.NextDouble() : 0.05);
            });
            return sim;
        }

        [Test]
        public void PressesReplayFromTheirHeardAndEffectiveTimes()
        {
            var entries = new FixtureSong(3).Beats(0, 30, 128, jitter: 0.004).Gap(30, 36).Beats(36, 60, 96, 150).Entries();
            var live = Scripted(entries, 60, 5);
            var moves = Moves(live);
            Assert.Greater(moves.FindAll(m => m.Late).Count, 5);
            Assert.Greater(moves.FindAll(m => !m.Late).Count, 50);
            var replay = Drive.Replay(Drive.Known(entries), 60, keys: live.Keys.ToArray());
            Assert.AreEqual(live.Hash, replay.Hash);
            CollectionAssert.AreEqual(live.EventsSince(0).ToArray(), replay.EventsSince(0).ToArray());
            var other = Drive.Replay(Drive.Known(entries), 60, keys: Scripted(entries, 60, 6).Keys.ToArray());
            Assert.AreNotEqual(live.Hash, other.Hash, "the hash sees the hero");
        }

        [Test]
        public void OnARealDumpTheHeroStepsOnlyOnTheGridExceptLateStrides()
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var sim = Scripted(entries, duration, 9);
            var steps = Steps(sim);
            var effective = new HashSet<double>();
            foreach (var k in sim.Keys) if (k.Down) effective.Add(k.Effective);
            int late = 0, onGrid = 0, overruns = 0;
            foreach (var m in Moves(sim))
            {
                Assert.AreEqual(m.T, m.Start);
                var step = steps.Find(s => s.Index == m.Step);
                var next = steps.Find(s => s.T > m.Start + 1e-9);
                if (m.Late)
                {
                    late++;
                    Assert.IsTrue(effective.Contains(m.T), $"a late stride at {m.T} not at a press");
                    Assert.Greater(m.T, step.T);
                }
                else
                {
                    onGrid++;
                    Assert.AreEqual(step.T, m.T, $"a stride at {m.T} off its step");
                }
                Assert.Greater(m.End, m.Start);
                // A late stride ends before the next step as planned when it starts; a beat revealed
                // after that may still bring the step a little earlier.
                if (next.T > 0 && !m.Late) Assert.LessOrEqual(m.End, next.T + 1e-9, $"the move at {m.T} runs into the next step");
                if (next.T > 0 && m.Late && m.End > next.T + 1e-9) overruns++;
            }
            Assert.IsTrue(sim.Arena.IsOpen(sim.State.Hero.TileX, sim.State.Hero.TileY));
            Assert.Greater(late, 3);
            Assert.Greater(onGrid, 20);
            Assert.LessOrEqual(overruns, late / 20, "late strides rarely run into a step the grid moved");
        }

        [Test]
        public void TheArenaRefusesBadData()
        {
            string Json(string start = "[1, 1]", string blocked = "[]", string extra = "") =>
                @"{ ""tileM"": 1.5, ""origin"": [0, 0], ""size"": [4, 4], ""heroStart"": " + start
                + @", ""blocked"": " + blocked + @", ""structures"": []" + extra + " }";
            Assert.DoesNotThrow(() => ArenaData.Parse(Json()));
            StringAssert.Contains("heroStart: the tile is blocked", Assert.Throws<ContentException>(() => ArenaData.Parse(Json(blocked: "[[1, 1]]"))).Message);
            StringAssert.Contains("outside the arena", Assert.Throws<ContentException>(() => ArenaData.Parse(Json(blocked: "[[4, 0]]"))).Message);
            StringAssert.Contains("blocked twice", Assert.Throws<ContentException>(() => ArenaData.Parse(Json(blocked: "[[0, 0], [0, 0]]"))).Message);
            StringAssert.Contains("expected a tile", Assert.Throws<ContentException>(() => ArenaData.Parse(Json(blocked: "[[0.5, 0]]"))).Message);
            StringAssert.Contains("unknown field", Assert.Throws<ContentException>(() => ArenaData.Parse(Json(extra: @", ""walls"": 1"))).Message);
        }

        [Test]
        public void TheDefaultArenaIs240MetresSquareWithTheHeroInTheMiddle()
        {
            var sim = new Simulation(new SimConfig());
            Assert.AreEqual(160 * Tile, sim.Arena.Width * sim.Arena.TileM, 1e-9);
            Assert.AreEqual(0.75, sim.State.Hero.X, 1e-9, "the centre tile's centre, half a tile east of the origin");
            Assert.AreEqual(0.75, sim.State.Hero.Y, 1e-9);
            Assert.IsFalse(sim.Arena.IsOpen(-1, 0));
            Assert.IsFalse(sim.Arena.IsOpen(0, 160));
        }
    }
}
