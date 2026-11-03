using System;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Pickup silhouettes and attraction amid the real palette crowd, beside yellow shots.</summary>
    sealed class PickupScene : IStillScene
    {
        readonly GameContent content;
        readonly Palette palette;
        readonly MonsterLook look;
        public PickupScene(GameContent content, Palette palette, MonsterLook look)
        { this.content = content; this.palette = palette; this.look = look; }
        public double[] Times => new[] { .25, 1.5 };
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            new PaletteScene(content, palette, look).Begin(root, camera, view);
            root.gameObject.AddComponent<PickupSceneDriver>().Begin(content, palette, camera, view);
        }
    }

    [DefaultExecutionOrder(-100)]
    sealed class PickupSceneDriver : MonoBehaviour
    {
        readonly Pickup[] drops = new Pickup[160];
        PickupPresenter pickups;
        SporePresenter spores;
        Camera camera;
        double start;
        public void Begin(GameContent content, Palette palette, Camera camera, Stills.View view)
        {
            this.camera = camera;
            start = Time.timeAsDouble;
            for (int i = 0; i < drops.Length; i++)
                drops[i] = new Pickup { Id = i, Kind = i % 40 == 0 ? PickupKind.Healing : PickupKind.Experience,
                    X = i % 16 - 8 + .3, Y = i / 16 - 5 + .3, Value = 1 };
            pickups = new PickupPresenter(camera.gameObject.layer);
            spores = new SporePresenter(palette, camera.gameObject.layer);
            for (int i = 0; i < 12; i++) spores.Take(SimEvent.Spore(0, i, -1, i - 6, -2, .4, 1, 20));
            view.Requeue = () => { pickups.Draw(drops, drops.Length, Time.timeAsDouble - start, camera); spores.Present(Time.timeAsDouble - start, camera); };
        }
        void Update()
        {
            double now = Time.timeAsDouble - start;
            // Authored attraction preview: short local paths, never shot-like straight velocity across the scene.
            for (int i = 0; i < 16; i++)
            {
                double fraction = Math.Min(1, Math.Max(0, 1 - (now - 1.35) / .25));
                double angle = i * Math.PI / 8;
                drops[i].X = Math.Cos(angle) * 3 * fraction;
                drops[i].Y = Math.Sin(angle) * 3 * fraction;
                drops[i].Value = fraction > .07 ? 1 : 0;
            }
            pickups.Draw(drops, drops.Length, now, camera);
            spores.Present(now, camera);
        }
        void OnDestroy() { pickups?.Dispose(); spores?.Dispose(); }
    }
}
