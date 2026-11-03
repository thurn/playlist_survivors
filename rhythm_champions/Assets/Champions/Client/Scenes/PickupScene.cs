using System;
using System.Linq;
using Champions.Client.UI;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>Pickup silhouettes and attraction amid the real palette crowd, beside orange/red shots.</summary>
    sealed class PickupScene : IStillScene
    {
        readonly GameContent content;
        readonly Palette palette;
        readonly MonsterLook look;
        readonly bool hud;
        public PickupScene(GameContent content, Palette palette, MonsterLook look, bool hud = false)
        { this.content = content; this.palette = palette; this.look = look; this.hud = hud; }
        public double[] Times => hud ? Enumerable.Range(1, 120).Select(i => i / 30.0).ToArray() : new[] { .25, 1.5 };
        public string Moment => null;
        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            new PaletteScene(content, palette, look).Begin(root, camera, view);
            root.gameObject.AddComponent<PickupSceneDriver>().Begin(content, palette, camera, view, hud);
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
        UiPanel panel;
        Color bossColour;
        Hud hud;
        public void Begin(GameContent content, Palette palette, Camera camera, Stills.View view, bool showHud)
        {
            this.camera = camera; bossColour = palette.BossBody;
            if (showHud)
            {
                panel = new UiPanel(transform, "HUD readability", RunUi.Order);
                hud = new Hud(palette); panel.Root.Add(hud);
            }
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
            if (hud != null)
            {
                // Explicit presentation fixture: full, low, empty, then boss phase, over the real crowd/post.
                var state = new SimSnapshot { HeroMaxHealth = 100, HeroHealth = now < 1 ? 100 : now < 2 ? 8 : now < 3 ? 0 : 60,
                    HordeClock = 299, RunClock = now, BossElapsed = Math.Max(0, now - 3), Experience = 40, ExperienceNeeded = 100 };
                hud.Present(state, new BossBar { Up = now >= 3, Health = .65f, Colour = bossColour });
            }
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
        void OnDestroy() { pickups?.Dispose(); spores?.Dispose(); hud?.Dispose(); panel?.Dispose(); }
    }
}
