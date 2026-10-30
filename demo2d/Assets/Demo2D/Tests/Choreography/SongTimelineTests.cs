using System;
using System.Collections.Generic;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>
    /// The song's 2D timeline (PX4): the core's timeline checks, its build time and determinism,
    /// the level script's on-screen counts, the top-down view, the roster's mix and the casters.
    /// </summary>
    public class SongTimelineTests
    {
        static Timeline Song => Fixtures.SongTimeline;

        static void Pass(List<string> problems) => Assert.That(problems, Is.Empty, TimelineChecks.Report(problems));

        [Test]
        public void EntityIdsAreStableAndEverythingIsOrdered() => Pass(TimelineChecks.EntitiesAndOrder(Song));

        [Test]
        public void ImpactsLieOnTheGrid()
        {
            Pass(TimelineChecks.ImpactsOnGrid(Song, out int n));
            Assert.That(n, Is.GreaterThan(1000));
        }

        [Test]
        public void GaitStepsStartOnBeats()
        {
            Pass(TimelineChecks.GaitStepsOnBeats(Song, out int steps));
            Assert.That(steps, Is.GreaterThan(1000));
        }

        [Test]
        public void MonstersMoveSmoothlyInFreeSections()
        {
            Pass(TimelineChecks.FreeSectionsMoveSmoothly(Song, out int moved));
            Assert.That(moved, Is.GreaterThan(0));
        }

        [Test]
        public void SectionDeathRatesFollowTheirPresets() => Pass(TimelineChecks.SectionDeathRates(Song, Fixtures.Level, TestContext.WriteLine));

        [Test]
        public void NoTwoMonstersOverlapByMoreThanAQuarterRadius()
        {
            Pass(TimelineChecks.Overlaps(Song, out double worst, out int samples));
            TestContext.WriteLine(samples + " samples, worst overlap " + worst.ToString("F3") + " of the smaller radius");
        }

        [Test]
        public void EveryHitAndDeathHasItsClipAndEffect()
        {
            Pass(TimelineChecks.HitsAndDeathsHaveClipAndEffect(Song, out int n));
            Assert.That(n, Is.GreaterThan(1000));
        }

        [Test]
        public void SpawnsAppearOutsideTheView()
        {
            Pass(TimelineChecks.SpawnsOutsideTheView(Song, out int spawns));
            Assert.That(spawns, Is.GreaterThan(1000));
        }

        [Test]
        public void SoundsAreAggregatedToThreeVoicesPerBeat() => Pass(TimelineChecks.SoundsAggregated(Song, out _));

        [Test]
        public void EveryAttackCarriesItsFittedClipTiming()
        {
            // The hero's combos land their impact frames after its 190 ms windup: the slashes at about 2.5x.
            Pass(TimelineChecks.AttacksCarryTheirFit(Song, Fixtures.Data.Clips, out int n, heroMaxSpeed: 4f));
            Assert.That(n, Is.GreaterThan(100));
        }

        [Test]
        public void FramingEasesTheZoom() => Pass(TimelineChecks.FramingEasesTheZoom(Song, Fixtures.Level.EnvelopeViewWidthM));

        /// <summary>The level script's held sections average their on-screen targets, within demo3d's tolerance.</summary>
        [Test]
        public void SectionsHoldTheirOnScreenTargets()
        {
            Pass(TimelineChecks.SectionsHoldTheirTargets(Song, TimelineChecks.SectionOnScreenTolerance, out int n, TestContext.WriteLine));
            Assert.That(n, Is.GreaterThanOrEqualTo(5));
        }

        /// <summary>About 400 on screen in the final peak, with the camera eased out 1.35x from the base 30 m view.</summary>
        [Test]
        public void TheClimaxHoldsAboutFourHundredEasedOut()
        {
            SectionStats peak = Array.Find(Song.Sections, s => s.Label == "final-peak");
            TestContext.WriteLine("final peak: " + peak.MeanOnScreen.ToString("F0") + " on screen, max " + peak.MaxOnScreen + ", at " + peak.ViewWidthM + " m");
            Assert.That(peak.MeanOnScreen, Is.InRange(360, 440));
            Assert.That(peak.ViewWidthM, Is.EqualTo(30 * 1.35f).Within(1e-3f));
            foreach (SectionStats s in Song.Sections) Assert.That(s.ViewWidthM, Is.InRange(30f, peak.ViewWidthM), s.Label);
        }

        /// <summary>The sim counts and spawns around the 2D camera's view: a 16:9 rectangle of ground.</summary>
        [Test]
        public void TheViewIsTheTopDownRectangle()
        {
            Assert.That(Song.ViewPitchDeg, Is.EqualTo(ViewGeometry.TopDownPitchDeg));
            ViewGeometry v = Song.View(30);
            Assert.That(v.NearHalf, Is.EqualTo(15f).Within(1e-3f));
            Assert.That(v.FarHalf, Is.EqualTo(15f).Within(1e-3f));
            Assert.That(v.Near, Is.EqualTo(30 / 16f * 9 / 2).Within(1e-3f));
            Assert.That(v.Far, Is.EqualTo(v.Near).Within(1e-3f));
        }

        /// <summary>Every roster type takes part: the waves' grunts, the casters, the elites and the boss.</summary>
        [Test]
        public void EveryRosterTypeAppears()
        {
            var spawned = new int[Song.TypeNames.Length];
            foreach (EntityInfo e in Song.Entities) if (e.Kind == EntityKind.Monster) spawned[e.Type]++;
            for (int t = 0; t < spawned.Length; t++)
            {
                TestContext.WriteLine(Song.TypeNames[t] + ": " + spawned[t]);
                Assert.That(spawned[t], Is.GreaterThan(0), Song.TypeNames[t]);
            }
            Assert.That(spawned[Fixtures.Data.Roster.MonsterIndex("boss-devil")], Is.EqualTo(1));
        }

        /// <summary>
        /// A caster's bolt (the mages', and the boss's hellfire since PX8) leaves it on its attack's
        /// beat and lands on an eighth note, where the hero parries it.
        /// </summary>
        [Test]
        public void CastersBoltsLandOnEighthsAndAreParried()
        {
            Roster roster = Fixtures.Data.Roster;
            var casterBolts = new HashSet<int>();
            foreach (MonsterType mt in roster.Monsters) if (mt.Projectile >= 0) casterBolts.Add(mt.Projectile);
            Assert.That(casterBolts.Count, Is.EqualTo(3));
            var parries = new List<double>();
            foreach (ActionEvent a in Song.ActionsOf(0)) if (a.Kind == ActionKind.Parry) parries.Add(a.Impact);
            var attacks = new List<double>();
            foreach (ActionEvent a in Song.Actions)
                if (a.Kind == ActionKind.Attack && a.Entity != 0 && roster.Monsters[Song.Entities[a.Entity].Type].Projectile >= 0) attacks.Add(a.Impact);
            var problems = new List<string>();
            int bolts = 0;
            foreach (EntityInfo e in Song.Entities)
            {
                if (e.Kind != EntityKind.Projectile || !casterBolts.Contains(e.Type)) continue;
                bolts++;
                if (!attacks.Exists(t => Math.Abs(t - e.Spawn) < 1e-9)) problems.Add("bolt " + e.Id + " leaves at " + e.Spawn + " with no caster's attack");
                GridPoint p = Song.Grid.Points[Song.Grid.Nearest(e.Despawn)];
                if (Math.Abs(p.Time - e.Despawn) > 1e-6 || !p.Has(GridFlags.Eighth)) problems.Add("bolt " + e.Id + " lands at " + e.Despawn + ", off the eighths");
                if (!parries.Exists(t => Math.Abs(t - e.Despawn) < 1e-9)) problems.Add("bolt " + e.Id + " lands at " + e.Despawn + " unparried");
                if (problems.Count > 20) break;
            }
            TestContext.WriteLine(bolts + " caster bolts");
            Assert.That(bolts, Is.GreaterThan(50));
            Pass(problems);
        }

        /// <summary>The same song map, level script and seed give a byte-identical dump.</summary>
        [Test]
        public void SongDumpIsByteIdenticalAcrossBuilds()
        {
            ulong a = TimelineJson.Hash(Song, out long charsA);
            ulong b = TimelineJson.Hash(Fixtures.BuildSong(), out long charsB);
            TestContext.WriteLine("song dump: " + charsA + " characters, FNV-1a " + a.ToString("x16"));
            Assert.That(charsB, Is.EqualTo(charsA));
            Assert.That(b, Is.EqualTo(a));
        }

        /// <summary>
        /// The song's dump is byte-identical to its dump before the level script's tuning fields
        /// (ps-5pd.1), whose defaults are the sim's old constants. The hash was recorded under .NET on
        /// Apple silicon (Mono and other platforms round differently); a change that alters the song on
        /// purpose records the new one here.
        /// </summary>
        [Test]
        public void SongDumpIsByteIdenticalToBeforeTheTuningFields()
        {
            if (Fixtures.OnMono || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.Arm64
                || !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                Assert.Ignore("the hash was recorded under .NET on Apple silicon");
            ulong h = TimelineJson.Hash(Song, out long chars);
            Assert.That(chars + " " + h.ToString("x16"), Is.EqualTo("19407912 43ef4283bee188b3"));
        }

        /// <summary>
        /// The song's timeline builds in under 3 s (the best of two builds, so the JIT's first
        /// compile is not counted twice). Unity's editor runs Mono, which only gets a looser
        /// sanity bound; the 3 s budget is measured under .NET.
        /// </summary>
        [Test]
        public void SongTimelineBuildsInUnderThreeSeconds()
        {
            double first = Song.BuildSeconds, second = Fixtures.BuildSong().BuildSeconds;
            TestContext.WriteLine("song build: first " + first.ToString("F3") + " s, second " + second.ToString("F3") + " s" + (Fixtures.OnMono ? " (Mono)" : ""));
            TestContext.WriteLine(TimelineReport.Summary(Song));
            Assert.That(Math.Min(first, second), Is.LessThan(Fixtures.OnMono ? 20.0 : 3.0));
        }
    }
}
