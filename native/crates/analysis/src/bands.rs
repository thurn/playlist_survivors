//! Bands, brightness and timbre (`bands.py`), streaming: each 100 fps frame's power spectrum of
//! the 2048 samples ending at it, and from it
//!
//! - each tick's bands `{kick, snare, hat, bass}`: a band's level against its running maximum
//!   (falling 0.2 dB/s, never under -60 dB, over 30 dB); bass is that level, the others the rise
//!   of it over 3 frames against a running peak of the rises (halving over 8 s);
//! - each onset's band: the largest rise at its frame against its band's running peak;
//! - each tick's brightness: the spectral centroid (in octaves) over the last 0.5 s, standardised
//!   against the past 30 s of those means, squashed;
//! - a timbre entry every second over the past 10 s: brightness on a fixed octave scale,
//!   flatness, the percussive share of a causal median split, and onset density.
//!
//! numpy computes the spectrum in float32 (pocketfft's own float32 FFT), so the spectra here (a
//! float64 FFT rounded to float32) agree within float32 rounding rather than to the bit; the
//! arithmetic after them follows numpy's: float32 band sums pairwise, the float64 centroid and
//! flatness, NEP 50's float32 comparisons with Python floats, scipy's medians.

use std::collections::VecDeque;
use std::f64::consts::PI;
use std::sync::Arc;

use realfft::num_complex::Complex;
use realfft::{RealFftPlanner, RealToComplex};

use crate::numeric::{pairwise_sum, pairwise_sum_f32};

pub const SR: f64 = 44100.0;
pub const FPS: f64 = 100.0;
pub const HOP: usize = 441;
/// A frame's window: the samples ending at the frame's time.
pub const N_FFT: usize = 2048;
const BINS: usize = N_FFT / 2 + 1;
/// The bands, in `bands.py`'s order (an onset's band breaks ties toward the first).
pub const BANDS: [&str; 4] = ["kick", "snare", "hat", "bass"];
const BAND_HZ: [&[(f64, f64)]; 4] = [
    &[(40.0, 120.0)],
    &[(150.0, 400.0), (2000.0, 5000.0)],
    &[(6000.0, 16000.0)],
    &[(60.0, 250.0)],
];
const BASS: usize = 3;
const DECAY_DB_PER_S: f64 = 0.2;
const RANGE_DB: f64 = 30.0;
const FLOOR_DB: f64 = -60.0;
const FULL_SCALE: f64 = (N_FFT as f64 / 4.0) * (N_FFT as f64 / 4.0);
const PEAK_HALF_S: f64 = 8.0;
const FLUX_FLOOR_DB: f64 = 6.0;
const RISE_FRAMES: usize = 3;
const FLAT_HZ: (f64, f64) = (100.0, 8000.0);
const FLAT_FLOOR_DB: f64 = 80.0;
const TIMBRE_BRIGHT_HZ: f64 = 1500.0;
const TIMBRE_BRIGHT_OCTAVES: f64 = 0.75;
const BRIGHT_WINDOW_S: f64 = 0.5;
const BRIGHT_STATS_S: f64 = 30.0;
const TIMBRE_EVERY_S: f64 = 1.0;
const TIMBRE_PAST_S: f64 = 10.0;
const HPSS_TIME: usize = 17;
const HPSS_FREQ: usize = 17;

/// A tick's bands (0-1).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct TickBands {
    pub kick: f64,
    pub snare: f64,
    pub hat: f64,
    pub bass: f64,
}

/// A timbre entry.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Timbre {
    pub known_at: f64,
    pub brightness: f64,
    pub flatness: f64,
    pub percussive: f64,
    pub density: f64,
}

/// One frame's derived values.
#[derive(Clone, Copy, Debug)]
struct FrameValues {
    /// Each band's rise against its running peak.
    rel: [f64; 4],
    /// Bass's level read against its running maximum, 0-1.
    bass: f64,
    bright: f64,
    flatness: f64,
    percussive: f32,
}

/// numpy's `np.hanning(n)` as float32.
fn hanning32(n: usize) -> Vec<f32> {
    let m = n as f64;
    (0..n)
        .map(|k| (0.5 + 0.5 * (PI * (1.0 - m + 2.0 * k as f64) / (m - 1.0)).cos()) as f32)
        .collect()
}

/// scipy's `median_filter` rank: the middle of an odd count, by selection (exact).
fn median(values: &mut [f32]) -> f32 {
    let mid = values.len() / 2;
    *values.select_nth_unstable_by(mid, f32::total_cmp).1
}

