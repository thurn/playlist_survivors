//! Boss notices share the simulation clock and camera, including freezes and rollback.
use super::Sim;
use crate::survivors::{BossCue, BossCueKind, boss_bar};
impl Sim {
    fn next_boss_notice(&self) -> Option<(usize, f64)> {
        if self.st.survivor.is_none() || self.st.freeze.frozen || self.st.blasting {
            return None;
        }
        let beats = &self.log.beats[..self.st.ingested.beats];
        self.env
            .input
            .level
            .survivor_schedule
            .iter()
            .filter(|row| {
                !row.bosses.is_empty() && !self.st.boss_presentation.warned.contains(&row.minute)
            })
            .map(|row| {
                let raw = row.minute as f64 * 60.0 + self.st.freeze.total_s;
                let (land, mut period) = boss_bar(raw, self.st.s, beats);
                if !beats.iter().any(|b| b.known_at <= self.st.s + 1e-9) {
                    period = self.env.rule.free_period_s;
                }
                let warning = land - 4.0 * period;
                let at = if warning < self.st.s - 1e-8 {
                    boss_bar(self.st.s, self.st.s, beats).0
                } else {
                    boss_bar(warning.max(self.st.s), self.st.s, beats).0
                };
                (row.minute, at)
            })
            .min_by(|a, b| a.1.total_cmp(&b.1))
    }
    pub(super) fn boss_notice_stop(&self) -> f64 {
        self.next_boss_notice().map_or(f64::INFINITY, |(_, at)| at)
    }
    pub(super) fn tick_boss_notice(&mut self) {
        let Some((minute, at)) = self.next_boss_notice() else {
            return;
        };
        if at > self.st.s + 1e-9 {
            return;
        }
        self.st.boss_presentation.warned.push(minute);
        if self.st.boss_presentation.pending == 0 {
            self.st.boss_presentation.warning_run = Some(self.st.run_s);
        }
        self.st.boss_presentation.pending += self
            .env
            .input
            .level
            .survivor_schedule
            .iter()
            .find(|r| r.minute == minute)
            .map_or(0, |r| r.bosses.len());
        self.st.boss_presentation.landed_run = None;
        self.boss_cue(BossCueKind::Warning, minute, 0.0, 0.0);
    }
    pub(super) fn boss_cue(&mut self, kind: BossCueKind, id: usize, x: f64, y: f64) {
        if kind == BossCueKind::Landed {
            self.st.boss_presentation.pending = self.st.boss_presentation.pending.saturating_sub(1);
            if self.st.boss_presentation.pending == 0 {
                self.st.boss_presentation.landed_run = Some(self.st.run_s);
            }
        }
        self.rec.boss_cue(BossCue {
            kind,
            pending: self.st.boss_presentation.pending,
            song: self.st.s,
            run: self.st.run_s,
            id,
            x,
            y,
        });
    }
}
