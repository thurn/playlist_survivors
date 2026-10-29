using System;
using System.Globalization;
using System.Text;

namespace Demo3D.Choreography
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

        /// <summary>
        /// The evidence's timing tables: the song's build time and tick timing, then each stress
        /// script (S1–S6) at a few N at its 40 m view. <paramref name="repeat"/> builds each that many
        /// times and keeps the fastest build (tick percentiles from that run).
        /// </summary>
        public static string TimingTables(string root, int repeat = 1)
        {
            var sb = new StringBuilder();
            ChoreographyData data = ChoreographyFiles.LoadData(root);
            sb.Append("| timeline | N | off-screen reserve | mean on screen | deaths/s (target) | build s | ticks | p50 ms | p99 ms | max ms | mean ms | flow rebuilds (slices) |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            Timeline best = null;
            for (int r = 0; r < repeat; r++)
            {
                Timeline tl = ChoreographyFiles.BuildSong(root);
                if (best == null || tl.BuildSeconds < best.BuildSeconds) best = tl;
            }
            Row(sb, "song", "per section", "0.5 × target", best);
            foreach (string scene in new[] { "S1", "S2", "S3", "S4", "S5", "S6" })
            {
                StressScript script = ChoreographyFiles.LoadStress(root, scene);
                foreach (int n in new[] { 500, 1000, 2000, 2800 })
                {
                    StressScript s = script.With(n: n);
                    Timeline bt = null;
                    for (int r = 0; r < repeat; r++)
                    {
                        Timeline tl = Choreographer.RunStress(s, data);
                        if (bt == null || tl.BuildSeconds < bt.BuildSeconds) bt = tl;
                    }
                    int nEff = Math.Min(n, new ViewGeometry(s.ViewWidthM).PackingCapacity());
                    Row(sb, scene + " (" + s.Load + ", " + F(s.Seconds, 0) + " s)", nEff.ToString(CultureInfo.InvariantCulture),
                        ((int)Math.Round(s.ReserveFactor * nEff)).ToString(CultureInfo.InvariantCulture), bt);
                }
            }
            return sb.ToString();
        }

        static void Row(StringBuilder sb, string name, string n, string reserve, Timeline tl)
        {
            SimTickTiming.Summary s = tl.TickTiming.Summarize();
            string onScreen = "–", deaths = "–";
            if (tl.Sections.Length == 1)
            {
                SectionStats st = tl.Sections[0];
                onScreen = F(st.MeanOnScreen, 0);
                deaths = F(st.DeathsPerSecond, 1) + " (" + F(st.TargetDeathsPerSecond, 1) + ")";
            }
            sb.Append("| ").Append(name).Append(" | ").Append(n).Append(" | ").Append(reserve).Append(" | ").Append(onScreen)
              .Append(" | ").Append(deaths).Append(" | ").Append(F(tl.BuildSeconds, 3))
              .Append(" | ").Append(s.Ticks).Append(" | ").Append(F(s.P50Ms, 3)).Append(" | ").Append(F(s.P99Ms, 3))
              .Append(" | ").Append(F(s.MaxMs, 3)).Append(" | ").Append(F(s.MeanMs, 4))
              .Append(" | ").Append(tl.TickTiming.FlowRebuilds).Append(" (").Append(tl.TickTiming.FlowRebuildSlices).Append(") |\n");
        }
    }
}
