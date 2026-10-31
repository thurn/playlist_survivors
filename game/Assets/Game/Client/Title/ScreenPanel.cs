using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Title
{
    /// <summary>
    /// A full-screen UI Toolkit screen (the title screen, the calibration screens, the pause
    /// menu): a UXML from Resources on a copy of the editor-made Title/TitlePanel asset (scaled to
    /// 1080p, the theme, and the text's ICU data, which only the editor assigns), so a target set
    /// here never touches the asset. A batch-mode player has no window to draw on, so the panel
    /// draws into a target of its own that the stills keep in place of the camera's view
    /// (<see cref="Stills.Screen"/>, or drawn over it, <see cref="Stills.Overlays"/>, for an overlay)
    /// until the screen closes. A later screen draws over an earlier one.
    /// </summary>
    public sealed class ScreenPanel : IDisposable
    {
        static int _opened;

        readonly UIDocument _doc;
        readonly PanelSettings _settings;
        readonly RenderTexture _target, _previous;
        readonly bool _overlay;

        public VisualElement Root => _doc.rootVisualElement;

        /// <param name="parent">The screen's owner, whose child holds the document.</param>
        /// <param name="uxml">The UXML's Resources path, such as "Title/Title".</param>
        /// <param name="overlay">Whether it draws over the game, so a still keeps the camera's view under it.</param>
        public ScreenPanel(Transform parent, string name, string uxml, bool overlay = false)
        {
            _settings = UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("Title/TitlePanel"));
            _settings.sortingOrder = ++_opened;
            _overlay = overlay;
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                _target = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = name };
                _target.Create();
                _settings.targetTexture = _target;
                // Cleared each frame: an overlay's translucent veil would otherwise build up to opaque.
                _settings.clearColor = true;
                _settings.colorClearValue = Color.clear;
                if (overlay) Stills.Overlays.Add(_target);
                else
                {
                    _previous = Stills.Screen;
                    Stills.Screen = _target;
                }
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = _settings;
            _doc.visualTreeAsset = Resources.Load<VisualTreeAsset>(uxml);
        }

        /// <summary>Shows or hides the whole screen (a screen opened over it hides it).</summary>
        public bool Visible
        {
            set => Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose()
        {
            if (_doc != null) UnityEngine.Object.Destroy(_doc.gameObject);
            if (_target != null && Stills.Screen == _target) Stills.Screen = _previous;
            if (_target != null && _overlay) Stills.Overlays.Remove(_target);
            if (_target != null) _target.Release();
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
        }
    }
}
