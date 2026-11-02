//! The stream's structure (`structure.py`): what a causal analysis knows of a track's form, per
//! 50 ms tick, streaming with bounded memory.
//!
//! - **Features:** each tick's frame holds the 4096 samples before it; its loudness in dB (the
//!   mean square summed row by row, as numpy's `mean(axis=0)` does), and from its power spectrum
//!   (numpy's periodic Hann window, `|rfft|**2` as `hypot` squared) librosa's chroma (the exported
//!   filterbank, each column scaled by its maximum) and MFCCs 1-12 (the exported mel filterbank,
//!   `power_to_db` without a top-dB clip, the exported orthonormal DCT), with the loudness / 10:
//!   25 features.
//! - **Changes:** novelty between the past 8 s and the next 2 s of features, each scaled by its
//!   spread over the past 30 s; a peak within ±4 s above the past 60 s's median plus half the gap
//!   to their 90th percentile, known at the tick ending the next window after it.
//! - **Jumps:** the last 1 s's mean loudness against the 6 s before it, 6 dB or more, at most one
//!   every 4 s.
//! - **Keys:** the past 20 s's mean chroma against the Krumhansl-Kessler profiles (numpy's
//!   `corrcoef`), from 10 s on.
//! - **Ends:** the loudness 20 dB below the loudest second of the past 30 s for 1.5 s, after 10 s.
//!
//! Every window statistic is a difference of running sums, as numpy's `cumsum` makes them (the
//! sums run from the stream's start, so their rounding is Python's).

use std::collections::VecDeque;
use std::f64::consts::PI;
use std::sync::Arc;

use realfft::num_complex::Complex;
use realfft::{RealFftPlanner, RealToComplex};

use crate::numeric::{pairwise_sum, percentile_linear};
use crate::tensors::{TensorError, Tensors};

/// Samples per feature frame (each ends at its tick).
pub const N_FFT: usize = 4096;
const BINS: usize = N_FFT / 2 + 1;
/// The novelty features: chroma (12), MFCCs 1-12 (12), loudness / 10.
pub const FEATURES: usize = 25;

pub(crate) const MAJOR: [f64; 12] = [
    6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88,
];
pub(crate) const MINOR: [f64; 12] = [
    6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17,
];
/// The keys, major from C then minor from C (`manifest.json`'s `codes.key`).
pub const KEYS: [&str; 24] = [
    "C major", "Db major", "D major", "Eb major", "E major", "F major", "F# major", "G major",
    "Ab major", "A major", "Bb major", "B major", "C minor", "C# minor", "D minor", "Eb minor",
    "E minor", "F minor", "F# minor", "G minor", "G# minor", "A minor", "Bb minor", "B minor",
];

/// The structure's parameters (`stream.py`'s `Params`; `manifest.json`'s `streamParams.structure`).
#[derive(Clone, Debug, PartialEq)]
pub struct StructureParams {
    pub change_past_s: f64,
    pub change_next_s: f64,
    pub change_stats_s: f64,
    pub change_peak_s: f64,
    pub change_threshold_s: f64,
    pub change_k: f64,
    pub change_min_prior: usize,
    pub jump_db: f64,
    pub jump_recent_s: f64,
    pub jump_before_s: f64,
    pub jump_gap_s: f64,
    pub key_window_s: f64,
    pub key_after_s: f64,
    pub end_drop_db: f64,
    pub end_hold_s: f64,
    pub end_after_s: f64,
    pub end_window_s: f64,
    pub end_smooth_s: f64,
}

impl Default for StructureParams {
    fn default() -> Self {
        StructureParams {
            change_past_s: 8.0,
            change_next_s: 2.0,
            change_stats_s: 30.0,
            change_peak_s: 4.0,
            change_threshold_s: 60.0,
            change_k: 0.5,
            change_min_prior: 20,
            jump_db: 6.0,
            jump_recent_s: 1.0,
            jump_before_s: 6.0,
            jump_gap_s: 4.0,
            key_window_s: 20.0,
            key_after_s: 10.0,
            end_drop_db: 20.0,
            end_hold_s: 1.5,
            end_after_s: 10.0,
            end_window_s: 30.0,
            end_smooth_s: 1.0,
        }
    }
}

