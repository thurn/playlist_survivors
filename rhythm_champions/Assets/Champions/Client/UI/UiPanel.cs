using System;
using Champions.Render;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>
    /// One UI Toolkit layer (a menu, the cards, the HUD), ported from game/'s ScreenPanel: a document on a copy of the
    /// editor-made Resources/UI/Panel asset (scaled from 1920×1080, the theme, and the text's ICU data, which only the
    /// editor assigns), the game's stylesheet (Resources/UI/Champions.uss) on its root, layered by its order. A
    /// batch-mode player has no window, so the panel then draws into a 1920×1080 target of its own that the stills draw
    /// over the frame while the layer shows (<see cref="Stills.Overlays"/>). Elements are built in code: the screens
    /// fill <see cref="Root"/>.
    /// </summary>
    public sealed class UiPanel : IDisposable
    {
        public const string SettingsPath = "UI/Panel", StylePath = "UI/Champions";

        readonly UIDocument _doc;
        readonly PanelSettings _settings;
        readonly RenderTexture _target;
        bool _shown = true;

        public VisualElement Root => _doc.rootVisualElement;

        /// <param name="parent">The owner, whose child holds the document.</param>
        /// <param name="order">Higher layers draw over lower ones and take the pointer first.</param>
        public UiPanel(Transform parent, string name, int order)
        {
            _settings = UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>(SettingsPath)
                                                       ?? throw new InvalidOperationException($"UiPanel: no panel settings at Resources/{SettingsPath}"));
            _settings.name = name;
            _settings.sortingOrder = order;
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                _target = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = name };
                _target.Create();
                _settings.targetTexture = _target;
                // Cleared each frame, so a translucent veil never builds up to opaque.
                _settings.clearColor = true;
                _settings.colorClearValue = Color.clear;
                Stills.Overlays.Add(_target);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = _settings;
            Root.styleSheets.Add(Resources.Load<StyleSheet>(StylePath));
            Root.AddToClassList("layer");
            Root.pickingMode = PickingMode.Ignore; // the layer itself is not a target: its screens are
        }

        /// <summary>Shows or hides the whole layer (hidden, it takes no pointer and no still shows it).</summary>
        public bool Shown
        {
            get => _shown;
            set
            {
                if (_shown == value) return;
                _shown = value;
                Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (_target == null) return;
                if (value) Stills.Overlays.Add(_target);
                else Stills.Overlays.Remove(_target);
            }
        }

        public void Dispose()
        {
            if (_doc != null) Destroy(_doc.gameObject);
            if (_target != null)
            {
                Stills.Overlays.Remove(_target);
                _target.Release();
            }
            if (_settings != null) Destroy(_settings);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o); // an EditMode test's
        }
    }
}
