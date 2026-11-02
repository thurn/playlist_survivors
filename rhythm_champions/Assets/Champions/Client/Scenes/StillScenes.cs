using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>A scripted scene for `champions stills`: what it builds, and when it keeps its stills.</summary>
    interface IStillScene
    {
        /// <summary>Seconds after the scene begins at which it keeps a still.</summary>
        double[] Times { get; }

        /// <summary>Builds the scene under <paramref name="root"/>, frames the camera, and fills the view's hero and
        /// ID table.</summary>
        void Begin(Transform root, Camera camera, Stills.View view);
    }

    /// <summary>
    /// The scripted scenes (`--capture-stills DIR --stills-scenes NAMES`, `all` for every one; plan.md, Look Metrics),
    /// played one after another in one launch: each is built, kept at its times as SCENE-T stills (<see
    /// cref="Stills.Keep"/>) and torn down. Scenes join as the systems they show land.
    /// </summary>
    public sealed class StillScenes
    {
        /// <summary>The scenes by name, in the order `all` plays them.</summary>
        static readonly (string Name, Func<GameContent, Palette, IStillScene> Make)[] Known =
        {
            (PaletteScene.Name, (content, palette) => new PaletteScene(content, palette)),
            (WorldScene.Name, (content, palette) => new WorldScene(palette)),
        };

        readonly Stills _stills;
        readonly Camera _camera;
        readonly GameContent _content;
        readonly Palette _palette;
        readonly Queue<string> _pending;
        string _name;
        IStillScene _scene;
        Stills.View _view;
        GameObject _root;
        double _start;
        int _next;

        /// <param name="names">Scene names, or `all`; an unknown one throws.</param>
        /// <param name="contentDir">The content folder (the palette, the cast, the level).</param>
        public StillScenes(Stills stills, Camera camera, IEnumerable<string> names, string contentDir)
        {
            _stills = stills;
            _camera = camera;
            _content = GameContent.Load(contentDir);
            _palette = Palette.Load(contentDir);
            _pending = new Queue<string>();
            foreach (string n in names)
            {
                if (n == "all")
                    foreach (var k in Known) _pending.Enqueue(k.Name);
                else if (Array.Exists(Known, k => k.Name == n)) _pending.Enqueue(n);
                else throw new ArgumentException($"--stills-scenes: no scene {n} (scenes: all, {string.Join(", ", Array.ConvertAll(Known, k => k.Name))})");
            }
        }

        public bool Done => _scene == null && _pending.Count == 0;

        /// <summary>At time `t`: begins the next scene if none is playing, keeps the playing scene's stills due, and
        /// ends it after its last.</summary>
        public void Update(double t)
        {
            if (_scene == null)
            {
                if (_pending.Count == 0) return;
                _name = _pending.Dequeue();
                _scene = Array.Find(Known, k => k.Name == _name).Make(_content, _palette);
                _root = new GameObject("Still scene " + _name);
                _view = new Stills.View();
                _scene.Begin(_root.transform, _camera, _view);
                _start = t;
                _next = 0;
                Debug.Log($"StillScenes: {_name} began at t={t:F3}");
            }
            var times = _scene.Times;
            while (_next < times.Length && t - _start + 1e-6 >= times[_next])
                _stills.Keep(_name + "-" + times[_next++].ToString("0.000", CultureInfo.InvariantCulture), _view);
            if (_next < times.Length) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root); // an EditMode test's
            _scene = null;
        }
    }
}
