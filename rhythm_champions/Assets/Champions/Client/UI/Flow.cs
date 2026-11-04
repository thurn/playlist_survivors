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
    /// the end card when the run ends (<see cref="ShowEnd"/>), whose Again starts a new run on the same music. Screens stack on
    /// one panel over the run's UI; the top one takes the menu's keys from the keyboard (<see cref="MenuKey"/>) and the
    /// pointer. Boot begins it on the title when no music was given on the command line, else straight into the run on that
    /// music (<see cref="BeginRun"/>). A scripted run (`--walk`) never pauses on a lost focus; a batch-mode run quits a second
    /// after its end card, with 0.
    ///
    /// The title's Boss Rush entry (boss-rush.md, The Run) leads to the same picker for a Boss Rush run, offering first to
    /// continue the act checkpoint saved (<see cref="RushSave"/>) or start anew. Its pause menu adds Restart act and Abandon
    /// run; its end card offers Retry act and New run after a death, New run after the victory.
    /// </summary>
    public sealed class Flow : MonoBehaviour
    {
        public const int Order = 10;

        UiPanel _panel;
        /// <summary>A batch-mode run quits this long after its end card shows (real seconds).</summary>
        public const double QuitAfterEndCard = 1;

        Camera _camera;
        WorldPresenter _world;
        Func<MusicSource, RunMode, RunSetup> _makeSetup;
        RunMode _mode = RunMode.Main;
        Sim.BossRushRules _rush;
        string _contentDir;
        bool _fixed, _direct;
        double _quitAt = double.NaN;
        readonly List<VisualElement> _screens = new List<VisualElement>();
        GameLoop _run;
        FileChooser _chooser;
        CalibrationScreen _calibration;

        /// <summary>A run plays (the world is the loop's to present).</summary>
        public bool Running => _run != null;

        /// <summary>The screen on top, or null during play.</summary>
        public VisualElement Top => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        /// <param name="makeSetup">A run's setup on the music picked.</param>
        /// <param name="bossRush">`--mode bossrush`: the title's Boss Rush entry at once.</param>
        public static Flow Begin(GameObject host, Camera camera, WorldPresenter world, Func<MusicSource, RunMode, RunSetup> makeSetup, string contentDir,
            bool fixedTime, Sim.BossRushRules rush, bool bossRush = false)
        {
            var flow = Make(host, camera, world, makeSetup, contentDir, fixedTime);
            flow._rush = rush;
            flow.Title();
            if (bossRush) flow.BossRushEntry();
            return flow;
        }

        /// <summary>Straight into a run on <paramref name="source"/> (`--file`, `--app`): music that fails quits the player with its code.</summary>
        public static Flow BeginRun(GameObject host, Camera camera, WorldPresenter world, Func<MusicSource, RunMode, RunSetup> makeSetup, string contentDir,
            bool fixedTime, MusicSource source, RunMode mode)
        {
            var flow = Make(host, camera, world, makeSetup, contentDir, fixedTime);
            flow._direct = true;
            flow._mode = mode;
            flow.StartRun(source);
            return flow;
        }

        static Flow Make(GameObject host, Camera camera, WorldPresenter world, Func<MusicSource, RunMode, RunSetup> makeSetup, string contentDir, bool fixedTime)
        {
            var flow = host.AddComponent<Flow>();
            flow._camera = camera;
            flow._world = world;
            flow._makeSetup = makeSetup;
            flow._contentDir = contentDir;
            flow._fixed = fixedTime;
            flow._panel = new UiPanel(flow.transform, "Menus", Order);
            return flow;
        }

        /// <summary>The run playing now, or null.</summary>
        public GameLoop Run => _run;

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
            Push(new TitleScreen(() =>
            {
                _mode = RunMode.Main;
                Pick();
            }, BossRushEntry, () => CalibrationChoice(overRun: false), () => Application.Quit(0)));
        }

        /// <summary>The title's Boss Rush: continue the saved act checkpoint or start anew, then the music.</summary>
        void BossRushEntry()
        {
            var saved = RushSave.Load();
            if (saved == null)
            {
                _mode = new RunMode { BossRush = true, Save = true };
                Pick();
                return;
            }
            Push(new MenuScreen(new[]
            {
                new MenuList.Row("Continue  " + RushSummary.Place(ActOf(saved), saved.Boss), () =>
                {
                    _mode = new RunMode { BossRush = true, Save = true, From = saved };
                    Pick();
                }),
                new MenuList.Row("New run", () =>
                {
                    _mode = new RunMode { BossRush = true, Save = true };
                    Pick();
                }),
            }, Pop, overRun: false));
        }

        int ActOf(Sim.BossRushCheckpoint c) => _rush != null && c.Boss < _rush.Count ? _rush.Bosses[c.Boss].Act : 0;

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
            _run = GameLoop.Begin(gameObject, _camera, _makeSetup(source, _mode), RunFailed);
            _run.Ended += ShowEnd;
        }

        void RunFailed(int code, string why)
        {
            if (_direct)
            {
                Debug.Log($"Flow: quitting with {code}: {why}");
                Application.Quit(code);
                return;
            }
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
            if (_run.Scripted) return;
            _run.Pause();
            if (!_run.Rush)
            {
                Push(new PauseMenu(Resume, () => CalibrationChoice(overRun: true), EndRun));
                return;
            }
            Push(new PauseMenu(Resume, () => CalibrationChoice(overRun: true), EndRun, () =>
            {
                Pop();
                _run.RestartAct();
            }, () =>
            {
                _run.AbandonRush();
                EndRun();
            }));
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
            if (summary.Rush != null)
            {
                summary.Rush.Rows.Clear();
                if (!summary.Victory) summary.Rush.Rows.Add(new MenuList.Row("Retry act", () => { Pop(); _run?.RetryAct(); }));
                summary.Rush.Rows.Add(new MenuList.Row("New run", () => { Pop(); _run?.NewRush(); }));
            }
            Push(new EndCard(summary, Icon.Load(_contentDir, "time"), Icon.Load(_contentDir, "kills"), () =>
            {
                Pop();
                again();
            }, EndRun));
            if (Application.isBatchMode) _quitAt = Time.realtimeSinceStartupAsDouble + QuitAfterEndCard;
        }

        void Update()
        {
            if (Time.realtimeSinceStartupAsDouble >= _quitAt)
            {
                _quitAt = double.NaN;
                Debug.Log("Flow: quitting with 0 after the end card (batch mode)");
                Application.Quit(0);
            }
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
            if (!focused && Running && Top == null && !_run.Scripted) Pause();
        }

        void OnDestroy()
        {
            _chooser?.Dispose();
            _calibration?.Dispose();
            _panel?.Dispose();
        }
    }
}
