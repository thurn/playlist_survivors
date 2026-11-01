using System;
using System.Text;

namespace Game.Client.Loop
{
    /// <summary>Numbers into a CSV row without allocating, for the logs a frame writes (<see cref="FrameProfile"/>, <see cref="TimingLog"/>).</summary>
    static class CsvText
    {
        public static StringBuilder Int(StringBuilder sb, long v)
        {
            if (v < 0)
            {
                sb.Append('-');
                v = -v;
            }
            int start = sb.Length;
            do
            {
                sb.Append((char)('0' + v % 10));
                v /= 10;
            } while (v > 0);
            for (int i = start, j = sb.Length - 1; i < j; i++, j--) (sb[i], sb[j]) = (sb[j], sb[i]);
            return sb;
        }

        /// <summary><paramref name="v"/> to <paramref name="decimals"/> places; `nan` when it isn't a number.</summary>
        public static StringBuilder Fixed(StringBuilder sb, double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return sb.Append("nan");
            long scale = 1;
            for (int i = 0; i < decimals; i++) scale *= 10;
            long units = (long)Math.Round(Math.Abs(v) * scale);
            if (v < 0 && units > 0) sb.Append('-');
            Int(sb, units / scale).Append('.');
            long frac = units % scale;
            for (long d = scale / 10; d > 0; d /= 10)
            {
                sb.Append((char)('0' + frac / d));
                frac %= d;
            }
            return sb;
        }
    }
}
