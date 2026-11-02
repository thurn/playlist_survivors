using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The attacks' and mods' content and build (design.md, "The attacks", "Mods", Decision 11): the design's
    /// numbers, read strictly; upgrades and slots; and the attacks allocating nothing per tick.
    /// </summary>
    public class AttackContentTests
    {
        [Test]
        public void TheContentHoldsTheDesignsAttacksAndMods()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var a = content.Attacks;
            var defaults = new AttackRules();
            var s = a.SwordSlash;
            Assert.AreEqual(HitTags.Strike | HitTags.Area, s.Tags);
            Assert.AreEqual(new[] { 150.0, 210, 210, 210 }, s.Ranks.Select(r => r.ArcDeg));
            Assert.AreEqual(new[] { 3.0, 3.8, 3.8, 3.8 }, s.Ranks.Select(r => r.ReachM));
            Assert.AreEqual(new[] { 0, 0, 4, 4 }, s.Ranks.Select(r => r.SpinEvery), "every fourth slash spins from rank 3");
            Assert.AreEqual(new[] { false, false, false, true }, s.Ranks.Select(r => r.Tempest));
            Assert.AreEqual((1.0, 1.5, 12.0), (s.Damage, s.SpinDamage, s.Tempest.RangeM));
            Assert.AreEqual(1.0, a.AimSlackM, "reach plus 1 m");

            var o = a.OrbitBlades;
            Assert.AreEqual(HitTags.Summon, o.Tags);
            Assert.AreEqual((0.6, 4), (o.Damage, o.LapBeats), "0.6x, a lap a bar");
            Assert.AreEqual(new[] { 2, 3, 4, 6 }, o.Ranks.Select(r => r.Swords));
            Assert.AreEqual(2.5, o.Ranks[0].RadiusM);
            Assert.AreEqual(4.0, o.Ranks[2].DownbeatRadiusM);
            Assert.AreEqual((2.0, 5.0), (o.Ranks[3].BreatheFromM, o.Ranks[3].BreatheToM));

            var b = a.BladeRain;
            Assert.AreEqual(HitTags.Area | HitTags.Summon, b.Tags);
            Assert.AreEqual((1.2, 10.0, 1.5), (b.Damage, b.RangeM, b.ImpactM));
            Assert.AreEqual(new[] { 4, 2, 2, 8 }, b.Ranks.Select(r => r.EveryHalfBeats), "every 2 beats, every beat, every beat, each downbeat");
            Assert.AreEqual(new[] { 1, 1, 2, 8 }, b.Ranks.Select(r => r.Swords));

            var c = a.Crescent;
            Assert.AreEqual(HitTags.Area, c.Tags);
            Assert.AreEqual((1.0, 2.0), (c.Damage, c.HalfWidthM), "1x, 4 m wide");
            Assert.AreEqual(new[] { 1, 2, 4, 0 }, c.Ranks.Select(r => r.Ways));
            Assert.AreEqual(new[] { 10.0, 10, 10, 14 }, c.Ranks.Select(r => r.RangeM));
            Assert.IsTrue(c.Ranks.All(r => r.EveryHalfBeats == 8), "each downbeat");

            var p = a.Phantom;
            Assert.AreEqual(HitTags.Strike | HitTags.Area | HitTags.Summon, p.Tags);
            Assert.AreEqual((0.7, 1.0), (p.Damage, p.DelayBeats));
            Assert.AreEqual(new[] { 1, 1, 2, 3 }, p.Ranks.Select(r => r.Phantoms));
            Assert.AreEqual(new[] { false, true, true, true }, p.Ranks.Select(r => r.RepeatsDash));

            var m = content.Mods;
            Assert.AreEqual((HitTags.Area, 1.4, 1.8), (m.WideArcs.Tag, m.WideArcs.Area[0], m.WideArcs.Area[1]));
            Assert.AreEqual((HitTags.Strike, 0.5, 0.75, 0.5), (m.TwinStrike.Tag, m.TwinStrike.Damage[0], m.TwinStrike.Damage[1], m.TwinStrike.AfterBeats));
            CollectionAssert.AreEqual(new[] { AttackKind.SwordSlash, AttackKind.Crescent }, m.Tempo.Attacks[0]);
            Assert.AreEqual(Upgrade.AttackCount, m.Tempo.Attacks[1].Length, "every attack at rank 2");
            Assert.AreEqual((HitTags.Strike, 1.5, 2.0), (m.KeenEdge.Tag, m.KeenEdge.Damage[0], m.KeenEdge.Damage[1]));
            CollectionAssert.AreEqual(new[] { 2.0, 2.0 }, m.KeenEdge.Downbeat);
            CollectionAssert.AreEqual(new[] { 0.45, 0.35 }, m.Quickstep.CooldownS);
            Assert.AreEqual((HitTags.Dash, 2.0, 0.8), (m.Momentum.Tag, m.Momentum.DashDamage, m.Momentum.TrailDamage));
            CollectionAssert.AreEqual(new[] { 0, 1 }, m.Momentum.Repeats);
            CollectionAssert.AreEqual(new[] { 1.0 }, m.Afterimage.Beats[0]);
            CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, m.Afterimage.Beats[1]);
            CollectionAssert.AreEqual(new[] { 0.3, 0.6 }, m.Shatterburst.Share);
            Assert.AreEqual((25, 0.02, 0.04), (m.Lifeblood.Kills, m.Lifeblood.Heal[0], m.Lifeblood.Heal[1]));
            Assert.AreEqual((3.0, 1.0), (m.Encore.Damage, m.Encore.WithinBeats));
            CollectionAssert.AreEqual(new[] { 1, 2 }, m.Encore.Strikes);

            // The code's defaults are the content's, so a sim made without the content plays the same.
            Assert.AreEqual(defaults.SwordSlash.Ranks, s.Ranks);
            Assert.AreEqual(defaults.OrbitBlades.Ranks, o.Ranks);
            Assert.AreEqual(defaults.BladeRain.Ranks, b.Ranks);
            Assert.AreEqual(defaults.Crescent.Ranks, c.Ranks);
            Assert.AreEqual(defaults.Phantom.Ranks, p.Ranks);
            Assert.AreEqual((defaults.BeatlessBeatS, defaults.WaveSlackM, defaults.SwordSlash.WindupS), (a.BeatlessBeatS, a.WaveSlackM, s.WindupS));
        }

        [Test]
        public void TheAttacksAndModsAreReadStrictly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string f in Directory.GetFiles(Checkout.In("Content"), "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
                string attacks = File.ReadAllText(Checkout.In("Content", AttackRules.File));
                string mods = File.ReadAllText(Checkout.In("Content", ModRules.File));
                void Fails(string file, string text, string message)
                {
                    File.WriteAllText(Path.Combine(dir, file), text);
                    var e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                    StringAssert.Contains(message, e.Message);
                    File.WriteAllText(Path.Combine(dir, AttackRules.File), attacks);
                    File.WriteAllText(Path.Combine(dir, ModRules.File), mods);
                }
                Fails(AttackRules.File, attacks.Replace("\"spinDamage\": 1.5,", "\"spinDamage\": 1.5, \"spinSpeed\": 2,"), "attacks.json.swordSlash.spinSpeed: unknown field");
                Fails(AttackRules.File, attacks.Replace("\"tags\": [\"Summon\"]", "\"tags\": [\"Sumon\"]"), "attacks.json.orbitBlades.tags[0]: a tag");
                Fails(AttackRules.File, attacks.Replace("{ \"ways\": 4, \"rangeM\": 10, \"everyHalfBeats\": 8 }", "{ \"ways\": 3, \"rangeM\": 10, \"everyHalfBeats\": 8 }"), "ways");
                Fails(AttackRules.File, attacks.Replace("{ \"everyHalfBeats\": 2, \"swords\": 2 },", ""), "ranks 1-3 and the evolution");
                Fails(ModRules.File, mods.Replace("\"tag\": \"Kill\",\n    \"radiusM\"", "\"tag\": \"Strike\",\n    \"radiusM\""), "mods.json.shatterburst.tag: Kill");
                Fails(ModRules.File, mods.Replace("\"swordSlash\", \"crescent\"", "\"swordSlash\", \"crescents\""), "mods.json.tempo.ranks[0].attacks[1]: an attack");
                Fails(ModRules.File, mods.Replace("{ \"damage\": 1.5, \"downbeat\": 2 }", "{ \"damage\": 1.5, \"downbeat\": 2, \"crit\": 1 }"), "mods.json.keenEdge.ranks[0].crit: unknown field");
                Assert.DoesNotThrow(() => GameContent.Load(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void UpgradesAreWrittenIdColonRank()
        {
            Assert.AreEqual(Upgrade.Of(AttackKind.SwordSlash, 3), Upgrade.Parse("swordSlash:3"));
            Assert.AreEqual(Upgrade.Of(AttackKind.Phantom, Upgrade.Evolution), Upgrade.Parse("phantom:4"));
            Assert.AreEqual(Upgrade.Of(ModKind.Encore, 2), Upgrade.Parse("encore:2"));
            Assert.AreEqual("tempo:1", Upgrade.Of(ModKind.Tempo, 1).ToString());
            Assert.Throws<FormatException>(() => Upgrade.Parse("tempo:3"), "mods have two ranks");
            Assert.Throws<FormatException>(() => Upgrade.Parse("swordSlash:0"));
            Assert.Throws<FormatException>(() => Upgrade.Parse("fireball:1"));
            Assert.Throws<FormatException>(() => Upgrade.Parse("swordSlash"));
        }

        [Test]
        public void TheBuildHasThreeAttackSlotsAndFourModSlots()
        {
            var sim = Arena.Sim(null, Arena.Build("swordSlash:1", "orbitBlades:1", "phantom:2", "tempo:1", "keenEdge:1", "encore:1", "lifeblood:2"));
            Assert.AreEqual((3, 4), (sim.Arsenal.Attacks, sim.Arsenal.Mods));
            Assert.Throws<InvalidOperationException>(() => sim.Arsenal.Set(sim, Upgrade.Of(AttackKind.Crescent, 1)));
            Assert.Throws<InvalidOperationException>(() => sim.Arsenal.Set(sim, Upgrade.Of(ModKind.WideArcs, 1)));
            sim.Arsenal.Set(sim, Upgrade.Of(AttackKind.SwordSlash, 2));
            Assert.AreEqual(2, sim.Arsenal.Rank(AttackKind.SwordSlash), "a rank of one owned needs no slot");
            var raised = Drive.Events(sim, SimEventKind.Upgrade).Last();
            Assert.AreEqual((0, (int)AttackKind.SwordSlash, 2, 1.0), (raised.I0, raised.I1, raised.I2, raised.D0));
        }

        [Test]
        public void TheAttacksAllocateNothingPerTick()
        {
            var known = Drive.Known(new FixtureSong().Beats(0, 30, 128, halfOnsets: true).Gap(30, 33).Beats(33, 60, 174).Entries());
            var dummies = Enumerable.Range(0, 300)
                .Select(i => new Dummy((2 + i % 20 * 0.6) * Math.Cos(2.4 * i), (2 + i % 20 * 0.6) * Math.Sin(2.4 * i), 1 + i % 4, 0.4))
                .ToArray();
            foreach (var build in new[]
            {
                Arena.Build("swordSlash:4", "bladeRain:4", "crescent:4", "twinStrike:2", "tempo:2", "keenEdge:2", "shatterburst:2"),
                Arena.Build("swordSlash:3", "orbitBlades:4", "phantom:4", "wideArcs:2", "momentum:2", "afterimage:2", "encore:2"),
            })
            {
                long bytes = Drive.LeastAllocated(() =>
                {
                    var sim = new Simulation(new RunInputs { Loadout = build, Dummies = dummies }.Build(Arena.Content));
                    foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
                    sim.StepTo(5);
                    return () =>
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            sim.StepTo(5 + 0.5 * (i + 1));
                            sim.Press(i % 2 == 0 ? Key.Up : Key.Right, sim.Now - 0.05);
                            if (i % 3 == 0) sim.Press(Key.Space, sim.Now - 0.05);
                            sim.Snapshot();
                        }
                    };
                });
                Assert.AreEqual(0, bytes, $"bytes allocated over 50 s of attacks with {string.Join(", ", build)}");
            }
        }
    }
}
