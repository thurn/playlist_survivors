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
            Assert.That(t.YieldToAccents, Is.False);
            Assert.That((t.SoundGainBase, t.SoundGainPerDoubling, t.SoundGainMax, t.HeavyBlowKills), Is.EqualTo((0.35, 0.15, 1.0, 0)));

            SimTuning set = LevelScript.Parse(Script("\"comboMaxSpeed\": 2.5, \"maxShapeScale\": 8, \"hitsShapeScale\": 6, \"maxShapeViewFraction\": 0.4, "
                + "\"hitPointScale\": { \"fodder\": 0, \"elite\": 0.5, \"boss\": 2 }, \"refillMinPerBeat\": 90, \"refillDivisor\": 3, \"releaseLeadS\": 3, \"yieldToAccents\": true,")).Tuning;
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
            Assert.That(set.YieldToAccents, Is.True);
            SimTuning sound = LevelScript.Parse(Script("\"soundGain\": { \"base\": 0.2, \"perDoubling\": 0.1, \"max\": 0.8 }, \"heavyBlowKills\": 40,")).Tuning;
            Assert.That((sound.SoundGainBase, sound.SoundGainPerDoubling, sound.SoundGainMax, sound.HeavyBlowKills), Is.EqualTo((0.2, 0.1, 0.8, 40)));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"comboMaxSpeed\": 0.5,")));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"heavyBlowKills\": -1,")));
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

        static readonly string[] Rhythmic = { "drop-1", "groove-2", "build", "drop-2", "final-peak" };

        /// <summary>
        /// Overdrive loads over every roster set's level script and sets the same horde on each: three times
        /// the default script's envelope, its views and its counts, whatever the set's own real-time cut
        /// (forest and heroic draw 150 at 18 m), so the song's checks on the default script hold for all three.
        /// </summary>
        [TestCase("action-level.json")]
        [TestCase("action-level-forest.json")]
        [TestCase("action-level-heroic.json")]
        public void OverdriveLoadsOverEveryRostersScript(string file)
        {
            LevelScript normal = ChoreographyFiles.LoadLevel(Fixtures.Root, file), od = ChoreographyFiles.LoadLevel(Fixtures.Root, file, "overdrive");
            LevelScript reference = Fixtures.OverdriveLevel;
            Assert.That(od.Intensity, Is.EqualTo("overdrive"));
            Assert.That(od.EnvelopeOnScreen, Is.EqualTo(3 * Fixtures.Level.EnvelopeOnScreen));
            Assert.That((od.EnvelopeOnScreen, od.EnvelopeViewWidthM), Is.EqualTo((reference.EnvelopeOnScreen, reference.EnvelopeViewWidthM)));
            foreach (SongSection sec in Fixtures.Song.Sections)
            {
                LevelSection a = od.For(sec.Label), b = reference.For(sec.Label);
                Assert.That((a.OnScreen, a.ViewWidthM, a.Load), Is.EqualTo((b.OnScreen, b.ViewWidthM, b.Load)), sec.Label);
            }
            foreach (string label in Rhythmic) StringAssert.StartsWith("overdrive", od.For(label).Load, label);
            Assert.That(od.For("intro").Load, Is.EqualTo(normal.For("intro").Load));
        }

        /// <summary>
        /// Overdrive's flurries (an eighth's combo entry) add blows between the beats without costing a
        /// beat its own: each blow between beats is followed by one on the next beat (or by the windup of a
        /// spin or slam the hero held back for, which at 3x may start just after that beat), and the hero strikes on most beats of every rhythmic section
        /// (such windups and strides on the way to a goal are the rest; the peaks hold ground).
        /// </summary>
        [Test]
        public void OverdriveFlurriesAddBlowsBetweenTheBeats()
        {
            Timeline tl = Fixtures.OverdriveTimeline;
            var blows = new List<double>();
            var windups = new List<(double from, double to)>();
            foreach (ActionEvent a in tl.ActionsOf(0))
                if (a.Kind == ActionKind.Attack) { blows.Add(a.Impact); windups.Add((a.ClipStart, a.Impact)); }
            double[] beats = tl.Map.Beats;
            bool OnBeat(double t) { int i = Array.BinarySearch(beats, t); if (i < 0) i = ~i; return (i < beats.Length && Math.Abs(beats[i] - t) < 1e-3) || (i > 0 && Math.Abs(beats[i - 1] - t) < 1e-3); }
            bool Struck(double t) { int i = blows.BinarySearch(t); if (i < 0) i = ~i; return (i < blows.Count && Math.Abs(blows[i] - t) < 1e-3) || (i > 0 && Math.Abs(blows[i - 1] - t) < 1e-3); }
            int between = 0;
            foreach (SongSection sec in tl.Map.Sections)
            {
                if (Array.IndexOf(Rhythmic, sec.Label) < 0) continue;
                int sectionBeats = 0, struck = 0;
                foreach (double b in beats)
                {
                    if (b < sec.Start || b >= sec.End) continue;
                    sectionBeats++;
                    if (Struck(b)) struck++;
                }
                TestContext.WriteLine(sec.Label + ": blows on " + struck + " of " + sectionBeats + " beats");
                // The peaks hold ground; the sections before travel to their goals, striding where nothing is in reach ahead.
                bool peak = sec.Label == "drop-2" || sec.Label == "final-peak";
                Assert.That(struck, Is.GreaterThanOrEqualTo((peak ? 0.85 : 0.6) * sectionBeats), sec.Label);
                foreach (double t in blows)
                {
                    if (t < sec.Start || t >= sec.End || OnBeat(t)) continue;
                    between++;
                    int next = Array.BinarySearch(beats, t);
                    next = next < 0 ? ~next : next + 1;
                    if (next < beats.Length && beats[next] < sec.End)
                        Assert.That(Struck(beats[next]) || windups.Exists(w => w.from < beats[next] + 0.1 && beats[next] < w.to), Is.True, "the flurry blow at " + t + " cost the next beat its blow");
                }
            }
            Assert.That(between, Is.GreaterThan(50), "no flurries between the beats");
        }

        /// <summary>
        /// Overdrive's sound (soundGain, heavyBlowKills): every blow that kills heavyBlowKills or more also booms with the
        /// ring's heavy impact on its impact, a beat's death sounds grow louder with its kills up to the cap,
        /// and no aggregated sound goes over it, so a mass kill is one big layered hit with headroom left.
        /// </summary>
        [Test]
        public void OverdriveMassKillsLandAsOneHeavyLayeredHit()
        {
            Timeline tl = Fixtures.OverdriveTimeline;
            SimTuning tuning = Fixtures.OverdriveLevel.Tuning;
            int ring = Array.IndexOf(Catalog.SoundCategories, "ring");
            var deaths = new Dictionary<double, int>();
            foreach (ActionEvent a in tl.Actions)
                if (a.Kind == ActionKind.Death) deaths[a.Impact] = deaths.TryGetValue(a.Impact, out int n) ? n + 1 : 1;
            var booms = new HashSet<double>();
            float quiet = float.MaxValue, loud = 0;
            foreach (SoundEvent s in tl.Sounds)
            {
                Assert.That(s.Gain, Is.LessThanOrEqualTo(tuning.SoundGainMax + 1e-3), "a sound over the cap at " + s.Time);
                int cat = s.Sound / Catalog.Variants;
                if (cat == ring) booms.Add(s.Time);
                if (cat != Catalog.DeathSound || !deaths.TryGetValue(s.Time, out int k)) continue;
                if (k <= 4) quiet = Math.Min(quiet, s.Gain);
                if (k >= 60) loud = Math.Max(loud, s.Gain);
            }
            // A beat's deaths can include a few the blow didn't make (a spell's on the same impact), so a
            // blow near the threshold may be just under it.
            int heavy = 0, boomed = 0;
            foreach (ActionEvent a in tl.ActionsOf(0))
            {
                if (a.Kind != ActionKind.Attack || !deaths.TryGetValue(a.Impact, out int k) || k < tuning.HeavyBlowKills) continue;
                heavy++;
                if (booms.Contains(a.Impact)) boomed++;
            }
            Assert.That(heavy, Is.GreaterThanOrEqualTo(10));
            Assert.That(boomed, Is.GreaterThanOrEqualTo(0.9 * heavy), boomed + " of " + heavy + " heavy blows boom");
            Assert.That(loud, Is.GreaterThan(quiet + 0.2f), "a 60-kill beat's death sound is barely louder than a 4-kill one's");
        }

        /// <summary>With yieldToAccents the hero holds back a blow to leave room for a phrase's slam; without it, the slam never fits between back-to-back blows.</summary>
        [Test]
        public void OverdriveSlamsLandOnTheirPhrases()
        {
            Timeline tl = Fixtures.OverdriveTimeline;
            int slam = Fixtures.Data.Clips.Require("hero slam"), slams = 0;
            foreach (ActionEvent a in tl.ActionsOf(0))
                if (a.Kind == ActionKind.Attack && a.Clip == slam && Array.IndexOf(Rhythmic, tl.Sections[tl.Map.SectionIndexAt(a.Impact)].Label) >= 0) slams++;
            Assert.That(slams, Is.GreaterThanOrEqualTo(3));
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
