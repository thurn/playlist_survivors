//! Enemies: spawning by the director, beat steps, motion and separation, samples and recycling
//! (simulate.ts's enemies, motion and spawning sections), and the beat-time helpers they use.

use std::rc::Rc;

use super::{
    CALM_CURL, CELL, CHARGE_SPEED, CONTACT_GAP, ENEMY_RATE, Ev, Foe, FoeStep, G, HURTBOX_RADIUS,
    KNOCK_TIME, MIN_GAP, POP_CAP, RECYCLE_VIEWS, SEPARATION, SPAWN_EXTRA, SPAWN_FORWARD,
    SPAWN_MARGIN, SPAWN_PER_BEAT, STREAM_SHARE, Sim,
};
use crate::content::types::{Behavior, EnemyDef, Subdivision};
use crate::core::rng::{Part, Rng};
use crate::core::shaping::clamp;
use crate::core::songmap::{bar_of_beat_index, time_of_beat_index};
use crate::jsmath::{PI, atan2, ceil, cos, floor, hypot, max, min, round, sin, sqrt};
use crate::movement::ease_out;
use crate::reactive::director::{by_level, director_population, section_roster};
use crate::recorder::EnemyRecord;

/// JS's `xs[i] ?? d`: the item at a whole index in range, else d.
pub(super) fn get_or(xs: &[f64], i: f64, d: f64) -> f64 {
    if i >= 0.0 && i.fract() == 0.0 {
        xs.get(i as usize).copied().unwrap_or(d)
    } else {
        d
    }
}

/// Sprite half extents (x, up, down) used for off-screen spawns and checks.
pub fn sprite_extents(def: &EnemyDef) -> (f64, f64, f64) {
    let big = matches!(def.atlas.as_str(), "slime" | "bat" | "bee" | "eyeball")
        || def.atlas.starts_with("thorn-plant-");
    let s = def.scale;
    if big {
        (16.0 * s, (32.0 + def.hover) * s, 6.0 * s)
    } else {
        (32.0 * s, (58.0 + def.hover) * s, 8.0 * s)
    }
}

impl Sim {
    pub(super) fn pos(&self, fi: usize, at_w: f64) -> (f64, f64) {
        let f = &self.st.foes[fi];
        let at_w = if self.st.survivor.is_some() && f.recoil_until > f.last_w {
            at_w.min(f.recoil_until)
        } else {
            at_w
        };
        if let Some(g) = f.step {
            let song = self
                .st
                .breaks
                .iter()
                .rev()
                .find(|b| b.w <= at_w + 1e-12 && b.rate > 0.0)
                .map_or(self.st.s, |b| b.s + (at_w - b.w) / b.rate);
            if song > g.peak_s + 1.0 / super::SIM_RATE + 1e-9 {
                return (f.x, f.y);
            }
            let de = ease_out(clamp((song - g.s0) / (g.peak_s - g.s0), 0.0, 1.0))
                - ease_out(clamp((g.last_s - g.s0) / (g.peak_s - g.s0), 0.0, 1.0));
            return (f.x + g.dx * de, f.y + g.dy * de);
        }
        if f.recoil_until > f.last_w {
            return (
                f.x + f.recoil_v.0 * (at_w - f.last_w),
                f.y + f.recoil_v.1 * (at_w - f.last_w),
            );
        }
        (f.x, f.y)
    }

    /// Can this enemy be hit now? (The boss is out of reach while it drops in.)
    pub(super) fn hittable(&self, fi: usize) -> bool {
        // A travelling missile can outlive its target's final sample and retirement.
        self.st
            .foes
            .get(fi)
            .is_some_and(|f| f.alive && f.land_s.is_none_or(|l| self.st.s >= l - 1e-9))
    }

    /// Formation members on their slots, ring members while their ring holds, and the boss dropping
    /// in hold their places.
    pub(super) fn pinned(&self, fi: usize) -> bool {
        let f = &self.st.foes[fi];
        ((f.slot.is_some() || f.has_at) && self.st.s < f.slot_until)
            || f.land_s.is_some_and(|l| self.st.s < l)
    }

