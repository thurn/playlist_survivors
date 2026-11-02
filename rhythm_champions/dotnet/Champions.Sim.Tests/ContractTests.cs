using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The sim's contract (plan.md, Decision 3 and "The Sim", Shape): no Unity, the content as the design
    /// says, determinism, and no allocation per tick.
    /// </summary>
    public class ContractTests
    {
        static string SimFolder => Checkout.In("Assets", "Champions", "Sim");

        [Test]
        public void TheSimReferencesNoUnityEngineType()
        {
            var references = typeof(Simulation).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.IsFalse(references.Any(n => n.StartsWith("Unity", StringComparison.Ordinal)), string.Join(", ", references));
            var sources = Directory.GetFiles(SimFolder, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(sources.Length, 20);
            foreach (string file in sources)
                StringAssert.DoesNotContain("UnityEngine", File.ReadAllText(file), file);
            // RC1's assembly definition, once it exists, keeps Unity's engine out of the assembly too.
            string asmdef = Path.Combine(SimFolder, "Champions.Sim.asmdef");
            if (!File.Exists(asmdef)) return;
            var json = JsonValue.Parse(File.ReadAllText(asmdef));
            var members = json.Members.ToDictionary(m => m.Key, m => m.Value);
            Assert.AreEqual("Champions.Sim", members["name"].String);
            Assert.IsTrue(members["noEngineReferences"].Bool, "noEngineReferences");
            if (members.TryGetValue("references", out var refs)) Assert.IsEmpty(refs.Items, "references");
        }

        [Test]
        public void TheContentHoldsTheDesignsNumbers()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var beat = content.Beat;
            // Decision 6.
            Assert.AreEqual(0.08, beat.BeatWindowS);
            Assert.AreEqual(0.06, beat.HalfBeatWindowS);
            Assert.AreEqual(0.06, beat.StrongOnsetWindowS);
            // Decision 9.
            Assert.AreEqual(1.0, beat.CadenceTargetS);
            CollectionAssert.AreEqual(new[] { 1, 2, 4 }, beat.CadenceBeats);
            Assert.AreEqual(0.08, beat.CadenceHoldWithin);
            Assert.AreEqual(1.0, beat.CadenceFallbackS);
            // Held for a quiet moment; Every boss.
            Assert.AreEqual(0.75, beat.SilenceS);
            Assert.AreEqual(2.0, beat.BeatlessS);
            Assert.AreEqual(0.5, beat.TelegraphBeatlessBeatS);
            // Decision 4; The Hero; Decision 15.
            Assert.AreEqual(6.0, content.Hero.SpeedMps);
            Assert.AreEqual(100, content.Hero.Health);
            Assert.AreEqual(0.5, content.Hero.HurtInvulnerableS);
            Assert.AreEqual(0.25, content.Hero.LevelUpHeal);
            Assert.AreEqual(1.0, content.Hero.BossKillHeal);
            // Decisions 5 and 6.
            var dashes = content.Dashes;
            Assert.AreEqual(0.6, dashes.CooldownS);
            Assert.AreEqual(0.15, dashes.BufferS);
            Assert.AreEqual(0.1, dashes.IFramesAfterS);
            Assert.AreEqual(2.0, dashes.OnBeatDamage);
            Assert.AreEqual(1.5, dashes.OnBeatArea);
            Assert.AreEqual("dash", dashes.Start);
            // Dashes: each type's move, the same at every rank (the evolutions may change it).
            foreach (var (id, distance, duration) in new[] { ("dash", 4.0, 0.15), ("bladeRush", 5.0, 0.22), ("leapSlam", 5.0, 0.35), ("shieldCharge", 6.0, 0.3), ("blinkStrike", 6.0, 0.05) })
            {
                var kind = dashes.Kinds[dashes.IndexOf(id)];
                Assert.AreEqual(id == "dash" ? 1 : 3, kind.TopRank, id);
                Assert.AreEqual(id != "dash", kind.Evolution != null, id);
                foreach (var form in kind.Ranks)
                {
                    Assert.AreEqual(distance, form.DistanceM, id);
                    Assert.AreEqual(duration, form.DurationS, id);
                }
            }
            Assert.AreEqual(8.0, dashes.Kinds[dashes.IndexOf("shieldCharge")].Evolution.DistanceM, "Juggernaut: 8 m");
            // The plain Dash has no damage (The Dash, "On the beat").
            Assert.IsFalse(dashes.Kinds[dashes.IndexOf("dash")].Ranks[0].Damaging);
            // The defaults without content are the design's rules and the plain Dash.
            var defaults = new DashRules();
            Assert.AreEqual(dashes.CooldownS, defaults.CooldownS);
            Assert.AreEqual(dashes.BufferS, defaults.BufferS);
            Assert.AreEqual(dashes.IFramesAfterS, defaults.IFramesAfterS);
            Assert.AreEqual(dashes.OnBeatDamage, defaults.OnBeatDamage);
            Assert.AreEqual(dashes.OnBeatArea, defaults.OnBeatArea);
            Assert.AreEqual(4.0, defaults.Kinds[defaults.IndexOf("dash")].Ranks[0].DistanceM);
            Assert.AreEqual(content.Hero.HitSettleS, new HeroRules().HitSettleS);
            Assert.AreEqual(content.Hero.RadiusM, new HeroRules().RadiusM);
        }

        [Test]
        public void TheContentIsReadStrictly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string beat = File.ReadAllText(Checkout.In("Content", BeatRules.File));
                string hero = File.ReadAllText(Checkout.In("Content", HeroRules.File));
                string dashes = File.ReadAllText(Checkout.In("Content", DashRules.File));
                File.WriteAllText(Path.Combine(dir, HeroRules.File), hero.Replace("\"speedMps\": 6.0,", "\"speedMps\": 6.0, \"sprint\": 9,"));
                File.WriteAllText(Path.Combine(dir, BeatRules.File), beat);
                File.WriteAllText(Path.Combine(dir, DashRules.File), dashes);
                var e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("hero.json.sprint: unknown field", e.Message);
                File.WriteAllText(Path.Combine(dir, HeroRules.File), "{ }");
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("hero.json.speedMps: missing", e.Message);
                File.WriteAllText(Path.Combine(dir, HeroRules.File), hero);
                File.WriteAllText(Path.Combine(dir, DashRules.File), dashes.Replace("\"radiusM\": 1.5,", "\"radiusM\": 1.5, \"reachM\": 2,"));
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("dashes.json.dashes[1].ranks[0].parts[0].reachM: unknown field", e.Message);
                File.WriteAllText(Path.Combine(dir, DashRules.File), dashes.Replace("\"shape\": \"cone\"", "\"shape\": \"wedge\""));
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("parts[1].shape: one of circle, ring, path, cone, not wedge", e.Message);
                File.WriteAllText(Path.Combine(dir, DashRules.File), dashes.Replace("\"start\": \"dash\"", "\"start\": \"hop\""));
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("dashes.json.start: no dash hop", e.Message);
                File.WriteAllText(Path.Combine(dir, DashRules.File), dashes);
                File.WriteAllText(Path.Combine(dir, BeatRules.File), beat.Replace("[1, 2, 4]", "[2, 1]"));
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("beat.json.cadence.beats", e.Message);
                File.WriteAllText(Path.Combine(dir, BeatRules.File), "{ \"gate\": ");
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.StartsWith("beat.json: JSON:", e.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheSameStreamAndInputsGiveTheSameRun()
        {
            var a = SessionTests.LiveRun().Sim;
            var b = SessionTests.LiveRun().Sim;
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.EventCount, b.EventCount);
        }

        [Test]
        public void SteppingAllocatesNothing()
        {
            var known = Drive.Known(new FixtureSong().Beats(0, 30, 128, halfOnsets: true).Gap(30, 32, silent: true).Beats(32, 60, 90).Entries());
            long bytes = Drive.LeastAllocated(() =>
            {
                var sim = new Simulation(new SimConfig());
                foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
                sim.Press(Key.Right, 0);
                sim.StepTo(5);
                return () =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        sim.StepTo(5 + 0.5 * (i + 1));
                        sim.Press(i % 2 == 0 ? Key.Up : Key.Down, sim.Now - 0.05);
                        sim.Press(Key.Space, sim.Now - 0.03);
                        sim.Health.Hurt(sim, new HeroHit { T = sim.Now, Damage = 0.1, Owner = -1, Beat = double.NaN });
                        sim.Judge(sim.Now - 0.05);
                        sim.Telegraphs.Plan(sim.Now, 2);
                        sim.Snapshot();
                        if (i % 10 != 0) continue;
                        sim.Progression.Treasure(sim);
                        sim.Press(Key.Right, sim.Now - 0.02);
                        sim.Press(Key.Enter, sim.Now - 0.01);
                    }
                };
            });
            Assert.AreEqual(0, bytes, "bytes allocated over 50 s of stepping");
        }
    }
}
