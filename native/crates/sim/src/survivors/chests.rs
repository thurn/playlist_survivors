//! Treasure rolls and rewards are simulation rules, independent of the presentation.
use super::{Inventory, Item, WeaponCatalog, WeaponId};
use crate::core::rng::Rng;
use serde::Serialize;

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct ChestLoot {
    pub tier: u8,
    pub minute: usize,
}
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct FieldChest {
    pub id: usize,
    pub x: f64,
    pub y: f64,
    pub tier: u8,
    pub minute: usize,
}
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub enum ChestEvent {
    Drop(FieldChest),
    Taken {
        id: usize,
    },
    Tick {
        id: usize,
        index: u8,
    },
    Landed {
        id: usize,
        rewards: [Option<Item>; 5],
    },
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Roulette {
    pub id: usize,
    pub tier: u8,
    pub minute: usize,
    pub opened: f64,
    /// Eight eighth-note intervals, starting on a bar, make two native two-beat bars.
    pub tick: u8,
    pub next_song: f64,
    pub next_beat: Option<f64>,
    pub started: bool,
    pub landed: Option<f64>,
    pub rewards: [Option<Item>; 5],
}
pub fn treasure_tier(chances: &[f64], scripted: Option<usize>, rng: &mut Rng) -> Option<u8> {
    if let Some(tier) = scripted {
        return Some(u8::try_from(tier).expect("validated tier"));
    }
    for (i, chance) in chances.iter().take(3).enumerate() {
        if rng.next() * 100.0 < *chance {
            return Some(3 - i as u8);
        }
    }
    None
}
pub fn chest_items(tier: u8) -> usize {
    match tier {
        1 => 1,
        2 => 3,
        3 => 5,
        _ => unreachable!("validated tier"),
    }
}
/// Chest fallback never introduces an unowned item. Evolutions use the expected path order.
pub fn chest_item(inv: Inventory, catalog: &WeaponCatalog, can_evolve: bool) -> Item {
    for base in [
        WeaponId::Sword,
        WeaponId::Garlic,
        WeaponId::MagicWand,
        WeaponId::KingBible,
        WeaponId::LightningRing,
    ] {
        if !can_evolve || inv.weapons.rank(base) != Some(catalog.get(base).max_rank()) {
            continue;
        }
        if let Some(evolved) = catalog.weapons.values().find(|d| {
            d.implemented
                && d.evolution_of == Some(base)
                && d.requires.is_some_and(|p| inv.passives.rank(p).is_some())
        }) {
            return Item::Weapon(evolved.id);
        }
    }
    inv.weapons
        .iter()
        .filter(|s| {
            let d = catalog.get(s.id);
            d.implemented && s.rank < d.max_rank()
        })
        .min_by_key(|s| (s.rank, s.id))
        .map(|s| Item::Weapon(s.id))
        .or_else(|| {
            inv.passives
                .iter()
                .filter(|s| s.rank < s.id.max_rank())
                .min_by_key(|s| (s.rank, s.id))
                .map(|s| Item::Passive(s.id))
        })
        .unwrap_or(Item::Heal)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        core::rng::Part,
        survivors::{PassiveId, weapon_catalog},
    };
    #[test]
    fn source_tiers_are_independent_ordered_rolls_and_scripts_consume_no_roll() {
        for seed in 0..200 {
            let mut actual = Rng::new(&[Part::Num(f64::from(seed)), Part::Str("treasure-source")]);
            let mut reference = actual.clone();
            let expected = if reference.next() * 100.0 < 3.0 {
                Some(3)
            } else if reference.next() * 100.0 < 10.0 {
                Some(2)
            } else if reference.next() * 100.0 < 50.0 {
                Some(1)
            } else {
                None
            };
            assert_eq!(
                treasure_tier(&[3.0, 10.0, 50.0], None, &mut actual),
                expected
            );
            assert_eq!(
                actual, reference,
                "three independent source rolls, short circuit on success"
            );
        }
        for tier in 1..=3 {
            let mut rng = Rng::new(&[Part::Str("scripted")]);
            let before = rng.clone();
            assert_eq!(
                treasure_tier(&[0.0, 0.0, 0.0], Some(tier), &mut rng),
                Some(tier as u8)
            );
            assert_eq!(rng, before);
            assert_eq!(chest_items(tier as u8), tier * 2 - 1);
        }
        let mut rng = Rng::new(&[Part::Str("zero")]);
        assert_eq!(treasure_tier(&[0.0; 3], None, &mut rng), None);
        assert_eq!(treasure_tier(&[0.0, 0.0, 100.0], None, &mut rng), Some(1));
    }
    #[test]
    fn items_evolve_then_level_owned_weapons_then_owned_passives_then_heal() {
        let mut inv = Inventory::default();
        let mut catalog = weapon_catalog().clone();
        // Exercise capability fallback independently of the shipped enabled set.
        for def in catalog
            .weapons
            .values_mut()
            .filter(|d| d.evolution_of.is_some())
        {
            def.implemented = false;
        }
        inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        inv.passives.upgrade(PassiveId::Spinach, 5).unwrap();
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Weapon(WeaponId::Sword)
        );
        for _ in 1..8 {
            inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        }
        // A max weapon's unimplemented evolution must fall back to levels, never be granted early.
        inv.passives.upgrade(PassiveId::HollowHeart, 5).unwrap();
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Passive(PassiveId::HollowHeart)
        );
        inv.weapons.upgrade(WeaponId::Garlic, 8).unwrap();
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Weapon(WeaponId::Garlic)
        );
        for _ in 1..8 {
            inv.weapons.upgrade(WeaponId::Garlic, 8).unwrap();
        }
        for p in [PassiveId::Spinach, PassiveId::HollowHeart] {
            while inv.passives.rank(p).unwrap() < p.max_rank() {
                inv.passives.upgrade(p, p.max_rank()).unwrap();
            }
        }
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Heal,
            "unowned items are never introduced"
        );
        // Capability seam: catalogue enablement is required as well as max rank+partner.
        catalog
            .weapons
            .get_mut(&WeaponId::BloodyTear)
            .unwrap()
            .implemented = true;
        assert_eq!(
            chest_item(inv, &catalog, false),
            Item::Heal,
            "early chests cannot evolve even a max weapon with its partner"
        );
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Weapon(WeaponId::BloodyTear)
        );
        inv.weapons
            .replace(WeaponId::Sword, WeaponId::BloodyTear)
            .unwrap();
        assert_eq!(
            chest_item(inv, &catalog, true),
            Item::Heal,
            "already evolved item cannot evolve again"
        );
    }
}
