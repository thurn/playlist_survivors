using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// Other processes' use of the GPU's 3D engine, from Windows' "GPU Engine" performance counters
    /// (the per-process utilization Task Manager shows; no administrator rights needed). Another
    /// session's player or editor rendering beside a sweep inflates its GPU times without showing in
    /// CPU or thermal readings.
    /// </summary>
    public sealed class GpuUsage : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQueryW(string source, IntPtr user, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);

        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
                                                        out uint itemCount, IntPtr items);

        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);

        const uint PdhFmtDouble = 0x200, PdhMoreData = 0x800007D2;
        static readonly Regex Instance = new Regex(@"^pid_(\d+)_.*engtype_3d$", RegexOptions.IgnoreCase);
        readonly IntPtr _query, _counter;
        readonly uint _ownPid = Win32.GetCurrentProcessId();
        public readonly bool Available;

        public GpuUsage()
        {
            try
            {
                Available = PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0
                            && PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) == 0;
                if (Available) PdhCollectQueryData(_query); // a rate: the first collect is the baseline
            }
            catch (Exception)
            {
                Available = false;
            }
        }

        /// <summary>
        /// Collects and returns other processes' total 3D-engine utilization (percent) since the last
        /// call, and the busiest other process's pid; NaN when unavailable.
        /// </summary>
        public double OtherPct(out int topPid, out double topPct)
        {
            topPid = 0;
            topPct = 0;
            if (!Available || PdhCollectQueryData(_query) != 0) return double.NaN;
            uint size = 0;
            uint status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out var count, IntPtr.Zero);
            if (status != PdhMoreData || size == 0) return double.NaN;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out count, buf) != 0) return double.NaN;
                // PDH_FMT_COUNTERVALUE_ITEM_W: a name pointer, then {CStatus, (padding), double}.
                int stride = IntPtr.Size + 16;
                double total = 0;
                var perPid = new System.Collections.Generic.Dictionary<int, double>();
                for (int i = 0; i < count; i++)
                {
                    var item = buf + i * stride;
                    var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    if (Marshal.ReadInt32(item, IntPtr.Size) != 0 || name == null) continue; // bad status
                    var m = Instance.Match(name);
                    if (!m.Success) continue;
                    int pid = int.Parse(m.Groups[1].Value);
                    if (pid == _ownPid) continue;
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
                    total += v;
                    perPid[pid] = (perPid.TryGetValue(pid, out var p) ? p : 0) + v;
                }
                foreach (var kv in perPid)
                    if (kv.Value > topPct) { topPct = kv.Value; topPid = kv.Key; }
                return total;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void Dispose()
        {
            if (Available) PdhCloseQuery(_query);
        }
    }
}
