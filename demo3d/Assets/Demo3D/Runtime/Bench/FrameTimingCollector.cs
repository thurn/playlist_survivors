using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// Collects every frame's timing once it is complete, for the harness (BenchRunner) and the
    /// song check (TD13's review: both must measure the same way). GetLatestTimings returns the
    /// newest frames first, and the newest ones don't have their GPU time yet (it reads 0). A frame
    /// stays pending, updated on every poll, until at least <see cref="Hold"/> newer frames exist and
    /// its GPU time has arrived, or until it is about to leave GetLatestTimings' 64-frame window.
    /// Frames are committed in order, so an unfinished one holds back the newer ones (the
    /// foundations review: dropping zero readings biased GPU p99 low, since the frames still
    /// unfinished tend to be the slow ones; 20% of a passing probe's frames read zero).
    /// Timestamps are FrameTimingManager's CPU timer, QueryPerformanceCounter on Windows.
    /// </summary>
    public sealed class FrameTimingCollector
    {
        public struct Frame
        {
            public ulong Start;
            public double Gpu, Main, Render, Cpu;
        }

        readonly FrameTiming[] _ft = new FrameTiming[64];
        readonly Dictionary<ulong, Frame> _pending = new Dictionary<ulong, Frame>();
        readonly List<ulong> _keys = new List<ulong>();
        ulong _committed;

        /// <summary>Committed frames, oldest first (trimmed by half past 200,000).</summary>
        public readonly List<Frame> Frames = new List<Frame>();
        public int Hold = 24;
        /// <summary>The newest frame start seen, committed or not.</summary>
        public ulong LastFrameStart { get; private set; }

        public void Poll()
        {
            FrameTimingManager.CaptureFrameTimings();
            uint n = FrameTimingManager.GetLatestTimings((uint)_ft.Length, _ft);
            for (int i = 0; i < n; i++)
            {
                var f = _ft[i];
                if (f.frameStartTimestamp <= _committed) continue;
                LastFrameStart = Math.Max(LastFrameStart, f.frameStartTimestamp);
                _pending[f.frameStartTimestamp] = new Frame
                {
                    Start = f.frameStartTimestamp,
                    Gpu = f.gpuFrameTime,
                    // The main thread's work time; its wait in Present is reported separately.
                    Main = f.cpuMainThreadFrameTime,
                    Render = f.cpuRenderThreadFrameTime,
                    Cpu = f.cpuFrameTime,
                };
            }
            if (_pending.Count <= Hold) return;
            _keys.Clear();
            _keys.AddRange(_pending.Keys);
            _keys.Sort();
            int window = _ft.Length - 2;
            for (int i = 0; i < _keys.Count - Hold; i++)
            {
                // Wait for the GPU time while the frame can still be read again.
                if (_pending[_keys[i]].Gpu <= 0 && _keys.Count - i < window) break;
                Frames.Add(_pending[_keys[i]]);
                _pending.Remove(_keys[i]);
                _committed = _keys[i];
            }
            if (Frames.Count > 200000) Frames.RemoveRange(0, 100000);
        }

        /// <summary>Commits whatever is still pending, in order (the run's last frames, GPU time or not).</summary>
        public void Flush()
        {
            _keys.Clear();
            _keys.AddRange(_pending.Keys);
            _keys.Sort();
            foreach (ulong k in _keys) Frames.Add(_pending[k]);
            _pending.Clear();
            if (_keys.Count > 0) _committed = _keys[_keys.Count - 1];
        }
    }
}
