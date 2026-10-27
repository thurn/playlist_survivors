//! Scripted preferences and a bounded, deterministic choice screen.
use super::{Inventory, PassiveId, StatDelta, WeaponCatalog, WeaponId};
use serde::Deserialize;
use std::{collections::BTreeMap, sync::OnceLock};
pub const BASE_WEAPONS: [WeaponId; 7] = [
    WeaponId::Sword,
    WeaponId::Garlic,
    WeaponId::MagicWand,
    WeaponId::KingBible,
    WeaponId::LightningRing,
    WeaponId::ChainLightning,
    WeaponId::FireTrail,
];
pub const PASSIVES: [PassiveId; 8] = [
    PassiveId::Spinach,
    PassiveId::HollowHeart,
    PassiveId::Pummarola,
    PassiveId::Duplicator,
    PassiveId::EmptyTome,
    PassiveId::Spellbinder,
    PassiveId::Magnet,
    PassiveId::Armour,
];
#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize)]
pub enum Item {
    Weapon(WeaponId),
    Passive(PassiveId),
    Heal,
}
impl Item {
    pub fn parse(name: &str) -> Option<Self> {
        WeaponId::from_name(name).map(Self::Weapon).or_else(|| {
            PASSIVES
                .into_iter()
                .find(|p| p.as_str() == name)
                .map(Self::Passive)
        })
    }
    pub fn name(self) -> &'static str {
        match self {
            Self::Weapon(w) => match w {
                WeaponId::Sword => "SWORD SPIN",
                WeaponId::Garlic => "GARLIC",
                WeaponId::MagicWand => "MAGIC WAND",
                WeaponId::KingBible => "KING BIBLE",
                WeaponId::LightningRing => "LIGHTNING RING",
                WeaponId::BloodyTear => "BLOODY TEAR",
                WeaponId::SoulEater => "SOUL EATER",
                WeaponId::HolyWand => "HOLY WAND",
                WeaponId::UnholyVespers => "UNHOLY VESPERS",
                WeaponId::ThunderLoop => "THUNDER LOOP",
                WeaponId::ChainLightning => "CHAIN LIGHTNING",
                WeaponId::FireTrail => "FIRE TRAIL",
            },
            Self::Passive(p) => p.name(),
            Self::Heal => "HEAL",
        }
    }
    pub fn rank(self, inv: Inventory) -> u8 {
        match self {
            Self::Weapon(w) => inv.weapons.rank(w).unwrap_or(0),
            Self::Passive(p) => inv.passives.rank(p).unwrap_or(0),
            Self::Heal => 0,
        }
    }
    fn available(self, inv: Inventory, catalog: &WeaponCatalog) -> bool {
        match self {
            Self::Weapon(w) => {
                let def = catalog.get(w);
                def.implemented
                    && def.evolution_of.is_none()
                    && !inv
                        .weapons
                        .iter()
                        .any(|s| catalog.get(s.id).evolution_of == Some(w))
                    && inv
                        .weapons
                        .rank(w)
                        .map_or(!inv.weapons.is_full(), |r| r < def.max_rank())
            }
            Self::Passive(p) => inv
                .passives
                .rank(p)
                .map_or(!inv.passives.is_full(), |r| r < p.max_rank()),
            Self::Heal => false,
        }
    }
}
impl PassiveId {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Spinach => "spinach",
            Self::HollowHeart => "hollow-heart",
            Self::Pummarola => "pummarola",
            Self::Duplicator => "duplicator",
            Self::EmptyTome => "empty-tome",
            Self::Spellbinder => "spellbinder",
            Self::Magnet => "magnet",
            Self::Armour => "armour",
        }
    }
    pub fn name(self) -> &'static str {
        match self {
            Self::Spinach => "SPINACH",
            Self::HollowHeart => "HOLLOW HEART",
            Self::Pummarola => "PUMMAROLA",
            Self::Duplicator => "DUPLICATOR",
            Self::EmptyTome => "EMPTY TOME",
            Self::Spellbinder => "SPELLBINDER",
            Self::Magnet => "MAGNET",
            Self::Armour => "ARMOUR",
        }
    }
    pub fn delta(self) -> StatDelta {
        match self {
            Self::Spinach => StatDelta {
                might: 0.1,
                ..StatDelta::default()
            },
            Self::HollowHeart => StatDelta {
                max_hp: 0.2,
                ..StatDelta::default()
            },
            Self::Pummarola => StatDelta {
                recovery: 0.2,
                ..StatDelta::default()
            },
            Self::Duplicator => StatDelta {
                amount: 1.0,
                ..StatDelta::default()
            },
            Self::EmptyTome => StatDelta {
                cooldown: -0.08,
                ..StatDelta::default()
            },
            Self::Magnet => StatDelta {
                magnet: 0.3,
                ..StatDelta::default()
            },
            Self::Armour => StatDelta {
                armor: 1.0,
                ..StatDelta::default()
            },
            Self::Spellbinder => StatDelta {
                duration: 0.1,
                ..StatDelta::default()
            },
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Card {
    pub item: Item,
    pub before: u8,
    pub after: u8,
}
impl Card {
    fn new(item: Item, inv: Inventory) -> Self {
        let before = item.rank(inv);
        Self {
            item,
            before,
            after: if item == Item::Heal { 0 } else { before + 1 },
        }
    }
    pub fn description(self) -> &'static str {
        match self.item {
            Item::Weapon(id) => {
                if self.before == 0 {
                    return "NEW ATTACK";
                }
                let def = super::weapon_catalog().get(id);
                let before = def.level(self.before);
                let after = def.level(self.after);
                if after.amount > before.amount {
                    if id.is_sword() {
                        "+1 SPIN"
                    } else if id == WeaponId::ChainLightning {
                        "+1 CHAIN JUMP"
                    } else {
                        "+1 PROJECTILE"
                    }
                } else if after.power > before.power {
                    "MORE DAMAGE"
                } else if after.area > before.area {
                    "MORE ATTACK AREA"
                } else if after.interval_ms < before.interval_ms {
                    "FASTER ATTACKS"
                } else if after.duration_ms > before.duration_ms {
                    "LONGER DURATION"
                } else {
                    "WEAPON UPGRADE"
                }
            }
            Item::Passive(p) => match p {
                PassiveId::Spinach => "+10% DAMAGE",
                PassiveId::HollowHeart => "+20% MAX HP; HEAL ADDED HP",
                PassiveId::Pummarola => "+0.2 HP / SECOND",
                PassiveId::Duplicator => "+1 PROJECTILE / SPIN",
                PassiveId::EmptyTome => "-8% COOLDOWN",
                PassiveId::Spellbinder => "+10% DURATION",
                PassiveId::Magnet => "+30% PICKUP REACH",
                PassiveId::Armour => "-1 CONTACT DAMAGE (MINIMUM 1)",
            },
            Item::Heal => "RECOVER HP",
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Offer {
    pub level: usize,
    pub cards: [Card; 3],
    pub reveals: [f64; 3],
}
#[derive(Clone, Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct OfferTable {
    pub offers: BTreeMap<usize, [Vec<String>; 3]>,
    pub fallback: Vec<String>,
}
impl OfferTable {
    pub fn parse(yaml: &str) -> Result<Self, String> {
        let out: Self = serde_saphyr::from_str(yaml).map_err(|e| e.to_string())?;
        if out.fallback
            != [
                "lowest-level weapon owned",
                "lowest-level passive owned",
                "any offerable",
            ]
        {
            return Err("unsupported offer fallback".into());
        }
        if out.offers.len() != 69 || !(2..=70).all(|l| out.offers.contains_key(&l)) {
            return Err("offers must cover every level 2-70".into());
        }
        for lists in out.offers.values() {
            for list in lists {
                if list.is_empty() {
                    return Err("empty card preference".into());
                }
                for name in list {
                    if name != "any" && Item::parse(name).is_none() {
                        return Err(format!("unknown offer {name}"));
                    }
                }
            }
        }
        Ok(out)
    }
    pub fn cards(&self, level: usize, inv: Inventory, catalog: &WeaponCatalog) -> [Card; 3] {
        let all: Vec<_> = BASE_WEAPONS
            .into_iter()
            .map(Item::Weapon)
            .chain(PASSIVES.into_iter().map(Item::Passive))
            .filter(|i| i.available(inv, catalog))
            .collect();
        let fallback = |used: &[Item]| -> Option<Item> {
            inv.weapons
                .iter()
                .filter(|s| {
                    Item::Weapon(s.id).available(inv, catalog)
                        && !used.contains(&Item::Weapon(s.id))
                })
                .min_by_key(|s| (s.rank, s.id))
                .map(|s| Item::Weapon(s.id))
                .or_else(|| {
                    inv.passives
                        .iter()
                        .filter(|s| {
                            Item::Passive(s.id).available(inv, catalog)
                                && !used.contains(&Item::Passive(s.id))
                        })
                        .min_by_key(|s| (s.rank, s.id))
                        .map(|s| Item::Passive(s.id))
                })
                .or_else(|| all.iter().copied().find(|i| !used.contains(i)))
        };
        let mut used = Vec::new();
        std::array::from_fn(|i| {
            let item = self
                .offers
                .get(&level)
                .and_then(|row| {
                    row[i].iter().find_map(|name| {
                        if name == "any" {
                            fallback(&used)
                        } else {
                            Item::parse(name)
                                .filter(|x| x.available(inv, catalog) && !used.contains(x))
                        }
                    })
                })
                .or_else(|| fallback(&used))
                .or_else(|| fallback(&[]))
                .unwrap_or(Item::Heal);
            used.push(item);
            Card::new(item, inv)
        })
    }
}
pub fn offer_table() -> &'static OfferTable {
    static TABLE: OnceLock<OfferTable> = OnceLock::new();
    TABLE.get_or_init(|| {
        OfferTable::parse(include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../content/offers.yaml"
        )))
        .expect("validated offer table")
    })
}
