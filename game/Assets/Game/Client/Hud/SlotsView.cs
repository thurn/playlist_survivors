using System;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The arsenal's slots (hud.rs' inventory): six weapons over six passives, each a crystal tile
    /// with its item's gem in its colour, its mark, and a pip per rank lit up to its rank. An empty
    /// slot recedes to a faint dot, so the twelve never dominate the run's opening. It rebuilds a
    /// row only when the row's items or ranks change.
    /// </summary>
    public sealed class SlotsView
    {
        const int Slots = 6;

        readonly VisualElement _weapons, _passives;
        readonly GameContent _content;
        readonly OwnedItem[] _shownWeapons = new OwnedItem[Slots], _shownPassives = new OwnedItem[Slots];
        int _weaponCount = -1, _passiveCount = -1;

        public SlotsView(VisualElement weapons, VisualElement passives, GameContent content)
        {
            _weapons = weapons;
            _passives = passives;
            _content = content;
        }

        public void Show(ReadOnlySpan<OwnedItem> weapons, ReadOnlySpan<OwnedItem> passives)
        {
            if (_content == null) return;
            if (Changed(weapons, _shownWeapons, ref _weaponCount)) Build(_weapons, weapons, true);
            if (Changed(passives, _shownPassives, ref _passiveCount)) Build(_passives, passives, false);
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

        void Build(VisualElement row, ReadOnlySpan<OwnedItem> items, bool weapons)
        {
            row.Clear();
            for (int i = 0; i < Slots; i++)
            {
                var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                slot.AddToClassList("slot");
                row.Add(slot);
                if (i >= items.Length)
                {
                    slot.Add(Element("slot__dot"));
                    continue;
                }
                string id = weapons ? _content.Weapons[items[i].Index].Id : _content.Passives[items[i].Index].Id;
                int max = weapons ? _content.Weapons[items[i].Index].MaxRank : _content.Passives[items[i].Index].MaxRank;
                var colour = HudText.Colour(id);
                slot.AddToClassList("slot--filled");
                var gem = Element("slot__gem");
                gem.style.backgroundColor = colour;
                gem.Add(Element("gem__core"));
                slot.Add(gem);
                var mark = new Label(HudText.Mark(id));
                mark.AddToClassList("slot__mark");
                slot.Add(mark);
                var pips = Element("slot__pips");
                for (int r = 0; r < max; r++)
                {
                    var pip = Element("slot__pip");
                    if (r < items[i].Rank) pip.style.backgroundColor = colour;
                    pips.Add(pip);
                }
                slot.Add(pips);
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
