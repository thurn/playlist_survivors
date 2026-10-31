using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The evolutions' own rules (unity-game.md, The Other Weapons and Crystal Structures; native's
    /// combat.rs, weapons.rs and bible.rs): Soul Eater's drain, Bloody Tear's critical blows, Unholy
    /// Vespers' permanent ring, and an evolved hit on an accented beat breaking structures. Holy Wand
    /// and Thunder Loop are their base weapons' rules with their own numbers (MagicWandTests,
    /// LightningTests).
    /// </summary>
    public class EvolutionTests
    {
        const double Eps = 1e-9;

        static GameContent Content => HordeFixture.Content;

        static FixtureSong Song(double until = 20) => new FixtureSong(3).Beats(0, until, 120);

        static Simulation Armed(string weapon, Stats? stats = null, ArenaData arena = null, IEnumerable<KnownEntry> extra = null,
            FixtureSong song = null)
        {
            var config = HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = new[] { (weapon, Content.Weapon(weapon).MaxRank) }, Cards = false, Stats = stats };
            var sim = new Simulation(config);
            foreach (var k in Drive.Known((song ?? Song()).Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            if (extra != null)
                foreach (var k in extra) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static Stats With(Func<Stats, Stats> change) => change(Content.Progression.Hero.Stats);

        /// <summary>An enemy at (dx, dy) from the hero that nothing here can kill.</summary>
        static int Dummy(Simulation sim, double dx, double dy, string role = "skeleton")
        {
            int i = HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);
            sim.State.EnemyStore[i].Health = 1e9;
            return i;
        }

        static List<HitEvent> Hits(Simulation sim, string weapon) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => !h.OnHero && h.Weapon == Content.WeaponIndex(weapon)).ToList();

        // ---------------------------------------------------------------- Soul Eater

        [Test]
        public void SoulEaterHealsOnEveryHitAndGrowsAPointOfPowerForEach600Healed()
        {
            var noRecovery = With(s => { s.Recovery = 0; return s; });
            var sim = Armed("soul-eater", noRecovery);
            var hero = sim.State.Hero;
            hero.Health = 20;
            int[] dummies = { Dummy(sim, 1, 0), Dummy(sim, -1, 0.5), Dummy(sim, 0, -2) };
            sim.StepTo(0.5);
            var first = Hits(sim, "soul-eater");
            Assert.AreEqual(3, first.Count, "the first pulse");
            Assert.IsTrue(first.All(h => Math.Abs(h.Damage - 2) < Eps), "rank power 2 x Might, nothing healed before");
            Assert.AreEqual(3, sim.Health.Healed, Eps, "1 + Recovery a hit");

            // 600 more healed, by any means, before the next pulse: one more point of power.
            for (int k = 0; k < 6; k++)
            {
                hero.Health = 20;
                sim.Health.Heal(sim.Now, 100, 0);
            }
            sim.StepTo(1.5);
            var second = Hits(sim, "soul-eater").Skip(3).ToList();
            Assert.AreEqual(3, second.Count);
            Assert.IsTrue(second.All(h => Math.Abs(h.Damage - (2 + 603.0 / 600)) < 1e-9), $"{second[0].Damage}");

            // Garlic itself drains nothing.
            var garlic = Armed("garlic", noRecovery);
            garlic.State.Hero.Health = 20;
            Dummy(garlic, 1, 0);
            garlic.StepTo(2);
            Assert.Greater(Hits(garlic, "garlic").Count, 0);
            Assert.AreEqual(0, garlic.Health.Healed, Eps);
        }

        [Test]
        public void SoulEatersPowerStopsSixPointsUp()
        {
            var sim = Armed("soul-eater", With(s => { s.Recovery = 0; return s; }));
            for (int k = 0; k < 40; k++)
            {
                sim.State.Hero.Health = 1;
                sim.Health.Heal(0, 110, 0);
            }
            Dummy(sim, 1, 0);
            sim.StepTo(0.5);
            Assert.AreEqual(2 + Garlic.SoulPowerCap, Hits(sim, "soul-eater")[0].Damage, Eps);
        }

        // ---------------------------------------------------------------- Bloody Tear

        [Test]
        public void BloodyTearsBlowsAreCriticalAtItsChanceTimesLuckAndACriticalHealsEightAtMostOnceASecond()
        {
            // Luck 10: a 10% chance ten times over, every blow critical.
            var sim = Armed("bloody-tear", With(s => { s.Luck = 10; return s; }));
            sim.State.Hero.Health = 40;
            Dummy(sim, 2, 0, "xlmummy");
            Dummy(sim, -2, 1, "xlmummy");
            sim.StepTo(4.2);
            var hits = Hits(sim, "bloody-tear");
            Assert.Greater(hits.Count, 10);
            double power = Content.Weapon("bloody-tear").Rank(1).Power;
            Assert.IsTrue(hits.All(h => h.Critical && Math.Abs(h.Damage - 2 * power) < Eps), "double damage, every one critical");
            double last = double.NegativeInfinity;
            int heals = 0;
            foreach (double t in hits.Select(h => h.T))
                if (t >= last + Sword.TearHealEveryS - Eps)
                {
                    last = t;
                    heals++;
                }
            Assert.AreEqual(Sword.TearHeal * heals, sim.Health.Healed, Eps, "8 for the first critical blow in each second of run clock");

            // No luck, no crits; the sword itself has none at any luck.
            foreach (var (weapon, luck) in new[] { ("bloody-tear", 0.0), ("sword", 10.0) })
            {
                var plain = Armed(weapon, With(s => { s.Luck = luck; return s; }));
                plain.State.Hero.Health = 40;
                Dummy(plain, 2, 0, "xlmummy");
                plain.StepTo(4.2);
                Assert.Greater(Hits(plain, weapon).Count, 10);
                Assert.IsFalse(Hits(plain, weapon).Any(h => h.Critical), weapon);
                Assert.AreEqual(0, plain.Health.Healed, Eps, weapon);
            }
        }

        // ---------------------------------------------------------------- Unholy Vespers

        [Test]
        public void UnholyVespersRingNeverEndsUntilTheNextShotTakesOver()
        {
            var sim = Armed("unholy-vespers");
            sim.StepTo(15);
            var orbits = Drive.Events(sim, SimEventKind.Orbit).Select(e => e.AsOrbit).ToList();
            var rings = orbits.GroupBy(o => o.Ring).Select(g => g.ToList()).ToList();
            Assert.GreaterOrEqual(rings.Count, 4);
            double duration = Content.Weapon("unholy-vespers").Rank(1).DurationS;
            for (int k = 0; k < rings.Count; k++)
            {
                var born = rings[k][0];
                Assert.IsTrue(double.IsPositiveInfinity(born.End), $"ring {k} is born without an end");
                if (k == rings.Count - 1)
                {
                    Assert.AreEqual(1, rings[k].Count, "the newest ring still has no end");
                    continue;
                }
                double next = rings[k + 1][0].Born;
                Assert.AreEqual(2, rings[k].Count);
                Assert.AreEqual(next, rings[k][1].T, Eps, "cut when the next is born");
                Assert.AreEqual(Math.Max(next, born.Born + duration), rings[k][1].End, Eps, "at that shot, or its duration if later");
            }

            // King Bible's rings end at their duration.
            var bible = Armed("king-bible");
            bible.StepTo(10);
            var first = Drive.Events(bible, SimEventKind.Orbit).Select(e => e.AsOrbit).First();
            Assert.AreEqual(first.Born + Content.Weapon("king-bible").Rank(8).DurationS, first.End, Eps);
        }

        // ---------------------------------------------------------------- structure breaks

        /// <summary>A 40 x 40 open arena (the hero on tile (20, 20), centred on (0.75, 0.75)) with one small structure two tiles east: 2.25 m away.</summary>
        static ArenaData OneStructure()
        {
            var arena = ArenaData.Open(40, 40);
            arena.Structures = new[] { new StructureSite { Tiles = new[] { new Tile(22, 20) } } };
            return arena;
        }

        static List<SimEvent> Breaks(Simulation sim) => Drive.Events(sim, SimEventKind.StructureBreak);

        [TestCase("soul-eater", true, true)]
        [TestCase("soul-eater", false, false)]
        [TestCase("garlic", true, false)]
        public void AnEvolvedHitOnAnAccentedBeatBreaksTheStructuresItReaches(string weapon, bool accent, bool breaks)
        {
            // The aura's pulses come every second on the beat; a strong onset accents the beat at 3 s.
            // Soul Eater reaches 5.5 m, Garlic at rank 8 3.7 m: both cover the structure.
            var onset = new[] { new KnownEntry(StreamEntry.Onset(3.0, 0, 300, true), 1.0) };
            var sim = Armed(weapon, arena: OneStructure(), extra: accent ? onset : null);
            sim.StepTo(6);
            var broken = Breaks(sim);
            if (!breaks)
            {
                Assert.AreEqual(0, broken.Count);
                Assert.IsTrue(sim.State.Structures[0].Standing);
                return;
            }
            Assert.AreEqual(1, broken.Count);
            Assert.AreEqual(3.0, broken[0].T, Eps, "on the accented beat's pulse");
            Assert.IsFalse(sim.State.Structures[0].Standing);
        }

        [Test]
        public void ALargeStructureTakesOneBreakingHitAnAccentHoweverOftenTheBladesTouchIt()
        {
            // Unholy Vespers' blades sweep a two-hit structure on their 5 m ring on every tick of an accent's
            // window: the accent at 3 s counts once, and the one at 5 s breaks it.
            var arena = ArenaData.Open(40, 40);
            arena.Structures = new[] { new StructureSite { Tiles = new[] { new Tile(23, 19), new Tile(23, 20), new Tile(23, 21) }, Large = true } };
            var onsets = new[] { 3.0, 5.0 }.Select(t => new KnownEntry(StreamEntry.Onset(t, 0, 300, true), 1.0));
            var sim = Armed("unholy-vespers", arena: arena, extra: onsets);
            Assert.AreEqual(2, sim.State.Structures[0].HitsToBreak);

            sim.StepTo(4.5);
            var glints = Drive.Events(sim, SimEventKind.Glint).Select(e => e.AsGlint).Where(g => g.Source == StructureHit.EvolvedAccent).ToList();
            Assert.Greater(glints.Count, 0, "the blades touch it in the accent's window");
            Assert.IsTrue(glints.All(g => g.Hits == 1), "one breaking hit for the whole accent");
            Assert.IsTrue(glints.All(g => Math.Abs(g.T - 3.0) <= Accents.Window + 1e-9));
            Assert.IsTrue(sim.State.Structures[0].Standing);

            sim.StepTo(6);
            var broken = Breaks(sim);
            Assert.AreEqual(1, broken.Count);
            Assert.AreEqual(5.0, broken[0].T, Accents.Window + 1e-9, "the second accent's hit breaks it");
        }
    }
}
