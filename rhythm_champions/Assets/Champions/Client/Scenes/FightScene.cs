using System;
using System.Collections.Generic;
using System.Linq;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// A Boss Rush boss's fight for review (`champions boss review ID`, still scene <c>fight-ID</c>; boss-rush.md, Fights):
    /// the fight alone in an empty world through the real run presentation, a hero with six weapons wandering the arena,
    /// immortal. It keeps a still every five seconds (<c>t-05</c> ...), and named moments as the sim decides them: the
    /// entrance, each of the first attacks' telegraphs at 60% filled (<c>attack-NN-MOVE</c>), each phase and the death.
    /// <c>fight-ID-motion</c> keeps 30 frames a second from 4 to 12 s instead, a clip's frames.
    /// </summary>
    sealed class FightScene : IStillScene, INamedStills
    {
        public const string Prefix = "fight-", MotionSuffix = "-motion";
        /// <summary>How long the review plays, and the fight's health: about a minute's fight against the six weapons.</summary>
        public const double LengthS = 60, Health = 6000;
        const int Attacks = 12;

        readonly string _boss, _contentDir;
        readonly GameContent _content;
        readonly Palette _palette;
        readonly MonsterLook _look;
        readonly bool _motion;
        FightSceneDriver _driver;

        /// <summary>The boss a scene name reviews, or null: <c>fight-ID</c> or <c>fight-ID-motion</c>, ID registered.</summary>
        public static string BossOf(string scene, out bool motion)
        {
            motion = false;
            if (scene == null || !scene.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            string id = scene.Substring(Prefix.Length);
            if (id.EndsWith(MotionSuffix, StringComparison.Ordinal))
            {
                motion = true;
                id = id.Substring(0, id.Length - MotionSuffix.Length);
            }
            return BossModules.Has(id) ? id : null;
        }

        public FightScene(string boss, GameContent content, Palette palette, MonsterLook look, string contentDir, bool motion)
        {
            _boss = boss; _content = content; _palette = palette; _look = look; _contentDir = contentDir; _motion = motion;
        }

        public double[] Times => _motion
            ? Enumerable.Range(0, 240).Select(i => 4 + i / 30.0).ToArray()
            : Enumerable.Range(1, (int)(LengthS / 5)).Select(i => i * 5.0).ToArray();

        public string Label(int index) => _motion ? "motion-" + index.ToString("D4") : "t-" + ((index + 1) * 5).ToString("D2");
        public string Moment => _driver?.Moment;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<FightSceneDriver>();
            _driver.Begin(camera, _content, _palette, view, _boss, _look, _contentDir);
            for (int c = 0; c < _content.Enemies.Cast.Length; c++)
            {
                var type = _content.Enemies.Cast[c];
                view.Ids[IdPass.Monster(c)] = (type.Id, type.Role.ToString().ToLowerInvariant(), (float)type.SizeM);
            }
            view.Ids[IdPass.Hero] = ("hero", "hero", 1.85f);
            view.Ids[IdPass.Effect] = ("weapons, pickups and boss telegraphs", "effect", 1.8f);
        }

        [DefaultExecutionOrder(-100)]
        sealed class FightSceneDriver : MonoBehaviour
        {
            SimDriver _driver;
            RunPresentation _presentation;
            WorldPresenter _world;
            List<StreamEntry> _entries;
            Camera _camera;
            Stills.View _view;
            readonly List<(double At, string Name)> _shots = new List<(double, string)>();
            readonly HashSet<int> _attacks = new HashSet<int>();
            double _frame, _nextTarget;
            int _next;
            System.Random _wander;
            public string Moment { get; private set; }

            public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view, string boss, MonsterLook look, string contentDir)
            {
                _camera = camera; _view = view;
                _wander = new System.Random(7);
                _entries = new ScriptedSong().Beats(0, LengthS + 10, 120, allStrong: true).Entries();
                var config = new RunInputs { Seed = 7, SurvivorWeapons = true, Fight = boss }.Build(content);
                config.Hero.Health = 1e9;
                config.FightHealth = Health;
                _driver = new SimDriver(config, new FrameClock());
                var sim = _driver.Sim;
                foreach (var kind in new[] { WeaponKind.Sword, WeaponKind.Hammer, WeaponKind.Bow, WeaponKind.OrbitBlades, WeaponKind.Lightning, WeaponKind.EmberField })
                    sim.State.Build.Set(kind, 5);
                _world = new WorldPresenter(camera, palette);
                _presentation = new RunPresentation(sim, config, content, contentDir, palette, camera, _world, null, null, gameObject, look);
                _presentation.Dispatch.Add("fight captures", (in SimEvent e) =>
                {
                    if (e.Kind == SimEventKind.BossArrive && _shots.All(s => s.Name != "entrance")) _shots.Add((e.T + 0.1, "entrance"));
                    if (e.Kind == SimEventKind.BossTelegraph && e.I0 >= BossFight.FirstMarkId)
                    {
                        // Its attack is the hazard's in the fight's list; the first few attacks each get a still.
                        int attack = -1;
                        foreach (var h in sim.Fight.Hazards) if (h.Id == e.I0) attack = h.Attack;
                        if (attack >= 0 && _attacks.Count < Attacks && _attacks.Add(attack))
                            _shots.Add((e.T + 0.6 * (e.D0 - e.T), $"attack-{_attacks.Count:D2}-{e.MarkMove.ToString().ToLowerInvariant()}"));
                    }
                    if (e.Kind == SimEventKind.BossPhase) _shots.Add((e.T + 0.25, "phase-" + e.I1));
                    if (e.Kind == SimEventKind.BossDeath && sim.Fight.Stage == FightStage.Dead) _shots.Add((e.T + 0.25, "death"));
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
                if (sim.Fight.InFight && sim.Now >= _nextTarget)
                {
                    // The hero wanders the arena so the boss's aimed attacks show their reach.
                    double a = _wander.NextDouble() * 2 * Math.PI, r = 3 + _wander.NextDouble() * 9;
                    sim.Target(sim.Fight.CentreX + r * Math.Cos(a), sim.Fight.CentreY + r * Math.Sin(a), sim.Now);
                    _nextTarget = sim.Now + 2;
                }
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
                    Moment = _shots[i].Name;
                    _shots.RemoveAt(i);
                    break;
                }
            }

            void OnDestroy() { _presentation?.Dispose(); _world?.Dispose(); }
        }
    }
}
