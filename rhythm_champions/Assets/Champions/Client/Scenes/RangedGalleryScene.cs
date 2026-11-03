using System;
using System.Collections.Generic;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
namespace Champions.Client.Scenes
{
    /// <summary>All four forms, normal/empowered, at game scale in a retained crowd; three motion phases each.</summary>
    sealed class RangedGalleryScene : IStillScene, INamedStills
    {
        public const string Name = "ranged-gallery";
        readonly GameContent content; readonly Palette palette; readonly string contentDir;
        public RangedGalleryScene(GameContent content, Palette palette, string contentDir)
        { this.content = content; this.palette = palette; this.contentDir = contentDir; }
        static int Phases => Environment.GetEnvironmentVariable("CHAMPIONS_RANGED_MOTION") == "1" ? 20 : 3;
        public double[] Times
        {
            get
            {
                var times = new List<double>();
                for (int i = 0; i < 16; i++)
                    for (int phase = 0; phase < Phases; phase++)
                        times.Add(i * 2.5 + (Phases == 20 ? (i < 8 ? .74 + phase * .035 : 1.01 + phase * .05)
                            : i < 8 ? .80 + phase * .10 : i < 12 ? 1.06 + phase * .10 : 1.12 + phase * .28));
                return times.ToArray();
            }
        }
        public string Label(int index)
        {
            int i = index / Phases;
            return $"{(i < 8 ? "spear" : "bow")}-form{i % 8 / 2 + 1}-{(i % 2 == 0 ? "normal" : "empowered")}-phase{index % Phases}";
        }
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view) =>
            root.gameObject.AddComponent<RangedGalleryDriver>().Begin(camera, content, palette, contentDir, view);
    }
    [DefaultExecutionOrder(-100)]
    sealed class RangedGalleryDriver : MonoBehaviour
    {
        Camera camera; GameContent content; Palette palette; string contentDir; Stills.View view;
        Simulation sim; RunPresentation presentation; WorldPresenter world;
        double start; int entry = -1;
        public void Begin(Camera camera, GameContent content, Palette palette, string contentDir, Stills.View view)
        {
            this.camera = camera; this.content = content; this.palette = palette; this.contentDir = contentDir; this.view = view;
            start = Time.timeAsDouble;
            view.Requeue = () => presentation?.Queue(camera);
            view.Ids[IdPass.Effect] = ("Spear and Bow spectral forms", "effect", HeroMarker.WidthM);
        }
        void Update()
        {
            double t = Time.timeAsDouble - start;
            int next = Math.Min(15, (int)(t / 2.5));
            if (next != entry)
            {
                presentation?.Dispose(); world?.Dispose(); entry = next;
                // Keep the standing crowd outside the starter Sword's reach to isolate this family.
                var dummies = new List<Dummy> { new Dummy(0, 9, 10000, .7) };
                for (int y = -2; y <= 3; y++)
                    for (int x = -4; x <= 4; x++)
                        if (Math.Abs(x) >= 3) dummies.Add(new Dummy(x * 1.4, y * 1.6, 10000, .45));
                // Keep a far target in view; the full game camera, hero and retained crowd are unchanged.
                var config = new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = dummies.ToArray(), Enemies = content.Enemies,
                    Hero = content.Hero, Combat = content.Combat };
                sim = new Simulation(config);
                sim.State.Build.Set(entry < 8 ? WeaponKind.Spear : WeaponKind.Bow, new[] { 1, 3, 5, 8 }[entry % 8 / 2]);
                if (entry % 2 == 1) sim.State.Build.Empower();
                world = new WorldPresenter(camera, palette);
                presentation = new RunPresentation(sim, config, content, contentDir, palette, camera, world, null, null, gameObject);
                // Dummies intentionally stand still. Dress their exact collision bodies in the existing crowd,
                // whose normal event mapping ignores type-zero test dummies.
                presentation.Drain();
                for (int id = 0; id < sim.Enemies.Count; id++)
                {
                    var e = sim.Enemies[id];
                    presentation.Dispatch.Dispatch(SimEvent.EnemySpawn(0, id, 1, false, e.X, e.Y, e.Radius, e.Health));
                }
                view.Ids[IdPass.Monster(0)] = (content.Enemies.Cast[0].Id, "crowd", (float)content.Enemies.Cast[0].SizeM);
                Debug.Log($"RangedGallery: entry {entry}, {(entry < 8 ? "spear" : "bow")}, form {entry % 8 / 2 + 1}, empowered={entry % 2 == 1}");
            }
            double local = t - entry * 2.5;
            sim.StepTo(local); presentation.Drain(); presentation.Present(local, local, Time.deltaTime, camera);
            view.Hero = Vector3.zero;
        }
        void OnDestroy() { presentation?.Dispose(); world?.Dispose(); }
    }
}
