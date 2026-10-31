using System;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using Game.Client.Title;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Loop
{
    /// <summary>
    /// The level-up's three cards over the frozen run (Resources/Cards: Cards.uxml and Cards.uss),
    /// a minimal version until the HUD (ps-msz.6.9) draws them. It follows the offer at frameSong
    /// (<see cref="SimSnapshot.Offer"/>): each card flips in when frameSong reaches its beat, the
    /// selected one is lit, and the panel goes once a card is taken. The keys reach the sim through
    /// <see cref="KeyInput"/>: Left, Up and Right choose, Space or Enter takes it.
    /// </summary>
    public sealed class LevelUpCards : IPresenter, IDisposable
    {
        static readonly string[] Keys = { "←", "↑", "→" };

        readonly Transform _parent;
        readonly GameContent _content;
        readonly VisualElement[] _cards = new VisualElement[3];
        ScreenPanel _panel;
        int _serial = -1;

        public LevelUpCards(Transform parent, GameContent content)
        {
            _parent = parent;
            _content = content;
        }

        public void OnEvent(in SimEvent e) { }

        public void Present(in PresentFrame frame)
        {
            var offer = frame.State.Offer;
            if (!offer.Open)
            {
                Close();
                return;
            }
            if (_panel == null || offer.Serial != _serial) Open(offer);
            for (int k = 0; k < 3; k++)
            {
                _cards[k].EnableInClassList("card--shown", frame.Song >= offer.Reveal(k));
                _cards[k].EnableInClassList("card--selected", offer.Selected == k);
            }
        }

        void Open(in Offer offer)
        {
            Close();
            _serial = offer.Serial;
            _panel = new ScreenPanel(_parent, "Level-up cards", "Cards/Cards", overlay: true);
            _panel.Root.Q<Label>("heading").text = "LEVEL " + offer.Level;
            var row = _panel.Root.Q("cards");
            for (int k = 0; k < 3; k++)
            {
                var card = offer.Card(k);
                var e = new VisualElement();
                e.AddToClassList("card");
                var gem = new VisualElement();
                gem.AddToClassList("card__gem");
                gem.AddToClassList(card.Item == CardItem.Weapon ? "card__gem--weapon" : card.Item == CardItem.Passive ? "card__gem--passive" : "card__gem--heal");
                e.Add(gem);
                e.Add(Text(Name(card), "card__name"));
                e.Add(Text(card.Item == CardItem.Weapon ? "WEAPON" : card.Item == CardItem.Passive ? "PASSIVE" : "", "card__kind"));
                e.Add(Text(card.Item == CardItem.Heal ? "RECOVER " + _content.Progression.MaxedHeal + " HP"
                    : card.Before == 0 ? "NEW" : $"LEVEL {card.Before} → {card.After}", "card__rank"));
                e.Add(Text(Keys[k], "card__key"));
                _cards[k] = e;
                row.Add(e);
            }
        }

        static Label Text(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }

        string Name(in OfferCard card)
        {
            if (card.Item == CardItem.Heal) return "HEAL";
            string id = card.Item == CardItem.Weapon ? _content.Weapons[card.Index].Id : _content.Passives[card.Index].Id;
            return id.Replace('-', ' ').ToUpperInvariant();
        }

        void Close()
        {
            _panel?.Dispose();
            _panel = null;
        }

        public void Dispose() => Close();
    }
}
