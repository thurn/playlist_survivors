using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Game.Client.Music
{
    /// <summary>
    /// The music plugin's C interface (native/crates/plugin/include/ps_plugin.h, the contract), for
    /// both scripting backends. Players bind it with DllImport; the editor binds a copy named after
    /// its build hash through <see cref="PluginLoader"/>, so a rebuilt plugin loads after a script
    /// reload instead of an editor restart (unity-game.md, Building and Loading the Plugin).
    ///
    /// Every struct is blittable and every array argument pins a caller-owned buffer, so polling
    /// and rendering never allocate. <see cref="RenderMusic"/> is Unity's audio thread's; every
    /// other call belongs to the main thread.
    /// </summary>
    public static class Plugin
    {
        public const int Ok = 0;
        public const int ErrFailed = -6;
        public const int SourceFile = 0;
        public const int Waiting = 0, Playing = 1, Ended = 2;
        public const int KindBeat = 1;

        /// <summary>ps_config. Paths are UTF-8 strings the caller allocates (<see cref="Utf8"/>).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Config
        {
            public int Source;
            public double M;
            public double OutputRate; // whole Hz: AudioSettings.outputSampleRate
            public IntPtr Path;
            public IntPtr Models;     // zero: $PS_MODELS, else the plugin's checkout
            public IntPtr SessionDir; // zero: no record
        }

        /// <summary>ps_entry: 120 bytes. <see cref="Stamp"/> is the C struct's known_at.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Entry
        {
            public int Kind;
            public double T;
            public double Stamp;
            public double V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11;
        }

        /// <summary>struct ps_status: 72 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Status
        {
            public double AnchorDsp;
            public double AnchorSong;
            public long AnchorFrames;
            public double OutputLatency; // the device's, without Unity's DSP buffering
            public double Buffered;
            public double Analysed;
            public int Stalls;
            public int State;
            public int Muted;
            public int AnalysisDone;
            public int Failed;
            public float FollowerGain;
        }

        public static int Open(ref Config config, out IntPtr session) => Fns.Open(ref config, out session);
        public static int Poll(IntPtr session, Entry[] into) => Fns.Poll(session, into, into.Length);
        public static int RenderMusic(IntPtr session, float[] interleaved, int frames, int channels, double dspTime) =>
            Fns.Render(session, interleaved, frames, channels, dspTime);
        public static void GetStatus(IntPtr session, out Status status) => Fns.GetStatus(session, out status);
        public static void Close(IntPtr session) => Fns.Close(session);

        /// <summary>The calling thread's last failure (the plugin owns the string; it is copied).</summary>
        public static string LastError()
        {
            IntPtr p = Fns.LastError();
            if (p == IntPtr.Zero) return "";
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n];
            Marshal.Copy(p, bytes, 0, n);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>A NUL-terminated UTF-8 copy of `s` in unmanaged memory (zero for null); free it with Marshal.FreeHGlobal.</summary>
        public static IntPtr Utf8(string s)
        {
            if (s == null) return IntPtr.Zero;
            var bytes = Encoding.UTF8.GetBytes(s);
            IntPtr p = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            Marshal.WriteByte(p, bytes.Length, 0);
            return p;
        }

        // The function signatures, shared by both bindings.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int OpenFn(ref Config config, out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PollFn(IntPtr session, [Out] Entry[] into, int max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RenderFn(IntPtr session, [In, Out] float[] interleaved, int frames, int channels, double dspTime);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void StatusFn(IntPtr session, out Status status);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CloseFn(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr LastErrorFn();

        /// <summary>One binding of the plugin's functions.</summary>
        public sealed class Functions
        {
            public OpenFn Open;
            public PollFn Poll;
            public RenderFn Render;
            public StatusFn GetStatus;
            public CloseFn Close;
            public LastErrorFn LastError;
        }

#if UNITY_EDITOR
        static Functions Fns => PluginLoader.Current;
#else
        const string Lib = "ps_plugin";
        [DllImport(Lib, EntryPoint = "ps_open")] static extern int ps_open(ref Config config, out IntPtr session);
        [DllImport(Lib, EntryPoint = "ps_poll")] static extern int ps_poll(IntPtr session, [Out] Entry[] into, int max);
        [DllImport(Lib, EntryPoint = "ps_render_music")] static extern int ps_render_music(IntPtr session, [In, Out] float[] interleaved, int frames, int channels, double dspTime);
        [DllImport(Lib, EntryPoint = "ps_status")] static extern void ps_status(IntPtr session, out Status status);
        [DllImport(Lib, EntryPoint = "ps_close")] static extern void ps_close(IntPtr session);
        [DllImport(Lib, EntryPoint = "ps_last_error")] static extern IntPtr ps_last_error();

        // IL2CPP needs static methods for DllImport; the delegates wrap them once.
        static readonly Functions Fns = new Functions
        {
            Open = ps_open, Poll = ps_poll, Render = ps_render_music,
            GetStatus = ps_status, Close = ps_close, LastError = ps_last_error,
        };
#endif
    }
}
