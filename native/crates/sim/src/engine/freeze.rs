//! A freeze owns its clock and pending world work; it never borrows hitstop's flags.
use super::{Entry, Ev, Sim, phase};
use crate::movement::InputKey;
use crate::player::{KeyEvent, KeyEventKind};
use crate::survivors::{FreezeReason, RunState};

#[derive(Clone, Debug)]
struct Held {
    ready_run: f64,
    entry: Entry,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(super) enum ResumeGrid {
    #[default]
    Committed,
    Free,
}

#[derive(Clone, Debug, Default)]
pub(super) struct Freeze {
    pub frozen: bool,
    pub total_s: f64,
    pub(super) reasons: Vec<FreezeReason>,
    last: Option<FreezeReason>,
    down: [bool; 7],
    blocked: [bool; 7],
    pub selected: usize,
    pub confirmed: bool,
    since: f64,
    beat_at_open: f64,
    held: Vec<Held>,
    ticks_seen: usize,
    quiet: Option<f64>,
    clear_from: Option<f64>,
    music_since: f64,
    music_pending: bool,
    music_lock: Option<usize>,
    pub resume: Option<f64>,
    pub(super) resume_grid: ResumeGrid,
    pub pulse_generation: u64,
}

impl Freeze {
    pub fn reasons_empty(&self) -> bool {
        self.reasons.is_empty()
    }
}

impl Ev {
    /// These are a cadence, not a backlog of actions to catch up after a menu.
    fn cadence(&self) -> bool {
        matches!(
            self,
            Self::EnemyWind { .. }
                | Self::Auto { .. }
                | Self::Held
                | Self::Beat { .. }
                | Self::ReactiveBeat { .. }
                | Self::AccentOnset { .. }
                | Self::Drain { .. }
        )
    }

    fn control(&self) -> bool {
        matches!(
            self,
            Self::Key(_) | Self::Freeze(_) | Self::FreePulse { .. }
        )
    }
}

impl Sim {
    pub fn run_state(&self) -> RunState {
        let f = &self.st.freeze;
        let reason = f
            .reasons
            .iter()
            .find(|r| **r == FreezeReason::Paused)
            .or_else(|| {
                f.reasons
                    .iter()
                    .find(|r| matches!(r, FreezeReason::LevelUp | FreezeReason::Chest))
            })
            .or(f.reasons.first())
            .copied()
            .or(f.last);
        RunState {
            song: self.st.s,
            presentation_s: if f.frozen { f.since } else { self.st.s },
            frozen_beat: f.frozen.then_some(f.beat_at_open),
            elapsed: self.st.run_s,
            spawn_minute: self.st.spawner.minute,
            spawned: self.st.spawner.spawned,
            offer: self.st.survivor.as_ref().and_then(|p| p.offer),
            level: self
                .st
                .survivor
                .as_ref()
                .map_or(self.st.level + 1, |p| p.level),
            max_hp: self.health_max(),
            world: self.st.w,
            frozen: f.frozen.then_some(reason).flatten(),
            selected: f.selected,
            confirmed: f.confirmed,
        }
    }

    /// Deterministic interaction hook for SF7/SH10 (usually called by a sim event).
    pub fn freeze_at(&mut self, s: f64, reason: FreezeReason) {
        assert!(s >= self.st.s, "a freeze cannot change the past");
        self.st.q.push(s, Ev::Freeze(reason));
    }

    pub(super) fn publish_run(&mut self) {
        if self.env.input.level.survivors.is_some() {
            self.rec.run_state(self.run_state());
        }
    }

