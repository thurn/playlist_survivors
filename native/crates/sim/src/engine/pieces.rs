//! The set pieces in play (simulate.ts's formations, rings, stampedes, archers, the boss and the
//! finale blast), the zone waves, landmarks and vacuums. The planner (`plan.rs`) decides them.

use std::rc::Rc;

use super::{
    Ev, FoeSlot, HURTBOX_LIFT, HURTBOX_RADIUS, KNOCK_TIME, Member, POP_CAP, RingPath, SIM_RATE,
    SPAWN_MARGIN, Sim, Spot, VACUUM_AHEAD,
};
use crate::content::types::{FormationShape, ReleaseMode};
use crate::core::geometry::Shape;
use crate::core::rng::{Part, Rng};
use crate::core::shaping::{clamp, lerp};
use crate::core::songmap::time_of_beat_index;
use crate::jsmath::{PI, atan2, ceil, cos, floor, hypot, max, min, pow, round, sin};
use crate::reactive::director::by_level;
use crate::recorder::{
    ArrowRecord, AttackRecord, DanceRecord, FormationRecord, HitSource, LandmarkRecord,
    SlamMarkRecord, SlamRecord, ThreatRecord, VacuumRecord, Weight, ZoneWaveRecord,
};

pub(super) const ARCHER_SHOOT_RANGE: f64 = 420.0;
pub(super) const ARCHER_GAP: f64 = 20.0;
const ARROW_OVERSHOOT: f64 = 260.0;
pub const SLAM_RADIUS: f64 = 28.0;
const BOSS_LAND_AHEAD: f64 = 110.0;
const BOSS_LAND_PUSH: f64 = 120.0;
const BOSS_KILL_STOP: f64 = 0.15;
const LINE_LEAD: f64 = 170.0;
const CHARGE_BEATS: f64 = 2.0;
pub const NEAR_MISS: f64 = 24.0;
pub const RING_CLOSE: f64 = 100.0;
const RING_ROW: f64 = 32.0;
pub const STAMPEDE_SPEED: f64 = 2.5;
pub(super) const STAMPEDE_ROW: f64 = 48.0;
const DIP_RAMP: f64 = 0.04;
const DIP_HOLD: f64 = 0.07;

/// A closing ring as the simulation keeps it: its centre, when it started, when its close starts
/// and ends (infinite until known), its first radius and its gaps' angles.
#[derive(Clone, Debug, PartialEq)]
pub(super) struct RingState {
    pub x: f64,
    pub y: f64,
    pub s0: f64,
    pub k1: f64,
    pub s1: f64,
    pub r0: f64,
    pub gaps: Vec<f64>,
}

impl FoeSlot {
    /// Where a formation member's slot is at song time t, from the formation's anchor.
    pub(super) fn at(&self, sbi_t: f64) -> (f64, f64) {
        let (tx, ty) = self.dir;
        let d = match self.place {
            super::SlotPlace::KickLine { d } => (tx * d, ty * d),
            super::SlotPlace::Wedge { arm, side, spacing } => {
                let cx = tx * 120.0;
                let cy = ty * 120.0 - 70.0;
                (
                    cx - tx * arm * spacing - ty * side * arm * spacing,
                    cy - ty * arm * spacing + tx * side * arm * spacing,
                )
            }
        };
        let u = clamp((sbi_t - self.b_start) / self.entrance_beats, 0.0, 1.0);
        if u >= 1.0 {
            return d;
        }
        let kk = floor(u * self.steps);
        let fr = u * self.steps - kk;
        let e = 1.0 - pow(1.0 - min(1.0, (kk + min(1.0, fr / 0.45)) / self.steps), 2.0);
        let hop = if matches!(self.place, super::SlotPlace::KickLine { .. }) {
            sin(PI * min(1.0, fr / 0.45)) * 3.0
        } else {
            0.0
        };
        (lerp(self.start.0, d.0, e), lerp(self.start.1, d.1, e) - hop)
    }
}

impl Sim {
    /// A set piece's id in the recording: set pieces are numbered as they enter.
    pub(super) fn piece_id(&mut self, m: Member) -> usize {
        if let Some(i) = self.st.piece_ids.iter().position(|x| *x == m) {
            return i;
        }
        self.st.piece_ids.push(m);
        self.st.piece_ids.len() - 1
    }

    /// The camera lead's direction: the heading, or a formation's while it is in play.
    pub(super) fn lead_dir_at(&self, s: f64) -> (f64, f64) {
        match self.st.formation_dir {
            Some(fd) if s < fd.until => (fd.x, fd.y),
            _ => self.st.heading,
        }
    }