/// The bands group, frame by frame from samples.
pub struct Bands {
    window: Vec<f32>,
    fft: Arc<dyn RealToComplex<f64>>,
    fft_in: Vec<f64>,
    fft_out: Vec<Complex<f64>>,
    scratch: Vec<Complex<f64>>,
    freqs: Vec<f64>,
    masks: [Vec<usize>; 4],
    flat_bins: Vec<usize>,
    /// The samples heard, the last ones kept; `samples[0]` is absolute sample `first`.
    samples: VecDeque<f32>,
    first: usize,
    /// The next frame to compute.
    next: usize,
    refs: [f64; 4],
    held: [VecDeque<f64>; 4],
    peaks: [f64; 4],
    /// The last HPSS_TIME frames' power spectra, oldest first.
    history: VecDeque<Vec<f32>>,
    /// Per frame (index `frames_first` on), the values ticks, onsets and timbre read.
    frames: VecDeque<FrameValues>,
    frames_first: usize,
    /// Running sum of the centroids from the start (`np.cumsum`), and its value per kept frame.
    centroid_sum: f64,
    centroid_sums: VecDeque<f64>,
    /// The last tick's frame, and the past brightness means.
    prev_tick: Option<usize>,
    bright_past: VecDeque<f64>,
    bright_span: usize,
    /// Onset times known, for density.
    onsets: VecDeque<f64>,
    next_timbre: usize,
    power: Vec<f32>,
}

impl Default for Bands {
    fn default() -> Self {
        Bands::new()
    }
}

impl Bands {
    pub fn new() -> Bands {
        let fft = RealFftPlanner::<f64>::new().plan_fft_forward(N_FFT);
        let freqs: Vec<f64> = (0..BINS).map(|k| k as f64 * SR / N_FFT as f64).collect();
        let masks = BAND_HZ.map(|ranges| {
            (0..BINS)
                .filter(|&k| {
                    ranges
                        .iter()
                        .any(|&(lo, hi)| freqs[k] >= lo && freqs[k] < hi)
                })
                .collect::<Vec<_>>()
        });
        let flat_bins = (0..BINS)
            .filter(|&k| freqs[k] >= FLAT_HZ.0 && freqs[k] < FLAT_HZ.1)
            .collect();
        Bands {
            window: hanning32(N_FFT),
            fft_in: fft.make_input_vec(),
            fft_out: fft.make_output_vec(),
            scratch: fft.make_scratch_vec(),
            fft,
            freqs,
            masks,
            flat_bins,
            samples: VecDeque::new(),
            first: 0,
            next: 0,
            refs: [FLOOR_DB; 4],
            held: Default::default(),
            peaks: [FLUX_FLOOR_DB; 4],
            history: VecDeque::new(),
            frames: VecDeque::new(),
            frames_first: 0,
            centroid_sum: 0.0,
            centroid_sums: VecDeque::from([0.0]),
            prev_tick: None,
            bright_past: VecDeque::new(),
            bright_span: 1,
            onsets: VecDeque::new(),
            next_timbre: 1,
            power: vec![0.0; BINS],
        }
    }

    /// Hears samples.
    pub fn push_samples(&mut self, samples: &[f32]) {
        self.samples.extend(samples);
        let keep = N_FFT + 4 * HOP;
        // (Frames not yet computed still need their windows.)
        let needed_from = (self.next * HOP).saturating_sub(N_FFT);
        while self.samples.len() > keep && self.first < needed_from {
            self.samples.pop_front();
            self.first += 1;
        }
    }

    /// The number of samples heard.
    pub fn received(&self) -> usize {
        self.first + self.samples.len()
    }

    /// Computes every frame up to `n` (frame n covers the samples before `n * HOP`).
    pub fn advance_to(&mut self, n: usize) {
        while self.next <= n {
            self.frame();
        }
    }