/// A structural change.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Change {
    /// The boundary: the middle of its tick's frame.
    pub t: f64,
    pub known_at: f64,
    pub strength: f64,
}

/// A loudness jump.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Jump {
    pub t: f64,
    pub known_at: f64,
    pub db: f64,
}

/// A candidate ending.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct End {
    /// Where the quiet began.
    pub t: f64,
    pub known_at: f64,
}

/// One tick's features.
#[derive(Clone, Debug, PartialEq)]
pub struct TickFeatures {
    pub chroma: [f64; 12],
    pub features: [f64; FEATURES],
    pub loudness_db: f64,
}

/// numpy's `mean(frame * frame, axis=0)` in dB for one frame (`structure.features`'s loudness): the
/// squares summed in order (an axis-0 reduction adds row by row), over n, `20 log10(sqrt + 1e-6)`.
pub(crate) fn frame_loudness_db(frame: &[f64]) -> f64 {
    let sum = frame.iter().fold(0.0, |s, &x| s + x * x);
    20.0 * ((sum / frame.len() as f64).sqrt() + 1e-6).log10()
}

/// librosa's chroma, mel and DCT matrices and the FFT behind the features.
pub struct StructureFeatures {
    window: Vec<f64>,
    chroma_fb: Vec<f64>,
    mel_fb: Vec<f64>,
    mels: usize,
    dct: Vec<f64>,
    mfccs: usize,
    fft: Arc<dyn RealToComplex<f64>>,
    fft_in: Vec<f64>,
    fft_out: Vec<Complex<f64>>,
    scratch: Vec<Complex<f64>>,
    power: Vec<f64>,
    mel: Vec<f64>,
}

fn matrix(models: &Tensors, name: &str, cols: usize) -> Result<(usize, Vec<f64>), TensorError> {
    let rows = match models.shape(name)? {
        &[rows, c] if c == cols && rows > 0 => rows,
        other => {
            return Err(TensorError::Format(format!(
                "{name}: shape {other:?}, not [rows, {cols}]"
            )));
        }
    };
    let values = match models.f32(name) {
        Ok(v) => v.into_iter().map(f64::from).collect(),
        Err(TensorError::Dtype { .. }) => models.f64(name)?,
        Err(e) => return Err(e),
    };
    Ok((rows, values))
}

impl StructureFeatures {
    pub fn new(models: &Tensors) -> Result<Self, TensorError> {
        let (chroma_rows, chroma_fb) = matrix(models, "librosa.chroma", BINS)?;
        if chroma_rows != 12 {
            return Err(TensorError::Format(format!(
                "librosa.chroma: {chroma_rows} rows, not 12"
            )));
        }
        let (mels, mel_fb) = matrix(models, "librosa.mel", BINS)?;
        let (mfccs, dct) = matrix(models, "librosa.dct", mels)?;
        if mfccs != 13 {
            return Err(TensorError::Format(format!(
                "librosa.dct: {mfccs} rows, not 13"
            )));
        }
        let fft = RealFftPlanner::<f64>::new().plan_fft_forward(N_FFT);
        // numpy's np.hanning(N_FFT + 1)[:-1]: the periodic Hann window.
        let m = (N_FFT + 1) as f64;
        let window = (0..N_FFT)
            .map(|k| 0.5 + 0.5 * (PI * (1.0 - m + 2.0 * k as f64) / (m - 1.0)).cos())
            .collect();
        Ok(StructureFeatures {
            window,
            chroma_fb,
            mel_fb,
            mels,
            dct,
            mfccs,
            fft_in: fft.make_input_vec(),
            fft_out: fft.make_output_vec(),
            scratch: fft.make_scratch_vec(),
            fft,
            power: vec![0.0; BINS],
            mel: vec![0.0; mels],
        })
    }