    /// Formations ride with the camera-smoothed hero: the camera centre less its lead.
    pub(super) fn formation_anchor(&self) -> (f64, f64, f64, f64) {
        let d = &self.env.director;
        let s = self.st.s;
        let lead = self.by_level(s, &|l| by_level(&d.camera, l).lead);
        let (tx, ty) = self.lead_dir_at(s);
        let cam = &self.st.camera;
        (cam.x - tx * lead, cam.y - ty * lead + 12.0, cam.vx, cam.vy)
    }

    /// Room for a set piece of n under the cap: the stream's enemies furthest from the hero leave the
    /// field. Returns how many of the n fit.
    pub(super) fn make_room(&mut self, n: f64) -> f64 {
        let excess = self.st.alive.len() as f64 + n - POP_CAP;
        if excess > 0.0 {
            let w = self.st.w;
            let hp = self.hero_pose(w);
            let lib = Rc::clone(&self.env);
            let mut far: Vec<usize> = self
                .st
                .alive
                .iter()
                .copied()
                .filter(|&fi| {
                    let f = &self.st.foes[fi];
                    f.alive
                        && f.member.is_none()
                        && lib.input.lib.enemies[f.def].size != crate::content::types::Size::Boss
                })
                .collect();
            let dist = |fi: usize| {
                let (x, y) = self.pos(fi, w);
                hypot(x - hp.x, y - hp.y)
            };
            far.sort_by(|&a, &b| {
                (dist(b) - dist(a))
                    .partial_cmp(&0.0)
                    .unwrap_or(std::cmp::Ordering::Equal)
            });
            for &fi in far.iter().take(excess as usize) {
                let f = &mut self.st.foes[fi];
                f.alive = false;
                f.final_sample = true;
                f.death_w = w;
                self.rec.death(fi, w, true);
            }
            let foes = &self.st.foes;
            self.st.alive.retain(|&i| foes[i].alive);
        }
        max(0.0, min(n, POP_CAP - self.st.alive.len() as f64))
    }

    pub(super) fn formation_enter(&mut self, fi: usize) {
        let env = Rc::clone(&self.env);
        let fp = self.st.plan.formations[fi].clone();
        let def = &env.input.lib.formations[fp.def.as_str()];
        let view = self.st.camera.view();
        let b_start = self.sbi_at(fp.s_start);
        let step_b = super::combat::subdivision_beats(def.step, 2.0);
        // A formation keeps the heading it entered on, and the camera leads that way while in play.
        let (tx, ty) = self.st.heading;
        self.st.formation_dir = Some(super::FormationDir {
            x: tx,
            y: ty,
            until: f64::INFINITY,
            fi,
        });
        let count = self.make_room(def.count);
        let def_i = env
            .input
            .lib
            .enemies
            .get_index_of(&def.enemy)
            .expect("a known enemy");
        if self.st.formation_members.len() <= fi {
            self.st.formation_members.resize(fi + 1, Vec::new());
        }
        let mut k = 0.0;
        while k < count {
            let a0 = self.formation_anchor();
            let (place, start) = if def.shape == FormationShape::KickLine {
                let d = LINE_LEAD + def.spacing * k;
                let ex = if tx > 1e-6 {
                    (view[2] - a0.0) / tx
                } else if tx < -1e-6 {
                    (view[0] - a0.0) / tx
                } else {
                    f64::INFINITY
                };
                let ey = if ty > 1e-6 {
                    (view[3] - a0.1) / ty
                } else if ty < -1e-6 {
                    (view[1] - a0.1) / ty
                } else {
                    f64::INFINITY
                };
                let out = max(
                    min(ex, ey) + 100.0 + k * 6.0,
                    LINE_LEAD + def.spacing * k + 40.0,
                );
                (super::SlotPlace::KickLine { d }, (tx * out, ty * out))
            } else {
                let arm = if k == 0.0 { 0.0 } else { ceil(k / 2.0) };
                let side = if k == 0.0 {
                    0.0
                } else if k % 2.0 == 0.0 {
                    1.0
                } else {
                    -1.0
                };
                let place = super::SlotPlace::Wedge {
                    arm,
                    side,
                    spacing: def.spacing,
                };
                let cx = tx * 120.0;
                let end_x = cx - tx * arm * def.spacing - ty * side * arm * def.spacing;
                (place, (end_x + 40.0, view[1] - 120.0 - a0.1 + 0.0))
            };
            let slot = FoeSlot {
                place,
                dir: (tx, ty),
                start,
                b_start,
                entrance_beats: def.entrance_beats,
                steps: def.entrance_beats / step_b,
            };
            let p = slot.at(self.sbi_at(self.st.s));
            let id = self.spawn_member(def_i, a0.0 + p.0, a0.1 + p.1, Member::Formation(fi));
            let weight = self.weight_of(&env.input.lib.enemies[def_i], true);
            let f = &mut self.st.foes[id];
            f.weight = weight;
            f.slot = Some(slot);
            f.slot_until = f64::INFINITY;
            self.st.formation_members[fi].push(id);
            k += 1.0;
        }
        // (Reactive mode: its dance starts when the entrance ends, its beat known by then.)
        if def.dance.is_some() {
            self.push_at_beat(b_start + def.entrance_beats, Ev::DanceStart { f: fi });
        }
    }

