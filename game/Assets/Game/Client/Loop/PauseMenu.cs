using System;
using Game.Client.Title;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Client.Loop
{
    /// <summary>
    /// The pause menu over a paused run (Resources/Pause: Pause.uxml and Pause.uss): resume, the
    /// two calibration screens, and end the run. Up and Down choose, Enter or Space picks, a click
    /// picks, Esc resumes. Its panel exists only while it is open, so a batch-mode still shows it,
    /// over the frozen run, only then (<see cref="ScreenPanel"/>).
    /// </summary>
    public sealed class PauseMenu : IDisposable
    {
        public enum Choice { None, Resume, CalibrateVideo, CalibrateInput, EndRun }

        static readonly (string Name, Choice Choice)[] Items =
        {
            ("Resume", Choice.Resume),
            ("Calibrate video", Choice.CalibrateVideo),
            ("Calibrate input", Choice.CalibrateInput),
            ("End run", Choice.EndRun),
        };

        readonly Transform _parent;
        readonly VisualElement[] _rows = new VisualElement[Items.Length];
        ScreenPanel _panel;
        int _selected, _shownFrame;
        Choice _clicked;

        public PauseMenu(Transform parent) => _parent = parent;

        public bool Open => _panel != null;

        public void Show()
        {
            if (Open) return;
            _panel = new ScreenPanel(_parent, "Pause menu", "Pause/Pause", overlay: true);
            var list = _panel.Root.Q("rows");
            for (int i = 0; i < Items.Length; i++)
            {
                var e = new VisualElement();
                e.AddToClassList("entry");
                var gem = new VisualElement();
                gem.AddToClassList("entry__gem");
                e.Add(gem);
                var name = new Label(Items[i].Name);
                name.AddToClassList("entry__name");
                e.Add(name);
                var choice = Items[i].Choice;
                e.RegisterCallback<PointerDownEvent>(_ => _clicked = choice);
                _rows[i] = e;
                list.Add(e);
            }
            _selected = 0;
            _clicked = Choice.None;
            _shownFrame = Time.frameCount;
            Highlight();
        }

        public void Hide()
        {
            _panel?.Dispose();
            _panel = null;
        }

        /// <summary>
        /// Shows or hides the open menu without closing it, while a calibration screen opened from it
        /// is up; the frame it shows again reads no keys (the closing key was the screen's).
        /// </summary>
        public bool Visible
        {
            set
            {
                if (_panel == null) return;
                _panel.Visible = value;
                if (value) _shownFrame = Time.frameCount;
            }
        }

        /// <summary>The player's pick this frame, if any. The frame it opens reads no keys: the key that paused isn't a pick.</summary>
        public Choice Read()
        {
            if (!Open || Time.frameCount == _shownFrame) return Choice.None;
            var clicked = _clicked;
            _clicked = Choice.None;
            if (clicked != Choice.None) return clicked;
            var k = Keyboard.current;
            if (k == null) return Choice.None;
            if (k.escapeKey.wasPressedThisFrame) return Choice.Resume;
            int by = (k.downArrowKey.wasPressedThisFrame ? 1 : 0) - (k.upArrowKey.wasPressedThisFrame ? 1 : 0);
            if (by != 0)
            {
                _selected = (_selected + by + Items.Length) % Items.Length;
                Highlight();
            }
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)
                return Items[_selected].Choice;
            return Choice.None;
        }

        void Highlight()
        {
            for (int i = 0; i < _rows.Length; i++) _rows[i].EnableInClassList("entry--selected", i == _selected);
        }

        public void Dispose() => Hide();
    }
}
