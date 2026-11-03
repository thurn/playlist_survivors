using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Music;
using Champions.Render;
using UnityEngine;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>The title (design.md, The Run > Starting): the name over the void, then Play, Calibrate, Quit.</summary>
    public sealed class TitleScreen : VisualElement, IMenuScreen
    {
        public readonly MenuList Menu = new MenuList();

        public TitleScreen(Action play, Action calibrate, Action quit)
        {
            AddToClassList("screen");
            AddToClassList("screen--void");
            pickingMode = PickingMode.Ignore;
            var column = Column();
            var mark = new Label("RHYTHM CHAMPIONS") { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("wordmark");
            column.Add(mark);
            column.Add(new Label("WASD→move  Space→dash  F→cleave") { pickingMode = PickingMode.Ignore });
            column.Add(Menu);
            Add(column);
            Menu.Set(new[] { new MenuList.Row("Play", play), new MenuList.Row("Calibrate", calibrate), new MenuList.Row("Quit", quit) });
        }

        public bool Key(MenuKey key) => Menu.Key(key);

        internal static VisualElement Column()
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("column");
            return column;
        }
    }

    /// <summary>A plain menu of rows (the calibration's choice of screen), over the void or over a held run; Esc goes back.</summary>
    public sealed class MenuScreen : VisualElement, IMenuScreen
    {
        public readonly MenuList Menu = new MenuList();
        readonly Action _back;

        public MenuScreen(IReadOnlyList<MenuList.Row> rows, Action back, bool overRun)
        {
            _back = back;
            AddToClassList("screen");
            AddToClassList(overRun ? "screen--veil" : "screen--void");
            pickingMode = PickingMode.Ignore;
            var column = TitleScreen.Column();
            column.Add(Menu);
            Add(column);
            Menu.Set(rows);
        }

        public bool Key(MenuKey key)
        {
            if (key != MenuKey.Back) return Menu.Key(key);
            _back();
            return true;
        }
    }

    /// <summary>
    /// The music picker (design.md, Music > Sources): the apps playing audio now, refreshed every second, then File...
    /// (a file or playlist, <see cref="FileChooser"/>). With no app playing, a dim note says so. Esc goes back.
    /// </summary>
    public sealed class MusicPicker : VisualElement, IMenuScreen
    {
        public const double RefreshS = 1;
        public const string FileRow = "File...", NothingRow = "Nothing playing";

        public readonly MenuList Menu = new MenuList();
        readonly Func<IReadOnlyList<Plugin.App>> _listApps;
        readonly Action<Plugin.App> _app;
        readonly Action _file, _back;
        IReadOnlyList<Plugin.App> _listed = Array.Empty<Plugin.App>();
        double _nextRefresh = double.NegativeInfinity;

        /// <param name="listApps">The apps playing audio (<see cref="Plugin.ListApps"/>, or a still's own).</param>
        public MusicPicker(Func<IReadOnlyList<Plugin.App>> listApps, Action<Plugin.App> app, Action file, Action back)
        {
            _listApps = listApps;
            _app = app;
            _file = file;
            _back = back;
            AddToClassList("screen");
            AddToClassList("screen--void");
            pickingMode = PickingMode.Ignore;
            var column = TitleScreen.Column();
            column.Add(Menu);
            Add(column);
            Present(0);
        }

        /// <summary>At real time <paramref name="now"/> (s): the apps listed again once a second.</summary>
        public void Present(double now)
        {
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshS;
            IReadOnlyList<Plugin.App> apps;
            try { apps = _listApps(); }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning("MusicPicker: no apps from the plugin: " + e.Message);
                apps = Array.Empty<Plugin.App>();
            }
            if (Menu.Count > 0 && Same(apps, _listed)) return;
            _listed = apps;
            var rows = new List<MenuList.Row>();
            foreach (var a in apps)
            {
                var app = a;
                rows.Add(new MenuList.Row(app.Name, () => _app(app)));
            }
            if (apps.Count == 0) rows.Add(new MenuList.Row(NothingRow, null));
            rows.Add(new MenuList.Row(FileRow, _file));
            Menu.Set(rows);
        }

        static bool Same(IReadOnlyList<Plugin.App> a, IReadOnlyList<Plugin.App> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Pid != b[i].Pid || a[i].Name != b[i].Name) return false;
            return true;
        }

        public bool Key(MenuKey key)
        {
            if (key != MenuKey.Back) return Menu.Key(key);
            _back();
            return true;
        }
    }

    /// <summary>
    /// The pause menu (design.md, The Run > Pauses), over the held world: Resume, Calibrate, Quality (Left and Right or
    /// a choice step through the tiers, <see cref="QualityTiers"/>, remembered), End run. Esc resumes.
    /// </summary>
    public sealed class PauseMenu : VisualElement, IMenuScreen
    {
        public readonly MenuList Menu = new MenuList();
        readonly Action _resume;
        const int QualityRow = 2;

        public PauseMenu(Action resume, Action calibrate, Action endRun)
        {
            _resume = resume;
            AddToClassList("screen");
            AddToClassList("screen--veil");
            pickingMode = PickingMode.Ignore;
            var column = TitleScreen.Column();
            column.Add(Menu);
            Add(column);
            Menu.Set(new[]
            {
                new MenuList.Row("Resume", resume), new MenuList.Row("Calibrate", calibrate),
                new MenuList.Row(QualityText(QualityTiers.Current), null, Step), new MenuList.Row("End run", endRun),
            });
        }

        static string QualityText(Tier tier) => "Quality " + tier;

        void Step(int by)
        {
            int n = Enum.GetValues(typeof(Tier)).Length;
            var tier = (Tier)((((int)QualityTiers.Current + by) % n + n) % n);
            QualityTiers.Apply(tier, remember: true);
            Menu.SetText(QualityRow, QualityText(tier));
        }

        public bool Key(MenuKey key)
        {
            if (key != MenuKey.Back) return Menu.Key(key);
            _resume();
            return true;
        }
    }

    /// <summary>What the end card shows: how the run ended, its combat duration, its kills and the build's icons in slot order.</summary>
    public sealed class EndSummary
    {
        public bool Victory;
        public double CombatSeconds;
        public int Kills;
        public readonly List<Icon> Build = new List<Icon>();
    }

    /// <summary>
    /// The end card (design.md, The Run > Ending): the run's time, its kills, the build's icons and Again, lit for a
    /// victory and unlit for a death. Again (Enter, Space or a click) runs again on the same music; Esc goes to the title.
    /// Its numbers wear icons, not words.
    /// </summary>
    public sealed class EndCard : VisualElement, IMenuScreen
    {
        public readonly MenuList Menu = new MenuList();
        readonly Action _title;

        public EndCard(EndSummary summary, Icon timeIcon, Icon killsIcon, Action again, Action title)
        {
            _title = title;
            AddToClassList("screen");
            AddToClassList("screen--veil");
            AddToClassList(summary.Victory ? "endcard--lit" : "endcard--unlit");
            pickingMode = PickingMode.Ignore;
            var column = TitleScreen.Column();
            column.AddToClassList("endcard");
            var tint = summary.Victory ? Color.white : Unlit;
            column.Add(Stat(timeIcon, Time(summary.CombatSeconds), "endcard__time", tint));
            column.Add(Stat(killsIcon, summary.Kills.ToString("N0", CultureInfo.InvariantCulture), "endcard__kills", tint));
            var build = new VisualElement { pickingMode = PickingMode.Ignore };
            build.AddToClassList("endcard__build");
            foreach (var icon in summary.Build)
            {
                var e = new IconElement(icon) { Colour = tint };
                e.AddToClassList("endcard__icon");
                build.Add(e);
            }
            column.Add(build);
            column.Add(Menu);
            Add(column);
            Menu.Set(new[] { new MenuList.Row("Again", again) });
        }

        /// <summary>A run clock as minutes and seconds: 14:07.</summary>
        public static string Time(double seconds)
        {
            int s = Math.Max(0, (int)Math.Floor(seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        static readonly Color Unlit = new Color(1, 1, 1, 0.4f);

        static VisualElement Stat(Icon icon, string text, string className, Color tint)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("endcard__stat");
            row.AddToClassList(className);
            var e = new IconElement(icon) { Colour = tint };
            e.AddToClassList("endcard__stat-icon");
            row.Add(e);
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("endcard__number");
            row.Add(label);
            return row;
        }

        public bool Key(MenuKey key)
        {
            if (key != MenuKey.Back) return Menu.Key(key);
            _title();
            return true;
        }
    }
}
