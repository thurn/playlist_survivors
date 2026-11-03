using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class SurvivorBossTests
    {
        static GameContent Content => GameContent.Load(Checkout.In("Content"));
        static SimConfig Config(string boss = null, bool level = false)
        {
            var c = new RunInputs { Duel = boss, Level = level, Loadout = new Upgrade[0] }.Build(Content);
            c.Hero.Health = 1e9;
            if (c.Level != null) { c.Level.Ramp = new Curve((0, 6)); c.Level.Cap = 6; }
            return c;
        }
        static Simulation Make(SimConfig config, double bpm = 0)
        {
            var s = new Simulation(config);
            if (bpm > 0) foreach (var e in Drive.Known(new FixtureSong().Beats(0, 100, bpm).Entries())) s.AddKnown(e.Entry, e.KnownAt);
            return s;
        }
        static void Pick(Simulation s) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }
        static void Strike(Simulation s, double damage)
        {
            ref readonly var b = ref s.Enemies[s.Bosses.Id];
            s.Strikes.Land(s, new Blow { Shape = HitShape.Circle(b.X, b.Y, 0.1), T = s.Now, Damage = damage,
                Tags = HitTags.Strike, Source = BlowSource.SwordSlash, Owner = -1, Part = -1 });
        }
        static void Until(Simulation s, Func<bool> ready, double seconds = 60)
        {
            double until = s.Now + seconds;
            while (!ready() && s.Now < until) s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.IsTrue(ready(), "scripted condition within the simulated horizon");
        }

        [TestCase("bigOrk")] [TestCase("troll")]
        public void OrdinaryElitesApproachAndHurtByContactWithoutSpecialMoves(string kind)
        {
            var c = new RunInputs { Loadout = Array.Empty<Upgrade>() }.Build(Content);
            var s = Make(c);
            int id = s.Horde.Spawn(Content.Enemies.IndexOf(kind), 4, 0, 100);
            s.StepTo(8);
            Assert.IsTrue(s.Enemies[id].Alive, "elite body remains a durable threat");
            Assert.Less(s.Enemies[id].X, 2, "removing the move must not strand the elite");
            Assert.IsTrue(Drive.Events(s, SimEventKind.HeroHit).Any(e => e.D0 > 0 && e.I1 == (int)HitSource.Touch));
            Assert.IsEmpty(Drive.Events(s, SimEventKind.EliteTelegraph));
            Assert.IsEmpty(Drive.Events(s, SimEventKind.EliteMove));
            Assert.IsEmpty(Drive.Events(s, SimEventKind.Spore));
        }

        [Test]
        public void AllThreeThresholdsUseHordeTimeWithClearedFieldAndOrderedRewards()
        {
            var s = Make(Config(level: true));
            var calls = Content.Level.Bosses;
            CollectionAssert.AreEqual(new[] { 180.0, 360, 540 }, calls.Select(b => b.CallAtS));
            s.StepTo(179.9);
            s.Control(RunControl.Pause, s.Now);
            s.StepTo(s.Now + 7);
            s.Hold(HoldReason.Cards, true);
            s.Control(RunControl.Resume, s.Now);
            s.StepTo(s.Now + 3);
            Assert.AreEqual(0, s.Bosses.Called, "neither a pause nor cards can cross the threshold");
            Assert.AreEqual(179.9, s.State.HordeClock, 0.02);
            s.Hold(HoldReason.Cards, false);
            double bossSeconds = 0;
            for (int n = 0; n < 3; n++)
            {
                double toThreshold = calls[n].CallAtS - s.State.HordeClock;
                s.StepTo(s.Now + toThreshold - 0.02);
                Assert.AreEqual(n, s.Bosses.Called);
                Until(s, () => s.Bosses.Called == n + 1, 0.1);
                Assert.IsTrue(s.Horde.Spawner.Stopped);
                double hordeTime = s.State.HordeClock, actual = s.State.RunClock;
                int spawned = s.Horde.Spawned, alive = s.Enemies.Alive;
                int structures = s.StructureField.Standing;
                s.Pickups.Drop(s, PickupKind.Experience, 90, 90, 7);
                int drops = s.Pickups.StoredCount;
                Until(s, () => s.Bosses.InDuel, 5);
                Assert.AreEqual(1, s.Enemies.Alive, "arrival isolates the boss from the entire old horde");
                Assert.AreEqual(drops, s.Pickups.StoredCount);
                Assert.AreEqual(structures, s.StructureField.Standing, "arrival never breaks structures");
                Assert.That(Drive.Events(s, SimEventKind.BossArrive).Last().I2, Is.InRange(0, alive));
                s.StepTo(s.Now + 6);
                Assert.AreEqual(spawned, s.Horde.Spawned);
                Assert.AreEqual(hordeTime, s.State.HordeClock);
                Assert.Greater(s.State.RunClock, actual + 7);
                bossSeconds += s.State.RunClock - actual;
                s.State.Hero.Health = 12;
                Strike(s, 1e9);
                Assert.AreEqual(s.State.Hero.MaxHealth, s.State.Hero.Health, "full heal precedes the reward");
                s.StepTo(s.Now + Simulation.TickSeconds);
                if (n < 2)
                {
                    Assert.AreEqual(OfferKind.Treasure, s.State.Offer.Kind);
                    for (int i = 0; i < s.State.Offer.Count; i++) Assert.IsFalse(s.State.Offer[i].New);
                    Assert.IsFalse(s.Horde.Spawner.Stopped);
                    double heldClock = s.State.RunClock; s.StepTo(s.Now + 3);
                    Assert.AreEqual(heldClock, s.State.RunClock); Assert.AreEqual(hordeTime, s.State.HordeClock);
                    Pick(s);
                    int victim = s.Horde.Mobs.ToArray().ToList().FindIndex(m => m.Owned);
                    if (victim >= 0) s.Horde.Despawn(victim, s.Now);
                    s.StepTo(s.Now + 0.5);
                    Assert.Greater(s.Horde.Spawned, spawned, "ordinary spawning resumes");
                }
            }
            Assert.AreEqual(RunEnd.Victory, s.State.End);
            Assert.AreEqual(3, s.Bosses.Called);
            Assert.AreEqual(540, s.State.HordeClock, 0.02);
            Assert.GreaterOrEqual(s.State.RunClock, 540 + bossSeconds - 0.05);
            Assert.IsEmpty(Drive.Events(s, SimEventKind.BossBreak));
            Assert.IsEmpty(Drive.Events(s, SimEventKind.BossStagger));
        }

        [Test]
        public void FightTimerStartsAtArrivalFreezesForHoldsAndClearsAfterDefeat()
        {
            var s = Make(Config("fatOgre"));
            Until(s, () => s.Bosses.InDuel);
            Assert.That(s.Snapshot().BossElapsed, Is.InRange(0, Simulation.TickSeconds * 2));
            s.StepTo(s.Now + 2);
            double fight = s.Snapshot().BossElapsed, horde = s.Snapshot().HordeClock;
            Assert.That(fight, Is.InRange(2 - 1e-9, 2 + Simulation.TickSeconds * 2));
            s.Control(RunControl.Pause, s.Now); s.StepTo(s.Now + 5);
            Assert.That(s.Snapshot().BossElapsed, Is.EqualTo(fight));
            Assert.That(s.Snapshot().HordeClock, Is.EqualTo(horde));
            s.Control(RunControl.Resume, s.Now); s.StepTo(s.Now + 1);
            Assert.That(s.Snapshot().BossElapsed, Is.EqualTo(fight + 1).Within(.02));
            double afterPause = s.Snapshot().BossElapsed;
            s.Hold(HoldReason.Cards, true); s.StepTo(s.Now + 4);
            Assert.That(s.Snapshot().BossElapsed, Is.EqualTo(afterPause), "cards freeze the fight clock too");
            s.Hold(HoldReason.Cards, false);
            Strike(s, 1e9); s.StepTo(s.Now + .1);
            Assert.That(s.Snapshot().BossElapsed, Is.Zero);
        }

        [TestCase("fatOgre", 0)] [TestCase("conductor", 0)] [TestCase("dragon", 0)]
        [TestCase("fatOgre", 0.99)] [TestCase("conductor", 0.99)] [TestCase("dragon", 0.99)]
        public void AlwaysDamageableAndPausePreservesTheLiveWarning(string boss, double fraction)
        {
            var s = Make(Config(boss), 174);
            Until(s, () => s.Bosses.Marks.Length > 0);
            double before = s.Bosses.Health;
            Strike(s, 10); Assert.AreEqual(before - 10, s.Bosses.Health, 1e-7);
            var mark = s.Bosses.Marks[0];
            s.StepTo(s.Now + (s.Telegraphs.LandTime(mark.Telegraph) - s.Now) * fraction);
            s.Control(RunControl.Pause, s.Now);
            double remaining = s.Telegraphs.LandTime(s.Bosses.Marks[0].Telegraph) - s.Now;
            double clock = s.State.RunClock; s.StepTo(s.Now + 5);
            Assert.AreEqual(clock, s.State.RunClock);
            Assert.AreEqual(mark.Id, s.Bosses.Marks[0].Id);
            s.Control(RunControl.Resume, s.Now);
            Assert.GreaterOrEqual(s.Telegraphs.LandTime(s.Bosses.Marks[0].Telegraph) - s.Now + 1e-8, remaining);
            s.StepTo(s.Now + Math.Max(0, remaining - 0.02));
            Assert.IsTrue(s.Bosses.Marks.ToArray().Any(m => m.Id == mark.Id));
        }

        [TestCase("fatOgre", 0)] [TestCase("fatOgre", 60)] [TestCase("fatOgre", 174)] [TestCase("fatOgre", 240)]
        [TestCase("conductor", 60)] [TestCase("conductor", 174)] [TestCase("conductor", 240)]
        [TestCase("dragon", 0)] [TestCase("dragon", 174)] [TestCase("dragon", 240)]
        public void WavesCatchWalkingButBaselineDashCanCrossAndRecover(string boss, double bpm)
        {
            var c = Config(boss);
            var type = c.Bosses.Of(c.Duel);
            type.Moves = new[] { type.Moves.Single(m => Bosses.IsWave(m.Move)) };
            var s = Make(c, bpm);
            Until(s, () => s.Bosses.Marks.Length > 0);
            var m = s.Bosses.Marks[0];
            double land = s.Telegraphs.LandTime(m.Telegraph);
            Assert.GreaterOrEqual(land - m.Telegraph.Start, 1.1 - 1e-8);
            double original = land;
            // The front catches a walking player moving away at baseline speed.
            s.Press(Key.Up, s.Now);
            Until(s, () => s.Now >= land && s.Bosses.WaveShape(m, s.Now).Touches(s.State.Hero.X, s.State.Hero.Y, c.Hero.RadiusM + 1), 15);
            double crossing = s.Now;
            s.Release(Key.Up, crossing); s.Press(Key.Down, crossing);
            s.Press(Key.Space, crossing); s.Release(Key.Space, crossing);
            s.StepTo(crossing + 0.3);
            Assert.IsTrue(Drive.Events(s, SimEventKind.HeroHit).Any(e => e.I0 == (int)HitOutcome.Dodged || e.I0 == (int)HitOutcome.PerfectDodge), "physical i-frames cross the wave");
            Assert.IsFalse(Drive.Events(s, SimEventKind.HeroHit).Any(e => e.D0 > 0), "no hidden timing grade required");
            s.Release(Key.Down, s.Now);
            Until(s, () => s.Bosses.Marks.Length > 0 && s.Bosses.Marks[0].Id != m.Id, 15);
            double next = s.Telegraphs.LandTime(s.Bosses.Marks[0].Telegraph);
            Assert.AreEqual(1, Drive.Events(s, SimEventKind.BossMove).Count(e => e.I0 == m.Id && e.I2 == 0), "one launch cue per traveling front, including exact tick boundaries");
            Assert.Greater(next - original, c.Dashes.CooldownS);
            Assert.LessOrEqual(s.State.Hero.DashReadyAt, next);
        }

        [TestCase("fatOgre", 0)] [TestCase("conductor", 0)] [TestCase("dragon", 0)]
        [TestCase("fatOgre", 174)] [TestCase("conductor", 174)] [TestCase("dragon", 174)]
        public void HoldingAnActiveFrontDoesNotJumpItBackwardOntoANewBeat(string boss, double bpm)
        {
            var c = Config(boss);
            c.Bosses.Of(c.Duel).Moves = new[] { c.Bosses.Of(c.Duel).Moves.Single(m => Bosses.IsWave(m.Move)) };
            var s = Make(c, bpm); Until(s, () => s.Bosses.Marks.Length > 0);
            var mark = s.Bosses.Marks[0];
            s.StepTo(s.Telegraphs.LandTime(mark.Telegraph) + 0.4);
            var before = s.Bosses.WaveShape(s.Bosses.Marks[0], s.Now);
            s.Control(RunControl.Pause, s.Now); s.StepTo(s.Now + 5.13); s.Control(RunControl.Resume, s.Now);
            var after = s.Bosses.WaveShape(s.Bosses.Marks[0], s.Now);
            Assert.AreEqual(before.Radius, after.Radius, 1e-7);
            Assert.AreEqual(before.Y, after.Y, 1e-7);
            Assert.AreEqual(before.Y2, after.Y2, 1e-7);
        }

        [Test]
        public void ConductorAlternatesItsFanGapsBetweenVolleys()
        {
            var c = Config("conductor");
            c.Bosses.Of(c.Duel).Moves = new[] { c.Bosses.Of(c.Duel).Moves.Single(m => m.Move == BossMove.Fans) };
            var s = Make(c); Until(s, () => s.Bosses.Marks.Length > 0);
            var first = s.Bosses.Marks[1];
            Until(s, () => s.Bosses.Marks.Length > 0 && s.Bosses.Marks[0].Id > first.Id);
            var next = s.Bosses.Marks[1];
            Assert.Greater(Math.Abs(first.Shape.DirX - next.Shape.DirX), 0.3, "the opposite baton angle moves the safe gaps");
        }

        [TestCase(0)] [TestCase(60)] [TestCase(174)] [TestCase(240)]
        public void MaximumStrideCannotWalkBehindTheWallBeforeItsLaunch(double bpm)
        {
            var c = Config("conductor");
            c.Bosses.Of(c.Duel).Moves = new[] { c.Bosses.Of(c.Duel).Moves.Single(m => m.Move == BossMove.Wall) };
            var s = Make(c, bpm); s.State.Build.Set(SupportKind.Stride, 5);
            Until(s, () => s.Bosses.Marks.Length > 0);
            double land = s.Telegraphs.LandTime(s.Bosses.Marks[0].Telegraph);
            s.Press(Key.Down, s.Now); s.StepTo(land + 0.3);
            Assert.IsTrue(Drive.Events(s, SimEventKind.HeroHit).Any(e => e.D0 > 0), "even maximum walking speed must cross using i-frames");
        }

        [TestCase("fatOgre", 1)] [TestCase("fatOgre", 2)]
        [TestCase("conductor", 1)] [TestCase("conductor", 2)]
        [TestCase("dragon", 1)] [TestCase("dragon", 2)]
        public void PositioningPatternsHaveWalkingEscapesAtSupportedTempos(string boss, int phase)
        {
            foreach (double bpm in new[] { 0.0, 60, 174, 240 })
            foreach (var move in Content.Bosses.Of(Content.Enemies.IndexOf(boss)).Moves.Where(m => !Bosses.IsWave(m.Move)))
            {
                var c = Config(boss);
                c.Bosses.Of(c.Duel).Moves = new[] { move.Clone() };
                var s = Make(c, bpm);
                Until(s, () => s.Bosses.InDuel);
                if (phase == 2) Strike(s, s.Bosses.MaxHealth * 0.6);
                Until(s, () => s.Bosses.Marks.Length > 0);
                var marks = s.Bosses.Marks.ToArray();
                double land = marks.Min(m => s.Telegraphs.LandTime(m.Telegraph));
                double distance = c.Hero.SpeedMps * (land - s.Now - 0.1);
                bool escaped = false;
                for (int i = 0; i < 64; i++)
                {
                    double angle = i * Math.PI / 32;
                    double x = s.State.Hero.X + Math.Cos(angle) * distance;
                    double y = s.State.Hero.Y + Math.Sin(angle) * distance;
                    if (marks.Any(m => m.Shape.Touches(x, y, c.Hero.RadiusM))) continue;
                    s.Target(x, y, s.Now); escaped = true; break;
                }
                Assert.IsTrue(escaped, $"{boss}/{move.Move}/phase {phase}/{bpm} BPM has a walking escape");
                s.StepTo(land + 0.15);
                Assert.IsFalse(Drive.Events(s, SimEventKind.HeroHit).Any(e => e.D0 > 0), $"walk {boss}/{move.Move}/{phase}/{bpm}");
            }
        }

        [Test]
        public void ArrivalAndWarningDeadlinesSurviveConfidenceLossWithoutWaitingForStrongMusic()
        {
            var c = Config("fatOgre");
            c.Bosses.Of(c.Duel).Moves[0].TelegraphBeats = new[] { 10, 10 };
            var s = new Simulation(c);
            var song = new FixtureSong().Beats(0, 5, 120).Gap(5, 15).Beats(15, 25, 174);
            foreach (var e in Drive.Known(song.Entries())) s.AddKnown(e.Entry, e.KnownAt);
            Until(s, () => s.Bosses.InDuel, 6);
            Assert.That(Drive.Events(s, SimEventKind.BossArrive).Single().T, Is.InRange(2, 4.02));
            Until(s, () => s.Bosses.Marks.Length > 0);
            var m = s.Bosses.Marks[0];
            double promised = s.Telegraphs.LandTime(m.Telegraph);
            s.StepTo(8);
            Assert.AreNotEqual(Reliability.Reliable, s.State.Beat);
            Assert.AreEqual(promised, s.Telegraphs.LandTime(s.Bosses.Marks[0].Telegraph));
            s.StepTo(promised - 0.02);
            Assert.IsTrue(s.Bosses.Marks.ToArray().Any(x => x.Id == m.Id));
        }
    }
}