    /// The features of one frame (the 4096 samples before its tick, as float64).
    pub fn tick(&mut self, frame: &[f64]) -> TickFeatures {
        assert_eq!(frame.len(), N_FFT, "a frame's samples");
        let loudness_db = frame_loudness_db(frame);
        for ((v, &x), &w) in self.fft_in.iter_mut().zip(frame).zip(&self.window) {
            *v = x * w;
        }
        self.fft
            .process_with_scratch(&mut self.fft_in, &mut self.fft_out, &mut self.scratch)
            .expect("FFT buffers are sized by the plan");
        for (p, c) in self.power.iter_mut().zip(&self.fft_out) {
            let m = c.re.hypot(c.im);
            *p = m * m;
        }
        let dot = |row: &[f64], x: &[f64]| row.iter().zip(x).fold(0.0, |s, (a, b)| s + a * b);
        let mut chroma = [0.0f64; 12];
        for (c, row) in chroma.iter_mut().zip(self.chroma_fb.as_chunks::<BINS>().0) {
            *c = dot(row, &self.power);
        }
        // librosa's normalize(norm=inf): each column over its largest magnitude (unless tiny).
        let peak = chroma.iter().fold(0.0f64, |m, c| m.max(c.abs()));
        let length = if peak < f64::MIN_POSITIVE { 1.0 } else { peak };
        for c in &mut chroma {
            *c /= length;
        }
        for (m, row) in self.mel.iter_mut().zip(self.mel_fb.as_chunks::<BINS>().0) {
            // librosa's power_to_db (ref 1, amin 1e-10, no top-dB clip).
            *m = 10.0 * dot(row, &self.power).max(1e-10).log10() - 10.0 * 1.0f64.max(1e-10).log10();
        }
        let mut features = [0.0f64; FEATURES];
        features[..12].copy_from_slice(&chroma);
        for (k, f) in features[12..24].iter_mut().enumerate() {
            *f = dot(
                &self.dct[(k + 1) * self.mels..(k + 2) * self.mels],
                &self.mel,
            );
        }
        features[24] = loudness_db / 10.0;
        debug_assert_eq!(self.mfccs, 13);
        TickFeatures {
            chroma,
            features,
            loudness_db,
        }
    }
}

/// Running sums of fixed-width rows from the stream's start (`np.cumsum` with a leading zero),
/// keeping the last `keep`.
struct Cumulative<const D: usize> {
    rows: VecDeque<[f64; D]>,
    /// The index of `rows[0]` (cumulative row `m` sums the first `m` values).
    first: usize,
    keep: usize,
}

impl<const D: usize> Cumulative<D> {
    fn new(keep: usize) -> Self {
        Cumulative {
            rows: VecDeque::from([[0.0; D]]),
            first: 0,
            keep: keep + 1,
        }
    }

    fn push(&mut self, v: &[f64; D]) {
        let mut next = *self.rows.back().expect("a row");
        for (n, x) in next.iter_mut().zip(v) {
            *n += x;
        }
        self.rows.push_back(next);
        if self.rows.len() > self.keep {
            self.rows.pop_front();
            self.first += 1;
        }
    }

    fn at(&self, m: usize) -> &[f64; D] {
        assert!(m >= self.first, "cumulative row {m} no longer kept");
        &self.rows[m - self.first]
    }

    /// `_window_mean`: the mean of the values in `[max(0, hi - length), hi)`.
    fn window_mean(&self, hi: usize, length: usize, d: usize) -> f64 {
        let lo = hi.saturating_sub(length);
        (self.at(hi)[d] - self.at(lo)[d]) / (hi - lo) as f64
    }
}

/// `np.median` of sorted values: the middle one, or the mean of the middle two.
pub(crate) fn median_sorted(sorted: &[f64]) -> f64 {
    let n = sorted.len();
    if n % 2 == 1 {
        sorted[n / 2]
    } else {
        f64::midpoint(sorted[n / 2 - 1], sorted[n / 2])
    }
}

