using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// `--intensity` (choreography.md, Intensity overlays): the level script's tuning fields default to
    /// the sim's old constants, so normal runs are unchanged; overlays merge over any roster's script;
    /// and the song under overdrive passes the song's checks (the generic ones run on it as
    /// "song-overdrive" in <see cref="TimelineRulesTests"/>).
    /// </summary>
    public class IntensityTests
    {
        static string Report(List<string> problems) => TimelineChecks.Report(problems);

        /// <summary>
        /// The dumps' FNV-1a hashes before the tuning fields existed (ps-5pd.1), under .NET on Apple
        /// silicon, where the dump is byte-identical run to run. A change that alters normal runs on
        /// purpose records the new hashes here.
        /// </summary>
        static readonly (string name, long chars, string hash)[] Before =
        {
            ("song", 19118387, "8b9387cc84d560ef"),
            ("S1", 22781381, "0efef3392079eb9e"),
            ("S2", 24718997, "fd46c88ae8289c77"),
            ("S3", 26045232, "84e21b99f520e8bb"),
            ("S4", 26045232, "bfaf50ba9209e6c6"),
            ("S5", 26814572, "dac8ba4d62ebbc1a"),
            ("S6", 25086816, "fccdae1c515fffd7"),
        };

        /// <summary>
        /// Without an intensity, the song's and the stress scenes' timelines are byte-identical to
        /// theirs before the tuning fields. (Unity's Mono, or another platform, rounds differently, so
        /// the hashes only hold where they were recorded.)
        /// </summary>
        [Test]
        public void NormalTimelinesAreByteIdenticalToBeforeTheFields()
        {
            if (Fixtures.OnMono || RuntimeInformation.ProcessArchitecture != Architecture.Arm64 || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Assert.Pass("not checked: the hashes were recorded under .NET on Apple silicon");
            foreach (var (name, chars, hash) in Before)
            {
                ulong h = TimelineJson.Hash(Fixtures.Named(name), out long n);
                Assert.That(n + " " + h.ToString("x16"), Is.EqualTo(chars + " " + hash), name + "'s dump changed");
            }
        }

        /// <summary>A script without the tuning fields gets the sim's old constants, and each field parses.</summary>
        [Test]
        public void TuningFieldsDefaultToTheOldConstants()
        {
            SimTuning t = LevelScript.Parse(Script("")).Tuning;
            Assert.That(t.ComboMaxSpeed, Is.EqualTo(1.33f));
            Assert.That(t.MaxShapeScale, Is.EqualTo(4f));
            Assert.That(t.HitsShapeScale, Is.EqualTo(2.5f));
            Assert.That(t.MaxShapeViewFraction, Is.EqualTo(0.25f));
            Assert.That(new[] { t.FodderHitPointScale, t.EliteHitPointScale, t.BossHitPointScale }, Is.All.EqualTo(1f));
            Assert.That(t.RefillMinPerBeat, Is.EqualTo(24));
            Assert.That(t.RefillDivisor, Is.EqualTo(6f));
            Assert.That(t.ReleaseLeadS, Is.EqualTo(2.5f));

            SimTuning set = LevelScript.Parse(Script("\"comboMaxSpeed\": 2.5, \"maxShapeScale\": 8, \"hitsShapeScale\": 6, \"maxShapeViewFraction\": 0.4, "
                + "\"hitPointScale\": { \"fodder\": 0, \"elite\": 0.5, \"boss\": 2 }, \"refillMinPerBeat\": 90, \"refillDivisor\": 3, \"releaseLeadS\": 3,")).Tuning;
            Assert.That(set.ComboMaxSpeed, Is.EqualTo(2.5f));
            Assert.That(set.MaxShapeScale, Is.EqualTo(8f));
            Assert.That(set.HitsShapeScale, Is.EqualTo(6f));
            Assert.That(set.MaxShapeViewFraction, Is.EqualTo(0.4f));
            Assert.That(set.HitPointScale(MonsterRank.Fodder), Is.EqualTo(0f));
            Assert.That(set.HitPointScale(MonsterRank.Elite), Is.EqualTo(0.5f));
            Assert.That(set.HitPointScale(MonsterRank.Boss), Is.EqualTo(2f));
            Assert.That(set.RefillMinPerBeat, Is.EqualTo(90));
            Assert.That(set.RefillDivisor, Is.EqualTo(3f));
            Assert.That(set.ReleaseLeadS, Is.EqualTo(3f));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"comboMaxSpeed\": 0.5,")));
        }

        /// <summary>A script's own load presets sit beside the built-in ones; an unknown one fails.</summary>
        [Test]
        public void ScriptsDefineTheirOwnLoadPresets()
        {
            LevelScript ls = LevelScript.Parse(Script("\"loads\": { \"fierce\": { \"deathsPerN\": 0.2, \"hitsPerDeath\": 0.5, \"spells\": [\"ring@phrase\"] } },", "fierce"));
            LoadPreset p = ls.Preset("fierce");
            Assert.That((p.DeathsPerN, p.HitsPerDeath, p.Spells.Length), Is.EqualTo((0.2f, 0.5f, 1)));
            Assert.That(ls.Preset("heavy"), Is.SameAs(LoadPreset.Heavy));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script("", "fierce")));
        }

        /// <summary>
        /// The overlay's multipliers scale the base's numbers, its other fields merge over the script's
        /// (objects by member), and its sections override by label after its "*" entry.
        /// </summary>
        [Test]
        public void OverlaysMergeOverTheScript()
        {
            string overlay = "{ \"name\": \"loud\", \"multiply\": { \"envelope.onScreen\": 3 }, \"envelope\": { \"viewWidthM\": 36 }, \"comboMaxSpeed\": 2,"
                + " \"sections\": { \"*\": { \"multiply\": { \"onScreen\": 2 } }, \"drop\": { \"load\": \"heavy\", \"multiply\": { \"onScreen\": 1.5 }, \"spells\": [\"ring@phrase\"] } } }";
            LevelScript ls = LevelScript.Parse(Script(""), overlay);
            Assert.That(ls.Intensity, Is.EqualTo("loud"));
            Assert.That(ls.EnvelopeOnScreen, Is.EqualTo(300));
            Assert.That(ls.EnvelopeViewWidthM, Is.EqualTo(36f));
            Assert.That(ls.Tuning.ComboMaxSpeed, Is.EqualTo(2f));
            LevelSection quiet = ls.For("quiet"), drop = ls.For("drop");
            Assert.That((quiet.OnScreen, quiet.Load, quiet.Spells.Length), Is.EqualTo((0.2f, "light", 0)));
            Assert.That((drop.OnScreen, drop.Load, drop.Spells[0].ToString()), Is.EqualTo((0.5f * 2 * 1.5f, "heavy", "ring@phrase")));
            Assert.That(drop.Combo.Length, Is.EqualTo(2), "a field the overlay leaves alone keeps the script's");

            // Within one level the multipliers apply first, so a value both scaled and set takes the set one.
            Assert.That(LevelScript.Parse(Script(""), "{ \"multiply\": { \"envelope.viewWidthM\": 2 }, \"envelope\": { \"viewWidthM\": 25 } }").EnvelopeViewWidthM, Is.EqualTo(25f));
        }

        [Test]
        public void OverlaysFailOnWhatTheScriptLacks()
        {
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script(""), "{ \"sections\": { \"drop-9\": { \"load\": \"heavy\" } } }"));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script(""), "{ \"multiply\": { \"releaseLeadS\": 2 } }"));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script(""), "{ \"multiply\": { \"envelope.onScreen\": \"x\" } }"));
        }

        /// <summary>An unknown intensity fails, listing the known ones, as `--roster` does; normal applies none.</summary>
        [Test]
        public void IntensitiesAreNamedByTheirFiles()
        {
            Assert.That(ChoreographyFiles.Intensities(Fixtures.Root), Is.EqualTo(new[] { "normal", "overdrive" }));
            var e = Assert.Throws<ArgumentException>(() => ChoreographyFiles.LoadLevel(Fixtures.Root, ChoreographyFiles.LevelFile, "eleven"));
            StringAssert.Contains("normal, overdrive", e.Message);
            Assert.That(ChoreographyFiles.LoadLevel(Fixtures.Root, ChoreographyFiles.LevelFile, "normal").Intensity, Is.Null);
        }

        /// <summary>Overdrive loads over every roster set's level script, tripling its envelope and taking over its rhythmic sections.</summary>
        [TestCase("action-level.json")]
        [TestCase("action-level-forest.json")]
        [TestCase("action-level-heroic.json")]
        public void OverdriveLoadsOverEveryRostersScript(string file)
        {
            LevelScript normal = ChoreographyFiles.LoadLevel(Fixtures.Root, file), od = ChoreographyFiles.LoadLevel(Fixtures.Root, file, "overdrive");
            Assert.That(od.Intensity, Is.EqualTo("overdrive"));
            Assert.That(od.EnvelopeOnScreen, Is.EqualTo(3 * normal.EnvelopeOnScreen));
            foreach (string label in new[] { "drop-1", "groove-2", "build", "drop-2", "final-peak" })
                Assert.That(od.For(label).Load, Is.EqualTo("overdrive"), label);
            Assert.That(od.For("intro").Load, Is.EqualTo(normal.For("intro").Load));
        }

        [Test]
        public void OverdriveSectionDeathRatesFollowTheirPresets()
        {
            List<string> problems = TimelineChecks.SectionDeathRates(Fixtures.OverdriveTimeline, Fixtures.OverdriveLevel, TestContext.WriteLine);
            Assert.That(problems, Is.Empty, Report(problems));
        }

        [Test]
        public void OverdriveSectionsHoldTheirOnScreenTargets()
        {
            List<string> problems = TimelineChecks.SectionsHoldTheirTargets(Fixtures.OverdriveTimeline, TimelineChecks.SectionOnScreenTolerance, out int n, TestContext.WriteLine);
            Assert.That(n, Is.GreaterThanOrEqualTo(5));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        [Test]
        public void OverdriveAttacksCarryTheirFitWithinItsSpeedCap()
        {
            List<string> problems = TimelineChecks.AttacksCarryTheirFit(Fixtures.OverdriveTimeline, Fixtures.Data.Clips, out int n, Fixtures.OverdriveLevel.Tuning.ComboMaxSpeed);
            Assert.That(problems, Is.Empty, Report(problems));
            Assert.That(n, Is.GreaterThan(50));
        }

        [Test]
        public void OverdriveFramingEasesTheZoom()
        {
            List<string> problems = TimelineChecks.FramingEasesTheZoom(Fixtures.OverdriveTimeline, Fixtures.OverdriveLevel.EnvelopeViewWidthM);
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>A two-section script ("quiet" and "drop") with <paramref name="fields"/> at its top level.</summary>
        static string Script(string fields, string dropLoad = "demo") =>
            "{ \"song\": \"action\", \"layout\": \"arena\", " + fields + " \"envelope\": { \"onScreen\": 100, \"viewWidthM\": 30 }, \"sections\": ["
            + " { \"section\": \"quiet\", \"onScreen\": 0.1, \"load\": \"light\", \"combo\": [\"slashL\"], \"spells\": [] },"
            + " { \"section\": \"drop\", \"onScreen\": 0.5, \"load\": \"" + dropLoad + "\", \"combo\": [\"slashL\", \"slashR\"], \"spells\": [\"ring@downbeat\", \"bolt@beat\"] } ] }";
    }
}
