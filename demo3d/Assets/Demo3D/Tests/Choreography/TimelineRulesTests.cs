using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// The choreography layer's checks that apply on open ground (windows.md, "Automated Checks"),
    /// run on the song's timeline and on every stress script's (S1 to S5). The wall, courtyard and
    /// flow-field checks are TD6's.
    /// </summary>
    public class TimelineRulesTests
    {
        const double Ms = 0.001;

        static IEnumerable<string> Timelines() => Fixtures.AllTimelines();

        static string Report(List<string> problems) =>
            problems.Count + " problems, first: " + string.Join("; ", problems.GetRange(0, Math.Min(5, problems.Count)));

        [TestCaseSource(nameof(Timelines))]
        public void EntityIdsAreStableAndSegmentsOrdered(string name)
        {
            Timeline tl = Fixtures.Named(name);
            var problems = new List<string>();
            for (int e = 0; e < tl.EntityCount; e++)
            {
                if (tl.Entities[e].Id != e) problems.Add("entity " + e + " has id " + tl.Entities[e].Id);
                if (tl.Entities[e].Despawn < tl.Entities[e].Spawn) problems.Add("entity " + e + " despawns before it spawns");
                ArraySegment<MotionSegment> segs = tl.SegmentsOf(e);
                for (int i = 0; i < segs.Count; i++)
                {
                    MotionSegment s = segs.Array[segs.Offset + i];
                    if (s.Entity != e) problems.Add("segment of " + s.Entity + " filed under " + e);
                    if (s.T1 < s.T0) problems.Add("entity " + e + " segment ends before it starts at " + s.T0);
                    if (i > 0 && s.T0 < segs.Array[segs.Offset + i - 1].T0)
                        problems.Add("entity " + e + " segments out of order at " + s.T0);
                }
                ArraySegment<ActionEvent> acts = tl.ActionsOf(e);
                for (int i = 1; i < acts.Count; i++)
                    if (acts.Array[acts.Offset + i].Impact < acts.Array[acts.Offset + i - 1].Impact) problems.Add("entity " + e + " actions out of order");
                if (problems.Count > 20) break;
            }
            for (int i = 1; i < tl.Effects.Length; i++) if (tl.Effects[i].Time < tl.Effects[i - 1].Time) { problems.Add("effects out of order"); break; }
            for (int i = 1; i < tl.Sounds.Length; i++) if (tl.Sounds[i].Time < tl.Sounds[i - 1].Time) { problems.Add("sounds out of order"); break; }
            Assert.That(problems, Is.Empty, Report(problems));
            Assert.That(tl.Framing.Length, Is.EqualTo((int)Math.Ceiling(tl.Duration * Timeline.FramingRate) + 1));
        }

        /// <summary>Every Attack, Hit, Tick and Death impact lies within 1 ms of the grid (the accents in free sections).</summary>
        [TestCaseSource(nameof(Timelines))]
        public void ImpactsLieOnTheGrid(string name)
        {
            Timeline tl = Fixtures.Named(name);
            var problems = new List<string>();
            int checkedCount = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Attack && a.Kind != ActionKind.Hit && a.Kind != ActionKind.Tick && a.Kind != ActionKind.Death) continue;
                checkedCount++;
                GridPoint p = tl.Grid.Points[tl.Grid.Nearest(a.Impact)];
                bool free = tl.Map.Sections[tl.Map.SectionIndexAt(a.Impact)].Feel == SectionFeel.Free;
                if (Math.Abs(p.Time - a.Impact) > Ms) problems.Add(a.Kind + " of " + a.Entity + " at " + a.Impact + " is " + (a.Impact - p.Time) * 1000 + " ms off the grid");
                else if (free && !p.Has(GridFlags.Free)) problems.Add(a.Kind + " at " + a.Impact + " in a free section is not on an accent");
            }
            if (name != "S1") Assert.That(checkedCount, Is.GreaterThan(0));   // S1 has no actions
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Every gait step (a Pulse segment) starts on a beat, outside free sections.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void GaitStepsStartOnBeats(string name)
        {
            Timeline tl = Fixtures.Named(name);
            double[] beats = tl.Map.Beats;
            var problems = new List<string>();
            int steps = 0;
            foreach (MotionSegment s in tl.Segments)
            {
                if (s.Ease != Ease.Pulse) continue;
                if (tl.Map.Sections[tl.Map.SectionIndexAt(s.T0)].Feel == SectionFeel.Free) continue;
                steps++;
                int i = Array.BinarySearch(beats, s.T0);
                if (i < 0) i = ~i;
                double best = double.MaxValue;
                if (i < beats.Length) best = Math.Abs(beats[i] - s.T0);
                if (i > 0) best = Math.Min(best, Math.Abs(beats[i - 1] - s.T0));
                if (best > Ms) problems.Add("entity " + s.Entity + " steps at " + s.T0 + ", " + best * 1000 + " ms off a beat");
            }
            Assert.That(steps, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// In free sections monsters move in Linear segments, never in Pulse gait steps.
        /// </summary>
        [Test]
        public void MonstersMoveSmoothlyInFreeSections()
        {
            Timeline tl = Fixtures.SongTimeline;
            int linear = 0;
            foreach (MotionSegment s in tl.Segments)
            {
                if (tl.Entities[s.Entity].Kind != EntityKind.Monster) continue;
                if (tl.Map.Sections[tl.Map.SectionIndexAt(s.T0)].Feel != SectionFeel.Free) continue;
                Assert.That(s.Ease, Is.EqualTo(Ease.Linear), "monster " + s.Entity + " at " + s.T0);
                if (s.To != s.From) linear++;
            }
            Assert.That(linear, Is.GreaterThan(0), "no monster moved in a free section");
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
            Timeline tl = Fixtures.SongTimeline;
            LevelScript level = Fixtures.Level;
            var problems = new List<string>();
            foreach (SongSection sec in tl.Map.Sections)
            {
                LevelSection ls = level.For(sec.Label);
                int n = (int)Math.Round(ls.OnScreen * level.EnvelopeOnScreen);
                double target = LoadPreset.Get(ls.Load).DeathsPerSecond(n);
                int deaths = CountDeaths(tl, sec.Start, sec.End);
                double rate = deaths / (sec.End - sec.Start);
                TestContext.WriteLine(sec.Label + ": " + rate.ToString("F2") + " deaths/s against " + target.ToString("F2"));
                if (target <= 0 || ls.Clear) continue;   // a clearing section kills what is on screen
                if (Math.Abs(rate / target - 1) > 0.10) problems.Add(sec.Label + " " + rate.ToString("F2") + " deaths/s against " + target.ToString("F2"));
            }
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>A stress timeline's deaths per second after its 5 s warm-up stay within 10% of its preset at its N.</summary>
        [TestCaseSource(nameof(StressScenes))]
        public void StressDeathRatesFollowTheirPresets(string scene)
        {
            Timeline tl = Fixtures.Stress(scene);
            StressScript s = Fixtures.StressScript(scene);
            double target = LoadPreset.Get(s.Load).DeathsPerSecond(s.N);
            double rate = CountDeaths(tl, 5, tl.Duration) / (tl.Duration - 5);
            if (target <= 0) Assert.That(rate, Is.EqualTo(0));
            else Assert.That(Math.Abs(rate / target - 1), Is.LessThanOrEqualTo(0.10), rate + " deaths/s against " + target);
        }

        static IEnumerable<string> StressScenes() => Fixtures.StressScenes;

        static int CountDeaths(Timeline tl, double from, double to)
        {
            int n = 0;
            foreach (ActionEvent a in tl.Actions) if (a.Kind == ActionKind.Death && a.Impact >= from && a.Impact < to) n++;
            return n;
        }

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
                    sum += OnScreen(tl, t);
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

        static int OnScreen(Timeline tl, double t)
        {
            FramingSample f = tl.FramingAt(t);
            var view = new ViewGeometry(f.ViewWidth);
            int n = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn) continue;
                if (view.Contains(f.Focus, tl.PositionAt(e, t))) n++;
            }
            return n;
        }

        /// <summary>
        /// No two living monsters overlap by more than a quarter of the smaller one's radius,
        /// sampled on every beat and 0.1 s and 0.2 s after it, while gait steps are in motion.
        /// Dying monsters are falling and are left out.
        /// </summary>
        [TestCaseSource(nameof(Timelines))]
        public void NoTwoMonstersOverlapByMoreThanAQuarterRadius(string name)
        {
            Timeline tl = Fixtures.Named(name);
            var deathAt = new double[tl.EntityCount];
            for (int e = 0; e < tl.EntityCount; e++)
            {
                deathAt[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e))
                    if (a.Kind == ActionKind.Death) { deathAt[e] = a.Impact; break; }
            }
            var problems = new List<string>();
            var ids = new List<int>();
            var xs = new List<float>();
            var ps = new List<Vector2>();
            float maxR = 0;
            foreach (float r in tl.TypeRadii) maxR = Math.Max(maxR, r);
            double worst = 0;
            int samples = 0;
            foreach (double beat in tl.Map.Beats)
            {
                foreach (double off in new[] { 0.0, 0.1, 0.2 })
                {
                    double t = beat + off;
                    samples++;
                    ids.Clear(); ps.Clear(); xs.Clear();
                    var all = new List<Vector2>();
                    var order = new List<int>();
                    for (int e = 1; e < tl.EntityCount; e++)
                    {
                        EntityInfo info = tl.Entities[e];
                        if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn || t >= deathAt[e]) continue;
                        order.Add(order.Count);
                        ids.Add(e);
                        all.Add(tl.PositionAt(e, t));
                    }
                    order.Sort((a, b) => all[a].X != all[b].X ? all[a].X.CompareTo(all[b].X) : a.CompareTo(b));
                    var sortedIds = new List<int>(order.Count);
                    foreach (int k in order) { sortedIds.Add(ids[k]); ps.Add(all[k]); }
                    ids.Clear();
                    ids.AddRange(sortedIds);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        float ri = tl.TypeRadii[tl.Entities[ids[i]].Type];
                        for (int j = i + 1; j < ids.Count && ps[j].X - ps[i].X <= ri + maxR; j++)
                        {
                            float rj = tl.TypeRadii[tl.Entities[ids[j]].Type];
                            float overlap = ri + rj - Vector2.Distance(ps[i], ps[j]);
                            float allowed = 0.25f * Math.Min(ri, rj);
                            worst = Math.Max(worst, overlap / Math.Min(ri, rj));
                            if (overlap > allowed + 1e-4f && problems.Count < 50)
                                problems.Add(ids[i] + " and " + ids[j] + " overlap " + (overlap / Math.Min(ri, rj)).ToString("F2") + " radii at " + t.ToString("F3"));
                        }
                    }
                }
            }
            TestContext.WriteLine(name + ": " + samples + " samples, worst overlap " + worst.ToString("F3") + " of the smaller radius");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Every hit that is not a tick has its hit clip and hit effect; every death its death clip and death effect.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void EveryHitAndDeathHasItsClipAndEffect(string name)
        {
            Timeline tl = Fixtures.Named(name);
            int hitSpark = Array.IndexOf(tl.EffectNames, "hit-spark"), deathBurst = Array.IndexOf(tl.EffectNames, "death-burst");
            var problems = new List<string>();
            int hits = 0, deaths = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death) continue;
                bool hit = a.Kind == ActionKind.Hit;
                if (hit) hits++; else deaths++;
                EntityInfo info = tl.Entities[a.Entity];
                string want = tl.TypeNames[info.Type] + (hit ? " hit" : " death");
                if (a.Clip < 0 || tl.ClipNames[a.Clip] != want) problems.Add(a.Kind + " of " + a.Entity + " at " + a.Impact + " has clip " + a.Clip + ", wants '" + want + "'");
                int effect = hit ? hitSpark : deathBurst;
                bool found = false;
                for (int i = tl.FirstEffectAt(a.Impact - 1e-9); i < tl.Effects.Length && tl.Effects[i].Time <= a.Impact + 1e-9; i++)
                    if (tl.Effects[i].Effect == effect && tl.Effects[i].AttachTo == a.Entity) { found = true; break; }
                if (!found) problems.Add(a.Kind + " of " + a.Entity + " at " + a.Impact + " has no " + tl.EffectNames[effect] + " effect");
                if (problems.Count > 20) break;
            }
            if (name != "S1") Assert.That(hits + deaths, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// Monsters spawned after the opening appear outside the view at that section's view width:
        /// at least 1 m beyond its edge (the choreographer spawns 2 m out; shake is at most 0.25 m
        /// and punch only narrows the view).
        /// </summary>
        [TestCaseSource(nameof(Timelines))]
        public void SpawnsAppearOutsideTheView(string name)
        {
            Timeline tl = Fixtures.Named(name);
            var problems = new List<string>();
            int spawns = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster || info.Spawn <= 0) continue;   // the opening's horde is placed at t = 0
                spawns++;
                FramingSample f = tl.FramingAt(info.Spawn);
                float width = Math.Max(f.ViewWidth, tl.Sections[tl.Map.SectionIndexAt(info.Spawn)].ViewWidthM);
                Vector2 p = tl.PositionAt(e, info.Spawn);
                float outside = new ViewGeometry(width).OutsideDistance(f.Focus, p);
                if (outside < 1f) problems.Add("monster " + e + " spawns " + outside.ToString("F2") + " m outside the view at " + info.Spawn);
                if (problems.Count > 20) break;
            }
            if (name != "S1") Assert.That(spawns, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Per beat and sound category, at most 3 voices play.</summary>
        [TestCaseSource(nameof(Timelines))]
        public void SoundsAreAggregatedToThreeVoicesPerBeat(string name)
        {
            Timeline tl = Fixtures.Named(name);
            var counts = new Dictionary<long, int>();
            foreach (SoundEvent s in tl.Sounds)
            {
                long key = ((long)Math.Floor(tl.Map.BeatIndexAt(s.Time) + 1e-6) << 8) | (long)(s.Sound / Catalog.Variants);
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
                Assert.That(s.Gain, Is.InRange(0f, 1f));
            }
            int max = 0;
            foreach (KeyValuePair<long, int> kv in counts) max = Math.Max(max, kv.Value);
            Assert.That(max, Is.LessThanOrEqualTo(3));
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
            float envelope = Fixtures.Level.EnvelopeViewWidthM;
            foreach (FramingSample f in tl.Framing) Assert.That(f.ViewWidth, Is.InRange(1f, envelope + 1e-3f));
            // Two bars into each section the width has settled on the section's.
            foreach (SongSection sec in tl.Map.Sections)
            {
                double t = tl.Map.TimeOfBeatIndex(sec.FirstBeat + 8) + 0.02;
                if (t >= sec.End) continue;
                Assert.That(tl.FramingAt(t).ViewWidth, Is.EqualTo(tl.Sections[tl.Map.SectionIndexOf(sec.Label)].ViewWidthM).Within(1e-3f), sec.Label);
            }
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
