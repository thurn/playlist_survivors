//! Onsets (`stream.py`'s `detect_onsets`), streaming: madmom's superflux detection function on
//! the online magnitude spectrogram, its online peak picking, and each onset's strength and
//! strong flag.
//!
//! Each step follows the Python libraries to the bit:
//! - superflux (`SpectralOnsetProcessor(onset_method="superflux", origin="online")`): the frame's
//!   magnitude spectrum minus the spectrum `diff_frames` back under a 3-bin maximum filter
//!   (scipy's `maximum_filter`, `reflect` at the edges), positive parts, summed in float32 as
//!   numpy sums (zero for the first `diff_frames` frames);
//! - peak picking (`OnsetPeakPickingProcessor(online=True)` over the whole function, as Python
//!   calls it: three zeros in front, then madmom's `peak_picking`): a frame is an onset when its
//!   value is at least its moving average over the past 11 frames plus the threshold (scipy's
//!   `uniform_filter`, a running sum in float64, cast to float32) and equals the maximum of the
//!   past 4 frames' detections; onsets within `combine` (plus madmom's 1e-12) of the last kept
//!   one are dropped;
//! - an onset's strength is the function's value at its frame; it is strong when at least the
//!   98th percentile (numpy's linear method) of the strengths known in the past 30 s.

use std::collections::VecDeque;

use crate::numeric::{pairwise_sum_f32, percentile_linear};

/// The onsets' parameters (`stream.py`'s `Params` and `detect_onsets`).
#[derive(Clone, Debug, PartialEq)]
pub struct OnsetParams {
    pub fps: f64,
    pub threshold: f64,
    pub bias_s: f64,
    pub strong_percentile: f64,
    pub strong_window_s: f64,
    pub pre_max_s: f64,
    pub pre_avg_s: f64,
    pub combine_s: f64,
    pub diff_max_bins: usize,
}

impl Default for OnsetParams {
    fn default() -> Self {
        OnsetParams {
            fps: 100.0,
            threshold: 1.5,
            bias_s: -0.010,
            strong_percentile: 98.0,
            strong_window_s: 30.0,
            pre_max_s: 0.03,
            pre_avg_s: 0.1,
            combine_s: 0.03,
            diff_max_bins: 3,
        }
    }
}

/// The superflux detection function, frame by frame.
pub struct Superflux {
    diff_frames: usize,
    half_bins: usize,
    /// The last `diff_frames` max-filtered spectra, oldest first.
    history: VecDeque<Vec<f32>>,
    diff: Vec<f32>,
}

impl Superflux {
    /// `diff_frames` is the online front end's (madmom computes the same from the window and hop).
    pub fn new(p: &OnsetParams, diff_frames: usize) -> Self {
        assert!(
            diff_frames >= 1 && p.diff_max_bins % 2 == 1,
            "a positive distance, an odd filter"
        );
        Superflux {
            diff_frames,
            half_bins: p.diff_max_bins / 2,
            history: VecDeque::with_capacity(diff_frames + 1),
            diff: Vec::new(),
        }
    }

    /// The detection function's value for the next frame's magnitude spectrum.
    pub fn step(&mut self, spectrum: &[f32]) -> f32 {
        let bins = spectrum.len();
        let value = if self.history.len() == self.diff_frames {
            let back = &self.history[0];
            self.diff.clear();
            self.diff
                .extend(spectrum.iter().zip(back).map(|(now, b)| (now - b).max(0.0)));
            pairwise_sum_f32(&self.diff)
        } else {
            0.0
        };
        // scipy's maximum filter over `diff_max_bins` bins, `reflect` mode (the edge bin repeats).
        let mut maxed = if self.history.len() == self.diff_frames {
            self.history.pop_front().expect("full")
        } else {
            vec![0.0; bins]
        };
        maxed.resize(bins, 0.0);
        for (b, m) in maxed.iter_mut().enumerate() {
            let lo = b.saturating_sub(self.half_bins);
            let hi = (b + self.half_bins).min(bins - 1);
            *m = spectrum[lo..=hi]
                .iter()
                .copied()
                .fold(f32::NEG_INFINITY, f32::max);
        }
        self.history.push_back(maxed);
        value
    }
}

/// scipy's `uniform_filter(mode="constant")` over the past `size` values, as a stream: a float64
/// running sum stepped by the newest value minus the oldest, divided by the size, cast to float32.
/// (A spike's rounding stays in the sum, as it does in scipy's.)
pub(crate) struct RunningMean {
    size: usize,
    sum: f64,
    /// The last `size` values (zeros before the first), oldest first.
    window: VecDeque<f32>,
}

