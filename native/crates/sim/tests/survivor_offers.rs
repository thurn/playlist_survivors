//! Scripted data and inventory eligibility, independent of presentation timing.
#![allow(clippy::float_cmp)]
use sim::survivors::{Inventory, Item, PassiveId, WeaponId, offer_table, weapon_catalog};
fn built_catalog() -> sim::survivors::WeaponCatalog {
    let mut c = weapon_catalog().clone();
    for w in c.weapons.values_mut() {
        w.implemented = true;
    }
    c
}
fn initial() -> Inventory {
    let mut i = Inventory::default();
    i.weapons.upgrade(WeaponId::Sword, 8).unwrap();
    i
}
fn take(i: &mut Inventory, item: Item) {
    match item {
        Item::Weapon(w) => {
            i.weapons.upgrade(w, 8).unwrap();
        }
        Item::Passive(p) => {
            i.passives.upgrade(p, p.max_rank()).unwrap();
        }
        Item::Heal => {}
    }
}
#[test]
fn preferences_skip_unbuilt_and_keep_distinct_viable_choices() {
    let i = initial();
    let table = offer_table();
    let cards = table.cards(2, i, &built_catalog());
    assert_eq!(
        cards.map(|c| c.item),
        [
            Item::Weapon(WeaponId::MagicWand),
            Item::Weapon(WeaponId::Garlic),
            Item::Weapon(WeaponId::Sword)
        ]
    );
    let current = table.cards(2, i, weapon_catalog());
    assert_eq!(
        current.map(|c| c.item),
        [
            Item::Weapon(WeaponId::MagicWand),
            Item::Weapon(WeaponId::Garlic),
            Item::Weapon(WeaponId::Sword)
        ]
    );
}
#[test]
fn fallback_prefers_lowest_owned_rank_and_heals_only_when_no_item_is_offerable() {
    let mut i = initial();
    for _ in 1..8 {
        i.weapons.upgrade(WeaponId::Sword, 8).unwrap();
    }
    i.passives.upgrade(PassiveId::Spinach, 5).unwrap();
    i.passives.upgrade(PassiveId::Spinach, 5).unwrap();
    i.passives.upgrade(PassiveId::Pummarola, 5).unwrap();
    assert_eq!(
        offer_table().cards(71, i, weapon_catalog())[0].item,
        Item::Passive(PassiveId::Pummarola)
    );
    let c = built_catalog();
    for w in [
        WeaponId::Sword,
        WeaponId::Garlic,
        WeaponId::MagicWand,
        WeaponId::KingBible,
        WeaponId::LightningRing,
    ] {
        while i.weapons.rank(w).unwrap_or(0) < 8 {
            i.weapons.upgrade(w, 8).unwrap();
        }
    }
    for p in [
        PassiveId::Spinach,
        PassiveId::HollowHeart,
        PassiveId::Pummarola,
        PassiveId::Duplicator,
        PassiveId::EmptyTome,
        PassiveId::Spellbinder,
    ] {
        while i.passives.rank(p).unwrap_or(0) < p.max_rank() {
            i.passives.upgrade(p, p.max_rank()).unwrap();
        }
    }
    assert!(
        offer_table()
            .cards(70, i, &c)
            .iter()
            .all(|c| c.item == Item::Heal)
    );
}
#[test]
fn scripted_first_column_fills_the_source_slots_then_offers_healing() {
    let mut inv = initial();
    let catalog = built_catalog();
    let mut heals = 0;
    for level in 2..=70 {
        let card = offer_table().cards(level, inv, &catalog)[0];
        if card.item == Item::Heal {
            heals += 1;
        } else {
            assert_eq!(card.before, card.item.rank(inv));
            assert_eq!(card.after, card.before + 1);
            take(&mut inv, card.item);
        }
    }
    // Eligibility/capacity contract, not a level-at-time or chest pacing gate.
    for weapon in [
        WeaponId::Sword,
        WeaponId::Garlic,
        WeaponId::MagicWand,
        WeaponId::KingBible,
        WeaponId::LightningRing,
    ] {
        assert_eq!(
            inv.weapons.rank(weapon),
            Some(catalog.get(weapon).max_rank())
        );
    }
    for passive in [
        PassiveId::Spinach,
        PassiveId::HollowHeart,
        PassiveId::Pummarola,
        PassiveId::EmptyTome,
        PassiveId::Spellbinder,
        PassiveId::Duplicator,
    ] {
        assert_eq!(inv.passives.rank(passive), Some(passive.max_rank()));
    }
    assert_eq!(heals, 3); // No chest grants: all source ranks filled before the heal fallback.
}

#[test]
fn an_evolved_weapon_blocks_reoffering_its_base() {
    let mut inv = initial();
    inv.weapons
        .replace(WeaponId::Sword, WeaponId::BloodyTear)
        .unwrap();
    assert!(
        offer_table()
            .cards(7, inv, &built_catalog())
            .iter()
            .all(|c| c.item != Item::Weapon(WeaponId::Sword))
    );
}

#[test]
fn shipped_first_offer_contains_the_implemented_wand() {
    let cards = offer_table().cards(2, initial(), weapon_catalog());
    assert_eq!(cards[0].item, Item::Weapon(WeaponId::MagicWand));
    assert_eq!((cards[0].before, cards[0].after), (0, 1));
    assert!(
        cards
            .iter()
            .all(|c| !matches!(c.item,Item::Weapon(w) if !weapon_catalog().get(w).implemented))
    );
}

#[test]
fn the_six_passive_cards_describe_the_source_rank_and_reject_retired_ids() {
    use sim::survivors::Card;
    for (passive, cap, line) in [
        (PassiveId::Spinach, 5, "+10% DAMAGE"),
        (PassiveId::HollowHeart, 5, "+20% MAX HP; HEAL ADDED HP"),
        (PassiveId::Pummarola, 5, "+0.2 HP / SECOND"),
        (PassiveId::EmptyTome, 5, "-8% COOLDOWN"),
        (PassiveId::Spellbinder, 5, "+10% DURATION"),
        (PassiveId::Duplicator, 2, "+1 PROJECTILE / SPIN"),
    ] {
        let item = Item::Passive(passive);
        assert_eq!(Item::parse(passive.as_str()), Some(item));
        assert_eq!(passive.max_rank(), cap);
        for before in 0..cap {
            let card = Card {
                item,
                before,
                after: before + 1,
            };
            assert_eq!(card.description(), line);
        }
    }
    for retired in ["max-health", "magnet", "might", "armour"] {
        assert_eq!(
            Item::parse(retired),
            None,
            "legacy passive must not enter Survivors offers"
        );
    }
}
