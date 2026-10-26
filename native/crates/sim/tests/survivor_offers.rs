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
            Item::Weapon(WeaponId::Garlic),
            Item::Weapon(WeaponId::MagicWand),
            Item::Weapon(WeaponId::Sword)
        ]
    );
    let current = table.cards(2, i, weapon_catalog());
    assert_eq!(
        current.map(|c| c.item),
        [
            Item::Weapon(WeaponId::Sword),
            Item::Passive(PassiveId::Spinach),
            Item::Passive(PassiveId::HollowHeart)
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
fn first_column_meets_the_planned_level_milestones_with_all_weapons_built() {
    let mut inv = initial();
    let catalog = built_catalog();
    let mut heals = 0;
    for level in 2..=70 {
        let card = offer_table().cards(level, inv, &catalog)[0];
        if card.item == Item::Heal {
            heals += 1;
        } else {
            assert_eq!(card.before, card.item.rank(inv));
            take(&mut inv, card.item);
        }
        if [5, 10, 15, 18, 22, 28, 38, 48, 67, 70].contains(&level) {
            eprintln!("intended all-built path at level {level}: {inv:?}");
        }
        match level {
            5 => {
                assert!(inv.weapons.rank(WeaponId::Sword) >= Some(2));
                assert!(inv.weapons.rank(WeaponId::Garlic).is_some());
                assert!(inv.weapons.rank(WeaponId::MagicWand).is_some());
            }
            10 => {
                assert!(inv.passives.rank(PassiveId::Spinach).is_some());
                assert!(inv.passives.rank(PassiveId::HollowHeart).is_some());
            }
            15 => {
                assert!(inv.weapons.rank(WeaponId::KingBible).is_some());
                assert!(inv.passives.rank(PassiveId::Pummarola).is_some());
            }
            18 => assert_eq!(inv.weapons.rank(WeaponId::Sword), Some(8)),
            22 => {
                assert!(inv.weapons.rank(WeaponId::LightningRing).is_some());
                assert!(inv.passives.rank(PassiveId::EmptyTome).is_some());
            }
            28 => assert_eq!(inv.weapons.rank(WeaponId::Garlic), Some(8)),
            38 => {
                assert_eq!(inv.weapons.rank(WeaponId::MagicWand), Some(8));
                assert!(inv.passives.rank(PassiveId::Duplicator).is_some());
                assert!(inv.passives.rank(PassiveId::Spellbinder).is_some());
            }
            48 => {
                assert_eq!(inv.weapons.rank(WeaponId::KingBible), Some(8));
                assert_eq!(inv.weapons.rank(WeaponId::LightningRing), Some(8));
            }
            _ => {}
        }
    }
    assert_eq!(heals, 3); // no chest upgrades in this pure table simulation: full max at67
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
