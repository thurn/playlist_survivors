//! The early signature owns run time; only known reliable main beats admit it/slams.
use super::{
    Sim,
    plan::{BossPlan, Slam},
};
use crate::survivors::BossCueKind;
#[derive(Clone, Debug, Default)]
pub(super) struct Signature {
    warned: bool,
    ends: usize,
    transition: f64,
    started: bool,
    landing: Option<f64>,
    drop_start: f64,
    drop_progress: f64,
    landed: Option<f64>,
    last: Option<usize>,
    beats: usize,
    mark: Option<(usize, f64)>,
    retreat: bool,
}
impl Sim {
    fn signature_enabled(&self) -> bool {
        self.st.survivor.is_some()
            && !self.st.freeze.frozen
            && !self.st.blasting
            && self.st.health > 0.0
            && self.env.director.set_pieces.is_some()
    }
    pub(super) fn signature_stop(&self) -> f64 {
        if !self.signature_enabled() {
            return f64::INFINITY;
        }
        self.st
            .signature
            .landing
            .into_iter()
            .chain(self.st.signature.mark.map(|m| m.1))
            .fold(f64::INFINITY, f64::min)
            .max(self.st.s)
    }
    pub(super) fn freeze_signature(&mut self) {
        if !self.st.signature.started {
            return;
        }
        self.st.signature.mark = None;
        self.st.signature.beats = 0;
        self.st.slam_marks.clear();
        if self.st.signature.started && self.st.signature.landed.is_none() {
            self.hold_descent();
            self.st.signature.landing = None;
            if let Some(id) = self.st.boss_foe {
                self.st.foes[id].land_s = Some(f64::INFINITY);
            }
        }
    }
    fn hold_descent(&mut self) {
        if let Some(land) = self.st.signature.landing {
            self.st.signature.drop_progress = ((self.st.s - self.st.signature.drop_start)
                / (land - self.st.signature.drop_start).max(1e-6))
            .clamp(0.0, 0.99);
        }
    }
    pub(super) fn tick_signature(&mut self) {
        if !self.signature_enabled() {
            return;
        }
        while let Some(end) = self
            .log
            .ends
            .as_deref()
            .unwrap_or(&[])
            .get(self.st.signature.ends)
            .filter(|e| e.known_at <= self.st.s)
            .copied()
        {
            self.st.signature.ends += 1;
            if self.st.s - end.known_at <= 0.1
                && self.st.run_s - self.st.signature.transition >= 8.0
            {
                self.rec.punctuation(self.st.s, self.st.w);
                self.st.signature.transition = self.st.run_s;
            }
        }
        let config = self.env.director.set_pieces.as_ref().unwrap().boss.clone();
        if self.st.run_s < config.after_s - 2.0 {
            return;
        }
        if !self.st.signature.warned && self.enemy_reliable() {
            self.st.signature.warned = true;
            self.st.boss_presentation.pending += 1;
            self.st.boss_presentation.warning_run = Some(self.st.run_s);
            self.st.boss_presentation.landed_run = None;
            self.boss_cue(BossCueKind::Warning, usize::MAX, 0.0, 0.0);
        }
        if self.st.signature.started && self.st.boss_foe.is_none_or(|id| !self.st.foes[id].alive) {
            self.st.signature.landing = None;
            self.st.signature.mark = None;
            self.st.slam_marks.clear();
            return;
        }
        // Stage after the scheduled early boss's beat, with at least350ms actual notice.
        if self.st.signature.landed.is_none()
            && self.st.signature.landing.is_none()
            && self.st.run_s >= config.after_s - 0.6
            && self.enemy_reliable()
            && self.st.spawner.waiting_bosses.is_empty()
        {
            let due = config.after_s + self.st.freeze.total_s;
            let landing = self.log.beats[..self.st.ingested.beats]
                .iter()
                .find(|b| {
                    b.known_at <= self.st.s
                        && b.t >= due.max(self.st.s + 0.35_f64.min(b.period * 0.75)) - 1e-9
                        && b.evidence >= self.env.input.gate
                })
                .map(|b| b.t);
            if let Some(landing) = landing {
                // Keep the source boss landing separate even when it was delayed to this beat.
                if self
                    .st
                    .boss_presentation
                    .last_landed_run
                    .is_some_and(|r| self.st.run_s - r < 0.5)
                {
                    return;
                }
                if !self.st.signature.started {
                    self.retire_musical();
                    self.reclaim_boss_room(1);
                    if self.st.alive.len() >= self.survivor_cap() {
                        return;
                    }
                    self.st.plan.boss = Some(BossPlan::signature(config.kind.clone(), landing));
                    self.boss_spawn();
                    let id = self.st.boss_foe.expect("signature spawned");
                    self.mark_survivor_boss(id, true);
                    self.st.foes[id].land_s = Some(f64::INFINITY);
                    self.st.signature.drop_start = self.st.s;
                    self.st.signature.started = true;
                } else if let Some(id) = self.st.boss_foe {
                    self.st.foes[id].land_s = Some(f64::INFINITY);
                    let p = self.pos(id, self.st.w);
                    let u = self.st.signature.drop_progress;
                    let start = self.st.s - u * (landing - self.st.s) / (1.0 - u);
                    self.st.signature.drop_start = start;
                    self.rec.boss(id, landing, p.0, p.1, start);
                }
                self.st.signature.landing = Some(landing);
            }
        }
        if let Some(at) = self.st.signature.landing
            && self.st.s >= at - 1e-9
        {
            if !self.enemy_contact_beat()
                || self
                    .st
                    .boss_presentation
                    .last_landed_run
                    .is_some_and(|r| (self.st.run_s - r).abs() < 1e-8)
            {
                self.hold_descent();
                self.st.signature.landing = None;
                if let Some(id) = self.st.boss_foe {
                    self.st.foes[id].land_s = Some(f64::INFINITY);
                }
                return;
            }
            self.st.signature.landing = None;
            self.st.signature.landed = Some(self.st.run_s);
            if let Some(id) = self.st.boss_foe {
                self.st.foes[id].land_s = Some(self.st.s);
            }
            self.boss_land();
            let id = self.st.boss_foe.expect("landed signature");
            let p = self.pos(id, self.st.w);
            self.boss_cue(BossCueKind::Landed, id, p.0, p.1);
            if let Some(name) = &config.landmark {
                self.landmark(name);
            }
            return;
        }
        let Some(landed) = self.st.signature.landed else {
            return;
        };
        if self.st.run_s >= landed + config.retreat_s {
            // A cancelled slam cannot keep signature_stop at the current clock.
            self.st.signature.mark = None;
            self.st.slam_marks.clear();
            if !self.st.signature.retreat {
                self.boss_retreat();
                self.st.signature.retreat = true;
            }
            if self.st.run_s >= landed + config.retreat_s + 4.0
                && let Some(id) = self.st.boss_foe
            {
                self.expire_event_members(&[id]);
            }
            return;
        }
        if let Some((k, at)) = self.st.signature.mark
            && self.st.s >= at - 1e-9
        {
            self.st.signature.mark = None;
            self.slam_land(k); // Shared reliability/contact and damage path.
            self.st.signature.beats = 0;
        }
        if !self.enemy_contact_beat() {
            return;
        }
        let bi = self.log.beats[..self.st.ingested.beats]
            .partition_point(|b| b.t <= self.st.s + 1e-9)
            - 1;
        if self.st.signature.last == Some(bi) {
            return;
        }
        self.st.signature.last = Some(bi);
        self.st.signature.beats += 1;
        if self.st.signature.mark.is_none()
            && self.st.signature.beats >= (config.slam_beats as usize).saturating_sub(1).max(1)
            && let Some(next) = self.log.beats[..self.st.ingested.beats]
                .iter()
                .find(|b| {
                    b.known_at <= self.st.s
                        && b.t >= self.st.s + 0.2
                        && b.evidence >= self.env.input.gate
                })
                .copied()
        {
            let plan = self.st.plan.boss.as_mut().unwrap();
            let k = plan.slams.len();
            plan.slams.push(Slam { s_land: next.t });
            self.slam_mark(k);
            self.st.signature.mark = Some((k, next.t));
        }
    }
}
