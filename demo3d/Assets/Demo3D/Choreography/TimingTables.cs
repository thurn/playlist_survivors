using System;
using System.Globalization;
using System.Text;
using PS.Choreography;

namespace Demo3D.Choreography
{
    /// <summary>demo3d's evidence timing tables (TD3, TD6): the song and the stress scripts S1–S6.</summary>
    public static class TimingTables
    {
        static string F(double v, int d) => v.ToString("F" + d, CultureInfo.InvariantCulture);

        /// <summary>
        /// The evidence's timing tables: the song's build time and tick timing, then each stress
        /// script (S1–S6) at a few N at its 40 m view. <paramref name="repeat"/> builds each that many
        /// times and keeps the fastest build (tick percentiles from that run).
        /// </summary>
        public static string Build(string root, int repeat = 1)
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
