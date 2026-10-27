//! Current-visible target selection, then fixed-point Thunder Loop returns.
use super::{Ev, Sim, combat::HitSpec};
use crate::{
    core::{
        geometry::Shape,
        rng::{Part, Rng},
    },
    recorder::{HitSource, Weight},
    survivors::{LightningStrike, Shot, WeaponId},
};
#[derive(Clone, Debug)]
pub(super) struct PendingLightning {
    pub shot: Shot,
    pub volley: usize,
    pub index: usize,
    /// A return strike keeps the original point even after its target moves or dies.
    pub point: Option<(f64, f64)>,
}
impl Sim {
    pub(super) fn launch_lightning(&mut self, shot: Shot) {
        let volley = self.st.next_lightning_volley;
        self.st.next_lightning_volley += 1;
        self.lightning_strike(PendingLightning {
            shot,
            volley,
            index: 0,
            point: None,
        });
    }
    pub(super) fn lightning_strike(&mut self, pending: PendingLightning) {
        if !self.weapons_active() {
            return;
        }
        // Newly committed knowledge can defer a provisional free subdivision.
        if let Some((_, at)) = self.known_grid_after(self.st.s - 1e-8, 0.25)
            && at > self.st.s + 1e-7
        {
            self.st.q.push(at, Ev::LightningStrike(Box::new(pending)));
            return;
        }
        if let Some((x, y)) = pending.point {
            self.lightning_at(pending.shot, x, y);
            return;
        }
        let view = self.st.camera.view();
        let targets: Vec<_> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&fi| self.hittable(fi))
            .filter(|&fi| {
                let (x, y) = self.pos(fi, self.st.w);
                x >= view[0] && x <= view[2] && y >= view[1] && y <= view[3]
            })
            .collect();
        if !targets.is_empty() {
            let random = Rng::new(&[
                Part::Num(self.env.input.seed),
                Part::Str("vs-lightning-target"),
                Part::Num(pending.volley as f64),
                Part::Num(pending.index as f64),
            ])
            .next();
            let fi = targets[(random * targets.len() as f64).floor() as usize];
            let (x, y) = self.pos(fi, self.st.w);
            self.lightning_at(pending.shot, x, y);
            if pending.shot.id == WeaponId::ThunderLoop {
                let due = self.st.s + 1.5;
                let at = self.known_grid_after(due, 0.25).map_or(due, |(_, at)| at);
                self.st.q.push(
                    at,
                    Ev::LightningStrike(Box::new(PendingLightning {
                        point: Some((x, y)),
                        ..pending.clone()
                    })),
                );
            }
        }
        if pending.index + 1 < pending.shot.amount {
            self.st.q.push(
                self.next_enemy_sixteenth(self.st.s),
                Ev::LightningStrike(Box::new(PendingLightning {
                    index: pending.index + 1,
                    ..pending
                })),
            );
        }
    }
    fn lightning_at(&mut self, shot: Shot, x: f64, y: f64) {
        let strike = LightningStrike {
            id: self.st.next_lightning_strike,
            weapon: shot.id,
            song: self.st.s,
            x,
            y,
            radius: shot.radius,
            accent: self.weapon_accent(),
        };
        self.st.next_lightning_strike += 1;
        self.rec.lightning(strike);
        let hit = self.add_hit(
            HitSpec {
                source: HitSource::Weapon,
                what: shot.id.as_str().into(),
                weight: if strike.accent {
                    Weight::Heavy
                } else {
                    Weight::Light
                },
                primary: true,
                shape: Shape::Circle {
                    x,
                    y,
                    radius: strike.radius,
                },
                dir: 0.0,
                accent: f64::from(strike.accent),
                damage: shot.damage,
                group: None,
            },
            None,
        );
        let victims: Vec<_> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&fi| {
                if !self.hittable(fi) {
                    return false;
                }
                let p = self.pos(fi, self.st.w);
                crate::aim::catches(&hit.shape, p.0, p.1, self.st.foes[fi].r)
            })
            .collect();
        for fi in victims {
            self.damage(fi, &hit);
        }
        let foes = &self.st.foes;
        self.st.alive.retain(|&fi| foes[fi].alive);
    }
}