impl RunningMean {
    pub(crate) fn new(size: usize) -> Self {
        RunningMean {
            size,
            sum: 0.0,
            window: std::iter::repeat_n(0.0, size).collect(),
        }
    }

    pub(crate) fn step(&mut self, value: f32) -> f32 {
        let oldest = self.window.pop_front().expect("window");
        self.window.push_back(value);
        self.sum += f64::from(value) - f64::from(oldest);
        (self.sum / self.size as f64) as f32
    }
}

/// `madmom.utils.combine_events(..., "left")` as a stream: an event is kept unless it lies within
/// `delta` of the last kept one, where madmom widens `delta` by 1e-12 against float error.
pub(crate) fn kept_after(last_kept: Option<f64>, t: f64, delta: f64) -> bool {
    let delta = delta + 1e-12;
    !last_kept.is_some_and(|last| t - last <= delta)
}

/// madmom's online peak picking, frame by frame.
pub struct PeakPicker {
    threshold: f32,
    combine: f64,
    fps: f64,
    mean: RunningMean,
    /// The last `max_len` detections (zeros before the function), oldest first.
    detections: VecDeque<f32>,
    frame: usize,
    last_onset: Option<f64>,
}

impl PeakPicker {
    pub fn new(p: &OnsetParams) -> Self {
        let frames = |s: f64| (s * p.fps).round_ties_even() as usize;
        let (avg_len, max_len) = (frames(p.pre_avg_s) + 1, frames(p.pre_max_s) + 1);
        PeakPicker {
            threshold: p.threshold as f32,
            combine: p.combine_s,
            fps: p.fps,
            mean: RunningMean::new(avg_len),
            detections: std::iter::repeat_n(0.0, max_len).collect(),
            frame: 0,
            last_onset: None,
        }
    }

    /// Feeds the next frame's value; returns the onset time when this frame is a kept onset.
    pub fn step(&mut self, value: f32) -> Option<f64> {
        let frame = self.frame;
        self.frame += 1;
        let mov_avg = self.mean.step(value);
        let detection = if value >= mov_avg + self.threshold {
            value
        } else {
            0.0
        };
        self.detections.pop_front();
        self.detections.push_back(detection);
        let mov_max = self
            .detections
            .iter()
            .copied()
            .fold(f32::NEG_INFINITY, f32::max);
        if detection == 0.0 || detection != mov_max {
            return None;
        }
        let t = frame as f64 / self.fps;
        if !kept_after(self.last_onset, t, self.combine) {
            return None;
        }
        self.last_onset = Some(t);
        Some(t)
    }
}

/// An onset.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Onset {
    /// When it sounds (s): `known_at` plus `biasS`.
    pub t: f64,
    /// When it was picked (s).
    pub known_at: f64,
    pub strength: f64,
    pub strong: bool,
    /// The band with the largest rise at its frame (`bands.py`; set by the analyzer, empty from
    /// `Strengths` alone).
    pub band: &'static str,
}

/// Strengths and strong flags over the past `strongWindowS`.
pub struct Strengths {
    p: OnsetParams,
    /// (knownAt, strength) of the onsets in the window, oldest first.
    known: VecDeque<(f64, f64)>,
    sorted: Vec<f64>,
}

impl Strengths {
    pub fn new(p: &OnsetParams) -> Self {
        Strengths {
            p: p.clone(),
            known: VecDeque::new(),
            sorted: Vec::new(),
        }
    }

    /// The onset picked at `time` (the picker's), with the function's value at its frame.
    pub fn onset(&mut self, time: f64, value: f32) -> Onset {
        // (Python: n = round(time * fps), knownAt = n / fps.)
        let n = (time * self.p.fps).round_ties_even();
        let known_at = n / self.p.fps;
        let strength = f64::from(value);
        self.known.push_back((known_at, strength));
        while self
            .known
            .front()
            .is_some_and(|&(k, _)| k < known_at - self.p.strong_window_s)
        {
            self.known.pop_front();
        }
        self.sorted.clear();
        self.sorted.extend(self.known.iter().map(|&(_, s)| s));
        self.sorted.sort_by(f64::total_cmp);
        let strong = strength >= percentile_linear(&self.sorted, self.p.strong_percentile);
        Onset {
            t: known_at + self.p.bias_s,
            known_at,
            strength,
            strong,
            band: "",
        }
    }
}