    fn frame(&mut self) {
        let n = self.next;
        self.next += 1;
        let end = n * HOP;
        assert!(end <= self.received(), "frame {n} before its samples");
        for (j, v) in self.fft_in.iter_mut().enumerate() {
            let s = (end + j)
                .checked_sub(N_FFT)
                .map_or(0.0, |i| self.samples[i - self.first]);
            *v = f64::from(s * self.window[j]);
        }
        self.fft
            .process_with_scratch(&mut self.fft_in, &mut self.fft_out, &mut self.scratch)
            .expect("FFT buffers are sized by the plan");
        for (p, c) in self.power.iter_mut().zip(&self.fft_out) {
            let m = (c.re as f32).hypot(c.im as f32);
            *p = m * m;
        }
        let power = self.power.clone();
        // Band levels, their running maxima, the held rises and their running peaks.
        let fall = DECAY_DB_PER_S / FPS;
        let decay = 0.5f64.powf(1.0 / (PEAK_HALF_S * FPS));
        let mut rel = [0.0; 4];
        let mut bass = 0.0;
        let mut gathered = Vec::with_capacity(BINS);
        for (b, r) in rel.iter_mut().enumerate() {
            gathered.clear();
            gathered.extend(self.masks[b].iter().map(|&k| power[k]));
            let energy = f64::from(pairwise_sum_f32(&gathered));
            let db = 10.0 * (energy / FULL_SCALE + 1e-12).log10();
            self.refs[b] = db.max(self.refs[b] - fall).max(FLOOR_DB);
            let held = db.max(self.refs[b] - RANGE_DB);
            let hist = &mut self.held[b];
            if hist.is_empty() {
                hist.extend([held; RISE_FRAMES]);
            }
            let before = hist[hist.len() - RISE_FRAMES];
            hist.push_back(held);
            while hist.len() > RISE_FRAMES {
                hist.pop_front();
            }
            let flux = (held - before).max(0.0);
            self.peaks[b] = flux.max(self.peaks[b] * decay).max(FLUX_FLOOR_DB);
            *r = flux / self.peaks[b];
            if b == BASS {
                bass = ((db - self.refs[b] + RANGE_DB) / RANGE_DB).clamp(0.0, 1.0);
            }
        }
        // The centroid: magnitudes in float32, their sum pairwise in float32 (compared with
        // Python floats as float32, NEP 50), the weighted sum in float64.
        let mag: Vec<f32> = power.iter().map(|p| p.sqrt()).collect();
        let total = pairwise_sum_f32(&mag);
        let weighted: Vec<f64> = mag
            .iter()
            .zip(&self.freqs)
            .map(|(&m, &f)| f64::from(m) * f)
            .collect();
        let hz = if total > 1e-6f32 {
            pairwise_sum(&weighted) / f64::from(total.max(1e-6f32))
        } else {
            20.0
        };
        let centroid = (hz.max(20.0) / 20.0).log2();
        let bright = 1.0
            / (1.0
                + (-(centroid - (TIMBRE_BRIGHT_HZ / 20.0).log2()) / TIMBRE_BRIGHT_OCTAVES).exp());
        // Flatness over FLAT_HZ in float64, each bin floored FLAT_FLOOR_DB under the peak.
        let flat_mag: Vec<f64> = self
            .flat_bins
            .iter()
            .map(|&k| f64::from(power[k]).sqrt())
            .collect();
        let peak = flat_mag.iter().copied().fold(f64::NEG_INFINITY, f64::max);
        let floor = peak * 10f64.powf(-FLAT_FLOOR_DB / 20.0) + 1e-12;
        let floored: Vec<f64> = flat_mag.iter().map(|&m| m.max(floor)).collect();
        let logs: Vec<f64> = floored.iter().map(|m| m.ln()).collect();
        let count = floored.len() as f64;
        let geo = (pairwise_sum(&logs) / count).exp();
        let flatness = if peak > 1e-6 {
            geo / (pairwise_sum(&floored) / count)
        } else {
            0.0
        };
        // The causal median split: harmonic from the past HPSS_TIME frames per bin (the first
        // frame repeated before the start), percussive from HPSS_FREQ bins (reflected edges).
        if self.history.is_empty() {
            for _ in 0..HPSS_TIME - 1 {
                self.history.push_back(power.clone());
            }
        }
        self.history.push_back(power.clone());
        while self.history.len() > HPSS_TIME {
            self.history.pop_front();
        }
        let mut column = vec![0.0f32; HPSS_TIME];
        let mut harm = vec![0.0f32; BINS];
        for (k, h) in harm.iter_mut().enumerate() {
            for (c, frame) in column.iter_mut().zip(&self.history) {
                *c = frame[k];
            }
            *h = median(&mut column);
        }
        let half = (HPSS_FREQ / 2) as isize;
        let mut row = vec![0.0f32; HPSS_FREQ];
        let mut perc = vec![0.0f32; BINS];
        for (k, p) in perc.iter_mut().enumerate() {
            for (j, r) in row.iter_mut().enumerate() {
                let mut i = k as isize + j as isize - half;
                // scipy's 'reflect': d c b a | a b c d | d c b a.
                if i < 0 {
                    i = -i - 1;
                }
                if i >= BINS as isize {
                    i = 2 * BINS as isize - i - 1;
                }
                *r = power[i as usize];
            }
            *p = median(&mut row);
        }
        let (hp, pp) = (pairwise_sum_f32(&harm), pairwise_sum_f32(&perc));
        let percussive = if hp + pp > 1e-10f32 {
            pp / (hp + pp).max(1e-10f32)
        } else {
            0.0
        };
        self.centroid_sum += centroid;
        self.centroid_sums.push_back(self.centroid_sum);
        self.frames.push_back(FrameValues {
            rel,
            bass,
            bright,
            flatness,
            percussive,
        });
        let keep = (TIMBRE_PAST_S * FPS) as usize + 8;
        while self.frames.len() > keep {
            self.frames.pop_front();
            self.centroid_sums.pop_front();
            self.frames_first += 1;
        }
    }

