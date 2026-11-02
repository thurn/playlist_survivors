using System;

namespace Champions.Sim
{
    /// <summary>What a card gives (design.md, "Cards": its outline shape tells the kind).</summary>
    public enum CardKind : byte { Dash = 0, Attack = 1, Mod = 2 }

    /// <summary>Why cards are up (<see cref="Offer.Kind"/>).</summary>
    public enum OfferKind : byte
    {
        None = 0,
        /// <summary>The first offer, at the top of a run: one new dash, one new attack, one new mod.</summary>
        First = 1,
        /// <summary>A level-up's: what the build can take, at least one card building on something owned.</summary>
        LevelUp = 2,
        /// <summary>A boss's treasure: evolutions, then raises to rank 3, then mods.</summary>
        Treasure = 3,
    }

    /// <summary>
    /// One card (design.md, "Cards"): a dash, attack or mod at the rank it gives, and the rank it raises from (0:
    /// new). Attacks and dashes go to 3, and 4 is the evolution (<see cref="Upgrade.Evolution"/>,
    /// <see cref="DashKind.EvolutionRank"/>); mods go to 2.
    /// </summary>
    public struct Card : IEquatable<Card>
    {
        public CardKind Kind;
        /// <summary>The dash type (<see cref="DashRules.Kinds"/>), the <see cref="AttackKind"/> or the <see cref="ModKind"/>.</summary>
        public byte Id;
        public byte Rank;
        public byte From;

        public static Card Dash(int kind, int rank, int from) => new Card { Kind = CardKind.Dash, Id = (byte)kind, Rank = (byte)rank, From = (byte)from };
        public static Card Of(AttackKind kind, int rank, int from) => new Card { Kind = CardKind.Attack, Id = (byte)kind, Rank = (byte)rank, From = (byte)from };
        public static Card Of(ModKind kind, int rank, int from) => new Card { Kind = CardKind.Mod, Id = (byte)kind, Rank = (byte)rank, From = (byte)from };

        public AttackKind Attack => (AttackKind)Id;
        public ModKind Mod => (ModKind)Id;

        /// <summary>A dash, attack or mod the build doesn't hold.</summary>
        public bool New => From == 0;

        /// <summary>An attack's or dash's evolution (treasure's only).</summary>
        public bool Evolution => Rank == Upgrade.Evolution && Kind != CardKind.Mod;

        /// <summary>The card in one int, as events carry it: the kind | id &lt;&lt; 4 | rank &lt;&lt; 12 | from &lt;&lt; 16.</summary>
        public int Code => (int)Kind | Id << 4 | Rank << 12 | From << 16;

        public static Card FromCode(int code) => new Card
        {
            Kind = (CardKind)(code & 0xF), Id = (byte)(code >> 4), Rank = (byte)(code >> 12 & 0xF), From = (byte)(code >> 16 & 0xF),
        };

        /// <summary>Is it the same dash, attack or mod as <paramref name="other"/>, at any rank?</summary>
        public bool SameThing(in Card other) => Kind == other.Kind && Id == other.Id;

        public override string ToString()
        {
            string id = Kind == CardKind.Attack ? Upgrade.IdOf(Attack) : Kind == CardKind.Mod ? Upgrade.IdOf(Mod) : "dash#" + Id;
            return id + ":" + From + ">" + Rank;
        }

        public bool Equals(Card other) => Kind == other.Kind && Id == other.Id && Rank == other.Rank && From == other.From;
        public override bool Equals(object obj) => obj is Card c && Equals(c);
        public override int GetHashCode() => Code;
    }

    /// <summary>
    /// The cards up (design.md, "The card moment"): up to three different cards, and the one the arrows have
    /// focused for Enter or Space (-1: none yet). A plain value, copied into the snapshot.
    /// </summary>
    public struct Offer
    {
        public const int Cards = 3;

        public OfferKind Kind;
        public int Count;
        public Card Card0, Card1, Card2;
        public int Focus;

        /// <summary>Are cards up?</summary>
        public bool Up => Count > 0;

        public Card this[int i] => i == 0 ? Card0 : i == 1 ? Card1 : i == 2 ? Card2 : throw new ArgumentOutOfRangeException(nameof(i));

        internal void Set(int i, in Card card)
        {
            if (i == 0) Card0 = card;
            else if (i == 1) Card1 = card;
            else if (i == 2) Card2 = card;
            else throw new ArgumentOutOfRangeException(nameof(i));
        }

        /// <summary>Card <paramref name="i"/>'s code, or -1 past the count.</summary>
        public int CodeOf(int i) => i < Count ? this[i].Code : -1;

        /// <summary>Holds a card for the same dash, attack or mod as <paramref name="card"/>?</summary>
        internal bool Holds(in Card card)
        {
            for (int i = 0; i < Count; i++) if (this[i].SameThing(card)) return true;
            return false;
        }
    }
}
