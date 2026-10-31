using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The slice's debug commands (unity-game.md, Debug Surfaces): a forced section change lands
    /// on the next downbeat as a detected one, a forced accent makes the next main strike heavy,
    /// and structures break and regrow on demand. Each is recorded like a key.
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
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.Attacker == -1).ToList();

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
            var slams = HeroAttacks(sim).Where(a => a.Kind == (int)HeroAttackKind.Slam).ToList();
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
            Assert.AreEqual(after.First(a => a.Kind != (int)HeroAttackKind.FollowUp).Impact, heavy[0].Impact,
                "the first main strike to wind up after the command");
            Assert.AreEqual((int)HeroAttackKind.Spin, heavy[0].Kind);
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
    }
}
