using System;
using System.Collections.Generic;
using Champions.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>
    /// Up to three brief choices: icon, name, description and level pips. Diamonds mark weapon forms at 3/5/8.
    /// Clicks pick on pointer down; number keys or arrows and Enter reach the sim, which returns focus events.
    /// Cards rise together over <see cref="AppearS"/> and remain the same size across stat and form choices.
    /// </summary>
    public sealed class CardsView : VisualElement
    {
        public const double AppearS = 0.15;
        const float RisePx = 18;

        readonly List<VisualElement> _cards = new List<VisualElement>();
        double _shownAt = double.NaN;

        /// <summary>A card was clicked: its slot (0-2).</summary>
        public event Action<int> Picked;

        public CardsView()
        {
            AddToClassList("cards");
            pickingMode = PickingMode.Ignore; // only the cards take the pointer
            style.display = DisplayStyle.None;
        }

        /// <summary>Cards are showing.</summary>
        public bool Up => !double.IsNaN(_shownAt);

        public int Count => _cards.Count;

        /// <summary>The card in <paramref name="slot"/>, for the clicks' tests and the stills.</summary>
        public VisualElement CardAt(int slot) => _cards[slot];

        /// <summary>Shows <paramref name="faces"/> (one to three) at song time <paramref name="now"/>, nothing focused.</summary>
        public void Show(IReadOnlyList<CardFace> faces, double now)
        {
            if (faces.Count < 1 || faces.Count > 3) throw new ArgumentException($"{faces.Count} cards: one to three");
            Clear();
            _cards.Clear();
            for (int i = 0; i < faces.Count; i++)
            {
                var card = Card(faces[i], i);
                _cards.Add(card);
                Add(card);
            }
            _shownAt = now;
            style.display = DisplayStyle.Flex;
            Present(now);
        }

        /// <summary>The arrows' focus: card <paramref name="slot"/>, or none (-1).</summary>
        public void Focus(int slot)
        {
            for (int i = 0; i < _cards.Count; i++) Mark(_cards[i], "card--focused", i == slot);
        }

        /// <summary>Sets a card's class and redraws its frame, which draws brighter when focused or hovered.</summary>
        static void Mark(VisualElement card, string className, bool on)
        {
            card.EnableInClassList(className, on);
            card.Q<CardFrame>().MarkDirtyRepaint();
        }

        public void Hide()
        {
            _shownAt = double.NaN;
            style.display = DisplayStyle.None;
            Clear();
            _cards.Clear();
        }

        /// <summary>The frame at song time <paramref name="now"/>: the cards' rise.</summary>
        public void Present(double now)
        {
            if (!Up) return;
            float t = Mathf.Clamp01((float)((now - _shownAt) / AppearS));
            float eased = 1 - (1 - t) * (1 - t);
            style.opacity = eased;
            style.translate = new Translate(0, RisePx * (1 - eased));
        }

        VisualElement Card(in CardFace face, int slot)
        {
            var card = new VisualElement { name = "card-" + slot };
            card.AddToClassList("card");
            card.AddToClassList("card--" + face.Kind.ToString().ToLowerInvariant());
            if (face.FormChange) card.AddToClassList("card--form");
            card.Add(new CardFrame(face.Kind));
            var icon = new IconElement(face.Icon);
            icon.AddToClassList("card__icon");
            card.Add(icon);
            card.Add(Text(face.Name, "card__name"));
            card.Add(Text(face.Line, "card__line"));
            card.Add(Pips(face));
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) Picked?.Invoke(slot);
            });
            card.RegisterCallback<PointerEnterEvent>(_ => Mark(card, "card--hover", true));
            card.RegisterCallback<PointerLeaveEvent>(_ => Mark(card, "card--hover", false));
            return card;
        }

        static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; // part of its card, which takes the pointer
            label.AddToClassList(className);
            return label;
        }

        /// <summary>The level it gives out of its content cap; diamond pips mark weapon form levels.</summary>
        static VisualElement Pips(in CardFace face)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("card__pips");
            int shown = face.Top;
            for (int i = 0; i < shown; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("pip");
                if (face.Kind == CardKind.Weapon && (i == 2 || i == 4 || i == 7)) pip.AddToClassList("pip--form");
                if (i < face.Rank) pip.AddToClassList("pip--lit");
                row.Add(pip);
            }
            return row;
        }

        /// <summary>A card's dark body and its outline, shaped by its kind; brighter when focused or hovered.</summary>
        sealed class CardFrame : VisualElement
        {
            const float Slant = 30, Cut = 24, Round = 30, Inset = 3;

            readonly CardKind _kind;

            public CardFrame(CardKind kind)
            {
                _kind = kind;
                AddToClassList("card__frame");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext context)
            {
                var r = contentRect;
                if (!(r.width > 0)) return;
                var card = parent;
                bool lit = card != null && (card.ClassListContains("card--focused") || card.ClassListContains("card--hover"));
                var p = context.painter2D;
                p.lineJoin = LineJoin.Miter;
                Shape(p, new Rect(r.x + Inset, r.y + Inset, r.width - 2 * Inset, r.height - 2 * Inset));
                p.fillColor = new Color(0.031f, 0.035f, 0.051f, 1f); // void.body: nothing of the world shows through
                p.Fill();
                p.strokeColor = new Color(0.957f, 0.973f, 1f, lit ? 1f : 0.5f); // hero.body
                p.lineWidth = lit ? 2.5f : 1.5f;
                p.Stroke();
                if (!lit) return;
                Shape(p, new Rect(r.x, r.y, r.width, r.height));
                p.strokeColor = new Color(0.957f, 0.973f, 1f, 0.22f);
                p.lineWidth = 1.5f;
                p.Stroke();
            }

            void Shape(Painter2D p, Rect r)
            {
                p.BeginPath();
                switch (_kind)
                {
                    case CardKind.Dash: // slanted forward
                        p.MoveTo(new Vector2(r.xMin + Slant, r.yMin));
                        p.LineTo(new Vector2(r.xMax, r.yMin));
                        p.LineTo(new Vector2(r.xMax - Slant, r.yMax));
                        p.LineTo(new Vector2(r.xMin, r.yMax));
                        break;
                    case CardKind.Weapon:
                    case CardKind.Attack: // corners cut like a blade's
                        p.MoveTo(new Vector2(r.xMin + Cut, r.yMin));
                        p.LineTo(new Vector2(r.xMax - Cut, r.yMin));
                        p.LineTo(new Vector2(r.xMax, r.yMin + Cut));
                        p.LineTo(new Vector2(r.xMax, r.yMax - Cut));
                        p.LineTo(new Vector2(r.xMax - Cut, r.yMax));
                        p.LineTo(new Vector2(r.xMin + Cut, r.yMax));
                        p.LineTo(new Vector2(r.xMin, r.yMax - Cut));
                        p.LineTo(new Vector2(r.xMin, r.yMin + Cut));
                        break;
                    default: // a mod: rounded
                        p.MoveTo(new Vector2(r.xMin + Round, r.yMin));
                        p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), Round);
                        p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), Round);
                        p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), Round);
                        p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), Round);
                        break;
                }
                p.ClosePath();
            }
        }
    }
}
