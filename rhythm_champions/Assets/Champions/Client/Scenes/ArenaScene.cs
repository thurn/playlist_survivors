using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `arenas` and `arena-scroll`: the Boss Rush arenas (boss-rush.md, Arenas; `champions stills --scene arenas`). The real
    /// sim plays a scripted arena (<see cref="ArenaScript"/>) to a scripted song at 120 BPM, drawn by the arena presenter over
    /// the world presenter, the camera framing it, the hero's feet a white ring (<see cref="HeroMarker"/>):
    /// - `arenas`: the test bed (Content/arenas.json's `testbed`) forms round the hero at 1 s over four beats; the hero walks
    ///   up its east side; at 5-7.5 s a tile rises, the centre tile sinks, the east shelf breaks away, a web and a wall grow
    ///   and a crate crumbles, each on a beat; at 10 s it retracts. Stills: forming, standing, each change, retracting.
    /// - `arena-scroll`: the long hall (`long-hall`, 72 m) forms round the hero at its west end and the hero walks east down
    ///   it: the view eases out to its farthest and scrolls, short of the walls at either end.
    /// - `kit-KIT` (<see cref="KitOf"/>): an act kit (boss-rush.md, Environments) in a 26 m arena of its own dressed by it, each
    ///   of its prop kinds on a ring round the middle, its floor treatment and backdrop; it forms round the hero at 1 s and
    ///   the hero walks across it.
    /// </summary>
    sealed class ArenaScene : IStillScene, INamedStills
    {
        public const string Name = "arenas", ScrollName = "arena-scroll", KitPrefix = "kit-";
        public const double Bpm = 120, SongEnd = 30;

        readonly GameContent _content;
        readonly Palette _palette;
        readonly bool _scroll;
        readonly string _kit;

        public ArenaScene(GameContent content, Palette palette, bool scroll, string kit = null)
        {
            _content = content;
            _palette = palette;
            _scroll = scroll;
            _kit = kit;
        }

        /// <summary>The kit a `kit-KIT` scene name shows, or null when it names none in the content.</summary>
        public static string KitOf(string name, GameContent content)
        {
            if (name == null || !name.StartsWith(KitPrefix, System.StringComparison.Ordinal)) return null;
            string id = name.Substring(KitPrefix.Length);
            return content.Arenas.Kit(id) != null ? id : null;
        }

        static readonly (double T, string Label)[] Bed =
        {
            (1.9, "forming"), (4.0, "standing"), (5.6, "tile-raised"), (6.1, "pit-sunk"), (6.6, "shelf-broken"),
            (7.6, "web-and-wall"), (8.4, "crate-crumbled"), (10.9, "retracting"),
        };

        static readonly (double T, string Label)[] Hall = { (1.4, "forming"), (3.5, "west-end"), (8.5, "scrolling"), (14.5, "east-end") };

        static readonly (double T, string Label)[] KitStills = { (1.9, "forming"), (3.5, "standing"), (7.0, "walk") };

        (double T, string Label)[] Stills => _kit != null ? KitStills : _scroll ? Hall : Bed;

        public double[] Times
        {
            get
            {
                var s = Stills;
                var t = new double[s.Length];
                for (int i = 0; i < s.Length; i++) t[i] = s[i].T;
                return t;
            }
        }

        public string Label(int index) => Stills[index].Label;

        public string Moment => null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            root.gameObject.AddComponent<ArenaSceneDriver>().Begin(camera, _content, _palette, view, _scroll, _kit);
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Effect] = ("hero stand-in", "effect", HeroMarker.WidthM);
            view.Ids[IdPass.Structure] = ("arena", "structure", (float)_content.Arenas.WallHeightM);
        }

        /// <summary>The test bed's script: form at 1 s, the changes on the beats from 5 s, retract at 10 s.</summary>
        internal static ArenaScript BedScript() => new ArenaScript
        {
            Arena = "testbed",
            FormAtS = 1,
            Steps = new[]
            {
                new ArenaStep { AtS = 5, Op = ArenaOp.Raise, Zone = "tile-w" },
                new ArenaStep { AtS = 5.5, Op = ArenaOp.Sink, Zone = "tile-c" },
                new ArenaStep { AtS = 6, Op = ArenaOp.Break, Zone = "shelf-e" },
                new ArenaStep { AtS = 6.5, Op = ArenaOp.Grow, Shape = ArenaShape.Circle(-9, -3, 2.5, 8), Obstacle = ArenaObstacleKind.Web, GrowS = 0.5 },
                new ArenaStep { AtS = 7, Op = ArenaOp.Grow, Shape = ArenaShape.Box(9, 0, 1, 6), Obstacle = ArenaObstacleKind.Wall, GrowS = 0.5 },
                new ArenaStep { AtS = 8, Op = ArenaOp.Crumble, Index = 2 },
                new ArenaStep { AtS = 10, Op = ArenaOp.Retract },
            },
        };

        internal static ArenaScript HallScript() => new ArenaScript { Arena = "long-hall", FormAtS = 0.5 };

        /// <summary>The kit's show arena (added to the scene's own rules, never the content's): its prop kinds round the middle.</summary>
        internal static ArenaDef KitArena(ArenaRules rules, string kit)
        {
            var kinds = new List<int>();
            for (int i = 0; i < rules.Props.Length; i++)
                if (rules.Props[i].Kit == kit) kinds.Add(i);
            var props = new ArenaPropPlacement[kinds.Count];
            for (int k = 0; k < kinds.Count; k++)
            {
                double a = 2 * System.Math.PI * (k + 0.5) / kinds.Count + System.Math.PI / 2;
                props[k] = new ArenaPropPlacement { Kind = kinds[k], X = 7.5 * System.Math.Cos(a), Y = 7.5 * System.Math.Sin(a) + 1, Degrees = 25 * k, Blocks = true, BlocksShots = true };
            }
            return new ArenaDef
            {
                Id = KitPrefix + kit, Kit = kit, Outline = ArenaShape.Circle(0, 0, 13, ArenaRules.CircleSides), HeroStartY = -9, BossStartY = 9, Props = props,
            };
        }
    }

    /// <summary>Plays <see cref="ArenaScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class ArenaSceneDriver : MonoBehaviour
    {
        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        ArenaPresenter _arena;
        HeroMarker _marker;
        Camera _camera;
        Stills.View _view;
        double _start;
        int _next, _cursor;
        bool _scroll, _kitScene;
        int _walk;

        public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view, bool scroll, string kit)
        {
            _camera = camera;
            _view = view;
            _scroll = scroll;
            _start = Time.timeAsDouble;
            _entries = new ScriptedSong().Beats(0, ArenaScene.SongEnd, ArenaScene.Bpm).Entries();
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(content);
            config.Arenas = content.Arenas.Clone();
            if (kit != null)
            {
                var show = ArenaScene.KitArena(config.Arenas, kit);
                config.Arenas.Arenas = config.Arenas.Arenas.Concat(new[] { show }).ToArray();
                config.ArenaScript = new ArenaScript { Arena = show.Id, FormAtS = 1 };
            }
            else config.ArenaScript = scroll ? ArenaScene.HallScript() : ArenaScene.BedScript();
            _kitScene = kit != null;
            config.Hero.Health = 1e9;
            _driver = new SimDriver(config, new FrameClock());
            _world = new WorldPresenter(camera, palette);
            _arena = new ArenaPresenter(config.Arenas, palette, _world.World, _world.World.Camera, camera.gameObject.layer, content.BossRush);
            view.Requeue = () => _arena.Queue(camera);
            _marker = new HeroMarker(camera.gameObject.layer);
            Debug.Log($"Arenas: the scripted {config.ArenaScript.Arena} at {ArenaScene.Bpm} BPM");
        }

        void Update()
        {
            if (_driver == null) return;
            double frame = Time.timeAsDouble - _start;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            // The walk: up the bed's east side, or east down the hall.
            if (_walk == 0 && frame >= (_scroll ? 3.6 : 2.5))
            {
                _walk = 1;
                if (_kitScene) sim.Target(-3, 9, _driver.Times.Heard);
                else if (_scroll) sim.Target(50, 0, _driver.Times.Heard);
                else sim.Target(12, 0, _driver.Times.Heard);
            }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = frame })) return;
            foreach (var e in sim.EventsSince(_cursor))
            {
                _world.Take(e);
                _arena.Take(e);
                if (e.Kind == SimEventKind.ArenaForm) Log("Arenas: arena {0} forms at {1:F3} s over {2:F3}-{3:F3} s, origin ({4:F2}, {5:F2})", e.I0, e.T, e.D2, e.D3, e.D0, e.D1);
                else if (e.Kind == SimEventKind.ArenaZone) Log("Arenas: zone {0} {1} -> {2} at {3:F3} s", e.I0, (ArenaZoneState)e.I2, (ArenaZoneState)e.I1, e.T);
                else if (e.Kind == SimEventKind.ArenaObstacle) Log("Arenas: obstacle {0} ({1}) {2} at {3:F3} s", e.I0, (ArenaObstacleKind)e.I1, e.I2 == 1 ? "grows" : "clears", e.T);
                else if (e.Kind == SimEventKind.ArenaProp) Log("Arenas: prop {0} hits left {1} at {2:F3} s", e.I0, e.I1, e.T);
                else if (e.Kind == SimEventKind.ArenaRetract) Log("Arenas: retracts at {0:F3} s until {1:F3} s", e.T, e.D1);
            }
            _world.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _marker.Place(feet);
            _arena.Present(now, sim, feet, _camera);
            _world.Present(now, feet, Time.deltaTime);
            _view.Hero = new Vector3(feet.x, 0, feet.y);
        }

        void OnDestroy()
        {
            _marker?.Dispose();
            _arena?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
