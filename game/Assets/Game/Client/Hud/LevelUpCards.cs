using System;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The level-up's three cards over the dimmed, frozen run (native's slice/cards.rs), following the
    /// offer at frameSong (<see cref="SimSnapshot.Offer"/>). Each card flips over in the
    /// <see cref="FlipS"/> before its beat, its crystal back narrowing to an edge and its face opening
    /// out, so the face is whole exactly on the beat; the selected card is lit and swells a little
    /// when chosen. A card shows its item's gem and mark, name, rank (NEW!, LV 2 > 3), what it does,
    /// and EVO PAIR when its evolution's partner is owned. Once a card is taken the chosen one lifts and
    /// the others fall away over <see cref="ExitS"/>. Everything is a function of song time, so
    /// fixed-time frames show the same flip. The keys reach the sim through KeyInput: Left, Up and
    /// Right choose, Space or Enter takes it.
    /// </summary>
    public sealed class LevelUpCards
    {
        public const double FlipS = 0.2, ExitS = 0.42, DimS = 0.12;
        static readonly string[] Keys = { "LEFT", "UP", "RIGHT" };

        readonly VisualElement _layer, _row;
        readonly Label _heading, _hint;
        readonly GameContent _content;
        readonly VisualElement[] _cards = new VisualElement[3], _faces = new VisualElement[3], _backs = new VisualElement[3];
        Offer _shown;
        int _serial = -1, _selected = -1;
        double _opened = double.NaN, _closed = double.NaN, _chosenAt = double.NegativeInfinity;
        // The latest offer's opening and its confirmation, from the events (song times).
        int _eventOffer = -1;
        double _eventOpened = double.NaN, _eventConfirmed = double.NaN;

        public LevelUpCards(VisualElement layer, GameContent content)
        {
            _layer = layer;
            _row = layer.Q("cards");
            _heading = layer.Q<Label>("cards-heading");
            _hint = layer.Q<Label>("cards-hint");
            _content = content;
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.LevelUp)
            {
                _eventOffer = e.AsLevelUp.Offer;
                _eventOpened = e.T;
                _eventConfirmed = double.NaN;
            }
            else if (e.Kind == SimEventKind.Card && e.AsCard.Confirmed && e.AsCard.Offer == _eventOffer) _eventConfirmed = e.T;
        }

        /// <summary>
        /// A card's flip at song time <paramref name="s"/> for its reveal: its width (1 whole, down to
        /// an edge mid-flip), whether its face shows, and whether it shows at all.
        /// </summary>
        public static float Flip(double s, double reveal, out bool face, out bool shown)
        {
            double p = (s - (reveal - FlipS)) / FlipS;
            shown = p >= 0;
            p = Math.Max(0, Math.Min(1, p));
            face = p >= 0.5;
            return Mathf.Max(0.035f, Mathf.Abs(Mathf.Cos(Mathf.PI * (float)p)));
        }

        public void Present(double s, in Offer offer, SimState arsenal)
        {
            if (_content == null) return;
            if (offer.Open)
            {
                if (offer.Serial != _serial) Build(offer, arsenal);
                _closed = double.NaN;
                if (offer.Selected != _selected)
                {
                    if (_selected >= 0) _chosenAt = s;
                    _selected = offer.Selected;
                }
                _shown = offer;
                ShowOpen(s);
            }
            else if (_serial >= 0 && double.IsNaN(_closed))
                _closed = _eventOffer == _serial && !double.IsNaN(_eventConfirmed) ? Math.Min(s, _eventConfirmed) : s;
            if (!offer.Open) ShowClosing(s);
        }

        void ShowOpen(double s)
        {
            float dim = double.IsNaN(_opened) ? 1 : Mathf.Clamp01((float)((s - _opened) / DimS));
            _layer.style.display = DisplayStyle.Flex;
            _layer.style.opacity = dim;
            for (int k = 0; k < 3; k++)
            {
                float width = Flip(s, _shown.Reveal(k), out bool face, out bool shown);
                bool selected = k == _shown.Selected;
                float swell = selected ? 1 + 0.035f * Mathf.Exp(-(float)(s - _chosenAt) * 13) : 1;
                var card = _cards[k];
                card.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;
                card.style.scale = new Scale(new Vector3(width * swell, swell, 1));
                card.style.translate = new Translate(0, 0);
                card.style.rotate = new Rotate(0);
                card.style.opacity = 1;
                _faces[k].style.display = face ? DisplayStyle.Flex : DisplayStyle.None;
                _backs[k].style.display = face ? DisplayStyle.None : DisplayStyle.Flex;
                card.EnableInClassList("card--selected", selected);
            }
            _hint.text = s + 1e-9 >= _shown.Reveal2 ? "LEFT / UP / RIGHT CHOOSE   ·   SPACE OR ENTER TAKES IT" : "CHOOSE AFTER THE THIRD BEAT";
        }

        void ShowClosing(double s)
        {
            double t = double.IsNaN(_closed) ? 1 : (s - _closed) / ExitS;
            if (t >= 1)
            {
                _layer.style.display = DisplayStyle.None;
                return;
            }
            float p = Mathf.Clamp01((float)t);
            _layer.style.display = DisplayStyle.Flex;
            _layer.style.opacity = 1 - p;
            _hint.text = "";
            for (int k = 0; k < 3; k++)
            {
                var card = _cards[k];
                bool selected = k == _shown.Selected;
                float scale = selected ? 1 + 0.16f * Mathf.Sin(Mathf.PI * p) : 1;
                card.style.visibility = Visibility.Visible;
                card.style.scale = new Scale(new Vector3(scale, scale, 1));
                card.style.translate = new Translate(0, selected ? -48 * p : 560 * p * p);
                card.style.rotate = new Rotate(selected ? 0 : (k - 1) * 14.3f * p);
                card.style.opacity = 1 - p;
                _faces[k].style.display = DisplayStyle.Flex;
                _backs[k].style.display = DisplayStyle.None;
            }
        }

        void Build(in Offer offer, SimState arsenal)
        {
            _serial = offer.Serial;
            _selected = offer.Selected;
            _chosenAt = double.NegativeInfinity;
            _opened = _eventOffer == offer.Serial ? _eventOpened : double.NaN;
            _heading.text = "LEVEL UP";
            _row.Clear();
            for (int k = 0; k < 3; k++)
            {
                var card = offer.Card(k);
                string id = HudText.Id(_content, card);
                var colour = HudText.Colour(id);
                var slot = SlotsView.Element("card");
                var face = SlotsView.Element("card__face");
                var glow = SlotsView.Element("card__gem-glow");
                glow.style.backgroundColor = colour;
                face.Add(glow);
                var gem = SlotsView.Element("card__gem");
                gem.style.backgroundColor = colour;
                gem.Add(SlotsView.Element("gem__core"));
                face.Add(gem);
                face.Add(Text(HudText.Mark(id), "card__mark"));
                face.Add(Text(HudText.Name(id), "card__name"));
                var rank = Text(HudText.Rank(card), "card__rank");
                rank.style.color = colour;
                face.Add(rank);
                face.Add(Text(HudText.Description(_content, card), "card__text"));
                string partner = Partner(card, arsenal);
                if (partner != null) face.Add(Text("EVO PAIR  ·  " + HudText.Name(partner), "card__pair"));
                else face.Add(Text(Keys[k], "card__key"));
                var back = SlotsView.Element("card__back");
                back.Add(SlotsView.Element("card__back-facet"));
                slot.Add(back);
                slot.Add(face);
                _cards[k] = slot;
                _faces[k] = face;
                _backs[k] = back;
                _row.Add(slot);
            }
        }

        /// <summary>The owned partner a card's item evolves with (cards.rs' pair): a weapon's passive, or a passive's weapon.</summary>
        string Partner(in OfferCard card, SimState arsenal)
        {
            if (card.Item == CardItem.Heal) return null;
            foreach (var def in _content.Weapons)
            {
                if (!def.IsEvolution) continue;
                if (card.Item == CardItem.Weapon && def.EvolutionOf == _content.Weapons[card.Index].Id && Owns(arsenal.PassiveSlots, _content.PassiveIndex(def.Requires)))
                    return def.Requires;
                if (card.Item == CardItem.Passive && def.Requires == _content.Passives[card.Index].Id && Owns(arsenal.WeaponSlots, _content.WeaponIndex(def.EvolutionOf)))
                    return def.EvolutionOf;
            }
            return null;
        }

        static bool Owns(ReadOnlySpan<OwnedItem> slots, int index)
        {
            foreach (var slot in slots)
                if (slot.Index == index) return true;
            return false;
        }

        internal static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }
    }
}
