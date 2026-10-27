//! Survivor weapons own run-clock deadlines; song time is only a presentation grid.
use super::Sim;
use super::combat::HitSpec;
use crate::core::geometry::Shape;
use crate::recorder::{HitSource, Weight};
use crate::survivors::{Inventory, Shot, SlotError, WeaponId, weapon_catalog};

/// One persistent aura incarnation: a target can enter at any time, but is hit
/// only once until the next interval-driven incarnation (VS GarlicProjectile).
#[derive(Clone, Debug)]
pub(super) struct GarlicAura {
    shot: Shot,
    hit: std::collections::HashSet<usize>,
}

impl Sim {
    pub(super) fn weapons_active(&self) -> bool {
        self.st.survivor.is_some()
            && !self.st.freeze.frozen
            && !self.st.blasting
            && self
                .env
                .input
                .level
                .survivors
                .is_some_and(|s| self.st.run_s < s.duration_s)
    }
    pub(super) fn weapon_accent(&self) -> bool {
        self.log.forecasts.as_ref().is_some_and(|forecasts| {
            self.st.forecasts.iter().any(|&i| {
                let f = forecasts[i];
                f.known_at <= self.st.s + 1e-9 && (f.t - self.st.s).abs() <= 1e-3
            })
        })
    }
    pub(super) fn weapon_stop(&self) -> f64 {
        if !self.weapons_active() {
            return f64::INFINITY;
        }
        self.st
            .survivor
            .as_ref()
            .expect("survivors")
            .arsenal
            .next_stop(self.st.run_s, self.st.s)
    }
    pub(super) fn prepare_weapons(&mut self) {
        if !self.weapons_active() {
            return;
        }
        let p = self.st.survivor.as_mut().expect("survivors");
        p.arsenal.prepare(
            self.st.run_s,
            self.st.s,
            &self.log.beats[..self.st.ingested.beats],
            p.stats,
        );
    }
    pub(super) fn sword_beat(&mut self, bi: usize) {
        if !self.weapons_active() {
            return;
        }
        let beat = self.log.beats[bi];
        let period = self.log.beats[..self.st.ingested.beats]
            .get(bi + 1)
            .filter(|b| b.known_at <= self.st.s + 1e-9)
            .map_or(beat.period, |b| b.t - beat.t);
        let p = self.st.survivor.as_mut().expect("survivors");
        p.arsenal
            .sword_pulse(self.st.run_s, self.st.s, Some(bi as f64), period, p.stats);
    }
    pub(super) fn sword_free_pulse(&mut self) {
        if !self.weapons_active() {
            return;
        }
        let p = self.st.survivor.as_mut().expect("survivors");
        p.arsenal.sword_pulse(
            self.st.run_s,
            self.st.s,
            None,
            self.env.rule.free_period_s,
            p.stats,
        );
    }
    pub(super) fn tick_weapons(&mut self) {
        if !self.weapons_active() {
            if self.st.blasting {
                self.clear_missiles();
                self.clear_bibles();
            }
            return;
        }
        self.prepare_weapons();
        let due = self
            .st
            .survivor
            .as_mut()
            .expect("survivors")
            .arsenal
            .take_due(self.st.s);
        for (id, ready_run, period) in due {
            let p = self.st.survivor.as_ref().expect("survivors");
            let rank = p
                .arsenal
                .inventory()
                .weapons
                .rank(id)
                .expect("owned weapon");
            let stats = p.stats.finalized();
            let weapon = weapon_catalog().get(id).level(rank);
            let hp = self.hero_pose(self.st.w);
            let accent = if id.is_sword() {
                self.st
                    .live_swings
                    .iter()
                    .map(|&i| &self.st.swing_pool[i])
                    .find(|sw| (sw.s_strike - self.st.s).abs() < 1e-8)
                    .map_or_else(
                        || self.weapon_accent() && self.clear_at(self.st.s),
                        |sw| sw.heavy,
                    )
            } else {
                self.weapon_accent()
            };
            let hero = &self.env.input.lib.hero;
            let heavy_damage = if id.is_sword() && accent {
                hero.swings.heavy.damage / hero.swings.light.damage.max(1e-9)
            } else {
                1.0
            };
            let period = if matches!(id, WeaponId::KingBible | WeaponId::UnholyVespers) {
                self.log.beats[..self.st.ingested.beats]
                    .iter()
                    .rfind(|b| b.t <= self.st.s + 1e-9)
                    .map_or(0.0, |b| b.period)
            } else {
                period
            };
            let attack = if id.is_sword() {
                self.survivor_sword_shot()
            } else {
                None
            };
            let tier = attack.map_or(self.st.tier, |a| a.2);
            let tier_mult = if self.env.input.zen {
                1.0
            } else {
                crate::tiers::tier_damage(&self.env.input.lib.hero, tier)
            };
            let shot = Shot {
                attack: attack.map(|a| a.0),
                dir: attack.map_or(0.0, |a| a.1),
                id,
                rank,
                ready_run,
                song: self.st.s,
                x: hp.x,
                y: hp.y,
                damage: (weapon.power * stats.might
                    + if id == WeaponId::SoulEater {
                        (self.st.soul_recovered / 600.0).min(6.0)
                    } else {
                        0.0
                    })
                    * heavy_damage
                    * tier_mult,
                amount: if id.is_sword() || matches!(id, WeaponId::MagicWand | WeaponId::HolyWand) {
                    1
                } else {
                    weapon.amount(stats)
                },
                radius: if id.is_sword() {
                    if accent {
                        self.heavy_tier_reach(tier).radius
                    } else {
                        self.tier_reach(tier).radius
                    }
                } else if matches!(id, WeaponId::MagicWand | WeaponId::HolyWand) {
                    0.08 * crate::survivors::PX_PER_VS_UNIT * weapon.area * stats.area
                } else if matches!(id, WeaponId::Garlic | WeaponId::SoulEater) {
                    0.32 * crate::survivors::PX_PER_VS_UNIT * weapon.area * stats.area
                } else if matches!(id, WeaponId::LightningRing | WeaponId::ThunderLoop) {
                    0.08 * crate::survivors::PX_PER_VS_UNIT * weapon.area * stats.area
                } else if matches!(id, WeaponId::KingBible | WeaponId::UnholyVespers) {
                    0.5 * (weapon.area * stats.area).min(4.5) * crate::survivors::PX_PER_VS_UNIT
                } else {
                    0.0
                },
                area: weapon.area * stats.area,
                speed: weapon.speed * stats.projectile_speed,
                duration_s: weapon.duration_ms / 1000.0 * stats.duration,
                pierce: weapon.pierce,
                hit_delay_s: weapon.hit_delay_ms / 1000.0,
                knockback: weapon.knockback,
                crit_chance: if weapon.crit_enabled {
                    weapon.crit_chance * stats.luck
                } else {
                    0.0
                },
                crit_multiplier: weapon.crit_multiplier,
                period,
                accent,
            };
            self.rec.fire(id.as_str(), self.st.s);
            self.rec.weapon_shot(shot);
            if matches!(id, WeaponId::MagicWand | WeaponId::HolyWand) {
                self.launch_missile(shot);
                for i in 1..weapon.amount(stats) {
                    // Source Weapon.PSpeedRepeatInterval uses the final projectile speed.
                    let offset = i as f64 * weapon.repeat_ms / 1000.0 / shot.speed.max(0.001);
                    self.st.q.push(
                        self.st.s + offset,
                        super::Ev::WandBullet(Box::new(Shot {
                            ready_run: ready_run + offset,
                            ..shot
                        })),
                    );
                }
            }
            if matches!(id, WeaponId::LightningRing | WeaponId::ThunderLoop) {
                self.launch_lightning(shot);
            }
            if matches!(id, WeaponId::KingBible | WeaponId::UnholyVespers) {
                self.launch_bible(shot);
            }
            if matches!(id, WeaponId::Garlic | WeaponId::SoulEater) {
                self.st.garlic = Some(GarlicAura {
                    shot,
                    hit: std::collections::HashSet::new(),
                });
            }
            // Later weapon beads implement their delivery on the shared shot schedule.
        }
        self.tick_garlic();
        self.tick_missiles();
        self.tick_bibles();
    }
    fn tick_garlic(&mut self) {
        let Some(mut aura) = self.st.garlic.take() else {
            return;
        };
        let hp = self.hero_pose(self.st.w);
        let shape = Shape::Circle {
            x: hp.x,
            y: hp.y,
            radius: aura.shot.radius,
        };
        let targets: Vec<_> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&fi| {
                if aura.hit.contains(&fi) || !self.hittable(fi) {
                    return false;
                }
                let (x, y) = self.pos(fi, self.st.w);
                crate::aim::catches(&shape, x, y, self.st.foes[fi].r)
            })
            .collect();
        if !targets.is_empty() {
            let hit = self.add_hit(
                HitSpec {
                    source: HitSource::Weapon,
                    what: aura.shot.id.as_str().into(),
                    weight: if aura.shot.accent {
                        Weight::Heavy
                    } else {
                        Weight::Light
                    },
                    primary: true,
                    shape,
                    dir: 0.0,
                    accent: f64::from(aura.shot.accent),
                    damage: aura.shot.damage,
                    group: None,
                },
                None,
            );
            for fi in targets {
                aura.hit.insert(fi);
                self.damage(fi, &hit);
            }
            let foes = &self.st.foes;
            self.st.alive.retain(|&fi| foes[fi].alive);
        }
        self.st.garlic = Some(aura);
    }
    pub(super) fn publish_inventory(&mut self) {
        if let Some(p) = &self.st.survivor {
            self.rec.inventory(p.arsenal.inventory());
        }
    }
    pub fn survivor_inventory(&self) -> Option<Inventory> {
        self.st.survivor.as_ref().map(|p| p.arsenal.inventory())
    }
    /// The offer rules call this after a causal key pick, never from the renderer.
    pub fn learn_survivor_weapon(&mut self, id: WeaponId) -> Result<u8, SlotError> {
        let p = self.st.survivor.as_mut().ok_or(SlotError::Unavailable)?;
        let rank = p.arsenal.learn(id, self.st.run_s)?;
        self.publish_inventory();
        Ok(rank)
    }
    /// Initial-loadout tools use the same passive delta as causal offer/chest choices.
    pub fn learn_survivor_passive(
        &mut self,
        id: crate::survivors::PassiveId,
    ) -> Result<u8, SlotError> {
        let rank = self
            .st
            .survivor
            .as_mut()
            .ok_or(SlotError::Unavailable)?
            .arsenal
            .learn_passive(id)?;
        self.apply_survivor_stats(id.delta());
        self.publish_inventory();
        Ok(rank)
    }
    /// Chest rewards call this atomically; emitted projectiles/bursts finish as captured.
    pub fn evolve_survivor_weapon(&mut self, id: WeaponId) -> Result<(), SlotError> {
        self.st
            .survivor
            .as_mut()
            .ok_or(SlotError::Unavailable)?
            .arsenal
            .evolve(id, self.st.run_s)?;
        if id == WeaponId::SoulEater {
            self.st.garlic = None;
            self.st.soul_recovered = 0.0;
        }
        self.rec.evolve(id.as_str(), self.st.s);
        self.publish_inventory();
        Ok(())
    }
    pub(super) fn hold_weapons(&mut self) {
        self.st.spawner.hold();
        if let Some(p) = &mut self.st.survivor {
            p.arsenal.hold();
        }
    }
}
