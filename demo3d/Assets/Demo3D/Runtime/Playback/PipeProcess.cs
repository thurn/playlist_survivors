using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Starts a process with a pipe to its standard input, through the OS directly: Win32 on
    /// Windows, libc's popen elsewhere. IL2CPP's System.Diagnostics.Process fails to start a child
    /// with redirected input on both (Win32Exception, "Native error= Success"; TD4 on Windows, the
    /// first trailer capture on the Mac), and the capture tool must stream frames into ffmpeg.
    /// </summary>
    public sealed class PipeProcess : IDisposable
    {
        static bool Windows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        IntPtr _process, _write;
        IntPtr _stream; // popen's FILE*

        /// <summary>Writes all of a buffer to the child's input (WriteFile, which blocks while the pipe is full).</summary>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (_write == IntPtr.Zero && _stream == IntPtr.Zero) throw new ObjectDisposedException("PipeProcess");
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr p = handle.AddrOfPinnedObject();
                while (count > 0)
                {
                    int written;
                    if (_stream != IntPtr.Zero)
                    {
                        written = (int)fwrite(IntPtr.Add(p, offset), (UIntPtr)1, (UIntPtr)count, _stream);
                        if (written == 0) throw new IOException($"PipeProcess: write failed (errno {Marshal.GetLastWin32Error()}): the child has exited?");
                    }
                    else if (!WriteFile(_write, IntPtr.Add(p, offset), count, out written, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "PipeProcess: write failed");
                    offset += written;
                    count -= written;
                }
            }
            finally { handle.Free(); }
        }

        public static PipeProcess Start(string exe, string args)
        {
            if (!Windows)
            {
                // A child that dies would otherwise kill the player with SIGPIPE on the next write;
                // ignored, the write fails and says so.
                signal(13 /* SIGPIPE */, (IntPtr)1 /* SIG_IGN */);
                // popen runs the line through /bin/sh, which parses args' double quotes.
                var stream = popen("'" + exe.Replace("'", "'\\''") + "' " + args, "w");
                if (stream == IntPtr.Zero)
                    throw new IOException($"PipeProcess: popen failed for {exe} (errno {Marshal.GetLastWin32Error()})");
                return new PipeProcess { _stream = stream };
            }
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

        /// <summary>
        /// Closes the input and waits for the process to exit; returns its exit code (-1 if it
        /// didn't exit normally). Off Windows pclose waits with no time limit.
        /// </summary>
        public int CloseAndWait(int timeoutMs)
        {
            if (_stream != IntPtr.Zero)
            {
                int status = pclose(_stream); // the child sees end of input; pclose reaps it
                _stream = IntPtr.Zero;
                return status != -1 && (status & 0x7f) == 0 ? (status >> 8) & 0xff : -1;
            }
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
            if (_stream != IntPtr.Zero)
            {
                CloseAndWait(120000);
                return;
            }
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

        const string Libc = "/usr/lib/libSystem.B.dylib"; // the Mac's libc (Linux players don't exist)

        [DllImport(Libc, SetLastError = true)]
        static extern IntPtr popen(string command, string mode);

        [DllImport(Libc)]
        static extern int pclose(IntPtr stream);

        [DllImport(Libc, SetLastError = true)]
        static extern UIntPtr fwrite(IntPtr buffer, UIntPtr size, UIntPtr count, IntPtr stream);

        [DllImport(Libc)]
        static extern IntPtr signal(int sig, IntPtr handler);

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