    pub(super) fn kill(&mut self, fi: usize) {
        let w = self.st.w;
        let f = &mut self.st.foes[fi];
        f.alive = false;
        f.final_sample = true;
        f.death_w = w;
        self.rec.death(fi, w, false);
    }

    pub(super) fn spawn_foe(&mut self, def_i: usize, x: f64, y: f64, health: Option<f64>) -> usize {
        self.spawn_with(def_i, x, y, health, None, false)
    }

    /// A set piece's member: recorded with its set piece's id.
    pub(super) fn spawn_member(
        &mut self,
        def_i: usize,
        x: f64,
        y: f64,
        member: super::Member,
    ) -> usize {
        self.spawn_with(def_i, x, y, None, Some(member), false)
    }

    /// The boss: it lands (an entrance), with its attacks recorded.
    pub(super) fn spawn_entrance(&mut self, def_i: usize, x: f64, y: f64) -> usize {
        self.spawn_with(def_i, x, y, None, None, true)
    }

    fn spawn_with(
        &mut self,
        def_i: usize,
        x: f64,
        y: f64,
        health: Option<f64>,
        member: Option<super::Member>,
        entrance: bool,
    ) -> usize {
        let piece = member.map(|m| self.piece_id(m));
        let env = Rc::clone(&self.env);
        let def = &env.input.lib.enemies[def_i];
        let id = self.st.foes.len();
        let h0 = health.unwrap_or_else(|| {
            def.health
                * if let Some(progress) = self
                    .st
                    .survivor
                    .as_ref()
                    .filter(|_| def.survivors.is_some_and(|s| s.level_scaled))
                {
                    progress.level as f64
                } else {
                    1.0
                }
        });
        // A stepping enemy takes its first step on its next step's gait time.
        let step_k = Some(
            self.log.beats[..self.st.ingested.beats].partition_point(|b| b.t <= self.st.s) as f64
                - 1.0,
        );
        let weight = self.weight_of(def, false);
        self.st.foes.push(Foe {
            id,
            def: def_i,
            r: def.radius,
            health: h0,
            survivor_boss: false,
            swarm_dir: None,
            chest_loot: None,
            attack: None,
            x,
            y,
            vx: 0.0,
            vy: 0.0,
            last_w: self.st.w,
            alive: true,
            final_sample: false,
            death_w: f64::INFINITY,
            last_fate: None,
            sx: 0.0,
            sy: 0.0,
            knock_x: 0.0,
            knock_y: 0.0,
            knock_t: 0.0,
            recoil_until: f64::NEG_INFINITY,
            recoil_v: (0.0, 0.0),
            knockback: def.survivors.map_or(0.0, |s| s.knockback),
            weight,
            charge_until: f64::NEG_INFINITY,
            land_s: None,
            step_k,
            step: None,
            health0: h0,
            member,
            slot: None,
            slot_until: f64::NEG_INFINITY,
            has_at: false,
            ring_path: None,
            run: None,
            retreat: false,
            archer_group: None,
            archer_slot: None,
            drawing: None,
        });
        // Its first sample is the spawn spot itself, at the last sample time before it appeared.
        self.rec.spawn(EnemyRecord {
            id: id as f64,
            kind: def.id.clone(),
            health: h0,
            spawn_w: self.st.w,
            death_w: f64::INFINITY,
            recycled: None,
            sample_count: 0.0,
            sample_w0: max(0.0, self.st.next_foe_k - 1.0) / ENEMY_RATE,
            fates: Vec::new(),
            formation: piece.map(|p| p as f64),
            entrance: entrance.then(|| "land".to_string()),
            attacks: entrance.then(Vec::new),
            dance: None,
            steps: None,
        });
        self.rec.sample(id, x, y);
        self.st.alive.push(id);
        self.st.foe_samples.push(id);
        id
    }

