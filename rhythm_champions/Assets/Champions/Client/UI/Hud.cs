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
        /// <summary>
        /// A Boss Rush fight's name, shown over the wide top bar (null keeps the main game's thin bar).
        /// </summary>
        public string Name;
        /// <summary>The health shares (0 to 1) below which the boss changes phase, notched on the wide bar; null for none.</summary>
        public double[] Phases;

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

        /// <summary>
        /// The bar for a frame of <paramref name="sim"/>: in a Boss Rush the wide one, with the boss's name and its fight's
        /// phase thresholds.
        /// </summary>
        public static BossBar Of(in SimSnapshot s, Simulation sim, Palette palette, GameContent content)
        {
            var bar = Of(s, palette, content);
            if (!s.Rush || s.RushBoss < 0 || s.RushBoss >= content.BossRush.Count) return bar;
            // A landed fight runs on BossFight, not the duel the snapshot's boss fields read: its bodies' health is the bar.
            var fight = sim.Fight;
            if (fight.InFight && fight.Module != null)
            {
                bar = new BossBar
                {
                    Up = true,
                    Colour = palette.ForType(fight.Module.Id),
                    Health = Mathf.Clamp01((float)fight.HealthShare),
                    Phases = fight.Module.PhasesBelow,
                };
            }
            if (bar.Up) bar.Name = content.BossRush.Bosses[s.RushBoss].Name.ToUpperInvariant();
            return bar;
        }
    }

    /// <summary>
    /// The run HUD: health vessel, phase clock, experience and boss health. Input passes through it.
    /// </summary>
    public sealed class Hud : VisualElement, System.IDisposable
    {
        readonly VisualElement _xp, _boss, _health, _trail;
        readonly VisualElement[] _notches = new VisualElement[4];
        readonly Image _orb;
        readonly Label _healthText, _timer, _banner, _bossName;
        // The wide bar's white trail: the health it eases down from after a hit, and the clock it last moved at.
        float _trailHealth = 1, _trailHold;
        double _trailClock = double.NaN;
        string _trailBoss;
        /// <summary>Seconds the trail holds after a hit, and the share of the bar it drains each second after.</summary>
        public const float TrailHoldS = 0.35f, TrailDrainPerS = 0.5f;
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
            _bossName = new Label { pickingMode = PickingMode.Ignore };
            _bossName.AddToClassList("hud__boss-name");
            _boss.Add(_bossName);
            var track = Bar(_boss, "hud__health", out _health);
            _trail = new VisualElement { pickingMode = PickingMode.Ignore };
            _trail.AddToClassList("hud__trail");
            track.Insert(0, _trail);
            for (int i = 0; i < _notches.Length; i++)
            {
                _notches[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                _notches[i].AddToClassList("hud__notch");
                track.Add(_notches[i]);
            }
            _boss.style.display = DisplayStyle.None;
        }

        /// <summary>Snapshot clocks freeze through cards/pause; shader motion uses the same active clock.</summary>
        public void Present(in SimSnapshot s, in BossBar boss)
        {
            Present(s.ExperienceNeeded > 0 ? (float)(s.Experience / s.ExperienceNeeded) : 0, boss, s.RunClock);
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

        /// <summary>
        /// The frame: the experience toward the next level (0 to 1) and the boss bar. <paramref name="clock"/> is the run
        /// clock, which eases the wide bar's trail (it freezes with cards and pauses); NaN snaps the trail to the health.
        /// </summary>
        public void Present(float experience, in BossBar boss, double clock = double.NaN)
        {
            _xp.style.width = Length.Percent(100 * Mathf.Clamp01(experience));
            _boss.style.display = boss.Up ? DisplayStyle.Flex : DisplayStyle.None;
            if (!boss.Up) { _trailBoss = null; _trailClock = double.NaN; return; }
            bool wide = boss.Name != null;
            _boss.EnableInClassList("hud__boss--rush", wide);
            _bossName.style.display = wide ? DisplayStyle.Flex : DisplayStyle.None;
            _bossName.text = wide ? boss.Name : "";
            var track = _health.parent;
            track.style.backgroundColor = Alpha(boss.Colour, wide ? 0.14f : 0.18f);
            _health.style.backgroundColor = boss.Colour;
            _health.style.width = Length.Percent(100 * boss.Health);
            if (wide)
            {
                _bossName.style.color = Color.Lerp(boss.Colour, Color.white, 0.55f);
                track.style.borderTopColor = track.style.borderBottomColor =
                    track.style.borderLeftColor = track.style.borderRightColor = Color.Lerp(boss.Colour, Color.white, 0.3f);
            }
            Trail(boss, wide, clock);
            int phases = wide && boss.Phases != null ? Mathf.Min(boss.Phases.Length, _notches.Length) : 0;
            for (int i = 0; i < _notches.Length; i++)
            {
                _notches[i].style.display = i < phases ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < phases) _notches[i].style.left = Length.Percent(100 * Mathf.Clamp01((float)boss.Phases[i]));
            }
        }

        /// <summary>The white trail behind the wide bar's fill: it holds where a hit took it from, then drains to the health.</summary>
        void Trail(in BossBar boss, bool wide, double clock)
        {
            _trail.style.display = wide ? DisplayStyle.Flex : DisplayStyle.None;
            if (!wide) return;
            float dt = double.IsNaN(clock) || double.IsNaN(_trailClock) ? 0 : Mathf.Max(0, (float)(clock - _trailClock));
            if (_trailBoss != boss.Name || double.IsNaN(clock) || boss.Health >= _trailHealth)
            {
                _trailHealth = boss.Health; _trailHold = TrailHoldS;
            }
            else if (_trailHold > 0) _trailHold -= dt;
            else _trailHealth = Mathf.Max(boss.Health, _trailHealth - TrailDrainPerS * dt);
            _trailBoss = boss.Name;
            _trailClock = clock;
            _trail.style.width = Length.Percent(100 * _trailHealth);
        }

        /// <summary>The wide bar's trail now (0 to 1), for tests.</summary>
        public float TrailHealth => _trailHealth;
    }
}
