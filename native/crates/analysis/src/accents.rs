//! Accent forecasts (`accents.py`): strong onsets repeating at one position of a 2-, 4- or
//! 8-beat cycle of committed beats, three cycles in a row, forecast on the committed beat a cycle
//! ahead, streaming.
//!
//! Python computes the forecasts from the whole stream at once and stamps each with the latest
//! `knownAt` of what it read: the target beat, and when each confirming beat's strong onset was
//! first known (`hit_times`). Streamed, an input arrives at its `knownAt`, so a forecast becomes
//! computable exactly when Python stamps it: each beat or strong onset marks the targets it could
//! complete, and `take` decides them once a frame's inputs are in, keeping Python's choice among
//! cycles (the earliest known, then the most confident, then the shortest cycle). Memory is
//! bounded by the longest cycle's history.

use std::collections::{BTreeSet, VecDeque};

use crate::beats::Commit;
use crate::onsets::Onset;

/// The forecasts' parameters (`stream.py`'s `Params.forecast_*`; `manifest.json`'s
/// `streamParams.accents`).
#[derive(Clone, Debug, PartialEq)]
pub struct AccentParams {
    pub cycles: Vec<usize>,
    pub confirm: usize,
    pub window_s: f64,
    pub min_lead_s: f64,
    pub history: usize,
}

impl Default for AccentParams {
    fn default() -> Self {
        AccentParams {
            cycles: vec![2, 4, 8],
            confirm: 3,
            window_s: 0.05,
            min_lead_s: 0.54,
            history: 4,
        }
    }
}

/// A forecast strong hit on a committed beat.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Forecast {
    pub t: f64,
    pub known_at: f64,
    pub cycle: usize,
    pub confidence: f64,
}

struct Beat {
    t: f64,
    known_at: f64,
    /// When a strong onset within the window of it was first known (no earlier than the beat).
    hit: Option<f64>,
}

/// The forecasting stage.
pub struct Accents {
    p: AccentParams,
    /// The last committed beats; `beats[0]` is beat number `first`.
    beats: VecDeque<Beat>,
    first: usize,
    keep: usize,
    /// Strong onsets (t, knownAt) that may still fall by a beat.
    strong: VecDeque<(f64, f64)>,
    /// Beats a new input may have completed a forecast for.
    dirty: BTreeSet<usize>,
    /// Beats already forecast.
    forecast: BTreeSet<usize>,
}

impl Accents {
    pub fn new(p: &AccentParams) -> Accents {
        let longest = p.cycles.iter().copied().max().unwrap_or(1);
        Accents {
            keep: longest * (p.history.max(p.confirm) + 1) + 1,
            p: p.clone(),
            beats: VecDeque::new(),
            first: 0,
            strong: VecDeque::new(),
            dirty: BTreeSet::new(),
            forecast: BTreeSet::new(),
        }
    }

    fn within(&self, onset_t: f64, beat_t: f64) -> bool {
        (onset_t - beat_t).abs() <= self.p.window_s + 1e-9
    }

    fn beat(&self, n: usize) -> Option<&Beat> {
        n.checked_sub(self.first).and_then(|k| self.beats.get(k))
    }

    /// A beat's hit changed: it may complete forecasts a cycle or more after it.
    fn mark_hit(&mut self, n: usize) {
        for &c in &self.p.cycles {
            for m in 1..=self.p.confirm {
                self.dirty.insert(n + m * c);
            }
        }
    }

    /// A committed beat (in order).
    pub fn push_beat(&mut self, c: &Commit) {
        let n = self.first + self.beats.len();
        let hit = self
            .strong
            .iter()
            .filter(|&&(t, _)| self.within(t, c.t))
            .map(|&(_, k)| k)
            .reduce(f64::min)
            .map(|k| k.max(c.known_at));
        self.beats.push_back(Beat {
            t: c.t,
            known_at: c.known_at,
            hit,
        });
        self.dirty.insert(n);
        if hit.is_some() {
            self.mark_hit(n);
        }
        while self.beats.len() > self.keep {
            self.beats.pop_front();
            self.first += 1;
        }
        // (A later beat is later still, so onsets well before this one can't fall by one.)
        while self.strong.front().is_some_and(|&(t, _)| t < c.t - 1.0) {
            self.strong.pop_front();
        }
        let first = self.first;
        self.dirty.retain(|&j| j >= first);
        self.forecast.retain(|&j| j >= first);
    }

    /// An onset (in order); only strong ones count.
    pub fn push_onset(&mut self, o: &Onset) {
        if !o.strong {
            return;
        }
        self.strong.push_back((o.t, o.known_at));
        let mut hits = Vec::new();
        for (k, b) in self.beats.iter_mut().enumerate() {
            if b.hit.is_none() && (o.t - b.t).abs() <= self.p.window_s + 1e-9 {
                b.hit = Some(o.known_at.max(b.known_at));
                hits.push(self.first + k);
            }
        }
        for n in hits {
            self.mark_hit(n);
        }
    }

    /// The forecasts the inputs so far complete, in beat order.
    pub fn take(&mut self, out: &mut Vec<Forecast>) {
        let dirty = std::mem::take(&mut self.dirty);
        let last = self.first + self.beats.len();
        for j in dirty {
            if j >= last {
                // (Not committed yet: its own arrival marks it again.)
                self.dirty.insert(j);
                continue;
            }
            if self.forecast.contains(&j) {
                continue;
            }
            if let Some(f) = self.best(j) {
                self.forecast.insert(j);
                out.push(f);
            }
        }
    }

    /// `forecasts`' choice for beat `j` among the cycles whose runs are complete.
    fn best(&self, j: usize) -> Option<Forecast> {
        let target = self.beat(j)?;
        let mut best: Option<Forecast> = None;
        for &cycle in &self.p.cycles {
            let Some(i) = j.checked_sub(cycle) else {
                continue;
            };
            if (self.p.confirm - 1) * cycle > i {
                continue;
            }
            let mut known = target.known_at;
            let mut complete = true;
            for m in 0..self.p.confirm {
                match self.beat(i - m * cycle).and_then(|b| b.hit) {
                    Some(h) => known = known.max(h),
                    None => complete = false,
                }
            }
            if !complete || target.t - known < self.p.min_lead_s - 1e-9 {
                continue;
            }
            let hits = (0..self.p.history)
                .filter(|&m| {
                    i.checked_sub(m * cycle)
                        .and_then(|k| self.beat(k))
                        .is_some_and(|b| b.hit.is_some())
                })
                .count();
            let confidence = hits as f64 / self.p.history as f64;
            let entry = Forecast {
                t: target.t,
                known_at: known,
                cycle,
                confidence,
            };
            let better = best.is_none_or(|b| {
                (known, -confidence, cycle)
                    .partial_cmp(&(b.known_at, -b.confidence, b.cycle))
                    .is_some_and(std::cmp::Ordering::is_lt)
            });
            if better {
                best = Some(entry);
            }
        }
        best
    }
}
