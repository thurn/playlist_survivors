using System;
using System.Linq;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Motion evidence through the live sim and presentation: ordinary swings, rapid dash exits and expiry.</summary>
    sealed class SwordFeelScene : IStillScene
    {
        readonly GameContent content;
        readonly Palette palette;
        readonly string contentDir;
        public SwordFeelScene(GameContent content, Palette palette, string contentDir)
        { this.content = content; this.palette = palette; this.contentDir = contentDir; }
        public double[] Times => Enumerable.Range(1, 240).Select(i => i / 30.0).ToArray();
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view) =>
            root.gameObject.AddComponent<SwordFeelDriver>().Begin(content, palette, contentDir, camera, view);
    }

    [DefaultExecutionOrder(-100)]
    sealed class SwordFeelDriver : MonoBehaviour
    {
        Simulation sim;
        RunPresentation presentation;
        WorldPresenter world;
        Camera camera;
        Stills.View view;
        double start;
        int dash;
        readonly double[] presses = { 2, 2.625, 3.25 };
        public void Begin(GameContent content, Palette palette, string contentDir, Camera camera, Stills.View view)
        {
            this.camera = camera; this.view = view; start = Time.timeAsDouble;
            var targets = new Dummy[72];
            for (int i = 0; i < targets.Length; i++)
            {
                int group = i / 18, member = i % 18;
                double a = member * 2.39996, r = 1.2 + member % 3 * .55;
                targets[i] = new Dummy(Math.Cos(a) * r, -2 - group * 4 + Math.Sin(a) * r, member == 0 ? 8 : .65, .35);
            }
            var config = new RunInputs { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = targets }.Build(content);
            sim = new Simulation(config);
            // Feed exactly the synthetic entries known by each frame, using the same lead as the live client.
            foreach (var entry in new ScriptedSong().Beats(0, 10, 120).Entries())
                sim.AddKnown(entry, Math.Max(0, entry.Stamp));
            world = new WorldPresenter(camera, palette);
            presentation = new RunPresentation(sim, config, content, contentDir, palette, camera, world,
                null, null, gameObject, LaunchOptions.Current.Look);
            // Dummies use type zero in the sim. Give this stationary hit-test formation visible
            // skeleton/slime/demon bodies without changing its damage or pretending it is a live horde.
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                presentation.Crowd.Take(SimEvent.EnemySpawn(0, i, 1 + i % 3, false,
                    target.X, target.Y, target.Radius, target.Health));
            }
            presentation.Hero.LogSwings = true;
            view.Requeue = () => presentation.Queue(camera);
        }
        void Update()
        {
            double t = Time.timeAsDouble - start;
            while (dash < presses.Length && presses[dash] <= t)
            {
                sim.StepTo(presses[dash]);
                sim.Press(Key.Space, presses[dash]); sim.Release(Key.Space, presses[dash]);
                dash++;
            }
            sim.StepTo(t);
            presentation.Drain(); presentation.Present(t, sim.Now, Time.deltaTime, camera);
            sim.State.Hero.PositionAt(t, out double x, out double y);
            view.Hero = new Vector3((float)x, 0, (float)y);
        }
        void OnDestroy() { presentation?.Dispose(); world?.Dispose(); }
    }
}