/// numpy's `cov` dot product of two rows (a 2 x n `dot` through BLAS: Accelerate's dgemm on
/// macOS arm64): two fused multiply-add accumulators over alternate elements, then their sum.
pub(crate) fn cov_dot(a: &[f64], b: &[f64]) -> f64 {
    let mut s = [0.0f64; 2];
    for (i, (x, y)) in a.iter().zip(b).enumerate() {
        s[i % 2] = x.mul_add(*y, s[i % 2]);
    }
    s[0] + s[1]
}

/// numpy's `np.corrcoef(v, w)[0, 1]`: `cov` (each row less its mean, `cov_dot`, times
/// `1 / (n - 1)`), over the two standard deviations in turn, clipped to [-1, 1].
pub(crate) fn corrcoef(v: &[f64; 12], w: &[f64; 12]) -> f64 {
    let mean = |a: &[f64; 12]| pairwise_sum(a) / 12.0;
    let (mv, mw) = (mean(v), mean(w));
    let xs: [f64; 12] = v.map(|x| x - mv);
    let ys: [f64; 12] = w.map(|y| y - mw);
    let f = 1.0 / 11.0;
    let (c00, c01, c11) = (
        cov_dot(&xs, &xs) * f,
        cov_dot(&xs, &ys) * f,
        cov_dot(&ys, &ys) * f,
    );
    (c01 / c00.sqrt() / c11.sqrt()).clamp(-1.0, 1.0)
}

/// `_key_name`: the best-correlated profile and rotation (later ones only past 1e-12 better).
pub(crate) fn key_name(v: &[f64; 12]) -> &'static str {
    let mut best: (&'static str, f64) = ("", f64::NEG_INFINITY);
    for (profile, names) in [(&MAJOR, &KEYS[..12]), (&MINOR, &KEYS[12..])] {
        for r in 0..12 {
            // np.roll(profile, r)
            let rolled: [f64; 12] = std::array::from_fn(|j| profile[(j + 12 - r) % 12]);
            let score = corrcoef(v, &rolled);
            if score > best.1 + 1e-12 {
                best = (names[r], score);
            }
        }
    }
    best.0
}

/// One tick's structure output.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct StructureTick {
    pub key: Option<&'static str>,
    pub change: Option<Change>,
    pub jump: Option<Jump>,
    pub end: Option<End>,
}

/// The structure stage, tick by tick.
pub struct Structure {
    p: StructureParams,
    features: StructureFeatures,
    center_s: f64,
    past: usize,
    next: usize,
    stats: usize,
    peak: usize,
    history: usize,
    recent: usize,
    before: usize,
    key_window: usize,
    smooth: usize,
    end_window: usize,
    hold: usize,
    ticks: usize,
    times: VecDeque<f64>,
    feats: Cumulative<FEATURES>,
    squares: Cumulative<FEATURES>,
    chroma: Cumulative<12>,
    loudness: Cumulative<1>,
    /// This tick's loudness.
    last_db: f64,
    /// Novelty by tick (NaN where its windows are incomplete), from tick `nov_first` on.
    nov: VecDeque<f64>,
    nov_first: usize,
    seconds: VecDeque<f64>,
    run: usize,
    last_jump: f64,
    prior: Vec<f64>,
    /// The last samples heard, ending at absolute sample `received - 1` (`tick_at`'s frames).
    samples: VecDeque<f32>,
    received: usize,
    frame: Vec<f64>,
}

impl Structure {
    /// The structure stage for `fps` ticks a second of audio sampled at `sample_rate`.
    pub fn new(
        models: &Tensors,
        p: &StructureParams,
        sample_rate: f64,
        fps: f64,
    ) -> Result<Self, TensorError> {
        let n = |s: f64| (s * fps).round_ties_even() as usize;
        let (past, next, stats, peak, history) = (
            n(p.change_past_s),
            n(p.change_next_s),
            n(p.change_stats_s),
            n(p.change_peak_s),
            n(p.change_threshold_s),
        );
        let (recent, before, key_window, smooth, end_window, hold) = (
            n(p.jump_recent_s),
            n(p.jump_before_s),
            n(p.key_window_s),
            n(p.end_smooth_s),
            n(p.end_window_s),
            n(p.end_hold_s),
        );
        Ok(Structure {
            features: StructureFeatures::new(models)?,
            center_s: N_FFT as f64 / 2.0 / sample_rate,
            past,
            next,
            stats,
            peak,
            history,
            recent,
            before,
            key_window,
            smooth,
            end_window,
            hold,
            ticks: 0,
            times: VecDeque::new(),
            feats: Cumulative::new(stats.max(past + next) + 1),
            squares: Cumulative::new(stats.max(past + next) + 1),
            chroma: Cumulative::new(key_window + 1),
            loudness: Cumulative::new((recent + before).max(smooth) + 1),
            last_db: 0.0,
            nov: VecDeque::new(),
            nov_first: 0,
            seconds: VecDeque::new(),
            run: 0,
            last_jump: f64::NEG_INFINITY,
            prior: Vec::new(),
            samples: VecDeque::new(),
            received: 0,
            frame: vec![0.0; N_FFT],
            p: p.clone(),
        })
    }

