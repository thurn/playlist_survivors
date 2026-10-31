using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>What a card gives: a weapon's rank, a passive's rank, or, with everything maxed, a heal.</summary>
    public enum CardItem { Weapon = 0, Passive = 1, Heal = 2 }

    /// <summary>One card of a level-up: the item (a content index for a weapon or passive) and its rank before and after.</summary>
    public struct OfferCard
    {
        public CardItem Item;
        /// <summary>The weapon's or passive's content index; -1 for the heal.</summary>
        public int Index;
        /// <summary>The rank owned now (0: new) and the rank the card gives (0 for the heal).</summary>
        public int Before, After;

        /// <summary>A card packed in 10 bits, for its event: the item code (0 heal, 1 + weapon, 32 + passive) and the rank before.</summary>
        internal int Packed => (Item == CardItem.Heal ? 0 : Item == CardItem.Weapon ? 1 + Index : 32 + Index) | (Before << 6);

        internal static OfferCard Unpack(int bits)
        {
            int code = bits & 63, before = (bits >> 6) & 15;
            if (code == 0) return new OfferCard { Item = CardItem.Heal, Index = -1 };
            return new OfferCard
            {
                Item = code >= 32 ? CardItem.Passive : CardItem.Weapon,
                Index = code >= 32 ? code - 32 : code - 1,
                Before = before,
                After = before + 1,
            };
        }

        public override string ToString() => Item == CardItem.Heal ? "heal" : $"{Item} {Index} {Before}->{After}";
    }

    /// <summary>
    /// A level-up's offer as the HUD reads it (<see cref="SimState.Offer"/>): the level it is for,
    /// its three cards, when each flips in (beats), the selected card and whether it is still open.
    /// </summary>
    public struct Offer
    {
        public bool Open;
        public int Level;
        /// <summary>Counts the run's offers from 0: the offer in its events.</summary>
        public int Serial;
        public OfferCard Card0, Card1, Card2;
        /// <summary>The song times the cards flip in; a card is confirmed only after the third.</summary>
        public double Reveal0, Reveal1, Reveal2;
        public int Selected;

        public OfferCard Card(int k) => k == 0 ? Card0 : k == 1 ? Card1 : k == 2 ? Card2 : throw new ArgumentOutOfRangeException(nameof(k));
        public double Reveal(int k) => k == 0 ? Reveal0 : k == 1 ? Reveal1 : k == 2 ? Reveal2 : throw new ArgumentOutOfRangeException(nameof(k));
    }

    /// <summary>
    /// The scripted offer table's cards (native's survivors/offers.rs, unity-game.md, Pickups,
    /// Level-ups and Chests). Each of a level's three preference lists shows its first entry still
    /// offerable and not already shown; "any", or a list with none, falls back to the lowest-rank
    /// owned weapon, then the lowest-rank owned passive (content order on ties), then the first
    /// offerable item in the table's "any" order. With nothing offerable a card repeats one, and with
    /// everything maxed it heals.
    ///
    /// A weapon is offerable when it is a base weapon whose evolution isn't owned, and it is owned
    /// below its top rank or a weapon slot is free; a passive when it is owned below its top rank or
    /// a passive slot is free.
    /// </summary>
    internal static class Offers
    {
        public static void Cards(GameContent content, Arsenal arsenal, int level, Span<OfferCard> into)
        {
            var row = content.Offers.Cards(level);
            Span<int> used = stackalloc int[3];
            for (int c = 0; c < 3; c++)
            {
                int item = -1;
                if (row != null)
                    foreach (string name in row[c])
                    {
                        if (name == OfferTable.Any) item = Fallback(content, arsenal, used.Slice(0, c));
                        else if (Offerable(content, arsenal, Code(content, name)) && !Used(used.Slice(0, c), Code(content, name)))
                            item = Code(content, name);
                        if (item >= 0) break;
                    }
                if (item < 0) item = Fallback(content, arsenal, used.Slice(0, c));
                if (item < 0) item = Fallback(content, arsenal, Span<int>.Empty);
                used[c] = item;
                into[c] = Card(content, arsenal, item);
            }
        }

        // Items as one code: a weapon's content index, or the weapon count plus a passive's.
        static int Code(GameContent content, string name)
        {
            int w = content.WeaponIndex(name);
            return w >= 0 ? w : content.Weapons.Length + content.PassiveIndex(name);
        }

        static bool Used(ReadOnlySpan<int> used, int item)
        {
            foreach (int u in used)
                if (u == item) return true;
            return false;
        }

        static bool Offerable(GameContent content, Arsenal arsenal, int item)
        {
            int weapons = content.Weapons.Length;
            if (item < weapons)
            {
                var def = content.Weapons[item];
                if (def.IsEvolution || arsenal.OwnsEvolutionOf(item)) return false;
                int rank = arsenal.WeaponRank(item);
                return rank > 0 ? rank < def.MaxRank : !arsenal.WeaponSlotsFull;
            }
            int p = item - weapons;
            int prank = arsenal.PassiveRank(p);
            return prank > 0 ? prank < content.Passives[p].MaxRank : !arsenal.PassiveSlotsFull;
        }

        /// <summary>native's fallback, skipping <paramref name="used"/>: -1 with nothing offerable.</summary>
        static int Fallback(GameContent content, Arsenal arsenal, ReadOnlySpan<int> used)
        {
            int weapons = content.Weapons.Length;
            int best = -1, bestRank = int.MaxValue;
            foreach (var slot in arsenal.Weapons)
                if (Offerable(content, arsenal, slot.Index) && !Used(used, slot.Index) && Better(slot.Rank, slot.Index, bestRank, best))
                {
                    best = slot.Index;
                    bestRank = slot.Rank;
                }
            if (best >= 0) return best;
            foreach (var slot in arsenal.Passives)
                if (Offerable(content, arsenal, weapons + slot.Index) && !Used(used, weapons + slot.Index)
                    && Better(slot.Rank, weapons + slot.Index, bestRank, best))
                {
                    best = weapons + slot.Index;
                    bestRank = slot.Rank;
                }
            if (best >= 0) return best;
            foreach (string name in content.Offers.AnyOrder)
            {
                int item = Code(content, name);
                if (Offerable(content, arsenal, item) && !Used(used, item)) return item;
            }
            return -1;
        }

        static bool Better(int rank, int item, int bestRank, int best) => best < 0 || rank < bestRank || (rank == bestRank && item < best);

        static OfferCard Card(GameContent content, Arsenal arsenal, int item)
        {
            if (item < 0) return new OfferCard { Item = CardItem.Heal, Index = -1 };
            int weapons = content.Weapons.Length;
            bool weapon = item < weapons;
            int index = weapon ? item : item - weapons;
            int before = weapon ? arsenal.WeaponRank(index) : arsenal.PassiveRank(index);
            return new OfferCard { Item = weapon ? CardItem.Weapon : CardItem.Passive, Index = index, Before = before, After = before + 1 };
        }
    }
}