    /// The stream keeps arriving during a freeze. Only known ticks can establish silence.
    pub(super) fn plan_freeze(&mut self) {
        if self.env.input.level.survivors.is_none() || self.st.blasting {
            return;
        }
        let end = self.st.ingested.ticks;
        for i in self.st.freeze.ticks_seen..end {
            let tick = &self.log.ticks[i];
            // Weak beat confidence is not silence: e.g. a swing track or a beatless
            // passage can stay audible throughout. The stream's normalized energy
            // bottoms out at silence; committed-beat evidence still gates re-lock below.
            if tick.energy > 0.01 {
                self.st.freeze.quiet = None;
                self.st.freeze.clear_from.get_or_insert(tick.t);
            } else {
                self.st.freeze.clear_from = None;
                if self.st.freeze.reasons.contains(&FreezeReason::MusicStopped) {
                    self.st.freeze.music_lock = None;
                    self.st.freeze.resume = None;
                }
                let from = *self.st.freeze.quiet.get_or_insert(tick.t);
                let confirmed_end = tick.t >= from + 3.0
                    && self.log.ends.iter().flatten().any(|e| {
                        e.known_at <= self.st.horizon && e.t >= from - 3.0 && e.t <= tick.t
                    });
                if (tick.t >= from + 5.0 || confirmed_end)
                    && !self.st.freeze.music_pending
                    && !self.st.freeze.reasons.contains(&FreezeReason::MusicStopped)
                {
                    let known = tick.known_at.max(self.st.s);
                    let at = self.next_stride(known);
                    self.st.q.push(at, Ev::Freeze(FreezeReason::MusicStopped));
                    self.st.freeze.music_pending = true;
                }
            }
        }
        self.st.freeze.ticks_seen = end;
        if self.st.freeze.reasons.contains(&FreezeReason::MusicStopped) {
            if self.st.freeze.music_lock.is_none() {
                self.st.freeze.music_lock = self.log.beats[..self.st.ingested.beats]
                    .iter()
                    .enumerate()
                    .find(|(_, b)| {
                        b.t >= self.st.freeze.music_since
                            && self.st.freeze.clear_from.is_some_and(|from| b.t >= from)
                            && b.evidence >= self.env.input.gate
                            && self.st.freeze.quiet.is_none()
                    })
                    .map(|(i, _)| i);
            }
            if let Some(lock) = self.st.freeze.music_lock {
                // Two following bar downbeats: preserve a complete bar for hearing the lock.
                let resume = (lock / 2 + 2) * 2;
                if let Some(b) = self.log.beats[..self.st.ingested.beats].get(resume) {
                    let at = b.t.max(self.st.s);
                    self.st.freeze.resume = Some(at);
                }
            }
        }
    }

    fn next_stride(&self, known: f64) -> f64 {
        let steps = &self.st.sch.stride_steps;
        if let Some(&s) = steps.s[..steps.tail()].iter().find(|&&s| s >= known - 1e-9) {
            return s;
        }
        let period = self.env.rule.free_period_s;
        let anchor = self.log.beats[..self.st.ingested.beats]
            .last()
            .map_or(0.0, |b| b.t);
        anchor + ((known - anchor) / period).ceil().max(0.0) * period
    }

    pub(super) fn enter_freeze(&mut self, reason: FreezeReason) {
        if self.env.input.level.survivors.is_none() || self.st.blasting {
            return;
        }
        if !self.st.freeze.reasons.contains(&reason) {
            self.st.freeze.reasons.push(reason);
        }
        self.st.freeze.last = Some(reason);
        if matches!(reason, FreezeReason::LevelUp | FreezeReason::Chest) {
            self.st.freeze.confirmed = false;
            self.st.freeze.selected = 0;
        }
        if reason == FreezeReason::MusicStopped {
            self.st.freeze.music_since = self.st.s;
            self.st.freeze.music_lock = None;
        }
        self.st.freeze.resume = None;
        self.st.freeze.resume_grid = ResumeGrid::Committed;
        self.st.freeze.blocked = self.st.freeze.down;
        self.st.held.clear();
        self.st.zen_taps.clear();
        self.st.zen_stomp = false;
        if !self.st.freeze.frozen {
            self.hold_weapons();
            self.st.freeze.frozen = true;
            self.st.freeze.since = self.st.s;
            self.st.freeze.beat_at_open = self.sbi_at(self.st.s);
            self.st.freeze.confirmed = false;
            self.st.freeze.selected = 0;
            let entries = std::mem::take(&mut self.st.q.heap);
            for entry in entries {
                if entry.ev.control() {
                    self.st.q.insert(entry);
                } else if !entry.ev.cadence() {
                    self.hold_world(entry);
                }
            }
            // A previously known victory beat must wait for the stopped run clock too.
            if self
                .st
                .plan
                .finale
                .as_ref()
                .is_some_and(|f| f.s > self.st.s)
            {
                let landmark = self
                    .env
                    .director
                    .set_pieces
                    .as_ref()
                    .and_then(|p| p.finale.landmark.clone());
                self.st.plan.finale = None;
                self.st.end_s = self.st.until_s;
                if landmark.is_some() {
                    self.st.plan.landmarks = self.st.plan.landmarks.saturating_sub(1);
                }
                self.st.freeze.held.retain(|h| !matches!(h.entry.ev, Ev::Blast { .. })
                    && !matches!(&h.entry.ev, Ev::Landmark { id } if Some(id)==landmark.as_ref()));
            }
        }
        self.publish_run();
    }