    /// The way forward now: the hero and its heading.
    pub(super) fn forward(&self) -> (f64, f64, f64, f64) {
        let hp = self.hero_pose(self.st.w);
        (hp.x, hp.y, self.st.heading.0, self.st.heading.1)
    }

    pub(super) fn spawn_point(
        &self,
        def: &EnemyDef,
        r: &mut Rng,
        forward_only: bool,
    ) -> (f64, f64) {
        let view = self.st.camera.view();
        let cx = (view[0] + view[2]) / 2.0;
        let cy = (view[1] + view[3]) / 2.0;
        let (hx, up, down) = sprite_extents(def);
        let m = SPAWN_MARGIN + SPAWN_EXTRA;
        let (_, _, ptx, pty) = self.forward();
        let fwd = atan2(pty, ptx);
        let ahead = forward_only || r.next() < SPAWN_FORWARD;
        let ang = if ahead {
            fwd + r.range(-PI / 2.0, PI / 2.0)
        } else {
            r.range(-PI, PI)
        };
        let out = r.range(0.0, 40.0);
        let c = cos(ang);
        let sn = sin(ang);
        let left = cx - view[0] + hx + m + out;
        let right = view[2] - cx + hx + m + out;
        let top = cy - view[1] + down + m + out;
        let bottom = view[3] - cy + up + m + out;
        let tx = if c > 1e-9 {
            right / c
        } else if c < -1e-9 {
            left / -c
        } else {
            f64::INFINITY
        };
        let ty = if sn > 1e-9 {
            bottom / sn
        } else if sn < -1e-9 {
            top / -sn
        } else {
            f64::INFINITY
        };
        let t = min(tx, ty);
        (cx + c * t, cy + sn * t)
    }

