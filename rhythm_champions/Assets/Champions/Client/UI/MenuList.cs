using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>A menu's keys (<see cref="Flow"/> reads them from the keyboard): the arrows or WASD, Enter or Space, Esc.</summary>
    public enum MenuKey { Up, Down, Left, Right, Choose, Back }

    /// <summary>A screen that takes the menu's keys; true when it used the key.</summary>
    public interface IMenuScreen
    {
        bool Key(MenuKey key);
    }

    /// <summary>
    /// A column of rows of one or two words (design.md, UI > Menus): the arrows move the focus, Enter or Space chooses
    /// it, the pointer focuses the row it is over and chooses the row it presses. A dim row (a note) takes neither.
    /// </summary>
    public sealed class MenuList : VisualElement
    {
        public readonly struct Row
        {
            public readonly string Text;
            public readonly Action Chosen;
            /// <summary>Left and Right on the row (a setting's value), or null.</summary>
            public readonly Action<int> Stepped;

            public Row(string text, Action chosen, Action<int> stepped = null)
            {
                Text = text;
                Chosen = chosen;
                Stepped = stepped;
            }

            public bool Pickable => Chosen != null || Stepped != null;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly List<Label> _labels = new List<Label>();

        public MenuList()
        {
            AddToClassList("menu");
            pickingMode = PickingMode.Ignore; // the rows take the pointer
        }

        public int Focused { get; private set; } = -1;
        public int Count => _rows.Count;
        public Label LabelAt(int i) => _labels[i];

        /// <summary>Sets the rows, keeping the focus on the row with the same text if there is one, else the first pickable.</summary>
        public void Set(IReadOnlyList<Row> rows)
        {
            string focused = Focused >= 0 && Focused < _rows.Count ? _rows[Focused].Text : null;
            Clear();
            _rows.Clear();
            _labels.Clear();
            Focused = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int index = i;
                var label = new Label(row.Text);
                label.AddToClassList("menu__row");
                if (row.Pickable)
                {
                    label.RegisterCallback<PointerEnterEvent>(_ => Focus(index));
                    label.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        Focus(index);
                        Key(MenuKey.Choose);
                    });
                }
                else
                {
                    label.AddToClassList("menu__row--dim");
                    label.pickingMode = PickingMode.Ignore; // a note, not a choice
                }
                _rows.Add(row);
                _labels.Add(label);
                Add(label);
                if (row.Text == focused && row.Pickable) Focused = i;
            }
            if (Focused < 0) Focus(Next(-1, 1));
            else Focus(Focused);
        }

        /// <summary>Changes row <paramref name="i"/>'s text (a setting's value).</summary>
        public void SetText(int i, string text)
        {
            _rows[i] = new Row(text, _rows[i].Chosen, _rows[i].Stepped);
            _labels[i].text = text;
        }

        public void Focus(int i)
        {
            if (i >= 0 && (i >= _rows.Count || !_rows[i].Pickable)) return;
            Focused = i;
            for (int k = 0; k < _labels.Count; k++) _labels[k].EnableInClassList("menu__row--focused", k == i);
        }

        /// <summary>The next pickable row from <paramref name="from"/> in direction <paramref name="dir"/>, wrapping; -1 when none.</summary>
        int Next(int from, int dir)
        {
            for (int step = 1; step <= _rows.Count; step++)
            {
                int i = ((from + dir * step) % _rows.Count + _rows.Count) % _rows.Count;
                if (_rows[i].Pickable) return i;
            }
            return -1;
        }

        public bool Key(MenuKey key)
        {
            if (_rows.Count == 0) return false;
            switch (key)
            {
                case MenuKey.Up:
                case MenuKey.Down:
                    Focus(Next(Focused < 0 ? (key == MenuKey.Down ? -1 : 0) : Focused, key == MenuKey.Down ? 1 : -1));
                    return true;
                case MenuKey.Left:
                case MenuKey.Right:
                    if (Focused < 0 || _rows[Focused].Stepped == null) return false;
                    _rows[Focused].Stepped(key == MenuKey.Right ? 1 : -1);
                    return true;
                case MenuKey.Choose:
                    if (Focused < 0) return false;
                    var row = _rows[Focused];
                    if (row.Chosen != null) row.Chosen();
                    else row.Stepped?.Invoke(1);
                    return true;
                default:
                    return false;
            }
        }
    }
}
