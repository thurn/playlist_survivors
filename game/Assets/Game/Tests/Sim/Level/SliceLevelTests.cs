using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The cinematic slice's level (<see cref="SliceLevel"/>) on a real dump in the game's arena:
    /// every role of its roster arrives and steps, the sword kills and breaks structures, and the
    /// horde stays dense but under the cap.
    /// </summary>
    public class SliceLevelTests
    {
        /// <summary>The game camera's loud view at 16:9 with its slack (the client's CameraRig.Footprint), metres around the hero.</summary>
        static readonly double[] GameCamera = { -18.31, -13.83, 18.31, -13.83, 24.93, 18.37, -24.93, 18.37 };

        [Test]
        public void TheSlicePlaysAFullHordeAgainstTheSwordOnARealDump()
        {
            var entries = EntryDump.Load("accelerando-100-140-30s.jsonl", out double duration);
            var config = SliceLevel.Config(HordeFixture.Content, HordeFixture.GameArena(), GameCamera);
            var sim = new Simulation(config);
            int fewest = int.MaxValue, most = 0;
            Drive.Feed(sim, entries, duration, eachFrame: s =>
            {
                if (s.Now < 5) return;
                fewest = System.Math.Min(fewest, s.State.EnemiesAlive);
                most = System.Math.Max(most, s.State.EnemiesAlive);
            });
            var content = HordeFixture.Content;
            var spawned = Drive.Events(sim, SimEventKind.Spawn).Select(e => content.Roles[e.AsSpawn.Role].Id).Distinct();
            CollectionAssert.AreEquivalent(SliceLevel.Roster, spawned);
            int deaths = Drive.Events(sim, SimEventKind.Death).Count;
            int heavy = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Count(a => a.ByHero && a.Heavy);
            int breaks = Drive.Events(sim, SimEventKind.StructureBreak).Count;
            TestContext.Progress.WriteLine($"slice: alive {fewest}..{most} after 5 s, {deaths} deaths, {heavy} heavy strikes, {breaks} breaks, " +
                                           $"{Drive.Events(sim, SimEventKind.EnemyStep).Count} steps, health {sim.State.Hero.Health:F0}/{sim.State.Hero.MaxHealth:F0}");
            Assert.GreaterOrEqual(fewest, SliceLevel.Minimum / 2, "dense enough to sell it");
            Assert.Less(most, 300, "under the cap");
            Assert.Greater(deaths, 100);
            Assert.Greater(heavy, 5);
            Assert.Greater(breaks, 0);
            Assert.IsFalse(sim.State.Hero.Fallen);
        }
    }
}
