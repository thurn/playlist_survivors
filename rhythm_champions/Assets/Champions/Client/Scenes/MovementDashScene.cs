using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Live sim events: empty-ground reward, then optional destination blast, then settled dark space.</summary>
    sealed class MovementDashScene : IStillScene, INamedStills
    {
        public const string Name = "movement-dash";
        readonly Palette palette;
        readonly string contentDir;
        public MovementDashScene(Palette palette, string contentDir) { this.palette = palette; this.contentDir = contentDir; }
        public double[] Times => new[] { 1.05, 2.18, 2.25, 2.6 };
        public string Label(int index) => new[] { "empty-ground", "landing", "settling", "rest" }[index];
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view) =>
            root.gameObject.AddComponent<MovementDashDriver>().Begin(camera, palette, contentDir, view);
    }

    [DefaultExecutionOrder(-100)]
    sealed class MovementDashDriver : MonoBehaviour
    {
        Simulation sim;
        WorldPresenter world;
        Effects effects;
        EffectCues cues;
        HeroMarker marker;
        Camera camera;
        Stills.View view;
        double start;
        int cursor, dash;
        public void Begin(Camera camera, Palette palette, string contentDir, Stills.View view)
        {
            this.camera = camera;
            this.view = view;
            start = Time.timeAsDouble;
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(null);
            sim = new Simulation(config);
            world = new WorldPresenter(camera, palette);
            marker = new HeroMarker(camera.gameObject.layer);
            var library = EffectLibrary.Load();
            if (library != null)
            {
                effects = new Effects(library, EffectRecipes.Load(contentDir), palette);
                cues = new EffectCues(effects, sim, config.Dashes);
                view.Requeue = () => effects.Queue(camera);
            }
            view.Ids[IdPass.Effect] = ("movement dash and landing blast", "effect", HeroMarker.WidthM);
        }
        void Update()
        {
            double t = Time.timeAsDouble - start;
            sim.StepTo(t);
            if (dash < 2 && t >= dash + 1)
            {
                if (dash == 1) sim.State.Build.Set(SupportKind.LandingBlast, 1);
                sim.Press(Key.Space, dash + 1);
                sim.Release(Key.Space, dash + 1);
                dash++;
            }
            foreach (var e in sim.EventsSince(cursor))
            {
                world.Take(e);
                cues?.Take(e);
                if (e.Kind == SimEventKind.HeroDash || e.Kind == SimEventKind.LandingBlast)
                    Debug.Log($"MovementDash: {e.Kind} at {e.T:F3}; sword empowered={sim.State.Build.Weapon(WeaponKind.Sword).Empowered}");
            }
            cursor = sim.EventCount;
            world.Flush();
            sim.State.Hero.PositionAt(t, out double x, out double y);
            var feet = new Vector2((float)x, (float)y);
            marker.Place(feet);
            world.Present(t, feet, Time.deltaTime);
            effects?.Present(t, feet, Time.deltaTime, camera);
            view.Hero = new Vector3(feet.x, 0, feet.y);
        }
        void OnDestroy()
        {
            marker?.Dispose();
            effects?.Dispose();
            world?.Dispose();
        }
    }
}
