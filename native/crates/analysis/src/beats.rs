//! The committed beats (`stream.py`'s `smooth_periods`, `evidence_at` and `commit_beats`,
//! without the hindsight phase check, which A4 adds), streaming with bounded memory:
//! - each frame's period is the mean of the past `period.windowS` of believed periods within
//!   `period.band` of its own (numpy's pairwise sum, so the mean is Python's to the bit);
//! - a tick's evidence is the mean peak activation within ±`evidence.halfS` of the last
//!   `evidence.beats` believed beats heard in full;
//! - the next beat after the last committed one (at least half a period after it and
//!   `commit.minLeadS` ahead of now) is committed once its forecast lies within `commit.periods`
//!   periods plus `commit.extraS` of now, and in any case at the last frame at or before the last
//!   committed beat, so the next beat is always known when a beat arrives; the believed grid is
//!   shifted by `biasS`.

use std::collections::VecDeque;

use crate::dbn::State;

/// The stream's beat parameters (`stream.py`'s `Params`; `manifest.json`'s `streamParams`).
#[derive(Clone, Debug, PartialEq)]
pub struct BeatParams {
    pub fps: f64,
    pub period_window_s: f64,
    pub period_band: f64,
    pub commit_periods: f64,
    pub commit_extra_s: f64,
    pub commit_min_lead_s: f64,
    pub bias_s: f64,
    pub evidence_beats: usize,
    pub evidence_half_s: f64,
    /// Frames per tick (`tickS * fps`).
    pub tick_frames: usize,
}

impl Default for BeatParams {
    fn default() -> Self {
        BeatParams {
            fps: 100.0,
            period_window_s: 2.0,
            period_band: 0.1,
            commit_periods: 1.0,
            commit_extra_s: 0.35,
            commit_min_lead_s: 0.1,
            bias_s: -0.013,
            evidence_beats: 4,
            evidence_half_s: 0.03,
            tick_frames: 5,
        }
    }
}

/// numpy's pairwise summation of float64 (`np.add.reduce`): eight accumulators per block of up
/// to 128 values, halved recursively above that.
pub fn pairwise_sum(a: &[f64]) -> f64 {
    let n = a.len();
    if n < 8 {
        return a.iter().fold(0.0, |s, &x| s + x);
    }
    if n <= 128 {
        let mut r = [0.0f64; 8];
        r.copy_from_slice(&a[..8]);
        let whole = n - n % 8;
        for block in a[8..whole].as_chunks::<8>().0 {
            for (rj, x) in r.iter_mut().zip(block) {
                *rj += x;
            }
        }
        let mut res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
        for &x in &a[whole..] {
            res += x;
        }
        return res;
    }
    let mut half = n / 2;
    half -= half % 8;
    pairwise_sum(&a[..half]) + pairwise_sum(&a[half..])
}

/// Each frame's period: the mean of the past window's believed periods within the band of its own.
pub struct PeriodSmoother {
    window: usize,
    band: f64,
    past: VecDeque<f64>,
    near: Vec<f64>,
}

impl PeriodSmoother {
    pub fn new(p: &BeatParams) -> Self {
        let window = ((p.period_window_s * p.fps).round_ties_even() as usize).max(1);
        PeriodSmoother {
            window,
            band: p.period_band,
            past: VecDeque::with_capacity(window),
            near: Vec::with_capacity(window),
        }
    }

    pub fn step(&mut self, period: f64) -> f64 {
        if self.past.len() == self.window {
            self.past.pop_front();
        }
        self.past.push_back(period);
        self.near.clear();
        self.near.extend(
            self.past
                .iter()
                .copied()
                .filter(|q| (q - period).abs() <= self.band * period),
        );
        pairwise_sum(&self.near) / self.near.len() as f64
    }
}

/// The believed grid at one frame: the last believed beat (model time, unbiased) and the
/// smoothed period.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Belief {
    pub frame: usize,
    pub last: f64,
    pub period: f64,
}

/// The beat tracker's beliefs, frame by frame: the DBN's state with its period smoothed.
pub struct Beliefs {
    smoother: PeriodSmoother,
    fps: f64,
    frame: usize,
}

impl Beliefs {
    pub fn new(p: &BeatParams) -> Self {
        Beliefs {
            smoother: PeriodSmoother::new(p),
            fps: p.fps,
            frame: 0,
        }
    }

    pub fn step(&mut self, state: State) -> Belief {
        let frame = self.frame;
        self.frame += 1;
        Belief {
            frame,
            last: frame as f64 / self.fps - state.since,
            period: self.smoother.step(state.period),
        }
    }
}

