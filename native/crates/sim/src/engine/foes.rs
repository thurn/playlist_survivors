//! Enemies: spawning by the director, beat steps, motion and separation, samples and recycling
//! (simulate.ts's enemies, motion and spawning sections), and the beat-time helpers they use.

use std::rc::Rc;

use super::{
    BITE_SLACK, CALM_CURL, CELL, CHARGE_SPEED, CONTACT_GAP, ENEMY_RATE, Ev, Foe, FoeStep, G,
    HURTBOX_RADIUS, KNOCK_TIME, MIN_GAP, POP_CAP, RECYCLE_VIEWS, SEPARATION, SIM_RATE, SPAWN_EXTRA,
    SPAWN_FORWARD, SPAWN_MARGIN, SPAWN_PER_BEAT, STEP_SEEN, STREAM_SHARE, Sim,
};
use crate::content::types::{Behavior, EnemyDef, Gait, Subdivision};
use crate::core::rng::{Part, Rng};
use crate::core::shaping::{beat_hit, clamp};
use crate::core::songmap::{bar_of_beat_index, time_of_beat_index};
use crate::gait::{gait_beat, gait_ground, gait_slot, gait_step_after, gait_step_at_or_before};
use crate::jsmath::{PI, atan2, ceil, cos, floor, hypot, max, min, pow, round, sin, sqrt};
use crate::movement::ease_out;
use crate::reactive::director::{by_level, director_population, section_roster};
use crate::recorder::EnemyRecord;
use crate::upgrades::armoured_bite;

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
    let big = matches!(def.atlas.as_str(), "slime" | "bat" | "bee" | "eyeball");
    let s = def.scale;
    if big {
        (16.0 * s, (32.0 + def.hover) * s, 6.0 * s)
    } else {
        (32.0 * s, (58.0 + def.hover) * s, 8.0 * s)
    }
}

fn group_period(group: Subdivision, bpb: f64) -> Option<f64> {
    match group {
        Subdivision::Eighth => Some(0.5),
        Subdivision::Beat => Some(1.0),
        Subdivision::Bar => Some(bpb),
        Subdivision::TwoBar => Some(2.0 * bpb),
        Subdivision::Sixteenth => None,
    }
}

impl Sim {
    pub(super) fn pos(&self, fi: usize, at_w: f64) -> (f64, f64) {
        let f = &self.st.foes[fi];
        (
            f.x + f.vx * (at_w - f.last_w),
            f.y + f.vy * (at_w - f.last_w),
        )
    }

    /// Can this enemy be hit now? (The boss is out of reach while it drops in, B6.)
    pub(super) fn hittable(&self, fi: usize) -> bool {
        let f = &self.st.foes[fi];
        f.alive && f.land_s.is_none_or(|l| self.st.s >= l - 1e-9)
    }

    /// The boss dropping in holds its place (formations and rings join with B6).
    pub(super) fn pinned(&self, fi: usize) -> bool {
        self.st.foes[fi].land_s.is_some_and(|l| self.st.s < l)
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
        let env = Rc::clone(&self.env);
        let def = &env.input.lib.enemies[def_i];
        let id = self.st.foes.len();
        let h0 = health.unwrap_or(def.health);
        // A stepping enemy takes its first step on its next step's gait time.
        let step_k = def
            .gait
            .as_ref()
            .map(|g| gait_slot(g, self.known_beat_at(self.st.s)));
        let weight = self.weight_of(def, false);
        self.st.foes.push(Foe {
            id,
            def: def_i,
            r: def.radius,
            health: h0,
            x,
            y,
            vx: 0.0,
            vy: 0.0,
            last_w: self.st.w,
            alive: true,
            final_sample: false,
            death_w: f64::INFINITY,
            last_fate: None,
            formation: None,
            sx: 0.0,
            sy: 0.0,
            knock_x: 0.0,
            knock_y: 0.0,
            knock_t: 0.0,
            weight,
            charge_until: f64::NEG_INFINITY,
            land_s: None,
            step_k,
            step: None,
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
            formation: None,
            entrance: None,
            steps: None,
        });
        self.rec.sample(id, x, y);
        self.st.alive.push(id);
        self.st.foe_samples.push(id);
        id
    }

