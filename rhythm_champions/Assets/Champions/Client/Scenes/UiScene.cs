using System;
using System.Collections.Generic;
using Champions.Client.Calibration;
using Champions.Client.Music;
using Champions.Client.UI;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `ui`: the UI's scripted scene (`champions stills --scene ui`). The real sim plays a scripted song (120 BPM to 20 s,
    /// then no beat) on the scene's clock, the hero stands where the sim has it (a white ring standing in for it) in the
    /// world at the game's camera, and its indicator shows each state (design.md, The Dash > The indicator): ready with the
    /// beats' ticks converging, just after an on-beat dash (the flash), a press dropped in the cooldown (the blink),
    /// cooling, just hurt (the bar's flash), health sustained, and beatless (no ticks); the HUD's hairline under it.
    /// Then each screen: the title, the music picker, the pause menu, the first offer's cards (the middle one focused), a
    /// owned-item boss reward, the HUD in a duel, the end card lit and unlit, and the calibration screens.
    /// </summary>
    sealed class UiScene : IStillScene, INamedStills
    {
        public const string Name = "ui";
        public const double Bpm = 120, BeatsUntil = 20, SongEnd = 40;

        /// <summary>Each still's label and scene time. A screen opens at least 0.4 s before its still, so its panel has drawn it.</summary>
        internal static readonly (string Label, double At)[] Stills =
        {
            ("indicator-ready", 5.80), ("indicator-dash-on-beat", 6.06), ("indicator-dropped-press", 6.33), ("indicator-cooling", 6.45),
            ("indicator-hurt", 8.05), ("indicator-health", 9.0), ("indicator-beatless", 24.0),
            ("title", 25.4), ("picker", 25.9), ("pause", 26.4), ("cards-first-offer", 26.9), ("cards-treasure", 27.4),
            ("hud-duel", 27.9), ("victory", 28.4), ("death", 28.9), ("calibration-video", 30.03), ("calibration-input", 30.6), ("cards-martial-forms", 31.4), ("cards-magic-forms", 31.9),
        };

        readonly GameContent _content;
        readonly Palette _palette;
        readonly string _contentDir;

        public UiScene(GameContent content, Palette palette, string contentDir)
        {
            _content = content;
            _palette = palette;
            _contentDir = contentDir;
        }

        public double[] Times => Array.ConvertAll(Stills, s => s.At);

        public string Label(int index) => Stills[index].Label;

        public string Moment => null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            root.gameObject.AddComponent<UiSceneDriver>().Begin(camera, _content, _palette, _contentDir, view);
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Effect] = ("hero stand-in and indicator", "effect", HeroMarker.WidthM);
        }
    }

    /// <summary>Plays <see cref="UiScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class UiSceneDriver : MonoBehaviour
    {
        const double DashAt = 6.0, DroppedAt = 6.30, HurtAt = 8.0;

        Camera _camera;
        Stills.View _view;
        Simulation _sim;
        WorldPresenter _world;
        HeroMarker _marker;
        IndicatorPresenter _indicator;
        UiPanel _run, _menus;
        Hud _hud;
        CardsView _cards;
        CardFaces _faces;
        Palette _palette;
        GameContent _content;
        string _contentDir;
        CalibrationScreen _calibration;
        double _start;
        int _cursor, _step = -1;
        bool _dashed, _dropped, _hurt;

        public void Begin(Camera camera, GameContent content, Palette palette, string contentDir, Stills.View view)
        {
            _camera = camera;
            _view = view;
            _content = content;
            _palette = palette;
            _contentDir = contentDir;
            _start = Time.timeAsDouble;
            var config = new RunInputs().Build(null);
            _sim = new Simulation(config);
            foreach (var e in new ScriptedSong().Beats(0, UiScene.BeatsUntil, UiScene.Bpm).Gap(UiScene.BeatsUntil, UiScene.SongEnd).Entries())
                _sim.AddKnown(e, Math.Max(e.Stamp + config.KnownAtBudget, _sim.Now));
            _world = new WorldPresenter(camera, palette);
            _marker = new HeroMarker(camera.gameObject.layer);
            _indicator = new IndicatorPresenter(palette, camera.gameObject.layer);
            _faces = CardFaces.Load(contentDir, content);
            _run = new UiPanel(transform, "UI scene: run", RunUi.Order);
            _hud = new Hud(palette);
            _run.Root.Add(_hud);
            _cards = new CardsView();
            _run.Root.Add(_cards);
            _menus = new UiPanel(transform, "UI scene: menus", Flow.Order);
            Debug.Log($"UI: the scripted scene, {UiScene.Bpm} BPM to {UiScene.BeatsUntil} s, a dash on the beat at {DashAt} s, " +
                      $"a press dropped at {DroppedAt} s, a hit at {HurtAt} s, then the screens");
        }

        void Update()
        {
            if (_sim == null) return;
            double now = Time.timeAsDouble - _start;
            if (!_dashed && now >= DashAt) Press(ref _dashed, DashAt);
            if (!_dropped && now >= DroppedAt) Press(ref _dropped, DroppedAt);
            _sim.StepTo(now + 0.05);
            foreach (var e in _sim.EventsSince(_cursor))
            {
                _world.Take(e);
                _indicator.Take(e);
            }
            _world.Flush();
            _cursor = _sim.EventCount;
            if (!_hurt && now >= HurtAt - 0.1)
            {
                _hurt = true;
                _indicator.Take(SimEvent.HeroHit(HurtAt, HitOutcome.Hurt, HitSource.Touch, 0, 35, 65, 1, 0, -1));
            }
            var snapshot = _sim.Snapshot();
            snapshot.HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _marker.Place(feet);
            _world.Present(now, feet, Time.deltaTime);
            _view.Hero = new Vector3(feet.x, 0, feet.y);
            _indicator.Present(now, snapshot, _sim.Beats, _camera);
            Screens(now);
            _hud.Present(snapshot, _step == 6
                ? new BossBar { Up = true, Colour = _palette.ForType("fatOgre"), Health = .72f } : default);
            _cards.Present(now);
            _calibration?.Update();
        }

        void Press(ref bool done, double heard)
        {
            done = true;
            _sim.Press(Key.Space, heard);
            _sim.Release(Key.Space, heard);
        }

        /// <summary>
        /// The screen at scene time <paramref name="now"/>: play (the indicator under the HUD) to 25 s, then one screen each
        /// half second to the death's end card, then the video calibration for a click and its flash, then the input's.
        /// </summary>
        static int ScreenAt(double now) => now < 25 ? 0 : now < 29 ? 1 + (int)Math.Floor((now - 25) / 0.5) : now < 30.1 ? 9 : now < 31 ? 10 : now < 31.5 ? 11 : 12;

        /// <summary>Opens each screen at its turn.</summary>
        void Screens(double now)
        {
            int step = ScreenAt(now);
            if (step == _step) return;
            _step = step;
            _menus.Root.Clear();
            _cards.Hide();
            CloseCalibration();
            void None() { }
            switch (step)
            {
                case 0: break;
                case 1:
                    _menus.Root.Add(new TitleScreen(None, None, None, None));
                    break;
                case 2:
                    _menus.Root.Add(new MusicPicker(() => new[] { new Plugin.App(1, "Spotify"), new Plugin.App(2, "Music"), new Plugin.App(3, "Safari") },
                        _ => { }, None, None));
                    break;
                case 3:
                    _menus.Root.Add(new PauseMenu(None, None, None));
                    break;
                case 4:
                    _cards.Show(new[]
                    {
                        _faces.Of(Card.Of(WeaponKind.Sword, 2, 1)), _faces.Of(Card.Of(WeaponKind.Bow, 1, 0)),
                        _faces.Of(Card.Of(SupportKind.Might, 1, 0)),
                    }, now);
                    _cards.Focus(1);
                    break;
                case 5:
                    _cards.Show(new[]
                    {
                        _faces.Of(Card.Of(WeaponKind.Sword, 3, 2)), _faces.Of(Card.Of(WeaponKind.OrbitBlades, 5, 4)),
                        _faces.Of(Card.Of(SupportKind.Tempo, 5, 4)),
                    }, now);
                    break;
                case 6:
                    break;
                case 7:
                case 8:
                    var summary = new EndSummary { Victory = step == 7, CombatSeconds = step == 7 ? 973.4 : 512.8, Kills = step == 7 ? 4127 : 1893 };
                    foreach (var (kind, id) in new[] { (CardKind.Weapon, "sword"), (CardKind.Weapon, "orbitBlades"), (CardKind.Weapon, "bow"),
                                 (CardKind.Support, "might"), (CardKind.Support, "tempo"), (CardKind.Support, "reach") })
                        summary.Build.Add(_faces.Of(kind, id, 1).Icon);
                    _menus.Root.Add(new EndCard(summary, Icon.Load(_contentDir, "time"), Icon.Load(_contentDir, "kills"), None, None));
                    break;
                case 9:
                    _calibration = new CalibrationScreen(transform, CalibrationScreen.Mode.Video, fixedTime: true, None);
                    _menus.Root.Add(_calibration);
                    break;
                case 10:
                    _calibration = new CalibrationScreen(transform, CalibrationScreen.Mode.Input, fixedTime: true, None);
                    _menus.Root.Add(_calibration);
                    break;
                case 11:
                    _cards.Show(new[] { _faces.Of(Card.Of(WeaponKind.Hammer, 3, 2)),
                        _faces.Of(Card.Of(WeaponKind.Spear, 5, 4)), _faces.Of(Card.Of(WeaponKind.Bow, 8, 7)) }, now);
                    break;
                default:
                    _cards.Show(new[] { _faces.Of(Card.Of(WeaponKind.Lightning, 5, 4)),
                        _faces.Of(Card.Of(WeaponKind.EmberField, 8, 7)), _faces.Of(Card.Of(WeaponKind.Sentinel, 8, 7)) }, now);
                    break;
            }
        }

        void CloseCalibration()
        {
            _calibration?.Dispose();
            _calibration = null;
        }

        void OnDestroy()
        {
            CloseCalibration();
            _hud?.Dispose();
            _menus?.Dispose();
            _run?.Dispose();
            _indicator?.Dispose();
            _marker?.Dispose();
            _world?.Dispose();
        }
    }
}
