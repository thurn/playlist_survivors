using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.UI
{
    /// <summary>
    /// A run's UI (design.md, UI): the hero's indicator in the world (<see cref="IndicatorPresenter"/>), the HUD
    /// (<see cref="Hud"/>) and the card moment's cards (<see cref="CardsView"/>) on one panel. The loop hands it every
    /// settled event (<see cref="Take"/>: an offer shows its cards, the arrows' focus moves, a pick puts them away) and
    /// presents it each frame from the sim's snapshot: the experience toward the next level, the boss bar in a duel, the
    /// held levels' dot. A card's click is <see cref="Picked"/>, which the loop taps as the card's number key, so the sim
    /// picks it as it picks a key.
    /// </summary>
    public sealed class RunUi : IDisposable
    {
        public const int Order = 1;

        readonly UiPanel _panel;
        readonly Palette _palette;
        readonly GameContent _content;
        readonly CardFaces _faces;
        readonly List<CardFace> _shown = new List<CardFace>(3);

        public readonly Hud Hud;
        public readonly CardsView Cards = new CardsView();
        public readonly IndicatorPresenter Indicator;

        public RunUi(Transform parent, Palette palette, GameContent content, CardFaces faces, int layer = 0)
        {
            _palette = palette;
            _content = content;
            _faces = faces;
            _panel = new UiPanel(parent, "Run UI", Order);
            Hud = new Hud(palette);
            _panel.Root.Add(Hud);
            _panel.Root.Add(Cards);
            Indicator = new IndicatorPresenter(palette, layer);
        }

        /// <summary>A card was clicked: its slot.</summary>
        public event Action<int> Picked
        {
            add => Cards.Picked += value;
            remove => Cards.Picked -= value;
        }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            Indicator.Take(e);
            switch (e.Kind)
            {
                case SimEventKind.Offer:
                    _shown.Clear();
                    foreach (int code in new[] { e.I0, e.I1, e.I2 })
                        if (code >= 0) _shown.Add(_faces.Of(Card.FromCode(code)));
                    Cards.Show(_shown, e.T);
                    break;
                case SimEventKind.CardFocus:
                    Cards.Focus(e.I0);
                    break;
                case SimEventKind.Pick:
                    Cards.Hide();
                    break;
            }
        }

        /// <summary>The frame at song time <paramref name="now"/>.</summary>
        public void Present(double now, Simulation sim, Camera camera)
        {
            var s = sim.Snapshot();
            Indicator.Present(now, s, sim.Beats, s.HeldLevels > 0, camera);
            Hud.Present(s.ExperienceNeeded > 0 ? (float)(s.Experience / s.ExperienceNeeded) : 0, BossBar.Of(s, _palette, _content));
            Cards.Present(now);
        }

        public void Dispose()
        {
            Indicator.Dispose();
            _panel.Dispose();
        }
    }
}
