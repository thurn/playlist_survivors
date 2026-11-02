using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Champions.Client.Music
{
    /// <summary>
    /// What the music plays (design.md, Music > Sources): Delayed listening, an app producing audio now,
    /// tapped muted and played M late (a System source), or a local audio file or M3U playlist through
    /// the same delay and analyzer (a File source). <see cref="Tag"/> names it: the app's name or the
    /// file's.
    /// </summary>
    public sealed class MusicSource
    {
        /// <summary>How long --app waits for its app to be producing audio.</summary>
        public const double AppWait = 3;

        public readonly int Kind;    // Plugin.SourceFile or SourceSystem
        public readonly string Path; // File: absolute
        public readonly int Pid;     // System: the app to tap
        public readonly string Tag;

        MusicSource(int kind, string path, int pid, string tag)
        {
            Kind = kind;
            Path = path;
            Pid = pid;
            Tag = tag;
        }

        public bool IsApp => Kind == Plugin.SourceSystem;

        /// <summary>A File source on `path`, an absolute path to an audio file or .m3u.</summary>
        public static MusicSource File(string path) =>
            new MusicSource(Plugin.SourceFile, path, 0, System.IO.Path.GetFileName(path));

        /// <summary>Delayed listening to `app`: tapped, muted and played M late.</summary>
        public static MusicSource App(Plugin.App app) => new MusicSource(Plugin.SourceSystem, null, app.Pid, app.Name);

        /// <summary>
        /// The app `want` names among `apps`: the one it names exactly (ignoring case), else the first
        /// whose name contains it. A number is a pid: a listed app's, or any process with audio, which the
        /// plugin taps by its own pid (a tool launched from a terminal is listed under the terminal). Null
        /// if none.
        /// </summary>
        public static Plugin.App? Pick(IReadOnlyList<Plugin.App> apps, string want)
        {
            if (int.TryParse(want, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid > 0)
            {
                foreach (var a in apps)
                    if (a.Pid == pid) return a;
                return new Plugin.App(pid, "pid " + pid);
            }
            foreach (var a in apps)
                if (string.Equals(a.Name, want, StringComparison.OrdinalIgnoreCase)) return a;
            foreach (var a in apps)
                if (a.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        /// <summary>
        /// `--app`'s source: the app `want` names (<see cref="Pick"/>), waiting up to <see cref="AppWait"/>
        /// for it to produce audio. Null with `error` saying what is playing if it never does.
        /// </summary>
        public static MusicSource FindApp(string want, out string error)
        {
            var started = DateTime.UtcNow;
            while (true)
            {
                Plugin.App[] apps;
                try { apps = Plugin.ListApps(); }
                catch (Exception e)
                {
                    error = e.Message;
                    return null;
                }
                var app = Pick(apps, want);
                if (app != null)
                {
                    error = null;
                    return App(app.Value);
                }
                if ((DateTime.UtcNow - started).TotalSeconds >= AppWait)
                {
                    var names = new List<string>();
                    foreach (var a in apps) names.Add($"{a.Name} (pid {a.Pid})");
                    error = $"no app named \"{want}\" is producing audio; playing now: " +
                            (names.Count > 0 ? string.Join(", ", names) : "none");
                    return null;
                }
                Thread.Sleep(250);
            }
        }
    }
}
