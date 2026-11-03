using System;
using System.Collections.Generic;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Retained-horde encounter previews through the real run presentation, with six automatic weapons.</summary>
    sealed class DuelScene : IStillScene, INamedStills
    {
        public const string OgreName = "duel-ogre", DragonName = "duel-dragon";
        public const string OgreDeathName = "duel-ogre-death", DragonDeathName = "duel-dragon-death";
        readonly string _boss, _contentDir;
        readonly GameContent _content;
        readonly Palette _palette;
        readonly MonsterLook _look;
        readonly bool _death;
        DuelSceneDriver _driver;

        public DuelScene(string boss, GameContent content, Palette palette, MonsterLook look, string contentDir, bool death = false)
        { _boss = boss; _content = content; _palette = palette; _look = look; _contentDir = contentDir; _death = death; }
        public double[] Times => new[] { _death ? 16.0 : 48.0 };
        public string Label(int index) => "encounter";
        public string Moment => _driver?.Moment;
        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<DuelSceneDriver>();
            _driver.Begin(camera, _content, _palette, view, _boss, _look, _contentDir, _death);
            for (int c = 0; c < _content.Enemies.Cast.Length; c++)
            {
                var type = _content.Enemies.Cast[c];
                view.Ids[IdPass.Monster(c)] = (type.Id, type.Role.ToString().ToLowerInvariant(), (float)type.SizeM);
            }
            view.Ids[IdPass.Hero] = ("hero", "hero", 1.85f);
            view.Ids[IdPass.Structure] = ("structures", "structure", 3f);
            view.Ids[IdPass.Effect] = ("weapons, pickups and boss telegraphs", "effect", 1.8f);
        }
    }

    [DefaultExecutionOrder(-100)]
    sealed class DuelSceneDriver : MonoBehaviour
    {
        SimDriver _driver;
        RunPresentation _presentation;
        WorldPresenter _world;
        List<StreamEntry> _entries;
        Camera _camera;
        Stills.View _view;
        readonly List<(double At, string Name)> _shots = new List<(double, string)>();
        readonly HashSet<BossMove> _seen = new HashSet<BossMove>();
        double _frame;
        int _next;
        public string Moment { get; private set; }

        public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view, string boss, MonsterLook look, string contentDir, bool death)
        {
            _camera = camera; _view = view;
            _entries = new ScriptedSong().Beats(0, 60, 120, allStrong: true).Entries();
            var config = new RunInputs { Seed = 7, Level = true, SurvivorWeapons = true }.Build(content);
            config.Hero.Health = 1e9;
            config.Level.Bosses = new[] { new LevelBoss { Cast = config.Enemies.IndexOf(boss), CallAtS = 3 } };
            config.Level.Ramp = new Curve((0, 80)); config.Level.Cap = 80; config.Level.PerS = 120;
            config.Level.RingXM = 14; config.Level.RingYM = 10; config.Level.RingJitterM = 1;
            config.Level.Toughness = new Curve((0, 1e9));
            config.Level.Mix = new[] { new MixEntry { Type = config.Enemies.IndexOf("skeleton"), Weight = 5 }, new MixEntry { Type = config.Enemies.IndexOf("troll"), Weight = 1 } };
            config.Structures = content.Structures.Clone();
            config.Enemies.Cast[config.Enemies.IndexOf(boss)].Health = death ? 4 : 1e9;
            _driver = new SimDriver(config, new FrameClock());
            var sim = _driver.Sim;
            foreach (var kind in new[] { WeaponKind.Sword, WeaponKind.Hammer, WeaponKind.Bow, WeaponKind.OrbitBlades, WeaponKind.Lightning, WeaponKind.EmberField })
                sim.State.Build.Set(kind, 5);
            _world = new WorldPresenter(camera, palette);
            _presentation = new RunPresentation(sim, config, content, contentDir, palette, camera, _world, null, null, gameObject, look);
            _presentation.Dispatch.Add("encounter captures", (in SimEvent e) =>
            {
                if (e.Kind == SimEventKind.BossCall) _shots.Add((e.T + 1, "warning"));
                if (e.Kind == SimEventKind.BossArrive) _shots.Add((e.T, "entrance"));
                if (e.Kind == SimEventKind.BossTelegraph && e.MarkPart == 0 && _seen.Add(e.MarkMove))
                {
                    string name = e.MarkMove.ToString().ToLowerInvariant();
                    _shots.Add((e.T + 0.6 * (e.D0 - e.T), name));
                    if (Bosses.IsWave(e.MarkMove)) _shots.Add((e.D0 + 0.5, name + "-travel"));
                }
                if (e.Kind == SimEventKind.BossDeath) _shots.Add((e.T + 0.25, "death"));
            });
            view.Requeue = () => _presentation.Queue(camera);
        }

        void Update()
        {
            Moment = null;
            if (_driver == null) return;
            _frame += 1.0 / Boot.FixedFps;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(_frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = _frame })) return;
            _presentation.Drain();
            double song = _driver.Times.FrameSong;
            _presentation.Present(song, _driver.Times.Frontier, 1f / Boot.FixedFps, _camera);
            sim.Snapshot().HeroAt(song, out double x, out double y);
            _view.Hero = new Vector3((float)x, 0, (float)y);
            _view.Impact = _world.World.Rings.Special > 0;
            for (int i = 0; i < _shots.Count; i++)
            {
                if (song < _shots[i].At) continue;
                Moment = _shots[i].Name; _shots.RemoveAt(i); break;
            }
        }
        void OnDestroy() { _presentation?.Dispose(); _world?.Dispose(); }
    }
}
