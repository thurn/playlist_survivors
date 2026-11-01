using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Game.Client.Crowd;
using Game.Client.Effects;
using Game.Client.Hero;
using Game.Client.Hud;
using Game.Client.Look;
using Game.Client.Pickups;
using Game.Client.Sound;
using Game.Client.Structures;
using Game.Client.Weapons;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Client.Loop
{
    /// <summary>
    /// `--profile-out FILE.csv`: the main thread's work in each frame of a run, split by part, with the
    /// frame's managed allocation and garbage collections. A frame is timed from just after Unity's
    /// time update (past any frame pacing) to the end of its player loop. Its parts: the sim's step,
    /// the events' dispatch to every presenter, each presenter's Present grouped by what it draws (the
    /// crowd without its GPU shatter, which is its own part, and the structures without their physics
    /// steps, likewise), the rest of <see cref="GameLoop"/>'s frame (`loop`: input, history, the other
    /// presenters), and `engine`, everything else on the main thread (Unity's player loop, other
    /// scripts' updates, and in a window the frame's rendering).
    ///
    /// Two things a real-time frame doesn't do are timed apart and left out of `frame_ms`: `wait_ms`,
    /// a fixed-time or replayed frame rendering its music and waiting on the plugin's analysis (live,
    /// the audio thread renders and the frame never waits), so a 30-minute run measured in 4 minutes
    /// reports the frames' own work; and `probe_ms`, the frame probe's hand-drawn camera and its file
    /// (<see cref="GpuProbe"/>), whose allocation is left out too. `alloc_bytes` is what the main thread allocated in the frame (IL2CPP
    /// lacks GC.GetAllocatedBytesForCurrentThread, so a native allocation hook counts there); `gc` the
    /// collections since the previous frame; `cpu_ms` the main thread's CPU time in the frame, its wait and probe left
    /// out, which falls short of `frame_ms` when the OS gave the thread's time to others. Rows are written without allocating, so the profile
    /// doesn't make the garbage it counts. Beside it, `FILE-alloc.csv`: the main thread's allocations from
    /// the first profiled frame to the end, by part of the frame and type (<see cref="AllocationCensus"/>).
    /// </summary>
    public sealed class FrameProfile : IDisposable
    {
        public enum Part { Sim, Events, Crowd, Shatter, Effects, Weapons, Pickups, Structures, Physics, Hero, Look, Hud, Sound, Loop, Engine }

        public const string Header = "frame,frame_song,alive,frame_ms,wait_ms,probe_ms,sim_ms,events_ms,crowd_ms,shatter_ms,effects_ms," +
                                     "weapons_ms,pickups_ms,structures_ms,physics_ms,hero_ms,look_ms,hud_ms,sound_ms,loop_ms,engine_ms,alloc_bytes,gc,cpu_ms";

        static readonly int PartCount = Enum.GetValues(typeof(Part)).Length;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>One frame's measures, in ms where timed; <see cref="Row"/> makes its CSV row.</summary>
        public struct Frame
        {
            public int Index, Alive, Collections;
            public double Song;
            /// <summary>The whole frame, GameLoop's Update in it, and the waits and probe draw inside that.</summary>
            public double TotalMs, LoopMs, WaitMs, ProbeMs;
            /// <summary>The main thread's CPU time in the frame, its waits and probe draw left out; negative where unknown.</summary>
            public double CpuMs;
            /// <summary>The timed parts by <see cref="Part"/>; <see cref="Row"/> works out Loop and Engine.</summary>
            public double[] Parts;
            public long Allocated;
        }

        readonly SimDriver _driver;
        readonly string _path;
        readonly StreamWriter _out;
        readonly double[] _parts = new double[PartCount];
        readonly StringBuilder _row = new StringBuilder(512);
        readonly char[] _chars = new char[512];
        readonly PlayerLoopSystem.UpdateFunction _begin, _end;
        long _start, _loopTicks, _waitTicks, _probeTicks, _allocated, _probeStart, _probeAllocStart, _probeAllocated;
        long _cpuStart, _waitStart, _waitCpuStart, _probeCpuStart, _asideCpu;
        int _collections, _framesSeen;
        bool _closed;

        public FrameProfile(string path, SimDriver driver)
        {
            _driver = driver;
            _path = path;
            _out = new StreamWriter(path);
            _out.WriteLine(Header);
            driver.Events.Timed = true;
            _framesSeen = driver.Frames;
            _collections = GC.CollectionCount(0);
            AllocatedBytes.Start();
            _begin = BeginFrame;
            _end = EndFrame;
            Hook(true);
        }

        /// <summary>GameLoop's Update took <paramref name="ticks"/> (Stopwatch ticks).</summary>
        public void Loop(long ticks)
        {
            _loopTicks += ticks;
            AllocationCensus.Tag(AllocationCensus.Engine);
        }

        /// <summary>Around the frame rendering its music or waiting on its analysis, which the frame leaves out.</summary>
        public void BeginWait()
        {
            _waitStart = Stopwatch.GetTimestamp();
            _waitCpuStart = ThreadCpu.Ns;
        }

        public void EndWait()
        {
            _waitTicks += Stopwatch.GetTimestamp() - _waitStart;
            _asideCpu += ThreadCpu.Ns - _waitCpuStart;
        }

        /// <summary>Around the frame probe's work (<see cref="GpuProbe.Frame"/>), whose time and allocation the frame leaves out.</summary>
        public void BeginProbe()
        {
            _probeStart = Stopwatch.GetTimestamp();
            _probeAllocStart = AllocatedBytes.Current;
            _probeCpuStart = ThreadCpu.Ns;
            AllocationCensus.Tag(AllocationCensus.Probe);
        }

        public void EndProbe()
        {
            _probeTicks += Stopwatch.GetTimestamp() - _probeStart;
            _probeAllocated += AllocatedBytes.Current - _probeAllocStart;
            _asideCpu += ThreadCpu.Ns - _probeCpuStart;
            AllocationCensus.Tag(AllocationCensus.Loop);
        }

        void BeginFrame()
        {
            if (_closed) return;
            _loopTicks = _waitTicks = _probeTicks = _probeAllocated = _asideCpu = 0;
            _driver.Events.ResetTimes();
            _allocated = AllocatedBytes.Current;
            _cpuStart = ThreadCpu.Ns;
            _start = Stopwatch.GetTimestamp();
        }

        void EndFrame()
        {
            long total = Stopwatch.GetTimestamp() - _start;
            long cpu = ThreadCpu.Ns;
            long allocated = AllocatedBytes.Current - _allocated - _probeAllocated;
            int collections = GC.CollectionCount(0);
            // Only frames that stepped the sim: before the music's anchor there is nothing to show. Closed,
            // the hooks still run to the end of the frame (the player loop changes from the next).
            if (_closed || _driver.Frames == _framesSeen || _start == 0) return;
            if (!AllocationCensus.On) AllocationCensus.Start();
            _framesSeen = _driver.Frames;
            var f = new Frame
            {
                Index = _driver.Frames - 1,
                Song = _driver.Times.FrameSong,
                Alive = _driver.Sim.State.EnemiesAlive,
                TotalMs = total * TickMs,
                LoopMs = _loopTicks * TickMs,
                WaitMs = _waitTicks * TickMs,
                ProbeMs = _probeTicks * TickMs,
                CpuMs = cpu < 0 || _cpuStart < 0 ? -1 : (cpu - _cpuStart - _asideCpu) / 1e6,
                Allocated = allocated,
                Collections = collections - _collections,
                Parts = _parts,
            };
            _collections = collections;
            Array.Clear(f.Parts, 0, f.Parts.Length);
            f.Parts[(int)Part.Sim] = _driver.StepMs;
            var events = _driver.Events;
            f.Parts[(int)Part.Events] = events.EventTicks * TickMs;
            for (int i = 0; i < events.Count; i++)
            {
                var presenter = events[i];
                double ms = events.PresentTicks(i) * TickMs;
                // The shatter's share of the crowd's Present, and the physics steps' of the structures'.
                if (presenter is CrowdView crowd)
                {
                    f.Parts[(int)Part.Shatter] += crowd.ShatterMs;
                    ms -= crowd.ShatterMs;
                }
                else if (presenter is StructureShatter structures)
                {
                    f.Parts[(int)Part.Physics] += structures.FramePhysicsMs;
                    ms -= structures.FramePhysicsMs;
                }
                var part = PartOf(presenter);
                if (part != Part.Loop) f.Parts[(int)part] += ms; // the rest of the loop, which Row works out
            }
            Row(f, _row);
            int n = _row.Length;
            _row.CopyTo(0, _chars, 0, n);
            _out.Write(_chars, 0, n);
        }

        /// <summary>The part a presenter's Present counts in; one not listed counts in GameLoop's own (`loop`).</summary>
        public static Part PartOf(IPresenter presenter)
        {
            switch (presenter)
            {
                case CrowdView _: return Part.Crowd;
                case EffectsPresenter _: return Part.Effects;
                case WandView _: case BibleView _: case GarlicAura _: case FireTrailView _: case LightningView _: case EvolutionLayers _:
                    return Part.Weapons;
                case GemView _: case ChestView _: return Part.Pickups;
                case StructureShatter _: return Part.Structures;
                case HeroPresenter _: return Part.Hero;
                case LookPresenter _: return Part.Look;
                case HudPresenter _: case DebugOverlay _: return Part.Hud;
                case SoundPresenter _: return Part.Sound;
                default: return Part.Loop;
            }
        }

        /// <summary>
        /// The frame's CSV row (<see cref="Header"/>, with its line end) into <paramref name="row"/>, without allocating:
        /// `frame_ms` is the frame without its wait and probe, `loop_ms` GameLoop's Update without them, the sim, the
        /// events and the presenters, and `engine_ms` the frame outside GameLoop's Update.
        /// </summary>
        public static void Row(Frame f, StringBuilder row)
        {
            double frame = f.TotalMs - f.WaitMs - f.ProbeMs;
            double loop = f.LoopMs - f.WaitMs - f.ProbeMs;
            double timed = 0;
            for (int i = 0; i < f.Parts.Length; i++)
                if (i != (int)Part.Loop && i != (int)Part.Engine) timed += f.Parts[i];
            f.Parts[(int)Part.Loop] = loop - timed;
            f.Parts[(int)Part.Engine] = frame - loop;
            row.Clear();
            CsvText.Int(row, f.Index).Append(',');
            CsvText.Fixed(row, f.Song, 4).Append(',');
            CsvText.Int(row, f.Alive).Append(',');
            CsvText.Fixed(row, frame, 4).Append(',');
            CsvText.Fixed(row, f.WaitMs, 4).Append(',');
            CsvText.Fixed(row, f.ProbeMs, 4);
            for (int i = 0; i < f.Parts.Length; i++) CsvText.Fixed(row.Append(','), f.Parts[i], 4);
            CsvText.Int(row.Append(','), f.Allocated).Append(',');
            CsvText.Int(row, f.Collections).Append(',');
            CsvText.Fixed(row, f.CpuMs, 4).Append('\n');
        }

        void Hook(bool on)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var systems = new List<PlayerLoopSystem>(loop.subSystemList);
            systems.RemoveAll(s => s.type == typeof(ProfileBegin) || s.type == typeof(ProfileEnd));
            if (on)
            {
                // After the time update, which waits out a frame rate's pacing.
                systems.Insert(systems.FindIndex(s => s.type == typeof(TimeUpdate)) + 1,
                    new PlayerLoopSystem { type = typeof(ProfileBegin), updateDelegate = _begin });
                systems.Add(new PlayerLoopSystem { type = typeof(ProfileEnd), updateDelegate = _end });
            }
            loop.subSystemList = systems.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        struct ProfileBegin { }
        struct ProfileEnd { }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            Hook(false);
            var tags = new List<string> { "engine", "loop", "sim", "probe" };
            for (int i = 0; i < _driver.Events.Count; i++)
            {
                string name = _driver.Events[i].GetType().Name;
                tags.Add("Present " + name);
                tags.Add("OnEvent " + name);
            }
            AllocationCensus.Write(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path)),
                Path.GetFileNameWithoutExtension(_path) + "-alloc.csv"), tags.ToArray());
            AllocatedBytes.Stop();
            _driver.Events.Timed = false;
            _out.Dispose();
        }

        /// <summary>
        /// The managed bytes this thread has allocated: GC.GetAllocatedBytesForCurrentThread, which IL2CPP
        /// doesn't implement, so a player counts them from IL2CPP's allocation hook while started
        /// (AllocationCounter.c, native: a managed hook re-enters itself as a thread attaches).
        /// </summary>
        static class AllocatedBytes
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            [DllImport("__Internal", EntryPoint = "game_allocated_bytes")] static extern long Read();
            [DllImport("__Internal", EntryPoint = "game_allocations_start")] public static extern void Start();
            [DllImport("__Internal", EntryPoint = "game_allocations_stop")] public static extern void Stop();

            public static long Current => Read();
