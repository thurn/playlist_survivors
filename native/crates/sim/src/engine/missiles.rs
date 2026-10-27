//! Homing is a demo adaptation; source speed/radius/pierce and burst cadence stay data-driven.
use super::{Sim, combat::HitSpec};
use crate::{
    core::geometry::Shape,
    recorder::{HitSource, Weight},
    survivors::{MissilePose, Shot},
};
use std::collections::BTreeMap;
// A finite travel budget prevents homing projectiles living forever among fresh weak targets.
const MAX_TRAVEL: f64 = 2400.0;
const MAX_FLIGHT_S: f64 = 12.0;
#[derive(Clone, Debug)]
pub(super) struct Missile {
    pose: MissilePose,
    shot: Shot,
    target: Option<usize>,
    last_w: f64,
    born_w: f64,
    sampled: f64,
    travel: f64,
    remaining: usize,
    hit_until: BTreeMap<usize, f64>,
}
impl Sim {
    fn missile_target(&self, x: f64, y: f64, hit_until: &BTreeMap<usize, f64>) -> Option<usize> {
        self.st
            .alive
            .iter()
            .copied()
            .filter(|&fi| {
                self.hittable(fi)
                    && hit_until
                        .get(&fi)
                        .is_none_or(|t| *t <= self.st.run_s + 1e-9)
            })
            .min_by(|&a, &b| {
                let distance = |fi| {
                    let p = self.pos(fi, self.st.w);
                    (p.0 - x).hypot(p.1 - y)
                };
                distance(a).total_cmp(&distance(b)).then(a.cmp(&b))
            })
    }
    pub(super) fn missile_burst_shot(&mut self, mut shot: Shot) {
        if !self.weapons_active() {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        shot.x = hp.x;
        shot.y = hp.y;
        shot.song = self.st.s;
        shot.accent = self.weapon_accent();
        self.rec.fire(shot.id.as_str(), self.st.s);
        self.rec.weapon_shot(shot);
        self.launch_missile(shot);
    }
    pub(super) fn launch_missile(&mut self, shot: Shot) {
        let hit_until = BTreeMap::new();
        let target = self.missile_target(shot.x, shot.y, &hit_until);
        let direction = target.map_or((1.0, 0.0), |fi| {
            let p = self.pos(fi, self.st.w);
            (p.0 - shot.x, p.1 - shot.y)
        });
        let direction = if direction.0.hypot(direction.1) < 1e-9 {
            (1.0, 0.0)
        } else {
            direction
        };
        let d = direction.0.hypot(direction.1);
        let speed = (1.65 * crate::survivors::PX_PER_VS_UNIT * shot.speed).max(0.0);
        let pose = MissilePose {
            id: self.st.next_missile,
            weapon: shot.id,
            w: self.st.w,
            x: shot.x,
            y: shot.y,
            vx: direction.0 / d * speed,
            vy: direction.1 / d * speed,
            radius: shot.radius,
            accent: shot.accent,
            alive: true,
        };
        self.st.next_missile += 1;
        self.rec.missile(pose);
        self.st.missiles.push(Missile {
            pose,
            shot,
            target,
            last_w: self.st.w,
            born_w: self.st.w,
            sampled: self.st.w,
            travel: 0.0,
            remaining: shot.pierce.max(1.0) as usize,
            hit_until,
        });
    }
    pub(super) fn clear_missiles(&mut self) {
        for m in self.st.missiles.drain(..) {
            self.rec.missile(MissilePose {
                alive: false,
                ..m.pose
            });
        }
    }
    pub(super) fn tick_missiles(&mut self) {
        let mut missiles = std::mem::take(&mut self.st.missiles);
        let contacts = super::contacts::Contacts::new(self);
        for m in &mut missiles {
            let dt = (self.st.w - m.last_w).max(0.0);
            m.last_w = self.st.w;
            if dt <= 1e-12 {
                continue;
            }
            m.hit_until.retain(|_, until| *until > self.st.run_s);
            if !m
                .target
                .is_some_and(|fi| self.hittable(fi) && !m.hit_until.contains_key(&fi))
            {
                m.target = self.missile_target(m.pose.x, m.pose.y, &m.hit_until);
            }
            let speed = (1.65 * crate::survivors::PX_PER_VS_UNIT * m.shot.speed).max(0.0);
            if let Some(fi) = m.target {
                let p = self.pos(fi, self.st.w);
                let delta = (p.0 - m.pose.x, p.1 - m.pose.y);
                let length = delta.0.hypot(delta.1);
                if length > 1e-9 {
                    let heading = m.pose.vy.atan2(m.pose.vx);
                    let desired = delta.1.atan2(delta.0);
                    let turn = (desired - heading + std::f64::consts::PI)
                        .rem_euclid(std::f64::consts::TAU)
                        - std::f64::consts::PI;
                    let heading = heading + turn.clamp(-12.0 * dt, 12.0 * dt);
                    m.pose.vx = heading.cos() * speed;
                    m.pose.vy = heading.sin() * speed;
                }
            }
            let (x0, y0) = (m.pose.x, m.pose.y);
            m.pose.x += m.pose.vx * dt;
            m.pose.y += m.pose.vy * dt;
            m.travel += speed * dt;
            m.pose.w = self.st.w;
            let shape = Shape::Capsule {
                x0,
                y0,
                x1: m.pose.x,
                y1: m.pose.y,
                radius: m.pose.radius,
            };
            let dx = m.pose.x - x0;
            let dy = m.pose.y - y0;
            let len2 = dx * dx + dy * dy;
            let mut targets: Vec<_> = contacts
                .candidates(&shape)
                .into_iter()
                .filter_map(|p| {
                    let fi = p.fi;
                    if !self.hittable(fi) || m.hit_until.contains_key(&fi) {
                        return None;
                    }
                    let (x, y) = (p.x, p.y);
                    if !crate::aim::catches(&shape, x, y, p.r) {
                        return None;
                    }
                    let centre = if len2 > 1e-12 {
                        ((x - x0) * dx + (y - y0) * dy) / len2
                    } else {
                        0.0
                    };
                    let r = m.pose.radius + self.st.foes[fi].r;
                    let perpendicular =
                        (x - x0).powi(2) + (y - y0).powi(2) - centre * centre * len2;
                    let entry = if len2 > 1e-12 {
                        centre - ((r * r - perpendicular).max(0.0) / len2).sqrt()
                    } else {
                        0.0
                    };
                    Some((entry.max(0.0), fi))
                })
                .collect();
            targets.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.cmp(&b.1)));
            let mut stopped_on_hit = false;
            for (entry, fi) in targets {
                if !self.hittable(fi) {
                    continue;
                }
                let hit = self.add_hit(
                    HitSpec {
                        source: HitSource::Weapon,
                        what: m.shot.id.as_str().into(),
                        weight: if m.shot.accent {
                            Weight::Heavy
                        } else {
                            Weight::Light
                        },
                        primary: true,
                        shape,
                        dir: m.pose.vy.atan2(m.pose.vx),
                        accent: f64::from(m.shot.accent),
                        damage: m.shot.damage,
                        group: None,
                    },
                    None,
                );
                self.damage(fi, &hit);
                m.hit_until.insert(fi, self.st.run_s + m.shot.hit_delay_s);
                if self.st.foes[fi].alive {
                    m.remaining -= 1;
                    if m.remaining == 0 {
                        m.pose.x = x0 + dx * entry.clamp(0.0, 1.0);
                        m.pose.y = y0 + dy * entry.clamp(0.0, 1.0);
                        m.pose.alive = false;
                        stopped_on_hit = true;
                        break;
                    }
                }
            }
            let v = self.st.camera.view();
            if self.st.w - m.born_w > MAX_FLIGHT_S
                || m.travel > MAX_TRAVEL
                || m.pose.x < v[0] - 96.0
                || m.pose.x > v[2] + 96.0
                || m.pose.y < v[1] - 96.0
                || m.pose.y > v[3] + 96.0
            {
                m.pose.alive = false;
            }
            if !m.pose.alive || self.st.w - m.sampled >= 1.0 / 60.0 - 1e-9 {
                if stopped_on_hit {
                    // Hold the head at collision until the same grid reaction as its sparks.
                    self.rec.missile(MissilePose {
                        alive: true,
                        vx: 0.0,
                        vy: 0.0,
                        ..m.pose
                    });
                    self.st.q.push(
                        self.next_enemy_sixteenth(self.st.s),
                        super::Ev::MissileEnd(Box::new(m.pose)),
                    );
                } else {
                    self.rec.missile(m.pose);
                }
                m.sampled = self.st.w;
            }
        }
        missiles.retain(|m| m.pose.alive);
        self.st.missiles = missiles;
        let foes = &self.st.foes;
        self.st.alive.retain(|&fi| foes[fi].alive);
    }
}
