using System;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The run's end card (native's slice/recap.rs): VICTORY in pale green or YOU FELL in red over the
    /// darkened run, fading in over half a second from the run's end; the run clock, kills and level;
    /// then a row per weapon that dealt damage (the arsenal's in slot order, then any it evolved away):
    /// its damage and damage per second times 10, and how long it was held, an evolution's name in
    /// gold, each with a hairline as long as its share of the most damage. A run ended by hand shows
    /// no card (the loop finishes at once).
    /// </summary>
    public sealed class EndCard
    {
        public const double FadeS = 0.5;

        readonly VisualElement _layer, _rows, _edge;
        readonly Label _title, _stats;
        readonly GameContent _content;
        double _endedAt = double.NaN;
        RunEnd _why;
        bool _built;

        public EndCard(VisualElement layer, GameContent content)
        {
            _layer = layer;
            _rows = layer.Q("end-rows");
            _edge = layer.Q("end-edge");
            _title = layer.Q<Label>("end-title");
            _stats = layer.Q<Label>("end-stats");
            _content = content;
        }

        /// <summary>The run's end event, ahead of frameSong.</summary>
        public void Ended(double t, RunEnd why)
        {
            _endedAt = t;
            _why = why;
        }

        /// <param name="final">The sim's state at the frontier, past the end: the arsenal and its damage as they ended.</param>
        /// <param name="held">How long each weapon was held.</param>
        public void Present(double s, in SimSnapshot state, int kills, SimState final, HeldTimes held)
        {
            bool shows = (_why == RunEnd.Victory || _why == RunEnd.Death) && s + 1e-9 >= _endedAt;
            _layer.style.display = shows ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shows) return;
            _layer.style.opacity = Mathf.Clamp01((float)((s - _endedAt) / FadeS));
            if (_built) return;
            _built = true;
            var colour = _why == RunEnd.Victory ? new Color(0.65f, 1f, 0.76f) : new Color(1f, 0.3f, 0.36f);
            _title.text = _why == RunEnd.Victory ? "VICTORY" : "YOU FELL";
            _title.style.color = colour;
            _edge.style.backgroundColor = colour;
            _stats.text = $"TIME {HudText.Clock(state.RunClock)}   ·   KILLS {kills}   ·   LEVEL {Math.Max(1, state.Level)}";
            if (_content != null) Rows(state.RunClock, final, held);
        }

        void Rows(double runClock, SimState final, HeldTimes times)
        {
            var damage = final.WeaponDamage;
            var order = new System.Collections.Generic.List<int>();
            foreach (var slot in final.WeaponSlots) order.Add(slot.Index);
            for (int i = 0; i < damage.Length; i++)
                if (damage[i] > 0 && !order.Contains(i)) order.Add(i);
            double most = 1;
            foreach (int i in order) most = Math.Max(most, i < damage.Length ? damage[i] : 0);
            for (int r = 0; r < order.Count; r++)
            {
                int i = order[r];
                var def = _content.Weapons[i];
                double dealt = (i < damage.Length ? damage[i] : 0) * HudText.DamageScale;
                double held = times.Of(i, runClock);
                var tint = def.IsEvolution ? new Color(1f, 0.83f, 0.4f) : new Color(0.8f, 0.88f, 1f);
                var row = SlotsView.Element("end__row");
                row.AddToClassList(r % 2 == 0 ? "end__row--even" : "end__row--odd");
                var gem = SlotsView.Element("end__gem");
                gem.style.backgroundColor = HudText.Colour(def.Id);
                row.Add(gem);
                var name = LevelUpCards.Text(HudText.Name(def.Id), "end__cell");
                name.AddToClassList("end__cell--name");
                name.style.color = tint;
                row.Add(name);
                row.Add(LevelUpCards.Text(HudText.Amount(dealt), "end__cell"));
                row.Add(LevelUpCards.Text(HudText.Held(held), "end__cell"));
                row.Add(LevelUpCards.Text(HudText.Amount(held > 0 ? dealt / held : 0), "end__cell"));
                var share = SlotsView.Element("end__share");
                share.style.width = (float)(580 * dealt / (most * HudText.DamageScale));
                share.style.backgroundColor = tint;
                row.Add(share);
                _rows.Add(row);
            }
        }
    }

    /// <summary>
    /// How long each weapon was held on the run clock (native's eval/recap.rs): from when it joins
    /// the slots until it leaves them, evolved away; a rank up doesn't restart it.
    /// </summary>
    public sealed class HeldTimes
    {
        readonly double[] _since, _held;

        public HeldTimes(int weapons)
        {
            _since = new double[weapons];
            _held = new double[weapons];
            for (int i = 0; i < weapons; i++) _since[i] = double.NaN;
        }

        /// <summary>The slots as they are at <paramref name="runClock"/>.</summary>
        public void Update(ReadOnlySpan<OwnedItem> slots, double runClock)
        {
            for (int i = 0; i < _since.Length; i++)
            {
                if (double.IsNaN(_since[i]) || Holds(slots, i)) continue;
                _held[i] += Math.Max(0, runClock - _since[i]);
                _since[i] = double.NaN;
            }
            foreach (var slot in slots)
                if (slot.Index < _since.Length && double.IsNaN(_since[slot.Index])) _since[slot.Index] = runClock;
        }

        /// <summary>How long weapon <paramref name="weapon"/> was held by <paramref name="runClock"/> (s).</summary>
        public double Of(int weapon, double runClock) =>
            weapon < 0 || weapon >= _held.Length ? 0
            : _held[weapon] + (double.IsNaN(_since[weapon]) ? 0 : Math.Max(0, runClock - _since[weapon]));

        static bool Holds(ReadOnlySpan<OwnedItem> slots, int weapon)
        {
            foreach (var slot in slots)
                if (slot.Index == weapon) return true;
            return false;
        }
    }
}
