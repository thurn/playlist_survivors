using System;
using System.Globalization;
using System.Text;

namespace PS.Choreography
{
    /// <summary>Plain-text summaries of a timeline and its tick timing, for the CLI and the evidence.</summary>
    public static class TimelineReport
    {
        static string F(double v, int d) => v.ToString("F" + d, CultureInfo.InvariantCulture);

        public static string Summary(Timeline tl)
        {
            var sb = new StringBuilder();
            SimTickTiming.Summary s = tl.TickTiming.Summarize();
            sb.Append(tl.Name).Append(": built in ").Append(F(tl.BuildSeconds, 3)).Append(" s; ")
              .Append(tl.Entities.Length).Append(" entities, ").Append(tl.Segments.Length).Append(" segments, ")
              .Append(tl.Actions.Length).Append(" actions, ").Append(tl.Effects.Length).Append(" effects, ")
              .Append(tl.Sounds.Length).Append(" sounds, ").Append(tl.Framing.Length).Append(" framing samples\n");
            sb.Append("sim ticks after 5 s: ").Append(s.Ticks).Append(" ticks, p50 ").Append(F(s.P50Ms, 3)).Append(" ms, p99 ")
              .Append(F(s.P99Ms, 3)).Append(" ms, max ").Append(F(s.MaxMs, 3)).Append(" ms, mean ").Append(F(s.MeanMs, 3)).Append(" ms; flow-field rebuilds ")
              .Append(tl.TickTiming.FlowRebuilds).Append(" in ").Append(tl.TickTiming.FlowRebuildSlices).Append(" slices\n");
            sb.Append(SectionTable(tl)).Append('\n').Append(CarnageTable(tl));
            return sb.ToString();
        }