    fn hold_world(&mut self, entry: Entry) {
        let ready_run = self.st.run_s + (entry.key.s - self.st.s).max(0.0);
        self.st.freeze.held.push(Held { ready_run, entry });
    }

    /// Called before dispatch. Keys still arrive, but never reach a movement handler while frozen.
    pub(super) fn freeze_key(&mut self, key: &KeyEvent) -> bool {
        if self.env.input.level.survivors.is_none() {
            return matches!(key.key, InputKey::Confirm | InputKey::Pause);
        }
        let slot = key.key.slot();
        let was_down = self.st.freeze.down[slot];
        self.st.freeze.down[slot] = key.kind == KeyEventKind::Press;
        if key.kind == KeyEventKind::Release {
            self.st.freeze.blocked[slot] = false;
            return self.st.freeze.frozen || matches!(key.key, InputKey::Confirm | InputKey::Pause);
        }
        if was_down || self.st.freeze.blocked[slot] {
            return true;
        }
        if key.key == InputKey::Pause {
            if self.st.freeze.reasons.contains(&FreezeReason::Paused) {
                self.st
                    .freeze
                    .reasons
                    .retain(|r| *r != FreezeReason::Paused);
                self.request_resume();
            } else {
                self.enter_freeze(FreezeReason::Paused);
            }
            return true;
        }
        if !self.st.freeze.frozen {
            return key.key == InputKey::Confirm;
        }
        let interaction = self
            .st
            .freeze
            .reasons
            .iter()
            .any(|r| matches!(r, FreezeReason::LevelUp | FreezeReason::Chest));
        if interaction {
            match key.key {
                InputKey::Left => self.st.freeze.selected = 0,
                InputKey::Up => self.st.freeze.selected = 1,
                InputKey::Right => self.st.freeze.selected = 2,
                InputKey::Confirm | InputKey::Space => {
                    if self.st.survivor.as_ref().is_some_and(|p| p.offer.is_some()) {
                        self.pick_survivor_offer();
                        self.publish_run();
                        return true;
                    }
                    self.st.freeze.confirmed = true;
                    self.st
                        .freeze
                        .reasons
                        .retain(|r| !matches!(r, FreezeReason::LevelUp | FreezeReason::Chest));
                    self.request_resume();
                }
                InputKey::Down | InputKey::Pause => {}
            }
            self.publish_run();
        }
        true
    }

    pub(super) fn request_resume(&mut self) {
        if self.st.freeze.reasons.is_empty() {
            // Resume on a committed beat, or a free pulse when no beat is known.
            // Ingestion can replace a pending free pulse with a newly committed beat.
            let beat = self.log.beats[..self.st.ingested.beats]
                .iter()
                .find(|b| b.t > self.st.s + 1e-9)
                .map(|b| b.t);
            self.st.freeze.resume_grid = if beat.is_none() {
                ResumeGrid::Free
            } else {
                ResumeGrid::Committed
            };
            self.st.freeze.resume = beat.or_else(|| Some(self.next_stride(self.st.s + 1e-6)));
        }
    }

    pub(super) fn freeze_stop(&self) -> f64 {
        self.st.freeze.resume.unwrap_or(f64::INFINITY)
    }

