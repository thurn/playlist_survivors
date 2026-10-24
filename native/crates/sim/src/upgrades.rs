//! `src/sim/upgrades.ts`: the upgrade pool. What a level-up offers, the loadout the hero builds
//! from what it takes, and what that loadout changes.

use indexmap::IndexMap;

use crate::content::ContentLibrary;
use crate::content::types::WeaponDef;
use crate::core::rng::{Part, Rng};
use crate::jsmath::{floor, max, min};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum ItemKind {
    Weapon,
    Evolve,
    Passive,
}

impl ItemKind {
    pub fn name(self) -> &'static str {
        match self {
            ItemKind::Weapon => "weapon",
            ItemKind::Evolve => "evolve",
            ItemKind::Passive => "passive",
        }
    }
}

/// One offer: a new weapon, an owned weapon's evolution, or a passive.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct UpgradeItem {
    pub kind: ItemKind,
    pub id: String,
}

impl UpgradeItem {
    fn new(kind: ItemKind, id: &str) -> UpgradeItem {
        UpgradeItem {
            kind,
            id: id.to_string(),
        }
    }
}

/// A level-up's offers and the one taken (an index into them, -1 if none), at song time s, with
/// the upgrade orbs' record (`UpgradeRecord`).
#[derive(Clone, Debug, PartialEq)]
pub struct UpgradeRecord {
    pub s: f64,
    pub offers: Vec<UpgradeItem>,
    pub taken: i32,
    pub w: Option<f64>,
    pub orbs: Option<Vec<crate::player::Point>>,
    pub s_taken: Option<f64>,
    pub w_taken: Option<f64>,
    pub s_apply: Option<f64>,
    pub walked: Option<bool>,
}

/// What the hero has: its weapons beside the sword (in the order it got them), the evolved ones,
/// and passive counts (in the order first taken, as a JS object's keys).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Loadout {
    pub weapons: Vec<String>,
    pub evolved: Vec<String>,
    pub passives: IndexMap<String, f64>,
}

/// How many items a level-up offers.
pub const OFFERS: usize = 3;

/// The weapon upgrades still to take: the level's new weapons not owned, then the owned weapons'
/// evolutions.
pub fn weapon_upgrades(
    lib: &ContentLibrary,
    offer: &[String],
    owned: &Loadout,
) -> Vec<UpgradeItem> {
    let mut out: Vec<UpgradeItem> = offer
        .iter()
        .filter(|id| !owned.weapons.contains(id))
        .map(|id| UpgradeItem::new(ItemKind::Weapon, id))
        .collect();
    for id in &owned.weapons {
        if lib.weapons.get(id).is_some_and(|w| w.evolve.is_some()) && !owned.evolved.contains(id) {
            out.push(UpgradeItem::new(ItemKind::Evolve, id));
        }
    }
    out
}

/// A level-up's offers: three different items, at least one a weapon upgrade while any remain,
/// drawn from the run's seed and the level-up's index, in a shuffled order.
pub fn offers_for(
    seed: f64,
    index: f64,
    lib: &ContentLibrary,
    offer: &[String],
    owned: &Loadout,
) -> Vec<UpgradeItem> {
    let mut r = Rng::new(&[Part::Num(seed), Part::Str("offers"), Part::Num(index)]);
    let pick = |list: &mut Vec<UpgradeItem>, r: &mut Rng| {
        let i = floor(r.next() * list.len() as f64) as usize;
        list.remove(i)
    };
    let mut weapons = weapon_upgrades(lib, offer, owned);
    let mut picks: Vec<UpgradeItem> = Vec::new();
    if !weapons.is_empty() {
        picks.push(pick(&mut weapons, &mut r));
    }
    let mut rest: Vec<UpgradeItem> = weapons;
    rest.extend(
        lib.passives
            .keys()
            .map(|id| UpgradeItem::new(ItemKind::Passive, id)),
    );
    while picks.len() < OFFERS && !rest.is_empty() {
        picks.push(pick(&mut rest, &mut r));
    }
    for i in (1..picks.len()).rev() {
        let j = floor(r.next() * (i as f64 + 1.0)) as usize;
        picks.swap(i, j);
    }
    picks
}

/// A fresh loadout: the level's starting weapons, nothing evolved, no passives.
pub fn starting_loadout(loadout: &[String]) -> Loadout {
    Loadout {
        weapons: loadout.to_vec(),
        evolved: Vec::new(),
        passives: IndexMap::new(),
    }
}

