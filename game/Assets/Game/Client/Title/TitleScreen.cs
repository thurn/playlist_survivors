using System;
using System.Collections.Generic;
using System.IO;
using Game.Client.Music;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Client.Title
{
    /// <summary>
    /// The title screen (unity-game.md, The Game, as the Player Sees It), in UI Toolkit
    /// (Resources/Title: Title.uxml, Title.uss and the panel's TitlePanel.asset): pick an app producing audio now, or a local
    /// file or playlist, and Enter or Space starts the run on it. Up and Down choose, a click
    /// chooses and a second click starts; Cmd-V adds a copied file or playlist path. The files are
    /// the recently played ones and those in <see cref="MusicDir"/>; the apps refresh every second.
    /// Player flags: `--title` shows it in batch mode or with --fixed-time too (for stills),
    /// `--music-dir DIR` lists DIR's files in place of <see cref="MusicDir"/>, and
    /// `--title-pick NAME` starts the first row whose name contains NAME, once per launch, for
    /// windowless checks of the title-to-run-to-title loop.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        const float AppRefresh = 1f;
        const int Recents = 6;
        const string RecentKey = "title.recent";

        static readonly string[] Extensions = { ".m3u", ".m3u8", ".mp3", ".m4a", ".aac", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".caf" };
        // The crystal's rainbow, one hue per letter of the wordmark.
        static readonly Color[] Spectrum =
        {
            new Color(1f, 0.55f, 0.62f), new Color(1f, 0.72f, 0.5f), new Color(1f, 0.92f, 0.55f),
            new Color(0.62f, 1f, 0.7f), new Color(0.5f, 0.92f, 1f), new Color(0.6f, 0.7f, 1f), new Color(0.85f, 0.6f, 1f),
        };
        static bool _picked; // --title-pick acts once per launch

        /// <summary>Why the previous run ended, shown once when the title screen next opens.</summary>
        public static string LastEnd;

        /// <summary>Where the title screen looks for music files: ~/Music/Playlist Survivors.</summary>
        public static string MusicDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Music", "Playlist Survivors");

        /// <summary>Whether this launch opens on the title screen: a windowed run without a source, or `--title`.</summary>
        public static bool Wanted =>
            Args.Has("--title") ||
            !(Application.isBatchMode || Args.Has("--headless") || Args.Has("--fixed-time") || Args.Has("--events"));

        struct Row
        {
            public string Key, Name, Kind;
            public MusicSource Source; // null: an empty list's note
            public VisualElement Element;
        }

        readonly List<Row> _rows = new List<Row>();
        Action<MusicSource> _start;
        UIDocument _doc;
        PanelSettings _settings;
        RenderTexture _target;
        VisualElement _apps, _files;
        Label _message;
        Plugin.App[] _listed = Array.Empty<Plugin.App>();
        string _selected, _pick, _musicDir;
        float _refreshAt;

        /// <summary>Opens the title screen on `host`; `start` gets the picked source after the screen has closed.</summary>
        public static TitleScreen Begin(GameObject host, Action<MusicSource> start)
        {
            var title = host.AddComponent<TitleScreen>();
            title._start = start;
            title.Setup();
            return title;
        }

        void Setup()
        {
            _pick = _picked ? null : Args.Get("--title-pick");
            _musicDir = Args.Get("--music-dir");
            _musicDir = _musicDir != null ? Args.Path(_musicDir) : MusicDir;
            // A copy of the editor-made asset (scaled to 1080p, the theme, and the text's ICU data,
            // which only the editor assigns), so a target set here never touches the asset.
            _settings = Instantiate(Resources.Load<PanelSettings>("Title/TitlePanel"));
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // No window to draw on: the panel draws into a target the stills keep in place of the camera's view.
                _target = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = "Title" };
                _target.Create();
                _settings.targetTexture = _target;
                Stills.Screen = _target;
            }
            var go = new GameObject("Title screen");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = _settings;
            _doc.visualTreeAsset = Resources.Load<VisualTreeAsset>("Title/Title");
            var root = _doc.rootVisualElement;
            _apps = root.Q("apps");
            _files = root.Q("files");
            _message = root.Q<Label>("message");
            var wordmark = root.Q("wordmark");
            const string name = "PLAYLIST SURVIVORS";
            for (int i = 0, hue = 0; i < name.Length; i++)
            {
                var letter = new Label(name[i].ToString());
                letter.AddToClassList("wordmark__letter");
                if (name[i] != ' ') letter.style.color = Spectrum[hue++ % Spectrum.Length];
                wordmark.Add(letter);
            }
            Say(LastEnd);
            LastEnd = null;
            _listed = ListApps();
            Rebuild();
        }

        void Update()
        {
            if (Time.unscaledTime >= _refreshAt)
            {
                _refreshAt = Time.unscaledTime + AppRefresh;
                var apps = ListApps();
                if (!SameApps(apps, _listed))
                {
                    _listed = apps;
                    Rebuild();
                }
            }
            if (_pick != null)
            {
                foreach (var r in _rows)
                    if (r.Source != null && r.Name.IndexOf(_pick, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _picked = true;
                        _pick = null;
                        Launch(r, remember: false); // a check's pick stays out of the player's recents
                        return;
                    }
            }
            var k = Keyboard.current;
            if (k == null) return;
            if (k.upArrowKey.wasPressedThisFrame) Move(-1);
            if (k.downArrowKey.wasPressedThisFrame) Move(1);
            if (k.vKey.wasPressedThisFrame && (k.leftCommandKey.isPressed || k.rightCommandKey.isPressed || k.ctrlKey.isPressed)) Paste();
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)
            {
                int i = Selected();
                if (i >= 0) Launch(_rows[i]);
            }
        }

        Plugin.App[] ListApps()
        {
            try { return Plugin.ListApps(); }
            catch (Exception e)
            {
                // A missing plugin or a listing error: files still play (or fail with the plugin's reason).
                Debug.LogWarning("TitleScreen: no app list: " + e.Message);
                return Array.Empty<Plugin.App>();
            }
        }

        static bool SameApps(Plugin.App[] a, Plugin.App[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Pid != b[i].Pid || a[i].Name != b[i].Name) return false;
            return true;
        }

        /// <summary>Rebuilds both lists, keeping the selection on the same app or file if it is still there.</summary>
        void Rebuild()
        {
            _rows.Clear();
            _apps.Clear();
            _files.Clear();
            foreach (var app in _listed)
                Add(_apps, new Row { Key = "app:" + app.Pid, Name = app.Name, Kind = "APP", Source = MusicSource.App(app) });
            if (_listed.Length == 0) Add(_apps, new Row { Name = "Nothing is playing. Play music in any app and it appears here." });
            int files = 0;
            foreach (var path in Files())
            {
                Add(_files, new Row
                {
                    Key = "file:" + path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Kind = Path.GetExtension(path).StartsWith(".m3u", StringComparison.OrdinalIgnoreCase) ? "PLAYLIST" : "FILE",
                    Source = MusicSource.File(path),
                });
                files++;
            }
            if (files == 0) Add(_files, new Row { Name = "Copy a file or playlist's path and press Cmd-V, or put music in " + _musicDir });
            if (Selected() < 0)
            {
                _selected = null;
                foreach (var r in _rows)
                    if (r.Source != null) { _selected = r.Key; break; }
            }
            Show();
        }

        void Add(VisualElement list, Row row)
        {
            var e = new VisualElement();
            e.AddToClassList("entry");
            var gem = new VisualElement();
            gem.AddToClassList("entry__gem");
            e.Add(gem);
            var name = new Label(row.Name);
            name.AddToClassList("entry__name");
            e.Add(name);
            if (row.Source == null) e.AddToClassList("entry--empty");
            else
            {
                var kind = new Label(row.Kind);
                kind.AddToClassList("entry__kind");
                e.Add(kind);
                gem.style.backgroundColor = Spectrum[_rows.Count % Spectrum.Length];
                string key = row.Key;
                e.RegisterCallback<PointerDownEvent>(_ =>
                {
                    if (_selected == key) Launch(_rows[Selected()]);
                    else
                    {
                        _selected = key;
                        Show();
                    }
                });
            }
            row.Element = e;
            _rows.Add(row);
            list.Add(e);
        }

        /// <summary>The recently played files that still exist, then the music folder's, each once.</summary>
        IEnumerable<string> Files()
        {
            var seen = new HashSet<string>();
            foreach (var path in LoadRecents())
                if (File.Exists(path) && seen.Add(path)) yield return path;
            if (!Directory.Exists(_musicDir)) yield break;
            var found = new List<string>();
            foreach (var path in Directory.GetFiles(_musicDir))
                if (Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0) found.Add(path);
            found.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var path in found)
                if (seen.Add(path)) yield return path;
        }

        static List<string> LoadRecents()
        {
            var list = new List<string>();
            foreach (var line in PlayerPrefs.GetString(RecentKey, "").Split('\n'))
                if (line.Length > 0) list.Add(line);
            return list;
        }

        static void Remember(string path)
        {
            var list = LoadRecents();
            list.Remove(path);
            list.Insert(0, path);
            if (list.Count > Recents) list.RemoveRange(Recents, list.Count - Recents);
            PlayerPrefs.SetString(RecentKey, string.Join("\n", list));
            PlayerPrefs.Save();
        }

        /// <summary>Adds the copied path (a Finder copy, a terminal's, quoted or with ~) and selects it.</summary>
        void Paste()
        {
            string text = GUIUtility.systemCopyBuffer?.Trim().Trim('"', '\'') ?? "";
            if (text.StartsWith("~/")) text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), text.Substring(2));
            if (text.StartsWith("file://")) text = Uri.UnescapeDataString(new Uri(text).LocalPath);
            if (text.Length == 0 || !Path.IsPathRooted(text) || !File.Exists(text))
            {
                Say(text.Length == 0 ? "Nothing copied: copy a file or playlist's path first." : "No file at " + text);
                return;
            }
            Remember(text);
            _selected = "file:" + text;
            Say(null);
            Rebuild();
        }

        int Selected()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Source != null && _rows[i].Key == _selected) return i;
            return -1;
        }

        void Move(int by)
        {
            int n = _rows.Count, at = Selected();
            if (at < 0) at = by > 0 ? n - 1 : 0;
            for (int step = 1; step <= n; step++)
            {
                int i = ((at + by * step) % n + n) % n;
                if (_rows[i].Source == null) continue;
                _selected = _rows[i].Key;
                break;
            }
            Show();
        }

        void Show()
        {
            foreach (var r in _rows) r.Element.EnableInClassList("entry--selected", r.Source != null && r.Key == _selected);
        }

        void Say(string message)
        {
            _message.text = message ?? "";
            _message.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void Launch(Row row, bool remember = true)
        {
            if (remember && !row.Source.IsApp) Remember(row.Source.Path);
            Debug.Log($"TitleScreen: starting {(row.Source.IsApp ? $"app {row.Source.Tag} (pid {row.Source.Pid})" : row.Source.Path)}");
            var start = _start;
            Destroy(this);
            start(row.Source);
        }

        void OnDestroy()
        {
            if (_doc != null) Destroy(_doc.gameObject);
            if (Stills.Screen == _target) Stills.Screen = null;
            if (_target != null) _target.Release();
            if (_settings != null) Destroy(_settings);
        }
    }
}
