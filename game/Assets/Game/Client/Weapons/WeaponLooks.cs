using PS.Game.Sim.Content;
using UnityEngine;

namespace Game.Client.Weapons
{
    /// <summary>
    /// Each weapon's look by its content index (unity-game.md, The Other Weapons and Weapon and
    /// Ability Effects): which family draws and sounds it, its crystal's colour (abilities carry their
    /// own colours, white at the core; the sword's is white), and whether it is an evolution, whose
    /// distinct layer goes on top of its family's (the chests bead's).
    /// </summary>
    public sealed class WeaponLooks
    {
        public enum Family { None, Sword, Wand, Bible, Ring, Chain }

        /// <summary>Magic Wand's violet crystal bolts.</summary>
        public static readonly Color Wand = new Color(0.55f, 0.2f, 1f);
        /// <summary>King Bible's gold crystal shards.</summary>
        public static readonly Color Bible = new Color(1f, 0.6f, 0.12f);
        /// <summary>Lightning Ring's electric blue (native's), Thunder Loop's magenta (native's) and Chain Lightning's lemon yellow.</summary>
        public static readonly Color Ring = new Color(0.4f, 0.65f, 1f), Thunder = new Color(1f, 0.4f, 0.85f), Chain = new Color(1f, 0.95f, 0.35f);

        readonly Family[] _family;
        readonly bool[] _evolved;

        /// <param name="weapons">Content's weapons, or null for none.</param>
        public WeaponLooks(WeaponDef[] weapons)
        {
            int n = weapons?.Length ?? 0;
            _family = new Family[n];
            _evolved = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var def = weapons[i];
                _evolved[i] = def.IsEvolution;
                switch (def.Shape.Kind)
                {
                    case ShapeKind.Sword: _family[i] = Family.Sword; break;
                    case ShapeKind.Missile: _family[i] = Family.Wand; break;
                    case ShapeKind.Orbit: _family[i] = Family.Bible; break;
                    case ShapeKind.Strike: _family[i] = Family.Ring; break;
                    case ShapeKind.Chain: _family[i] = Family.Chain; break;
                }
            }
        }

        /// <summary>Content's weapons.</summary>
        public int Count => _family.Length;

        public Family Of(int weapon) => weapon >= 0 && weapon < _family.Length ? _family[weapon] : Family.None;

        public bool Evolved(int weapon) => weapon >= 0 && weapon < _evolved.Length && _evolved[weapon];

        public Color Colour(int weapon)
        {
            switch (Of(weapon))
            {
                case Family.Wand: return Wand;
                case Family.Bible: return Bible;
                case Family.Ring: return Evolved(weapon) ? Thunder : Ring;
                case Family.Chain: return Chain;
                default: return Color.white;
            }
        }
    }
}
