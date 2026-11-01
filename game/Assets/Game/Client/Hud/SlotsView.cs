using System;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The arsenal's slots (hud.rs' inventory): six weapons over six passives, each a crystal tile
    /// with its item's gem in its colour, its mark, and a pip per rank lit up to its rank. An empty
    /// slot recedes to a faint dot, so the twelve never dominate the run's opening. It fills a row
    /// in only when the row's items or ranks change.
    /// </summary>
    public sealed class SlotsView
    {
        const int Slots = 6;

        /// <summary>One slot's parts, built once: a change shows, hides and colours them (a new tree each time was garbage).</summary>
        sealed class Slot
        {
            public VisualElement Root, Dot, Gem, PipRow;
            public Label Mark;
            public VisualElement[] Pips;
        }

        readonly GameContent _content;
        readonly Slot[] _weaponSlots, _passiveSlots;
        readonly OwnedItem[] _shownWeapons = new OwnedItem[Slots], _shownPassives = new OwnedItem[Slots];
        int _weaponCount = -1, _passiveCount = -1;

        public SlotsView(VisualElement weapons, VisualElement passives, GameContent content)
        {
            _content = content;
            if (content == null) return;
            int pips = 0;
            foreach (var w in content.Weapons) pips = Math.Max(pips, w.MaxRank);
            foreach (var p in content.Passives) pips = Math.Max(pips, p.MaxRank);
            _weaponSlots = Row(weapons, pips);
            _passiveSlots = Row(passives, pips);
        }

        public void Show(ReadOnlySpan<OwnedItem> weapons, ReadOnlySpan<OwnedItem> passives)
        {
            if (_content == null) return;
            if (Changed(weapons, _shownWeapons, ref _weaponCount)) Fill(_weaponSlots, weapons, true);
            if (Changed(passives, _shownPassives, ref _passiveCount)) Fill(_passiveSlots, passives, false);
        }

        static bool Changed(ReadOnlySpan<OwnedItem> items, OwnedItem[] shown, ref int count)
        {
            bool changed = items.Length != count;
            for (int i = 0; i < items.Length && i < Slots && !changed; i++)
                changed = items[i].Index != shown[i].Index || items[i].Rank != shown[i].Rank;
            if (!changed) return false;
            count = items.Length;
            for (int i = 0; i < items.Length && i < Slots; i++) shown[i] = items[i];
            return true;
        }

        static Slot[] Row(VisualElement row, int pips)
        {
            row.Clear();
            var slots = new Slot[Slots];
            for (int i = 0; i < Slots; i++)
            {
                var s = slots[i] = new Slot { Root = Element("slot"), Dot = Element("slot__dot"), Gem = Element("slot__gem"), PipRow = Element("slot__pips") };
                s.Root.Add(s.Dot);
                s.Gem.Add(Element("gem__core"));
                s.Root.Add(s.Gem);
                s.Mark = new Label { pickingMode = PickingMode.Ignore };
                s.Mark.AddToClassList("slot__mark");
                s.Root.Add(s.Mark);
                s.Pips = new VisualElement[pips];
                for (int r = 0; r < pips; r++) s.PipRow.Add(s.Pips[r] = Element("slot__pip"));
                s.Root.Add(s.PipRow);
                row.Add(s.Root);
            }
            return slots;
        }

        void Fill(Slot[] slots, ReadOnlySpan<OwnedItem> items, bool weapons)
        {
            for (int i = 0; i < Slots; i++)
            {
                var s = slots[i];
                bool filled = i < items.Length;
                s.Root.EnableInClassList("slot--filled", filled);
                s.Dot.style.display = filled ? DisplayStyle.None : DisplayStyle.Flex;
                var shown = filled ? DisplayStyle.Flex : DisplayStyle.None;
                s.Gem.style.display = shown;
                s.Mark.style.display = shown;
                s.PipRow.style.display = shown;
                if (!filled) continue;
                string id = weapons ? _content.Weapons[items[i].Index].Id : _content.Passives[items[i].Index].Id;
                int max = weapons ? _content.Weapons[items[i].Index].MaxRank : _content.Passives[items[i].Index].MaxRank;
                var colour = HudText.Colour(id);
                s.Gem.style.backgroundColor = colour;
                HudPresenter.Show(s.Mark, HudText.Mark(id));
                for (int r = 0; r < s.Pips.Length; r++)
                {
                    s.Pips[r].style.display = r < max ? DisplayStyle.Flex : DisplayStyle.None;
                    // A lit pip in the item's colour; an unlit one as the sheet draws it.
                    s.Pips[r].style.backgroundColor = r < items[i].Rank ? new StyleColor(colour) : new StyleColor(StyleKeyword.Null);
                }
            }
        }

        internal static VisualElement Element(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            return e;
        }
    }
}
