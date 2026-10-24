//! Judging: the grid as known, when presses are judged, the groove meter and its tiers, beatless
//! credit, the settled judgment and the drain (simulate.ts's reactive mode and input sections).

use std::rc::Rc;

use super::{BEATLESS, FAIR, PendingSettle, Sim};
use crate::core::shaping::clamp;
use crate::jsmath::max;
use crate::reactive::director::tick_index_in;
use crate::reactive::settle::{READING, SETTLE_DELAY, judgement_label};
use crate::recorder::SettleRecord;
use crate::timing::{drain_per_beat, first_at_or_after, groove_after_change, tier_for};

impl Sim {
    /// The latest tick at or before song time s among those taken in (`tickIndexAt`).
    fn tick_index(&self, s: f64) -> isize {
        tick_index_in(&self.log.ticks[..self.st.ingested.ticks], s)
    }

    /// The timing grid as known at song time s: the committed beats known by then (a prefix).
    #[allow(clippy::manual_midpoint)]
    pub(super) fn grid_known_at(&self, s: f64) -> &[f64] {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let (mut lo, mut hi) = (0usize, beats.len());
        while lo < hi {
            let mid = (lo + hi) >> 1;
            if beats[mid].known_at <= s + 1e-9 {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        let grid = &self.st.sch.grid_times;
        &grid[..lo.min(grid.len())]
    }

    /// Are presses judged at song time s (the beat clear for a second at least)?
    pub(super) fn clear_at(&self, s: f64) -> bool {
        self.st.judging.judged_at(self.tick_index(s))
    }

    fn evidence_at(&self, s: f64) -> f64 {
        let i = self.tick_index(s);
        if i < 0 {
            0.0
        } else {
            self.log.ticks[i as usize].evidence
        }
    }

    /// Judged time from song time 0 to t (`judgedClock`).
    fn judged_clock(&self, t: f64) -> f64 {
        self.st.judging.clock(
            t,
            if t.is_finite() {
                self.tick_index(t)
            } else {
                -1
            },
        )
    }

    /// (hv-s4pt.12) How much of a penalty counts at song time t: scaled by the tracker's confidence.
    fn penalty_scale(&self, t: f64) -> f64 {
        let gate = self.env.input.gate;
        FAIR.floor
            + (1.0 - FAIR.floor)
                * clamp(
                    (self.evidence_at(t) - gate) / max(1e-6, FAIR.full_at - gate),
                    0.0,
                    1.0,
                )
    }

    /// (hv-s4pt.13) Is song time t in the lock-on grace?
    fn in_grace(&self, t: f64) -> bool {
        let lock = self.st.judging.lock_on_at(self.tick_index(t));
        !lock.is_nan() && self.judged_clock(t) - self.judged_clock(lock) < BEATLESS.grace_s - 1e-9
    }

    fn drain_graced(&self, b: f64, bar_s: f64) -> bool {
        self.in_grace(b)
            && self.st.steady_run >= FAIR.steady_run
            && self.st.last_press_s >= b - bar_s - 1e-9
    }

    /// (hv-s4pt.13) A beat whose presses are not judged earns the groove a Perfect's worth once its
    /// stretch without judging has lasted BEATLESS.afterS. True if the beat went unjudged.
    fn beatless_credit(&mut self, b: f64) -> bool {
        if self.clear_at(b) {
            return false;
        }
        if b - self.st.judging.unjudged_since_at(self.tick_index(b)) < BEATLESS.after_s - 1e-9 {
            return true;
        }
        let before = self.st.groove;
        self.st.groove = f64::min(1.0, self.st.groove + BEATLESS.per_beat);
        if self.st.groove != before {
            self.rec.groove(self.st.s, self.st.groove);
            self.set_tier();
        }
        true
    }

    /// Judged time at song time t since the latest raise, or since judging last resumed if later.
    fn judged_since_raise(&self, t: f64) -> f64 {
        let resumed = self.st.judging.judged_since_at(self.tick_index(t));
        let from = if resumed.is_nan() {
            self.st.last_raise_s
        } else {
            max(self.st.last_raise_s, resumed)
        };
        self.judged_clock(t) - self.judged_clock(from)
    }

    /// The groove after a press's change, and the tier it sets.
    pub(super) fn meter_change(&mut self, change: f64) {
        let before = self.st.groove;
        self.st.groove = groove_after_change(self.st.groove, change);
        if change > 0.0 {
            self.st.last_raise_s = self.st.s;
        }
        if self.st.groove != before {
            self.rec.groove(self.st.s, self.st.groove);
        }
        self.set_tier();
    }

    /// The tier for the groove now; a change sounds its chime on the next grid time.
    pub(super) fn set_tier(&mut self) {
        let t = tier_for(&self.env.input.lib.hero, self.st.tier, self.st.groove);
        if t == self.st.tier {
            return;
        }
        self.st.tier = t;
        let s = self.st.s;
        let grid = self.grid_known_at(s);
        let sound = grid
            .get(first_at_or_after(grid, s + 1e-6))
            .copied()
            .unwrap_or(s);
        self.rec.tier(s, self.st.w, t, sound);
    }

    /// Stage 2: a press settles 2 s after it, against the reading of the latest hindsight entry.
    pub(super) fn settle_press(&mut self, p: &PendingSettle) {
        let env = Rc::clone(&self.env);
        let s = self.st.s;
        let pr = p.press;
        let entry = self.log.hindsight.as_ref().and_then(|h| {
            let known = &h[..self.st.ingested.hindsight];
            let n = known.partition_point(|x| x.known_at <= s + 1e-9);
            if n > 0 {
                Some(known[n - 1].clone())
            } else {
                None
            }
        });
        // After an unclear stretch the reading starts again, and only presses since judging resumed choose it.
        let resumed = if pr.unclear {
            f64::NAN
        } else {
            self.st.judging.judged_since_at(self.tick_index(pr.s))
        };
        // (NaN never equals the last run: a NaN run restarts the reading each time, as in TS.)
        #[allow(clippy::float_cmp)]
        let changed = resumed != self.st.settle_run;
        if !pr.unclear && changed {
            self.st.settle_run = resumed;
            self.st.settler.restart();
        }
        let res = entry.as_ref().map(|entry| {
            let from = max(
                entry.known_at - READING.window_s,
                if resumed.is_nan() {
                    f64::NEG_INFINITY
                } else {
                    resumed
                },
            );
            #[allow(clippy::float_cmp)]
            let window: Vec<f64> = self
                .st
                .press_log
                .iter()
                .filter(|x| {
                    !x.1 && x.0 != pr.s && x.0 >= from - 1e-9 && x.0 <= entry.known_at + 1e-9
                })
                .map(|x| x.0)
                .collect();
            self.st
                .settler
                .settle(&env.input.lib.hero, &pr, entry, &window)
        });
        let judgement = res.as_ref().and_then(|r| r.judgement);
        let settled_change = judgement.map_or(0.0, |j| j.change);
        let kept = FAIR.benefit_of_doubt
            && p.steady
            && judgement.is_some()
            && p.provisional_change > settled_change;
        let raw = if kept {
            p.provisional_change
        } else {
            settled_change
        };
        let change = if raw < 0.0 && self.in_grace(pr.s) {
            if p.steady {
                0.0
            } else {
                raw * self.penalty_scale(pr.s) * BEATLESS.unsteady_grace
            }
        } else if raw < 0.0 || !p.steady {
            raw * self.penalty_scale(pr.s)
        } else {
            raw
        };
        let before = self.st.groove;
        self.st.groove = groove_after_change(self.st.groove, change);
        if change > 0.0 {
            self.st.last_raise_s = max(self.st.last_raise_s, pr.s);
        }
        if self.st.groove != before {
            self.rec.groove(s, self.st.groove);
        }
        self.set_tier();
        let reading = res
            .as_ref()
            .map_or_else(|| "none".to_string(), |r| r.reading.chosen.label());
        let settled = if kept {
            p.provisional.clone()
        } else {
            judgement_label(judgement.as_ref())
        };
        self.rec.settle(SettleRecord {
            s,
            p: pr.s,
            provisional: p.provisional.clone(),
            settled,
            change,
            reading,
            switched: res.as_ref().is_some_and(|r| r.reading.switched),
            known_at: entry.as_ref().map_or(-1.0, |e| e.known_at),
            evidence: self.evidence_at(pr.s),
            kept: kept.then(|| judgement_label(judgement.as_ref())),
        });
        while self
            .st
            .press_log
            .first()
            .is_some_and(|x| x.0 < s - READING.window_s - SETTLE_DELAY - 2.0)
        {
            self.st.press_log.remove(0);
        }
    }

    /// The drain as of beat bi at song time b: a bar after the latest press that raised the groove,
    /// while presses are judged (stage 2 runs it 2 s late, on the settled groove's timeline).
    pub(super) fn drain(&mut self, bi: usize, b: f64) {
        // (A beat without judging earns its credit instead.)
        if self.beatless_credit(b) {
            return;
        }
        let bpb = super::foes::get_or(&self.st.idx.beats_per_bar, 1.0, 2.0);
        let beats = &self.st.song.beats;
        let beat_s = if bi > 0 {
            beats[bi] - beats[bi - 1]
        } else {
            self.log.beats[0].period
        };
        let graced = self.drain_graced(b, bpb * beat_s);
        let hero = &self.env.input.lib.hero;
        if self.st.groove > 0.0
            && self.clear_at(b)
            && !graced
            && self.judged_since_raise(b) > hero.meter.drain.after_bars * bpb * beat_s + 1e-9
        {
            self.st.groove = max(
                0.0,
                self.st.groove - drain_per_beat(hero, bpb) * self.penalty_scale(b),
            );
            self.rec.groove(self.st.s, self.st.groove);
            self.set_tier();
        }
    }
}