/// Takes an item into a loadout.
pub fn take_item(l: &mut Loadout, item: &UpgradeItem) {
    match item.kind {
        ItemKind::Weapon => l.weapons.push(item.id.clone()),
        ItemKind::Evolve => l.evolved.push(item.id.clone()),
        ItemKind::Passive => *l.passives.entry(item.id.clone()).or_insert(0.0) += 1.0,
    }
}

/// The loadout at song time s: the starting weapons with every upgrade applied by then (an orb's on
/// its grid time).
pub fn loadout_at(loadout: &[String], upgrades: &[UpgradeRecord], s: f64) -> Loadout {
    let mut l = starting_loadout(loadout);
    for u in upgrades {
        if u.s_apply.unwrap_or(u.s) <= s && u.taken >= 0 {
            take_item(&mut l, &u.offers[u.taken as usize]);
        }
    }
    l
}

/// Upgrade orbs: the virtual players' fixed priority (below LOW_HEALTH, max health comes first).
pub const ORB_PRIORITY: [&str; 7] = [
    "evolve:orbiting-blades",
    "evolve:chain-lightning",
    "passive:might",
    "weapon:fire-trail",
    "passive:magnet",
    "passive:max-health",
    "passive:armour",
];
pub const LOW_HEALTH: f64 = 0.5;

/// The offer a virtual player chooses: the first by ORB_PRIORITY (max health first below
/// LOW_HEALTH), else the first offered. (`health` defaults to 1 in TS.)
pub fn preferred_offer(offers: &[UpgradeItem], health: f64) -> usize {
    let mut order: Vec<&str> = Vec::new();
    if health < LOW_HEALTH {
        order.push("passive:max-health");
        order.extend(ORB_PRIORITY.iter().filter(|x| **x != "passive:max-health"));
    } else {
        order.extend(ORB_PRIORITY);
    }
    let rank = |o: &UpgradeItem| {
        let key = format!("{}:{}", o.kind.name(), o.id);
        order.iter().position(|x| *x == key).unwrap_or(order.len())
    };
    let mut best = 0;
    for (i, o) in offers.iter().enumerate() {
        if rank(o) < rank(&offers[best]) {
            best = i;
        }
    }
    best
}

/// The passives' bonuses added up.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct PassiveBonus {
    pub max_health: f64,
    pub pickup: f64,
    pub damage: f64,
    pub armour: f64,
}

pub fn passive_bonus(lib: &ContentLibrary, passives: &IndexMap<String, f64>) -> PassiveBonus {
    let mut b = PassiveBonus::default();
    for (id, n) in passives {
        let Some(p) = lib.passives.get(id) else {
            continue;
        };
        b.max_health += p.max_health * n;
        b.pickup += p.pickup * n;
        b.damage += p.damage * n;
        b.armour += p.armour * n;
    }
    b
}

/// A bite after armour: armour less, but never below 1 (or below the bite itself, if that is less).
pub fn armoured_bite(amount: f64, armour: f64) -> f64 {
    max(min(1.0, amount), amount - armour)
}

/// Evolved blades: more blades and a wider orbit than the tier's.
pub fn evolved_orbit(
    wd: &WeaponDef,
    orbit: crate::content::types::OrbitTier,
    evolved: bool,
) -> crate::content::types::OrbitTier {
    match (&wd.evolve, evolved) {
        (Some(e), true) => crate::content::types::OrbitTier {
            count: orbit.count + e.count,
            radius: orbit.radius * e.radius_scale,
        },
        _ => orbit,
    }
}

/// An item's name, for labels: the weapon's, its evolution's or the passive's.
pub fn item_name(lib: &ContentLibrary, item: &UpgradeItem) -> String {
    if item.kind == ItemKind::Passive {
        return lib
            .passives
            .get(&item.id)
            .map_or_else(|| item.id.clone(), |p| p.name.clone());
    }
    let wd = lib.weapons.get(&item.id);
    let name = if item.kind == ItemKind::Evolve {
        wd.and_then(|w| w.evolve.as_ref()).map(|e| e.name.clone())
    } else {
        wd.map(|w| w.name.clone())
    };
    name.unwrap_or_else(|| item.id.clone())
}
