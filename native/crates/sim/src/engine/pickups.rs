//! Deaths, gems, vacuums, level-ups and their orbs, chickens and health (simulate.ts's deaths, gems
//! and level-ups section).

use std::rc::Rc;

use super::{
    CHICKEN_BOSS, CHICKEN_CHANCE, CHICKEN_SPREAD, CHICKEN_TOUCH, Ev, FieldGem, GEM_CAP, KNOCK_TIME,
    LEVEL_PULSE, ORB_BARS, ORB_GRID_WAIT, ORB_RISE, ORB_TOUCH, Orbs, Sim, Spot, VACUUM_TOUCH,
};
use crate::content::types::Size;
use crate::core::rng::{Part, Rng};
use crate::core::shaping::clamp;
use crate::core::songmap::bar_of_beat_index;
use crate::jsmath::{PI, atan2, cos, floor, hypot, max, min, sin};
use crate::player::Point;
use crate::recorder::{BiteRecord, ChickenRecord, Fate, GemRecord, HealRecord, Hit};
use crate::timing::{NEVER_LATE, first_at_or_after};
use crate::upgrades::{ItemKind, UpgradeItem, UpgradeRecord, offers_for, passive_bonus, take_item};

/// Upgrade orbs rise this far apart (radians).
pub const ORB_SPREAD: f64 = (2.0 * PI) / 3.0;

#[derive(Clone, Debug)]
pub(super) struct FlyingGem {
    gem: FieldGem,
    motion: crate::survivors::Attraction,
}

