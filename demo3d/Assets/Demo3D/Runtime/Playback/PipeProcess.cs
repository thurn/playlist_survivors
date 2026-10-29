using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Starts a process with a pipe to its standard input, through Win32 directly. IL2CPP's
    /// System.Diagnostics.Process fails to start a child with redirected input on Windows
    /// (Win32Exception, "Native error= Success"; TD4), and the capture tool must stream frames
    /// into ffmpeg.
    /// </summary>
    public sealed class PipeProcess : IDisposable
    {
        IntPtr _process, _write;

        /// <summary>Writes all of a buffer to the child's input (WriteFile, which blocks while the pipe is full).</summary>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_write == IntPtr.Zero) throw new ObjectDisposedException("PipeProcess");
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr p = handle.AddrOfPinnedObject();
                while (count > 0)
                {
                    if (!WriteFile(_write, IntPtr.Add(p, offset), count, out int written, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "PipeProcess: write failed");
                    offset += written;
                    count -= written;
                }
            }
            finally { handle.Free(); }
        }

        public static PipeProcess Start(string exe, string args)
        {
            // Neither end inheritable, then only the child's read end: if the child inherited our
            // write end too, closing ours would never give it end of input.
            var sa = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = false };
            if (!CreatePipe(out var read, out var write, ref sa, 1 << 20)) throw new Win32Exception();
            if (!SetHandleInformation(read, 1 /* HANDLE_FLAG_INHERIT */, 1)) throw new Win32Exception();
            var si = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                dwFlags = 0x100, // STARTF_USESTDHANDLES
                hStdInput = read,
                hStdOutput = GetStdHandle(-11),
                hStdError = GetStdHandle(-12),
            };
            string cmd = "\"" + exe + "\" " + args;
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, true, 0x08000000 /* CREATE_NO_WINDOW */,
                    IntPtr.Zero, null, ref si, out var pi))
            {
                int err = Marshal.GetLastWin32Error();
                CloseHandle(read);
                CloseHandle(write);
                throw new Win32Exception(err, "PipeProcess: CreateProcess failed for " + exe);
            }
            CloseHandle(read);
            CloseHandle(pi.hThread);
            return new PipeProcess { _process = pi.hProcess, _write = write };
        }

        /// <summary>Closes the input and waits for the process to exit; returns its exit code.</summary>
        public int CloseAndWait(int timeoutMs)
        {
            if (_write != IntPtr.Zero) CloseHandle(_write); // the child sees end of input
            _write = IntPtr.Zero;
            if (_process == IntPtr.Zero) return -1;
            uint wait = WaitForSingleObject(_process, (uint)timeoutMs);
            GetExitCodeProcess(_process, out uint code);
            if (wait != 0) UnityEngine.Debug.LogError($"PipeProcess: wait returned {wait:X} (error {Marshal.GetLastWin32Error()})");
            return (int)code;
        }

        public void Dispose()
        {
            CloseAndWait(120000);
            if (_process != IntPtr.Zero) CloseHandle(_process);
            _process = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes sa, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
            IntPtr env, string dir, ref StartupInfo si, out ProcessInformation pi);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr handle, IntPtr buffer, int count, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("kernel32.dll")]
        static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    }
}
