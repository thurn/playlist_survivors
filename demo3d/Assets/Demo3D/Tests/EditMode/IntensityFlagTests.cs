using System;
using System.IO;
using Demo3D.Runtime.Playback;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>`--intensity` (choreography.md, Intensity overlays): the flag, its failures, and each intensity's overlay file.</summary>
    public class IntensityFlagTests
    {
        [Test]
        public void TheFlagNamesAnIntensityAndDefaultsToNormal()
        {
            Assert.That(Intensity.FromArgs(new[] { "player" }).IsNormal);
            Assert.That(Intensity.FromArgs(new[] { "player", "--intensity", "overdrive", "--palette", "neon" }).Name, Is.EqualTo("overdrive"));
            Assert.That(Intensity.FromArgs(new[] { "--intensity", "normal" }).OverlayName, Is.Null);
            Assert.That(Intensity.Parse("overdrive").OverlayName, Is.EqualTo("intensity-overdrive"));
        }

        [Test]
        public void UnknownOrMissingIntensitiesFailListingTheKnownOnes()
        {
            var e = Assert.Throws<ArgumentException>(() => Intensity.FromArgs(new[] { "--intensity", "eleven" }));
            StringAssert.Contains("normal, overdrive", e.Message);
            Assert.Throws<ArgumentException>(() => Intensity.FromArgs(new[] { "--intensity", "Overdrive" }));
            Assert.Throws<ArgumentException>(() => Intensity.FromArgs(new[] { "--intensity" }));
            Assert.Throws<ArgumentException>(() => Intensity.FromArgs(new[] { "--intensity", "--fixed-time" }));
        }

        /// <summary>Every intensity but normal has its overlay beside the level scripts, and no overlay lacks a name.</summary>
        [Test]
        public void EachIntensityHasItsOverlay()
        {
            const string data = "Assets/Demo3D/Choreography/Data/";
            foreach (var name in Intensity.Names)
                if (name != "normal") Assert.That(File.Exists(data + Intensity.Parse(name).OverlayName + ".json"), name);
            foreach (var f in Directory.GetFiles(data, "intensity-*.json"))
                Assert.That(Intensity.Names, Does.Contain(Path.GetFileNameWithoutExtension(f).Substring("intensity-".Length)), f);
        }
    }
}
