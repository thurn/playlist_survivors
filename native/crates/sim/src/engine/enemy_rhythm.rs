//! Contact attacks have notice, one peak, and a return. Only committed knowledge plans them.
use super::{BITE_SLACK, CONTACT_GAP, Ev, HURTBOX_RADIUS, Sim};

pub(super) fn boss_gait() -> &'static crate::content::types::Gait {
    static GAIT: std::sync::OnceLock<crate::content::types::Gait> = std::sync::OnceLock::new();
    GAIT.get_or_init(|| crate::content::types::Gait {
        // The native map has two-beat bars; gait slots are beats/eighths.
        every: crate::content::types::Subdivision::Beat,
        pattern: "x".into(),
        share: 0.2,
    })
}

#[derive(Clone, Copy, Debug)]
pub(super) struct Lunge {
    peak_s: f64,
    generation: Option<u64>,
    pub(super) attempted: bool,
}

impl Sim {
    pub(super) fn enemy_reliable(&self) -> bool {
        let ticks = &self.log.ticks[..self.st.ingested.ticks];
        self.clear_at(self.st.s)
            && ticks
                .iter()
                .rev()
                .find(|t| t.t <= self.st.s + 1e-9)
                .is_some_and(|t| t.known_at <= self.st.s + 1e-9 && self.st.s - t.t <= 0.25)
    }

    pub(super) fn enemy_contact_beat(&self) -> bool {
        self.enemy_reliable()
            && self.log.beats[..self.st.ingested.beats].iter().any(|b| {
                (b.t - self.st.s).abs() < 1e-8
                    && b.known_at <= self.st.s
                    && b.evidence >= self.env.input.gate
            })
    }

    pub(super) fn enemy_beat_period(&self, bi: usize) -> f64 {
        let b = self.log.beats[bi];
        let observed = bi
            .checked_sub(1)
            .map_or(b.period, |i| b.t - self.log.beats[i].t);
        // A tracking gap is not a long travel interval to repay on relock.
        if observed >= 0.5 * b.period && observed <= 1.5 * b.period {
            observed
        } else {
            b.period
        }
    }

    /// One admission shared by chase, formations, boss travel and contact.
    /// Only the ingested prefix is inspected. No stale tempo extrapolation.
    pub(super) fn enemy_main_window(&self) -> Option<(usize, f64, f64)> {
        if self.st.freeze.frozen || !self.enemy_reliable() {
            return None;
        }
        let beats = &self.log.beats[..self.st.ingested.beats];
        let bi = beats.partition_point(|b| b.t < self.st.s - 1e-9);
        let b = beats.get(bi)?;
        if b.known_at > self.st.s + 1e-9 || b.evidence < self.env.input.gate {
            return None;
        }
        let period = self.enemy_beat_period(bi);
        if !period.is_finite() || period <= 0.0 {
            return None;
        }
        let window = (0.2 * period).min(0.1);
        (self.st.s >= b.t - window - 1e-9 && b.t > self.st.s + 1e-9).then_some((bi, b.t, period))
    }

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
        if self.st.freeze.frozen || generation.is_some() || peak <= self.st.s + 1e-8 {
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
        if !self.enemy_reliable() || generation.is_some() {
            return;
        }
        for fi in self.st.alive.clone() {
            if !self.hittable(fi) || self.st.foes[fi].recoil_until > self.st.w {
                continue;
            }
            let f = &self.st.foes[fi];
            // Repeated ingestion/queue rebuilding cannot arm a second attempt at this beat.
            if f.attack.is_some_and(|a| (a.peak_s - peak).abs() < 1e-8) {
                continue;
            }
            let hp = self.hero_pose(self.st.w);
            let (x, y) = self.pos(fi, self.st.w);
            let reach = self.env.input.lib.enemies[self.st.foes[fi].def].speed * period;
            if (x - hp.x).hypot(y - hp.y)
                <= HURTBOX_RADIUS + self.st.foes[fi].r + CONTACT_GAP + BITE_SLACK + reach
            {
                self.rec.enemy_notice(fi, peak);
            }
            self.st.foes[fi].attack = Some(Lunge {
                peak_s: peak,
                generation,
                attempted: false,
            });
        }
    }

    pub(super) fn tick_enemy_bites(&mut self) {
        if !self.enemy_contact_beat()
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
            let swept = f.step.is_some_and(|g| {
                if (g.peak_s - self.st.s).abs() > 1e-6 {
                    return false;
                }
                let d2 = g.dx * g.dx + g.dy * g.dy;
                let u = if d2 > 1e-9 {
                    ((hp.x - (x - g.dx)) * g.dx + (hp.y - (y - g.dy)) * g.dy) / d2
                } else {
                    1.0
                }
                .clamp(0.0, 1.0);
                (x - g.dx + u * g.dx - hp.x).hypot(y - g.dy + u * g.dy - hp.y)
                    <= HURTBOX_RADIUS + f.r + CONTACT_GAP + BITE_SLACK
            });
            if swept
                || (x - hp.x).hypot(y - hp.y) <= HURTBOX_RADIUS + f.r + CONTACT_GAP + BITE_SLACK
            {
                let def = &self.env.input.lib.enemies[f.def];
                let amount = if self.st.survivor.is_some() {
                    def.contact_damage
                } else {
                    crate::upgrades::armoured_bite(def.contact_damage, self.st.bonus.armour)
                };
                let id = def.id.clone();
                self.hurt(amount, "bite", &id);
            }
        }
    }
}
