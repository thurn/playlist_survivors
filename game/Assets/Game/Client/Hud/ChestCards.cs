using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>A chest's roulette as the HUD draws it: its tier, its eighth-note ticks, the downbeat it lands on, and its rewards.</summary>
    public struct ChestRoulette
    {
        public bool Open;
        public int Tier;
        /// <summary>When it opened, when it lands (a downbeat), and when its card goes (song times).</summary>
        public double Opened, Landed, Closes;
        /// <summary>The eighth notes it ticks on, up to the landing.</summary>
        public double[] Ticks;
        /// <summary>Each reward's item id and the rank it brings.</summary>
        public string[] Rewards;
        public int[] Ranks;
    }

    /// <summary>
    /// A chest's roulette over the dimmed run (native's slice/treasure.rs): TREASURE over one, three or
    /// five gold-lit gems (the tier's items) cycling through the arsenal's items on each eighth-note
    /// tick, jittering less as the ticks run out, then landing together on the downbeat: TREASURE
    /// FOUND, each gem swelling with its reward's name and rank. The sim's chests (ps-msz.6.8) fill
    /// <see cref="ChestRoulette"/>; until then `--chest-preview S[:TIER]` opens a preview at song time S
    /// whose ticks and landing come from the beats known then (eight eighths from the next
    /// downbeat), with the hero's own items as its rewards, for stills. The preview freezes nothing.
    /// </summary>
    public sealed class ChestCards
    {
        public const double ShowAfterLandingS = 2.5;
        /// <summary>The roulette's ticks before it lands (native's chests.rs: eight eighths, two two-beat bars).</summary>
        public const int Eighths = 8;

        readonly VisualElement _layer, _row;
        readonly Label _heading, _hint;
        readonly GameContent _content;
        readonly List<(VisualElement Gem, Label Mark, Label Name, Label Rank)> _items = new List<(VisualElement, Label, Label, Label)>();
        readonly double _previewAt = double.NaN;
        readonly int _previewTier = 3;
        ChestRoulette _roulette;

        public ChestCards(VisualElement layer, GameContent content, string preview)
        {
            _layer = layer;
            _row = layer.Q("chest-items");
            _heading = layer.Q<Label>("chest-heading");
            _hint = layer.Q<Label>("chest-hint");
            _content = content;
            if (preview == null) return;
            var parts = preview.Split(':');
            _previewAt = double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (parts.Length > 1) _previewTier = Math.Max(1, Math.Min(3, int.Parse(parts[1], CultureInfo.InvariantCulture)));
        }

        /// <summary>Ticks so far at song time <paramref name="s"/>.</summary>
        public static int TicksBy(double[] ticks, double s)
        {
            int n = 0;
            while (n < ticks.Length && ticks[n] <= s + 1e-9) n++;
            return n;
        }

        public void Present(double s, Simulation sim, in HeardBeat beat)
        {
            if (_content == null) return;
            if (!_roulette.Open && !double.IsNaN(_previewAt) && s >= _previewAt && beat.Has) Preview(s, sim);
            if (!_roulette.Open || s >= _roulette.Closes)
            {
                _layer.style.display = DisplayStyle.None;
                return;
            }
            _layer.style.display = DisplayStyle.Flex;
            _layer.style.opacity = Mathf.Clamp01((float)((s - _roulette.Opened) / LevelUpCards.DimS));
            bool landed = s + 1e-9 >= _roulette.Landed;
            int ticks = TicksBy(_roulette.Ticks, s);
            _heading.text = landed ? "TREASURE FOUND" : "TREASURE";
            _hint.text = landed ? "REWARDS RECEIVED" : "TWO BARS   ·   IT LANDS ON THE DOWNBEAT";
            float burst = landed ? Mathf.Exp(-3 * (float)(s - _roulette.Landed)) : 0;
            var cycle = _content.Weapons;
            for (int k = 0; k < _items.Count; k++)
            {
                var (gem, mark, name, rank) = _items[k];
                string id = landed ? _roulette.Rewards[k] : cycle[(ticks + k) % cycle.Length].Id;
                // Before it lands each gem jitters, less as the ticks run out (treasure.rs' spin).
                float jitter = landed ? 0 : Mathf.Sin((float)s * 18 + k) * (1 - ticks / 9f) * 16;
                float swell = 1 + 0.17f * burst;
                gem.style.translate = new Translate(0, jitter);
                gem.style.scale = new Scale(new Vector3(swell, swell, 1));
                gem.style.backgroundColor = HudText.Colour(id);
                mark.text = HudText.Mark(id);
                mark.style.translate = new Translate(0, jitter);
                name.text = landed ? HudText.Name(id) : "";
                rank.text = landed ? (_roulette.Ranks[k] <= 1 ? "NEW!" : "LV " + _roulette.Ranks[k]) : "";
            }
        }

        /// <summary>Shows <paramref name="roulette"/> (the sim's chest), replacing any shown.</summary>
        public void Show(in ChestRoulette roulette)
        {
            _roulette = roulette;
            _row.Clear();
            _items.Clear();
            for (int k = 0; k < roulette.Rewards.Length; k++)
            {
                var item = SlotsView.Element("reward");
                item.Add(SlotsView.Element("reward__glow"));
                var gem = SlotsView.Element("reward__gem");
                gem.Add(SlotsView.Element("gem__core"));
                item.Add(gem);
                var mark = LevelUpCards.Text("", "reward__mark");
                item.Add(mark);
                var name = LevelUpCards.Text("", "reward__name");
                item.Add(name);
                var rank = LevelUpCards.Text("", "reward__rank");
                item.Add(rank);
                _row.Add(item);
                _items.Add((gem, mark, name, rank));
            }
        }

        /// <summary>
        /// The preview's roulette from song time <paramref name="s"/>: native's eight eighth-note ticks
        /// (two of its two-beat bars) from the next downbeat, at that beat's period, landing on the
        /// downbeat after them. It waits for that downbeat to be committed, a moment before it sounds.
        /// </summary>
        void Preview(double s, Simulation sim)
        {
            var beats = sim.Stream.Beats;
            int known = HeardBeats.KnownBy(sim.Stream, s);
            int first = -1;
            for (int i = 0; i < known; i++)
                if (beats[i].T > s && sim.Beats.IsDownbeat(i)) { first = i; break; }
            if (first < 0) return; // not committed yet: try again next frame
            double from = beats[first].T, period = sim.Beats.PeriodOf(first);
            var ticks = new double[Eighths];
            for (int k = 0; k < Eighths; k++) ticks[k] = from + k * period / 2;
            double landed = from + Eighths * period / 2;
            int count = _content.Progression.Chests.ItemsByTier[_previewTier - 1];
            var rewards = new string[count];
            var ranks = new int[count];
            var owned = sim.State.WeaponSlots;
            for (int k = 0; k < count; k++)
            {
                if (k < owned.Length)
                {
                    rewards[k] = _content.Weapons[owned[k].Index].Id;
                    ranks[k] = owned[k].Rank + 1;
                }
                else
                {
                    rewards[k] = _content.Passives[k % _content.Passives.Length].Id;
                    ranks[k] = 1;
                }
            }
            Show(new ChestRoulette
            {
                Open = true,
                Tier = _previewTier,
                Opened = s,
                Ticks = ticks,
                Landed = landed,
                Closes = landed + ShowAfterLandingS,
                Rewards = rewards,
                Ranks = ranks,
            });
        }
    }
}
