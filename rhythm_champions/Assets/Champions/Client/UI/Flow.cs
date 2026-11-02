using System;
using System.Collections.Generic;
using Champions.Client.Calibration;
using Champions.Client.Loop;
using Champions.Client.Music;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>
    /// The game's screens in order (plan.md, The Client > Flow; design.md, The Run > Starting, Pauses, Ending): the title,
    /// the music picker, the run (a <see cref="GameLoop"/> on the music picked, with its <see cref="RunUi"/>), the pause
    /// menu over it on Esc or when the window loses focus, the calibration screens from the title or the pause menu, and
    /// the end card (<see cref="ShowEnd"/>). Screens stack on one panel over the run's UI; the top one takes the menu's keys
    /// from the keyboard (<see cref="MenuKey"/>) and the pointer. Boot begins it when no music was given on the command line.
    /// </summary>
    public sealed class Flow : MonoBehaviour
    {
        public const int Order = 10;

        UiPanel _panel;
        Camera _camera;
        WorldPresenter _world;
        Func<RunUi> _makeUi;
        string _contentDir;
        bool _fixed, _mute;
        readonly List<VisualElement> _screens = new List<VisualElement>();
        GameLoop _run;
        FileChooser _chooser;
        CalibrationScreen _calibration;

        /// <summary>A run plays (the world is the loop's to present).</summary>
        public bool Running => _run != null;

        /// <summary>The screen on top, or null during play.</summary>
        public VisualElement Top => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        /// <param name="makeUi">A run's UI, made for each run.</param>
        public static Flow Begin(GameObject host, Camera camera, WorldPresenter world, Func<RunUi> makeUi, string contentDir, bool fixedTime, bool mute)
        {
            var flow = host.AddComponent<Flow>();
            flow._camera = camera;
            flow._world = world;
            flow._makeUi = makeUi;
            flow._contentDir = contentDir;
            flow._fixed = fixedTime;
            flow._mute = mute;
            flow._panel = new UiPanel(flow.transform, "Menus", Order);
            flow.Title();
            return flow;
        }

        void Push(VisualElement screen)
        {
            if (Top != null) Top.style.display = DisplayStyle.None;
            _screens.Add(screen);
            _panel.Root.Add(screen);
        }

        void Pop()
        {
            if (_screens.Count == 0) return;
            Top.RemoveFromHierarchy();
            _screens.RemoveAt(_screens.Count - 1);
            if (Top != null) Top.style.display = DisplayStyle.Flex;
        }

        void Title()
        {
            while (_screens.Count > 0) Pop();
            Push(new TitleScreen(Pick, () => CalibrationChoice(overRun: false), () => Application.Quit(0)));
        }

        void Pick() => Push(new MusicPicker(() => Plugin.ListApps(), app => StartRun(MusicSource.App(app)), ChooseFile, Pop));

        void ChooseFile()
        {
            if (Application.isBatchMode || _chooser != null) return; // no panel without a window
            _chooser = FileChooser.Open();
        }

        void StartRun(MusicSource source)
        {
            while (_screens.Count > 0) Pop();
            Debug.Log($"Flow: a run on {source.Tag}");
            _run = GameLoop.Begin(gameObject, _camera, source, _fixed, _mute, _world, _makeUi(), RunFailed);
        }

        void RunFailed(int code, string why)
        {
            Debug.LogWarning($"Flow: the run's music stopped ({code}): {why}");
            EndRun();
            Pick();
        }

        void EndRun()
        {
            if (_run == null) return;
            _run.End();
            _run = null;
            Title();
        }

        void Pause()
        {
            _run.Pause();
            Push(new PauseMenu(Resume, () => CalibrationChoice(overRun: true), EndRun));
        }

        void Resume()
        {
            Pop();
            _run?.Resume();
        }

        void CalibrationChoice(bool overRun) => Push(new MenuScreen(new[]
        {
            new MenuList.Row("Video", () => Calibrate(CalibrationScreen.Mode.Video)),
            new MenuList.Row("Input", () => Calibrate(CalibrationScreen.Mode.Input)),
        }, Pop, overRun));

        void Calibrate(CalibrationScreen.Mode mode)
        {
            _calibration = new CalibrationScreen(transform, mode, _fixed, CloseCalibration, _run != null ? _run.Music : null);
            Push(_calibration);
        }

        void CloseCalibration()
        {
            Pop();
            _calibration.Dispose();
            _calibration = null;
            // The run's clock takes what was saved.
            if (_run != null) CalibrationStore.Load().For(CalibrationScreen.CurrentDevice().Uid).ApplyTo(_run.Music.Clock);
        }

        /// <summary>
        /// The run ended (the assembly's call on the sim's RunEnd): the end card over it, lit for a victory. Again runs
        /// <paramref name="again"/>; Esc ends the run for the title.
        /// </summary>
        public void ShowEnd(EndSummary summary, Action again)
        {
            Push(new EndCard(summary, Icon.Load(_contentDir, "time"), Icon.Load(_contentDir, "kills"), () =>
            {
                Pop();
                again();
            }, EndRun));
        }

        void Update()
        {
            (Top as MusicPicker)?.Present(Time.realtimeSinceStartupAsDouble);
            _calibration?.Update();
            if (_chooser != null && _chooser.Done)
            {
                string path = _chooser.Path;
                _chooser.Dispose();
                _chooser = null;
                if (path != null && Top is MusicPicker) StartRun(MusicSource.File(path));
            }
            var k = Keyboard.current;
            if (k == null) return;
            Key(k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame, MenuKey.Up);
            Key(k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame, MenuKey.Down);
            Key(k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame, MenuKey.Left);
            Key(k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame, MenuKey.Right);
            Key(k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame, MenuKey.Choose);
            Key(k.escapeKey.wasPressedThisFrame, MenuKey.Back);
        }

        void Key(bool pressed, MenuKey key)
        {
            if (!pressed) return;
            if (Top is IMenuScreen screen) screen.Key(key);
            else if (Running && key == MenuKey.Back) Pause(); // during play only Esc is the flow's
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && Running && Top == null) Pause();
        }

        void OnDestroy()
        {
            _chooser?.Dispose();
            _calibration?.Dispose();
            _panel?.Dispose();
        }
    }
}
