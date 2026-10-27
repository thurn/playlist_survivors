//! A bounded summary of logical hit and inventory records, copied with every rollback view.
use sim::survivors::{Inventory, WeaponId};
use std::collections::BTreeMap;
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct WeaponStats {
    damage: f64,
    held: f64,
    since: Option<f64>,
}
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Recap {
    weapons: BTreeMap<WeaponId, WeaponStats>,
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Row {
    pub id: WeaponId,
    pub damage: f64,
    pub held_s: f64,
    pub dps: f64,
}
/// Completed metrics are captured at the causal fatal record, rather than a later display frame.
#[derive(Clone, Debug, PartialEq)]
pub struct Summary {
    pub elapsed: f64,
    pub level: usize,
    pub kills: usize,
    pub rows: Vec<Row>,
}
impl Summary {
    pub fn from_view(view: &crate::live::LiveView) -> Option<Self> {
        if let Some(summary) = &view.fatal_summary {
            return Some(summary.clone());
        }
        let run = view.run?;
        Some(Self {
            elapsed: run.elapsed,
            level: run.level,
            kills: view.kills,
            rows: view.recap.rows(run.elapsed),
        })
    }
}
impl Recap {
    /// Rank upgrades do not restart the timer; replacement closes only the old identity.
    pub fn inventory(&mut self, inventory: Inventory, run: f64) {
        for (id, stats) in &mut self.weapons {
            if inventory.weapons.rank(*id).is_none()
                && let Some(since) = stats.since.take()
            {
                stats.held += (run - since).max(0.0);
            }
        }
        for weapon in inventory.weapons.iter() {
            self.weapons
                .entry(weapon.id)
                .or_default()
                .since
                .get_or_insert(run);
        }
    }
    /// Actual applied damage includes criticals/overkill, but not presentation reactions or recycling.
    pub fn damage(&mut self, id: WeaponId, damage: f64) {
        if damage.is_finite() && damage > 0.0 {
            self.weapons.entry(id).or_default().damage += damage;
        }
    }
    pub fn rows(&self, run: f64) -> Vec<Row> {
        self.weapons
            .iter()
            .map(|(&id, stats)| {
                let held_s = stats.held + stats.since.map_or(0.0, |since| (run - since).max(0.0));
                Row {
                    id,
                    damage: stats.damage,
                    held_s,
                    dps: if held_s > 0.0 {
                        stats.damage / held_s
                    } else {
                        0.0
                    },
                }
            })
            .collect()
    }
}

#[cfg(test)]
mod tests;
