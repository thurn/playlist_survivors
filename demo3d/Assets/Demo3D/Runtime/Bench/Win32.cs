using System;
using System.Runtime.InteropServices;
using System.Text;
using AOT;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// The Windows and NVML calls the benchmark player makes. None needs administrator rights.
    /// Every wrapper returns null or false rather than throwing when a call isn't available.
    /// </summary>
    public static class Win32
    {
        // kernel32

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition,
                                         uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr h, byte[] buffer, uint count, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        [DllImport("kernel32.dll")]
        static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[] buffer, uint size);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceCounter(out long count);

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceFrequency(out long frequency);

        /// <summary>QueryPerformanceCounter, FrameTimingManager's clock on Windows. (Mono's Stopwatch
        /// counts from another epoch; IL2CPP's matches.)</summary>
        public static long Qpc()
        {
            QueryPerformanceCounter(out var c);
            return c;
        }

        public static long QpcFrequency()
        {
            QueryPerformanceFrequency(out var f);
            return f;
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr p);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder name, ref uint size);

        /// <summary>A process's executable path, or null (PROCESS_QUERY_LIMITED_INFORMATION suffices).</summary>
        public static string ProcessImage(int pid)
        {
            var h = OpenProcess(0x1000, false, (uint)pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        public static readonly IntPtr InvalidHandle = new IntPtr(-1);
        const int ErrorSharingViolation = 32;

        /// <summary>
        /// Opens a file for exclusive access (no sharing): the machine lock. Returns the handle, or
        /// InvalidHandle with <paramref name="held"/> set when another process has it open.
        /// Windows closes the handle, and so releases the lock, when the process dies.
        /// </summary>
        public static IntPtr OpenExclusive(string path, out bool held, out int error)
        {
            const uint GenericReadWrite = 0xC0000000, OpenAlways = 4, Normal = 0x80;
            var h = CreateFileW(path, GenericReadWrite, 0, IntPtr.Zero, OpenAlways, Normal, IntPtr.Zero);
            error = h == InvalidHandle ? Marshal.GetLastWin32Error() : 0;
            held = error == ErrorSharingViolation;
            return h;
        }

        public static void Write(IntPtr h, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            WriteFile(h, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
        }

        /// <summary>System idle, kernel (including idle) and user time, plus our own process's
        /// kernel + user time, all in 100 ns units.</summary>
        public static bool CpuTimes(out long idle, out long kernel, out long user, out long own)
        {
            own = 0;
            if (!GetSystemTimes(out idle, out kernel, out user)) return false;
            if (!GetProcessTimes(GetCurrentProcess(), out _, out _, out var k, out var u)) return false;
            own = k + u;
            return true;
        }

        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;

        /// <summary>Holds the display (and the system) on from the calling thread until released.</summary>
        public static bool HoldDisplay() => SetThreadExecutionState(EsContinuous | EsDisplayRequired | EsSystemRequired) != 0;

        public static void ReleaseDisplay() => SetThreadExecutionState(EsContinuous);

        public static byte[] RawSmbios()
        {
            const uint Rsmb = 0x52534D42; // 'RSMB'
            uint size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
            if (size == 0) return null;
            var buf = new byte[size];
            return GetSystemFirmwareTable(Rsmb, 0, buf, size) == size ? buf : null;
        }

        // powrprof

        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting,
                                                 byte[] buffer, ref uint size);

        public static bool ActivePowerScheme(out Guid scheme, out string name)
        {
            scheme = Guid.Empty;
            name = null;
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return false;
            scheme = Marshal.PtrToStructure<Guid>(p);
            LocalFree(p);
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size > 0)
            {
                var buf = new byte[size];
                if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0)
                    name = Encoding.Unicode.GetString(buf).TrimEnd('\0');
            }
            return true;
        }

        // The console display's power state, from GUID_CONSOLE_DISPLAY_STATE notifications.

        [StructLayout(LayoutKind.Sequential)]
        struct DeviceNotifySubscribeParameters
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        delegate uint DeviceNotifyCallback(IntPtr context, uint type, IntPtr setting);

        [DllImport("powrprof.dll")]
        static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags,
                                                            ref DeviceNotifySubscribeParameters recipient, out IntPtr handle);

        static readonly Guid ConsoleDisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        static readonly DeviceNotifyCallback DisplayCallbackDelegate = DisplayCallback;
        static IntPtr _displayRegistration;
        static volatile int _displayState = -1;

        /// <summary>-1 unknown, 0 off, 1 on, 2 dimmed. Registration reports the current state at once.</summary>
        public static int DisplayState => _displayState;

        public static bool WatchDisplay()
        {
            if (_displayRegistration != IntPtr.Zero) return true;
            var guid = ConsoleDisplayState;
            var p = new DeviceNotifySubscribeParameters
            {
                Callback = Marshal.GetFunctionPointerForDelegate(DisplayCallbackDelegate),
                Context = IntPtr.Zero,
            };
            const uint DeviceNotifyCallbackFlag = 2;
            return PowerSettingRegisterNotification(ref guid, DeviceNotifyCallbackFlag, ref p, out _displayRegistration) == 0;
        }

        [MonoPInvokeCallback(typeof(DeviceNotifyCallback))]
        static uint DisplayCallback(IntPtr context, uint type, IntPtr setting)
        {
            const uint PbtPowerSettingChange = 0x8013;
            if (type == PbtPowerSettingChange && setting != IntPtr.Zero)
            {
                // POWERBROADCAST_SETTING: GUID (16 bytes), DWORD DataLength, then the data.
                int len = Marshal.ReadInt32(setting, 16);
                if (len >= 4) _displayState = Marshal.ReadInt32(setting, 20);
            }
            return 0;
        }

        // user32

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

        [DllImport("user32.dll")]
        static extern bool SwitchDesktop(IntPtr desktop);

        [DllImport("user32.dll")]
        static extern bool CloseDesktop(IntPtr desktop);

        /// <summary>
        /// Whether the interactive desktop is locked (or on the secure desktop): then the input
        /// desktop can't be opened or switched to.
        /// </summary>
        public static bool DesktopLocked()
        {
            const uint DesktopSwitchDesktop = 0x0100;
            var d = OpenInputDesktop(0, false, DesktopSwitchDesktop);
            if (d == IntPtr.Zero) return true;
            bool ok = SwitchDesktop(d);
            CloseDesktop(d);
            return !ok;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr h);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        delegate bool EnumWindowsProc(IntPtr h, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr h, uint cmd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out Rect r);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr h, StringBuilder name, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr h, StringBuilder text, int max);

        [DllImport("user32.dll")]
        static extern int GetWindowLongW(IntPtr h, int index);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr h, uint attr, out int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct MouseInput
        {
            // INPUT on x64: a DWORD type, then the union aligned to 8 bytes (MOUSEINPUT): 40 bytes.
            public uint Type;
            public uint Pad;
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public IntPtr Extra;
        }

        [DllImport("user32.dll")]
        static extern uint SendInput(uint count, MouseInput[] inputs, int size);

        static IntPtr _found;
        static uint _findPid;
        static readonly EnumWindowsProc FindProc = FindWindowOfProcess;

        [MonoPInvokeCallback(typeof(EnumWindowsProc))]
        static bool FindWindowOfProcess(IntPtr h, IntPtr lParam)
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid != _findPid || !IsWindowVisible(h)) return true;
            if (ClassName(h) != "UnityWndClass") return true;
            _found = h;
            return false;
        }

        /// <summary>The player's own top-level window.</summary>
        public static IntPtr OwnWindow()
        {
            _found = IntPtr.Zero;
            _findPid = GetCurrentProcessId();
            EnumWindows(FindProc, IntPtr.Zero);
            return _found;
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassNameW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string Title(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool WindowRect(IntPtr h, out Rect r) => GetWindowRect(h, out r);

        /// <summary>
        /// Wakes the display with a zero-sum mouse nudge (input wakes a sleeping display; the
        /// execution state alone only keeps it from sleeping).
        /// </summary>
        public static void NudgeInput()
        {
            const uint InputMouse = 0, MouseMove = 0x1;
            var inputs = new[]
            {
                new MouseInput { Type = InputMouse, Dx = 1, Dy = 0, Flags = MouseMove },
                new MouseInput { Type = InputMouse, Dx = -1, Dy = 0, Flags = MouseMove },
            };
            SendInput(2, inputs, Marshal.SizeOf<MouseInput>());
        }

        /// <summary>
        /// Brings the window to the foreground. Windows refuses SetForegroundWindow to a process
        /// that didn't receive the last input, so an Alt tap (synthesized input) precedes it.
        /// </summary>
        public static bool BringToForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            if (GetForegroundWindow() == h) return true;
            const byte VkMenu = 0x12;
            const uint KeyUp = 0x2;
            keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
            keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            return GetForegroundWindow() == h;
        }

        /// <summary>
        /// The first visible, uncloaked, unminimized window above ours in the z-order whose rectangle
        /// overlaps ours, or IntPtr.Zero. Unity doesn't expose its swap chain's occlusion status
        /// (DXGI_STATUS_OCCLUDED), so the z-order stands in for it. Click-through overlays
        /// (WS_EX_TRANSPARENT) and zero-area windows don't count.
        /// </summary>
        public static IntPtr Occluder(IntPtr own)
        {
            if (own == IntPtr.Zero || !GetWindowRect(own, out var me)) return IntPtr.Zero;
            const uint GwHwndPrev = 3;
            const int GwlExStyle = -20, WsExTransparent = 0x20;
            const uint DwmwaCloaked = 14;
            for (var h = GetWindow(own, GwHwndPrev); h != IntPtr.Zero; h = GetWindow(h, GwHwndPrev))
            {
                if (!IsWindowVisible(h) || IsIconic(h)) continue;
                if ((GetWindowLongW(h, GwlExStyle) & WsExTransparent) != 0) continue;
                if (DwmGetWindowAttribute(h, DwmwaCloaked, out var cloaked, 4) == 0 && cloaked != 0) continue;
                if (!GetWindowRect(h, out var r)) continue;
                if (r.Right <= r.Left || r.Bottom <= r.Top) continue;
                bool overlap = r.Left < me.Right && me.Left < r.Right && r.Top < me.Bottom && me.Top < r.Bottom;
                if (overlap) return h;
            }
            return IntPtr.Zero;
        }

        /// <summary>Whether a PIX GPU capturer is loaded (the player was launched by pixtool).</summary>
        public static bool PixCapturerLoaded => GetModuleHandleW("WinPixGpuCapturer.dll") != IntPtr.Zero;

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        delegate int PixGpuCaptureNextFramesFn(string fileName, uint frames);

        /// <summary>
        /// PIX's programmatic capture API: pix3.h's PIXGpuCaptureNextFrames, which calls
        /// WinPixGpuCapturer.dll's CaptureNextFrame export. Returns the HRESULT, or null without a capturer.
        /// </summary>
        public static int? PixCaptureNextFrames(string file, uint frames)
        {
            var m = GetModuleHandleW("WinPixGpuCapturer.dll");
            if (m == IntPtr.Zero) return null;
            var f = GetProcAddress(m, "CaptureNextFrame");
            if (f == IntPtr.Zero) return null;
            var fn = Marshal.GetDelegateForFunctionPointer<PixGpuCaptureNextFramesFn>(f);
            return fn(file, frames);
        }
    }

    /// <summary>NVML (nvml.dll in System32, the library nvidia-smi uses): temperature, clock event
    /// (throttle) reasons, driver version.</summary>
    public static class Nvml
    {
        [DllImport("nvml.dll")]
        static extern int nvmlInit_v2();

        [DllImport("nvml.dll")]
        static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll")]
        static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);

        [DllImport("nvml.dll")]
        static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

        [DllImport("nvml.dll")]
        static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);

        [DllImport("nvml.dll")]
        static extern int nvmlSystemGetDriverVersion(byte[] version, uint length);

        [DllImport("nvml.dll")]
        static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        static IntPtr _device;
        static bool _tried, _ok;
        static readonly object Gate = new object();

        public static bool Available
        {
            get
            {
                lock (Gate)
                {
                    if (_tried) return _ok;
                    _tried = true;
                    try
                    {
                        _ok = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out _device) == 0;
                    }
                    catch (Exception)
                    {
                        _ok = false; // no nvml.dll: not an NVIDIA machine
                    }
                    return _ok;
                }
            }
        }

        public static uint? TemperatureC()
        {
            if (!Available) return null;
            lock (Gate) return nvmlDeviceGetTemperature(_device, 0, out var t) == 0 ? t : (uint?)null;
        }

        public static ulong? ThrottleReasons()
        {
            if (!Available) return null;
            lock (Gate) return nvmlDeviceGetCurrentClocksThrottleReasons(_device, out var r) == 0 ? r : (ulong?)null;
        }

        public static double? PowerW()
        {
            if (!Available) return null;
            lock (Gate) return nvmlDeviceGetPowerUsage(_device, out var mw) == 0 ? mw / 1000.0 : (double?)null;
        }

        public static string DriverVersion()
        {
            if (!Available) return null;
            var buf = new byte[96];
            lock (Gate) return nvmlSystemGetDriverVersion(buf, (uint)buf.Length) == 0 ? CString(buf) : null;
        }

        public static string Name()
        {
            if (!Available) return null;
            var buf = new byte[96];
            lock (Gate) return nvmlDeviceGetName(_device, buf, (uint)buf.Length) == 0 ? CString(buf) : null;
        }

        static string CString(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            return Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }
    }
}
