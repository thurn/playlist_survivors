using System;
using System.IO;
using System.Text;
using Game.Client.Effects;
using Game.Client.Music;
using Game.Client.Sound;
using PS.Game.Sim;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Client.Loop
{
    /// <summary>
    /// The session's timing log (unity-game.md, Sessions and Replay): `timing.csv` beside its audio, entries and
    /// inputs, one row for each frame of the loop from the run's start, always written while a run is recorded, as
    /// native writes its timing.jsonl, so a run that was played can be asked about afterwards (`game log`). A row is the
    /// frame's times and what the run did in it:
    /// - `frame` (Unity's frame count), `stepped` (1 when the sim stepped: not while the arena loads or before the
    ///   music's anchor), `real_s` (the time since launch);
    /// - `interval_ms`, the time since the previous frame, and `cpu_ms`, the main thread's CPU time over that interval
    ///   (from the previous frame's Update to this one's, so it holds the previous frame's loop; an IL2CPP player's,
    ///   -1 elsewhere): a long interval with little CPU time waited on the GPU, the display or the OS;
    /// - `loop_ms` (<see cref="GameLoop"/>'s Update), `step_ms` (the sim's step, the dispatch and the presenters) and
    ///   `sim_ms` (the sim's step alone);
    /// - `render_ms`, `gpu_ms` and `present_wait_ms`: Unity's frame timing (FrameTimingManager) of the latest frame it
    ///   has timed, which trails this one by a few frames; -1 where it has none (a batch-mode player's frames mostly);
    /// - `frame_song` and `lead_ms` (the frontier ahead of the music heard), `phase` and `frozen` as the frame shows the
    ///   run, the enemies `alive` (at the sim's frontier) and the hero's `hero_x`, `hero_y` (m), nan before the sim's first step;
    /// - the frame's share of the run's counts (<see cref="Count"/>): sim events, kills, music entries and those
    ///   that arrived late, key changes, sounds scheduled, dropped and cut off, effects spawned and rejected, garbage
    ///   collections, presenter faults, freezes started, and the music plugin's stalls.
    /// A row is written without allocating, into a buffer flushed about once a second, so a crash loses at most that.
    /// The log never stops a run: if it can't be opened or written it says so once in the player's log and stops.
    /// </summary>
    sealed class TimingLog : IDisposable
    {
        public const string FileName = "timing.csv";

        /// <summary>The run's running counts a row gives its frame's share of, each a column named in lower case.</summary>
        public enum Count { Events, Kills, Entries, Late, Keys, Sounds, Dropped, Cut, Effects, Rejected, Gc, Faults, Freezes, Stalls }

        public static readonly string Header = MakeHeader();

        static readonly int CountCount = Enum.GetValues(typeof(Count)).Length;
        static readonly string[] Phases = { "waiting", "playing", "finale", "ended" };
        const double FlushS = 1;

        /// <summary>One frame's row; <see cref="Row"/> writes it.</summary>
        public struct Frame
        {
            public int Index, Alive;
            public bool Stepped, Frozen;
            public RunPhase Phase;
            public double RealS, IntervalMs, CpuMs, LoopMs, StepMs, SimMs, RenderMs, GpuMs, PresentWaitMs, Song, LeadMs, HeroX, HeroY;
            /// <summary>The run's counts by <see cref="Count"/> at the frame's end.</summary>
            public long[] Counts;
        }

        readonly SimDriver _driver;
        readonly RunTelemetry _telemetry;
        readonly MusicPlayer _music;
        readonly SoundScheduler _sounds;
        readonly EffectsPresenter _effects;
        StreamWriter _out;
        readonly long[] _counts = new long[CountCount], _before = new long[CountCount];
        readonly FrameTiming[] _timings = new FrameTiming[1];
        readonly StringBuilder _row = new StringBuilder(1024);
        readonly char[] _chars = new char[1024];
        readonly int _collections;
        long _cpu = -1;
        double _cpuMs = -1;
        int _framesSeen;
        double _flushedAt;

        /// <summary>
        /// The log in <paramref name="sessionDir"/>, or null if it can't be created there (the run goes on without
        /// it); the music, the sounds and the effects may be null.
        /// </summary>
        public static TimingLog Open(string sessionDir, SimDriver driver, RunTelemetry telemetry, MusicPlayer music,
            SoundScheduler sounds, EffectsPresenter effects)
        {
            try
            {
                return new TimingLog(sessionDir, driver, telemetry, music, sounds, effects);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TimingLog: no timing log in {sessionDir}: {e.Message}");
                return null;
            }
        }

        TimingLog(string sessionDir, SimDriver driver, RunTelemetry telemetry, MusicPlayer music, SoundScheduler sounds,
            EffectsPresenter effects)
        {
            _driver = driver;
            _telemetry = telemetry;
            _music = music;
            _sounds = sounds;
            _effects = effects;
            _out = new StreamWriter(Path.Combine(sessionDir, FileName), false, new UTF8Encoding(false), 1 << 16);
            _out.Write(Header);
            _out.Write('\n');
            _collections = GC.CollectionCount(0);
            _framesSeen = driver.Frames;
        }

        /// <summary>
        /// The start of GameLoop's Update: the main thread's CPU time since the previous one's start, which spans the
        /// frame's interval (the previous frame's Update, its rendering and this frame's start) as Unity measures it.
        /// </summary>
        public void Begin()
        {
            long cpu = ThreadCpu.Ns;
            _cpuMs = cpu < 0 || _cpu < 0 ? -1 : (cpu - _cpu) / 1e6;
            _cpu = cpu;
        }

        /// <summary>The frame's row, at the end of GameLoop's Update, which took <paramref name="loopTicks"/> (Stopwatch ticks).</summary>
        public void Write(long loopTicks)
        {
            if (_out == null) return;
            try
            {
                WriteRow(loopTicks);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TimingLog: stopped writing the timing log: {e.Message}");
                Dispose();
            }
        }

        void WriteRow(long loopTicks)
        {
            var driver = _driver;
            var f = new Frame
            {
                Index = Time.frameCount,
                Stepped = driver.Frames != _framesSeen,
                RealS = Time.realtimeSinceStartupAsDouble,
                IntervalMs = 1000.0 * Time.unscaledDeltaTime,
                CpuMs = _cpuMs,
                LoopMs = loopTicks * 1000.0 / Stopwatch.Frequency,
                RenderMs = -1, GpuMs = -1, PresentWaitMs = -1,
                Song = double.NaN, LeadMs = double.NaN, HeroX = double.NaN, HeroY = double.NaN,
                Counts = _counts,
            };
            if (f.Stepped)
            {
                f.StepMs = driver.FrameMs;
                f.SimMs = driver.StepMs;
            }
            _framesSeen = driver.Frames;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                f.RenderMs = Timed(_timings[0].cpuRenderThreadFrameTime);
                f.GpuMs = Timed(_timings[0].gpuFrameTime);
                f.PresentWaitMs = Timed(_timings[0].cpuMainThreadPresentWaitTime);
            }
            if (driver.Frames > 0)
            {
                var shown = driver.Presented;
                f.Song = shown.Song;
                f.LeadMs = 1000 * driver.Times.Lead;
                f.Phase = shown.State.Phase;
                f.Frozen = shown.State.Frozen;
                f.Alive = driver.Sim.State.EnemiesAlive;
                shown.State.Hero.PositionAt(shown.Song, out f.HeroX, out f.HeroY);
            }
            Take(f.Counts);
            Row(f, _before, _row);
            Array.Copy(_counts, _before, CountCount);
            int n = _row.Length;
            _row.CopyTo(0, _chars, 0, n);
            _out.Write(_chars, 0, n);
            if (f.RealS >= _flushedAt + FlushS)
            {
                _flushedAt = f.RealS;
                _out.Flush();
            }
        }

        /// <summary>A frame timing's measure, or -1 for none: Unity leaves 0 where it timed nothing (a batch-mode player's frames).</summary>
        static double Timed(double ms) => ms > 0 ? ms : -1;

        void Take(long[] c)
        {
            c[(int)Count.Events] = _driver.Sim.EventCount;
            c[(int)Count.Kills] = _telemetry.Kills;
            c[(int)Count.Entries] = _telemetry.Entries;
            c[(int)Count.Late] = _driver.LateEntries;
            c[(int)Count.Keys] = _driver.Sim.Keys.Length; // the player's, the bot's and --walk's
            c[(int)Count.Sounds] = _sounds?.Scheduled ?? 0;
            c[(int)Count.Dropped] = _sounds?.Dropped ?? 0;
            c[(int)Count.Cut] = _sounds?.Stolen ?? 0;
            c[(int)Count.Effects] = _effects?.Spawned ?? 0;
            c[(int)Count.Rejected] = _effects?.System.RejectedTotal ?? 0;
            c[(int)Count.Gc] = GC.CollectionCount(0) - _collections;
            c[(int)Count.Faults] = _driver.Events.Faults;
            c[(int)Count.Freezes] = _telemetry.Freezes;
            c[(int)Count.Stalls] = _music != null ? _music.Status.Stalls : 0;
        }

        /// <summary>
        /// The frame's CSV row (<see cref="Header"/>, with its line end) into <paramref name="row"/>, without allocating:
        /// each count is the frame's share, its <see cref="Frame.Counts"/> less <paramref name="before"/>, the previous frame's.
        /// </summary>
        public static void Row(in Frame f, long[] before, StringBuilder row)
        {
            row.Clear();
            CsvText.Int(row, f.Index).Append(',');
            row.Append(f.Stepped ? '1' : '0').Append(',');
            CsvText.Fixed(row, f.RealS, 4).Append(',');
            CsvText.Fixed(row, f.IntervalMs, 3).Append(',');
            CsvText.Fixed(row, f.CpuMs, 3).Append(',');
            CsvText.Fixed(row, f.LoopMs, 3).Append(',');
            CsvText.Fixed(row, f.StepMs, 3).Append(',');
            CsvText.Fixed(row, f.SimMs, 3).Append(',');
            CsvText.Fixed(row, f.RenderMs, 3).Append(',');
            CsvText.Fixed(row, f.GpuMs, 3).Append(',');
            CsvText.Fixed(row, f.PresentWaitMs, 3).Append(',');
            CsvText.Fixed(row, f.Song, 4).Append(',');
            CsvText.Fixed(row, f.LeadMs, 2).Append(',');
            row.Append(Phases[(int)f.Phase]).Append(',');
            row.Append(f.Frozen ? '1' : '0').Append(',');
            CsvText.Int(row, f.Alive).Append(',');
            CsvText.Fixed(row, f.HeroX, 3).Append(',');
            CsvText.Fixed(row, f.HeroY, 3);
            for (int i = 0; i < CountCount; i++) CsvText.Int(row.Append(','), f.Counts[i] - before[i]);
            row.Append('\n');
        }

        static string MakeHeader()
        {
            var sb = new StringBuilder("frame,stepped,real_s,interval_ms,cpu_ms,loop_ms,step_ms,sim_ms,render_ms,gpu_ms,present_wait_ms," +
                                       "frame_song,lead_ms,phase,frozen,alive,hero_x,hero_y");
            foreach (Count c in Enum.GetValues(typeof(Count))) sb.Append(',').Append(c.ToString().ToLowerInvariant());
            return sb.ToString();
        }

        /// <summary>Closes the log, flushing its last rows; a failure to is only logged.</summary>
        public void Dispose()
        {
            var o = _out;
            _out = null;
            try
            {
                o?.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TimingLog: couldn't close the timing log: {e.Message}");
            }
        }
    }
}
