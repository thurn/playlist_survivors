using System;
using System.Collections.Generic;
using Champions.Client.Hero;
using Champions.Client.Sound;
using Champions.Client.UI;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Loop
{
    /// <summary>How a run's sounds are scheduled: on the music's clock live, or silently on the frames' under --fixed-time.</summary>
    public sealed class SoundClock
    {
        public IGameClock Clock;
        public Func<double> DspNow;
        public bool Audible;
        /// <summary>The music's follower gain, or null (full level).</summary>
        public Func<float> Follower;
    }

    /// <summary>
    /// Everything a run shows and sounds, built for one sim and wired through one <see cref="EventDispatch"/> (plan.md, The
    /// Client > Presenters): the world's beat rings, zones and event rings (<see cref="WorldPresenter"/>, Boot's, shared with
    /// the title), the structures, the horde (<see cref="CrowdPresenter"/> over its <see cref="ICrowdView"/>: the trailer's crowd,
    /// or stand-ins without baked monsters), the bosses in that crowd and
    /// their red telegraphs with the elites' yellow ones, the spores, the hero (<see cref="HeroPresenter"/>, in white), the
    /// weapon effects (<see cref="EffectCues"/>), the run's UI and the sounds. Presenters a checkout can't build (no vendor
    /// files baked) are left out and logged; a presenter that throws is contained by the dispatch.
    ///
    /// Each frame: <see cref="Drain"/> the settled events, then <see cref="Present"/> at the frame's song time. Frames
    /// present in this order: the world (it moves the camera), the structures, the crowd and the boss written then drawn,
    /// the telegraphs, the spores, the hero, the effects, the UI, the sounds.
    /// </summary>
    public sealed class RunPresentation : IDisposable
    {
        readonly Simulation _sim;
        readonly WorldPresenter _world;
        readonly List<IDisposable> _owned = new List<IDisposable>();
        /// <summary>A Boss Rush fight's bodies past the first (the Wraith Council's), each with its own presenter.</summary>
        readonly List<BossPresenter> _bodies = new List<BossPresenter>();
        readonly Func<BossPresenter> _makeBody;
        int _arrivals;

        /// <summary>
        /// The boss events: each of a fight's arrivals goes to its body's presenter (the first to <see cref="Boss"/>), the
        /// fight's call to the first alone (it warms the look), everything else to every presenter (each keeps to its own).
        /// </summary>
        void TakeBoss(in SimEvent e)
        {
            if (e.Kind == SimEventKind.BossCall || e.Kind == SimEventKind.FightCall) _arrivals = 0;
            if (e.Kind == SimEventKind.BossArrive)
            {
                int k = _arrivals++;
                if (k == 0) Boss.Take(e);
                else
                {
                    while (_bodies.Count < k) _bodies.Add(_makeBody());
                    _bodies[k - 1].Take(e);
                }
                return;
            }
            Boss.Take(e);
            if (e.Kind == SimEventKind.FightCall) return;
            foreach (var b in _bodies) b.Take(e);
        }
        readonly GameObject _voices;

        public readonly EventDispatch Dispatch = new EventDispatch();
        public readonly RunUi Ui;
        public readonly CrowdPresenter Crowd;
        public readonly SporePresenter Spores;
        /// <summary>The Hive Queen's drones, honeycomb and bursts (ps-8z4.8).</summary>
        public readonly HiveSwarmPresenter Swarm;
        public readonly PickupPresenter Pickups;
        public readonly TelegraphPresenter Telegraphs;
        public readonly BossPresenter Boss;
        public readonly HeroPresenter Hero;
        public readonly StructurePresenter Structures;
        /// <summary>The Boss Rush arena and its framing, with the arenas (<see cref="SimConfig.Arenas"/>); null without.</summary>
        public readonly ArenaPresenter Arena;
        public readonly Effects Effects;
        /// <summary>A Boss Rush fight's sparks in its boss's hue (with the baked effects; null without).</summary>
        public readonly FightFlavour Flavour;
        public readonly RangedPresenter Ranged;
        public readonly SoundPresenter Sounds;

        /// <summary>Mobs and bosses killed (the end card's count).</summary>
        public int Kills { get; private set; }

        public RunPresentation(Simulation sim, SimConfig config, GameContent content, string contentDir, Palette palette, Camera camera,
            WorldPresenter world, RunUi ui, SoundClock sound, GameObject host, MonsterLook look = MonsterLook.Default)
        {
            _sim = sim;
            _world = world;
            Ui = ui;
            int layer = camera.gameObject.layer;
            var trailer = TrailerCrowd.Create(look, content.Enemies, palette);
            if (trailer == null) Debug.LogWarning("RunPresentation: no baked monsters (champions monster bake all, then build): the horde draws as stand-ins");
            Crowd = new CrowdPresenter((ICrowdView)trailer ?? new StandInCrowd(content.Enemies, palette, layer), content.Enemies);
            _owned.Add(Crowd);
            Pickups = new PickupPresenter(layer);
            _owned.Add(Pickups);
            Spores = new SporePresenter(palette, layer);
            _owned.Add(Spores);
            Swarm = new HiveSwarmPresenter(palette, content.Enemies, layer);
            _owned.Add(Swarm);
            Telegraphs = new TelegraphPresenter(palette, content.Enemies, layer);
            _owned.Add(Telegraphs);
            Boss = new BossPresenter(trailer, content.Enemies, palette, world.World, layer);
            _owned.Add(Boss);
            _makeBody = () =>
            {
                var b = new BossPresenter(trailer, content.Enemies, palette, null, layer);
                _owned.Add(b);
                return b;
            };
            Hero = new HeroPresenter(palette, content.Hero.SpeedMps, layer, look);
            _owned.Add(Hero);
            if (config.Structures != null)
            {
                Structures = StructurePresenter.Create(config.Structures, palette, world.World, layer, look);
                _owned.Add(Structures);
            }
            if (config.Arenas != null)
            {
                Arena = new ArenaPresenter(config.Arenas, palette, world.World, world.World.Camera, layer, content.BossRush);
                _owned.Add(Arena);
            }
            Ranged = new RangedPresenter(palette);
            _owned.Add(Ranged);
            var effects = EffectLibrary.Load();
            if (effects != null)
            {
                Effects = new Effects(effects, EffectRecipes.Load(contentDir), palette);
                _owned.Add(Effects);
                Flavour = new FightFlavour(effects, palette, content.Enemies);
                _owned.Add(Flavour);
            }
            else Debug.LogWarning("RunPresentation: no baked weapon effects (champions setup): the hero's effects are unseen");
            var bank = SoundBank.Load();
            if (bank != null && sound != null)
            {
                // The run's voices on an object of their own, gone with the run (Again makes the next run's).
                _voices = new GameObject("Run sounds");
                _voices.transform.SetParent(host.transform, false);
                var scheduler = new SoundScheduler(bank, _voices, sound.Clock, sound.DspNow, sound.Audible);
                Sounds = new SoundPresenter(scheduler, sim, config.Dashes.IndexOf("leapSlam"), bank.Limit, sound.Follower);
            }
            else if (bank == null) Debug.LogWarning("RunPresentation: no baked sounds (champions setup): the run is silent but for the music");
            var cues = Effects != null ? new EffectCues(Effects, sim, config.Dashes, Backhand) : null;
            Wire(cues);
        }

        void Wire(EffectCues cues)
        {
            var d = Dispatch;
            d.Add("ranged weapons", (in SimEvent e) =>
            {
                if (e.Kind == SimEventKind.RangedImpact)
                    Effects?.Fire(EffectCue.LeapRing, new EffectShot { T = e.T, At = new Vector2((float)e.D0, (float)e.D1),
                        Radius = (float)e.D2, Duration = 0.15f });
            }, null, (in PresentFrame f) => Ranged.Present(f.Sim.Ranged, f.Camera));
            // Before the world: the arena's framing places the camera the world then moves.
            if (Arena != null) d.Add("arena", (in SimEvent e) => Arena.Take(e), null, (in PresentFrame f) => Arena.Present(f.Song, f.Sim, f.Hero, f.Camera));
            d.Add("world", (in SimEvent e) => _world.Take(e), _world.Flush, (in PresentFrame f) => _world.Present(f.Song, f.Hero, f.Dt));
            if (Structures != null)
                d.Add("structures", (in SimEvent e) => Structures.Take(e), Structures.Flush, (in PresentFrame f) => Structures.Present(f.Song, f.Camera));
            d.Add("crowd", (in SimEvent e) => Crowd.Take(e), null, (in PresentFrame f) => Crowd.Present(f.Song, f.Sim, f.Hero, f.Dt));
            d.Add("boss", (in SimEvent e) => TakeBoss(e), null, (in PresentFrame f) =>
            {
                Boss.Present(f.Song, f.Sim);
                foreach (var b in _bodies) b.Present(f.Song, f.Sim);
            });
            d.Add("crowd draw", null, null, (in PresentFrame f) => Crowd.Draw(f.Song, f.Camera));
            d.Add("telegraphs", (in SimEvent e) => Telegraphs.Take(e), null, (in PresentFrame f) => Telegraphs.Present(f.Song, f.Camera, f.Sim));
            if (Flavour != null) d.Add("fight flavour", (in SimEvent e) => Flavour.Take(e, _sim), null, (in PresentFrame f) => Flavour.Present(f.Song, f.Camera));
            d.Add("spores", (in SimEvent e) => Spores.Take(e), null, (in PresentFrame f) => Spores.Present(f.Song, f.Camera));
            d.Add("hive swarm", (in SimEvent e) => Swarm.Take(e), null, (in PresentFrame f) => Swarm.Present(f.Song, f.Sim, f.Camera));
            d.Add("pickups", null, null, (in PresentFrame f) => Pickups.Present(f.Sim, f.Song, f.Camera));
            d.Add("hero", TakeHero, null, (in PresentFrame f) => Hero.Present(f.Song, f.Hero, f.Dt, f.Camera));
            if (cues != null)
                d.Add("effects", (in SimEvent e) => cues.Take(e), null, (in PresentFrame f) =>
                {
                    double combat = f.Sim.CombatTime(f.Song);
                    float area = (float)f.Sim.State.Build.AreaAt(combat);
                    Effects.Rhythm(area, (float)Math.Max(0, f.Sim.State.Build.AuraUntil - combat), f.Hero);
                    if (f.Sim.Constructs != null)
                        Effects.ConstructOrbit(f.Sim.State.Build.Level(WeaponKind.OrbitBlades),
                            f.Sim.State.Build.Level(WeaponKind.OrbitBlades) > 0 ? (float)f.Sim.State.Build.Stats(WeaponKind.OrbitBlades).AreaFactor * area : 1);
                    Effects.Present(f.Song, f.Hero, f.Dt, f.Camera);
                });
            if (Ui != null) d.Add("ui", (in SimEvent e) => Ui.Take(e), null, (in PresentFrame f) => Ui.Present(f.Song, f.Sim, f.Camera));
            if (Sounds != null) d.Add("sound", (in SimEvent e) => Sounds.Take(e), null, (in PresentFrame f) => Sounds.Present(f.Frontier));
            d.Add("count", Count);
        }

        /// <summary>The hero's commands (ps-6y2.20's mapping).</summary>
        void TakeHero(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Hold:
                    if (_sim.Melee != null) Hero.Motion.Hold(e.T, e.I2 != 0);
                    break;
                case SimEventKind.HeroMove:
                    Hero.Walk(e.D0, new Vector2((float)e.D3, (float)e.D4));
                    break;
                case SimEventKind.MeleeAction when (WeaponKind)e.I0 == WeaponKind.Sword && (e.I2 & 1) == 0:
                    Hero.Motion.AimSwing(e.T, new Vector2((float)e.D2, (float)e.D3));
                    break;
                case SimEventKind.Swing:
                    Hero.Swing(e.I0 == 1 ? SwingKind.Spin : SwingKind.Slash, e.D0, new Vector2((float)e.D1, (float)e.D2));
                    break;
                case SimEventKind.HeroDash:
                    var direction = new Vector2((float)e.D4, (float)e.D5);
                    if (e.InPlaceCleave)
                    {
                        double reach = MeleeWeapons.CleaveRadius(_sim.State.Build.Stats(WeaponKind.Sword))
                            * Math.Sqrt(_sim.State.Build.AreaAt(_sim.CombatTime(e.D1)));
                        Hero.Motion.WalkingCleave(e.D0, e.D1, direction, (float)reach, (e.I0 & 1) != 0);
                    }
                    else
                    {
                        Hero.Dash((DashPose)e.DashType, e.D0, e.D1, direction, evolved: e.DashRank == Upgrade.Evolution);
                        if (_sim.Melee != null) Hero.Motion.LandingSpin(e.D0, e.D1, direction);
                    }
                    break;
                case SimEventKind.HeroHit when e.D0 > 0:
                    Hero.Hurt(e.T);
                    break;
                case SimEventKind.HeroDeath:
                    Hero.Death(e.T, new Vector2((float)e.D2, (float)e.D3));
                    break;
            }
        }

        /// <summary>Is the swing striking at <paramref name="t"/> the backhand, LightCombo01B (its sweep mirrored)?</summary>
        bool Backhand(double t)
        {
            var motion = Hero.Motion;
            if (motion == null) return false;
            for (int i = Math.Max(0, motion.SwingCount - 4); i < motion.SwingCount; i++)
            {
                var s = motion.SwingAt(i);
                if (Math.Abs(s.Impact - t) < 1e-6) return s.Clip == motion.Clips.SlashB.Index;
            }
            return false;
        }

        void Count(in SimEvent e)
        {
            if (e.Kind == SimEventKind.Kill && (BlowSource)e.I1 != BlowSource.Boss) Kills++;
        }

        /// <summary>The presenters whose state outlives frames: a scene running ahead hands them alone the events it skips drawing.</summary>
        public static readonly string[] Keepers = { "arena", "world", "structures", "crowd", "boss", "telegraphs", "spores", "count" };

        /// <summary>Hands out the sim's settled events (with <paramref name="only"/>, to those presenters alone).</summary>
        public void Drain(ICollection<string> only = null) => Dispatch.Drain(_sim, only);

        /// <summary>The frame at song time <paramref name="song"/>, the sim at <paramref name="frontier"/>, <paramref name="dt"/> real s after the last.</summary>
        public void Present(double song, double frontier, float dt, Camera camera)
        {
            _sim.Snapshot().HeroAt(song, out double hx, out double hy);
            Dispatch.Present(new PresentFrame
            {
                Song = song, Frontier = frontier, Dt = dt, Hero = new Vector2((float)hx, (float)hy), Sim = _sim, Camera = camera,
            });
        }

        /// <summary>Queues the frame's hand-drawn draws again (the stills' second drawing of a frame); the stand-ins are scene objects.</summary>
        public void Queue(Camera camera)
        {
            Structures?.Queue(camera);
            Arena?.Queue(camera);
            Crowd.Queue(camera);
            Pickups.Present(_sim, _sim.Now, camera);
            Hero.Queue(camera);
            Telegraphs.Queue(camera);
            Effects?.Queue(camera);
            Flavour?.Queue(camera);
            Ranged.Queue(camera);
        }

        public void Dispose()
        {
            foreach (var o in _owned)
            {
                try { o.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _owned.Clear();
            if (_voices == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_voices);
            else UnityEngine.Object.DestroyImmediate(_voices);
        }
    }
}