    /// Features per tick (for tests of the features).
    pub fn features(&mut self) -> &mut StructureFeatures {
        &mut self.features
    }

    /// Hears samples for `tick_at`, keeping a frame and the most a tick may lag them.
    pub fn push_samples(&mut self, samples: &[f32]) {
        self.samples.extend(samples);
        self.received += samples.len();
        while self.samples.len() > 2 * N_FFT {
            self.samples.pop_front();
        }
    }

    /// The next tick, at time `t`, its frame the `N_FFT` samples heard before absolute sample
    /// `end` (zeros before the stream's start and past the samples heard, as Python's frames).
    pub fn tick_at(&mut self, t: f64, end: usize) -> StructureTick {
        let first = self.received - self.samples.len();
        assert!(
            end.saturating_sub(N_FFT) >= first || end < N_FFT,
            "the frame ending at {end} is no longer kept"
        );
        let mut frame = std::mem::take(&mut self.frame);
        for (j, v) in frame.iter_mut().enumerate() {
            *v = (end + j)
                .checked_sub(N_FFT)
                .filter(|&n| n < self.received)
                .map_or(0.0, |n| f64::from(self.samples[n - first]));
        }
        let out = self.tick(t, &frame);
        self.frame = frame;
        out
    }

    /// The next tick, at time `t`, from its frame (the `N_FFT` samples before it, as float64).
    pub fn tick(&mut self, t: f64, frame: &[f64]) -> StructureTick {
        let f = self.features.tick(frame);
        self.tick_features(t, &f)
    }

    /// The next tick, at time `t`, from its features (Python's, in the stage tests).
    pub fn tick_features(&mut self, t: f64, f: &TickFeatures) -> StructureTick {
        let i = self.ticks;
        self.ticks += 1;
        self.times.push_back(t);
        self.feats.push(&f.features);
        self.squares.push(&f.features.map(|x| x * x));
        self.chroma.push(&f.chroma);
        self.loudness.push(&[f.loudness_db]);
        self.last_db = f.loudness_db;
        // (Times back to the earliest change's tick are kept.)
        while self.times.len() > self.peak + self.next + 2 {
            self.times.pop_front();
        }
        let mut out = StructureTick {
            key: self.key(i, t),
            ..StructureTick::default()
        };
        // Novelty at the tick whose next window ended here, then a change it decides.
        if let Some(j) = (i + 1).checked_sub(self.next) {
            let value = self.novelty(j);
            self.nov.push_back(value);
            while self.nov.len() > self.history + 2 * self.peak + 2 {
                self.nov.pop_front();
                self.nov_first += 1;
            }
            if let Some(c) = j.checked_sub(self.peak) {
                out.change = self.change(c, t);
            }
        }
        out.jump = self.jump(i, t);
        out.end = self.end(i, t);
        out
    }