    /// Integrate only admitted travel or a separately tagged forced reaction. The
    /// endpoint is analytic, so sample cadence cannot stretch a step into its hold.
    pub(super) fn move_foes(&mut self, w1: f64) {
        let hp = self.hero_pose(w1);
        let alive = self.st.alive.clone();
        let admitted = self.enemy_main_window();
        let reliable = self.enemy_reliable();
        for &fi in &alive {
            let f = &mut self.st.foes[fi];
            if f.recoil_until > f.last_w {
                let used = (w1.min(f.recoil_until) - f.last_w).max(0.0);
                f.x += f.recoil_v.0 * used;
                f.y += f.recoil_v.1 * used;
                f.step = None;
            } else if f.knock_t > 0.0 {
                let used = (w1 - f.last_w).min(f.knock_t);
                f.x += f.knock_x * used / KNOCK_TIME;
                f.y += f.knock_y * used / KNOCK_TIME;
                f.knock_t -= used;
                f.step = None;
            } else if let Some(g) = f.step {
                // Loss of confidence cancels travel; it never finishes a stale step.
                if reliable && self.st.s <= g.peak_s + 1.0 / super::SIM_RATE + 1e-9 {
                    let de = ease_out(clamp((self.st.s - g.s0) / (g.peak_s - g.s0), 0.0, 1.0))
                        - ease_out(clamp((g.last_s - g.s0) / (g.peak_s - g.s0), 0.0, 1.0));
                    f.x += g.dx * de;
                    f.y += g.dy * de;
                    f.step.as_mut().expect("step").last_s = self.st.s;
                } else {
                    f.step = None;
                }
            }
            if f.recoil_until <= w1 {
                f.recoil_until = f64::NEG_INFINITY;
            }
            if let Some(path) = f.ring_path.as_mut() {
                let radius = ((f.x - path.x) * path.c + (f.y - path.y) * path.sn)
                    / (path.c * path.c + path.sn * path.sn).max(1e-9);
                path.u = ((path.from - radius) / (path.from - path.to).max(1e-9)).clamp(0.0, 1.0);
            }
            f.last_w = w1;
            f.vx = 0.0;
            f.vy = 0.0;
        }
        let Some((bi, peak, period)) = admitted else {
            return;
        };
        // Separation only contributes to a newly admitted step. Recomputing it on every
        // physics tick within the same beat window changes no destination and dominates
        // dense rollback work. Still admit a new spawn or a foe leaving recoil mid-window.
        let needs_step = |f: &Foe| {
            !f.step_k.is_some_and(|k| k >= bi as f64)
                && f.recoil_until < w1
                && f.knock_t <= 0.0
                && !f.land_s.is_some_and(|s| self.st.s < s)
        };
        if !alive.iter().any(|&id| needs_step(&self.st.foes[id])) {
            return;
        }
        let s = self.st.s;
        let slots: Vec<usize> = alive.iter().map(|&id| self.st.foes.slot(id)).collect();
        let gox = floor(hp.x / CELL) as i64 - G / 2;
        let goy = floor(hp.y / CELL) as i64 - G / 2;
        let separation_input: Vec<[u64; 4]> = alive
            .iter()
            .map(|&id| {
                let f = &self.st.foes[id];
                [
                    f.x.to_bits(),
                    f.y.to_bits(),
                    f.r.to_bits(),
                    u64::from(self.pinned(id) || f.run.is_some()),
                ]
            })
            .collect();
        if let Some(output) = self.spatial.cache.lookup((gox, goy), &separation_input) {
            for (&slot, pose) in slots.iter().zip(output) {
                let f = self.st.foes.at_slot_mut(slot);
                [f.x, f.y, f.sx, f.sy] = *pose;
            }
        } else {
            {
                if self.spatial.generation == i32::MAX {
                    self.spatial.stamp.fill(0);
                    self.spatial.generation = 0;
                }
                self.spatial.generation += 1;
                let generation = self.spatial.generation;
                if self.spatial.next_arr.len() < alive.len() {
                    self.spatial.next_arr = vec![0; alive.len() * 2];
                }
                let cell_of = |x: f64, gox: i64| floor(x / CELL) as i64 - gox;
                for (i, &fi) in alive.iter().enumerate() {
                    let f = &mut self.st.foes[fi];
                    f.sx = 0.0;
                    f.sy = 0.0;
                    let ix = cell_of(f.x, gox);
                    let iy = cell_of(f.y, goy);
                    if ix < 0 || iy < 0 || ix >= G || iy >= G {
                        self.spatial.next_arr[i] = -2;
                        continue;
                    }
                    let c = (iy * G + ix) as usize;
                    if self.spatial.stamp[c] != generation {
                        self.spatial.stamp[c] = generation;
                        self.spatial.head[c] = -1;
                    }
                    self.spatial.next_arr[i] = self.spatial.head[c];
                    self.spatial.head[c] = i as i32;
                }
                // Separation forces and positional correction, pairwise.
                for i in 0..alive.len() {
                    let (ax0, ay0) = (self.st.foes[alive[i]].x, self.st.foes[alive[i]].y);
                    let ix = cell_of(ax0, gox);
                    let iy = cell_of(ay0, goy);
                    if ix < 0 || iy < 0 || ix >= G || iy >= G {
                        continue;
                    }
                    for gx in ix - 1..=ix + 1 {
                        if !(0..G).contains(&gx) {
                            continue;
                        }
                        for gy in iy - 1..=iy + 1 {
                            if !(0..G).contains(&gy) {
                                continue;
                            }
                            let c = (gy * G + gx) as usize;
                            if self.spatial.stamp[c] != generation {
                                continue;
                            }
                            let mut j = self.spatial.head[c];
                            while j >= 0 {
                                let ju = j as usize;
                                let next = self.spatial.next_arr[ju];
                                if ju <= i {
                                    j = next;
                                    continue;
                                }
                                let (a, b) = self.st.foes.pair_at_slots_mut(slots[i], slots[ju]);
                                let ox = a.x - b.x;
                                let oy = a.y - b.y;
                                let lim = SEPARATION * (a.r + b.r);
                                let d2 = ox * ox + oy * oy;
                                if d2 >= lim * lim {
                                    j = next;
                                    continue;
                                }
                                let dd = sqrt(d2);
                                let dd = if dd == 0.0 || dd.is_nan() { 0.01 } else { dd };
                                let ux = if dd > 0.01 { ox / dd } else { 1.0 };
                                let uy = if dd > 0.01 { oy / dd } else { 0.0 };
                                // Members, the landing boss and runners hold their places or course.
                                let pinned = |f: &Foe| {
                                    ((f.slot.is_some() || f.has_at) && s < f.slot_until)
                                        || f.land_s.is_some_and(|l| s < l)
                                };
                                let pa = pinned(a) || a.run.is_some();
                                let pb = pinned(b) || b.run.is_some();
                                if pa && pb {
                                    j = next;
                                    continue;
                                }
                                let wa = if pa {
                                    0.0
                                } else if pb {
                                    1.0
                                } else {
                                    0.5
                                };
                                let k = (1.0 - dd / lim) * 90.0;
                                let gap = MIN_GAP * (a.r + b.r);
                                {
                                    a.sx += ux * k * 2.0 * wa;
                                    a.sy += uy * k * 2.0 * wa;
                                }
                                {
                                    b.sx -= ux * k * 2.0 * (1.0 - wa);
                                    b.sy -= uy * k * 2.0 * (1.0 - wa);
                                }
                                if dd < gap {
                                    let push = gap - dd;
                                    a.sx += ux * push * wa;
                                    a.sy += uy * push * wa;
                                    b.sx -= ux * push * (1.0 - wa);
                                    b.sy -= uy * push * (1.0 - wa);
                                }
                                j = next;
                            }
                        }
                    }
                }
            }
            let output = alive
                .iter()
                .map(|&id| {
                    let f = &self.st.foes[id];
                    [f.x, f.y, f.sx, f.sy]
                })
                .collect();
            self.spatial
                .cache
                .insert((gox, goy), separation_input, output);
        }

        // Separation is part of the next step's destination. It cannot translate
        // roots while holding, and its vector is frozen for the entire step.
        for &fi in &alive {
            let f = &self.st.foes[fi];
            if f.step_k.is_some_and(|k| k >= bi as f64)
                || f.recoil_until >= w1
                || f.knock_t > 0.0
                || f.land_s.is_some_and(|s| self.st.s < s)
            {
                continue;
            }
            self.st.foes[fi].step_k = Some(bi as f64);
            let def = &self.env.input.lib.enemies[self.st.foes[fi].def];
            let cadence = if self.st.foes[fi].survivor_boss {
                1
            } else {
                def.gait.as_ref().map_or(1, |g| {
                    // Subdivision patterns only choose a slower cadence. They
                    // never introduce eighth/sixteenth movement admissions.
                    if crate::gait::gait_ground(g) >= 2.0 {
                        2
                    } else {
                        1
                    }
                })
            };
            if bi % cadence != 0 {
                continue;
            }
            let f = &self.st.foes[fi];
            let span = period * cadence as f64;
            let speed = def.speed
                * if self.st.s < f.charge_until {
                    CHARGE_SPEED
                } else {
                    1.0
                };
            let (x, y) = (f.x, f.y);
            let mut dx = hp.x - x;
            let mut dy = hp.y - y;
            let d = hypot(dx, dy).max(1e-9);
            let contact = HURTBOX_RADIUS + f.r + CONTACT_GAP;
            let mut distance = (d - contact).max(0.0).min(speed * span);
            dx /= d;
            dy /= d;
            if let Some((vx, vy)) = f.run {
                dx = vx;
                dy = vy;
                distance = span;
            } else if let Some((ux, uy)) = f.swarm_dir {
                dx = ux;
                dy = uy;
                distance = speed * span;
            } else if let Some(path) = f.ring_path.filter(|_| self.st.s < f.slot_until) {
                let radius = crate::core::shaping::lerp(path.from, path.to, path.u);
                let next = (radius - (path.from - path.to) * path.du * span).max(path.to);
                dx = path.x + path.c * next - x;
                dy = path.y + path.sn * next - y;
                distance = 1.0;
            } else if let Some(slot) = f.slot.filter(|_| self.st.s < f.slot_until) {
                let a = self.formation_anchor();
                let p = slot.at(bi as f64);
                let tx = a.0 + p.0 - x;
                let ty = a.1 + p.1 - y;
                let d = hypot(tx, ty).max(1e-9);
                dx = tx / d;
                dy = ty / d;
                distance = d.min(speed * span);
            } else if f.has_at && self.st.s < f.slot_until {
                continue;
            } else {
                let calm = self.by_level(self.st.s, &|l| by_level(&self.env.director.calm, l));
                let curl = calm * CALM_CURL * if fi % 2 == 0 { 1.0 } else { -1.0 };
                (dx, dy) = (
                    dx * cos(curl) - dy * sin(curl),
                    dy * cos(curl) + dx * sin(curl),
                );
                distance *= 1.0 - calm;
            }
            let f = &self.st.foes[fi];
            let push = if !self.pinned(fi) && f.run.is_none() && f.swarm_dir.is_none() {
                (f.sx * span, f.sy * span)
            } else {
                (0.0, 0.0)
            };
            // Bound dense correction to one normal step; it never accumulates debt.
            let length = hypot(push.0, push.1).max(1e-9);
            let scale = (speed * span / length).min(1.0);
            let dur = ((peak - self.st.s) * self.rate_at(self.st.s)).max(1e-9);
            let f = &mut self.st.foes[fi];
            f.step = Some(FoeStep {
                s0: self.st.s,
                peak_s: peak,
                last_s: self.st.s,
                w0: w1,
                dur,
                dx: dx * distance + push.0 * scale,
                dy: dy * distance + push.1 * scale,
                span: 0.0,
            });
            self.rec.enemy_step(fi, w1, dur);
        }
    }