impl Sim {
    pub(super) fn on_death(&mut self, fi: usize, fate: Fate, h: &Hit) {
        let env = Rc::clone(&self.env);
        let f = &self.st.foes[fi];
        let def = &env.input.lib.enemies[f.def];
        // Every death drops gems; during the finale blast they fly to the hero a beat after it.
        let value = if self.st.survivor.is_some() {
            let xp = def.survivors.expect("survivor enemy stats").xp;
            let random = Rng::new(&[
                Part::Num(env.input.seed),
                Part::Str("vs-gem"),
                Part::Num(fi as f64),
            ])
            .next();
            crate::survivors::gem_value(xp, random)
        } else if def.size != Size::Regular {
            5.0
        } else {
            1.0
        };
        let big = if self.st.survivor.is_some() {
            value >= 10.0
        } else {
            def.size != Size::Regular
        };
        if self.st.blasting {
            let blast_s = self.blast_s().expect("the finale's blast");
            let s1 = if self.st.survivor.is_some() {
                self.known_grid_after(blast_s + 1e-8, 1.0)
                    .map_or(blast_s + self.env.rule.free_period_s, |(_, at)| at)
            } else {
                self.known_time_of_beat(self.known_beat_at(blast_s) + 1.0)
            };
            if value > 0.0 {
                self.rec.gem(GemRecord {
                    w0: self.st.w,
                    x0: fate.x,
                    y0: fate.y,
                    s1,
                    w1: 0.0,
                    value,
                    big,
                    enemy: fi as f64,
                    w_t: None,
                });
                let gem = self.st.n_gems;
                if self.st.survivor.is_some() {
                    self.rec.gem_value(gem, value);
                }
                self.st.n_gems += 1;
                self.st.q.push(s1, Ev::Gem { gem, value });
            }
            if self.st.boss_foe == Some(fi) {
                self.boss_died(h.id, h.source);
            }
            return;
        }
        if value > 0.0 {
            self.drop_on_field(fi, fate, value, big);
        }
        // The small health pool: an orc elite drops a chicken half the time (seeded by the enemy), the boss two.
        let chicken_roll = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("chicken"),
            Part::Num(fi as f64),
        ])
        .next();
        let n = if let Some(run) = env.input.level.survivors {
            if self.st.foes[fi].survivor_boss {
                run.boss_chickens
            } else if def.survivors.is_some_and(|s| s.brute)
                && chicken_roll < run.brute_chicken_chance
            {
                1.0
            } else {
                0.0
            }
        } else if def.size == Size::Boss {
            CHICKEN_BOSS
        } else if def.size == Size::Elite
            && Rng::new(&[
                Part::Num(env.input.seed),
                Part::Str("chicken"),
                Part::Num(fi as f64),
            ])
            .next()
                < CHICKEN_CHANCE
        {
            1.0
        } else {
            0.0
        };
        let mut k = 0.0;
        while k < n {
            let x = fate.x
                + if n > 1.0 {
                    (k - (n - 1.0) / 2.0) * CHICKEN_SPREAD
                } else {
                    0.0
                };
            self.rec.chicken(ChickenRecord {
                s: self.st.s,
                w: self.st.w,
                x,
                y: fate.y,
                s_taken: None,
                w_taken: None,
                s_heal: None,
            });
            let i = self.st.n_chickens;
            if self.st.survivor.is_some() {
                self.rec.floor_chicken(
                    self.st.s,
                    crate::survivors::FloorChicken::Drop {
                        id: i,
                        x,
                        y: fate.y,
                    },
                );
            }
            self.st.n_chickens += 1;
            self.st.chickens.push(Spot { i, x, y: fate.y });
            k += 1.0;
        }
        if self.st.boss_foe == Some(fi) {
            self.boss_died(h.id, h.source);
        }
    }

    /// The finale blast's song time, once the finale is known.
    pub(super) fn blast_s(&self) -> Option<f64> {
        self.st.plan.finale.as_ref().map(|f| f.s)
    }

    /// The pickup radius at the tier now, widened by the magnet passive.
    pub(super) fn pickup_radius(&self) -> f64 {
        if let Some(p) = &self.st.survivor {
            return crate::survivors::PICKUP_RADIUS * p.stats.magnet;
        }
        let r = self
            .env
            .input
            .lib
            .hero
            .pickup_radius
            .clone()
            .unwrap_or_else(|| vec![40.0]);
        let i = max(0.0, min(r.len() as f64 - 1.0, self.st.tier));
        // (An empty list reads undefined in TS: NaN.)
        r.get(i as usize).copied().unwrap_or(f64::NAN) * (1.0 + self.st.bonus.pickup)
    }

    /// At the cap, survivors merges into the farthest gem from the hero; legacy uses the nearest death.
    pub(super) fn drop_on_field(&mut self, fi: usize, fate: Fate, value: f64, big: bool) {
        let survivors = self.st.survivor.is_some();
        let cap = if survivors {
            crate::survivors::GEM_CAP
        } else {
            GEM_CAP
        };
        if self.st.field.len() >= cap {
            let hp = self.hero_pose(self.st.w);
            let mut near = 0usize;
            for (i, g) in self.st.field.iter().enumerate() {
                let n = &self.st.field[near];
                let choose = if survivors {
                    hypot(g.x - hp.x, g.y - hp.y) > hypot(n.x - hp.x, n.y - hp.y)
                } else {
                    hypot(g.x - fate.x, g.y - fate.y) < hypot(n.x - fate.x, n.y - fate.y)
                };
                if choose {
                    near = i;
                }
            }
            let g = &mut self.st.field[near];
            g.value += value;
            g.big = if survivors {
                g.value >= 10.0
            } else {
                g.big || big || g.value >= 5.0
            };
            let (gem, v, b) = (g.gem, g.value, g.big);
            self.rec.gem_grow(gem, v, b);
            if survivors {
                self.rec.gem_value(gem, v);
            }
            return;
        }
        self.rec.gem(GemRecord {
            w0: self.st.w,
            x0: fate.x,
            y0: fate.y,
            s1: f64::INFINITY,
            w1: f64::INFINITY,
            value,
            big,
            enemy: fi as f64,
            w_t: None,
        });
        let gem = self.st.n_gems;
        if survivors {
            self.rec.gem_value(gem, value);
        }
        self.st.n_gems += 1;
        self.st.field.push(FieldGem {
            gem,
            x: fate.x,
            y: fate.y,
            value,
            big,
        });
    }

    /// A gem takes off for the hero now, collected at song time s1.
    fn take_off(&mut self, g: FieldGem, s1: f64) {
        self.rec.gem_fly(g.gem, self.st.w, s1);
        self.st.q.push(
            s1,
            Ev::Gem {
                gem: g.gem,
                value: g.value,
            },
        );
    }

    /// On every fixed step: gems inside the pickup radius take off; a vacuum the hero touches sends
    /// every gem in view.
    pub(super) fn collect_gems(&mut self) {
        if self.st.blasting {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        if self.st.survivor.is_some() {
            self.collect_survivor_gems();
        } else {
            let r = self.pickup_radius();
            if self
                .st
                .field
                .iter()
                .any(|g| hypot(g.x - hp.x, g.y - hp.y) <= r)
            {
                let s1 = self.sixteenth_at_or_after(self.st.s + NEVER_LATE);
                let field = std::mem::take(&mut self.st.field);
                for g in field {
                    if hypot(g.x - hp.x, g.y - hp.y) > r {
                        self.st.field.push(g);
                    } else {
                        self.take_off(g, s1);
                    }
                }
            }
        }
        if let Some(v) = self
            .st
            .vacuums
            .iter()
            .copied()
            .find(|v| hypot(v.x - hp.x, v.y - hp.y) <= VACUUM_TOUCH)
        {
            self.touch_vacuum(v, (hp.x, hp.y));
        }
    }

    fn collect_survivor_gems(&mut self) {
        if self.st.freeze.frozen || self.st.health <= 0.0 {
            return;
        }
        let flying = std::mem::take(&mut self.st.flying_gems);
        for mut f in flying {
            let (mut updated, mut collected) = (false, false);
            while f.motion.next_w <= self.st.w + 1e-9 {
                let hp = self.hero_pose(f.motion.next_w);
                collected = f.motion.update((hp.x, hp.y), super::HURTBOX_RADIUS);
                updated = true;
                if collected {
                    break;
                }
            }
            if updated {
                self.rec.gem_pose(
                    self.st.s,
                    crate::survivors::GemPose {
                        id: f.gem.gem,
                        w: self.st.w,
                        x: f.motion.x,
                        y: f.motion.y,
                    },
                );
            }
            if collected {
                let at = self.sixteenth_at_or_after(self.st.s + NEVER_LATE);
                self.st.q.push(
                    at,
                    Ev::Gem {
                        gem: f.gem.gem,
                        value: f.gem.value,
                    },
                );
            } else {
                self.st.flying_gems.push(f);
            }
        }
        let hp = self.hero_pose(self.st.w);
        let radius = self.pickup_radius() + crate::survivors::GEM_BODY_RADIUS;
        for gem in std::mem::take(&mut self.st.field) {
            if (gem.x - hp.x).hypot(gem.y - hp.y) > radius {
                self.st.field.push(gem);
                continue;
            }
            let motion = crate::survivors::Attraction::new(self.st.w, gem.x, gem.y);
            self.rec.gem_pose(
                self.st.s,
                crate::survivors::GemPose {
                    id: gem.gem,
                    w: self.st.w,
                    x: gem.x,
                    y: gem.y,
                },
            );
            self.st.flying_gems.push(FlyingGem { gem, motion });
        }
    }

    fn touch_vacuum(&mut self, v: Spot, hp: (f64, f64)) {
        self.st.vacuums.retain(|x| x.i != v.i);
        let view = self.st.camera.view();
        let mut in_view: Vec<FieldGem> = self
            .st
            .field
            .iter()
            .copied()
            .filter(|g| g.x >= view[0] && g.x <= view[2] && g.y >= view[1] && g.y <= view[3])
            .collect();
        let d = |g: &FieldGem| hypot(g.x - hp.0, g.y - hp.1);
        in_view.sort_by(|a, b| {
            (d(a) - d(b))
                .partial_cmp(&0.0)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
        self.rec
            .vacuum_touch(v.i, self.st.s, self.st.w, in_view.len() as f64);
        if in_view.is_empty() {
            return;
        }
        // Over one bar of sixteenths from the first at least NEVER_LATE away, nearest first.
        let first = self.sixteenth_at_or_after(self.st.s + NEVER_LATE);
        let free = self.st.survivor.is_some()
            && self
                .known_grid_after(self.st.s + NEVER_LATE, 0.25)
                .is_none();
        let bi0 = self.known_beat_at(first);
        let bar = max(1.0, bar_of_beat_index(&self.st.idx, self.sbi_at(self.st.s)));
        let bpb = super::foes::get_or(&self.st.idx.beats_per_bar, bar, 2.0);
        let n = 4.0 * bpb;
        let len = in_view.len() as f64;
        let taken: Vec<usize> = in_view.iter().map(|g| g.gem).collect();
        for (k, g) in in_view.into_iter().enumerate() {
            let offset = floor((k as f64 * n) / len) / 4.0;
            let t = self.known_time_of_beat(bi0 + offset);
            let t = if self.st.survivor.is_some() && (free || !t.is_finite()) {
                first + offset * self.env.rule.free_period_s
            } else {
                t
            };
            self.take_off(g, t);
        }
        self.st.field.retain(|g| !taken.contains(&g.gem));
    }

    pub(super) fn level_req(&self) -> f64 {
        let lu = &self.st.sch.level_ups;
        if lu.is_empty() {
            1.0
        } else {
            lu[self.st.level.min(lu.len() - 1)].xp
        }
    }

    fn push_xp(&mut self) {
        let fill = self.st.survivor.as_ref().map_or_else(
            || clamp(self.st.xp_now / self.level_req(), 0.0, 1.0),
            crate::survivors::Progress::fill,
        );
        self.rec.xp(self.st.s, fill);
    }

    pub(super) fn grant(&mut self) {
        if self.st.blasting {
            return;
        }
        let env = Rc::clone(&self.env);
        self.st.level_ready = false;
        let index = self.st.level;
        self.st.level += 1;
        self.st.xp_now = 0.0;
        self.st.overdue = false;
        self.push_xp();
        // A choice still waiting takes its orb ahead first, and what has been taken but not yet
        // applied counts as owned.
        if self.st.orbs.is_some() {
            self.take_orb(0, false);
        }
        let mut owned = self.st.loadout.clone();
        for item in &self.st.applying {
            take_item(&mut owned, item);
        }
        let offers = offers_for(
            env.input.seed,
            index as f64,
            &env.input.lib,
            &env.input.level.upgrades.offer,
            &owned,
        );
        self.heal(env.input.lib.hero.health.level_up_heal, "level-up");
        if offers.is_empty() {
            self.rec.upgrade(UpgradeRecord {
                s: self.st.s,
                offers,
                taken: -1,
                w: None,
                orbs: None,
                s_taken: None,
                w_taken: None,
                s_apply: None,
                walked: None,
            });
        } else {
            self.rise_orbs(index, offers);
        }
    }

    /// Upgrade orbs: the pulse pushes nearby enemies out, and the offers rise as orbs around the hero.
    fn rise_orbs(&mut self, u: usize, offers: Vec<UpgradeItem>) {
        let w = self.st.w;
        let hp = self.hero_pose(w);
        for fi in self.st.alive.clone() {
            if !self.st.foes[fi].alive || self.st.boss_foe == Some(fi) || self.pinned(fi) {
                continue;
            }
            let (x, y) = self.pos(fi, w);
            let dx = x - hp.x;
            let dy = y - hp.y;
            let d = hypot(dx, dy);
            if d >= LEVEL_PULSE || d < 1e-6 {
                continue;
            }
            let f = &mut self.st.foes[fi];
            f.knock_x = (dx / d) * (LEVEL_PULSE - d);
            f.knock_y = (dy / d) * (LEVEL_PULSE - d);
            f.knock_t = KNOCK_TIME;
        }
        let a0 = atan2(self.st.heading.1, self.st.heading.0);
        let spread = [0.0, -ORB_SPREAD, ORB_SPREAD];
        let pts: Vec<(f64, f64)> = (0..offers.len())
            .map(|i| {
                let a = a0 + spread[i];
                (hp.x + cos(a) * ORB_RISE, hp.y + sin(a) * ORB_RISE)
            })
            .collect();
        let idx = &self.st.idx;
        let bar = max(1.0, bar_of_beat_index(idx, self.sbi_at(self.st.s)));
        self.rec.upgrade(UpgradeRecord {
            s: self.st.s,
            offers: offers.clone(),
            taken: -1,
            w: Some(w),
            orbs: Some(pts.iter().map(|&(x, y)| Point { x, y }).collect()),
            s_taken: None,
            w_taken: None,
            s_apply: None,
            walked: None,
        });
        // Reactive mode: the deadline is counted in committed beats as they arrive.
        let deadline_beat = idx.bar_start[bar as usize]
            + ORB_BARS * super::foes::get_or(&idx.beats_per_bar, bar, 2.0);
        self.st.orbs = Some(Orbs {
            u,
            offers,
            pts,
            deadline_beat,
        });
    }

    /// On every fixed step: a chicken within reach of the hero is eaten, healing on the first
    /// sixteenth at least NEVER_LATE away.
    pub(super) fn touch_chickens(&mut self) {
        if self.st.survivor.is_some() && (self.st.freeze.frozen || self.st.health <= 0.0) {
            return;
        }
        if self.st.chickens.is_empty() {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        let chickens = std::mem::take(&mut self.st.chickens);
        for c in chickens {
            if hypot(c.x - hp.x, c.y - hp.y) > CHICKEN_TOUCH {
                self.st.chickens.push(c);
                continue;
            }
            let s_heal = self.sixteenth_at_or_after(self.st.s + NEVER_LATE);
            self.rec.chicken_taken(c.i, self.st.s, self.st.w, s_heal);
            if self.st.survivor.is_some() {
                self.st.invuln_until = self
                    .st
                    .invuln_until
                    .max(self.st.w + crate::survivors::CHICKEN_IMMUNITY_S);
                self.rec
                    .floor_chicken(self.st.s, crate::survivors::FloorChicken::Taken { id: c.i });
            }
            self.st.q.push(s_heal, Ev::ChickenHeal { chicken: c.i });
        }
    }

    /// On every fixed step: an orb within reach of the hero is taken (the nearest, if two are).
    pub(super) fn touch_orbs(&mut self) {
        let Some(o) = &self.st.orbs else { return };
        let hp = self.hero_pose(self.st.w);
        let mut k: Option<usize> = None;
        for (i, p) in o.pts.iter().enumerate() {
            let d = hypot(p.0 - hp.x, p.1 - hp.y);
            if d <= ORB_TOUCH && k.is_none_or(|k| d < hypot(o.pts[k].0 - hp.x, o.pts[k].1 - hp.y)) {
                k = Some(i);
            }
        }
        if let Some(k) = k {
            self.take_orb(k, true);
        }
    }

    /// An orb is taken: it applies on the first grid time at least NEVER_LATE away.
    pub(super) fn take_orb(&mut self, k: usize, walked: bool) {
        let o = self.st.orbs.take().expect("orbs to take");
        let s = self.st.s;
        let item = o.offers[k].clone();
        self.st.applying.push(item.clone());
        // The first grid time (a committed beat) at least NEVER_LATE away, within ORB_GRID_WAIT. It
        // may be a beat not known yet: then it applies, and is
        // recorded, once that beat arrives (before its time).
        let beats = &self.st.song.beats;
        let i = first_at_or_after(beats, s + NEVER_LATE - 1e-9);
        let w = self.st.w;
        if i < beats.len() {
            let s_apply = match beats.get(i).copied() {
                Some(g) if g - s <= ORB_GRID_WAIT => g,
                Some(b) => b,
                None => s,
            };
            self.rec.upgrade_taken(o.u, k, s, w, s_apply, walked);
            self.st.q.push(s_apply, Ev::OrbApply { item });
        } else {
            self.defer_at_beat(
                i as f64,
                super::Then::OrbTaken {
                    u: o.u,
                    k,
                    s,
                    w,
                    walked,
                    item,
                },
            );
        }
    }

    /// The hero takes an item: a new weapon starts firing, an evolution applies, a passive adds.
    pub(super) fn take_upgrade(&mut self, item: &UpgradeItem) {
        let env = Rc::clone(&self.env);
        let lib = &env.input.lib;
        take_item(&mut self.st.loadout, item);
        self.st.bonus = passive_bonus(lib, &self.st.loadout.passives);
        match item.kind {
            ItemKind::Weapon => self.grant_weapon(&item.id),
            ItemKind::Evolve => {
                self.rec.evolve(&item.id, self.st.s);
                let wi = lib.weapons.get_index_of(&item.id).expect("a known weapon");
                let wd = &lib.weapons[wi];
                if let Some(every) = wd.evolve.as_ref().and_then(|e| e.every) {
                    self.schedule_fires(wi, every, Some(wd.fires.every));
                }
            }
            ItemKind::Passive => {
                let p = &lib.passives[item.id.as_str()];
                if p.max_health > 0.0 {
                    self.heal(p.max_health, "max-health");
                }
            }
        }
    }

    pub(super) fn gem_arrives(&mut self, i: usize, value: f64) {
        self.rec.gem_arrive(i, self.st.w);
        if let Some(p) = &mut self.st.survivor {
            p.gain_xp(value);
            self.open_survivor_offer();
            self.push_xp();
            self.publish_run();
            return;
        }
        self.st.xp_now += value;
        self.push_xp();
        if self.st.level >= self.st.sch.level_ups.len() {
            return;
        }
        // Reactive mode (stage 4): a full bar waits for the next detected section start, or the
        // director's wait.
        if !self.st.level_ready && self.st.xp_now >= self.level_req() {
            self.st.level_ready = true;
            let at = self.st.s + self.env.director.level_up_wait_s;
            self.st.q.push(
                at,
                Ev::LevelWait {
                    level: self.st.level,
                },
            );
            return;
        }
        if self.st.overdue && self.st.xp_now >= self.level_req() {
            self.st.overdue = false;
            let s = self.st.s;
            self.push_at_downbeat(s, Ev::LevelUp { level: None });
        }
    }

    pub(super) fn level_due(&mut self, i: usize) {
        if i != self.st.level {
            return;
        }
        if self.st.xp_now >= self.level_req() {
            self.grant();
        } else {
            self.st.overdue = true;
        }
    }

    pub fn survivor_progress(&self) -> Option<&crate::survivors::Progress> {
        self.st.survivor.as_ref()
    }

    /// Sim-owned passive rules call this; added maximum HP is healed, as in VS.
    pub fn apply_survivor_stats(&mut self, delta: crate::survivors::StatDelta) {
        if let Some(p) = &mut self.st.survivor {
            let before = p.stats.max_hp;
            p.stats.apply(delta);
            let added = p.stats.max_hp - before;
            if added > 0.0 {
                self.heal(added, "hollow-heart");
            }
            self.publish_run();
        }
    }

    /// The maximum health, with the max-health passives.
    pub(super) fn health_max(&self) -> f64 {
        self.st.survivor.as_ref().map_or(
            self.env.input.lib.hero.health.max + self.st.bonus.max_health,
            |p| p.stats.max_hp,
        )
    }

    /// The hero takes damage; nothing hurts it while it is invulnerable, and each hurt makes it so.
    pub(super) fn hurt(&mut self, amount: f64, source: &'static str, enemy: &str) {
        // Once the survivors deadline is reached, the next downbeat belongs to victory.
        if self
            .env
            .input
            .level
            .survivors
            .is_some_and(|run| self.st.run_s >= run.duration_s)
            || amount <= 0.0
            || self.st.health <= 0.0
            || self.st.w < self.st.invuln_until - 1e-9
            || self.env.input.no_hero_damage
        {
            return;
        }
        let hero = &self.env.input.lib.hero;
        let amount = if let Some(p) = &self.st.survivor {
            self.st.invuln_until = self.st.w + crate::survivors::DAMAGE_LOCKOUT_S;
            if source == "bite" {
                p.stats.contact_damage(
                    amount,
                    self.env
                        .input
                        .level
                        .survivors
                        .expect("survivors")
                        .contact_damage,
                )
            } else {
                amount
            }
        } else {
            self.st.invuln_until = self.st.w + hero.health.invulnerable_ms / 1000.0;
            amount
        };
        self.st.health = max(0.0, self.st.health - amount);
        let frac = self.st.health / self.health_max();
        self.rec.health(self.st.s, frac);
        self.rec.bite(BiteRecord {
            s: self.st.s,
            w: self.st.w,
            source,
            enemy: enemy.to_string(),
            damage: amount,
            until: self.st.invuln_until,
        });
    }

    /// The hero heals (never past its maximum).
    pub(super) fn heal(&mut self, amount: f64, source: &'static str) {
        let hm = self.health_max();
        if self.st.health <= 0.0 || self.st.health >= hm {
            return;
        }
        let before = self.st.health;
        self.st.health = min(hm, self.st.health + amount);
        self.rec.health(self.st.s, self.st.health / hm);
        self.rec.heal(HealRecord {
            s: self.st.s,
            amount: self.st.health - before,
            source,
        });
    }
}