    fn key(&self, i: usize, t: f64) -> Option<&'static str> {
        if t < self.p.key_after_s - 1e-9 {
            return None;
        }
        let lo = (i + 1).saturating_sub(self.key_window);
        let (hi_row, lo_row) = (self.chroma.at(i + 1), self.chroma.at(lo));
        let n = (i + 1 - lo) as f64;
        let v: [f64; 12] = std::array::from_fn(|k| (hi_row[k] - lo_row[k]) / n);
        let (lo_v, hi_v) = v
            .iter()
            .fold((f64::INFINITY, f64::NEG_INFINITY), |(a, b), &x| {
                (a.min(x), b.max(x))
            });
        (hi_v - lo_v > 1e-9).then(|| key_name(&v))
    }

    /// Novelty at tick `j` (its next window just ended), NaN before the past window is complete.
    fn novelty(&self, j: usize) -> f64 {
        if j < self.past {
            return f64::NAN;
        }
        let known = j + self.next;
        let lo = known.saturating_sub(self.stats);
        let span = (known - lo) as f64;
        let (c1k, c1l, c2k, c2l) = (
            self.feats.at(known),
            self.feats.at(lo),
            self.squares.at(known),
            self.squares.at(lo),
        );
        let (c1j, c1p) = (self.feats.at(j), self.feats.at(j - self.past));
        let mut sq = 0.0;
        for d in 0..FEATURES {
            let mu = (c1k[d] - c1l[d]) / span;
            let sd = ((c2k[d] - c2l[d]) / span - mu * mu).max(0.0).sqrt() + 1e-6;
            let before = (c1j[d] - c1p[d]) / self.past as f64;
            let after = (c1k[d] - c1j[d]) / self.next as f64;
            let z = (before - after) / sd;
            sq += z * z;
        }
        sq.sqrt() / (FEATURES as f64).sqrt()
    }

    fn nov_at(&self, i: usize) -> f64 {
        self.nov[i - self.nov_first]
    }

    /// The change decision for tick `c` (the novelty through `c + peak` is known), at time `t`.
    fn change(&mut self, c: usize, t: f64) -> Option<Change> {
        let value = self.nov_at(c);
        if value.is_nan() {
            return None;
        }
        self.prior.clear();
        for k in c.saturating_sub(self.history)..c {
            let v = self.nov_at(k);
            if !v.is_nan() {
                self.prior.push(v);
            }
        }
        if self.prior.len() < self.p.change_min_prior {
            return None;
        }
        self.prior.sort_by(f64::total_cmp);
        let median = median_sorted(&self.prior);
        let threshold = median + self.p.change_k * (percentile_linear(&self.prior, 90.0) - median);
        let peak = (c.saturating_sub(self.peak)..=c + self.peak)
            .map(|k| self.nov_at(k))
            .filter(|v| !v.is_nan())
            .fold(f64::NEG_INFINITY, f64::max);
        if value >= peak && value > threshold {
            let tick_time = self.times[self.times.len() - 1 - (self.peak + self.next - 1)];
            Some(Change {
                t: tick_time - self.center_s,
                known_at: t,
                strength: value,
            })
        } else {
            None
        }
    }

    fn jump(&mut self, i: usize, t: f64) -> Option<Jump> {
        if i + 1 < self.recent + self.before {
            return None;
        }
        let end = i + 1;
        let now = self.loudness.window_mean(end, self.recent, 0);
        let then = self.loudness.window_mean(end - self.recent, self.before, 0);
        let db = now - then;
        if db.abs() >= self.p.jump_db && t - self.last_jump > self.p.jump_gap_s {
            self.last_jump = t;
            return Some(Jump {
                t: t - self.p.jump_recent_s,
                known_at: t,
                db,
            });
        }
        None
    }

    fn end(&mut self, i: usize, t: f64) -> Option<End> {
        let second = self.loudness.window_mean(i + 1, self.smooth, 0);
        self.seconds.push_back(second);
        while self.seconds.len() > self.end_window + 1 {
            self.seconds.pop_front();
        }
        let loudest = self
            .seconds
            .iter()
            .copied()
            .fold(f64::NEG_INFINITY, f64::max);
        self.run = if self.last_db < loudest - self.p.end_drop_db {
            self.run + 1
        } else {
            0
        };
        (self.run == self.hold && t >= self.p.end_after_s - 1e-9).then_some(End {
            t: t - self.p.end_hold_s,
            known_at: t,
        })
    }
}
