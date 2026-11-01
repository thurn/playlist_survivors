using System;
using Game.Client.Title;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Client
{
    /// <summary>
    /// What the command line's flags left unread (<see cref="LaunchOptions.Unrecognized"/>), in a popup before
    /// anything starts (Resources/Flags: Flags.uxml and Flags.uss), so a mistyped flag doesn't pass for a feature
    /// that does nothing. A click, Enter, Space or Esc closes it, and the launch goes on the frame after, so that key
    /// or click is the popup's alone. It opens once per launch (a run's end reloads the boot scene). The notes are
    /// logged as warnings too; a windowless launch only logs them, unless it keeps stills, which show the popup in
    /// place of the game it held back.
    /// </summary>
    public sealed class FlagsPopup : MonoBehaviour
    {
        static bool _shown;

        ScreenPanel _panel;
        Action _then;
        int _shownFrame, _closedFrame;
        bool _clicked;

        /// <summary>Shows `options`' unrecognized arguments, then calls `then`; calls it at once if there's nothing to show.</summary>
        public static void Begin(GameObject host, LaunchOptions options, Action then)
        {
            bool show = !_shown && options.Unrecognized.Length > 0;
            _shown = true;
            if (!show)
            {
                then();
                return;
            }
            foreach (var note in options.Unrecognized) Debug.LogWarning("FlagsPopup: " + note);
            if (!options.Windowed && options.StillsDir == null)
            {
                then();
                return;
            }
            var popup = host.AddComponent<FlagsPopup>();
            popup._then = then;
            popup.Show(options.Unrecognized);
        }

        void Show(string[] notes)
        {
            _panel = new ScreenPanel(transform, "Unrecognized flags", "Flags/Flags");
            var list = _panel.Root.Q("notes");
            foreach (var text in notes)
            {
                int colon = text.IndexOf(": ", StringComparison.Ordinal);
                var row = new VisualElement();
                row.AddToClassList("note");
                var arg = new Label(colon > 0 ? text.Substring(0, colon) : text);
                arg.AddToClassList("note__arg");
                row.Add(arg);
                var why = new Label(colon > 0 ? text.Substring(colon + 2) : "");
                why.AddToClassList("note__why");
                row.Add(why);
                list.Add(row);
            }
            _panel.Root.RegisterCallback<PointerDownEvent>(_ => _clicked = true);
            _shownFrame = Time.frameCount;
        }

        void Update()
        {
            if (_panel == null)
            {
                if (Time.frameCount == _closedFrame) return;
                Destroy(this);
                _then();
                return;
            }
            if (Time.frameCount == _shownFrame) return;
            var k = Keyboard.current;
            bool key = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame
                                     || k.spaceKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame);
            if (!_clicked && !key) return;
            _panel.Dispose();
            _panel = null;
            _closedFrame = Time.frameCount;
        }
    }
}
