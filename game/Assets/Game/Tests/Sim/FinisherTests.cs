using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The finishers (`--finishers`, docs/design/game-modes.md, "Finishers"): rings closing on the zen hero toward the
    /// phrase downbeats, predicted causally from the beats known; Space in a ring's window fires the sword's
    /// screen-clearing slam at once, in heard time; a press outside it, or none, does nothing and costs nothing; and no
    /// rings while held or under the gate.
    /// </summary>
    public class FinisherTests
    {
        static GameContent Content => HordeFixture.Content;

        /// <summary>Beats and ticks only, at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until, double evidence = 0.9, double jitter = 0) =>
            Drive.Known(new FixtureSong(3).Beats(0, until, bpm, evidence: evidence, jitter: jitter).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        /// <summary>A zen hero that never falls, with the sword at rank 1 and no horde of its own, and the finishers.</summary>
        static SimConfig Config(ArenaData arena = null, bool finishers = true)
        {
            var config = HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = new[] { ("sword", 1) }, Cards = false };
            config.Mode = GameMode.Zen;
            config.Finishers = finishers;
            return config;
        }

        static Simulation Start(IEnumerable<KnownEntry> known, SimConfig config = null)
        {
            var sim = new Simulation(config ?? Config());
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<FinisherEvent> Rings(Simulation sim, FinisherPhase? phase = null) =>
            Drive.Events(sim, SimEventKind.Finisher).Select(e => e.AsFinisher).Where(f => phase == null || f.Phase == phase).ToList();

        static List<AttackEvent> Slams(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack)
                .Where(a => a.IsSword && a.SwordKind == HeroAttackKind.Slam && a.Reach == Sword.FinisherReachM).ToList();

        static List<double> Beats(IEnumerable<KnownEntry> known) =>
            known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();

        /// <summary>Steps until the next ring has started; returns it.</summary>
        static FinisherEvent NextRing(Simulation sim, double until = 60)
        {
            int before = Rings(sim, FinisherPhase.Ring).Count;
            while (sim.Now < until)
            {
                sim.StepTo(sim.Now + Simulation.TickSeconds);
                var rings = Rings(sim, FinisherPhase.Ring);
                if (rings.Count > before) return rings[before];
            }
            Assert.Fail("no ring started");
            return default;
        }

        static int Place(Simulation sim, string role, double dx, double dy) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        // ---------------------------------------------------------------- the flag

        [Test]
        public void OnlyZenTakesTheFinishersAndWithoutThemNothingRings()
        {
            Assert.IsTrue(GameModes.Of(GameMode.Zen).Finishers);
            Assert.IsFalse(GameModes.Of(GameMode.Grid).Finishers);
            Assert.IsFalse(GameModes.Of(GameMode.Free).Finishers);
            foreach (var mode in new[] { GameMode.Grid, GameMode.Free })
            {
                var config = Config();
                config.Mode = mode;
                Assert.Throws<ArgumentException>(() => new Simulation(config), $"{mode} refuses --finishers");
            }
            var plain = Start(Plain(128, 40), Config(finishers: false));
            plain.StepTo(40);
            Assert.AreEqual(0, Rings(plain).Count, "no rings without the flag");
        }

        // ---------------------------------------------------------------- the rings

        [Test]
        public void RingsCloseOnPhraseDownbeatsEightToFifteenSecondsApart([Values(70, 128, 174)] double bpm)
        {
            var known = Plain(bpm, 120, jitter: 0.004);
            var beats = Beats(known);
            var sim = Start(known);
            sim.StepTo(120);
            double period = 60 / bpm;
            int phrase = bpm > 137 ? Finishers.LongPhrase : Finishers.ShortPhrase;
            Assert.AreEqual(phrase, Finishers.PhraseBeats(period));
            double telegraph = Finishers.Telegraph(period);
            Assert.AreEqual(bpm < 96 ? 2 : 4, telegraph / period, 1e-9, "four beats, or two on a slow song");

            var started = Rings(sim, FinisherPhase.Ring);
            var ended = Rings(sim, FinisherPhase.Miss);
            Assert.GreaterOrEqual(started.Count, 120 / (phrase * period) - 2, "a ring every phrase");
            Assert.AreEqual(started.Count, ended.Count + (Rings(sim).Last().Phase == FinisherPhase.Miss ? 0 : 1), "nobody pressed: each one missed");
            Assert.AreEqual(0, Slams(sim).Count, "and nothing slammed");
            for (int k = 0; k < ended.Count; k++)
            {
                var ring = started[k];
                var end = ended[k];
                Assert.AreEqual(ring.Ring, end.Ring);
                Assert.AreEqual(beats[end.Beat], end.Downbeat, 1e-12, "the ring's downbeat is its beat's, once known");
                Assert.AreEqual(ring.Downbeat, end.Downbeat, 0.02, "predicted within 20 ms when it started");
                Assert.AreEqual(telegraph, ring.Downbeat - ring.Start, 0.02, "it closes over its telegraph");
                Assert.That(ring.T, Is.InRange(ring.Start, ring.Start + 0.25 * telegraph), "and starts as it begins to close");
                Assert.Less(ring.T, ring.Downbeat - 0.5 * telegraph);
                if (k == 0) continue;
                Assert.AreEqual(0, (end.Beat - ended[k - 1].Beat) % phrase, "on the phrase grid");
                double gap = end.Downbeat - ended[k - 1].Downbeat;
                Assert.That(gap, Is.InRange(7.0, 15.0), $"{bpm} BPM: rings {gap:F2} s apart");
            }
        }

        [Test]
        public void ARingStartsOnlyOnceItsDownbeatIsPredictedFromTheBeatsKnown()
        {
            var known = Plain(128, 90, jitter: 0.004);
            var sim = Start(known);
            sim.StepTo(90);
            var rings = Rings(sim).Where(f => f.Phase == FinisherPhase.Ring || f.Phase == FinisherPhase.Retime).ToList();
            Assert.Greater(rings.Count, 8);
            foreach (var ring in rings)
            {
                // The beats revealed when the event fired: the downbeat is theirs, or the latest's plus its beats on at the
                // mean of the latest intervals (up to four).
                var revealed = known.Where(k => k.Entry.Kind == EntryKind.Beat && k.KnownAt <= ring.T + 1e-9)
                    .Select(k => k.Entry.T).OrderBy(t => t).ToList();
                int last = revealed.Count - 1;
                Assert.GreaterOrEqual(last, 0);
                int from = Math.Max(0, last - Finishers.PeriodBeats);
                double period = (revealed[last] - revealed[from]) / (last - from);
                double expected = ring.Beat <= last ? revealed[ring.Beat] : revealed[last] + (ring.Beat - last) * period;
                Assert.AreEqual(expected, ring.Downbeat, 1e-9, $"ring {ring.Ring} {ring.Phase} at {ring.T:F3}");
                Assert.Greater(ring.Downbeat, ring.T, "always ahead of it");
            }
            Assert.IsTrue(rings.Any(f => f.Phase == FinisherPhase.Ring && f.Beat > known.Count(k => k.Entry.Kind == EntryKind.Beat && k.KnownAt <= f.T)),
                "rings start before their downbeat's own beat is revealed (they are predicted)");
        }

        [Test]
        public void FinishersAreCausal([Values(false, true)] bool realDump)
        {
            double duration = 60;
            var known = realDump ? Drive.Known(EntryDump.Load(EntryDump.House128, out duration)) : Plain(128, 60, jitter: 0.004);
            // Space now and then, and on each ring's downbeat, heard a lead before the sim takes them.
            var keys = new List<KeyRecord>();
            for (double t = 3.03; t < duration; t += 1.37) keys.Add(new KeyRecord { Key = Key.Space, Down = true, Effective = t, Heard = t - 0.09 });
            var dry = Replay(known, duration);
            foreach (var ring in Rings(dry, FinisherPhase.Miss))
                keys.Add(new KeyRecord { Key = Key.Space, Down = true, Effective = ring.Downbeat + 0.11, Heard = ring.Downbeat + 0.02 });
            keys = keys.OrderBy(k => k.Effective).ToList();
            Simulation Replay(List<KnownEntry> entries, double until)
            {
                var sim = new Simulation(Config(HordeFixture.GameArena()));
                foreach (var k in entries) sim.AddKnown(k.Entry, k.KnownAt);
                foreach (var k in keys)
                {
                    if (k.Effective > until) break;
                    sim.StepTo(k.Effective);
                    sim.Press(k.Key, k.Heard);
                }
                sim.StepTo(until);
                return sim;
            }
            int hits = 0;
            for (double cut = 9.1; cut < duration; cut += 6.7)
            {
                double at = Math.Floor(cut * 120) / 120;
                var full = Replay(known, at);
                var truncated = Replay(known.Where(k => k.KnownAt <= at).ToList(), at);
                Assert.AreEqual(full.Hash, truncated.Hash, $"truncated at {at}");
                CollectionAssert.AreEqual(full.EventsSince(0).ToArray(), truncated.EventsSince(0).ToArray());
                hits = Rings(full, FinisherPhase.Hit).Count;
            }
            Assert.Greater(hits, 0, "some presses hit");
        }

        // ---------------------------------------------------------------- the press

        [Test]
        public void APressInTheWindowFiresTheSlamAtOnceAndItClearsTheView()
        {
            var sim = Start(Plain(128, 40));
            var ring = NextRing(sim);
            // Past the beat's strikes' reach (7 m), which would kill them first.
            sim.StepTo(ring.Downbeat - 0.5);
            int nearGhost = Place(sim, "ghost", 9, 0), farGhost = Place(sim, "ghost", -11, 9);
            int brute = Place(sim, "werewolf", 0, -10), outside = Place(sim, "ghost", 26, 0);
            sim.StepTo(ring.Downbeat - 0.04);
            int cursor = sim.EventCount;
            double heard = sim.Now;
            sim.Press(Key.Space, heard);

            var hit = Rings(sim, FinisherPhase.Hit).Single();
            Assert.AreEqual(ring.Ring, hit.Ring);
            Assert.AreEqual(heard, hit.Heard);
            Assert.AreEqual(heard + Sword.PressImpactS, hit.Impact, 1e-12, "it hits at once, a press's two frames on");
            var slam = Slams(sim).Single();
            Assert.IsTrue(slam.Heavy && slam.Pressed, "a heavy slam, struck on the press");
            Assert.AreEqual(heard, slam.WindupStart, 1e-12);
            Assert.AreEqual(hit.Impact, slam.Impact, 1e-12);

            sim.StepTo(hit.Impact + 0.01);
            var deaths = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.Death).Select(e => e.AsDeath).ToList();
            CollectionAssert.IsSubsetOf(new[] { nearGhost, farGhost }, deaths.Select(d => d.Enemy).ToList(), "the crowd in view dies");
            Assert.IsTrue(deaths.Where(d => d.Enemy == nearGhost || d.Enemy == farGhost).All(d => Math.Abs(d.T - hit.Impact) < 1e-9),
                "on the impact, no build-up");
            var blows = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.Hit).Select(e => e.AsHit).ToList();
            var onBrute = blows.Single(b => !b.OnHero && b.Target == brute && b.WeaponHit == WeaponHit.HeavyStrike);
            double damage = Content.Weapon("sword").Rank(1).Power * Content.Weapon("sword").Shape.HeavyDamage * Sword.FinisherDamage;
            Assert.AreEqual(damage, onBrute.Damage, 1e-9, "ten times the sword's power");
            Assert.IsTrue(sim.EventsSince(cursor).ToArray().Any(e => e.Kind == SimEventKind.EnemyKnock && e.I0 == brute), "a survivor is thrown");
            Assert.IsFalse(blows.Any(b => !b.OnHero && b.Target == outside), "past its reach and its wave, nothing");

            // Spent: the ring ends with the hit, and the next press does nothing until the next ring.
            sim.Press(Key.Space, sim.Now);
            sim.StepTo(ring.Downbeat + 2);
            Assert.AreEqual(1, Slams(sim).Count);
            Assert.AreEqual(0, Rings(sim, FinisherPhase.Miss).Count(m => m.Ring == ring.Ring));
        }

        [Test]
        public void TheSlamBreaksStructuresInItsReach()
        {
            // Small structures 15 m east (past the heavy strike's wave, 15.1 m, only just) and 18 m north (in the slam's wave).
            var arena = ArenaData.Open(40, 40);
            arena.Structures = new[]
            {
                new StructureSite { Tiles = new[] { new Tile(30, 20) } },
                new StructureSite { Tiles = new[] { new Tile(20, 32) } },
                new StructureSite { Tiles = new[] { new Tile(20, 39) } },
            };
            var sim = Start(Plain(128, 40), Config(arena));
            var ring = NextRing(sim);
            sim.StepTo(ring.Downbeat);
            Assert.AreEqual(0, Drive.Events(sim, SimEventKind.StructureBreak).Count, "the beat's strikes break nothing");
            sim.Press(Key.Space, sim.Now);
            sim.StepTo(ring.Downbeat + 1);
            var broke = Drive.Events(sim, SimEventKind.StructureBreak).Select(e => e.AsStructureBreak.Structure).OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(new[] { 0, 1 }, broke, "the slam and its wave break what they reach; 28 m north stands");
        }

        [Test]
        public void APressOutsideTheWindowDoesNothingAndCostsNothing()
        {
            var sim = Start(Plain(128, 60));
            var ring = NextRing(sim);
            sim.StepTo(ring.Downbeat - 0.3);
            sim.Press(Key.Space, sim.Now);
            sim.Release(Key.Space, sim.Now);
            sim.StepTo(ring.Downbeat - Finishers.WindowS - 0.02);
            sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(0, Slams(sim).Count, "early presses fire nothing");
            Assert.AreEqual(0, Rings(sim, FinisherPhase.Hit).Count);
            sim.StepTo(ring.Downbeat + 0.06);
            sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(1, Rings(sim, FinisherPhase.Hit).Count, "and the ring still takes one in its window");

            var next = NextRing(sim);
            sim.StepTo(next.Downbeat + Finishers.WindowS + 0.02);
            sim.Press(Key.Space, sim.Now);
            sim.StepTo(next.Downbeat + 1);
            var miss = Rings(sim, FinisherPhase.Miss).Single();
            Assert.AreEqual(next.Ring, miss.Ring, "a late press, or none: the ring ends");
            Assert.AreEqual(1, Slams(sim).Count, "with nothing fired");
            Assert.AreEqual(Health(sim), sim.State.Hero.Health, "and nothing lost");
            var third = NextRing(sim);
            Assert.AreEqual(next.Ring + 1, third.Ring, "the next phrase rings as ever");
        }

        static double Health(Simulation sim) => sim.State.Hero.MaxHealth;

        /// <summary>With `--dash` too, Space in a ring's window is the finisher's and dashes not; outside it, it dashes.</summary>
        [Test]
        public void WithTheDashTooAPressInTheWindowIsAFinisherAndNotADash()
        {
            var config = Config();
            config.Dash = true;
            var sim = Start(Plain(128, 40), config);
            var ring = NextRing(sim);
            sim.StepTo(ring.Downbeat - 0.5);
            sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.HeroDash).Count, "outside the window: a dash");
            sim.StepTo(ring.Downbeat);
            sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(1, Rings(sim, FinisherPhase.Hit).Count, "in it: the finisher");
            Assert.AreEqual(1, Slams(sim).Count);
            sim.StepTo(ring.Downbeat + 1);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.HeroDash).Count, "and no dash");
        }

        [Test]
        public void ALiveKeyHeardInTheWindowHitsThoughTheSimTakesItAfter()
        {
            var sim = Start(Plain(128, 40));
            var ring = NextRing(sim);
            int ghost = -1;
            sim.StepTo(ring.Downbeat - 0.3);
            ghost = Place(sim, "ghost", 10, 0);
            // Heard 50 ms after the downbeat; taken 0.2 s later, past the window.
            sim.StepTo(ring.Downbeat + 0.25);
            double heard = ring.Downbeat + 0.05;
            int cursor = sim.EventCount;
            sim.Press(Key.Space, heard);
            var hit = Rings(sim, FinisherPhase.Hit).Single();
            Assert.AreEqual(heard + Sword.PressImpactS, hit.Impact, 1e-12);
            Assert.Less(hit.Impact, sim.Now, "resolved in heard time");
            var death = sim.EventsSince(cursor).ToArray().Single(e => e.Kind == SimEventKind.Death && e.I0 == ghost);
            Assert.AreEqual(hit.Impact, death.T, 1e-12, "stamped at its own time");

            // Heard after the window: nothing, though taken as soon.
            var next = NextRing(sim);
            sim.StepTo(next.Downbeat + 0.25);
            sim.Press(Key.Space, next.Downbeat + Finishers.WindowS + 0.01);
            Assert.AreEqual(1, Rings(sim, FinisherPhase.Hit).Count);
        }

        // ---------------------------------------------------------------- holds and the gate

        [Test]
        public void ARingInFlightIsDroppedByAFreezeAndNoneRingsWhileFrozen()
        {
            var sim = Start(Plain(128, 60));
            var ring = NextRing(sim);
            sim.StepTo(ring.Downbeat - 0.3);
            sim.Control(RunControl.Pause);
            sim.StepTo(ring.Downbeat - 0.2);
            var drop = Rings(sim, FinisherPhase.Drop).Single();
            Assert.AreEqual(ring.Ring, drop.Ring);
            sim.Press(Key.Space, ring.Downbeat);
            sim.StepTo(ring.Downbeat + 9);
            Assert.AreEqual(1, Rings(sim, FinisherPhase.Ring).Count, "frozen through the next phrase: no ring");
            Assert.AreEqual(0, Slams(sim).Count);
            sim.Control(RunControl.Resume);
            var next = NextRing(sim);
            Assert.Greater(next.T, ring.Downbeat + 9, "rings come back after the resume");
        }

        [Test]
        public void NoRingsForAFallenHeroOrUnderTheGate()
        {
            var below = Start(Plain(128, 40, evidence: 0.1));
            below.StepTo(40);
            Assert.AreEqual(0, Rings(below).Count, "no reliable beat: no rings");

            var sim = Start(Plain(128, 40));
            var ring = NextRing(sim);
            sim.StepTo(ring.Downbeat - 0.3);
            sim.State.Hero.Fallen = true;
            sim.StepTo(40);
            Assert.AreEqual(ring.Ring, Rings(sim, FinisherPhase.Drop).Single().Ring);
            Assert.AreEqual(1, Rings(sim, FinisherPhase.Ring).Count);
        }
    }
}
