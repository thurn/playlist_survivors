#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Game.Client.Music
{
    /// <summary>
    /// The editor's binding of the plugin. Unity keeps a native plugin it loaded until the editor
    /// quits, so the editor never DllImports this one: it copies the built plugin
    /// (<see cref="BuiltPath"/>, which the game tools write) to Library/PsPlugin under a name
    /// carrying its content hash, dlopens that copy and resolves the functions itself. A rebuild
    /// changes the hash, so the next domain reload (which resets <see cref="Current"/>) loads the
    /// new copy beside the old, which stays mapped until the editor quits.
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>Where `game plugin` puts the built plugin, relative to the project.</summary>
        public const string BuiltPath = "Assets/Plugins/macOS/libps_plugin.dylib";
        public const string CopiesDir = "Library/PsPlugin";

        static Plugin.Functions _current;

        /// <summary>The path of the copy <see cref="Current"/> was resolved from.</summary>
        public static string LoadedPath { get; private set; }

        /// <summary>The functions of the built plugin as it is now, loaded on first use in this domain.</summary>
        public static Plugin.Functions Current => _current ?? (_current = Load(Path.GetFullPath(BuiltPath)));

        /// <summary>Loads the plugin at `built` through its hash-named copy, and makes it <see cref="Current"/>.</summary>
        public static Plugin.Functions Load(string built)
        {
            if (!File.Exists(built))
                throw new DllNotFoundException($"no plugin at {built}: run `uv run --project demo3d/tools game plugin`");
            string copy = CopyFor(built);
            IntPtr handle = dlopen(copy, RtldNow | RtldLocal);
            if (handle == IntPtr.Zero) throw new DllNotFoundException($"dlopen {copy}: {Marshal.PtrToStringAnsi(dlerror())}");
            var fns = new Plugin.Functions
            {
                Open = Resolve<Plugin.OpenFn>(handle, "ps_open"),
                ListApps = Resolve<Plugin.ListAppsFn>(handle, "ps_list_apps"),
                Poll = Resolve<Plugin.PollFn>(handle, "ps_poll"),
                Render = Resolve<Plugin.RenderFn>(handle, "ps_render_music"),
                GetStatus = Resolve<Plugin.StatusFn>(handle, "ps_status"),
                Close = Resolve<Plugin.CloseFn>(handle, "ps_close"),
                LastError = Resolve<Plugin.LastErrorFn>(handle, "ps_last_error"),
            };
            LoadedPath = copy;
            return _current = fns;
        }

        /// <summary>Library/PsPlugin/libps_plugin-&lt;first 16 hex of its SHA-256&gt;.dylib, copied if missing.</summary>
        static string CopyFor(string built)
        {
            string hash;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(built))
                hash = BitConverter.ToString(sha.ComputeHash(f), 0, 8).Replace("-", "").ToLowerInvariant();
            string dir = Path.GetFullPath(CopiesDir);
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, $"libps_plugin-{hash}.dylib");
            if (!File.Exists(copy))
            {
                string tmp = copy + ".tmp";
                File.Copy(built, tmp, true);
                File.Move(tmp, copy);
            }
            return copy;
        }

        static T Resolve<T>(IntPtr handle, string name) where T : Delegate
        {
            IntPtr p = dlsym(handle, name);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"{name}: {Marshal.PtrToStringAnsi(dlerror())}");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        const int RtldNow = 0x2, RtldLocal = 0x4;
        const string LibSystem = "/usr/lib/libSystem.B.dylib";
        [DllImport(LibSystem)] static extern IntPtr dlopen(string path, int mode);
        [DllImport(LibSystem)] static extern IntPtr dlsym(IntPtr handle, string symbol);
        [DllImport(LibSystem)] static extern IntPtr dlerror();
    }
}
#endif
