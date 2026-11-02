using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `duel-ogre` and `duel-dragon`: a boss's duel and each of its telegraphs (`champions stills --scene duel-ogre`). The real
    /// sim calls the boss at the start (<see cref="RunInputs.Duel"/>) to a scripted 120 BPM song whose every beat is strong,
    /// so the boss lands on the first downbeat 4 s in; skeletons stand round the hero for its entrance to shatter. The hero
    /// (a white ring standing in, immortal here) stands its ground while the boss comes and takes each of its moves in turn,
    /// presented by the world, the telegraph and the boss presenters over the edge crowd. Its stills, named by what each
    /// shows: the entrance's impact frame (`entrance`), each move's telegraph three fifths filled (`smash`, `boulders`, ...)
    /// and the duel at its end (`duel`).
    /// </summary>
    sealed class DuelScene : IStillScene, INamedStills
    {
        public const string OgreName = "duel-ogre", DragonName = "duel-dragon";
        public const double Bpm = 120, SongEnd = 40, EndAt = 24, FillShown = 0.6;

        readonly string _boss;
        readonly GameContent _content;
        readonly Palette _palette;
        DuelSceneDriver _driver;

        public DuelScene(string boss, GameContent content, Palette palette)
        {
            _boss = boss;
            _content = content;
            _palette = palette;
        }

        public double[] Times => new[] { EndAt };

        public string Label(int index) => "duel";

        public string Moment => _driver != null ? _driver.Moment : null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<DuelSceneDriver>();
            _driver.Begin(camera, _content, _palette, view, _boss);
            view.Hero = Vector3.zero;
            int cast = _content.Enemies.IndexOf(_boss), skeleton = _content.Enemies.IndexOf("skeleton");
            view.Ids[IdPass.Monster(cast)] = (_boss, "boss", (float)_content.Enemies.Cast[cast].SizeM);
            view.Ids[IdPass.Monster(skeleton)] = ("skeleton", "fodder", (float)_content.Enemies.Cast[skeleton].SizeM);
            view.Ids[IdPass.Effect] = ("hero stand-in, telegraphs and shards", "effect", HeroMarker.WidthM);
        }
    }

    /// <summary>Plays <see cref="DuelScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class DuelSceneDriver : MonoBehaviour
    {
        const int Skeletons = 24;

        struct Standing
        {
            public int Id;
            public Vector2 At;
            public double Killed;
            public Vector2 Blow;
            public float Power;
        }

        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        TelegraphPresenter _telegraphs;
        BossPresenter _boss;
        EdgeCrowd _crowd;
        EdgeShatter _shatter;
        HeroMarker _marker;
        Camera _camera;
        Stills.View _view;
        readonly List<Standing> _standing = new List<Standing>();
        readonly List<(double At, string Name)> _shots = new List<(double, string)>();
        readonly HashSet<BossMove> _seen = new HashSet<BossMove>();
        int _skeleton = -1, _serial;
        uint _skeletonTint;
        double _start, _arrived = double.NaN;
        int _next, _cursor;
        bool _entranceKept;

        /// <summary>The moment to keep on this frame (DuelScene.Moment), or null.</summary>
        public string Moment { get; private set; }

        public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view, string boss)
        {
            _camera = camera;
            _view = view;
            _start = Time.timeAsDouble;
            _entries = new ScriptedSong().Beats(0, DuelScene.SongEnd, DuelScene.Bpm, allStrong: true).Entries();
            var dummies = new Dummy[Skeletons];
            for (int i = 0; i < Skeletons; i++)
            {
                double a = 2.4 * i, r = 5 + i % 4 * 2.5;
                dummies[i] = new Dummy(r * Math.Cos(a), r * Math.Sin(a) * 0.8, 3, content.Enemies.Cast[content.Enemies.IndexOf("skeleton")].RadiusM);
            }
            var config = new RunInputs { Seed = 7, Duel = boss, Loadout = new Upgrade[0], Dummies = dummies }.Build(content);
            config.Hero.Health = 1e9;
            _driver = new SimDriver(config, new FrameClock());
            int layer = camera.gameObject.layer;
            _world = new WorldPresenter(camera, palette);
            _telegraphs = new TelegraphPresenter(palette, config.Enemies, layer);
            var lib = EdgeCutLibrary.Load();
            if (lib != null && lib.Types.Length > 0)
            {
                _crowd = new EdgeCrowd(lib, Skeletons);
                _shatter = new EdgeShatter(lib, _crowd.Palette);
                _skeleton = lib.IndexOf("skeleton");
                if (_skeleton >= 0) _crowd.SetIdCode(_skeleton, IdPass.Monster(content.Enemies.IndexOf("skeleton")));
                _skeletonTint = MonsterInstance.Pack(palette.ForType("skeleton"));
            }
            _boss = new BossPresenter(_crowd, _shatter, config.Enemies, palette, _world.World);
            _marker = new HeroMarker(layer);
            foreach (var d in dummies) _standing.Add(new Standing { Id = _standing.Count, At = new Vector2((float)d.X, (float)d.Y), Killed = double.NaN });
            view.Requeue = () =>
            {
                _crowd?.Queue(_camera);
                _shatter?.Queue(_camera);
                _telegraphs.Queue(_camera);
            };
            Log("Duel: {0} called at the start, {1} BPM, every beat strong; {2} skeletons stand round the hero; {3}", boss,
                DuelScene.Bpm, Skeletons, _crowd == null ? "no baked edge cuts: stand-ins" : "the edge crowd");
        }

        void Update()
        {
            Moment = null;
            if (_driver == null) return;
            double frame = Time.timeAsDouble - _start;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = frame })) return;
            foreach (var e in sim.EventsSince(_cursor))
            {
                _world.Take(e);
                _telegraphs.Take(e);
                _boss.Take(e);
                switch (e.Kind)
                {
                    case SimEventKind.BossArrive:
                        _arrived = e.T;
                        Log("Duel: the boss lands at {0:F3} s, shattering {1}", e.T, e.I2);
                        break;
                    case SimEventKind.BossTelegraph:
                        Log("Duel: {0} part {1} fills {2:F3} to {3:F3} s", e.MarkMove, e.MarkPart, e.T, e.D0);
                        if (e.MarkPart == 0 && _seen.Add(e.MarkMove))
                            _shots.Add((e.T + DuelScene.FillShown * (e.D0 - e.T), e.MarkMove.ToString().ToLowerInvariant()));
                        break;
                    case SimEventKind.Kill:
                        // The skeletons die only to the entrance (the boss then takes a freed slot).
                        if (e.I0 < _standing.Count && (BlowSource)e.I1 == BlowSource.Boss)
                        {
                            var s = _standing[e.I0];
                            s.Killed = e.T;
                            s.Blow = new Vector2((float)e.D0, (float)e.D1);
                            s.Power = (float)e.D2;
                            _standing[e.I0] = s;
                        }
                        break;
                }
            }
            _world.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _marker.Place(feet);
            _world.Present(now, feet, Time.deltaTime);
            _telegraphs.Present(now, _camera);
            WriteSkeletons(now);
            _boss.Present(now, sim);
            if (_crowd != null)
            {
                _crowd.Draw(_camera);
                _shatter.Frame(now, _camera);
            }
            _view.Hero = new Vector3(feet.x, 0, feet.y);
            _view.Impact = _world.World.Rings.Special > 0;
            if (!_entranceKept && now >= _arrived)
            {
                _entranceKept = true;
                Moment = "entrance";
                return;
            }
            for (int i = 0; i < _shots.Count; i++)
            {
                if (now < _shots[i].At) continue;
                Moment = _shots[i].Name;
                _shots.RemoveAt(i);
                return;
            }
        }

        /// <summary>The standing skeletons at song time <paramref name="now"/>: each breaks on the first frame at or after its kill.</summary>
        void WriteSkeletons(double now)
        {
            if (_skeleton < 0) return;
            var t = _crowd.Type(_skeleton);
            int idle = Math.Max(0, t.Clip("idle"));
            var slots = _crowd.Instances(_skeleton, _standing.Count);
            for (int i = 0; i < _standing.Count; i++)
            {
                var s = _standing[i];
                var body = new MonsterInstance
                {
                    Position = new Vector3(s.At.x, 0, s.At.y), Yaw = Mathf.Atan2(-s.At.x, -s.At.y), ClipA = (uint)idle,
                    TimeA = (float)(now + i * 0.37), ClipB = (uint)idle, TimeB = (float)(now + i * 0.37), Blend = 1, Scale = 1,
                    Tint = _skeletonTint, Flags = (uint)CrowdFlags.Alive,
                };
                if (now >= s.Killed)
                {
                    if (s.Power < 0)
                    {
                        slots[i] = new MonsterInstance { Flags = (uint)CrowdFlags.Hidden };
                        continue;
                    }
                    body.Flags = (uint)CrowdFlags.Dying;
                    _shatter.Break(_skeleton, i, body, ++_serial, s.Killed, s.Blow.x, s.Blow.y, s.Power * 0.4f);
                    s.Power = -1;
                    _standing[i] = s;
                }
                slots[i] = body;
            }
        }

        void OnDestroy()
        {
            _marker?.Dispose();
            _boss?.Dispose();
            _telegraphs?.Dispose();
            _shatter?.Dispose();
            _crowd?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