/// Tick evidence from the activations heard so far, keeping only the frames it can still read.
pub struct Evidence {
    /// Activation frames kept: the furthest the evidence reads back (see `new`).
    keep: usize,
    beats: usize,
    half_s: f64,
    fps: f64,
    /// Activations from absolute frame `first` on.
    acts: VecDeque<f32>,
    first: usize,
}

impl Evidence {
    /// The evidence for beats no slower than `max_period` (the DBN's `max_period()`). It reads the
    /// last `evidence.beats` beats before the believed last beat, which is less than a period
    /// ago, so at most `since + beats * period + halfS` back: 5.5 s at 55 bpm with the stream's
    /// parameters.
    pub fn new(p: &BeatParams, max_period: f64) -> Self {
        assert!(p.evidence_beats <= 16, "at most 16 evidence beats");
        let reach = (p.evidence_beats as f64 + 1.0) * max_period + p.evidence_half_s;
        Evidence {
            keep: (reach * p.fps).ceil() as usize + 2,
            beats: p.evidence_beats,
            half_s: p.evidence_half_s,
            fps: p.fps,
            acts: VecDeque::new(),
            first: 0,
        }
    }

    /// Records the next frame's activation.
    pub fn push(&mut self, activation: f32) {
        if self.acts.len() == self.keep {
            self.acts.pop_front();
            self.first += 1;
        }
        self.acts.push_back(activation);
    }

    /// The evidence at time `t` (the last frame pushed is `t * fps`): the mean peak activation
    /// within ±halfS of the last `beats` believed beats (from `last`, every `period` back) heard
    /// in full by `t`; a beat less than halfS ago gives way to the one before the oldest.
    pub fn at(&self, last: f64, period: f64, t: f64) -> f64 {
        let end = self.first + self.acts.len();
        let mut vals = [0.0f64; 16];
        let mut n = 0;
        for j in 0..=self.beats {
            let b = last - j as f64 * period;
            if b < 0.0 || n == self.beats {
                break;
            }
            if b + self.half_s > t {
                continue;
            }
            // (Python's round: half to even.)
            let lo = ((b - self.half_s) * self.fps).round_ties_even().max(0.0) as usize;
            let hi = (((b + self.half_s) * self.fps).round_ties_even() as usize)
                .min(end.saturating_sub(1));
            if hi >= lo {
                assert!(
                    lo >= self.first,
                    "evidence reads frame {lo}, older than the {} kept",
                    self.keep
                );
                let peak = (lo..=hi)
                    .map(|f| self.acts[f - self.first])
                    .fold(f32::NEG_INFINITY, f32::max);
                vals[n] = f64::from(peak);
                n += 1;
            }
        }
        if n == 0 {
            0.0
        } else {
            pairwise_sum(&vals[..n]) / n as f64
        }
    }
}

/// A committed beat.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Commit {
    /// When the beat falls (s).
    pub t: f64,
    /// When it was committed (s).
    pub known_at: f64,
    /// The period believed then (s).
    pub period: f64,
    /// The evidence known then.
    pub evidence: f64,
}

/// The commit rule, frame by frame.
pub struct Committer {
    p: BeatParams,
    /// The last committed beat's time.
    last: f64,
}

impl Committer {
    pub fn new(p: &BeatParams) -> Self {
        Committer {
            p: p.clone(),
            last: f64::NEG_INFINITY,
        }
    }

    /// Commits every beat due at this frame (`belief.last` is unbiased; `shift` moves the grid,
    /// 0 without the phase check); `evidence` is the evidence known at this frame.
    /// The believed last beat with `biasS` (the grid the phase check measures, before its shift).
    pub fn biased_last(&self, belief: &Belief) -> f64 {
        belief.last + self.p.bias_s
    }

    pub fn step(&mut self, belief: &Belief, shift: f64, evidence: f64, out: &mut Vec<Commit>) {
        let p = &self.p;
        let t = belief.frame as f64 / p.fps;
        let last = self.biased_last(belief) + shift;
        let per = belief.period;
        let horizon = p.commit_periods * per + p.commit_extra_s;
        loop {
            let k =
                (((self.last + 0.5 * per).max(t + p.commit_min_lead_s) - last) / per - 1e-9).ceil();
            let cand = last + k * per;
            // The last frame at or before the last committed beat commits the next one regardless.
            let due = self.last - 1.0 / p.fps < t && t <= self.last + 1e-9;
            if cand - t > horizon + 1e-9 && !due {
                break;
            }
            out.push(Commit {
                t: cand,
                known_at: t,
                period: per,
                evidence,
            });
            self.last = cand;
        }
    }
}