    pub(super) fn maybe_resume(&mut self) {
        if self.st.freeze.resume.is_none_or(|at| at > self.st.s + 1e-9) {
            return;
        }
        if self.st.freeze.reasons.contains(&FreezeReason::MusicStopped) {
            self.st
                .freeze
                .reasons
                .retain(|r| *r != FreezeReason::MusicStopped);
            self.st.freeze.music_pending = false;
            self.st.freeze.music_lock = None;
        }
        self.st.freeze.resume = None;
        if !self.st.freeze.reasons.is_empty() {
            return;
        }
        self.st.freeze.frozen = false;
        self.st.freeze.last = None;
        self.st.freeze.music_lock = None;
        self.st.freeze.music_pending = false;
        let gap = self.st.s - self.st.freeze.since;
        self.st.freeze.total_s += gap;
        if let Some(p) = &mut self.st.survivor {
            p.arsenal.resume(self.st.freeze.total_s);
        }
        // World-owned song timestamps keep their remaining lifetime; world timestamps did not move.
        if self.st.stop_end > self.st.freeze.since {
            self.st.stop_end += gap;
        }
        self.st.dip_start += gap;
        self.st.plan.zone_last += gap;
        for punch in &mut self.st.punches {
            if punch.s >= self.st.freeze.since {
                punch.s += gap;
            }
        }
        let shift = self.sbi_at(self.st.s) - self.st.freeze.beat_at_open;
        for d in &mut self.st.deferred {
            d.bi += shift;
            if let Some(need) = &mut d.need {
                *need = (*need as f64 + shift).ceil().max(0.0) as usize;
            }
        }
        // Do not catch up a contact attack across a menu. The next offbeat winds afresh.
        for foe in &mut self.st.foes {
            foe.attack = None;
        }
        self.requeue_held();
        self.queue_cadence();
        self.arm_free_pulse();
        self.publish_run();
    }

    /// Incoming finite events are held; cadence events are regenerated once on resume.
    pub(super) fn dispatch_entry(&mut self, entry: Entry) {
        if self.st.freeze.frozen && !entry.ev.control() {
            if !entry.ev.cadence() {
                self.hold_world(entry);
            }
        } else {
            self.handle(entry.ev);
        }
    }

    pub(super) fn requeue_held(&mut self) {
        if self.st.freeze.frozen {
            return;
        }
        let held = std::mem::take(&mut self.st.freeze.held);
        let strike_due: Vec<(usize, f64)> = held
            .iter()
            .filter_map(|h| match h.entry.ev {
                Ev::Strike(sw) => Some((sw, self.st.s + (h.ready_run - self.st.run_s).max(0.0))),
                _ => None,
            })
            .collect();
        for mut h in held {
            if let Ev::Wind(sw) = h.entry.ev
                && strike_due
                    .iter()
                    .find(|(i, _)| *i == sw)
                    .is_some_and(|(_, due)| self.known_grid_after(*due, 1.0).is_none())
            {
                self.st.freeze.held.push(h);
                continue;
            }
            let due = self.st.s + (h.ready_run - self.st.run_s).max(0.0);
            if matches!(h.entry.ev, Ev::WandBullet(_)) {
                h.entry.key.s = due;
                self.st.q.insert(h.entry);
                continue;
            }
            let step = match &h.entry.ev {
                Ev::Fire { weapon, .. } => super::combat::subdivision_beats(
                    self.env.input.lib.weapons[*weapon].fires.every,
                    2.0,
                ),
                Ev::Chain { weapon, .. } => super::combat::subdivision_beats(
                    self.env.input.lib.weapons[*weapon]
                        .chain
                        .as_ref()
                        .expect("a chain")
                        .step,
                    2.0,
                ),
                Ev::Wave { .. }
                | Ev::Gem { .. }
                | Ev::Recoil { .. }
                | Ev::EnemyImpact { .. }
                | Ev::MissileEnd(_)
                | Ev::ChickenHeal { .. }
                | Ev::LightningStrike(_) => 0.25,
                _ => 1.0,
            };
            let grid = self.known_grid_after(due, step).or_else(|| {
                matches!(
                    h.entry.ev,
                    Ev::Recoil { .. }
                        | Ev::EnemyImpact { .. }
                        | Ev::MissileEnd(_)
                        | Ev::ChickenHeal { .. }
                        | Ev::LightningStrike(_)
                )
                .then(|| (0.0, self.next_enemy_sixteenth(due)))
            });
            let Some((bi, at)) = grid else {
                self.st.freeze.held.push(h);
                continue;
            };
            match &mut h.entry.ev {
                Ev::Fire { bi: b, .. } => *b = bi,
                Ev::Chain { chain, jump, .. } => {
                    self.st.chains[*chain].bi = bi - *jump as f64 * step;
                }
                Ev::ZoneFallback { at: previous } => {
                    // A newer section can cancel this fallback while it waits for a grid.
                    if self.st.plan.zone_check != Some(*previous) {
                        continue;
                    }
                    // Its check and interval belong to the held world clock too.
                    self.st.plan.zone_check = Some(at);
                    self.st.plan.zone_last = at - self.env.director.zone_fallback_s;
                    *previous = at;
                }
                Ev::ZoneWave {
                    section: Some(section),
                    ..
                } if *section == self.st.plan.zone_section => {
                    self.st.plan.zone_last = at;
                }
                Ev::Wind(sw) => self.st.swing_pool[*sw].s_wind = at,
                Ev::Strike(sw) => {
                    let swing = &mut self.st.swing_pool[*sw];
                    let shift = at - swing.s_strike;
                    swing.s_strike = at;
                    swing.s_armed += shift;
                    if let Some(record) = &mut swing.record {
                        record.s = at;
                        record.s_armed = swing.s_armed;
                        record.s_wind = swing.s_wind;
                    }
                    self.st.taken_slots.insert(bi as usize);
                }
                _ => {}
            }
            h.entry.key.s = at;
            self.st.q.insert(h.entry);
        }
    }

