using System;
using System.Collections.Generic;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>
    /// The timeline contract's checks on any demo's timeline (windows.md, "Automated Checks"):
    /// each returns its problems (empty when the timeline passes) and how much it checked, so a
    /// demo's tests can assert both. Checks that depend on a demo's layout stay in its tests.
    /// </summary>
    public static class TimelineChecks
    {
        const double Ms = 0.001;

        public static string Report(List<string> problems) =>
            problems.Count + " problems, first: " + string.Join("; ", problems.GetRange(0, Math.Min(5, problems.Count)));

        /// <summary>Entity ids are dense, segments and actions are ordered per entity, effects and sounds by time.</summary>
        public static List<string> EntitiesAndOrder(Timeline tl)
        {
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
            int framing = (int)Math.Ceiling(tl.Duration * Timeline.FramingRate) + 1;
            if (tl.Framing.Length != framing) problems.Add(tl.Framing.Length + " framing samples, wants " + framing);
            return problems;
        }

        /// <summary>Every Attack, Hit, Tick and Death impact lies within 1 ms of the grid (the accents in free sections).</summary>
        public static List<string> ImpactsOnGrid(Timeline tl, out int checkedCount)
        {
            var problems = new List<string>();
            checkedCount = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Attack && a.Kind != ActionKind.Hit && a.Kind != ActionKind.Tick && a.Kind != ActionKind.Death) continue;
                checkedCount++;
                GridPoint p = tl.Grid.Points[tl.Grid.Nearest(a.Impact)];
                bool free = tl.Map.Sections[tl.Map.SectionIndexAt(a.Impact)].Feel == SectionFeel.Free;
                if (Math.Abs(p.Time - a.Impact) > Ms) problems.Add(a.Kind + " of " + a.Entity + " at " + a.Impact + " is " + (a.Impact - p.Time) * 1000 + " ms off the grid");
                else if (free && !p.Has(GridFlags.Free)) problems.Add(a.Kind + " at " + a.Impact + " in a free section is not on an accent");
            }
            return problems;
        }

        /// <summary>Every gait step (a Pulse segment) starts on a beat, outside free sections.</summary>
        public static List<string> GaitStepsOnBeats(Timeline tl, out int steps)
        {
            double[] beats = tl.Map.Beats;
            var problems = new List<string>();
            steps = 0;
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
            return problems;
        }

        /// <summary>In free sections monsters move in Linear segments, never in Pulse gait steps; <paramref name="moved"/> counts the moves.</summary>
        public static List<string> FreeSectionsMoveSmoothly(Timeline tl, out int moved)
        {
            var problems = new List<string>();
            moved = 0;
            foreach (MotionSegment s in tl.Segments)
            {
                if (tl.Entities[s.Entity].Kind != EntityKind.Monster) continue;
                if (tl.Map.Sections[tl.Map.SectionIndexAt(s.T0)].Feel != SectionFeel.Free) continue;
                if (s.Ease != Ease.Linear) problems.Add("monster " + s.Entity + " steps with " + s.Ease + " at " + s.T0);
                else if (s.To != s.From) moved++;
            }
            return problems;
        }

        /// <summary>
        /// Each song section's measured deaths per second stay within 10% of its load preset at its
        /// target (a clearing section kills what is on screen, so it is left out). With one-hit kills
        /// the hero reaps whatever walks in, so only the sections that hold or grow the horde (as in
        /// <see cref="SectionsHoldTheirTargets"/>) are held to their rate, from 90% of it to
        /// <see cref="OneHitDeathRateCeiling"/> times it: the opening and the free breaks fill with the
        /// next section's horde, and a section after a bigger one kills what is left of it as it walks in.
        /// </summary>
        public static List<string> SectionDeathRates(Timeline tl, LevelScript level, Action<string> log = null)
        {
            var problems = new List<string>();
            int prevN = 0;
            for (int i = 0; i < tl.Map.Sections.Length; i++)
            {
                SongSection sec = tl.Map.Sections[i];
                LevelSection ls = level.For(sec.Label);
                int n = (int)Math.Round(ls.OnScreen * level.EnvelopeOnScreen);
                double target = level.Preset(ls.Load).DeathsPerSecond(n);
                double rate = CountDeaths(tl, sec.Start, sec.End) / (sec.End - sec.Start);
                bool held = i > 0 && sec.Feel != SectionFeel.Free && n >= prevN;
                prevN = n;
                log?.Invoke(sec.Label + ": " + rate.ToString("F2") + " deaths/s against " + target.ToString("F2"));
                if (target <= 0 || ls.Clear) continue;
                bool ok = !level.Tuning.OneHitKills ? Math.Abs(rate / target - 1) <= 0.10
                    : !held || (rate >= 0.9 * target && rate <= OneHitDeathRateCeiling * target);
                if (!ok) problems.Add(sec.Label + " " + rate.ToString("F2") + " deaths/s against " + target.ToString("F2"));
            }
            return problems;
        }

        /// <summary>
        /// With one-hit kills, how far above its preset's rate a section that holds or grows the horde may
        /// kill: demo3d's groove, whose walk-in outpaces its quiet rate, reaches about 1.3 on some seeds.
        /// </summary>
        public const double OneHitDeathRateCeiling = 1.5;

        /// <summary>
        /// How far a song section's mean on screen may stray from its target: demo3d's song holds
        /// within this (its shortest held section, build, averages 77% of its target).
        /// </summary>
        public const double SectionOnScreenTolerance = 0.25;

        /// <summary>
        /// The song's sections that hold or grow the horde average within <paramref name="tolerance"/>
        /// of their on-screen targets: pulsed sections after the opening whose target is at least the
        /// previous section's. The opening's horde walks in, free breaks overfill as the reserve walks
        /// in smoothly, a section after a bigger one drains at its load's death rate, and a clearing
        /// section kills what is on screen, so those are left out.
        /// </summary>
        public static List<string> SectionsHoldTheirTargets(Timeline tl, double tolerance, out int checkedCount, Action<string> log = null)
        {
            var problems = new List<string>();
            checkedCount = 0;
            for (int i = 1; i < tl.Sections.Length; i++)
            {
                SectionStats s = tl.Sections[i];
                double ratio = s.TargetOnScreen > 0 ? s.MeanOnScreen / s.TargetOnScreen : 0;
                log?.Invoke(s.Label + ": " + s.MeanOnScreen.ToString("F0") + " on screen against " + s.TargetOnScreen + " (" + ratio.ToString("F2") + ")");
                if (s.Feel == SectionFeel.Free || s.TargetOnScreen <= 0 || s.TargetOnScreen < tl.Sections[i - 1].TargetOnScreen) continue;
                checkedCount++;
                if (Math.Abs(ratio - 1) > tolerance) problems.Add(s.Label + " averages " + s.MeanOnScreen.ToString("F0") + " on screen against " + s.TargetOnScreen);
            }
            return problems;
        }

        public static int CountDeaths(Timeline tl, double from, double to)
        {
            int n = 0;
            foreach (ActionEvent a in tl.Actions) if (a.Kind == ActionKind.Death && a.Impact >= from && a.Impact < to) n++;
            return n;
        }

        /// <summary>Monsters on screen (alive and dying) at <paramref name="t"/>, in the view the sim counted with.</summary>
        public static int OnScreen(Timeline tl, double t)
        {
            FramingSample f = tl.FramingAt(t);
            ViewGeometry view = tl.View(f.ViewWidth);
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
        public static List<string> Overlaps(Timeline tl, out double worst, out int samples)
        {
            var deathAt = new double[tl.EntityCount];
            for (int e = 0; e < tl.EntityCount; e++)
            {
                deathAt[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e))
                    if (a.Kind == ActionKind.Death) { deathAt[e] = a.Impact; break; }
            }
            var problems = new List<string>();
            var ids = new List<int>();
            var ps = new List<Vector2>();
            float maxR = 0;
            foreach (float r in tl.TypeRadii) maxR = Math.Max(maxR, r);
            worst = 0;
            samples = 0;
            foreach (double beat in tl.Map.Beats)
            {
                foreach (double off in new[] { 0.0, 0.1, 0.2 })
                {
                    double t = beat + off;
                    samples++;
                    ids.Clear(); ps.Clear();
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
            return problems;
        }

        /// <summary>Every hit that is not a tick has its hit clip and hit effect; every death its death clip and death effect.</summary>
        public static List<string> HitsAndDeathsHaveClipAndEffect(Timeline tl, out int count)
        {
            int hitSpark = Array.IndexOf(tl.EffectNames, "hit-spark"), deathBurst = Array.IndexOf(tl.EffectNames, "death-burst");
            var problems = new List<string>();
            count = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death) continue;
                bool hit = a.Kind == ActionKind.Hit;
                count++;
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
            return problems;
        }

        /// <summary>
        /// Monsters spawned after the opening appear outside the view at that section's view width:
        /// at least 1 m beyond its edge (the choreographer spawns 2 m out; shake is at most 0.25 m
        /// and punch only narrows the view).
        /// </summary>
        public static List<string> SpawnsOutsideTheView(Timeline tl, out int spawns)
        {
            var problems = new List<string>();
            spawns = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster || info.Spawn <= 0) continue;   // the opening's horde is placed at t = 0
                spawns++;
                FramingSample f = tl.FramingAt(info.Spawn);
                float width = Math.Max(f.ViewWidth, tl.Sections[tl.Map.SectionIndexAt(info.Spawn)].ViewWidthM);
                Vector2 p = tl.PositionAt(e, info.Spawn);
                float outside = tl.View(width).OutsideDistance(f.Focus, p);
                if (outside < 1f) problems.Add("monster " + e + " spawns " + outside.ToString("F2") + " m outside the view at " + info.Spawn);
                if (problems.Count > 20) break;
            }
            return problems;
        }

        /// <summary>Per beat and sound category, at most 3 voices play, each with a gain in [0, 1].</summary>
        public static List<string> SoundsAggregated(Timeline tl, out int maxVoices)
        {
            var problems = new List<string>();
            var counts = new Dictionary<long, int>();
            foreach (SoundEvent s in tl.Sounds)
            {
                long key = ((long)Math.Floor(tl.Map.BeatIndexAt(s.Time) + 1e-6) << 8) | (long)(s.Sound / Catalog.Variants);
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
                if (!(s.Gain >= 0f && s.Gain <= 1f)) problems.Add("sound at " + s.Time + " has gain " + s.Gain);
            }
            maxVoices = 0;
            foreach (KeyValuePair<long, int> kv in counts) maxVoices = Math.Max(maxVoices, kv.Value);
            if (maxVoices > 3) problems.Add(maxVoices + " voices of one category on one beat");
            return problems;
        }

        /// <summary>
        /// Every attack carries the clip start and speed the sim fitted, within the speed limits, and
        /// its blow lands on its impact: start + annotated impact / speed = impact. A hero with a
        /// windup (its roster's <c>windupS</c>) plays its combos faster, up to <paramref name="heroMaxSpeed"/>.
        /// </summary>
        public static List<string> AttacksCarryTheirFit(Timeline tl, ClipLibrary clips, out int attacks, float heroMaxSpeed = ClipLibrary.MaxSpeed)
        {
            var problems = new List<string>();
            attacks = 0;
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Kind != ActionKind.Attack) continue;
                attacks++;
                if (!a.HasClipTiming) { problems.Add("attack at " + a.Impact + " has no clip timing"); continue; }
                float max = a.Entity == 0 ? heroMaxSpeed : ClipLibrary.MaxSpeed;
                if (a.ClipSpeed < ClipLibrary.MinSpeed - 1e-6f || a.ClipSpeed > max + 1e-6f)
                    problems.Add("attack at " + a.Impact + " plays at " + a.ClipSpeed);
                double landed = a.ClipStart + clips[a.Clip].Impact / a.ClipSpeed;
                if (Math.Abs(landed - a.Impact) > 1e-6) problems.Add("attack at " + a.Impact + " lands at " + landed);
                if (problems.Count > 20) break;
            }
            return problems;
        }

        /// <summary>
        /// Every view width stays within (1 m, <paramref name="envelopeM"/>], and two bars into each
        /// section the width has settled on the section's.
        /// </summary>
        public static List<string> FramingEasesTheZoom(Timeline tl, float envelopeM)
        {
            var problems = new List<string>();
            foreach (FramingSample f in tl.Framing)
                if (f.ViewWidth < 1f || f.ViewWidth > envelopeM + 1e-3f) { problems.Add("view width " + f.ViewWidth); break; }
            foreach (SongSection sec in tl.Map.Sections)
            {
                double t = tl.Map.TimeOfBeatIndex(sec.FirstBeat + 8) + 0.02;
                if (t >= sec.End) continue;
                float want = tl.Sections[tl.Map.SectionIndexOf(sec.Label)].ViewWidthM, got = tl.FramingAt(t).ViewWidth;
                if (Math.Abs(got - want) > 1e-3f) problems.Add(sec.Label + " settles at " + got + " m, wants " + want);
            }
            return problems;
        }
    }
}