        public static string SectionTable(Timeline tl)
        {
            var sb = new StringBuilder();
            sb.Append("| section | feel | s | load | target N | mean on screen | max | deaths/s target | deaths/s | ratio | hits/s | spawns | attacks | strides | goal reached |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (SectionStats st in tl.Sections)
            {
                double ratio = st.TargetDeathsPerSecond > 0 ? st.DeathsPerSecond / st.TargetDeathsPerSecond : 0;
                sb.Append("| ").Append(st.Label).Append(" | ").Append(st.Feel == SectionFeel.Free ? "free" : "pulsed")
                  .Append(" | ").Append(F(st.Start, 1)).Append("–").Append(F(st.End, 1))
                  .Append(" | ").Append(st.Load).Append(" | ").Append(st.TargetOnScreen)
                  .Append(" | ").Append(F(st.MeanOnScreen, 0)).Append(" | ").Append(st.MaxOnScreen)
                  .Append(" | ").Append(F(st.TargetDeathsPerSecond, 2)).Append(" | ").Append(F(st.DeathsPerSecond, 2))
                  .Append(" | ").Append(st.TargetDeathsPerSecond > 0 ? F(ratio, 3) : "–")
                  .Append(" | ").Append(F(st.HitsPerSecond, 1)).Append(" | ").Append(st.Spawns).Append(" | ").Append(st.Attacks)
                  .Append(" | ").Append(st.HeroStrides).Append(" | ")
                  .Append(st.Goal == null ? "–" : st.GoalReachedAt >= 0 ? F(st.GoalReachedAt, 1) + " s" : "no").Append(" |\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The structure breaks (choreography.md, "Structure breaks"), one row per break: when and where it struck,
        /// whether the camera saw it, how far the hero's bolt flew, and the horde through the opening in the
        /// <paramref name="bars"/> bars after it (monsters that stepped over the broken pieces' footprints, those that
        /// spawned since included), and the shares of the horde alive at the break, and of the part of it within
        /// <see cref="NearBreakM"/> of the point struck, that went through. Empty
        /// without breaks. <paramref name="list"/> is the run's placement list.
        /// </summary>
        public static string BreakTable(Timeline tl, PlacementList list, int bars = 4)
        {
            if (tl.Breaks.Length == 0) return "";
            var sb = new StringBuilder();
            sb.Append("| break | s | section | pieces | in view | bolt m | through it in ").Append(bars).Append(" bars | horde at the break | share | within ")
              .Append(F(NearBreakM, 0)).Append(" m | share of those |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|---|\n");
            var deathAt = new double[tl.EntityCount];
            for (int e = 0; e < tl.EntityCount; e++)
            {
                deathAt[e] = double.PositiveInfinity;
                foreach (ActionEvent a in tl.ActionsOf(e)) if (a.Kind == ActionKind.Death) { deathAt[e] = a.Impact; break; }
            }
            for (int i = 0; i < tl.Breaks.Length;)
            {
                BreakEvent b = tl.Breaks[i];
                var pieces = new System.Collections.Generic.List<int>();
                for (; i < tl.Breaks.Length && tl.Breaks[i].Time == b.Time && tl.Breaks[i].Target == b.Target; i++) pieces.Add(tl.Breaks[i].Placement);
                double bi = tl.Map.BeatIndexAt(b.Time);
                double end = tl.Map.TimeOfBeatIndex(bi + bars * tl.Map.BeatsPerBarAt(1));
                FramingSample f = tl.FramingAt(b.Time);
                int alive = 0, near = 0, through = 0, aliveThrough = 0, nearThrough = 0;
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind != EntityKind.Monster || info.Spawn > end || info.Despawn <= b.Time) continue;
                    bool isNear = false, isAlive = info.Spawn <= b.Time && deathAt[e] > b.Time;
                    if (isAlive)
                    {
                        alive++;
                        isNear = System.Numerics.Vector2.Distance(tl.PositionAt(e, b.Time), b.Point) <= NearBreakM;
                        if (isNear) near++;
                    }
                    double stop = Math.Min(end, deathAt[e]);
                    foreach (MotionSegment sg in tl.SegmentsOf(e))
                    {
                        if (sg.T1 < b.Time || sg.T0 > stop) continue;
                        // Along the step's line, finely enough not to step over a wall's thickness.
                        int n = Math.Max(1, (int)Math.Ceiling(System.Numerics.Vector2.Distance(sg.From, sg.To) / 0.1f));
                        bool hit = false;
                        for (int k = 0; k <= n && !hit; k++) hit = Within(list, pieces, System.Numerics.Vector2.Lerp(sg.From, sg.To, k / (float)n));
                        if (!hit) continue;
                        through++;
                        if (isAlive) aliveThrough++;
                        if (isNear) nearThrough++;
                        break;
                    }
                }
                sb.Append("| ").Append(b.Target).Append(" | ").Append(F(b.Time, 2)).Append(" | ").Append(tl.Sections[tl.Map.SectionIndexAt(b.Time)].Label)
                  .Append(" | ").Append(pieces.Count).Append(" | ").Append(tl.View(f.ViewWidth).Contains(f.Focus, b.Point) ? "yes" : "no")
                  .Append(" | ").Append(F(System.Numerics.Vector2.Distance(b.Origin, b.Point), 1))
                  .Append(" | ").Append(through).Append(" | ").Append(alive).Append(" | ").Append(alive > 0 ? F(aliveThrough / (double)alive, 2) : "–")
                  .Append(" | ").Append(near).Append(" | ").Append(near > 0 ? F(nearThrough / (double)near, 2) : "–").Append(" |\n");
            }
            return sb.ToString();
        }

        /// <summary>The break table's nearby horde: within this of the point the bolt struck.</summary>
        public const float NearBreakM = 10f;

        /// <summary>Does <paramref name="p"/> lie within any of the placements' footprints?</summary>
        static bool Within(PlacementList list, System.Collections.Generic.List<int> pieces, System.Numerics.Vector2 p)
        {
            foreach (int k in pieces)
            {
                Placement pl = list.Items[k];
                Footprint fp = list.Props[pl.Prop];
                if (fp.Shape == FootprintShape.Circle)
                {
                    if (System.Numerics.Vector2.Distance(p, pl.ToWorld(fp.Center)) < fp.Radius * pl.Scale) return true;
                    continue;
                }
                if (fp.Shape != FootprintShape.Polygon) continue;
                bool inside = false;
                for (int a = 0, n = fp.Points.Length; a < n; a++)
                {
                    System.Numerics.Vector2 u = pl.ToWorld(fp.Points[a]), v = pl.ToWorld(fp.Points[(a + 1) % n]);
                    if ((u.Y > p.Y) != (v.Y > p.Y) && p.X < u.X + (p.Y - u.Y) * (v.X - u.X) / (v.Y - u.Y)) inside = !inside;
                }
                if (inside) return true;
            }
            return false;
        }

        /// <summary>
        /// Per section, the numbers an intensity is tuned by: the hero's swings per second, kills per
        /// swing (mean and peak), the spells' kills and their share, and the mean on screen against target.
        /// </summary>
        public static string CarnageTable(Timeline tl)
        {
            var sb = new StringBuilder();
            sb.Append("| section | s | swings/s | kills/swing | peak kills/swing | hero kills | spell kills | spell share | other kills | target N | mean on screen | on screen/target |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (SectionStats st in tl.Sections)
            {
                int other = st.Deaths - st.HeroKills - st.SpellKills;
                sb.Append("| ").Append(st.Label).Append(" | ").Append(F(st.Start, 1)).Append("–").Append(F(st.End, 1))
                  .Append(" | ").Append(F(st.Duration > 0 ? st.HeroSwings / st.Duration : 0, 2))
                  .Append(" | ").Append(st.HeroSwings > 0 ? F(st.HeroKills / (double)st.HeroSwings, 1) : "–")
                  .Append(" | ").Append(st.MaxSwingKills).Append(" | ").Append(st.HeroKills).Append(" | ").Append(st.SpellKills)
                  .Append(" | ").Append(st.Deaths > 0 ? F(st.SpellKills / (double)st.Deaths, 2) : "–").Append(" | ").Append(other)
                  .Append(" | ").Append(st.TargetOnScreen).Append(" | ").Append(F(st.MeanOnScreen, 0))
                  .Append(" | ").Append(st.TargetOnScreen > 0 ? F(st.MeanOnScreen / st.TargetOnScreen, 2) : "–").Append(" |\n");
            }
            return sb.ToString();
        }
    }
}
