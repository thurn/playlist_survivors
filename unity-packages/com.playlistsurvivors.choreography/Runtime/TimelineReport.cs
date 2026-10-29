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
            sb.Append(SectionTable(tl));
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
    }
}
