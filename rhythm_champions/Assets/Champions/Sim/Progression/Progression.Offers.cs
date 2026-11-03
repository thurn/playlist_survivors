namespace Champions.Sim
{
    // The offers' cards (design.md, "Offers" and "Treasure"), drawn from the build as it stands.
    public sealed partial class Progression
    {
        // What the build can take: ranks of what it owns, and what it can add. One card per dash, attack or mod.
        readonly StructList<Card> grow = new StructList<Card>(32), fresh = new StructList<Card>(32);
        // Treasure's evolutions and raises.
        readonly StructList<Card> jumps = new StructList<Card>(8);

        /// <summary>Composes an offer of <paramref name="kind"/> into <see cref="offer"/> (none when the build can take nothing).</summary>
        void Compose(Simulation sim, OfferKind kind)
        {
            offer = new Offer { Kind = kind, Focus = -1 };
            if (!dashes.LegacyForms)
            {
                SurvivorCandidates(kind == OfferKind.Treasure);
                TakeAny(grow, null);
                while (offer.Count < Offer.Cards)
                {
                    int n = 0;
                    for (int i = 0; i < grow.Count; i++) if (!offer.Holds(grow[i])) n++;
                    int owned = n;
                    for (int i = 0; i < fresh.Count; i++) if (!offer.Holds(fresh[i])) n++;
                    if (n == 0) break;
                    TakeAny(random.Below(n) < owned ? grow : fresh, null);
                }
                SortByKind();
                if (offer.Count == 0) offer.Kind = OfferKind.None;
                return;
            }
            Candidates(sim);
            switch (kind)
            {
                case OfferKind.First:
                    // One new dash, one new attack, one new mod.
                    for (var k = CardKind.Dash; k <= CardKind.Mod; k++) TakeAny(fresh, k);
                    FillAny();
                    SortByKind();
                    break;
                case OfferKind.LevelUp:
                    // At least one card builds on something owned, when anything owned can rank up.
                    TakeAny(grow, null);
                    FillAny();
                    SortByKind();
                    break;
                case OfferKind.Treasure:
                    ComposeTreasure(sim);
                    break;
            }
            if (offer.Count == 0) offer.Kind = OfferKind.None;
        }

        void SurvivorCandidates(bool ownedOnly)
        {
            grow.Clear(); fresh.Clear();
            var build = state.Build;
            for (int i = 0; i < CombatRules.WeaponCount; i++)
            {
                var kind = (WeaponKind)i;
                int level = build.Level(kind);
                if (build.CanUpgrade(kind) && (!ownedOnly || level > 0))
                    (level > 0 ? grow : fresh).Add(Card.Of(kind, level + 1, level));
            }
            for (int i = 0; i < CombatRules.SupportCount; i++)
            {
                var kind = (SupportKind)i;
                int level = build.Level(kind);
                if (build.CanUpgrade(kind) && (!ownedOnly || level > 0))
                    (level > 0 ? grow : fresh).Add(Card.Of(kind, level + 1, level));
            }
        }

        /// <summary>
        /// The cards the build can take: a rank of the dash and of each attack and mod owned below its top, a damaging
        /// dash the hero doesn't have, and a new attack or mod while a slot is free.
        /// </summary>
        void Candidates(Simulation sim)
        {
            grow.Clear();
            fresh.Clear();
            var hero = sim.State.Hero;
            var dash = dashes.Kinds[hero.Dash];
            if (dashes.LegacyForms && hero.DashRank < dash.TopRank) grow.Add(Card.Dash(hero.Dash, hero.DashRank + 1, hero.DashRank));
            for (int k = 0; k < dashes.Kinds.Length; k++)
                if (dashes.LegacyForms && k != hero.Dash && dashes.Kinds[k].Form(1).Damaging) fresh.Add(Card.Dash(k, 1, 0));
            var arsenal = sim.Arsenal;
            bool attackSlot = arsenal.Attacks < Upgrade.AttackSlots, modSlot = arsenal.Mods < Upgrade.ModSlots;
            for (int a = 0; a < Upgrade.AttackCount; a++)
            {
                int r = arsenal.Rank((AttackKind)a);
                if (r == 0 && attackSlot) fresh.Add(Card.Of((AttackKind)a, 1, 0));
                else if (r > 0 && r < Upgrade.AttackTop) grow.Add(Card.Of((AttackKind)a, r + 1, r));
            }
            for (int m = 0; m < Upgrade.ModCount; m++)
            {
                int r = arsenal.Rank((ModKind)m);
                if (r == 0 && modSlot) fresh.Add(Card.Of((ModKind)m, 1, 0));
                else if (r > 0 && r < Upgrade.ModTop) grow.Add(Card.Of((ModKind)m, r + 1, r));
            }
        }

        /// <summary>
        /// The treasure: the evolution of every owned rank-3 attack and dash (three at random when four qualify), then
        /// "raise to rank 3" for the highest-ranked owned attacks and dash below it, then mod ranks and new mods, and
        /// past those anything a level-up could offer.
        /// </summary>
        void ComposeTreasure(Simulation sim)
        {
            var hero = sim.State.Hero;
            var dash = dashes.Kinds[hero.Dash];
            var arsenal = sim.Arsenal;
            jumps.Clear();
            for (int a = 0; a < Upgrade.AttackCount; a++)
                if (arsenal.Rank((AttackKind)a) == Upgrade.AttackTop) jumps.Add(Card.Of((AttackKind)a, Upgrade.Evolution, Upgrade.AttackTop));
            if (dashes.LegacyForms && dash.Evolution != null && hero.DashRank == dash.TopRank) jumps.Add(Card.Dash(hero.Dash, DashKind.EvolutionRank, hero.DashRank));
            while (TakeAny(jumps, null)) { }
            jumps.Clear();
            for (int a = 0; a < Upgrade.AttackCount; a++)
            {
                int r = arsenal.Rank((AttackKind)a);
                if (r > 0 && r < Upgrade.AttackTop) jumps.Add(Card.Of((AttackKind)a, Upgrade.AttackTop, r));
            }
            if (dashes.LegacyForms && hero.DashRank < dash.TopRank) jumps.Add(Card.Dash(hero.Dash, dash.TopRank, hero.DashRank));
            for (int from = Upgrade.AttackTop - 1; from >= 1; from--)
                while (TakeAny(jumps, null, from)) { }
            while (TakeAny(grow, CardKind.Mod)) { }
            while (TakeAny(fresh, CardKind.Mod)) { }
            FillAny();
        }

        /// <summary>Adds a card at random from <paramref name="from"/> (of <paramref name="kind"/>, raising from <paramref name="fromRank"/>, when given) that the offer can still take; false when there was none.</summary>
        bool TakeAny(StructList<Card> from, CardKind? kind, int fromRank = -1)
        {
            if (offer.Count >= Offer.Cards) return false;
            int n = 0;
            for (int i = 0; i < from.Count; i++)
                if (Takes(from[i], kind, fromRank)) n++;
            if (n == 0) return false;
            int pick = random.Below(n);
            for (int i = 0; i < from.Count; i++)
            {
                if (!Takes(from[i], kind, fromRank) || pick-- > 0) continue;
                offer.Set(offer.Count++, from[i]);
                return true;
            }
            return false;
        }

        bool Takes(in Card c, CardKind? kind, int fromRank) =>
            (kind == null || c.Kind == kind) && (fromRank < 0 || c.From == fromRank) && !offer.Holds(c);

        /// <summary>Fills the offer from everything the build can take: a kind at random among those left, then a card of it.</summary>
        void FillAny()
        {
            while (offer.Count < Offer.Cards)
            {
                int kinds = 0, chosen = -1;
                for (var k = CardKind.Dash; k <= CardKind.Mod; k++)
                    if (Left(k) && random.Below(++kinds) == 0) chosen = (int)k;
                if (chosen < 0) return;
                var kind = (CardKind)chosen;
                int n = Count(grow, kind) + Count(fresh, kind);
                int pick = random.Below(n);
                var list = pick < Count(grow, kind) ? grow : fresh;
                if (list == fresh) pick -= Count(grow, kind);
                for (int i = 0; i < list.Count; i++)
                {
                    if (!Takes(list[i], kind, -1) || pick-- > 0) continue;
                    offer.Set(offer.Count++, list[i]);
                    break;
                }
            }
        }

        bool Left(CardKind kind) => Count(grow, kind) + Count(fresh, kind) > 0;

        int Count(StructList<Card> list, CardKind kind)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
                if (Takes(list[i], kind, -1)) n++;
            return n;
        }

        /// <summary>Puts the cards in the order they are laid out: dashes, attacks, mods.</summary>
        void SortByKind()
        {
            for (int i = 1; i < offer.Count; i++)
                for (int j = i; j > 0 && Order(offer[j]) < Order(offer[j - 1]); j--)
                {
                    var c = offer[j];
                    offer.Set(j, offer[j - 1]);
                    offer.Set(j - 1, c);
                }
        }

        static int Order(in Card c) => (int)c.Kind << 8 | c.Id;
    }
}
