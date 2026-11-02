//! Ticks every 50 ms (`stream.py`'s ticks and `energies`, `structure.py`'s `levels`): the
//! evidence (from the beliefs: `beats::Evidence`), the energy and the level.
//!
//! - The energy compares the tick's loudness, the mean square of the samples in the past
//!   `energy.windowS` in dB (`loudness_db`: float64 squares, numpy's pairwise mean, `+ 1e-12`), with
//!   a running maximum that decays by `decayDbPerS` and never falls under `floorDb`, mapped to 0-1
//!   over `rangeDb`.
//! - The level moves between quiet, medium and loud with hysteresis, starting medium.

use std::collections::VecDeque;

use crate::numeric::pairwise_sum;

/// The ticks' parameters (`stream.py`'s `Params` and `structure.level`).
#[derive(Clone, Debug, PartialEq)]
pub struct TickParams {
    pub sample_rate: f64,
    pub energy_window_s: f64,
    pub energy_decay_db_per_s: f64,
    pub energy_range_db: f64,
    pub energy_floor_db: f64,
    pub loud_in: f64,
    pub loud_out: f64,
    pub quiet_in: f64,
    pub quiet_out: f64,
}

impl Default for TickParams {
    fn default() -> Self {
        TickParams {
            sample_rate: 44_100.0,
            energy_window_s: 0.5,
            energy_decay_db_per_s: 0.2,
            energy_range_db: 30.0,
            energy_floor_db: -60.0,
            loud_in: 0.8,
            loud_out: 0.7,
            quiet_in: 0.4,
            quiet_out: 0.5,
        }
    }
}

/// A tick's level (`manifest.json`'s codes: quiet 0, medium 1, loud 2).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Level {
    Quiet,
    Medium,
    Loud,
}

impl Level {
    pub fn code(self) -> i32 {
        match self {
            Level::Quiet => 0,
            Level::Medium => 1,
            Level::Loud => 2,
        }
    }
}

/// A tick.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Tick {
    pub t: f64,
    /// Its time, or `M − A` before it when every group spends the lookahead.
    pub known_at: f64,
    pub evidence: f64,
    pub energy: f64,
    pub level: Level,
    /// The key of the past `key.windowS` (from `key.afterS` on).
    pub key: Option<&'static str>,
    pub bands: crate::bands::TickBands,
    pub brightness: f64,
}

/// The energy over the samples heard so far, keeping only the window it reads.
pub struct Energy {
    p: TickParams,
    /// The last samples, ending at absolute sample `received - 1`.
    samples: VecDeque<f32>,
    keep: usize,
    received: usize,
    reference: f64,
    prev_t: f64,
    squares: Vec<f64>,
}

impl Energy {
    /// `lag` is the most samples that may arrive past a tick's time before its energy is taken.
    pub fn new(p: &TickParams, lag: usize) -> Self {
        let window = (p.energy_window_s * p.sample_rate).round_ties_even() as usize;
        let keep = window + lag + 2;
        Energy {
            p: p.clone(),
            samples: VecDeque::with_capacity(keep),
            keep,
            received: 0,
            reference: p.energy_floor_db,
            prev_t: 0.0,
            squares: Vec::with_capacity(window + 2),
        }
    }

    pub fn push(&mut self, samples: &[f32]) {
        for &s in samples {
            if self.samples.len() == self.keep {
                self.samples.pop_front();
            }
            self.samples.push_back(s);
            self.received += 1;
        }
    }

    /// The loudness (dB) of the samples in `(t - window, t]`, as far as they have arrived.
    fn loudness_db(&mut self, t: f64) -> f64 {
        let sr = self.p.sample_rate;
        let hi = ((t * sr).round_ties_even() as usize).min(self.received);
        let lo = ((t - self.p.energy_window_s) * sr)
            .round_ties_even()
            .max(0.0) as usize;
        if hi <= lo {
            return -120.0;
        }
        let first = self.received - self.samples.len();
        assert!(
            lo >= first,
            "energy reads sample {lo}, older than the {} kept",
            self.keep
        );
        self.squares.clear();
        self.squares
            .extend(self.samples.range(lo - first..hi - first).map(|&x| {
                let x = f64::from(x);
                x * x
            }));
        10.0 * (pairwise_sum(&self.squares) / self.squares.len() as f64 + 1e-12).log10()
    }

    /// The energy at tick time `t` (ticks in order).
    pub fn at(&mut self, t: f64) -> f64 {
        let level = self.loudness_db(t);
        let p = &self.p;
        // (Python's max: the first of equal values; equal values are equal here.)
        self.reference = level
            .max(self.reference - p.energy_decay_db_per_s * (t - self.prev_t))
            .max(p.energy_floor_db);
        self.prev_t = t;
        ((level - self.reference + p.energy_range_db) / p.energy_range_db).clamp(0.0, 1.0)
    }
}

/// The level with hysteresis.
pub struct Levels {
    p: TickParams,
    level: Level,
}

impl Levels {
    pub fn new(p: &TickParams) -> Self {
        Levels {
            p: p.clone(),
            level: Level::Medium,
        }
    }

    pub fn step(&mut self, energy: f64) -> Level {
        let p = &self.p;
        if (self.level == Level::Loud && energy < p.loud_out)
            || (self.level == Level::Quiet && energy >= p.quiet_out)
        {
            self.level = Level::Medium;
        }
        if self.level == Level::Medium {
            if energy >= p.loud_in {
                self.level = Level::Loud;
            } else if energy < p.quiet_in {
                self.level = Level::Quiet;
            }
        }
        self.level
    }
}
