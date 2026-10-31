using System;
using System.IO;
using Demo3D.Runtime.Playback;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>`--structure-breaks` (choreography.md, Structure breaks): the flag, its failures, and each schedule's overlay file.</summary>
    public class BreakScheduleFlagTests
    {
        [Test]
        public void TheFlagNamesAScheduleAndDefaultsToNone()
        {
            Assert.That(BreakSchedule.FromArgs(new[] { "player" }).IsNone);
            Assert.That(BreakSchedule.FromArgs(new[] { "player" }).OverlayName, Is.Null);
            Assert.That(BreakSchedule.FromArgs(new[] { "player", "--structure-breaks", "walls", "--intensity", "overdrive" }).Name, Is.EqualTo("walls"));
            Assert.That(BreakSchedule.Parse("walls").OverlayName, Is.EqualTo("structure-breaks-walls"));
        }

        [Test]
        public void UnknownOrMissingSchedulesFailListingTheKnownOnes()
        {
            var e = Assert.Throws<ArgumentException>(() => BreakSchedule.FromArgs(new[] { "--structure-breaks", "everything" }));
            StringAssert.Contains("(one of walls)", e.Message);
            Assert.Throws<ArgumentException>(() => BreakSchedule.FromArgs(new[] { "--structure-breaks", "Walls" }));
            Assert.Throws<ArgumentException>(() => BreakSchedule.FromArgs(new[] { "--structure-breaks" }));
            Assert.Throws<ArgumentException>(() => BreakSchedule.FromArgs(new[] { "--structure-breaks", "--fixed-time" }));
        }

        /// <summary>Every schedule has its overlay beside the level scripts, and no overlay lacks a name.</summary>
        [Test]
        public void EachScheduleHasItsOverlay()
        {
            const string data = "Assets/Demo3D/Choreography/Data/";
            foreach (var name in BreakSchedule.Names) Assert.That(File.Exists(data + BreakSchedule.Parse(name).OverlayName + ".json"), name);
            foreach (var f in Directory.GetFiles(data, "structure-breaks-*.json"))
                Assert.That(BreakSchedule.Names, Does.Contain(Path.GetFileNameWithoutExtension(f).Substring("structure-breaks-".Length)), f);
        }
    }
}
