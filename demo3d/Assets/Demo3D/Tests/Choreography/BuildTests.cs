using System;
using NUnit.Framework;

namespace Demo3D.Choreography.Tests
{
    /// <summary>Determinism of the dump, and the song's build time.</summary>
    public class BuildTests
    {
        /// <summary>The same song map, level script and seed give a byte-identical dump (on one runtime and platform).</summary>
        [Test]
        public void SongDumpIsByteIdenticalAcrossBuilds()
        {
            ulong a = TimelineJson.Hash(Fixtures.SongTimeline, out long charsA);
            ulong b = TimelineJson.Hash(Fixtures.BuildSong(), out long charsB);
            TestContext.WriteLine("song dump: " + charsA + " characters, FNV-1a " + a.ToString("x16"));
            Assert.That(charsB, Is.EqualTo(charsA));
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void StressDumpIsByteIdenticalAcrossBuilds()
        {
            StressScript s = Fixtures.StressScript("S5").With(n: 600, seconds: 20);
            string a = TimelineJson.ToJson(Choreographer.RunStress(s, Fixtures.Data));
            string b = TimelineJson.ToJson(Choreographer.RunStress(s, Fixtures.Data));
            Assert.That(b.Length, Is.EqualTo(a.Length));
            Assert.That(b == a, Is.True, "the dumps differ");
        }

        [Test]
        public void ADifferentSeedGivesADifferentTimeline()
        {
            StressScript s = Fixtures.StressScript("S3").With(n: 400, seconds: 10);
            ulong a = TimelineJson.Hash(Choreographer.RunStress(s, Fixtures.Data, 1), out _);
            ulong b = TimelineJson.Hash(Choreographer.RunStress(s, Fixtures.Data, 2), out _);
            Assert.That(b, Is.Not.EqualTo(a));
        }

        /// <summary>
        /// The song's timeline builds in under 3 s (the best of two builds, so the JIT's first
        /// compile is not counted twice). Unity's editor runs Mono, which only gets a looser
        /// sanity bound; the 3 s budget is measured under .NET.
        /// </summary>
        [Test]
        public void SongTimelineBuildsInUnderThreeSeconds()
        {
            double first = Fixtures.SongTimeline.BuildSeconds;
            double second = Fixtures.BuildSong().BuildSeconds;
            double best = Math.Min(first, second);
            TestContext.WriteLine("song build: first " + first.ToString("F3") + " s, second " + second.ToString("F3") + " s" + (Fixtures.OnMono ? " (Mono)" : ""));
            Assert.That(best, Is.LessThan(Fixtures.OnMono ? 20.0 : 3.0));
        }

        [Test]
        public void DumpHasTheContractsFields()
        {
            Timeline tl = Choreographer.RunStress(Fixtures.StressScript("S3").With(n: 200, seconds: 6), Fixtures.Data);
            string json = TimelineJson.ToJson(tl);
            JsonValue root = JsonValue.Parse(json);
            Assert.That(root["format"].AsInt(), Is.EqualTo(LayerInfo.TimelineFormatVersion));
            Assert.That(root["segments"].Count, Is.EqualTo(tl.Segments.Length));
            Assert.That(root["actions"].Count, Is.EqualTo(tl.Actions.Length));
            Assert.That(root["framing"].Count, Is.EqualTo(tl.Framing.Length));
            JsonValue seg = root["segments"][0];
            foreach (string key in new[] { "entity", "t0", "t1", "from", "to", "facing", "ease" }) Assert.That(seg.Has(key), Is.True, key);
            JsonValue act = root["actions"][0];
            foreach (string key in new[] { "entity", "kind", "impact", "clip", "target", "strength" }) Assert.That(act.Has(key), Is.True, key);
            // A window keeps only what overlaps it.
            JsonValue window = JsonValue.Parse(TimelineJson.ToJson(tl, 2, 3));
            for (int i = 0; i < window["actions"].Count; i++) Assert.That(window["actions"][i]["impact"].AsDouble(), Is.InRange(2.0, 3.0));
        }
    }
}
