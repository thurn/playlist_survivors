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
            // Decision 4.
            Assert.AreEqual(6.0, content.Hero.SpeedMps);
        }

        [Test]
        public void TheContentIsReadStrictly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string beat = File.ReadAllText(Checkout.In("Content", BeatRules.File));
                File.WriteAllText(Path.Combine(dir, HeroRules.File), "{ \"speedMps\": 6, \"sprint\": 9 }");
                File.WriteAllText(Path.Combine(dir, BeatRules.File), beat);
                var e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("hero.json.sprint: unknown field", e.Message);
                File.WriteAllText(Path.Combine(dir, HeroRules.File), "{ }");
                e = Assert.Throws<ContentException>(() => GameContent.Load(dir));
                StringAssert.Contains("hero.json.speedMps: missing", e.Message);
                File.WriteAllText(Path.Combine(dir, HeroRules.File), "{ \"speedMps\": 6 }");
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
                        sim.Judge(sim.Now - 0.05);
                        sim.Telegraphs.Plan(sim.Now, 2);
                        sim.Snapshot();
                    }
                };
            });
            Assert.AreEqual(0, bytes, "bytes allocated over 50 s of stepping");
        }
    }
}
