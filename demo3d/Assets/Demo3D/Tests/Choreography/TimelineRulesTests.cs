using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// The choreography layer's checks that apply on open ground (windows.md, "Automated Checks"),
    /// run on the song's timeline and on every stress script's (S1 to S5). The checks themselves are
    /// the package's <see cref="TimelineChecks"/>, which demo2d runs too. The wall, courtyard and
    /// flow-field checks are TD6's.
    /// </summary>
    public class TimelineRulesTests
    {
        static IEnumerable<string> Timelines() => Fixtures.AllTimelines();

        static string Report(List<string> problems) => TimelineChecks.Report(problems);

        [TestCaseSource(nameof(Timelines))]
        public void EntityIdsAreStableAndSegmentsOrdered(string name)
        {
            List<string> problems = TimelineChecks.EntitiesAndOrder(Fixtures.Named(name));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Every Attack, Hit, Tick and Death impact lies within 1 ms of the grid (the accents in free sections).</summary>
        [TestCaseSource(nameof(Timelines))]
        public void ImpactsLieOnTheGrid(string name)
        {
            List<string> problems = TimelineChecks.ImpactsOnGrid(Fixtures.Named(name), out int checkedCount);
            if (name != "S1") Assert.That(checkedCount, Is.GreaterThan(0));   // S1 has no actions
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Every gait step (a Pulse segment) starts on a beat, outside free sections.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void GaitStepsStartOnBeats(string name)
        {
            List<string> problems = TimelineChecks.GaitStepsOnBeats(Fixtures.Named(name), out int steps);
            Assert.That(steps, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// In free sections monsters move in Linear segments, never in Pulse gait steps.
        /// </summary>
        [Test]
        public void MonstersMoveSmoothlyInFreeSections()
        {
            List<string> problems = TimelineChecks.FreeSectionsMoveSmoothly(Fixtures.SongTimeline, out int moved);
            Assert.That(problems, Is.Empty, Report(problems));
            Assert.That(moved, Is.GreaterThan(0), "no monster moved in a free section");
        }

        /// <summary>
        /// Every hero stride (a hero Pulse segment; attacks move by root motion, in Linear segments)
        /// heads toward the section's goal until the hero first stands in it, and stays in it
        /// afterwards. With a clear line to the goal the stride heads straight for it; otherwise it
        /// follows the goal's flow field around the walls (TD6): within 60° of the field's way downhill,
        /// and never uphill by more than a cell. Checked on the song and on a sparse song (an envelope
        /// of 100): at the envelope the hero rarely strides, since a monster is nearly always in its way
        /// and it cuts its way by its blows (TD13).
        /// </summary>
        [Test]
        public void HeroStridesHeadTowardTheGoalUntilReached()
        {
            LevelScript sparse = ChoreographyFiles.LoadLevel(Fixtures.Root);
            sparse.EnvelopeOnScreen = 100;
            var problems = new List<string>();
            StrideProblems(Fixtures.SongTimeline, problems);   // at the envelope: strides are rare, but each must head for the goal
            int toward = StrideProblems(Choreographer.Run(Fixtures.Song, sparse, Fixtures.Data), problems);
            Assert.That(toward, Is.GreaterThan(0), "the sparse song never strode toward a goal");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Adds the timeline's stride problems; returns its strides toward a goal.</summary>
        static int StrideProblems(Timeline tl, List<string> problems)
        {
            GroundLayout layout = Fixtures.Data.Layout;
            NavGrid nav = layout.Nav;
            float heroR = Fixtures.Data.Roster.HeroRadius;
            ArraySegment<MotionSegment> hero = tl.SegmentsOf(0);
            int toward = 0, around = 0, after = 0;
            for (int si = 0; si < tl.Sections.Length; si++)
            {
                SectionStats sec = tl.Sections[si];
                if (sec.Goal == null) continue;
                GoalArea goal = layout.Goal(sec.Goal);
                var field = new FlowField(nav, heroR + 0.05f);
                field.BeginArea(goal.Center, Math.Min(goal.Radius, 1f));
                field.BuildAll();
                // When the hero first stands in the goal during this section, from its segments.
                double reached = double.PositiveInfinity;
                if (Vector2.Distance(tl.PositionAt(0, sec.Start), goal.Center) <= goal.Radius + 1e-3f) reached = sec.Start;
                for (int i = 0; i < hero.Count && double.IsPositiveInfinity(reached); i++)
                {
                    MotionSegment s = hero.Array[hero.Offset + i];
                    if (s.T1 < sec.Start || s.T0 >= sec.End) continue;
                    for (int k = 0; k <= 8; k++)
                    {
                        double t = Math.Max(sec.Start, s.T0 + (s.T1 - s.T0) * k / 8);
                        if (t < sec.End && Vector2.Distance(s.PositionAt(t), goal.Center) <= goal.Radius + 1e-3f) { reached = t; break; }
                    }
                }
                for (int i = 0; i < hero.Count; i++)
                {
                    MotionSegment s = hero.Array[hero.Offset + i];
                    if (s.Ease != Ease.Pulse || s.T0 < sec.Start || s.T0 >= sec.End) continue;
                    Vector2 step = s.To - s.From;
                    if (step.Length() < 1e-4f) continue;
                    if (s.T0 < reached)
                    {
                        toward++;
                        if (nav.Clear(s.From, goal.Center, heroR))
                        {
                            float cos = Vector2.Dot(Vector2.Normalize(step), Vector2.Normalize(goal.Center - s.From));
                            if (cos < 0.999f) problems.Add("stride at " + s.T0 + " in " + sec.Label + " heads " + Math.Acos(Math.Min(1, cos)) * 57.3 + "° off a clear line to the goal");
                        }
                        else
                        {
                            around++;
                            bool ok = field.Direction(s.From, out Vector2 down);
                            float cos = ok ? Vector2.Dot(Vector2.Normalize(step), down) : -1;
                            if (cos < 0.5f) problems.Add("stride at " + s.T0 + " in " + sec.Label + " heads " + Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 57.3 + "° off the goal field");
                            if (field.CostAt(s.To) > field.CostAt(s.From) + 0.5f) problems.Add("stride at " + s.T0 + " in " + sec.Label + " climbs the goal field");
                        }
                    }
                    else
                    {
                        after++;
                        if (Vector2.Distance(s.To, goal.Center) > goal.Radius + 1e-3f) problems.Add("stride at " + s.T0 + " in " + sec.Label + " leaves the reached goal");
                    }
                }
            }
            TestContext.WriteLine(tl.Name + ": strides toward a goal: " + toward + " (" + around + " around walls by the goal field), within a reached goal: " + after);
            return toward;
        }

        /// <summary>Each section's measured deaths per second stay within 10% of its load preset.</summary>
        [Test]
        public void SongSectionDeathRatesFollowTheirPresets()
        {
            List<string> problems = TimelineChecks.SectionDeathRates(Fixtures.SongTimeline, Fixtures.Level, TestContext.WriteLine);
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>The song's held sections average their on-screen targets, within the tolerance the shared check takes from this song.</summary>
        [Test]
        public void SongSectionsHoldTheirOnScreenTargets()
        {
            List<string> problems = TimelineChecks.SectionsHoldTheirTargets(Fixtures.SongTimeline, TimelineChecks.SectionOnScreenTolerance, out int n, TestContext.WriteLine);
            Assert.That(n, Is.GreaterThanOrEqualTo(5));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>A stress timeline's deaths per second after its 5 s warm-up stay within 10% of its preset at its N.</summary>
        [TestCaseSource(nameof(StressScenes))]
        public void StressDeathRatesFollowTheirPresets(string scene)
        {
            Timeline tl = Fixtures.Stress(scene);
            StressScript s = Fixtures.StressScript(scene);
            double target = LoadPreset.Get(s.Load).DeathsPerSecond(s.N);
            double rate = TimelineChecks.CountDeaths(tl, 5, tl.Duration) / (tl.Duration - 5);
            if (target <= 0) Assert.That(rate, Is.EqualTo(0));
            else Assert.That(Math.Abs(rate / target - 1), Is.LessThanOrEqualTo(0.10), rate + " deaths/s against " + target);
        }

        static IEnumerable<string> StressScenes() => Fixtures.StressScenes;

        /// <summary>
        /// A stress timeline holds N on screen (alive and dying): after the warm-up, each bar's
        /// average stays within 5% of N (choreography.md, "Stress timelines"). S5's worst beats kill
        /// 30% of the screen every 4 bars by design (refilled over the next bar or two), so there only
        /// the run's mean must stay within 20% (15% on TD3's open ground; in the arena's opening the
        /// reserve walks in through edge bands at the view's far corners). S6's walls ration the screen
        /// by design (the courtyard's inside is the hero's kill zone, fed through four gaps), so there no
        /// bar may overfill and the run's mean must reach 75% of N.
        /// </summary>
        [TestCaseSource(nameof(StressScenes))]
        public void StressTimelinesHoldNOnScreen(string scene)
        {

            Timeline tl = Fixtures.Stress(scene);
            StressScript s = Fixtures.StressScript(scene);
            double bar = 4 * 60.0 / s.Bpm;
            var problems = new List<string>();
            var ranges = new List<string>();
            double total = 0;
            int bars = 0;
            for (double b0 = Math.Ceiling(5 / bar) * bar; b0 + bar <= tl.Duration + 1e-9; b0 += bar)
            {
                double sum = 0;
                int samples = 0;
                for (double t = b0; t < b0 + bar - 1e-9; t += 4 / Timeline.FramingRate)
                {
                    sum += TimelineChecks.OnScreen(tl, t);
                    samples++;
                }
                double mean = sum / samples;
                ranges.Add(mean.ToString("F0"));
                total += mean;
                bars++;
                bool walled = scene == "S6";   // its walls ration the screen: no bar overfills, and the run's mean is checked below
                if (!s.WorstBeats && !walled && Math.Abs(mean / s.N - 1) > 0.05) problems.Add("bar at " + b0.ToString("F1") + " s averages " + mean.ToString("F0"));
                if (walled && mean > 1.05 * s.N) problems.Add("bar at " + b0.ToString("F1") + " s averages " + mean.ToString("F0"));
            }
            TestContext.WriteLine(scene + " bar means: " + string.Join(" ", ranges));
            if (s.WorstBeats && Math.Abs(total / bars / s.N - 1) > 0.20) problems.Add("the run averages " + (total / bars).ToString("F0"));
            if (scene == "S6" && total / bars < 0.75 * s.N) problems.Add("the run averages " + (total / bars).ToString("F0"));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// No two living monsters overlap by more than a quarter of the smaller one's radius,
        /// sampled on every beat and 0.1 s and 0.2 s after it, while gait steps are in motion.
        /// </summary>
        [TestCaseSource(nameof(Timelines))]
        public void NoTwoMonstersOverlapByMoreThanAQuarterRadius(string name)
        {
            List<string> problems = TimelineChecks.Overlaps(Fixtures.Named(name), out double worst, out int samples);
            TestContext.WriteLine(name + ": " + samples + " samples, worst overlap " + worst.ToString("F3") + " of the smaller radius");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Every hit that is not a tick has its hit clip and hit effect; every death its death clip and death effect.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void EveryHitAndDeathHasItsClipAndEffect(string name)
        {
            List<string> problems = TimelineChecks.HitsAndDeathsHaveClipAndEffect(Fixtures.Named(name), out int count);
            if (name != "S1") Assert.That(count, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Monsters spawned after the opening appear at least 1 m outside the view at that section's view width.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void SpawnsAppearOutsideTheView(string name)
        {
            List<string> problems = TimelineChecks.SpawnsOutsideTheView(Fixtures.Named(name), out int spawns);
            if (name != "S1") Assert.That(spawns, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Per beat and sound category, at most 3 voices play.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void SoundsAreAggregatedToThreeVoicesPerBeat(string name)
        {
            List<string> problems = TimelineChecks.SoundsAggregated(Fixtures.Named(name), out _);
            Assert.That(problems, Is.Empty, Report(problems));
        }

        [TestCaseSource(nameof(Timelines))]
        public void TickTimingIsReported(string name)
        {
            Timeline tl = Fixtures.Named(name);
            SimTickTiming.Summary s = tl.TickTiming.Summarize();
            Assert.That(tl.TickTiming.TickMs.Length, Is.EqualTo(tl.Framing.Length));
            Assert.That(s.Ticks, Is.EqualTo(tl.TickTiming.TickMs.Length - 300));
            Assert.That(s.P99Ms, Is.GreaterThan(0));
            Assert.That(s.MaxMs, Is.GreaterThanOrEqualTo(s.P99Ms));
            Assert.That(s.P99Ms, Is.GreaterThanOrEqualTo(s.P50Ms));
            string json = tl.TickTiming.ToJson();
            Assert.That(json.Contains("\"simTickP99Ms\":"), Is.True, json);
            // Every timeline now runs on the arena: the field toward the hero is built at load and rebuilt
            // as the hero moves, time-sliced across a beat's ticks (TD6).
            Assert.That(json.Contains("\"flowRebuildSlices\":"), Is.True, json);
            Assert.That(json.Contains("\"flowRebuilds\":"), Is.True, json);
            Assert.That(tl.TickTiming.FlowRebuilds, Is.GreaterThanOrEqualTo(1), json);
            Assert.That(tl.TickTiming.FlowRebuildSlices, Is.GreaterThanOrEqualTo(tl.TickTiming.FlowRebuilds), json);
            if (name == "song") Assert.That(tl.TickTiming.FlowRebuilds, Is.GreaterThan(10), "the hero travels, so its field is rebuilt");
            TestContext.WriteLine(name + ": " + json);
        }

        [Test]
        public void FramingFollowsTheHeroAndEasesTheZoom()
        {
            Timeline tl = Fixtures.SongTimeline;
            List<string> zoom = TimelineChecks.FramingEasesTheZoom(tl, Fixtures.Level.EnvelopeViewWidthM);
            Assert.That(zoom, Is.Empty, Report(zoom));
            // The focus trails the hero by a spring, never by more than a few metres. A slam's root
            // motion (about 7 m in under a second) on the way to a goal outruns the 0.4 s critically
            // damped spring, whose steady lag behind a speed v is 2 x 0.4 s x v (about 5.8 m at 7.3 m/s):
            // such a move may stretch the lag past 3 m briefly (TD12's 30 m envelope put one slam
            // en route, 4.7 m at 173.4 s), never past 6 m, and only for a few samples.
            int samples = 0, over = 0;
            for (int i = 0; i < tl.Framing.Length; i += 30)
            {
                double t = i / Timeline.FramingRate;
                float lag = Vector2.Distance(tl.Framing[i].Focus, tl.PositionAt(0, t));
                Assert.That(lag, Is.LessThan(6f), "at " + t);
                samples++;
                if (lag >= 3f) over++;
            }
            Assert.That(over, Is.LessThanOrEqualTo(Math.Max(1, samples / 100)), "samples trailing by 3 m or more");
        }
    }
}
