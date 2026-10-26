//! Source-duration orbiting blades; hit memory clears per blade every 1.7 s.
use super::{Sim, combat::HitSpec};
use crate::{
    core::geometry::Shape,
    recorder::{HitSource, Weight},
    survivors::{MissilePose, PX_PER_VS_UNIT, Shot, bible_orbit_speed},
};
use std::{collections::BTreeSet, f64::consts::TAU};
#[derive(Clone, Debug)]
struct Blade {
    id: usize,
    hit: BTreeSet<usize>,
}
#[derive(Clone, Debug)]
pub(super) struct BibleCycle {
    shot: Shot,
    born: f64,
    last: f64,
    sampled: f64,
    clear_at: f64,
    omega: f64,
    radius: f64,
    blades: Vec<Blade>,
}
impl BibleCycle {
    fn size(&self, w: f64) -> f64 {
        let age = (w - self.born).max(0.0);
        let grow = (age / 0.5).min(1.0);
        let fade = (1.0 - (age - self.shot.duration_s).max(0.0) / 0.5).max(0.0);
        (1.0 + 0.3 * self.shot.area) * grow * fade
    }
    fn pose(&self, i: usize, w: f64, hero: (f64, f64), alive: bool) -> MissilePose {
        let angle = self.omega * (w - self.born) + TAU * i as f64 / self.blades.len() as f64;
        MissilePose {
            id: self.blades[i].id,
            weapon: self.shot.id,
            w,
            x: hero.0 + angle.cos() * self.radius,
            y: hero.1 + angle.sin() * self.radius,
            vx: -angle.sin() * self.radius * self.omega,
            vy: angle.cos() * self.radius * self.omega,
            radius: 0.08 * PX_PER_VS_UNIT * self.size(w),
            accent: self.shot.accent,
            alive,
        }
    }
}
impl Sim {
    pub(super) fn launch_bible(&mut self, shot: Shot) {
        let (omega, _) = bible_orbit_speed(shot.speed, shot.period, shot.amount);
        let blades = (0..shot.amount)
            .map(|_| {
                let id = self.st.next_missile;
                self.st.next_missile += 1;
                Blade {
                    id,
                    hit: BTreeSet::new(),
                }
            })
            .collect();
        let cycle = BibleCycle {
            shot,
            born: self.st.w,
            last: self.st.w,
            sampled: self.st.w,
            clear_at: self.st.w + shot.hit_delay_s,
            omega,
            radius: shot.radius,
            blades,
        };
        let hp = self.hero_pose(self.st.w);
        for i in 0..shot.amount {
            self.rec
                .missile(cycle.pose(i, self.st.w, (hp.x, hp.y), true));
        }
        self.st.bibles.push(cycle);
    }
    pub(super) fn clear_bibles(&mut self) {
        let hp = self.hero_pose(self.st.w);
        for c in self.st.bibles.drain(..) {
            for i in 0..c.blades.len() {
                self.rec.missile(c.pose(i, self.st.w, (hp.x, hp.y), false));
            }
        }
    }
    pub(super) fn tick_bibles(&mut self) {
        let mut cycles = std::mem::take(&mut self.st.bibles);
        for c in &mut cycles {
            let age = self.st.w - c.born;
            let hp = self.hero_pose(self.st.w);
            if age >= c.shot.duration_s + 0.5 - 1e-9 {
                for i in 0..c.blades.len() {
                    self.rec.missile(c.pose(i, self.st.w, (hp.x, hp.y), false));
                }
                continue;
            }
            if age < c.shot.duration_s - 1e-9 {
                while self.st.w >= c.clear_at - 1e-9 {
                    for blade in &mut c.blades {
                        blade.hit.clear();
                    }
                    c.clear_at += c.shot.hit_delay_s.max(0.001);
                }
                let old_hero = self.hero_pose(c.last);
                for i in 0..c.blades.len() {
                    // Subdivide unusually long/angular moves so the swept chord
                    // cannot cut through the orbit's empty centre.
                    let steps = ((self.st.w - c.last) * c.omega / (std::f64::consts::PI / 32.0))
                        .ceil()
                        .max(1.0) as usize;
                    for sub in 0..steps {
                        let f0 = sub as f64 / steps as f64;
                        let f1 = (sub + 1) as f64 / steps as f64;
                        let hero_at = |f: f64| {
                            (
                                old_hero.x + (hp.x - old_hero.x) * f,
                                old_hero.y + (hp.y - old_hero.y) * f,
                            )
                        };
                        let w0 = c.last + (self.st.w - c.last) * f0;
                        let w1 = c.last + (self.st.w - c.last) * f1;
                        let a = c.pose(i, w0, hero_at(f0), true);
                        let b = c.pose(i, w1, hero_at(f1), true);
                        let shape = Shape::Capsule {
                            x0: a.x,
                            y0: a.y,
                            x1: b.x,
                            y1: b.y,
                            radius: b.radius,
                        };
                        let targets: Vec<_> = self
                            .st
                            .alive
                            .iter()
                            .copied()
                            .filter(|&fi| self.hittable(fi) && !c.blades[i].hit.contains(&fi))
                            .filter(|&fi| {
                                let p = self.pos(fi, self.st.w);
                                crate::aim::catches(&shape, p.0, p.1, self.st.foes[fi].r)
                            })
                            .collect();
                        if targets.is_empty() {
                            continue;
                        }
                        let hit = self.add_hit(
                            HitSpec {
                                source: HitSource::Weapon,
                                what: c.shot.id.as_str().into(),
                                weight: if c.shot.accent {
                                    Weight::Heavy
                                } else {
                                    Weight::Light
                                },
                                primary: true,
                                shape,
                                dir: b.vy.atan2(b.vx),
                                accent: f64::from(c.shot.accent),
                                damage: c.shot.damage,
                                group: None,
                            },
                            None,
                        );
                        for fi in targets {
                            c.blades[i].hit.insert(fi);
                            self.damage(fi, &hit);
                        }
                    }
                }
            }
            if self.st.w - c.sampled >= 1.0 / 60.0 - 1e-9 {
                for i in 0..c.blades.len() {
                    self.rec.missile(c.pose(i, self.st.w, (hp.x, hp.y), true));
                }
                c.sampled = self.st.w;
            }
            c.last = self.st.w;
        }
        cycles.retain(|c| self.st.w - c.born < c.shot.duration_s + 0.5 - 1e-9);
        self.st.bibles = cycles;
        let foes = &self.st.foes;
        self.st.alive.retain(|&fi| foes[fi].alive);
    }
}
