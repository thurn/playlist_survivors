using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Demo3D.Bench;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// The quiet-machine lock (benchmarks.md, Quiet machine): bench.lock in the per-machine cache,
    /// opened for exclusive access for the sweep's whole life. Windows closes the handle when the
    /// process dies, so a killed sweep leaves nothing to clean up. The holder's pid and start time go
    /// to bench.lock.holder beside it, for whoever waits.
    /// </summary>
    public sealed class MachineLock : IDisposable
    {
        IntPtr _handle = Win32.InvalidHandle;
        public readonly string Path;

        public MachineLock(string path) { Path = path; }

        public static string DefaultPath()
        {
            var env = Environment.GetEnvironmentVariable("DEMO3D_CACHE");
            string root = !string.IsNullOrEmpty(env) ? env
                : Directory.Exists("D:\\") ? "D:\\cache\\playlist-survivors\\demo3d"
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                         "playlist-survivors", "demo3d");
            return System.IO.Path.Combine(root, "bench.lock");
        }

        public string HolderPath => Path + ".holder";

        /// <summary>One attempt. False while another process holds it.</summary>
        public bool TryAcquire(string description)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var h = Win32.OpenExclusive(Path, out var held, out var error);
            if (h == Win32.InvalidHandle)
            {
                if (held) return false;
                throw new IOException($"bench lock: can't open {Path} (error {error})");
            }
            _handle = h;
            var info = $"pid {Win32.GetCurrentProcessId()} since {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {description}\n";
            Win32.Write(h, info);
            try { File.WriteAllText(HolderPath, info); } catch (IOException) { }
            return true;
        }

        public string Holder()
        {
            try { return File.ReadAllText(HolderPath).Trim(); }
            catch (Exception) { return "unknown holder"; }
        }

        public void Dispose()
        {
            if (_handle == Win32.InvalidHandle) return;
            Win32.CloseHandle(_handle);
            _handle = Win32.InvalidHandle;
        }
    }

    /// <summary>One second of the machine's state.</summary>
    public struct NoiseSample
    {
        public long Ticks; // QueryPerformanceCounter
        public double OtherCpuPct;
        public double OtherCpuPctWindow; // the rolling mean over the quiet window
        public double TempC; // NaN if unknown
        public ulong Reasons;
        public double PowerW;
        public double OtherGpuPct; // other processes' 3D-engine utilization, NaN if unknown
        public string OtherGpuTop; // the busiest other GPU process
        public bool Foreground;
        public string ForegroundOwner; // class and title of the foreground window when it isn't ours
        public string Occluder; // class and title of a window above ours, or null
        public int Display; // -1 unknown, 0 off, 1 on, 2 dimmed
    }

    /// <summary>
    /// Samples the machine every second on a background thread (benchmarks.md, Quiet machine):
    /// other processes' CPU and GPU (3D engine), the GPU's temperature and throttle reasons, and
    /// whether the player's window is in the foreground, unoccluded and on a lit display.
    /// </summary>
    public sealed class NoiseSampler : IDisposable
    {
        readonly List<NoiseSample> _samples = new List<NoiseSample>();
        readonly object _gate = new object();
        readonly Thread _thread;
        readonly double _windowS;
        volatile bool _run = true;
        public IntPtr Window;

        public NoiseSampler(double windowS)
        {
            _windowS = windowS;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Demo3D noise sampler" };
            _thread.Start();
        }

        void Loop()
        {
            Win32.CpuTimes(out var idle0, out var kernel0, out var user0, out var own0);
            var window = new Queue<double>();
            using var gpu = new GpuUsage();
            while (_run)
            {
                Thread.Sleep(1000);
                if (!_run) break;
                var s = new NoiseSample { Ticks = Win32.Qpc(), TempC = double.NaN, PowerW = double.NaN };
                if (Win32.CpuTimes(out var idle1, out var kernel1, out var user1, out var own1))
                {
                    s.OtherCpuPct = Stats.OtherCpuPct(idle0, kernel0, user0, own0, idle1, kernel1, user1, own1);
                    idle0 = idle1; kernel0 = kernel1; user0 = user1; own0 = own1;
                }
                window.Enqueue(s.OtherCpuPct);
                while (window.Count > Math.Max(1, (int)Math.Round(_windowS))) window.Dequeue();
                double sum = 0;
                foreach (var v in window) sum += v;
                s.OtherCpuPctWindow = sum / window.Count;
                var t = Nvml.TemperatureC();
                if (t.HasValue) s.TempC = t.Value;
                s.Reasons = Nvml.ThrottleReasons() ?? 0;
                s.PowerW = Nvml.PowerW() ?? double.NaN;
                s.OtherGpuPct = gpu.OtherPct(out var topPid, out var topPct);
                if (topPct >= 1) s.OtherGpuTop = $"pid {topPid} ({ProcessName(topPid)}, {topPct:F0}%)";
                var w = Window;
                var fg = Win32.GetForegroundWindow();
                s.Foreground = w == IntPtr.Zero || fg == w;
                if (!s.Foreground) s.ForegroundOwner = fg == IntPtr.Zero ? "none" : $"{Win32.ClassName(fg)} \"{Win32.Title(fg)}\"";
                var occ = Win32.Occluder(w);
                if (occ != IntPtr.Zero) s.Occluder = $"{Win32.ClassName(occ)} \"{Win32.Title(occ)}\"";
                s.Display = Win32.DisplayState;
                lock (_gate) _samples.Add(s);
            }
        }

        static string ProcessName(int pid)
        {
            return Win32.ProcessImage(pid) ?? "unknown";
        }

        public NoiseSample? Latest
        {
            get
            {
                lock (_gate) return _samples.Count == 0 ? (NoiseSample?)null : _samples[_samples.Count - 1];
            }
        }

        /// <summary>Samples taken between two QueryPerformanceCounter timestamps.</summary>
        public List<NoiseSample> Between(long t0, long t1)
        {
            var list = new List<NoiseSample>();
            lock (_gate)
                foreach (var s in _samples)
                    if (s.Ticks >= t0 && s.Ticks <= t1) list.Add(s);
            return list;
        }

        public void Dispose()
        {
            _run = false;
            _thread.Join(3000);
        }
    }

    /// <summary>The verdict on a run's samples.</summary>
    public sealed class NoiseVerdict
    {
        public bool Noisy, FocusLost, Occluded, DisplayOff;
        public double? OtherCpuPctMax, OtherGpuPctMax, TempMaxC;
        public ulong ReasonsSeen;
        public readonly List<string> Reasons = new List<string>();

        public static NoiseVerdict Judge(List<NoiseSample> samples, QuietRules q)
        {
            var v = new NoiseVerdict();
            string occluder = null, foreground = null, gpuTop = null;
            foreach (var s in samples)
            {
                v.OtherCpuPctMax = Math.Max(v.OtherCpuPctMax ?? 0, s.OtherCpuPctWindow);
                if (!double.IsNaN(s.TempC)) v.TempMaxC = Math.Max(v.TempMaxC ?? 0, s.TempC);
                if (!double.IsNaN(s.OtherGpuPct) && s.OtherGpuPct > (v.OtherGpuPctMax ?? -1))
                {
                    v.OtherGpuPctMax = s.OtherGpuPct;
                    gpuTop = s.OtherGpuTop;
                }
                v.ReasonsSeen |= s.Reasons;
                if (!s.Foreground) { v.FocusLost = true; foreground = s.ForegroundOwner; }
                if (s.Occluder != null) { v.Occluded = true; occluder = s.Occluder; }
                if (s.Display == 0) v.DisplayOff = true;
            }
            if (v.OtherCpuPctMax > q.CpuPct)
                v.Reasons.Add($"other processes used {v.OtherCpuPctMax:F1}% CPU over {q.WindowS:F0} s");
            if (v.OtherGpuPctMax > q.GpuPct)
                v.Reasons.Add($"other processes used {v.OtherGpuPctMax:F1}% of the GPU's 3D engine (busiest: {gpuTop})");
            if ((v.ReasonsSeen & MachineFacts.NoiseReasons) != 0)
                v.Reasons.Add("GPU slowdown: " + MachineFacts.ThrottleNames(v.ReasonsSeen & MachineFacts.NoiseReasons));
            if (v.FocusLost && q.RequireForeground) v.Reasons.Add("the player's window lost the foreground to " + foreground);
            if (v.Occluded) v.Reasons.Add("the player's window was occluded by " + occluder);
            if (v.DisplayOff) v.Reasons.Add("the display was off");
            if (samples.Count == 0) v.Reasons.Add("no machine samples");
            v.Noisy = v.Reasons.Count > 0;
            return v;
        }
    }
}
