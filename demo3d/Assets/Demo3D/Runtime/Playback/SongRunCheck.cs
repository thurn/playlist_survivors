using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Demo3D.Bench;
using Demo3D.Runtime.Bench;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// `--song-check <out.json>`: collects every frame's timing through the song with the harness's
    /// policy (<see cref="FrameTimingCollector"/>), the effect rejections, the light drop share and
    /// the harness's machine samples, and writes <see cref="SongWindows"/>' verdict when the run
    /// ends. Each frame is placed at its own Update's song time: FrameTimingManager's timestamps are
    /// QueryPerformanceCounter ticks (the harness checks this), and frame k starts in
    /// (U[k-1], U[k]], U being the counter at each Update. Frames that started before the first
    /// Update (loading) are dropped. A run that doesn't cover the whole song fails.
    /// </summary>
    public sealed class SongRunCheck : IDisposable
    {
        readonly FrameTimingCollector _timing = new FrameTimingCollector();
        readonly List<long> _updateQpc = new List<long>();
        readonly List<double> _updateSong = new List<double>();
        readonly List<(double, double)> _drops = new List<(double, double)>();
        readonly NoiseSampler _sampler = new NoiseSampler(new QuietRules().WindowS);
        readonly double _ftmPerQpc;
        readonly long _t0 = Win32.Qpc();
        int _rejected;

        public SongRunCheck()
        {
            ulong ftm = FrameTimingManager.GetCpuTimerFrequency();
            long qpc = Win32.QpcFrequency();
            _ftmPerQpc = ftm > 0 && qpc > 0 ? (double)ftm / qpc : 1;
        }

        /// <summary>Once per frame, with the frame's song time and this frame's effect counters.</summary>
        public void Update(double songTime, int rejectedThisFrame, double dropShare)
        {
            if (_sampler.Window == IntPtr.Zero) _sampler.Window = Win32.OwnWindow();
            _updateQpc.Add(Win32.Qpc());
            _updateSong.Add(songTime);
            _rejected += rejectedThisFrame;
            _drops.Add((songTime, dropShare));
            _timing.Poll();
        }

        /// <summary>The song time of the Update that ended the frame starting at FrameTimingManager tick <paramref name="start"/>, or NaN before the first.</summary>
        double SongTimeOf(ulong start)
        {
            double qpc = start / _ftmPerQpc;
            if (_updateQpc.Count == 0 || qpc <= _updateQpc[0]) return double.NaN;
            int lo = 1, hi = _updateQpc.Count - 1;
            if (qpc > _updateQpc[hi]) return _updateSong[hi];
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_updateQpc[mid] >= qpc) hi = mid; else lo = mid + 1;
            }
            return _updateSong[lo];
        }

        /// <summary>Writes the verdict; returns whether the run passes. The song runs from 0 to <paramref name="songDuration"/>.</summary>
        public bool Write(string path, double songDuration, string commandLine)
        {
            _timing.Flush();
            var frames = new List<SongWindows.Frame>(_timing.Frames.Count);
            int beforeFirstUpdate = 0;
            foreach (var f in _timing.Frames)
            {
                double t = SongTimeOf(f.Start);
                if (double.IsNaN(t)) { beforeFirstUpdate++; continue; }
                frames.Add(new SongWindows.Frame { T = t, GpuMs = f.Gpu, MainMs = f.Main, RenderMs = f.Render });
            }
            var r = SongWindows.Evaluate(frames, _drops, _rejected, new Lines(), Effects.EffectLights.DropLimit);
            // Coverage by the run's Updates, not its first measured frame: the first frame loads for
            // over a second while the song plays, so the first frame after it starts near 1 s.
            bool whole = r.Frames > 0 && _updateSong.Count > 0 && _updateSong[0] <= 0.5 && _updateSong[_updateSong.Count - 1] >= songDuration - 0.5;
            r.Pass &= whole;
            var machine = NoiseVerdict.Judge(_sampler.Between(_t0, Win32.Qpc()), new QuietRules());
            var ci = CultureInfo.InvariantCulture;
            r.Context.Add(new KeyValuePair<string, string>("wholeSong", whole ? "true" : "false"));
            r.Context.Add(new KeyValuePair<string, string>("framesBeforeFirstUpdate", beforeFirstUpdate.ToString(ci)));
            r.Context.Add(new KeyValuePair<string, string>("machineQuiet", machine.Noisy ? "false" : "true"));
            r.Context.Add(new KeyValuePair<string, string>("machineReasons", "[" + string.Join(", ", machine.Reasons.ConvertAll(SongWindows.Quote)) + "]"));
            r.Context.Add(new KeyValuePair<string, string>("gpuTempMaxC", machine.TempMaxC.HasValue ? machine.TempMaxC.Value.ToString("F0", ci) : "null"));
            r.Context.Add(new KeyValuePair<string, string>("otherCpuPctMax", machine.OtherCpuPctMax.HasValue ? machine.OtherCpuPctMax.Value.ToString("F1", ci) : "null"));
            r.Context.Add(new KeyValuePair<string, string>("build", SongWindows.Quote(Application.version + " " + Application.buildGUID)));
            r.Context.Add(new KeyValuePair<string, string>("commandLine", SongWindows.Quote(commandLine)));
            r.Context.Add(new KeyValuePair<string, string>("written", SongWindows.Quote(DateTime.Now.ToString("s", ci))));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, r.ToJson());
            Debug.Log($"SongRunCheck: {(r.Pass ? "PASS" : "FAIL")} over {r.Windows.Count} windows{(machine.Noisy ? " (machine noisy: " + string.Join("; ", machine.Reasons) + ")" : "")} -> {path}");
            return r.Pass;
        }

        public void Dispose() => _sampler.Dispose();
    }
}
