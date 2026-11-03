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
    /// The HUD (design.md, UI > HUD and Decision 26): a hairline experience bar along the bottom edge and, in a duel, a
    /// slim health bar at the top in the boss's colour. Nothing else, and no text.
    /// </summary>
    public sealed class Hud : VisualElement
    {
        readonly VisualElement _xp, _boss, _health;

        public Hud(Palette palette)
        {
            AddToClassList("hud");
            pickingMode = PickingMode.Ignore; // shows, never takes the pointer
            Bar(this, "hud__xp", out _xp);
            _xp.style.backgroundColor = Alpha(palette.HeroBody, 0.7f);
            _boss = new VisualElement { pickingMode = PickingMode.Ignore };
            _boss.AddToClassList("hud__boss");
            Add(_boss);
            Bar(_boss, "hud__health", out _health);
            _boss.style.display = DisplayStyle.None;
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