    pub(super) fn record_foes(&mut self, wk: f64) {
        let mut i = self.st.foe_samples.len();
        while i > 0 {
            i -= 1;
            let fi = self.st.foe_samples[i];
            let f = &self.st.foes[fi];
            if f.final_sample {
                let (x, y) = match f.last_fate {
                    Some(last) if last.lethal => (last.x, last.y),
                    _ => (f.x, f.y),
                };
                self.rec.sample(fi, x, y);
                self.st.foe_samples.remove(i);
                continue;
            }
            let (x, y) = self.pos(fi, wk);
            self.rec.sample(fi, x, y);
        }
    }

    /// Enemies too far from the hero leave the field and re-enter ahead with their health.
    pub(super) fn recycle(&mut self) {
        let view = self.st.camera.view();
        let vw = view[2] - view[0];
        let w = self.st.w;
        let hp = self.hero_pose(w);
        let behind: Vec<usize> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&fi| {
                let (x, y) = self.pos(fi, w);
                let f = &self.st.foes[fi];
                // Event members keep their identity until their run-clock expiry.
                if matches!(f.member, Some(super::Member::SurvivorEvent(_))) {
                    return false;
                }
                // A stampede's runner, once it has left the view on the side it runs to.
                if let Some((vx, vy)) = f.run.filter(|_| f.alive) {
                    let row = super::pieces::STAMPEDE_ROW;
                    return (vx > 0.0 && x > view[2] + row)
                        || (vx < 0.0 && x < view[0] - row)
                        || (vy > 0.0 && y > view[3] + row)
                        || (vy < 0.0 && y < view[1] - row);
                }
                f.alive && hypot(x - hp.x, y - hp.y) >= RECYCLE_VIEWS * vw
            })
            .collect();
        let env = Rc::clone(&self.env);
        for fi in behind {
            let f = &mut self.st.foes[fi];
            f.alive = false;
            f.final_sample = true;
            f.death_w = w;
            let (def_i, health, id, retreat, boss, chest) = (
                f.def,
                f.health,
                f.id,
                f.retreat,
                f.survivor_boss,
                f.chest_loot,
            );
            self.rec.death(fi, w, true);
            // (A retreating boss leaves for good.)
            if retreat {
                continue;
            }
            let mut r = Rng::new(&[
                Part::Num(env.input.seed),
                Part::Str("recycle"),
                Part::Num(id as f64),
            ]);
            let p2 = self.spawn_point(&env.input.lib.enemies[def_i], &mut r, true);
            let replacement = self.spawn_foe(def_i, p2.0, p2.1, Some(health));
            self.mark_survivor_boss(replacement, boss);
            self.st.foes[replacement].chest_loot = chest;
        }
        let foes = &self.st.foes;
        self.st.alive.retain(|&i| foes[i].alive);
        // A stampede whose runners have all left the view (or died): its vacuum on the next downbeat.
        for i in 0..self.st.stampedes.len() {
            let (k, done) = self.st.stampedes[i];
            let running = self.st.alive.iter().any(|&fi| {
                let f = &self.st.foes[fi];
                f.member == Some(super::Member::Stampede(k)) && f.run.is_some()
            });
            if done || running {
                continue;
            }
            self.st.stampedes[i].1 = true;
            self.push_at_downbeat(self.st.s + 1e-6, Ev::VacuumDrop);
        }
    }

    /// The director's population at song time s, scaled by the loudness level.
    fn director_target(&self, s: f64) -> f64 {
        let d = &self.env.director;
        let music_s = if self.env.input.level.survivors.is_some() {
            self.st.s
        } else {
            s
        };
        let scale = if self.env.input.leak_future_evidence {
            let ticks = &self.log.ticks;
            let i = ticks.partition_point(|k| k.t <= s + 1.0).saturating_sub(1);
            0.6 + 0.7 * ticks.get(i).map_or(0.0, |k| k.evidence)
        } else {
            self.by_level(music_s, &|l| by_level(&d.population.level_scale, l))
        };
        director_population(d, s, scale)
    }

    pub(super) fn spawn_director(&mut self, bi: f64) {
        if self.st.blasting || self.st.survivor.is_some() {
            return;
        }
        let env = Rc::clone(&self.env);
        let lib = &env.input.lib;
        let s = self.st.s;
        let target_s = if env.input.level.survivors.is_some() {
            self.st.run_s
        } else {
            s
        };
        let target = self.director_target(target_s) * STREAM_SHARE;
        let all = self.st.alive.len() as f64 + self.st.pending;
        let living = self
            .st
            .alive
            .iter()
            .filter(|&&fi| self.st.foes[fi].member.is_none())
            .count() as f64
            + self.st.pending;
        let want = min(min(SPAWN_PER_BEAT, round(target) - living), POP_CAP - all);
        if want <= 0.0 {
            return;
        }
        let roster: Vec<(usize, f64)> = section_roster(&env.director, self.st.section)
            .iter()
            .filter_map(|(t, wgt)| {
                let i = lib.enemies.get_index_of(t)?;
                (lib.enemies[i].behavior == Behavior::Chase).then_some((i, *wgt))
            })
            .collect();
        let weights: Vec<f64> = roster.iter().map(|x| x.1).collect();
        let mut r = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("spawn"),
            Part::Num(round(bi * 4.0)),
        ]);
        let mut k = 0.0;
        while k < want {
            let kind = roster[r.weighted_index(roster.len(), &weights)].0;
            let def = &lib.enemies[kind];
            // Each enemy enters on its movement group's subdivision: a downbeat, perhaps not known yet.
            let mut s_appear = s;
            let mut appear_at: Option<f64> = None;
            if matches!(def.group, Subdivision::Bar | Subdivision::TwoBar) {
                let idx = &self.st.idx;
                let bar = bar_of_beat_index(idx, bi + 1e-6);
                let mut nb = bar
                    + if get_or(&idx.bar_start, bar, f64::NAN) < bi - 1e-6 {
                        1.0
                    } else {
                        0.0
                    };
                if def.group == Subdivision::TwoBar && (nb - 1.0) % 2.0 == 1.0 {
                    nb += 1.0;
                }
                // (Bars are two beats each from the first.)
                let start = 2.0 * (nb - 1.0);
                if self.beat_time_known(start) {
                    s_appear = time_of_beat_index(&self.st.song, start);
                } else {
                    appear_at = Some(start);
                }
            }
            self.st.pending += 1.0;
            match appear_at {
                Some(start) => self.push_at_beat(start, Ev::Appear { kind }),
                None => self.st.q.push(max(s, s_appear), Ev::Appear { kind }),
            }
            k += 1.0;
        }
    }

    pub(super) fn appear(&mut self, kind: usize) {
        self.st.pending -= 1.0;
        // (Never past the cap.)
        if self.st.blasting || self.st.alive.len() as f64 >= POP_CAP {
            return;
        }
        let env = Rc::clone(&self.env);
        let mut r = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("appear"),
            Part::Num(self.st.foes.len() as f64),
        ]);
        let p = self.spawn_point(&env.input.lib.enemies[kind], &mut r, false);
        self.spawn_foe(kind, p.0, p.1, None);
    }

    // -------------------------------------------------------------- beat times

    /// The latest committed beat known by now (-1 before the first).
    #[allow(clippy::manual_midpoint)]
    fn last_known_beat(&self) -> isize {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let s = self.st.s;
        let (mut lo, mut hi) = (0usize, beats.len());
        while lo < hi {
            let mid = (lo + hi) >> 1;
            if beats[mid].known_at <= s + 1e-9 {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        lo as isize - 1
    }

    /// Song time of fractional beat index bi from the beats committed by now (past the last of them,
    /// its period on), so nothing scheduled now reads a beat not yet known.
    pub(super) fn known_time_of_beat(&self, bi: f64) -> f64 {
        let k = self.last_known_beat();
        if k < 1 || ceil(bi - 1e-9) <= k as f64 {
            return time_of_beat_index(&self.st.song, bi);
        }
        let b = self.log.beats[k as usize];
        b.t + (bi - k as f64) * b.period
    }

    /// The beat index of song time t from the beats committed by now.
    pub(super) fn known_beat_at(&self, t: f64) -> f64 {
        let k = self.last_known_beat();
        if k < 1 || t <= self.log.beats[k as usize].t + 1e-9 {
            return self.sbi_at(t);
        }
        let b = self.log.beats[k as usize];
        k as f64 + (t - b.t) / b.period
    }

    /// The first sixteenth note at or after song time t, from the beats known.
    pub(super) fn sixteenth_at_or_after(&self, t: f64) -> f64 {
        if self.st.survivor.is_some() {
            return self
                .known_grid_after(t, 0.25)
                .map_or_else(|| self.free_grid_at_or_after(t, 0.25), |(_, at)| at);
        }
        self.known_time_of_beat(ceil(self.known_beat_at(t) * 4.0 - 1e-6) / 4.0)
    }

    /// The first downbeat at or after song time t (`downbeatAtOrAfter`): its beat index. A downbeat
    /// not known yet lies ahead. (The reactive song map's bars are two beats from the first.)
    pub(super) fn downbeat_index_at_or_after(&self, t: f64) -> usize {
        let mut bar = max(1.0, bar_of_beat_index(&self.st.idx, self.sbi_at(t)));
        loop {
            let k = (2.0 * (bar - 1.0)) as usize;
            match self.st.song.beats.get(k) {
                Some(&b) if b < t - 1e-9 => bar += 1.0,
                _ => return k,
            }
        }
    }

    /// Queues an event on the first downbeat at or after song time t, once that beat is known.
    pub(super) fn push_at_downbeat(&mut self, t: f64, ev: Ev) {
        let k = self.downbeat_index_at_or_after(t);
        self.push_at_beat(k as f64, ev);
    }
}