#else
            public static long Current => GC.GetAllocatedBytesForCurrentThread();
            public static void Start() { }
            public static void Stop() { }
#endif
        }
    }

    /// <summary>The calling thread's CPU time in ns (AllocationCounter.c), or -1 where it can't be read finely (Windows, the editor).</summary>
    static class ThreadCpu
    {
#if ENABLE_IL2CPP && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "game_thread_cpu_ns")] static extern long Read();
        public static long Ns => Read();
#else
        public static long Ns => -1;
#endif
    }

    /// <summary>
    /// The frame profile's census of the main thread's allocations by part of the frame and type
    /// (AllocationCounter.c): the loop tags each part as it runs it, from <see cref="Start"/> to <see cref="Write"/>.
    /// A tag is <see cref="Engine"/> (outside GameLoop's Update), <see cref="Loop"/>, <see cref="Sim"/> (its step),
    /// <see cref="Probe"/> (the frame probe's, which the profile's rows leave out), or a
    /// presenter's Present or OnEvent by its index in the dispatch. Only in an IL2CPP player; elsewhere nothing.
    /// </summary>
    public static class AllocationCensus
    {
        public const int Engine = 0, Loop = 1, Sim = 2, Probe = 3;

        /// <summary>Whether the census is counting (the profile started it), so tags are kept.</summary>
        public static bool On { get; private set; }

        public static int Present(int presenter) => 4 + 2 * presenter;
        public static int OnEvent(int presenter) => 5 + 2 * presenter;

        /// <summary>The part allocating from now on; returns the one before, to restore.</summary>
        public static int Tag(int tag) => On ? NativeTag(tag) : tag;

#if ENABLE_IL2CPP && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "game_census_start")] static extern void NativeStart();
        [DllImport("__Internal", EntryPoint = "game_census_tag")] static extern int NativeTag(int tag);
        [DllImport("__Internal", EntryPoint = "game_census_write")] static extern int NativeWrite(string path, string[] tags, int count);

        public static void Start()
        {
            NativeStart();
            On = true;
        }

        /// <summary>Writes the census as CSV, most bytes first (part, type, count, bytes, largest), and ends it.</summary>
        public static void Write(string path, string[] tags)
        {
            if (!On) return;
            On = false;
            if (NativeWrite(path, tags, tags.Length) != 0) UnityEngine.Debug.LogWarning($"FrameProfile: the allocation census {path} wasn't written");
        }
#else
        static int NativeTag(int tag) => tag;
        public static void Start() { }
        public static void Write(string path, string[] tags) { }
#endif
    }
}
