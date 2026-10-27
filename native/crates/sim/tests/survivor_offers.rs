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
        WeaponId::ChainLightning,
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
fn scripted_first_column_keeps_legal_growth_with_six_of_seven_weapon_slots() {
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
    assert_eq!(inv.weapons.iter().count(), 6);
    assert_eq!(
        heals, 0,
        "seven-choice pool still has a legal rank to offer within70 levels"
    );
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
fn eight_passive_cards_describe_real_deltas_without_duplicate_health_or_might() {
    use sim::survivors::Card;
    for (passive, cap, line) in [
        (PassiveId::Spinach, 5, "+10% DAMAGE"),
        (PassiveId::HollowHeart, 5, "+20% MAX HP; HEAL ADDED HP"),
        (PassiveId::Pummarola, 5, "+0.2 HP / SECOND"),
        (PassiveId::EmptyTome, 5, "-8% COOLDOWN"),
        (PassiveId::Spellbinder, 5, "+10% DURATION"),
        (PassiveId::Duplicator, 2, "+1 PROJECTILE / SPIN"),
        (PassiveId::Magnet, 5, "+30% PICKUP REACH"),
        (PassiveId::Armour, 5, "-1 CONTACT DAMAGE (MINIMUM 1)"),
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
    for retired in ["max-health", "might"] {
        assert_eq!(
            Item::parse(retired),
            None,
            "legacy passive must not enter Survivors offers"
        );
    }
}

/// Bounded search of actual offered cards. Every retained edge is a legal choice;
/// rank/capacity signatures deduplicate paths, and a beam bounds qualification cost.
fn legal_path(goals: &[(Item, u8)]) -> Option<(Inventory, Vec<(usize, usize)>)> {
    use sim::survivors::{BASE_WEAPONS, PASSIVES};
    let signature = |inv: Inventory| {
        let mut out = [0u8; 15];
        for (i, w) in BASE_WEAPONS.iter().enumerate() {
            out[i] = inv.weapons.rank(*w).unwrap_or(0);
        }
        for (i, p) in PASSIVES.iter().enumerate() {
            out[7 + i] = inv.passives.rank(*p).unwrap_or(0);
        }
        out
    };
    let score = |inv: Inventory| {
        let progress = goals
            .iter()
            .map(|(i, n)| u32::from(i.rank(inv).min(*n)) * if *n == 1 { 100 } else { 15 })
            .sum::<u32>();
        let extra = inv
            .weapons
            .iter()
            .filter(|s| !goals.iter().any(|(i, _)| *i == Item::Weapon(s.id)))
            .count()
            + inv
                .passives
                .iter()
                .filter(|s| !goals.iter().any(|(i, _)| *i == Item::Passive(s.id)))
                .count();
        (
            progress,
            std::cmp::Reverse(extra),
            signature(inv).iter().map(|n| u32::from(*n)).sum::<u32>(),
        )
    };
    let mut beam = vec![(initial(), Vec::new())];
    for level in 2..=70 {
        let mut seen = std::collections::BTreeSet::new();
        let mut next = Vec::new();
        for (inv, path) in beam {
            for (pick, card) in offer_table()
                .cards(level, inv, weapon_catalog())
                .into_iter()
                .enumerate()
            {
                let mut after = inv;
                take(&mut after, card.item);
                let mut chosen = path.clone();
                chosen.push((level, pick));
                if goals.iter().all(|(i, n)| i.rank(after) >= *n) {
                    return Some((after, chosen));
                }
                let blocked = goals.iter().any(|(i, _)| {
                    i.rank(after) == 0
                        && match i {
                            Item::Weapon(_) => after.weapons.is_full(),
                            Item::Passive(_) => after.passives.is_full(),
                            Item::Heal => false,
                        }
                });
                if !blocked && seen.insert(signature(after)) {
                    next.push((after, chosen));
                }
            }
        }
        next.sort_by_cached_key(|(i, _)| std::cmp::Reverse(score(*i)));
        next.truncate(1024);
        beam = next;
    }
    None
}

#[test]
fn every_base_and_evolution_has_a_legal_path_with_restored_alternatives() {
    use sim::survivors::{BASE_WEAPONS, chest_item};
    for id in BASE_WEAPONS {
        let goal = Item::Weapon(id);
        let (inv, path) = legal_path(&[(goal, weapon_catalog().get(id).max_rank())])
            .expect("base weapon remains reachable and upgradable");
        assert_eq!(goal.rank(inv), 8);
        eprintln!(
            "{id:?}: max rank by level{}, {} legal picks",
            path.last().unwrap().0,
            path.len()
        );
    }
    for evo in weapon_catalog()
        .weapons
        .values()
        .filter(|d| d.evolution_of.is_some())
    {
        let base = evo.evolution_of.unwrap();
        let partner = evo.requires.unwrap();
        let goals = [
            (Item::Weapon(base), 8),
            (Item::Passive(partner), 1),
            (Item::Weapon(WeaponId::ChainLightning), 1),
            (Item::Weapon(WeaponId::FireTrail), 1),
            (Item::Passive(PassiveId::Magnet), 1),
            (Item::Passive(PassiveId::Armour), 1),
        ];
        let (mut inv, path) =
            legal_path(&goals).expect("evolution and restored choices coexist on a legal path");
        let mut found = false;
        for _ in 0..5 {
            let item = chest_item(inv, weapon_catalog(), true);
            if item == Item::Weapon(evo.id) {
                found = true;
                break;
            }
            if let Item::Weapon(w) = item {
                if let Some(parent) = weapon_catalog().get(w).evolution_of {
                    inv.weapons.replace(parent, w).unwrap();
                } else {
                    inv.weapons
                        .upgrade(w, weapon_catalog().get(w).max_rank())
                        .unwrap();
                }
            }
        }
        assert!(found, "actual chest eligibility for {:?}", evo.id);
        eprintln!(
            "{:?} + both restored weapons and support passives: legal by level{}",
            evo.id,
            path.last().unwrap().0
        );
    }
    let (inv, path) = legal_path(
        &[
            WeaponId::Sword,
            WeaponId::Garlic,
            WeaponId::MagicWand,
            WeaponId::KingBible,
            WeaponId::ChainLightning,
            WeaponId::FireTrail,
        ]
        .map(|w| (Item::Weapon(w), 1)),
    )
    .unwrap();
    assert!(inv.weapons.is_full());
    assert_eq!(inv.weapons.rank(WeaponId::LightningRing), None);
    for level in 2..=70 {
        assert!(
            offer_table()
                .cards(level, inv, weapon_catalog())
                .iter()
                .all(|c| c.item != Item::Weapon(WeaponId::LightningRing))
        );
    }
    eprintln!(
        "Six-slot restored build: legal by level{}; seventh weapon is excluded",
        path.last().unwrap().0
    );
}