    /// A finite stand-in grid, phased from the last committed beat at/before the deadline.
    pub(super) fn free_grid_at_or_after(&self, due: f64, step: f64) -> f64 {
        let period = self.env.rule.free_period_s * step;
        let anchor = self.log.beats[..self.st.ingested.beats]
            .iter()
            .rev()
            .find(|b| b.t <= due)
            .map_or(0.0, |b| b.t);
        anchor + (((due - anchor) / period - 1e-9).ceil().max(0.0)) * period
    }

    pub(super) fn known_grid_after(&self, due: f64, step: f64) -> Option<(f64, f64)> {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let due = due.max(self.st.s);
        // Fractional steps require both bounding beats; extrapolation would read an unknown grid.
        let first = beats
            .partition_point(|b| b.t < due - 1e-9)
            .saturating_sub(1);
        for (i, beat) in beats.iter().enumerate().skip(first) {
            let mut bi = (i as f64 / step - 1e-9).ceil() * step;
            while bi < i as f64 + 1.0 - 1e-9 {
                let at = if bi.fract().abs() < 1e-9 {
                    beat.t
                } else {
                    let next = beats.get(i + 1)?;
                    beat.t + (next.t - beat.t) * bi.fract()
                };
                if at >= due - 1e-9 {
                    return Some((bi, at));
                }
                bi += step;
            }
        }
        None
    }

    pub(super) fn queue_cadence(&mut self) {
        for bi in 0..self.st.ingested.beats {
            if self.log.beats[bi].t < self.st.s - 1e-9 {
                continue;
            }
            self.queue_beat(bi);
        }
        let steps = &self.st.sch.stride_steps;
        let times: Vec<(usize, f64)> = steps.s[..steps.tail()]
            .iter()
            .copied()
            .enumerate()
            .filter(|(_, s)| *s >= self.st.s - 1e-9)
            .collect();
        for (k, s) in times {
            self.st.q.push_init(s, Ev::Held, phase::HELD, k as u64);
        }
    }

    pub(super) fn queue_beat(&mut self, bi: usize) {
        if self.st.freeze.frozen {
            return;
        }
        let b = self.log.beats[bi];
        let period = bi
            .checked_sub(1)
            .map_or(b.period, |i| b.t - self.log.beats[i].t);
        self.queue_enemy_wind(b.t, period, None);
        self.st
            .q
            .push_init(b.t, Ev::Beat { bi }, phase::BEAT, bi as u64);
    }

    pub(super) fn arm_free_pulse(&mut self) {
        if self.env.input.level.survivors.is_none() {
            return;
        }
        self.st.freeze.pulse_generation += 1;
        let generation = self.st.freeze.pulse_generation;
        let last = self.log.beats[..self.st.ingested.beats]
            .last()
            .map_or(0.0, |b| b.t);
        let peak = (last + self.env.rule.free_period_s).max(self.st.s);
        self.queue_enemy_wind(peak, self.env.rule.free_period_s, Some(generation));
        self.st.q.push(peak, Ev::FreePulse { generation });
    }

    pub(super) fn free_pulse(&mut self, generation: u64) {
        if generation != self.st.freeze.pulse_generation {
            return;
        }
        if !self.st.freeze.frozen && !self.st.blasting {
            self.held_stride();
            self.spawn_director(self.sbi_at(self.st.s));
            self.sword_free_pulse();
        }
        self.queue_enemy_wind(
            self.st.s + self.env.rule.free_period_s,
            self.env.rule.free_period_s,
            Some(generation),
        );
        self.st.q.push(
            self.st.s + self.env.rule.free_period_s,
            Ev::FreePulse { generation },
        );
    }
}
