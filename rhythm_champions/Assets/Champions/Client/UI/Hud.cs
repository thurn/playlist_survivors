using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>The boss bar's values for a frame (design.md, Bosses > The boss bar).</summary>
    public struct BossBar
    {
        /// <summary>A duel is on: the bar shows.</summary>
        public bool Up;
        /// <summary>The boss's colour (its cast type's in the palette).</summary>
        public Color Colour;
        /// <summary>Health, 0 to 1.</summary>
        public float Health;

        /// <summary>The bar from a snapshot's duel fields; down outside a duel.</summary>
        public static BossBar Of(in SimSnapshot s, Palette palette, GameContent content)
        {
            if (s.Boss < 0 || s.BossCast < 0 || s.BossMaxHealth <= 0) return default;
            return new BossBar
            {
                Up = true,
                Colour = palette.ForType(content.Enemies.Cast[s.BossCast].Id),
                Health = Mathf.Clamp01((float)(s.BossHealth / s.BossMaxHealth)),
            };
        }
    }

    /// <summary>
    /// The run HUD: health vessel, phase clock, experience and boss health. Input passes through it.
    /// </summary>
    public sealed class Hud : VisualElement, System.IDisposable
    {
        readonly VisualElement _xp, _boss, _health;
        readonly Image _orb;
        readonly Label _healthText, _timer, _banner;
        readonly Material _liquid;
        readonly RenderTexture _texture;

        public Hud(Palette palette)
        {
            AddToClassList("hud");
            pickingMode = PickingMode.Ignore; // shows, never takes the pointer
            Bar(this, "hud__xp", out _xp);
            _xp.style.backgroundColor = new Color(.12f, .9f, .32f);
            _liquid = new Material(Resources.Load<Shader>("UI/HealthOrb"));
            _texture = new RenderTexture(192, 192, 0, RenderTextureFormat.ARGB32) { name = "Health orb" };
            _texture.Create();
            var vessel = new VisualElement { pickingMode = PickingMode.Ignore };
            vessel.AddToClassList("hud__vessel");
            _orb = new Image { image = _texture, pickingMode = PickingMode.Ignore };
            _orb.AddToClassList("hud__orb");
            vessel.Add(_orb);
            _healthText = new Label { pickingMode = PickingMode.Ignore };
            _healthText.AddToClassList("hud__number");
            vessel.Add(_healthText);
            Add(vessel);
            _timer = new Label { pickingMode = PickingMode.Ignore };
            _timer.AddToClassList("hud__timer");
            Add(_timer);
            _banner = new Label { pickingMode = PickingMode.Ignore };
            _banner.AddToClassList("hud__banner");
            _banner.style.display = DisplayStyle.None;
            Add(_banner);
            _boss = new VisualElement { pickingMode = PickingMode.Ignore };
            _boss.AddToClassList("hud__boss");
            Add(_boss);
            Bar(_boss, "hud__health", out _health);
            _boss.style.display = DisplayStyle.None;
        }

        /// <summary>Snapshot clocks freeze through cards/pause; shader motion uses the same active clock.</summary>
        public void Present(in SimSnapshot s, in BossBar boss)
        {
            Present(s.ExperienceNeeded > 0 ? (float)(s.Experience / s.ExperienceNeeded) : 0, boss);
            float health = s.HeroMaxHealth > 0 ? Mathf.Clamp01((float)(s.HeroHealth / s.HeroMaxHealth)) : 0;
            _liquid.SetFloat("_Fill", health);
            _liquid.SetFloat("_Motion", (float)s.RunClock);
            var previous = RenderTexture.active;
            Graphics.Blit(null, _texture, _liquid);
            RenderTexture.active = previous;
            _healthText.text = $"{System.Math.Ceiling(System.Math.Max(0, s.HeroHealth)):0} / {s.HeroMaxHealth:0}";
            _timer.text = s.Rush ? RushClockText(s.RushAct, s.RushBoss, s.BossElapsed) : ClockText(s.HordeClock, s.BossElapsed, boss.Up);
        }

        /// <summary>The Boss Rush's clock (boss-rush.md, Differences): ACT 1 · BOSS 2  01:07, no horde clock.</summary>
        public static string RushClockText(int act, int boss, double fight)
        {
            int seconds = (int)System.Math.Floor(System.Math.Max(0, fight) + 1e-7);
            return $"{RushSummary.Place(act, boss)}  {seconds / 60:00}:{seconds % 60:00}";
        }

        /// <summary>The act card's text over the arena (null hides it).</summary>
        public void Banner(string text)
        {
            _banner.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (text != null) _banner.text = text;
        }

        public static string ClockText(double horde, double fight, bool boss)
        {
            int seconds = (int)System.Math.Floor(System.Math.Max(0, boss ? fight : horde) + 1e-7);
            return $"{(boss ? "BOSS" : "HORDE")}  {seconds / 60:00}:{seconds % 60:00}";
        }

        public void Dispose()
        {
            _texture.Release();
            foreach (var o in new UnityEngine.Object[] { _texture, _liquid })
                if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static VisualElement Bar(VisualElement parent, string className, out VisualElement fill)
        {
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList(className);
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("hud__fill");
            track.Add(fill);
            parent.Add(track);
            return track;
        }

        /// <summary>The frame: the experience toward the next level (0 to 1) and the boss bar.</summary>
        public void Present(float experience, in BossBar boss)
        {
            _xp.style.width = Length.Percent(100 * Mathf.Clamp01(experience));
            _boss.style.display = boss.Up ? DisplayStyle.Flex : DisplayStyle.None;
            if (!boss.Up) return;
            _health.parent.style.backgroundColor = Alpha(boss.Colour, 0.18f);
            _health.style.backgroundColor = boss.Colour;
            _health.style.width = Length.Percent(100 * boss.Health);
        }
    }
}
