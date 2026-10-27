//! Contact attacks have notice, one peak, and a return. Only committed knowledge plans them.
use super::{BITE_SLACK, CONTACT_GAP, Ev, HURTBOX_RADIUS, Sim};

const REACH: f64 = 12.0;
pub(super) fn boss_gait() -> &'static crate::content::types::Gait {
    static GAIT: std::sync::OnceLock<crate::content::types::Gait> = std::sync::OnceLock::new();
    GAIT.get_or_init(|| crate::content::types::Gait {
        // The native map has two-beat bars; gait slots are beats/eighths.
        every: crate::content::types::Subdivision::Beat,
        pattern: "x.".into(),
        share: 0.7,
    })
}

#[derive(Clone, Copy, Debug)]
pub(super) struct Lunge {
    wind_w: f64,
    peak_w: f64,
    end_w: f64,
    peak_s: f64,
    direction: (f64, f64),
    generation: Option<u64>,
    pub(super) attempted: bool,
}

impl Lunge {
    pub fn offset(self, w: f64) -> (f64, f64) {
        let d = if w < self.wind_w || w >= self.end_w {
            0.0
        } else if w < self.peak_w {
            let u = ((w - self.wind_w) / (self.peak_w - self.wind_w)).clamp(0.0, 1.0);
            if u < 0.4 {
                -4.0 * u / 0.4
            } else {
                -4.0 + (REACH + 4.0) * (u - 0.4) / 0.6
            }
        } else {
            REACH * (1.0 - (w - self.peak_w) / (self.end_w - self.peak_w))
        };
        (self.direction.0 * d, self.direction.1 * d)
    }
}

impl Sim {
    pub(super) fn mark_survivor_boss(&mut self, id: usize, boss: bool) {
        self.st.foes[id].survivor_boss = boss;
        if boss {
            self.rec.survivor_boss(id);
            // The role's ordinary gait may use a different subdivision. Its slot
            // number cannot be reused as a bar slot at a late-minute spawn.
            self.st.foes[id].step_k = Some(crate::gait::gait_slot(
                boss_gait(),
                self.known_beat_at(self.st.s),
            ));
        }
    }
    pub(super) fn next_enemy_sixteenth(&self, due: f64) -> f64 {
        self.known_grid_after(due + 1e-8, 0.25).map_or_else(
            || {
                let period = self.env.rule.free_period_s / 4.0;
                let anchor = self.log.beats[..self.st.ingested.beats]
                    .iter()
                    .rev()
                    .find(|b| b.t <= due)
                    .map_or(0.0, |b| b.t);
                anchor + (((due - anchor) / period + 1e-8).floor() + 1.0) * period
            },
            |(_, at)| at,
        )
    }

    pub(super) fn queue_enemy_wind(&mut self, peak: f64, period: f64, generation: Option<u64>) {
        if self.st.survivor.is_none() || self.st.freeze.frozen || peak <= self.st.s + 1e-8 {
            return;
        }
        // A late known beat may shorten notice, but cannot manufacture a wind-up in the past.
        let at = (peak - period * 0.5).max(self.st.s);
        self.st.q.push(
            at,
            Ev::EnemyWind {
                peak,
                period,
                generation,
            },
        );
    }

    pub(super) fn enemy_wind(&mut self, peak: f64, period: f64, generation: Option<u64>) {
        if generation.is_some_and(|g| g != self.st.freeze.pulse_generation)
            || self.st.freeze.frozen
            || self.st.blasting
            || self.st.health <= 0.0
        {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        for fi in self.st.alive.clone() {
            if !self.hittable(fi) || self.st.foes[fi].recoil_until > self.st.w {
                continue;
            }
            let f = &self.st.foes[fi];
            // Repeated ingestion/queue rebuilding cannot arm a second attempt at this beat.
            if f.attack.is_some_and(|a| (a.peak_s - peak).abs() < 1e-8) {
                continue;
            }
            let (x, y) = self.pos(fi, self.st.w);
            let (dx, dy) = (hp.x - x, hp.y - y);
            let d = dx.hypot(dy);
            if !d.is_finite() || d > HURTBOX_RADIUS + f.r + CONTACT_GAP + BITE_SLACK + REACH {
                continue;
            }
            self.st.foes[fi].attack = Some(Lunge {
                wind_w: self.st.w,
                peak_w: self.st.w + peak - self.st.s,
                end_w: self.st.w + peak - self.st.s + period * 0.25,
                peak_s: peak,
                direction: if d > 1e-9 {
                    (dx / d, dy / d)
                } else {
                    (1.0, 0.0)
                },
                generation,
                attempted: false,
            });
        }
    }

    pub(super) fn tick_enemy_bites(&mut self) {
        if self.st.survivor.is_none()
            || self.st.freeze.frozen
            || self.st.blasting
            || self.st.health <= 0.0
        {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        for fi in self.st.alive.clone() {
            let Some(a) = self.st.foes[fi].attack else {
                continue;
            };
            if a.attempted || (a.peak_s - self.st.s).abs() > 1e-8 {
                continue;
            }
            self.st.foes[fi].attack.as_mut().expect("lunge").attempted = true;
            if a.generation
                .is_some_and(|g| g != self.st.freeze.pulse_generation)
                || !self.hittable(fi)
            {
                continue;
            }
            let (x, y) = self.pos(fi, self.st.w);
            let f = &self.st.foes[fi];
            if (x - hp.x).hypot(y - hp.y) <= HURTBOX_RADIUS + f.r + CONTACT_GAP + BITE_SLACK {
                let def = &self.env.input.lib.enemies[f.def];
                let (amount, id) = (def.contact_damage, def.id.clone());
                self.hurt(amount, "bite", &id);
            }
        }
    }
}
