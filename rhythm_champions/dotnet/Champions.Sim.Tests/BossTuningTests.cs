using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss tuning (ps-6y2.17; design.md, "Level 1, minute by minute"): a scripted reference build kills the Ogre in
    /// 60-90 s and the dragon in 2-3 minutes. The reference player keeps within Sword Slash's reach of the boss and dashes
    /// through each attack on its beat (every perfect dodge it can make); the builds are what a good run holds by each
    /// boss: by 7:00 the first offer and three level-ups, by 13:00 the Ogre's treasure and three more. Bosses attack on
    /// the beat, so a duel lasts about as many beats at any tempo: the targets hold at 128 BPM and beatless (0.5 s a
    /// beat), and rhythm_champions/evidence/bosses/README.md records the lengths from 80 to 174 BPM.
    /// </summary>
    public class BossTuningTests
    {
        static readonly GameContent Content = DashTests.Content;

        /// <summary>A good build at the Ogre (7:00): Blade Rush from the first offer, then Sword Slash 2, Orbit Blades, Keen Edge.</summary>
        public static readonly (string[] Build, string Dash, int Rank) OgreBuild = (new[] { "swordSlash:2", "orbitBlades:1", "keenEdge:1" }, "bladeRush", 1);

        /// <summary>A good build at the dragon (13:00): the Ogre's treasure raised Sword Slash to 3, then Orbit Blades 2, Wide Arcs, Blade Rush 2.</summary>
        public static readonly (string[] Build, string Dash, int Rank) DragonBuild = (new[] { "swordSlash:3", "orbitBlades:2", "keenEdge:1", "wideArcs:1" }, "bladeRush", 2);

        /// <summary>
        /// The duel's length, s, from the boss's entrance to its death (NaN if it outlived <paramref name="limit"/>), and the
        /// hero's health left; the reference player dodges when <paramref name="dodge"/>, and only chases when not (then immortal).
        /// </summary>
        public static (double Length, double Health, int Staggers) Duel(string boss, double bpm, bool dodge = true, double limit = 400)
        {
            var (build, dash, rank) = boss == "fatOgre" ? OgreBuild : DragonBuild;
            var song = bpm > 0 ? new FixtureSong().Beats(0, limit + 80, bpm) : new FixtureSong().Gap(0, limit + 80);
            var sim = BossTests.Duel(boss, song, c => { if (!dodge) c.Hero.Health = 1e9; }, build);
            sim.Dashes.Equip(sim, Content.Dashes.IndexOf(dash), rank);
            var dodger = new BossTests.Dodger(through: true);
            double arrived = double.NaN, chased = 0;
            while (sim.State.Phase == RunPhase.Playing && sim.Bosses.Stage != BossStage.Dead && sim.Now < limit + 80)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                if (!sim.Bosses.InDuel) continue;
                if (double.IsNaN(arrived)) arrived = Drive.Events(sim, SimEventKind.BossArrive).Single().T;
                if (dodge) dodger.Frame(sim);
                if (sim.Now - chased < 0.25) continue;
                // Back within reach: walk to 3 m short of the boss.
                chased = sim.Now;
                ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
                sim.HeroWas(sim.Now, out double hx, out double hy);
                double dx = b.X - hx, dy = b.Y - hy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 4.5) sim.Target(b.X - dx / d * 3, b.Y - dy / d * 3, sim.Now - BossTests.Dodger.Lead);
            }
            var death = Drive.Events(sim, SimEventKind.BossDeath).SingleOrDefault();
            double length = death.Kind == SimEventKind.BossDeath ? death.T - arrived : double.NaN;
            return (length, sim.State.Hero.Health, Drive.Events(sim, SimEventKind.BossStagger).Count(s => s.I1 == 1));
        }

        [Test]
        public void TheReferenceBuildKillsTheOgreInSixtyToNinetySeconds([Values(0, 128)] double bpm)
        {
            var (length, health, staggers) = Duel("fatOgre", bpm);
            TestContext.Out.WriteLine($"fatOgre at {bpm} BPM: {length:F1} s, hero {health:F0}, {staggers} staggers");
            Assert.That(length, Is.InRange(60.0, 90.0));
            Assert.Greater(health, 0, "the reference player lives");
        }

        [Test]
        public void TheReferenceBuildKillsTheDragonInTwoToThreeMinutes([Values(0, 128)] double bpm)
        {
            var (length, health, staggers) = Duel("dragon", bpm);
            TestContext.Out.WriteLine($"dragon at {bpm} BPM: {length:F1} s, hero {health:F0}, {staggers} staggers");
            Assert.That(length, Is.InRange(120.0, 180.0));
            Assert.Greater(health, 0, "the reference player lives");
        }

        [Test, Explicit("prints the duel lengths for evidence/bosses/README.md")]
        public void PrintTheDuelLengths()
        {
            foreach (string boss in new[] { "fatOgre", "dragon" })
                foreach (double bpm in new[] { 0.0, 80, 100, 128, 160, 174 })
                    foreach (bool dodge in new[] { true, false })
                    {
                        var (length, health, staggers) = Duel(boss, bpm, dodge);
                        TestContext.Out.WriteLine($"| {boss} | {(bpm > 0 ? bpm + " BPM" : "beatless")} | {(dodge ? "dodges" : "chases only")} | {length:F1} s | {(dodge ? health.ToString("F0") : "-")} | {staggers} |");
                    }
        }
    }
}
