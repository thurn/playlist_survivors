using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>The floor's zone key (design.md, The World &gt; Zones; Decision 23).</summary>
    public class ZoneTests
    {
        static List<SimEvent> Changes(Simulation sim) => Drive.Events(sim, SimEventKind.Zone);

        static SimConfig Config(Action<ZoneRules> tweak = null)
        {
            var config = new SimConfig();
            tweak?.Invoke(config.Zones);
            return config;
        }

        [Test]
        public void TheContentHoldsTheDesignsZones()
        {
            var zones = GameContent.Load(Checkout.In("Content")).Zones;
            Assert.AreEqual(4, zones.Keys, "two cool, a violet, a dim warm");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, zones.Cycle);
            Assert.AreEqual(3, zones.Warm, "the dragon's");
            Assert.AreEqual(180, zones.EveryS, "every 3 minutes without sections");
            Assert.AreEqual(0.5, zones.BeatlessFadeS, "a beat's fallback");
        }

        [Test]
        public void AStrongSectionChangeMovesTheKeyOnTheNextBeatOverThatBeat()
        {
            // The fixture's changes fall on every 32nd beat (16 s apart at 120 BPM), known 0.35 s before they are
            // heard. The first, at 16 s, comes before 30 s of run clock; the one at 32 s moves the key; 48 s is
            // inside the gap after it; 64 s moves it again.
            var song = new FixtureSong().Beats(0, 70, 120);
            var sim = Drive.Run(song, 70, Config());
            var changes = Changes(sim);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(32.0, changes[0].T, 1e-9, "on the beat after the change became known");
            Assert.AreEqual(1, changes[0].I0);
            Assert.AreEqual(0, changes[0].I1);
            Assert.AreEqual(ZoneReason.Section, (ZoneReason)changes[0].I2);
            Assert.AreEqual(0.5, changes[0].D0, 1e-9, "over a beat");
            Assert.AreEqual(64.0, changes[1].T, 1e-9);
            Assert.AreEqual(2, changes[1].I0);
            Assert.AreEqual(2, sim.State.Zone);
            Assert.AreEqual(2, sim.Snapshot().Zone);
            CollectionAssert.Contains(song.BeatTimes(), changes[0].T, "a zone change starts on a beat");
        }

        [Test]
        public void WeakChangesLeaveTheKey()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 70, 120), 70, Config(z => z.SectionMinStrength = 1));
            Assert.IsEmpty(Changes(sim), "the fixture's changes are 0.8 strong");
        }

        [Test]
        public void WithoutSectionsTheKeyMovesOnEveryThreeMinutes()
        {
            var song = new FixtureSong().Beats(0, 400, 120);
            var sim = Drive.Run(song, 400, Config(z => z.SectionMinStrength = 1));
            var changes = Changes(sim);
            Assert.AreEqual(2, changes.Count);
            var beats = song.BeatTimes();
            for (int i = 0; i < 2; i++)
            {
                double due = (i == 0 ? 0 : changes[i - 1].T) + 180; // three minutes of run clock after the last key
                Assert.That(changes[i].T, Is.InRange(due - 1e-6, due + 0.5 + 1e-6), "on the first beat once due");
                CollectionAssert.Contains(beats, changes[i].T);
                Assert.AreEqual(ZoneReason.Timer, (ZoneReason)changes[i].I2);
            }
            Assert.AreEqual(new[] { 1, 2 }, changes.Select(e => e.I0).ToArray());
        }

        [Test]
        public void TheKeysCycleAndSkipTheWarmOne()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 400, 120), 400, Config(z => z.SectionMinGapS = 0));
            var keys = Changes(sim).Select(e => e.I0).ToList();
            Assert.That(keys.Count, Is.GreaterThan(6));
            for (int i = 0; i < keys.Count; i++) Assert.AreEqual((i + 1) % 3, keys[i], $"change {i}");
        }

        [Test]
        public void BeatlessTheKeyChangesAtOnceOverTheFallback()
        {
            var song = new FixtureSong().Beats(0, 40, 120).Gap(40, 80);
            var sim = Drive.Run(song, 80, Config(z =>
            {
                z.SectionMinStrength = 1;
                z.EveryS = 50;
            }));
            var changes = Changes(sim);
            Assert.AreEqual(1, changes.Count);
            Assert.AreNotEqual(Reliability.Reliable, sim.State.Beat);
            Assert.AreEqual(50.0, changes[0].T, 2 * Simulation.TickSeconds, "at the tick it fell due");
            Assert.AreEqual(0.5, changes[0].D0, 1e-9, "the beatless fallback");
        }

        [Test]
        public void TheDragonsEntranceTurnsTheFloorWarmForGood()
        {
            var song = new FixtureSong().Beats(0, 120, 120);
            var known = Drive.Known(song.Entries());
            var sim = new Simulation(Config());
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(40);
            Assert.AreEqual(1, sim.Zones.Key);
            sim.Zones.Warm(sim, sim.Now);
            sim.StepTo(120);
            var changes = Changes(sim);
            Assert.AreEqual(2, changes.Count, "nothing moves the warm key on");
            Assert.AreEqual(3, changes[1].I0);
            Assert.AreEqual(ZoneReason.Warm, (ZoneReason)changes[1].I2);
            Assert.AreEqual(0.5, changes[1].D0, 1e-9, "over a beat");
            Assert.IsTrue(sim.Zones.Locked);
            Assert.AreEqual(3, sim.State.Zone);
        }

        [Test]
        public void ChangesHeardBeforeTheRunStartsArePassedOver()
        {
            var song = new FixtureSong().Beats(0, 100, 120);
            var config = Config(z => z.SectionMinGapS = 0);
            config.Start = RunStart.OnControl;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(40);
            Assert.IsEmpty(Changes(sim), "the changes at 16 s and 32 s came before the start");
            sim.Control(RunControl.Start, sim.Now);
            sim.StepTo(100);
            var changes = Changes(sim);
            Assert.AreEqual(new[] { 48.0, 64.0, 80.0, 96.0 }, changes.Select(e => e.T).ToArray());
        }

        [Test]
        public void ZoneChangesReplayExactly()
        {
            var song = new FixtureSong().Beats(0, 200, 120);
            var a = Drive.Run(song, 200, Config());
            var b = Drive.Run(song, 200, Config());
            Assert.AreEqual(Changes(a).Count, Changes(b).Count);
            Assert.That(Changes(a).Count, Is.GreaterThan(0));
            Assert.AreEqual(a.Hash, b.Hash);
        }
    }
}
