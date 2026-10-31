using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The grid hero (unity-game.md, "Hero Movement on the Grid", "Key Presses" and "Speeds Follow
    /// the Tempo"): stride steps, press strides, held keys, stomps, bumps,
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
        public void TheDocsWorkedPressStride()
        {
            // Press heard at 10.02, reaching the sim at F = 10.08 (10.075 in whole ticks): it belongs to
            // the step at 10.00, and strides at once, drawn from when it was heard.
            var sim = At120();
            sim.StepTo(10.08);
            double x = sim.State.Hero.X, y = sim.State.Hero.Y;
            sim.Press(Key.Right, 10.02);
            sim.Release(Key.Right, 10.06);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count);
            var m = moves[0];
            Assert.IsTrue(m.Pressed);
            Assert.AreEqual(StepAt(sim, 10.0).Index, m.Step, "it belongs to the step before it");
            Assert.AreEqual(10.075, m.T, 1e-9, "emitted when the sim took it");
            Assert.AreEqual(10.02, m.Start, 1e-9, "drawn from when it was heard");
            Assert.AreEqual(10.13, m.End, 1e-9);
            Assert.AreEqual(x, m.FromX);
            Assert.AreEqual(x + Tile, m.ToX, 1e-9);
            Assert.AreEqual(y, m.ToY, 1e-9);
            Assert.AreEqual(Direction.Right, sim.State.Hero.Facing);
            Assert.AreEqual(x + Tile, sim.State.Hero.X, 1e-9);
            Assert.AreEqual(10.02, sim.State.Hero.MoveStart, 1e-9);
            Assert.AreEqual(10.13, sim.State.Hero.MoveEnd, 1e-9);
        }

        [Test]
        public void APressJustAheadOfAStepTakesItAndAHeldKeyStridesOnTheStepAfter()
        {
            // Heard at 10.15: its stride would still be on at 10.25, so it takes that step.
            var sim = At120();
            sim.StepTo(10.2);
            sim.Press(Key.Up, 10.15);
            var m = Moves(sim)[0];
            Assert.IsTrue(m.Pressed);
            Assert.AreEqual(10.2, m.T, 1e-9);
            Assert.AreEqual(10.15, m.Start, 1e-9);
            Assert.AreEqual(10.15 + 0.110, m.End, 1e-9, "it may run past the step it took");
            Assert.AreEqual(m.FromY + Tile, m.ToY, 1e-9);
            sim.StepTo(10.6);
            sim.Release(Key.Up, 10.56);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(StepAt(sim, 10.25).Index, moves[0].Step);
            Assert.AreEqual(2, moves.Count, "nothing on 10.25, the press's");
            Assert.IsFalse(moves[1].Pressed);
            Assert.AreEqual(10.5, moves[1].Start, 1e-9);
            Assert.AreEqual(moves[0].ToY, moves[1].FromY, 1e-9);
        }

        [Test]
        public void APressBetweenStepsStridesAtOnceAndAHeldKeyStillStridesOnTheNextStep()
        {
            var sim = At120();
            sim.StepTo(10.15);
            sim.Press(Key.Left, 10.1);
            sim.StepTo(10.3);
            sim.Release(Key.Left, 10.27);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count);
            Assert.IsTrue(moves[0].Pressed);
            Assert.AreEqual(StepAt(sim, 10.0).Index, moves[0].Step);
            Assert.AreEqual(10.1, moves[0].Start, 1e-9);
            Assert.AreEqual(10.21, moves[0].End, 1e-9);
            Assert.IsFalse(moves[1].Pressed);
            Assert.AreEqual(10.25, moves[1].Start, 1e-9);
            Assert.AreEqual(moves[0].ToX, moves[1].FromX, 1e-9, "the second starts where the first ended");
            Assert.AreEqual(moves[0].ToX - Tile, moves[1].ToX, 1e-9);
        }

        [Test]
        public void APressCutsShortTheMoveInProgressFromWhereTheHeroIs()
        {
            // A press heard 10 ms into a held stride sets off from where the hero is then, to the tile
            // beside the one it was striding to.
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.81);
            sim.StepTo(10.1);
            var up = Moves(sim)[Moves(sim).Count - 1];
            Assert.AreEqual(10.0, up.Start, 1e-9, "Up strode on 10.00, held");
            sim.Press(Key.Right, 10.01);
            var m = Moves(sim)[Moves(sim).Count - 1];
            Assert.IsTrue(m.Pressed);
            Assert.AreEqual(Direction.Right, m.Facing);
            Assert.AreEqual(10.01, m.Start, 1e-9);
            Assert.AreEqual(10.12, m.End, 1e-9);
            Assert.AreEqual(up.FromY + Tile * 0.01 / 0.11, m.FromY, 1e-9);
            Assert.AreEqual(up.ToX, m.FromX, 1e-9);
            Assert.AreEqual(up.ToX + Tile, m.ToX, 1e-9);
            Assert.AreEqual(up.ToY, m.ToY, 1e-9);
        }

        [Test]
        public void HeldKeysStrideOnEveryStepAndTheLastPressedWins()
        {
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.86); // at once, then held
            sim.StepTo(10.6);
            sim.Press(Key.Right, 10.56); // at once, cutting short the stride on 10.50, then held over Up
            sim.StepTo(11.1);
            sim.Release(Key.Right, 11.06);
            sim.StepTo(11.6);
            var moves = Moves(sim);
            CollectionAssert.AreEqual(new[]
            {
                Direction.Up, Direction.Up, Direction.Up, Direction.Up, // 9.86, 10.00, 10.25, 10.50
                Direction.Right, Direction.Right, Direction.Right,      // 10.56, 10.75, 11.00
                Direction.Up, Direction.Up,                             // 11.25, 11.50
            }, moves.ConvertAll(m => m.Facing));
            var starts = new[] { 9.86, 10.0, 10.25, 10.5, 10.56, 10.75, 11.0, 11.25, 11.5 };
            for (int i = 0; i < starts.Length; i++) Assert.AreEqual(starts[i], moves[i].Start, 1e-9);
            CollectionAssert.AreEqual(new[] { true, false, false, false, true, false, false, false, false },
                moves.ConvertAll(m => m.Pressed));
            Assert.IsTrue(moves.TrueForAll(m => !m.Bump));
            Assert.AreEqual(sim.Arena.HeroStart.X + 3, sim.State.Hero.TileX);
            Assert.AreEqual(sim.Arena.HeroStart.Y + 6, sim.State.Hero.TileY);
        }

        [Test]
        public void SpaceStompsInPlaceAtOnce()
        {
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Space, 9.86);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count, "Space is a press, never held");
            Assert.IsTrue(moves[0].Stomp && moves[0].Pressed);
            Assert.AreEqual(9.86, moves[0].Start, 1e-9);
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
            sim.Press(Key.Left, 10.36); // to (3, 1) at once, then held into the ruin on 10.50
            sim.StepTo(10.6);
            sim.Release(Key.Left, 10.56);
            sim.StepTo(12);
            var moves = Moves(sim);
            Assert.AreEqual(3, moves.Count, "nothing moved on 10.00 or 10.25");
            Assert.IsTrue(moves[0].Bump);
            Assert.AreEqual(Direction.Right, moves[0].Facing);
            Assert.AreEqual(moves[0].FromX, moves[0].ToX);
            Assert.AreEqual(10.36, moves[1].Start, 1e-9);
            Assert.IsFalse(moves[1].Bump);
            Assert.AreEqual(4.5 + 0.75, moves[1].ToX, 1e-9);
            Assert.AreEqual(10.5, moves[2].Start, 1e-9);
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
            sim.Press(Key.Left, 10.06); // frozen: no press stride
            sim.Release(Key.Left, 10.1);
            sim.StepTo(11);
            Assert.AreEqual(0, Moves(sim).Count);
            Assert.AreEqual(sim.Arena.HeroStart.Y, sim.State.Hero.TileY);
            sim.State.Frozen = false;
            sim.StepTo(11.6);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count, "Up is still held: 11.25 and 11.50");
            Assert.AreEqual(11.25, moves[0].Start, 1e-9);
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
        public void ASecondPressForTheSameStepWaitsForTheNextStep()
        {
            var sim = At120();
            sim.StepTo(10.08);
            sim.Press(Key.Right, 10.02); // the step at 10.00's: at once
            sim.Release(Key.Right, 10.03);
            sim.Press(Key.Up, 10.04); // 10.00's again: the first step after F no press took
            sim.Release(Key.Up, 10.05);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(2, moves.Count);
            Assert.IsTrue(moves[0].Pressed);
            Assert.AreEqual(Direction.Right, moves[0].Facing);
            Assert.IsFalse(moves[1].Pressed);
            Assert.AreEqual(10.25, moves[1].Start, 1e-9);
            Assert.AreEqual(Direction.Up, moves[1].Facing);
            Assert.AreEqual(moves[0].ToX, moves[1].FromX, 1e-9, "the second starts where the first ended");
        }

        [Test]
        public void MashingStridesAtMostOncePerStepAndTheLatestPressWins()
        {
            var sim = At120();
            sim.StepTo(10.1);
            for (int i = 0; i < 8; i++)
            {
                var key = i % 2 == 0 ? Key.Left : Key.Down;
                sim.Press(key, sim.Now - 0.04);
                sim.StepTo(sim.Now + 1 / 60.0);
                sim.Release(key, sim.Now - 0.04);
                sim.StepTo(sim.Now + 1 / 60.0);
            }
            sim.StepTo(11);
            var moves = Moves(sim);
            // Presses heard 10.06 to 10.29, taken 10.10 to 10.33: one at once for 10.00, one at once for
            // 10.25 (heard from 10.14 on), and the last press for neither waits for 10.50.
            Assert.AreEqual(3, moves.Count);
            Assert.IsTrue(moves[0].Pressed && moves[1].Pressed && !moves[2].Pressed);
            Assert.AreEqual(Direction.Left, moves[0].Facing);
            Assert.AreEqual(Direction.Down, moves[2].Facing);
            Assert.AreEqual(10.5, moves[2].Start, 1e-9);
        }

        /// <summary>A 100 ms tap at each of 50 phases across the stride period, reaching the sim 90 ms late.</summary>
        [Test]
        public void ATapMovesOneTileWhereverItFalls()
        {
            for (int i = 0; i < 50; i++)
            {
                var sim = At120();
                double h = 10 + 0.25 * i / 50;
                sim.StepTo(h + 0.09);
                sim.Press(Key.Right, h);
                sim.StepTo(h + 0.19);
                sim.Release(Key.Right, h + 0.1);
                sim.StepTo(12);
                var moves = Moves(sim);
                Assert.AreEqual(1, moves.Count, $"a tap at {h}");
                Assert.AreEqual(h, moves[0].Start, 1e-9);
            }
        }

        [Test]
        public void ATurnHeardBeforeAStepTheSimHasPassedTurnsThere()
        {
            // Up held, then released and Right pressed, heard at 10.22 and taken at 10.31. The sim
            // passed the step at 10.25 with Up still down, but judges it in heard time: no Up on it.
            var sim = At120();
            sim.StepTo(9.9);
            sim.Press(Key.Up, 9.81);
            sim.StepTo(10.31);
            sim.Release(Key.Up, 10.22);
            sim.Press(Key.Right, 10.22);
            sim.StepTo(10.4);
            sim.Release(Key.Right, 10.31);
            sim.StepTo(12);
            var moves = Moves(sim);
            CollectionAssert.AreEqual(new[] { Direction.Up, Direction.Up, Direction.Right }, moves.ConvertAll(m => m.Facing));
            CollectionAssert.AreEqual(new[] { 9.81, 10.0, 10.22 }, moves.ConvertAll(m => Math.Round(m.Start, 9)));
            Assert.That(moves[1].T - 10.09, Is.InRange(0, Simulation.TickSeconds), "the held stride is taken the lead after its step");
        }

        [Test]
        public void WithALongLeadAPressIsStillDrawnFromWhenItWasHeard()
        {
            // Headphones: heard at 10.15, reaching the sim at 10.30. The stride is drawn from 10.15 and
            // has ended by the time the sim takes it; the step at 10.25, which it was on, is the press's.
            var sim = At120();
            sim.StepTo(10.3);
            sim.Press(Key.Down, 10.15);
            sim.Release(Key.Down, 10.2);
            sim.StepTo(11);
            var moves = Moves(sim);
            Assert.AreEqual(1, moves.Count);
            Assert.IsTrue(moves[0].Pressed);
            Assert.AreEqual(StepAt(sim, 10.25).Index, moves[0].Step);
            Assert.AreEqual(10.3, moves[0].T, 1e-9);
            Assert.AreEqual(10.15, moves[0].Start, 1e-9);
            Assert.AreEqual(10.26, moves[0].End, 1e-9);
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
        /// key, heard 20 to 140 ms before the frame takes them.
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
            Assert.Greater(moves.FindAll(m => m.Pressed).Count, 50);
            Assert.Greater(moves.FindAll(m => !m.Pressed).Count, 50);
            var replay = Drive.Replay(Drive.Known(entries), 60, keys: live.Keys.ToArray());
            Assert.AreEqual(live.Hash, replay.Hash);
            CollectionAssert.AreEqual(live.EventsSince(0).ToArray(), replay.EventsSince(0).ToArray());
            var other = Drive.Replay(Drive.Known(entries), 60, keys: Scripted(entries, 60, 6).Keys.ToArray());
            Assert.AreNotEqual(live.Hash, other.Hash, "the hash sees the hero");
        }

        [Test]
        public void OnARealDumpTheHeroStepsOnTheGridOrAtOnceOnPresses()
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var sim = Scripted(entries, duration, 9);
            var steps = Steps(sim);
            var heard = new Dictionary<double, double>(); // each press's effective time: the earliest heard then
            foreach (var k in sim.Keys)
                if (k.Down) heard[k.Effective] = heard.TryGetValue(k.Effective, out double h) ? Math.Min(h, k.Heard) : k.Heard;
            int pressed = 0, onGrid = 0, overlaps = 0;
            double lastEnd = double.NegativeInfinity;
            foreach (var m in Moves(sim))
            {
                if (m.Pressed)
                {
                    pressed++;
                    Assert.IsTrue(heard.ContainsKey(m.T), $"a press stride at {m.T} not at a press");
                    Assert.GreaterOrEqual(m.Start, heard[m.T] - 1e-9, "drawn from when the press was heard");
                }
                else
                {
                    onGrid++;
                    Assert.AreEqual(steps.Find(s => s.Index == m.Step).T, m.Start, $"a stride at {m.Start} off its step");
                    // Taken once the keys heard by its step are in: at most the script's longest lag after it.
                    Assert.That(m.T - m.Start, Is.InRange(0, 0.14 + Simulation.TickSeconds + 1e-9));
                    // A move ends before the next step as planned when it starts; a beat revealed after
                    // that may still bring the step a little earlier.
                    if (m.Start < lastEnd - 1e-9) overlaps++;
                }
                Assert.LessOrEqual(m.Start, m.T + 1e-9);
                Assert.Greater(m.End, m.Start);
                lastEnd = m.End;
            }
            Assert.IsTrue(sim.Arena.IsOpen(sim.State.Hero.TileX, sim.State.Hero.TileY));
            Assert.Greater(pressed, 20);
            Assert.Greater(onGrid, 20);
            Assert.LessOrEqual(overlaps, onGrid / 20, "strides rarely start before the one before has ended");
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
