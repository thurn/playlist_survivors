using System.Globalization;
using System.Text;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Loop
{
    /// <summary>
    /// The debug overlay (unity-game.md, Debug Surfaces): song, heard and frame time, the frontier
    /// and lead; the stream's next beats with their known_at; press strides as they happen (when
    /// each was taken, and how far before that it is drawn from); the frame's and sim's timings.
    /// F3 toggles it; it starts hidden, so stills leave it out unless
    /// `--overlay` asks for it. It is text in the camera's view, in front of everything, so the
    /// windowless stills draw it too. It builds its text only while shown.
    /// </summary>
    public sealed class DebugOverlay : IPresenter
    {
        const int PressStrides = 4, NextBeats = 4;
        const float Distance = 1f, Margin = 0.03f;

        readonly Camera _camera;
        readonly SimDriver _driver;
        readonly TextMesh _text;
        readonly StringBuilder _sb = new StringBuilder(1024);
        readonly double[] _pressAt = new double[PressStrides];
        readonly double[] _pressLag = new double[PressStrides];
        int _pressCount;

        public DebugOverlay(Camera camera, SimDriver driver, bool shown)
        {
            _camera = camera;
            _driver = driver;
            var go = new GameObject("Debug overlay");
            if (camera != null) go.transform.SetParent(camera.transform, false);
            _text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            _text.fontSize = 48;
            _text.characterSize = 0.0055f;
            _text.anchor = TextAnchor.UpperLeft;
            _text.color = new Color(0.6f, 1f, 0.7f);
            go.SetActive(shown);
        }

        public bool Shown => _text.gameObject.activeSelf;

        /// <summary>Toggles the overlay when F3 went down this frame.</summary>
        public void Toggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) _text.gameObject.SetActive(!Shown);
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind != SimEventKind.HeroStep || !e.AsHeroStep.Pressed) return;
            int slot = _pressCount++ % PressStrides;
            _pressAt[slot] = e.T;
            _pressLag[slot] = e.T - e.AsHeroStep.Start;
        }

        public void Present(in PresentFrame frame)
        {
            if (!Shown) return;
            Place();
            var c = CultureInfo.InvariantCulture;
            var t = _driver.Times;
            var sim = _driver.Sim;
            _sb.Clear();
            _sb.AppendFormat(c, "song {0:F3}  heard {1:F3}  frame {2:F3}\n", t.Song, t.Heard, t.FrameSong);
            _sb.AppendFormat(c, "frontier {0:F3}  lead {1:F1} ms", t.Frontier, 1000 * t.Lead);
            if (frame.AtFrontier) _sb.Append("  SHOWING THE FRONTIER");
            _sb.AppendFormat(c, "  ({0} frames)\n", _driver.FramesAtFrontier);
            _sb.AppendFormat(c, "beat {0}  bar {1}  stride {2:F3} s\n", frame.State.Beat, frame.State.Bar, frame.State.StridePeriod);
            _sb.Append("next beats:");
            var beats = sim.Stream.Beats;
            int shown = 0;
            for (int i = beats.Length - 1; i >= 0 && beats[i].T > t.Heard; i--) shown++;
            for (int i = beats.Length - shown; i < beats.Length && i < beats.Length - shown + NextBeats; i++)
                _sb.AppendFormat(c, "  {0:F3} (known {1:F3})", beats[i].T, sim.Stream.KnownAt(EntryKind.Beat, i));
            _sb.Append("\npress strides:");
            for (int k = 0; k < PressStrides && k < _pressCount; k++)
            {
                int slot = (_pressCount - 1 - k) % PressStrides;
                _sb.AppendFormat(c, "  {0:F3} (drawn {1:F0} ms before)", _pressAt[slot], 1000 * _pressLag[slot]);
            }
            _sb.AppendFormat(c, "\nframe {0:F1} ms  loop {1:F2} ms  sim {2:F3} ms  tick {3}  events {4}",
                1000 * Time.unscaledDeltaTime, _driver.FrameMs, _driver.StepMs, sim.State.Tick, sim.EventCount);
            _text.text = _sb.ToString();
        }

        /// <summary>Keeps the text at the view's top left, whatever the aspect (a still's is 16:9).</summary>
        void Place()
        {
            if (_camera == null) return;
            float aspect = Application.isBatchMode ? (float)Stills.Width / Stills.Height : _camera.aspect;
            float h = Distance * Mathf.Tan(0.5f * _camera.fieldOfView * Mathf.Deg2Rad);
            var t = _text.transform;
            t.localPosition = new Vector3(-h * aspect * (1 - Margin), h * (1 - Margin), Distance);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (h / 0.58f); // the same size on screen at any field of view
        }
    }
}