    fn at(&self, n: usize) -> &FrameValues {
        &self.frames[n - self.frames_first]
    }

    /// The last frame computed.
    fn last(&self) -> usize {
        self.next - 1
    }

    /// The tick at frame `f` (frames through `min(f, last)` computed): its bands and brightness.
    /// `ticks_every` is the tick spacing in frames (for brightness's 30 s of past means).
    pub fn tick(&mut self, f: usize, ticks_every: usize) -> (TickBands, f64) {
        self.bright_span =
            ((BRIGHT_STATS_S * FPS / ticks_every.max(1) as f64).round_ties_even() as usize).max(1);
        let hi = f.min(self.last());
        let lo = self.prev_tick.map_or(0, |p| p + 1).min(hi);
        let mut transient = [0.0f64; 3];
        for (b, t) in transient.iter_mut().enumerate() {
            *t = (lo..=hi)
                .map(|n| self.at(n).rel[b])
                .fold(f64::NEG_INFINITY, f64::max)
                .clamp(0.0, 1.0);
        }
        let bands = TickBands {
            kick: transient[0],
            snare: transient[1],
            hat: transient[2],
            bass: self.at(hi).bass,
        };
        self.prev_tick = Some(f);
        // Brightness: the centroid's mean over the last 0.5 s, against the past means.
        let w = (BRIGHT_WINDOW_S * FPS).round_ties_even() as usize;
        let end = hi + 1;
        let start = end.saturating_sub(w);
        let sum_at = |m: usize| self.centroid_sums[m - self.frames_first];
        let m = (sum_at(end) - sum_at(start)) / (end - start).max(1) as f64;
        self.bright_past.push_back(m);
        while self.bright_past.len() > self.bright_span {
            self.bright_past.pop_front();
        }
        let len = self.bright_past.len() as f64;
        let mean = self.bright_past.iter().fold(0.0, |s, x| s + x) / len;
        let var = self
            .bright_past
            .iter()
            .fold(0.0, |s, x| s + (x - mean).powi(2))
            / len;
        let sd = var.powf(0.5);
        let z = if sd > 1e-9 { (m - mean) / sd } else { 0.0 };
        (bands, 1.0 / (1.0 + (-z).exp()))
    }

    /// An onset picked at frame `n` (known at `known_at`): its band.
    pub fn onset(&mut self, n: usize, known_at: f64) -> &'static str {
        self.onsets.push_back(known_at);
        let v = self.at(n.min(self.last()));
        let mut best = 0;
        for b in 1..4 {
            if v.rel[b] > v.rel[best] {
                best = b;
            }
        }
        BANDS[best]
    }

    /// The timbre entries due by `duration` seconds of audio (frames through it computed).
    pub fn timbre(&mut self, duration: f64, out: &mut Vec<Timbre>) {
        while self.next_timbre as f64 * TIMBRE_EVERY_S <= duration + 1e-9 {
            let t = self.next_timbre as f64 * TIMBRE_EVERY_S;
            self.next_timbre += 1;
            let hi = ((t * FPS).round_ties_even() as usize).min(self.last()) + 1;
            let lo = hi.saturating_sub((TIMBRE_PAST_S * FPS).round_ties_even() as usize);
            let span = t.min(TIMBRE_PAST_S);
            let frames: Vec<&FrameValues> = (lo..hi).map(|n| self.at(n)).collect();
            let count = frames.len() as f64;
            let bright: Vec<f64> = frames.iter().map(|v| v.bright).collect();
            let flat: Vec<f64> = frames.iter().map(|v| v.flatness).collect();
            let perc: Vec<f32> = frames.iter().map(|v| v.percussive).collect();
            while self.onsets.front().is_some_and(|&o| o <= t - TIMBRE_PAST_S) {
                self.onsets.pop_front();
            }
            let density = self
                .onsets
                .iter()
                .filter(|&&o| t - TIMBRE_PAST_S < o && o <= t + 1e-9)
                .count();
            out.push(Timbre {
                known_at: t,
                brightness: pairwise_sum(&bright) / count,
                flatness: pairwise_sum(&flat) / count,
                percussive: f64::from(pairwise_sum_f32(&perc) / perc.len() as f32),
                density: density as f64 / span,
            });
        }
    }
}
