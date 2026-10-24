//! `src/reactive/stream.ts`'s `judging()` and its readers, extended tick by tick as ticks arrive.
//!
//! When the beat is judged: judging stops at the first tick below the gate and resumes once the beat
//! has been clear for `JUDGE_AFTER_S`. Each tick's answer reads only ticks at or before it, except
//! that TS takes a judged run's end (`lastJudged`) from the next tick: here it is taken when that
//! tick arrives, before anything reads it, so the result is the same. The judged-time clock
//! (`judgedClock` in simulate.ts) is kept alongside.

use stream::Tick;

/// How long the beat must have been clear before presses are judged again.
pub const JUDGE_AFTER_S: f64 = 1.0;
/// A stretch without judging this long or longer makes the next run of judging a lock-on.
pub const LOCK_GAP_S: f64 = 4.0;

#[derive(Clone, Debug, PartialEq)]
pub struct Judging {
    gate: f64,
    /// Per tick: is the beat judged from it to the next?
    pub judged: Vec<bool>,
    /// Per judged tick: when its run of judged ticks began (NaN otherwise).
    pub since: Vec<f64>,
    /// Per tick: when the latest lock-on began (NaN before any, and at ticks below the gate).
    pub lock_on: Vec<f64>,
    /// Per unjudged tick: when its stretch without judging began (NaN when judged).
    pub unjudged_since: Vec<f64>,
    /// Judged time up to each tick (each judged tick holding to the next).
    cum: Vec<f64>,
    times: Vec<f64>,
    clear_from: f64,
    last_judged: f64,
    lock: f64,
}

impl Judging {
    pub fn new(gate: f64) -> Judging {
        Judging {
            gate,
            judged: Vec::new(),
            since: Vec::new(),
            lock_on: Vec::new(),
            unjudged_since: Vec::new(),
            cum: vec![0.0],
            times: Vec::new(),
            clear_from: f64::NAN,
            last_judged: 0.0,
            lock: f64::NAN,
        }
    }

    pub fn len(&self) -> usize {
        self.judged.len()
    }

    pub fn is_empty(&self) -> bool {
        self.judged.is_empty()
    }

    /// Takes in `ticks[self.len()..]` (the ticks are the stream's, in order).
    pub fn extend(&mut self, ticks: &[Tick]) {
        for tick in &ticks[self.len()..] {
            let j = self.judged.len();
            let t = tick.t;
            if j > 0 {
                // (TS: the previous judged tick's run ends at this tick's time; the judged clock
                // counts the previous tick's span.)
                if self.judged[j - 1] {
                    self.last_judged = t;
                }
                let prev = self.cum[j - 1]
                    + if self.judged[j - 1] {
                        t - self.times[j - 1]
                    } else {
                        0.0
                    };
                self.cum.push(prev);
            }
            self.times.push(t);
            let mut judged = false;
            let mut since = f64::NAN;
            let mut lock_on = f64::NAN;
            if tick.evidence < self.gate {
                self.clear_from = f64::NAN;
            } else {
                if self.clear_from.is_nan() {
                    self.clear_from = t;
                }
                if t - self.clear_from >= JUDGE_AFTER_S - 1e-9 {
                    judged = true;
                    let run = j > 0 && self.judged[j - 1];
                    since = if run { self.since[j - 1] } else { t };
                    if !run && t - self.last_judged >= LOCK_GAP_S - 1e-9 {
                        self.lock = t;
                    }
                }
                lock_on = self.lock;
            }
            let unjudged_since = if judged {
                f64::NAN
            } else if j > 0 && !self.judged[j - 1] {
                self.unjudged_since[j - 1]
            } else if j > 0 {
                t
            } else {
                0.0
            };
            self.judged.push(judged);
            self.since.push(since);
            self.lock_on.push(lock_on);
            self.unjudged_since.push(unjudged_since);
        }
    }

    /// Is the beat judged at tick index i (`judgedAt` with i = tickIndexAt(s))?
    pub fn judged_at(&self, i: isize) -> bool {
        i >= 0 && self.judged[i as usize]
    }

    /// When the run of judging tick i falls in began (NaN when not judged).
    pub fn judged_since_at(&self, i: isize) -> f64 {
        if i >= 0 {
            self.since[i as usize]
        } else {
            f64::NAN
        }
    }

    /// When the latest lock-on began, as of tick i (NaN before any).
    pub fn lock_on_at(&self, i: isize) -> f64 {
        if i >= 0 {
            self.lock_on[i as usize]
        } else {
            f64::NAN
        }
    }

    /// When the stretch without judging tick i falls in began (NaN when judged; 0 before the first tick).
    pub fn unjudged_since_at(&self, i: isize) -> f64 {
        if i >= 0 {
            self.unjudged_since[i as usize]
        } else {
            0.0
        }
    }

    /// Judged time from song time 0 to t, with i = tickIndexAt(t) (`judgedClock`).
    pub fn clock(&self, t: f64, i: isize) -> f64 {
        if !t.is_finite() {
            return t;
        }
        if i < 0 {
            return 0.0;
        }
        let j = i as usize;
        self.cum[j]
            + if self.judged[j] {
                t - self.times[j]
            } else {
                0.0
            }
    }
}