    pub(super) fn dance_start(&mut self, fi: usize) {
        let env = Rc::clone(&self.env);
        let def = &env.input.lib.formations[self.st.plan.formations[fi].def.as_str()];
        let Some(dance) = &def.dance else { return };
        let s = self.st.s;
        for id in self
            .st
            .formation_members
            .get(fi)
            .cloned()
            .unwrap_or_default()
        {
            if self.st.foes[id].alive {
                self.rec
                    .dance_start(id, s, &dance.animation, dance.alternate_legs);
            }
        }
    }

    pub(super) fn formation_release(&mut self, fi: usize) {
        let env = Rc::clone(&self.env);
        let fp = self.st.plan.formations[fi].clone();
        let def = &env.input.lib.formations[fp.def.as_str()];
        let s = self.st.s;
        let w = self.st.w;
        let w_enter = self.world_of(time_of_beat_index(
            &self.st.song,
            self.sbi_at(fp.s_start) + def.entrance_beats,
        ));
        let beats = &self.st.song.beats;
        let at = |i: f64| {
            if i >= 0.0 && (i as usize) < beats.len() {
                Some(beats[i as usize])
            } else {
                None
            }
        };
        let sb = floor(self.sbi_at(s));
        let next = at(min(beats.len() as f64 - 1.0, sb + 1.0)).unwrap_or(s);
        let beat = next - at(sb).unwrap_or(s);
        let members = self
            .st
            .formation_members
            .get(fi)
            .cloned()
            .unwrap_or_default();
        self.rec.formation(FormationRecord {
            def: fp.def.clone(),
            s_start: fp.s_start,
            s_release: s,
            members: members.iter().map(|&x| x as f64).collect(),
        });
        if self.st.formation_dir.is_some_and(|d| d.fi == fi) {
            self.st
                .formation_dir
                .as_mut()
                .expect("a formation direction")
                .until = s;
        }
        for &id in &members {
            let f = &self.st.foes[id];
            let danced = f.alive || f.death_w > w_enter + 1e-9;
            if let Some(dance) = &def.dance
                && danced
            {
                let w1 = if f.alive { w } else { f.death_w };
                self.rec.dance(
                    id,
                    DanceRecord {
                        w0: w_enter,
                        w1,
                        anim: dance.animation.clone(),
                        alternate: dance.alternate_legs,
                        phase: 0.0,
                    },
                );
            }
            if !self.st.foes[id].alive {
                continue;
            }
            let f = &mut self.st.foes[id];
            f.slot_until = s;
            let mode = match def.release {
                crate::content::types::Release::AfterBeats { mode, .. }
                | crate::content::types::Release::FinalAccentOfPhrase { mode, .. } => mode,
            };
            if mode == ReleaseMode::Charge {
                f.charge_until = s + CHARGE_BEATS * max(0.1, beat);
            }
        }
        if let Some(m) = self.st.formation_members.get_mut(fi) {
            m.clear();
        }
    }

