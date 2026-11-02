using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Champions.Client.UI
{
    /// <summary>
    /// The music picker's File...: the Mac's own open panel for a file or playlist, through `osascript` (AppleScript's
    /// `choose file`), run beside the game so its frames go on. <see cref="Done"/> once the panel closes; <see cref="Path"/>
    /// is the POSIX path chosen, or null when it was cancelled or couldn't open.
    /// </summary>
    public sealed class FileChooser : IDisposable
    {
        readonly Process _process;
        string _path;
        bool _read;

        FileChooser(Process process)
        {
            _process = process;
        }

        /// <summary>Opens the panel; null when it can't (no osascript).</summary>
        public static FileChooser Open()
        {
            var info = new ProcessStartInfo("/usr/bin/osascript", "-e \"POSIX path of (choose file with prompt \\\"Music\\\")\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try { return new FileChooser(Process.Start(info)); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                Debug.LogWarning("FileChooser: can't open the panel: " + e.Message);
                return null;
            }
        }

        public bool Done => _process.HasExited;

        public string Path
        {
            get
            {
                if (_read || !Done) return _path;
                _read = true;
                string output = _process.StandardOutput.ReadToEnd().Trim();
                _path = _process.ExitCode == 0 && output.Length > 0 ? output : null;
                return _path;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException) { } // gone already
            _process.Dispose();
        }
    }
}
