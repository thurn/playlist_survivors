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
        // (combine_events, 'left': keep it only past `combine` from the last kept onset; madmom
        // widens `combine` by 1e-12 against float error.)
        let delta = self.combine + 1e-12;
        if self.last_onset.is_some_and(|last| t - last <= delta) {
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
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn superflux_is_madmoms() {
        // scipy's maximum_filter(size=(1, 3)) (reflect), the positive differences against the
        // frame before, np.sum over each frame in float32: Python's bits for the same spectra.
        let mut spec: Vec<Vec<f32>> = (0..4u64)
            .map(|f| {
                (0..1024u64)
                    .map(|b| {
                        ((((f * 1031 + b * 7919) % 997) as f64 / 997.0).powf(3.0) * 50.0) as f32
                    })
                    .collect()
            })
            .collect();
        spec[2][..3].copy_from_slice(&[60.0, 0.0, 5.0]);
        spec[2][1021..].copy_from_slice(&[1.0, 70.0, 2.0]);
        let mut sf = Superflux::new(&OnsetParams::default(), 1);
        let got: Vec<u32> = spec.iter().map(|s| sf.step(s).to_bits()).collect();
        assert_eq!(got, [0x0, 0x42e7_46d0, 0x4338_905c, 0x42fe_b9de]);
    }

    #[test]
    fn the_running_mean_is_scipys_even_after_a_spike() {
        // scipy.ndimage.uniform_filter(size 11, origin 5, constant) over madmom's buffer (three
        // zeros, then these values): the 1e20 spike's rounding stays in the running sum.
        let values: [f32; 20] = [
            0.5, 1e9, 3.25, 0.0, 7.0, 1e20, 2.0, 0.125, 9.0, 4.0, 1.0, 0.0, 6.5, 3.0, 2.5, 1e-3,
            8.0, 0.75, 5.0, 1.5,
        ];
        let scipy: [u32; 20] = [
            0x3d3a_2e8c,
            0x4cad_6534,
            0x4cad_6534,
            0x4cad_6534,
            0x4cad_6534,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x5efc_52cc,
            0x0,
            0xbde8_ba2f,
            0x3ea8_ba2f,
            0xbeb4_5d17,
        ];
        let mut mean = RunningMean::new(11);
        for _ in 0..3 {
            mean.step(0.0);
        }
        let got: Vec<u32> = values.iter().map(|&v| mean.step(v).to_bits()).collect();
        assert_eq!(got, scipy);
    }

    #[test]
    fn strong_flags_follow_numpys_percentile_over_the_window() {
        // detect_onsets' strong rule in Python over 152 onsets: the one at 104.5 s is exactly 30 s
        // after the one at 74.5 s, which stays in its window (and keeps it from being strong).
        let times: Vec<f64> = (0..150)
            .map(|i| f64::from(i) * 0.5)
            .chain([104.5, 104.75])
            .collect();
        let mut strengths: Vec<f64> = (0..152u32)
            .map(|i| f64::from((i * 7) % 23) + f64::from((i * 13) % 5) * 0.5)
            .collect();
        strengths[149] = 20.0;
        strengths[150] = 10.0;
        let python = "11110000000001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000000100000000000000000000001";
        let mut st = Strengths::new(&OnsetParams::default());
        let got: String = times
            .iter()
            .zip(&strengths)
            .map(|(&t, &s)| {
                if st.onset(t, s as f32).strong {
                    '1'
                } else {
                    '0'
                }
            })
            .collect();
        assert_eq!(got, python);
    }
}