    /// A closing ring: its members evenly spaced on a circle just outside the view around the hero,
    /// with gaps; they hold their radius until the close starts.
    pub(super) fn ring_enter(&mut self, k: usize) {
        let env = Rc::clone(&self.env);
        self.st.plan.rings[k].entered = true;
        let ring = self.st.plan.rings[k].clone();
        let count = self.make_room(ring.count);
        if count == 0.0 {
            return;
        }
        let def_i = env
            .input
            .lib
            .enemies
            .get_index_of(&ring.enemy)
            .expect("a known enemy");
        let def = &env.input.lib.enemies[def_i];
        let hp = self.hero_pose(self.st.w);
        let view = self.st.camera.view();
        let mut far = f64::NEG_INFINITY;
        for x in [view[0], view[2]] {
            for y in [view[1], view[3]] {
                far = max(far, hypot(x - hp.x, y - hp.y));
            }
        }
        let r0 = far + def.radius + SPAWN_MARGIN;
        let mut r = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("ring"),
            Part::Num(k as f64),
        ]);
        let a0 = r.range(0.0, PI * 2.0);
        let rows = if count > 40.0 { 2.0 } else { 1.0 };
        let s0 = self.st.s;
        let mut members = Vec::new();
        let mut gap_angles = Vec::new();
        let weight = self.weight_of(def, true);
        let mut row = 0.0;
        while row < rows {
            let n = if row == 0.0 {
                ceil(count / rows)
            } else {
                floor(count / rows)
            };
            let places = n + 3.0 * ring.gaps;
            let mut gap = std::collections::HashSet::new();
            let mut g = 0.0;
            while g < ring.gaps {
                let first = floor((g * places) / ring.gaps);
                for j in 0..3 {
                    gap.insert((first + f64::from(j)) as i64);
                }
                if row == 0.0 {
                    gap_angles.push(a0 + ((first + 1.0) * PI * 2.0) / places);
                }
                g += 1.0;
            }
            let from = r0 + row * RING_ROW;
            let to = RING_CLOSE + row * RING_ROW;
            let mut j = 0.0;
            while j < places {
                if gap.contains(&(j as i64)) {
                    j += 1.0;
                    continue;
                }
                let th = a0 + (j * PI * 2.0) / places;
                let c = cos(th);
                let sn = sin(th);
                // (It holds its radius until the close starts, after it entered.)
                let id =
                    self.spawn_member(def_i, hp.x + c * from, hp.y + sn * from, Member::Ring(k));
                let f = &mut self.st.foes[id];
                f.weight = weight;
                f.has_at = true;
                f.ring_path = Some(RingPath {
                    x: hp.x,
                    y: hp.y,
                    c,
                    sn,
                    from,
                    to,
                    u: 0.0,
                    du: 0.0,
                });
                f.slot_until = f64::INFINITY;
                members.push(id);
                j += 1.0;
            }
            row += 1.0;
        }
        // Reactive mode: the close's end is known only when it starts.
        self.st.rings.push(RingState {
            x: hp.x,
            y: hp.y,
            s0,
            k1: f64::NAN,
            s1: f64::INFINITY,
            r0,
            gaps: gap_angles,
        });
        self.st.ring_members.push((k, s0, members));
        let seq = self.st.q.reserve();
        self.st.plan.rings[k].close_seq = Some(seq);
        if let Some(k_close) = self.st.plan.rings[k].k_close {
            self.st.q.push_reserved(k_close, Ev::RingClose { k }, seq);
        }
    }

    /// A ring's close starts; it ends on its downbeat, with its vacuum after it.
    pub(super) fn ring_close(&mut self, k: usize) {
        let Some(i) = self.st.ring_members.iter().position(|m| m.0 == k) else {
            return;
        };
        let (_, s0, members) = self.st.ring_members.remove(i);
        if self.st.blasting {
            return;
        }
        let plan = self.st.plan.rings[k].clone();
        let s1 = plan
            .s_close
            .expect("a ring's close end, known when it starts");
        let s = self.st.s;
        if let Some(r) = self.st.rings.iter_mut().rev().find(|x| x.s0 == s0) {
            r.s1 = s1;
            r.k1 = plan.k_close.unwrap_or(s);
        }
        for fi in self.st.alive.clone() {
            let f = &mut self.st.foes[fi];
            if !f.alive || f.member != Some(Member::Ring(k)) {
                continue;
            }
            if let Some(rp) = f.ring_path.as_mut() {
                rp.du = 1.0 / max(1e-3, s1 - s);
                f.slot_until = s1;
            }
        }
        self.push_at_downbeat(s1 + 1e-6, Ev::VacuumDrop);
        self.rec.formation(FormationRecord {
            def: "ring".into(),
            s_start: s0,
            s_release: s1,
            members: members.iter().map(|&x| x as f64).collect(),
        });
    }

    /// The boss, still alive at its retreat, runs off the field away from the hero.
    pub(super) fn boss_retreat(&mut self) {
        let Some(bi) = self.st.boss_foe else { return };
        if !self.st.foes[bi].alive || self.st.blasting {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        let (x, y) = self.pos(bi, self.st.w);
        let (dx, dy) = (x - hp.x, y - hp.y);
        let d = hypot(dx, dy);
        let d = if d == 0.0 || d.is_nan() { 1.0 } else { d };
        let speed = self.env.input.lib.enemies[self.st.foes[bi].def].speed * STAMPEDE_SPEED;
        let f = &mut self.st.foes[bi];
        f.run = Some(((dx / d) * speed, (dy / d) * speed));
        f.retreat = true;
    }

    /// A stampede: enemies enter from one side of the view and run straight across in rows.
    pub(super) fn stampede_enter(&mut self, k: usize) {
        let env = Rc::clone(&self.env);
        let sp = self.st.plan.stampedes[k].clone();
        let count = self.make_room(sp.count);
        if count == 0.0 {
            return;
        }
        let def_i = env
            .input
            .lib
            .enemies
            .get_index_of(&sp.enemy)
            .expect("a known enemy");
        let def = &env.input.lib.enemies[def_i];
        let view = self.st.camera.view();
        let mut r = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("stampede"),
            Part::Num(k as f64),
        ]);
        let dir = if r.next() < 0.5 { 1.0 } else { -1.0 };
        let speed = def.speed * STAMPEDE_SPEED;
        let per_row = ceil(count / sp.rows);
        let places = per_row + 3.0;
        let top = view[1] - def.radius;
        let span = view[3] - view[1] + 2.0 * def.radius;
        let gap_at = floor(r.range(0.2, 0.7) * places);
        let mut left = count;
        let mut members = Vec::new();
        let mut row = 0.0;
        while row < sp.rows && left > 0.0 {
            let x = if dir > 0.0 {
                view[0] - SPAWN_MARGIN - def.radius - row * STAMPEDE_ROW
            } else {
                view[2] + SPAWN_MARGIN + def.radius + row * STAMPEDE_ROW
            };
            let mut j = 0.0;
            while j < places && left > 0.0 {
                if j >= gap_at && j < gap_at + 3.0 {
                    j += 1.0;
                    continue;
                }
                let y = top + (span * (j + 0.5)) / places;
                let id = self.spawn_member(def_i, x, y, Member::Stampede(k));
                let f = &mut self.st.foes[id];
                f.run = Some((dir * speed, 0.0));
                f.vx = dir * speed;
                members.push(id);
                left -= 1.0;
                j += 1.0;
            }
            row += 1.0;
        }
        self.rec.formation(FormationRecord {
            def: "stampede".into(),
            s_start: self.st.s,
            s_release: self.st.s,
            members: members.iter().map(|&x| x as f64).collect(),
        });
        self.st.stampedes.push((k, false));
    }

    // -------------------------------------------------------------- archers

    pub(super) fn spawn_archers(&mut self, group: usize, n: f64) {
        let env = Rc::clone(&self.env);
        let def_i = env
            .input
            .lib
            .enemies
            .get_index_of("skeleton-archer")
            .expect("the archer");
        let mut r = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("archers"),
            Part::Num(group as f64),
            Part::Num(self.st.foes.len() as f64),
        ]);
        let p = self.spawn_point(&env.input.lib.enemies[def_i], &mut r, true);
        let hp = self.hero_pose(self.st.w);
        let (dx, dy) = (p.0 - hp.x, p.1 - hp.y);
        let dl = hypot(dx, dy);
        let dl = if dl == 0.0 || dl.is_nan() { 1.0 } else { dl };
        let taken: Vec<usize> = self
            .st
            .alive
            .iter()
            .filter_map(|&fi| {
                let f = &self.st.foes[fi];
                (f.alive && f.archer_group == Some(group))
                    .then_some(f.archer_slot)
                    .flatten()
            })
            .collect();
        let (mut k, mut placed) = (0usize, 0.0);
        while placed < n && k < 16 {
            if taken.contains(&k) {
                k += 1;
                continue;
            }
            let off = (k as f64 - 0.5) * ARCHER_GAP;
            let id = self.spawn_foe(def_i, p.0 + (-dy / dl) * off, p.1 + (dx / dl) * off, None);
            let f = &mut self.st.foes[id];
            f.archer_group = Some(group);
            f.archer_slot = Some(k);
            placed += 1.0;
            k += 1;
        }
    }

    pub(super) fn draw(&mut self, v: usize) {
        let vol = self.st.plan.volleys[v];
        let w = self.st.w;
        let hp = self.hero_pose(w);
        let group: Vec<usize> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&fi| {
                self.st.foes[fi].alive && self.st.foes[fi].archer_group == Some(vol.group)
            })
            .collect();
        for &fi in &group {
            let (x, y) = self.pos(fi, w);
            if hypot(x - hp.x, y - hp.y) > ARCHER_SHOOT_RANGE {
                continue;
            }
            self.st.foes[fi].drawing = Some(w);
            self.rec.draw_start(
                fi,
                w,
                vol.s_release,
                atan2(hp.y - HURTBOX_LIFT - y + 24.0, hp.x - x),
            );
        }
        // Replacements for fallen archers walk in for the next volley.
        if (group.len() as f64) < vol.archers {
            self.spawn_archers(vol.group, vol.archers - group.len() as f64);
        }
    }

    pub(super) fn loose(&mut self, v: usize) {
        let vol = self.st.plan.volleys[v];
        let (s, w) = (self.st.s, self.st.w);
        let hp = self.hero_pose(w);
        // Each arrow aims at the hero's position at the moment of release.
        let ax = hp.x;
        let ay = hp.y - HURTBOX_LIFT;
        for fi in self.st.alive.clone() {
            let f = &self.st.foes[fi];
            let Some(w0) = f
                .drawing
                .filter(|_| f.alive && f.archer_group == Some(vol.group))
            else {
                continue;
            };
            let (x, y) = self.pos(fi, w);
            let (x0, y0) = (x, y - 24.0);
            let dl = hypot(ax - x0, ay - y0);
            let dl = if dl == 0.0 || dl.is_nan() { 1.0 } else { dl };
            self.rec.arrow(ArrowRecord {
                s_release: s,
                w_release: w,
                s_arrive: vol.s_arrive,
                w_arrive: 0.0,
                x0,
                y0,
                ax,
                ay,
                x1: ax,
                y1: ay,
                w1: 0.0,
                outcome: "miss",
            });
            let index = self.st.n_arrows;
            self.st.n_arrows += 1;
            let damage = self.env.input.lib.enemies[self.st.foes[fi].def].attack_damage;
            self.st.flying.push(super::Flying {
                v,
                index,
                x0,
                y0,
                ax,
                ay,
                w_release: w,
                ux: (ax - x0) / dl,
                uy: (ay - y0) / dl,
                damage,
            });
            self.rec.attack(
                fi,
                AttackRecord {
                    w0,
                    w,
                    kind: "shoot",
                    dir: atan2(ay - y0, ax - x0),
                    provisional: None,
                },
            );
            self.st.foes[fi].drawing = None;
        }
    }

    pub(super) fn arrive(&mut self, v: usize) {
        let (s, w) = (self.st.s, self.st.w);
        let hp = self.hero_pose(w);
        let hx0 = hp.x;
        let hy0 = hp.y - HURTBOX_LIFT;
        let mut i = self.st.flying.len();
        while i > 0 {
            i -= 1;
            if self.st.flying[i].v != v {
                continue;
            }
            let a = self.st.flying.remove(i);
            let t = (hx0 - a.x0) * a.ux + (hy0 - a.y0) * a.uy;
            let perp = ((hx0 - a.x0) * a.uy - (hy0 - a.y0) * a.ux).abs();
            let edge = perp - HURTBOX_RADIUS;
            let outcome = if t < 0.0 {
                "miss"
            } else if edge <= 0.0 {
                "touch"
            } else if edge <= NEAR_MISS {
                "near-miss"
            } else {
                "miss"
            };
            let len = hypot(a.ax - a.x0, a.ay - a.y0);
            let speed = len / max(1e-3, w - a.w_release);
            if outcome == "touch" {
                self.hurt(a.damage, "arrow", "skeleton-archer");
                self.rec.arrow_end(
                    a.index,
                    w,
                    outcome,
                    a.x0 + a.ux * t,
                    a.y0 + a.uy * t,
                    a.w_release + t / speed,
                );
            } else {
                self.rec.arrow_end(
                    a.index,
                    w,
                    outcome,
                    a.ax + a.ux * ARROW_OVERSHOOT,
                    a.ay + a.uy * ARROW_OVERSHOOT,
                    w + ARROW_OVERSHOOT / speed,
                );
            }
            self.rec.threat(ThreatRecord {
                s,
                w,
                kind: "arrow",
                outcome,
                x: hp.x,
                y: hp.y,
            });
            if outcome == "near-miss" {
                self.near_miss();
            }
        }
    }

    /// A near miss: the slow-motion dip starts now (dips do not stack).
    fn near_miss(&mut self) {
        if self.st.s < self.st.dip_start + 2.0 * DIP_RAMP + DIP_HOLD {
            return;
        }
        self.st.dip_start = self.st.s;
    }

    // -------------------------------------------------------------- the boss

    pub(super) fn boss_spawn(&mut self) {
        let env = Rc::clone(&self.env);
        let b = self.st.plan.boss.clone().expect("a boss");
        let hp = self.hero_pose(self.st.w);
        let (tx, ty) = self.st.heading;
        let x = hp.x + tx * BOSS_LAND_AHEAD;
        let y = hp.y + ty * BOSS_LAND_AHEAD;
        self.make_room(1.0);
        let def_i = env
            .input
            .lib
            .enemies
            .get_index_of(&b.kind)
            .expect("a known boss");
        let id = self.spawn_entrance(def_i, x, y);
        self.st.foes[id].land_s = Some(b.s_enter);
        self.st.boss_foe = Some(id);
        self.rec.boss(id, b.s_enter, x, y, self.st.s);
    }

    pub(super) fn boss_land(&mut self) {
        let Some(bi) = self.st.boss_foe else { return };
        if !self.st.foes[bi].alive {
            return;
        }
        let w = self.st.w;
        // (Reactive mode: one dropping in as the finale comes leaves with the blast.)
        if self.st.blasting {
            let f = &mut self.st.foes[bi];
            f.alive = false;
            f.final_sample = true;
            f.death_w = w;
            self.rec.death(bi, w, true);
            let foes = &self.st.foes;
            self.st.alive.retain(|&i| foes[i].alive);
            return;
        }
        let (bx, by) = (self.st.foes[bi].x, self.st.foes[bi].y);
        for fi in self.st.alive.clone() {
            if fi == bi || !self.st.foes[fi].alive || self.pinned(fi) {
                continue;
            }
            let (x, y) = self.pos(fi, w);
            let (dx, dy) = (x - bx, y - by);
            let d = hypot(dx, dy);
            if d >= BOSS_LAND_PUSH || d < 1e-6 {
                continue;
            }
            let f = &mut self.st.foes[fi];
            f.knock_x = (dx / d) * (BOSS_LAND_PUSH - d) * 0.5;
            f.knock_y = (dy / d) * (BOSS_LAND_PUSH - d) * 0.5;
            f.knock_t = KNOCK_TIME;
        }
    }

    pub(super) fn slam_mark(&mut self, k: usize) {
        let Some(bi) = self.st.boss_foe else { return };
        if !self.st.foes[bi].alive {
            return;
        }
        let (s, w) = (self.st.s, self.st.w);
        let hp = self.hero_pose(w);
        self.st.slam_marks.push((k, hp.x, hp.y, s, w));
        let (bx, by) = self.pos(bi, w);
        let s_land = self.st.plan.boss.as_ref().expect("a boss").slams[k].s_land;
        self.rec.slam_mark(
            k,
            SlamMarkRecord {
                x: hp.x,
                y: hp.y,
                s_mark: s,
                w_mark: w,
                s_land,
                boss: bi,
                dir: atan2(hp.y - by, hp.x - bx),
            },
        );
    }

    pub(super) fn slam_land(&mut self, k: usize) {
        let Some(i) = self.st.slam_marks.iter().position(|m| m.0 == k) else {
            return;
        };
        let Some(bi) = self.st.boss_foe else { return };
        if !self.st.foes[bi].alive {
            return;
        }
        let (_, mx, my, s_mark, w_mark) = self.st.slam_marks.remove(i);
        let (s, w) = (self.st.s, self.st.w);
        let hp = self.hero_pose(w);
        // The marker appears under the hero: leaving it before the slam lands is a near miss.
        let edge = hypot(hp.x - mx, hp.y - my) - SLAM_RADIUS - HURTBOX_RADIUS;
        let outcome = if edge <= 0.0 { "touch" } else { "near-miss" };
        self.rec.slam(SlamRecord {
            s_mark,
            w_mark,
            s_land: s,
            w_land: w,
            x: mx,
            y: my,
            radius: SLAM_RADIUS,
            outcome,
        });
        let (bx, by) = self.pos(bi, w);
        self.rec.attack(
            bi,
            AttackRecord {
                w0: max(w_mark, w - 0.6),
                w,
                kind: "slam",
                dir: atan2(my - by, mx - bx),
                provisional: None,
            },
        );
        self.rec.threat(ThreatRecord {
            s,
            w,
            kind: "slam",
            outcome,
            x: hp.x,
            y: hp.y,
        });
        if outcome == "touch" {
            let def = &self.env.input.lib.enemies[self.st.foes[bi].def];
            let (dmg, kind) = (def.attack_damage, def.id.clone());
            self.hurt(dmg, "slam", &kind);
        } else {
            self.near_miss();
        }
    }

    /// The boss died to hit `hit` (its slams marked never land).
    pub(super) fn boss_died(&mut self, hit: f64, source: HitSource) {
        self.rec.boss_death(self.st.s, hit);
        self.st.slam_marks.clear();
        if source != HitSource::Ring {
            self.start_hit_stop(BOSS_KILL_STOP);
        }
    }

    // -------------------------------------------------------------- the finale

    pub(super) fn blast_band(&mut self, band: usize) {
        let env = Rc::clone(&self.env);
        let fin = self.st.plan.finale.clone().expect("the finale");
        let wd = &env.input.lib.weapons[fin.blast.as_str()];
        let b = wd.blast.as_ref().expect("a blast");
        let bands = max(
            1.0,
            round(b.beats / super::combat::subdivision_beats(b.step, 2.0)),
        );
        let hp = self.hero_pose(self.st.w);
        let last = band as f64 == bands - 1.0;
        let outer = if last {
            1e6
        } else {
            (b.radius * (band as f64 + 1.0)) / bands
        };
        let spec = super::combat::HitSpec {
            source: HitSource::Ring,
            what: wd.id.clone(),
            weight: Weight::Heavy,
            primary: false,
            shape: Shape::Ring {
                x: hp.x,
                y: hp.y,
                inner: 0.0,
                outer,
            },
            dir: 0.0,
            accent: 0.0,
            damage: wd.damage,
            group: None,
        };
        let h = self.add_hit(spec, Some(1.0));
        self.apply_hit(&h, None);
        if last {
            // Nothing survives the final band, wherever it stands.
            for fi in self.st.alive.clone() {
                if self.st.foes[fi].alive {
                    if self.st.survivor.is_some() {
                        let mut lethal = h.clone();
                        lethal.damage = lethal.damage.max(self.st.foes[fi].health);
                        self.damage(fi, &lethal);
                    } else {
                        self.damage(fi, &h);
                    }
                }
            }
            let foes = &self.st.foes;
            self.st.alive.retain(|&i| foes[i].alive);
        }
    }

    // -------------------------------------------------------------- the open field

    pub(super) fn zone_wave(&mut self, zone: usize, section: Option<usize>) {
        let hp = self.hero_pose(self.st.w);
        let id = self.env.input.level.zones[zone].clone();
        self.rec.zone_wave(ZoneWaveRecord {
            s: self.st.s,
            w: self.st.w,
            x: hp.x,
            y: hp.y,
            zone: id,
            section: section.map(|x| x as f64),
        });
        // A detected section starts: the roster steps, and a level-up whose experience is reached
        // falls due now.
        if let Some(section) = section {
            self.st.section = section;
            if self.st.level_ready {
                self.grant();
            }
        }
    }

    pub(super) fn landmark(&mut self, id: &str) {
        let hp = self.hero_pose(self.st.w);
        self.rec.landmark(LandmarkRecord {
            id: id.to_string(),
            s: self.st.s,
            w: self.st.w,
            x: hp.x,
            y: hp.y,
        });
    }

    /// A vacuum drops VACUUM_AHEAD along the hero's heading.
    pub(super) fn vacuum_drop(&mut self) {
        let hp = self.hero_pose(self.st.w);
        let x = hp.x + self.st.heading.0 * VACUUM_AHEAD;
        let y = hp.y + self.st.heading.1 * VACUUM_AHEAD;
        let i = self.st.n_vacuums;
        self.st.n_vacuums += 1;
        self.rec.vacuum(VacuumRecord {
            s: self.st.s,
            w: self.st.w,
            x,
            y,
            s_touch: None,
            w_touch: None,
            gems: None,
        });
        self.st.vacuums.push(Spot { i, x, y });
    }

    /// Set pieces' members on a fixed step before the others move: rings on their circles, formation
    /// members on their slots. True if the member's motion is done.
    pub(super) fn move_pinned(&mut self, fi: usize, w1: f64) -> bool {
        let s = self.st.s;
        let f = &self.st.foes[fi];
        let dt = w1 - f.last_w;
        if f.ring_path.is_some() && s < f.slot_until {
            let slot_until = f.slot_until;
            let f = &mut self.st.foes[fi];
            let rp = f.ring_path.as_mut().expect("a ring path");
            // (Its pace re-solved each step for what is left of the close by its end in song time.)
            if rp.du > 0.0 {
                rp.du = (1.0 - rp.u) / max(1.0 / SIM_RATE, slot_until - s);
            }
            rp.u = min(1.0, rp.u + dt * rp.du);
            let rad = lerp(rp.from, rp.to, rp.u);
            let (x, y) = (rp.x + rp.c * rad, rp.y + rp.sn * rad);
            let v = if rp.u < 1.0 {
                rp.du * (rp.to - rp.from)
            } else {
                0.0
            };
            let (vx, vy) = (rp.c * v, rp.sn * v);
            f.x = x;
            f.y = y;
            f.vx = vx;
            f.vy = vy;
            return true;
        }
        if let Some(slot) = f.slot.filter(|_| s < f.slot_until) {
            let a = self.formation_anchor();
            let p = slot.at(self.sbi_at(s));
            let rate = self.rate_at(s);
            let q2 = slot.at(self.sbi_at(s + 1.0 / SIM_RATE));
            let f = &mut self.st.foes[fi];
            f.vx = a.2
                + if rate > 0.0 {
                    ((q2.0 - p.0) * SIM_RATE) / rate
                } else {
                    0.0
                };
            f.vy = a.3
                + if rate > 0.0 {
                    ((q2.1 - p.1) * SIM_RATE) / rate
                } else {
                    0.0
                };
            f.x = a.0 + p.0;
            f.y = a.1 + p.1;
            return true;
        }
        false
    }
}
