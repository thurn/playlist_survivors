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
    /// collections since the previous frame. Rows are written without allocating, so the profile
    /// doesn't make the garbage it counts.
    /// </summary>
    public sealed class FrameProfile : IDisposable
    {
        public enum Part { Sim, Events, Crowd, Shatter, Effects, Weapons, Pickups, Structures, Physics, Hero, Look, Hud, Sound, Loop, Engine }

        public const string Header = "frame,frame_song,alive,frame_ms,wait_ms,probe_ms,sim_ms,events_ms,crowd_ms,shatter_ms,effects_ms," +
                                     "weapons_ms,pickups_ms,structures_ms,physics_ms,hero_ms,look_ms,hud_ms,sound_ms,loop_ms,engine_ms,alloc_bytes,gc";

        static readonly int PartCount = Enum.GetValues(typeof(Part)).Length;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>One frame's measures, in ms where timed; <see cref="Row"/> makes its CSV row.</summary>
        public struct Frame
        {
            public int Index, Alive, Collections;
            public double Song;
            /// <summary>The whole frame, GameLoop's Update in it, and the waits and probe draw inside that.</summary>
            public double TotalMs, LoopMs, WaitMs, ProbeMs;
            /// <summary>The timed parts by <see cref="Part"/>; <see cref="Row"/> works out Loop and Engine.</summary>
            public double[] Parts;
            public long Allocated;
        }

        readonly SimDriver _driver;
        readonly StreamWriter _out;
        readonly double[] _parts = new double[PartCount];
        readonly StringBuilder _row = new StringBuilder(512);
        readonly char[] _chars = new char[512];
        readonly PlayerLoopSystem.UpdateFunction _begin, _end;
        long _start, _loopTicks, _waitTicks, _probeTicks, _allocated, _probeStart, _probeAllocStart, _probeAllocated;
        int _collections, _framesSeen;
        bool _closed;

        public FrameProfile(string path, SimDriver driver)
        {
            _driver = driver;
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
        public void Loop(long ticks) => _loopTicks += ticks;

        /// <summary>The frame rendered the music or waited on its analysis for <paramref name="ticks"/>.</summary>
        public void Wait(long ticks) => _waitTicks += ticks;

        /// <summary>Around the frame probe's work (<see cref="GpuProbe.Frame"/>), whose time and allocation the frame leaves out.</summary>
        public void BeginProbe()
        {
            _probeStart = Stopwatch.GetTimestamp();
            _probeAllocStart = AllocatedBytes.Current;
        }

        public void EndProbe()
        {
            _probeTicks += Stopwatch.GetTimestamp() - _probeStart;
            _probeAllocated += AllocatedBytes.Current - _probeAllocStart;
        }

        void BeginFrame()
        {
            if (_closed) return;
            _loopTicks = _waitTicks = _probeTicks = _probeAllocated = 0;
            _driver.Events.ResetTimes();
            _allocated = AllocatedBytes.Current;
            _start = Stopwatch.GetTimestamp();
        }

        void EndFrame()
        {
            long total = Stopwatch.GetTimestamp() - _start;
            long allocated = AllocatedBytes.Current - _allocated - _probeAllocated;
            int collections = GC.CollectionCount(0);
            // Only frames that stepped the sim: before the music's anchor there is nothing to show. Closed,
            // the hooks still run to the end of the frame (the player loop changes from the next).
            if (_closed || _driver.Frames == _framesSeen || _start == 0) return;
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
            Int(row, f.Index).Append(',');
            Fixed(row, f.Song, 4).Append(',');
            Int(row, f.Alive).Append(',');
            Fixed(row, frame, 4).Append(',');
            Fixed(row, f.WaitMs, 4).Append(',');
            Fixed(row, f.ProbeMs, 4);
            for (int i = 0; i < f.Parts.Length; i++) Fixed(row.Append(','), f.Parts[i], 4);
            Int(row.Append(','), f.Allocated).Append(',');
            Int(row, f.Collections).Append('\n');
        }

        static StringBuilder Int(StringBuilder sb, long v)
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

        static StringBuilder Fixed(StringBuilder sb, double v, int decimals)
        {
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
}
