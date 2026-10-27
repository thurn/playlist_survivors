//! Retire data only once its final sample has been emitted. Public IDs never change.
use super::{Ev, Sim, Then};
use std::collections::HashSet;

impl Sim {
    pub(super) fn trim_state(&mut self) {
        // Frozen/legacy set pieces have their own recorded references and are unchanged.
        if self.st.survivor.is_none() {
            return;
        }
        let mut foes: HashSet<usize> = self.st.foe_samples.iter().copied().collect();
        foes.extend(self.st.boss_foe);
        foes.extend(self.st.formation_members.iter().flatten().copied());
        foes.extend(
            self.st
                .ring_members
                .iter()
                .flat_map(|(_, _, members)| members.iter().copied()),
        );
        self.st.foes.retain(|id, f| f.alive || foes.contains(&id));
        let mut swings: HashSet<usize> = self
            .st
            .swings
            .iter()
            .chain(&self.st.live_swings)
            .copied()
            .collect();
        let mut waves = HashSet::new();
        let mut chains = HashSet::new();
        for ev in self
            .st
            .q
            .heap
            .iter()
            .map(|e| &e.ev)
            .chain(self.st.deferred.iter().filter_map(|e| match &e.then {
                Then::Push(ev) => Some(ev),
                _ => None,
            }))
        {
            match ev {
                Ev::Wind(id) | Ev::Strike(id) => {
                    swings.insert(*id);
                }
                Ev::Wave { group, .. } => {
                    waves.insert(*group);
                }
                Ev::Chain { chain, .. } => {
                    chains.insert(*chain);
                }
                _ => {}
            }
        }
        self.st.swing_pool.retain(|id, _| swings.contains(&id));
        self.st.waves.retain(|id, _| waves.contains(&id));
        self.st.chains.retain(|id, _| chains.contains(&id));
        let w = self.st.w;
        self.st
            .punches
            .retain(|p| p.w.is_none_or(|at| w - at <= 1.2));
    }
}

/// Counts of retained simulation state. `issued_foes` is a monotonic identity counter,
/// not retained storage. Shared history is one copy plus changed pages across snapshots.
#[derive(Clone, Debug, serde::Serialize)]
pub struct Storage {
    pub alive: usize,
    pub foes: usize,
    pub issued_foes: usize,
    pub swings: usize,
    pub waves: usize,
    pub chains: usize,
    pub foe_samples: usize,
    pub punches: usize,
    pub gems: usize,
    pub beats: usize,
    pub ticks: usize,
    pub page_index_bytes: usize,
    pub separation_cache_bytes: usize,
    pub separation_hits: u64,
    pub separation_misses: u64,
}
impl Sim {
    pub fn storage(&self) -> Storage {
        Storage {
            alive: self
                .st
                .alive
                .iter()
                .filter(|&&id| self.st.foes[id].alive)
                .count(),
            foes: self.st.foes.stored(),
            issued_foes: self.st.foes.len(),
            swings: self.st.swing_pool.stored(),
            waves: self.st.waves.stored(),
            chains: self.st.chains.stored(),
            foe_samples: self.st.foe_samples.len(),
            punches: self.st.punches.len(),
            gems: self.st.field.len(),
            beats: self.st.song.beats.len(),
            ticks: self.st.judging.len(),
            separation_cache_bytes: self.spatial.cache.bytes(),
            separation_hits: self.spatial.cache.hits,
            separation_misses: self.spatial.cache.misses,
            page_index_bytes: self.st.song.beats.index_bytes()
                + self.st.song.energy.index_bytes()
                + self.st.sch.song.beats.index_bytes()
                + self.st.sch.song.energy.index_bytes()
                + self.st.sch.grid_times.index_bytes()
                + self.st.sch.grid_slots.index_bytes()
                + self.st.sch.stride_steps.s.index_bytes()
                + self.st.sch.stride_steps.period.index_bytes()
                + self.st.sch.stride_steps.per.index_bytes()
                + self.st.breaks.index_bytes()
                + self.st.forecasts.index_bytes()
                + self.st.strong_onsets.index_bytes()
                + self.st.judging.index_bytes(),
        }
    }
}

impl Sim {
    /// Final-effects density fixture, configured before the first rollback snapshot.
    pub fn seed_evolved_scale_check(&mut self) {
        self.seed_scale_check();
        for id in [
            crate::survivors::WeaponId::BloodyTear,
            crate::survivors::WeaponId::SoulEater,
            crate::survivors::WeaponId::HolyWand,
            crate::survivors::WeaponId::UnholyVespers,
            crate::survivors::WeaponId::ThunderLoop,
        ] {
            self.evolve_survivor_weapon(id).unwrap();
        }
    }

    /// Offline density fixture only, before Engine takes its first snapshot. Zero-XP gems
    /// exercise all 400 field entries without creating upgrade screens in the stationary soak.
    pub fn seed_scale_check(&mut self) {
        assert_eq!(self.st.s, 0.0);
        assert!(self.st.survivor.is_some());
        self.disable_hero_damage_for_check();
        // Max the real implemented loadout, including H weapons that landed while F's
        // soak was running. Future weapon beads automatically join this measurement.
        for def in crate::survivors::weapon_catalog()
            .weapons
            .values()
            .filter(|d| d.implemented && d.evolution_of.is_none())
        {
            while self
                .survivor_inventory()
                .unwrap()
                .weapons
                .rank(def.id)
                .unwrap_or(0)
                < def.max_rank()
            {
                self.learn_survivor_weapon(def.id).unwrap();
            }
        }
        for passive in [
            crate::survivors::PassiveId::Spinach,
            crate::survivors::PassiveId::HollowHeart,
            crate::survivors::PassiveId::Pummarola,
            crate::survivors::PassiveId::Duplicator,
            crate::survivors::PassiveId::EmptyTome,
            crate::survivors::PassiveId::Spellbinder,
        ] {
            for _ in 0..passive.max_rank() {
                self.st
                    .survivor
                    .as_mut()
                    .unwrap()
                    .arsenal
                    .learn_passive(passive)
                    .unwrap();
                self.apply_survivor_stats(passive.delta());
            }
        }
        self.publish_inventory();
        for i in 0..400 {
            self.drop_on_field(
                i,
                crate::recorder::Fate {
                    critical: false,
                    w: 0.0,
                    x: if i % 20 < 10 {
                        -150.0 - (i % 10) as f64 * 15.0
                    } else {
                        150.0 + (i % 10) as f64 * 15.0
                    },
                    y: (i / 20) as f64 * 22.0 - 210.0,
                    hit: 0.0,
                    lethal: true,
                    damage: 0.0,
                },
                0.0,
                false,
            );
        }
    }
}
