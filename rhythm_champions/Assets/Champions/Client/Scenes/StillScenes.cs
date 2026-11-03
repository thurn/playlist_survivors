using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>A scripted scene for `champions stills`: what it builds, and when it keeps its stills.</summary>
    interface IStillScene
    {
        /// <summary>Seconds after the scene begins at which it keeps a still.</summary>
        double[] Times { get; }

        /// <summary>
        /// A still to keep on this frame beyond <see cref="Times"/>, named so, or null: a moment the scene can't time
        /// ahead (a break's impact frame, which the sim decides). Read after the scene presented its frame.
        /// </summary>
        string Moment { get; }

        /// <summary>Builds the scene under <paramref name="root"/>, frames the camera, and fills the view's hero and
        /// ID table.</summary>
        void Begin(Transform root, Camera camera, Stills.View view);
    }

    /// <summary>A scene whose stills are named by what each shows (SCENE-LABEL) rather than by their times.</summary>
    interface INamedStills
    {
        /// <summary>The label of the still at <see cref="IStillScene.Times"/>[<paramref name="index"/>].</summary>
        string Label(int index);
    }

    /// <summary>
    /// The scripted scenes (`--capture-stills DIR --stills-scenes NAMES`, `all` for every one; plan.md, Look Metrics),
    /// played one after another in one launch: each is built, kept at its times as SCENE-T stills (<see
    /// cref="Stills.Keep"/>) and torn down. Scenes join as the systems they show land.
    /// </summary>
    public sealed class StillScenes
    {
        /// <summary>The scenes by name, in the order `all` plays them.</summary>
        static readonly (string Name, Func<StillScenes, IStillScene> Make)[] Known =
        {
            (PaletteScene.Name, s => new PaletteScene(s._content, s._palette, s._look)),
            ("sword-feel", s => new SwordFeelScene(s._content, s._palette, s._contentDir)),
            (MovementDashScene.Name, s => new MovementDashScene(s._palette, s._contentDir)),
            ("magic-combat", s => new MagicCombatScene(s._palette, s._contentDir)),
            ("hud-readability", s => new PickupScene(s._content, s._palette, s._look, hud: true)),
            ("pickups", s => new PickupScene(s._content, s._palette, s._look)),
            (WorldScene.Name, s => new WorldScene(s._palette)),
            (StructureScene.Name, s => new StructureScene(s._content, s._palette, s._look)),
            (GalleryScene.Name, s => new GalleryScene(s._palette, s._contentDir)),
            (RangedGalleryScene.Name, s => new RangedGalleryScene(s._content, s._palette, s._contentDir)),
            ("full-ogre-storm", s => new DuelScene("fatOgre", s._content, s._palette, s._look, s._contentDir, stress: "storm")),
            ("full-ogre-barrage", s => new DuelScene("fatOgre", s._content, s._palette, s._look, s._contentDir, stress: "barrage")),
            ("full-conductor-storm", s => new DuelScene("conductor", s._content, s._palette, s._look, s._contentDir, stress: "storm")),
            ("full-conductor-barrage", s => new DuelScene("conductor", s._content, s._palette, s._look, s._contentDir, stress: "barrage")),
            ("full-dragon-storm", s => new DuelScene("dragon", s._content, s._palette, s._look, s._contentDir, stress: "storm")),
            ("full-dragon-barrage", s => new DuelScene("dragon", s._content, s._palette, s._look, s._contentDir, stress: "barrage")),
            (DuelScene.OgreName, s => new DuelScene("fatOgre", s._content, s._palette, s._look, s._contentDir)),
            ("duel-conductor", s => new DuelScene("conductor", s._content, s._palette, s._look, s._contentDir)),
            (DuelScene.DragonName, s => new DuelScene("dragon", s._content, s._palette, s._look, s._contentDir)),
            (DuelScene.OgreDeathName, s => new DuelScene("fatOgre", s._content, s._palette, s._look, s._contentDir, death: true)),
            (DuelScene.DragonDeathName, s => new DuelScene("dragon", s._content, s._palette, s._look, s._contentDir, death: true)),
            (LevelScene.Name, s => new LevelScene(s._content, s._palette, s._contentDir, s._look)),
            (UiScene.Name, s => new UiScene(s._content, s._palette, s._contentDir)),
            (HeroScene.Name, s => new HeroScene(s._content, s._palette, s._contentDir, s._look)),
        };

        readonly Stills _stills;
        readonly Camera _camera;
        readonly GameContent _content;
        readonly Palette _palette;
        readonly string _contentDir;
        readonly MonsterLook _look;
        readonly Queue<string> _pending;
        string _name;
        IStillScene _scene;
        Stills.View _view;
        GameObject _root;
        double _start;
        int _next;

        /// <param name="names">Scene names, or `all`; an unknown one throws.</param>
        /// <param name="contentDir">The content folder (the palette, the cast, the level).</param>
        /// <param name="look">The monsters' drawing (`--look trailer`).</param>
        public StillScenes(Stills stills, Camera camera, IEnumerable<string> names, string contentDir, MonsterLook look = MonsterLook.Default)
        {
            _look = look;
            _stills = stills;
            _camera = camera;
            _content = GameContent.Load(contentDir);
            _palette = Palette.Load(contentDir);
            _contentDir = contentDir;
            _pending = new Queue<string>();
            foreach (string n in names)
            {
                if (n == "all")
                {
                    // Peak-load sweeps and dense motion captures are explicit operator tools,
                    // not the CI stills set's per-image look analysis.
                    foreach (var k in Known)
                        if (!k.Name.StartsWith("full-", StringComparison.Ordinal) &&
                            k.Name != "sword-feel" && k.Name != "hud-readability")
                            _pending.Enqueue(k.Name);
                }
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
                _scene = Array.Find(Known, k => k.Name == _name).Make(this);
                _root = new GameObject("Still scene " + _name);
                _view = new Stills.View();
                _scene.Begin(_root.transform, _camera, _view);
                _stills.Requeue = _view.Requeue;
                _start = t;
                _next = 0;
                Debug.Log($"StillScenes: {_name} began at t={t:F3}");
            }
            var times = _scene.Times;
            while (_next < times.Length && t - _start + 1e-6 >= times[_next])
            {
                string label = _scene is INamedStills named ? named.Label(_next) : times[_next].ToString("0.000", CultureInfo.InvariantCulture);
                _stills.Keep(_name + "-" + label, _view);
                _next++;
            }
            string moment = _scene.Moment;
            if (moment != null) _stills.Keep(_name + "-" + moment, _view);
            if (_next < times.Length) return;
            _stills.Requeue = null;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root); // an EditMode test's
            _scene = null;
        }
    }
}
