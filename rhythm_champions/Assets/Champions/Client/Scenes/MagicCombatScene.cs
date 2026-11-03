using System;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Render.Telegraphs;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Actual scheduled Lightning/Ember events, four forms, normal then dash-empowered, in a retained crowd.</summary>
    sealed class MagicCombatScene : IStillScene, INamedStills
    {
        readonly Palette palette;
        readonly string contentDir;
        public MagicCombatScene(Palette palette, string contentDir) { this.palette = palette; this.contentDir = contentDir; }
        public double[] Times => new[] { 1.55, 3.05, 4.55, 6.05, 7.55, 9.05, 10.55, 12.05 };
        public string Label(int index) => "form" + (index / 2 + 1) + (index % 2 == 0 ? "-normal" : "-empowered");
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view) =>
            root.gameObject.AddComponent<MagicCombatDriver>().Begin(camera, palette, contentDir, view);
    }

    [DefaultExecutionOrder(-100)]
    sealed class MagicCombatDriver : MonoBehaviour
    {
        Simulation sim;
        Effects effects;
        EffectCues cues;
        WorldPresenter world;
        ICrowdView crowd;
        TelegraphRenderer warning;
        HeroMarker hero;
        Camera camera;
        double start;
        int cursor, nextForm, nextEmpower;
        readonly int[] levels = { 1, 3, 5, 8 };
        public void Begin(Camera camera, Palette palette, string contentDir, Stills.View view)
        {
            this.camera = camera; start = Time.timeAsDouble;
            var dummies = new Dummy[22];
            for (int k = 0; k < dummies.Length; k++)
            {
                double a = k * 2.39996, r = 1.8 + k % 4 * 1.1;
                dummies[k] = new Dummy(Math.Cos(a) * r, Math.Sin(a) * r, 1000, .35);
            }
            var config = new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = dummies };
            sim = new Simulation(config);
            sim.State.Build.Set(WeaponKind.Lightning, 1); sim.State.Build.Set(WeaponKind.EmberField, 1);
            sim.State.Build.Set(SupportKind.Tempo, 2); sim.State.Build.Set(SupportKind.Persistence, 2);
            sim.State.Build.Set(SupportKind.Pierce, 1); sim.State.Build.Set(SupportKind.Echo, 1);
            nextForm = 1;
            world = new WorldPresenter(camera, palette);
            hero = new HeroMarker(camera.gameObject.layer);
            var enemies = GameContent.Load(contentDir).Enemies;
            crowd = (ICrowdView)TrailerCrowd.Create(LaunchOptions.Current.Look, enemies, palette)
                ?? new StandInCrowd(enemies, palette, camera.gameObject.layer);
            warning = new TelegraphRenderer(camera.gameObject.layer);
            warning.Add(1, HitShape.Path(-5, -1, 5, -1, .28), 0, 1000, palette.BossTelegraph.linear * 2.5f);
            var library = EffectLibrary.Load();
            if (library != null)
            {
                effects = new Effects(library, EffectRecipes.Load(contentDir), palette);
                cues = new EffectCues(effects, sim, config.Dashes);
            }
            view.Requeue = () => { effects?.Queue(camera); crowd.Queue(camera); warning.Queue(camera); };
            view.Ids[IdPass.Effect] = ("Lightning and Ember Field", "effect", 1.8f);
            view.Ids[IdPass.Monster(0)] = ("skeleton", "monster", 1.8f);
            view.Hero = Vector3.zero;
        }
        void Update()
        {
            double t = Time.timeAsDouble - start;
            while (nextForm < 4 && t >= nextForm * 3 + .1)
            {
                sim.State.Build.Set(WeaponKind.Lightning, levels[nextForm]);
                sim.State.Build.Set(WeaponKind.EmberField, levels[nextForm]); nextForm++;
            }
            if (nextEmpower < 4 && t >= nextEmpower * 3 + 2.5)
            {
                // Same build reward as the on-beat dash, without moving the capture camera out of the gallery.
                sim.State.Build.Empower(); nextEmpower++;
            }
            sim.StepTo(t);
            foreach (var e in sim.EventsSince(cursor)) { world.Take(e); cues?.Take(e); }
            cursor = sim.EventCount;
            world.Flush(); world.Present(t, Vector2.zero, Time.deltaTime);
            hero.Place(Vector2.zero);
            effects?.Present(t, Vector2.zero, Time.deltaTime, camera);
            warning.Present(t, camera);
            crowd.Begin(t);
            for (int k = 0; k < sim.Enemies.Count; k++)
            {
                var e = sim.Enemies[k];
                if (e.Alive) crowd.Mob(new CrowdMob { Id = k, Serial = k, Cast = 0, Position = new Vector3((float)e.X, 0, (float)e.Y),
                    Action = CrowdAction.Idle, ActionTime = (float)t, StepAge = CrowdMob.Long, KickAge = CrowdMob.Long });
            }
            crowd.Draw(t, camera);
        }
        void OnDestroy() { effects?.Dispose(); world?.Dispose(); crowd?.Dispose(); warning?.Dispose(); hero?.Dispose(); }
    }
}
