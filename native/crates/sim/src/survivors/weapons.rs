//! Curated VS level deltas, loaded independently of the frozen prototype content.
/// Starting spin radius; SH3 tunes personal space, independently of VS damage.
pub const SWORD_BASE_RADIUS: f64 = 48.0;

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::{collections::BTreeMap, sync::OnceLock};

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum WeaponId {
    Sword,
    BloodyTear,
    Garlic,
    SoulEater,
    MagicWand,
    HolyWand,
    KingBible,
    UnholyVespers,
    LightningRing,
    ThunderLoop,
}

impl WeaponId {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Sword => "sword",
            Self::BloodyTear => "bloody-tear",
            Self::Garlic => "garlic",
            Self::SoulEater => "soul-eater",
            Self::MagicWand => "magic-wand",
            Self::HolyWand => "holy-wand",
            Self::KingBible => "king-bible",
            Self::UnholyVespers => "unholy-vespers",
            Self::LightningRing => "lightning-ring",
            Self::ThunderLoop => "thunder-loop",
        }
    }
    pub fn from_name(name: &str) -> Option<Self> {
        Some(match name {
            "sword" => Self::Sword,
            "bloody-tear" => Self::BloodyTear,
            "garlic" => Self::Garlic,
            "soul-eater" => Self::SoulEater,
            "magic-wand" => Self::MagicWand,
            "holy-wand" => Self::HolyWand,
            "king-bible" => Self::KingBible,
            "unholy-vespers" => Self::UnholyVespers,
            "lightning-ring" => Self::LightningRing,
            "thunder-loop" => Self::ThunderLoop,
            _ => return None,
        })
    }
    pub fn is_sword(self) -> bool {
        matches!(self, Self::Sword | Self::BloodyTear)
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum PassiveId {
    HollowHeart,
    EmptyTome,
    Spellbinder,
    Duplicator,
    Pummarola,
    Spinach,
}
impl PassiveId {
    pub fn max_rank(self) -> u8 {
        if self == Self::Duplicator { 2 } else { 5 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum FireGrid {
    Eighth,
    Beat,
}
impl FireGrid {
    pub fn beats(self) -> f64 {
        if self == Self::Eighth { 0.5 } else { 1.0 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WeaponStats {
    pub power: f64,
    pub amount: f64,
    pub interval_ms: f64,
    pub repeat_ms: f64,
    pub area: f64,
    pub speed: f64,
    pub duration_ms: f64,
    pub pierce: f64,
    pub hit_delay_ms: f64,
    pub knockback: f64,
    pub crit_chance: f64,
    pub crit_multiplier: f64,
    pub crit_enabled: bool,
    pub interval_depends_on_duration: bool,
}
impl WeaponStats {
    pub fn interval(self, stats: super::Stats) -> f64 {
        let stats = stats.finalized();
        self.interval_ms / 1000.0 * stats.cooldown
            + if self.interval_depends_on_duration {
                self.duration_ms / 1000.0 * stats.duration
            } else {
                0.0
            }
    }
    pub fn amount(self, stats: super::Stats) -> usize {
        (self.amount + stats.finalized().amount).max(1.0).floor() as usize
    }
}

#[derive(Clone, Debug)]
pub struct WeaponDef {
    pub id: WeaponId,
    pub source: String,
    pub grid: FireGrid,
    pub implemented: bool,
    pub evolution_of: Option<WeaponId>,
    pub requires: Option<PassiveId>,
    levels: Vec<WeaponStats>,
}
impl WeaponDef {
    pub fn max_rank(&self) -> u8 {
        u8::try_from(self.levels.len()).expect("validated rank count")
    }
    pub fn level(&self, rank: u8) -> WeaponStats {
        self.levels[usize::from(rank.saturating_sub(1)).min(self.levels.len() - 1)]
    }
}

#[derive(Clone, Debug)]
pub struct WeaponCatalog {
    pub weapons: BTreeMap<WeaponId, WeaponDef>,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RawWeapon {
    id: WeaponId,
    source: String,
    grid: FireGrid,
    implemented: bool,
    evolution_of: Option<WeaponId>,
    requires: Option<PassiveId>,
    levels: Vec<Map<String, Value>>,
}
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct RawCatalog {
    weapons: Vec<RawWeapon>,
}

impl WeaponCatalog {
    pub fn parse(text: &str) -> Result<Self, String> {
        let raw: RawCatalog = serde_saphyr::from_str(text).map_err(|e| e.to_string())?;
        let mut weapons = BTreeMap::new();
        for def in raw.weapons {
            if def.levels.is_empty() || def.levels.len() > 8 {
                return Err(format!("{}: invalid rank count", def.id.as_str()));
            }
            let mut value = Map::new();
            let mut levels = Vec::new();
            for (i, patch) in def.levels.into_iter().enumerate() {
                for (key, field) in patch {
                    let field = if let Some(delta) = field.as_f64() {
                        serde_json::json!(
                            value.get(&key).and_then(Value::as_f64).unwrap_or(0.0) + delta
                        )
                    } else {
                        field
                    };
                    value.insert(key, field);
                }
                let stats: WeaponStats = serde_json::from_value(Value::Object(value.clone()))
                    .map_err(|e| format!("{} rank{}: {e}", def.id.as_str(), i + 1))?;
                if stats.power <= 0.0
                    || stats.amount < 1.0
                    || stats.amount.fract() != 0.0
                    || stats.interval_ms <= 0.0
                    || stats.area <= 0.0
                    || stats.speed < 0.0
                    || stats.duration_ms < 0.0
                    || stats.knockback < 0.0
                {
                    return Err(format!(
                        "{} rank{}: invalid combat numbers",
                        def.id.as_str(),
                        i + 1
                    ));
                }
                levels.push(stats);
            }
            let id = def.id;
            if weapons
                .insert(
                    id,
                    WeaponDef {
                        id,
                        source: def.source,
                        grid: def.grid,
                        implemented: def.implemented,
                        evolution_of: def.evolution_of,
                        requires: def.requires,
                        levels,
                    },
                )
                .is_some()
            {
                return Err(format!("duplicate {}", id.as_str()));
            }
        }
        for def in weapons.values() {
            if let Some(parent) = def.evolution_of
                && (parent == def.id
                    || !weapons.contains_key(&parent)
                    || def.requires.is_none()
                    || weapons
                        .get(&parent)
                        .is_some_and(|p| p.evolution_of.is_some()))
            {
                return Err(format!("{}: invalid evolution", def.id.as_str()));
            }
        }
        Ok(Self { weapons })
    }
    pub fn get(&self, id: WeaponId) -> &WeaponDef {
        &self.weapons[&id]
    }
}

pub fn weapon_catalog() -> &'static WeaponCatalog {
    static DATA: OnceLock<WeaponCatalog> = OnceLock::new();
    DATA.get_or_init(|| {
        WeaponCatalog::parse(include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../content/survivors/weapons.yaml"
        )))
        .expect("validated survivors weapon tables")
    })
}

/// A causal sample of one travelling projectile; `alive=false` removes its view.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MissilePose {
    pub id: usize,
    pub weapon: WeaponId,
    pub w: f64,
    pub x: f64,
    pub y: f64,
    pub vx: f64,
    pub vy: f64,
    pub radius: f64,
    pub accent: bool,
    pub alive: bool,
}
