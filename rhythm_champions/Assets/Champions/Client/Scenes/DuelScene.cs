using System;
using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Hero;
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
    /// (immortal here) stands its ground while the boss comes and takes each of its moves in turn, presented by the world,
    /// the telegraph, the boss and the hero presenters: the boss and the skeletons in the trailer's crowd, the knight in
    /// white (`--look trailer`: exactly as the trailer drew them; stand-ins without baked monsters). Its stills, named by
    /// what each shows: the entrance's impact frame (`entrance`), each move's telegraph three fifths filled (`smash`,
    /// `boulders`, ...) and the duel at its end (`duel`). `duel-ogre-death` and `duel-dragon-death` play the same duel with
    /// the hero's Sword Slash against a boss of <see cref="DeathHealth"/>, for its death: its break's frame (`break`), the
    /// shards flying (`break-0.25`, `break-0.75`) and the field after (`after`).
    /// </summary>
    sealed class DuelScene : IStillScene, INamedStills
    {
        public const string OgreName = "duel-ogre", DragonName = "duel-dragon";
        public const string OgreDeathName = "duel-ogre-death", DragonDeathName = "duel-dragon-death";
        public const double Bpm = 120, SongEnd = 40, EndAt = 24, FillShown = 0.6;
        /// <summary>A death scene's boss's health (Sword Slash's base hits), and when the scene ends, well after it falls.</summary>
        public const double DeathHealth = 4, DeathEndAt = 16;
        /// <summary>A death scene's stills after the break's frame (s).</summary>
        internal static readonly double[] AfterDeath = { 0.25, 0.75 };

        readonly string _boss;
        readonly GameContent _content;
        readonly Palette _palette;
        readonly MonsterLook _look;
        readonly bool _death;
        DuelSceneDriver _driver;

        public DuelScene(string boss, GameContent content, Palette palette, MonsterLook look = MonsterLook.Default, bool death = false)
        {
            _boss = boss;
            _content = content;
            _palette = palette;
            _look = look;
            _death = death;
        }

        public double[] Times => new[] { _death ? DeathEndAt : EndAt };

        public string Label(int index) => _death ? "after" : "duel";

        public string Moment => _driver != null ? _driver.Moment : null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<DuelSceneDriver>();
            _driver.Begin(camera, _content, _palette, view, _boss, _look, _death);
            view.Hero = Vector3.zero;
            int cast = _content.Enemies.IndexOf(_boss), skeleton = _content.Enemies.IndexOf("skeleton");
            view.Ids[IdPass.Monster(cast)] = (_boss, "boss", (float)_content.Enemies.Cast[cast].SizeM);
            view.Ids[IdPass.Monster(skeleton)] = ("skeleton", "fodder", (float)_content.Enemies.Cast[skeleton].SizeM);
            view.Ids[IdPass.Hero] = ("hero", "hero", HeroPresenter.HeightM);
            view.Ids[IdPass.Effect] = ("telegraphs and shards", "effect", HeroPresenter.HeightM);
        }
    }

    /// <summary>Plays <see cref="DuelScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class DuelSceneDriver : MonoBehaviour
    {
        const int Skeletons = 24;

        struct Standing
        {
            public CrowdMob Mob;
            public double Killed;
            public Vector2 Blow;
            public float Power;
            public bool Broken;
        }

        SimDriver _driver;
        List<StreamEntry> _entries;
        WorldPresenter _world;
        TelegraphPresenter _telegraphs;
        ICrowdView _crowd;
        BossPresenter _boss;
        HeroPresenter _hero;
        Camera _camera;
        Stills.View _view;
        readonly List<Standing> _standing = new List<Standing>();
        readonly List<(double At, string Name)> _shots = new List<(double, string)>();
        readonly HashSet<BossMove> _seen = new HashSet<BossMove>();
        double _start, _arrived = double.NaN;
        int _next, _cursor;
        bool _death, _entranceKept;

        /// <summary>The moment to keep on this frame (DuelScene.Moment), or null.</summary>
        public string Moment { get; private set; }

        public void Begin(Camera camera, GameContent content, Palette palette, Stills.View view, string boss, MonsterLook look, bool death)
        {
            _camera = camera;
            _view = view;
            _death = death;
            _start = Time.timeAsDouble;
            _entries = new ScriptedSong().Beats(0, DuelScene.SongEnd, DuelScene.Bpm, allStrong: true).Entries();
            int skeleton = content.Enemies.IndexOf("skeleton");
            var skeletonType = content.Enemies.Cast[skeleton];
            var dummies = new Dummy[Skeletons];
            for (int i = 0; i < Skeletons; i++)
            {
                double a = 2.4 * i, r = 5 + i % 4 * 2.5;
                dummies[i] = new Dummy(r * Math.Cos(a), r * Math.Sin(a) * 0.8, 3, skeletonType.RadiusM);
            }
            var loadout = death ? RunInputs.StartLoadout() : new Upgrade[0];
            var config = new RunInputs { Seed = 7, Duel = boss, Loadout = loadout, Dummies = dummies }.Build(content);
            config.Hero.Health = 1e9;
            if (death) config.Enemies.Cast[config.Enemies.IndexOf(boss)].Health = DuelScene.DeathHealth;
            _driver = new SimDriver(config, new FrameClock());
            int layer = camera.gameObject.layer;
            _world = new WorldPresenter(camera, palette);
            _telegraphs = new TelegraphPresenter(palette, config.Enemies, layer);
            var trailer = TrailerCrowd.Create(look, config.Enemies, palette);
            _crowd = (ICrowdView)trailer ?? new StandInCrowd(config.Enemies, palette, layer);
            _boss = new BossPresenter(trailer, config.Enemies, palette, _world.World, layer);
            _hero = new HeroPresenter(palette, content.Hero.SpeedMps, layer, look);
            for (int i = 0; i < dummies.Length; i++)
            {
                var d = dummies[i];
                var mob = new CrowdMob
                {
                    Id = i, Cast = skeleton, Position = new Vector3((float)d.X, 0, (float)d.Y),
                    Yaw = Mathf.Atan2(-(float)d.X, -(float)d.Y), Action = CrowdAction.Idle, Serial = i + 1,
                    StepAge = CrowdMob.Long, KickAge = CrowdMob.Long,
                };
                _standing.Add(new Standing { Mob = mob, Killed = double.NaN });
            }
            view.Requeue = () =>
            {
                _crowd.Queue(_camera);
                _hero.Queue(_camera);
                _telegraphs.Queue(_camera);
            };
            Log("Duel: {0} called at the start, {1} BPM, every beat strong; {2} skeletons stand round the hero; {3}{4}", boss,
                DuelScene.Bpm, Skeletons, trailer != null ? $"the trailer's crowd ({MonsterLooks.Name(look)} look)" : "no baked monsters: stand-ins",
                death ? $"; Sword Slash against {DuelScene.DeathHealth} health, for its death" : "");
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
                        if (!_death && e.MarkPart == 0 && _seen.Add(e.MarkMove))
                            _shots.Add((e.T + DuelScene.FillShown * (e.D0 - e.T), e.MarkMove.ToString().ToLowerInvariant()));
                        break;
                    case SimEventKind.BossDeath:
                        Log("Duel: the boss falls at {0:F3} s", e.T);
                        _shots.Add((e.T, "break"));
                        foreach (double after in DuelScene.AfterDeath)
                            _shots.Add((e.T + after, "break-" + after.ToString("0.00", CultureInfo.InvariantCulture)));
                        break;
                    case SimEventKind.Swing:
                        _hero.Swing(e.I0 == 1 ? SwingKind.Spin : SwingKind.Slash, e.D0, new Vector2((float)e.D1, (float)e.D2));
                        break;
                    case SimEventKind.Kill:
                        // The skeletons die only to the entrance, thrown away from the boss (the boss then takes a freed slot).
                        if (e.I0 < _standing.Count && (BlowSource)e.I1 == BlowSource.Boss)
                        {
                            var s = _standing[e.I0];
                            s.Killed = e.T;
                            s.Blow = new Vector2((float)e.D0, (float)e.D1);
                            s.Power = (float)e.D2;
                            _standing[e.I0] = s;
                        }
                        break;
                    case SimEventKind.HeroMove:
                        _hero.Walk(e.D0, new Vector2((float)e.D3, (float)e.D4));
                        break;
                    case SimEventKind.HeroHit when e.D0 > 0:
                        _hero.Hurt(e.T);
                        break;
                }
            }
            _world.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            _world.Present(now, feet, Time.deltaTime);
            _crowd.Begin(now);
            WriteSkeletons(now);
            _boss.Present(now, sim);
            _crowd.Draw(now, _camera);
            _telegraphs.Present(now, _camera);
            _hero.Present(now, feet, Time.deltaTime, _camera);
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
            for (int i = 0; i < _standing.Count; i++)
            {
                var s = _standing[i];
                if (s.Broken) continue;
                var mob = s.Mob;
                mob.ActionTime = (float)now;
                if (now >= s.Killed)
                {
                    _crowd.Break(mob, s.Killed, s.Blow, s.Power);
                    s.Broken = true;
                    _standing[i] = s;
                }
                else _crowd.Mob(mob);
            }
        }

        void OnDestroy()
        {
            _hero?.Dispose();
            _boss?.Dispose();
            _crowd?.Dispose();
            _telegraphs?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