    /// The way forward now: the hero and its heading.
    fn forward(&self) -> (f64, f64, f64, f64) {
        let hp = self.hero_pose(self.st.w);
        (hp.x, hp.y, self.st.heading.0, self.st.heading.1)
    }

    fn spawn_point(&self, def: &EnemyDef, r: &mut Rng, forward_only: bool) -> (f64, f64) {
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

    /// Beat steps: whether the enemy is due a step at beat index b, song time s, and its ground and
    /// duration (`stepDue`).
    fn step_due(&mut self, fi: usize, gait: &Gait, b: f64, s: f64) -> Option<(f64, f64)> {
        let k = gait_slot(gait, b);
        let f = &mut self.st.foes[fi];
        let joins = f.step_k.is_none();
        let due = joins
            || (k > f.step_k.unwrap_or(f64::NAN)
                && gait_step_after(gait, f.step_k.unwrap_or(0.0)) <= k);
        f.step_k = Some(if joins {
            k
        } else {
            max(f.step_k.unwrap_or(f64::NAN), k)
        });
        if !due {
            return None;
        }
        let kk = if joins {
            k
        } else {
            gait_step_at_or_before(gait, k)
        };
        let t0 = self.known_time_of_beat(gait_beat(gait, kk));
        let r = self.base_rate(s);
        let ground = r * if joins {
            self.known_time_of_beat(gait_beat(gait, gait_step_after(gait, k))) - s
        } else {
            self.known_time_of_beat(gait_beat(gait, kk + gait_ground(gait))) - t0
        };
        let dur = min(
            ground,
            r * gait.share * (self.known_time_of_beat(gait_beat(gait, kk + 1.0)) - t0),
        );
        Some((ground, dur))
    }

    fn start_step(&mut self, fi: usize, w: f64, dx: f64, dy: f64, due: (f64, f64)) {
        let dur = max(1.0 / SIM_RATE, due.1);
        self.st.foes[fi].step = Some(FoeStep {
            w0: w,
            dur,
            dx,
            dy,
            span: due.0,
        });
        if hypot(dx, dy) >= STEP_SEEN {
            self.rec.enemy_step(fi, w, dur);
        }
    }

    fn stop_stepping(&mut self, fi: usize, w: f64) {
        if self.st.foes[fi].step_k.is_some() {
            self.rec.enemy_step(fi, w, 0.0);
        }
        let f = &mut self.st.foes[fi];
        f.step_k = None;
        f.step = None;
    }

    fn step_velocity(&mut self, fi: usize, w: f64) {
        let f = &mut self.st.foes[fi];
        let Some(g) = f.step else {
            f.vx = 0.0;
            f.vy = 0.0;
            return;
        };
        let u = (w - g.w0) / g.dur;
        if u >= 1.0 {
            f.vx = 0.0;
            f.vy = 0.0;
            return;
        }
        let slope = (3.0 * pow(1.0 - u, 2.0)) / g.dur;
        f.vx = (g.dx + f.sx * g.span) * slope;
        f.vy = (g.dy + f.sy * g.span) * slope;
    }

    /// Integrates every enemy to world time w1 (a fixed step).
    #[allow(clippy::too_many_lines)] // (one TS function)
    pub(super) fn move_foes(&mut self, w1: f64) {
        let env = Rc::clone(&self.env);
        let lib = &env.input.lib;
        let hp = self.hero_pose(w1);
        let s = self.st.s;
        // A calm section slows the enemies and curls their approach (the loudness level's calm, blended).
        let d = &env.director;
        let calm = self.by_level(s, &|l| by_level(&d.calm, l));
        let calm_speed = 1.0 - calm;
        let curl_cos = cos(calm * CALM_CURL);
        let curl_sin = sin(calm * CALM_CURL);
        let sbi = self.sbi_at(s);
        let idx = &self.st.idx;
        let bar = max(1.0, bar_of_beat_index(idx, sbi));
        let bpb = get_or(&idx.beats_per_bar, min(idx.bar_count, bar), 2.0);
        let bar_start = get_or(&idx.bar_start, min(idx.bar_count, bar), 0.0);
        let mult = |g: Subdivision| -> f64 {
            match group_period(g, bpb) {
                Some(period) => {
                    let ph = ((sbi - bar_start) / period) % 1.0;
                    let ph = if ph < 0.0 { ph + 1.0 } else { ph };
                    (0.6 + 0.8 * beat_hit(ph)) / 0.733
                }
                None => 1.0,
            }
        };
        // Beat steps need a steady beat: enemies with a gait glide while the beat is unclear.
        let stepping = self.clear_at(s);
        let step_b = if stepping { self.known_beat_at(s) } else { 0.0 };
        let health = self.st.health > 0.0;
        let alive = self.st.alive.clone();
        // Bring everyone to this step's time first.
        for &fi in &alive {
            let f = &mut self.st.foes[fi];
            let dt = w1 - f.last_w;
            if f.land_s.is_some_and(|l| s < l) {
                f.vx = 0.0;
                f.vy = 0.0;
                continue;
            }
            if let Some(g) = f.step {
                let de = ease_out(clamp((w1 - g.w0) / g.dur, 0.0, 1.0))
                    - ease_out(clamp((f.last_w - g.w0) / g.dur, 0.0, 1.0));
                f.x += (g.dx + f.sx * g.span) * de;
                f.y += (g.dy + f.sy * g.span) * de;
                continue;
            }
            f.x += f.vx * dt;
            f.y += f.vy * dt;
        }
        {
            self.st.generation += 1;
            let generation = self.st.generation;
            let gox = floor(hp.x / CELL) as i64 - G / 2;
            let goy = floor(hp.y / CELL) as i64 - G / 2;
            if self.st.next_arr.len() < alive.len() {
                self.st.next_arr = vec![0; alive.len() * 2];
            }
            let cell_of = |x: f64, gox: i64| floor(x / CELL) as i64 - gox;
            for (i, &fi) in alive.iter().enumerate() {
                let f = &mut self.st.foes[fi];
                f.sx = 0.0;
                f.sy = 0.0;
                let ix = cell_of(f.x, gox);
                let iy = cell_of(f.y, goy);
                if ix < 0 || iy < 0 || ix >= G || iy >= G {
                    self.st.next_arr[i] = -2;
                    continue;
                }
                let c = (iy * G + ix) as usize;
                if self.st.stamp[c] != generation {
                    self.st.stamp[c] = generation;
                    self.st.head[c] = -1;
                }
                self.st.next_arr[i] = self.st.head[c];
                self.st.head[c] = i as i32;
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
                        if self.st.stamp[c] != generation {
                            continue;
                        }
                        let mut j = self.st.head[c];
                        while j >= 0 {
                            let ju = j as usize;
                            let next = self.st.next_arr[ju];
                            if ju <= i {
                                j = next;
                                continue;
                            }
                            let (ai, bi) = (alive[i], alive[ju]);
                            let (a, b) = (&self.st.foes[ai], &self.st.foes[bi]);
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
                            let pa = self.pinned(ai);
                            let pb = self.pinned(bi);
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
                                let a = &mut self.st.foes[ai];
                                a.sx += ux * k * 2.0 * wa;
                                a.sy += uy * k * 2.0 * wa;
                            }
                            {
                                let b = &mut self.st.foes[bi];
                                b.sx -= ux * k * 2.0 * (1.0 - wa);
                                b.sy -= uy * k * 2.0 * (1.0 - wa);
                            }
                            if dd < gap {
                                let push = gap - dd;
                                let a = &mut self.st.foes[ai];
                                a.x += ux * push * wa;
                                a.y += uy * push * wa;
                                let b = &mut self.st.foes[bi];
                                b.x -= ux * push * (1.0 - wa);
                                b.y -= uy * push * (1.0 - wa);
                            }
                            j = next;
                        }
                    }
                }
            }
        }
        for &fi in &alive {
            let dt = {
                let f = &mut self.st.foes[fi];
                let dt = w1 - f.last_w;
                f.last_w = w1;
                dt
            };
            if self.pinned(fi) {
                self.stop_stepping(fi, w1);
                continue;
            }
            let def = &lib.enemies[self.st.foes[fi].def];
            let (fx, fy, fr, fid, charge_until) = {
                let f = &self.st.foes[fi];
                (f.x, f.y, f.r, f.id, f.charge_until)
            };
            let k = min(1.0, dt * 12.0);
            let dx = hp.x - fx;
            let dy = hp.y - fy;
            let dd = hypot(dx, dy);
            let dd = if dd == 0.0 || dd.is_nan() { 0.01 } else { dd };
            let contact = HURTBOX_RADIUS + fr + CONTACT_GAP;
            let charge = if s < charge_until { CHARGE_SPEED } else { 1.0 };
            // The curl turns half the enemies clockwise and half counterclockwise.
            let cs = if fid % 2 == 0 { curl_sin } else { -curl_sin };
            match &def.gait {
                Some(gait) if stepping => {
                    // A beat step on each gait time, aimed where the hero is then.
                    if let Some(due) = self.step_due(fi, gait, step_b, s) {
                        let reach = def.speed * calm_speed * due.0;
                        let len = if def.behavior == Behavior::Archer {
                            clamp(dd - super::ARCHER_RANGE, -0.5 * reach, reach)
                        } else {
                            clamp(dd - contact, 0.0, reach * charge)
                        };
                        self.start_step(
                            fi,
                            w1,
                            ((dx * curl_cos - dy * cs) / dd) * len,
                            ((dy * curl_cos + dx * cs) / dd) * len,
                            due,
                        );
                    }
                    self.step_velocity(fi, w1);
                }
                _ => {
                    self.stop_stepping(fi, w1);
                    let m = mult(def.group) * calm_speed;
                    // Archers keep their distance; everyone else closes to contact.
                    let speed = if def.behavior == Behavior::Archer {
                        def.speed * m * clamp((dd - super::ARCHER_RANGE) / 30.0, -0.5, 1.0)
                    } else {
                        def.speed * m * charge * clamp((dd - contact) / 16.0, 0.0, 1.0)
                    };
                    let f = &mut self.st.foes[fi];
                    let tvx = ((dx * curl_cos - dy * cs) / dd) * speed + f.sx;
                    let tvy = ((dy * curl_cos + dx * cs) / dd) * speed + f.sy;
                    f.vx += (tvx - f.vx) * k;
                    f.vy += (tvy - f.vy) * k;
                }
            }
            let f = &mut self.st.foes[fi];
            if f.knock_t > 0.0 {
                let used = min(dt, f.knock_t);
                f.x += (f.knock_x * used) / KNOCK_TIME;
                f.y += (f.knock_y * used) / KNOCK_TIME;
                f.knock_t -= used;
            }
            // The hero passes through the crowd: enemies are pushed aside.
            let min_d = HURTBOX_RADIUS + f.r;
            let ex = f.x - hp.x;
            let ey = f.y - hp.y;
            let ed = hypot(ex, ey);
            if ed < min_d {
                let ux = if ed > 0.01 { ex / ed } else { 1.0 };
                let uy = if ed > 0.01 { ey / ed } else { 0.0 };
                f.x = hp.x + ux * min_d;
                f.y = hp.y + uy * min_d;
            }
            // An enemy at contact bites whenever the hero isn't invulnerable.
            if health
                && def.contact_damage > 0.0
                && max(ed, min_d) <= min_d + CONTACT_GAP + BITE_SLACK
                && self.st.w >= self.st.invuln_until - 1e-9
            {
                let amount = armoured_bite(def.contact_damage, self.st.bonus.armour);
                self.hurt(amount, "bite", &def.id);
            }
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
                self.st.foes[fi].alive && hypot(x - hp.x, y - hp.y) >= RECYCLE_VIEWS * vw
            })
            .collect();
        let env = Rc::clone(&self.env);
        for fi in behind {
            let f = &mut self.st.foes[fi];
            f.alive = false;
            f.final_sample = true;
            f.death_w = w;
            let (def_i, health, id) = (f.def, f.health, f.id);
            self.rec.death(fi, w, true);
            let mut r = Rng::new(&[
                Part::Num(env.input.seed),
                Part::Str("recycle"),
                Part::Num(id as f64),
            ]);
            let p2 = self.spawn_point(&env.input.lib.enemies[def_i], &mut r, true);
            self.spawn_foe(def_i, p2.0, p2.1, Some(health));
        }
        let foes = &self.st.foes;
        self.st.alive.retain(|&i| foes[i].alive);
    }

    /// The director's population at song time s, scaled by the loudness level.
    fn director_target(&self, s: f64) -> f64 {
        let d = &self.env.director;
        director_population(
            d,
            s,
            self.by_level(s, &|l| by_level(&d.population.level_scale, l)),
        )
    }

    pub(super) fn spawn_director(&mut self, bi: f64) {
        if self.st.blasting {
            return;
        }
        let env = Rc::clone(&self.env);
        let lib = &env.input.lib;
        let s = self.st.s;
        let target = self.director_target(s) * STREAM_SHARE;
        let all = self.st.alive.len() as f64 + self.st.pending;
        let living = self
            .st
            .alive
            .iter()
            .filter(|&&fi| self.st.foes[fi].formation.is_none())
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
                // (The whole stream's bars, two beats each from the first.)
                let total = self.total_beats();
                let bar_count = if total == usize::MAX {
                    f64::INFINITY
                } else {
                    ceil(total as f64 / 2.0)
                };
                let start = 2.0 * (min(bar_count, nb) - 1.0);
                if start < total as f64 {
                    if self.beat_time_known(start) {
                        s_appear = time_of_beat_index(&self.st.song, start);
                    } else {
                        appear_at = Some(start);
                    }
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
        self.known_time_of_beat(ceil(self.known_beat_at(t) * 4.0 - 1e-6) / 4.0)
    }

    /// The first downbeat at or after song time t as the whole stream gives it (`downbeatAtOrAfter`):
    /// its beat index, or None past the stream's last bar (TS then takes t itself). A downbeat not
    /// known yet lies ahead. (The reactive song map's bars are two beats from the first.)
    fn downbeat_index_at_or_after(&self, t: f64) -> Option<usize> {
        let mut bar = max(1.0, bar_of_beat_index(&self.st.idx, self.sbi_at(t)));
        loop {
            let k = (2.0 * (bar - 1.0)) as usize;
            if k >= self.total_beats() {
                return None;
            }
            match self.st.song.beats.get(k) {
                Some(&b) if b < t - 1e-9 => bar += 1.0,
                _ => return Some(k),
            }
        }
    }

    /// Queues an event on the first downbeat at or after song time t (t itself past the last bar).
    pub(super) fn push_at_downbeat(&mut self, t: f64, ev: Ev) {
        match self.downbeat_index_at_or_after(t) {
            Some(k) => self.push_at_beat(k as f64, ev),
            None => self.st.q.push(t, ev),
        }
    }
}
