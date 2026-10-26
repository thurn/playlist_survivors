//! Hindsight grids and the phase check (`stream.py`'s `hindsight_entries`, `online_beats`,
//! `offline_phase` and `phase_shift`).
//!
//! Every `everyS` of audio, at window end `T`, madmom's offline beat model decodes the last
//! `windowS` of samples (`RNNBeatProcessor(fps=100)` then `DBNBeatTrackingProcessor`'s Viterbi
//! decode with its beat correction); the entry keeps the decode's beats and the causal tracker's
//! believed beats from `T - keepS` to `T`. Live, a decode takes time, so an entry becomes usable
//! by the phase check at sample time `T + D` (docs/design/native-app.md, "Hindsight's live
//! latency"); its `known_at` is then `T + D`, and `D = 0` reproduces Python.
//!
//! Each step follows the Python libraries to the bit where they are discrete:
//! - the offline front end: frames of 1024, 2048 and 4096 samples centred on `n * 441`
//!   (madmom's offline origin), the float64 Hann window and FFT, complex64 bins, float32
//!   magnitudes, each resolution's 6-bands-per-octave filterbank, `log10(x + 1)`, positive
//!   differences, stacked in that order (266 features);
//! - the BiLSTM ensemble: eight networks of three bidirectional layers (the backward LSTM runs
//!   the frames reversed; its outputs follow the forward ones) and a sigmoid unit, averaged;
//! - the Viterbi decode in float64 log space (the densities' logs in float32, as numpy computes
//!   them), keeping the first best predecessor, then the peak activation in each run of beat
//!   states;
//! - the phase check's complex arithmetic as numpy does it (`2j * pi * x / period` divides by the
//!   period as a complex, multiplying by its reciprocal; the complex mean is numpy's pairwise sum
//!   of pairs times `1 / n`), with the C library's `cos`, `sin`, `hypot` and `atan2`.

use std::collections::VecDeque;
use std::f64::consts::PI;
use std::sync::Arc;

use realfft::num_complex::Complex;
use realfft::{RealFftPlanner, RealToComplex};

use crate::frontend::{HOP, hanning};
use crate::rnn::{Lstm, LstmState, affine, sigmoid, sized};
use crate::tensors::{TensorError, Tensors};

/// The hindsight grids' and the phase check's parameters (`stream.py`'s `Params`), and the live
/// delay `D`.
#[derive(Clone, Debug, PartialEq)]
pub struct HindsightParams {
    pub every_s: f64,
    pub window_s: f64,
    pub keep_s: f64,
    pub bias_s: f64,
    /// `D`: a window ending at `T` is usable from sample time `T + D` (0 reproduces Python).
    pub delay_s: f64,
    pub phase_settle_s: f64,
    pub phase_min_beats: usize,
    pub phase_min_r: f64,
    pub phase_min_shift: f64,
    pub phase_level_tol: f64,
    pub phase_level_share: f64,
    pub phase_persist: usize,
    pub phase_agree: f64,
}

/// `D` live: phase 0 set it from the spike's decode times (docs/design/native-app.md, "Threads").
pub const LIVE_DELAY_S: f64 = 0.67;

impl HindsightParams {
    /// The stream's parameters with the live delay `D`.
    pub fn live() -> Self {
        HindsightParams {
            delay_s: LIVE_DELAY_S,
            ..HindsightParams::default()
        }
    }
}

impl Default for HindsightParams {
    fn default() -> Self {
        HindsightParams {
            every_s: 1.0,
            window_s: 20.0,
            keep_s: 8.0,
            bias_s: 0.0,
            delay_s: 0.0,
            phase_settle_s: 1.0,
            phase_min_beats: 4,
            phase_min_r: 0.6,
            phase_min_shift: 0.2,
            phase_level_tol: 0.06,
            phase_level_share: 0.9,
            phase_persist: 2,
            phase_agree: 0.95,
        }
    }
}

// ---------------------------------------------------------------- the offline front end

struct Resolution {
    size: usize,
    window: Vec<f64>,
    filterbank: Vec<Vec<(usize, f32)>>,
    bands: usize,
    diff_frames: usize,
    fft: Arc<dyn RealToComplex<f64>>,
}

impl Resolution {
    fn new(
        models: &Tensors,
        size: usize,
        planner: &mut RealFftPlanner<f64>,
    ) -> Result<Self, TensorError> {
        let name = format!("offline.{size}.filterbank");
        let bands = match models.shape(&name)? {
            &[bins, bands] if bins == size / 2 && bands > 0 => bands,
            other => return Err(TensorError::Format(format!("{name}: shape {other:?}"))),
        };
        let filterbank = models
            .f32(&name)?
            .chunks_exact(bands)
            .map(|row| {
                row.iter()
                    .enumerate()
                    .filter(|(_, w)| **w != 0.0)
                    .map(|(b, w)| (b, *w))
                    .collect()
            })
            .collect();
        let diff_frames = match models
            .i32(&format!("offline.{size}.diff_frames"))?
            .as_slice()
        {
            &[d] if d >= 1 => d as usize,
            other => {
                return Err(TensorError::Format(format!(
                    "offline.{size}.diff_frames: {other:?}"
                )));
            }
        };
        Ok(Resolution {
            size,
            window: hanning(size),
            filterbank,
            bands,
            diff_frames,
            fft: planner.plan_fft_forward(size),
        })
    }
}

/// madmom's offline beat front end over a whole window (batch).
pub struct OfflineFrontEnd {
    res: Vec<Resolution>,
}

impl OfflineFrontEnd {
    pub fn new(models: &Tensors) -> Result<Self, TensorError> {
        let mut planner = RealFftPlanner::<f64>::new();
        let res = [1024, 2048, 4096]
            .into_iter()
            .map(|s| Resolution::new(models, s, &mut planner))
            .collect::<Result<_, _>>()?;
        Ok(OfflineFrontEnd { res })
    }

    /// Features per frame (266 with madmom's filterbanks).
    pub fn width(&self) -> usize {
        self.res.iter().map(|r| 2 * r.bands).sum()
    }

    /// The features of a window of samples: `ceil(len / 441)` frames, row-major.
    pub fn process(&self, y: &[f32]) -> (usize, Vec<f32>) {
        let frames = y.len().div_ceil(HOP);
        let width = self.width();
        let mut out = vec![0.0f32; frames * width];
        let mut col = 0;
        for r in &self.res {
            let mut fft_in = r.fft.make_input_vec();
            let mut fft_out: Vec<Complex<f64>> = r.fft.make_output_vec();
            let mut scratch = r.fft.make_scratch_vec();
            let mut acc = vec![0.0f64; r.bands];
            let mut specs = vec![0.0f32; frames * r.bands];
            let half = r.size / 2;
            for f in 0..frames {
                // (Centred: frame f starts at f * 441 - size / 2, zeros outside the window.)
                let start = (f * HOP) as isize - half as isize;
                for (i, v) in fft_in.iter_mut().enumerate() {
                    let at = start + i as isize;
                    let s = if at < 0 || at as usize >= y.len() {
                        0.0
                    } else {
                        y[at as usize]
                    };
                    *v = f64::from(s) * r.window[i];
                }
                r.fft
                    .process_with_scratch(&mut fft_in, &mut fft_out, &mut scratch)
                    .expect("FFT buffers are sized by the plan");
                acc.fill(0.0);
                for (row, c) in r.filterbank.iter().zip(&fft_out) {
                    if row.is_empty() {
                        continue;
                    }
                    let mag = (c.re as f32).hypot(c.im as f32);
                    for &(band, w) in row {
                        acc[band] += f64::from(mag * w);
                    }
                }
                for (s, a) in specs[f * r.bands..(f + 1) * r.bands].iter_mut().zip(&acc) {
                    *s = (*a as f32 + 1.0).log10();
                }
            }
            for f in 0..frames {
                let row = &mut out[f * width + col..f * width + col + 2 * r.bands];
                row[..r.bands].copy_from_slice(&specs[f * r.bands..(f + 1) * r.bands]);
                if f >= r.diff_frames {
                    let back = (f - r.diff_frames) * r.bands;
                    for b in 0..r.bands {
                        row[r.bands + b] = (specs[f * r.bands + b] - specs[back + b]).max(0.0);
                    }
                }
            }
            col += 2 * r.bands;
        }
        (frames, out)
    }
}

// ---------------------------------------------------------------- the BiLSTM ensemble

struct BiNetwork {
    /// (forward, backward) per layer.
    layers: Vec<(Lstm, Lstm)>,
    out_w: Vec<f32>,
    out_b: Vec<f32>,
}

/// madmom's offline beat ensemble (`BEATS_BLSTM`, `offline.<net>.<layer>.{fwd,bwd}` in
/// `madmom.bin`).
pub struct OfflineEnsemble {
    nets: Vec<BiNetwork>,
    width: usize,
}

impl OfflineEnsemble {
    pub fn new(models: &Tensors) -> Result<Self, TensorError> {
        let width = match models.shape("offline.0.0.fwd.input_gate.w")? {
            &[inputs, _] => inputs,
            other => {
                return Err(TensorError::Format(format!(
                    "offline.0.0.fwd.input_gate.w: shape {other:?}"
                )));
            }
        };
        let mut nets = Vec::new();
        while models
            .shape(&format!("offline.{}.0.fwd.cell.b", nets.len()))
            .is_ok()
        {
            let n = nets.len();
            let mut layers: Vec<(Lstm, Lstm)> = Vec::new();
            while models
                .shape(&format!("offline.{n}.{}.fwd.cell.b", layers.len()))
                .is_ok()
            {
                let l = layers.len();
                let inputs = layers.last().map_or(width, |(f, b)| f.hidden + b.hidden);
                layers.push((
                    Lstm::load(models, &format!("offline.{n}.{l}.fwd"), inputs)?,
                    Lstm::load(models, &format!("offline.{n}.{l}.bwd"), inputs)?,
                ));
            }
            let hidden = layers.last().map_or(width, |(f, b)| f.hidden + b.hidden);
            let ff = format!("offline.{n}.{}.ff", layers.len());
            nets.push(BiNetwork {
                out_w: sized(models, &format!("{ff}.w"), hidden)?,
                out_b: sized(models, &format!("{ff}.b"), 1)?,
                layers,
            });
        }
        if nets.is_empty() {
            return Err(TensorError::Missing("offline.0.0.fwd.cell.b".into()));
        }
        Ok(OfflineEnsemble { nets, width })
    }

    pub fn input_width(&self) -> usize {
        self.width
    }

    /// The averaged activations of `frames` feature rows, on the calling thread.
    pub fn activations(&self, features: &[f32], frames: usize) -> Vec<f32> {
        self.activations_with_jobs(features, frames, 1)
    }

    /// Independent models run in bounded batches; reduce their outputs in model order,
    /// preserving the inline arithmetic. Scoped workers are joined even during unwinding,
    /// and a worker panic is propagated on the caller. Live analysis defaults to one job.
    pub fn activations_with_jobs(&self, features: &[f32], frames: usize, jobs: usize) -> Vec<f32> {
        assert_eq!(features.len(), frames * self.width, "features per frame");
        let jobs = jobs.clamp(1, self.nets.len());
        let mut sum = vec![0.0f32; frames];
        for batch in self.nets.chunks(jobs) {
            let outputs = if jobs == 1 {
                vec![self.model_activations(&batch[0], features, frames)]
            } else {
                std::thread::scope(|scope| {
                    let handles: Vec<_> = batch
                        .iter()
                        .map(|net| {
                            scope.spawn(move || self.model_activations(net, features, frames))
                        })
                        .collect();
                    handles
                        .into_iter()
                        .map(|h| h.join().unwrap_or_else(|e| std::panic::resume_unwind(e)))
                        .collect::<Vec<_>>()
                })
            };
            for values in outputs {
                for (sum, value) in sum.iter_mut().zip(values) {
                    *sum += value;
                }
            }
        }
        let n = self.nets.len() as f32;
        sum.iter().map(|s| s / n).collect()
    }

    fn model_activations(&self, net: &BiNetwork, features: &[f32], frames: usize) -> Vec<f32> {
        let mut values = vec![0.0f32; frames];
        let mut out = [0.0f32];
        let mut x = features.to_vec();
        let mut inputs = self.width;
        for (fwd, bwd) in &net.layers {
            let hidden = fwd.hidden + bwd.hidden;
            let mut y = vec![0.0f32; frames * hidden];
            let mut st = LstmState::new(fwd.hidden);
            for f in 0..frames {
                fwd.step(&x[f * inputs..(f + 1) * inputs], &mut st);
                y[f * hidden..f * hidden + fwd.hidden].copy_from_slice(&st.h);
            }
            let mut st = LstmState::new(bwd.hidden);
            for f in (0..frames).rev() {
                bwd.step(&x[f * inputs..(f + 1) * inputs], &mut st);
                y[f * hidden + fwd.hidden..(f + 1) * hidden].copy_from_slice(&st.h);
            }
            x = y;
            inputs = hidden;
        }
        for (f, s) in values.iter_mut().enumerate() {
            affine(
                &x[f * inputs..(f + 1) * inputs],
                &net.out_w,
                &net.out_b,
                &mut out,
            );
            *s = sigmoid(out[0]);
        }
        values
    }
}

// ---------------------------------------------------------------- the offline DBN

/// madmom's offline beat-tracking DBN (`dbn_offline` in `madmom.bin`): Viterbi and beat
/// correction.
pub struct OfflineDbn {
    indptr: Vec<usize>,
    prev: Vec<usize>,
    log_prob: Vec<f64>,
    beat_state: Vec<bool>,
    lambda: f32,
    fps: f64,
    /// The back pointers' buffer, reused across decodes (frames x states).
    bt: Vec<u16>,
}

impl OfflineDbn {
    pub fn new(models: &Tensors, fps: f64) -> Result<Self, TensorError> {
        let index = |name: &str| -> Result<Vec<usize>, TensorError> {
            models
                .i32(name)?
                .into_iter()
                .map(|v| {
                    usize::try_from(v).map_err(|_| TensorError::Format(format!("{name}: {v}")))
                })
                .collect()
        };
        let indptr = index("dbn_offline.indptr")?;
        let prev = index("dbn_offline.prev")?;
        let log_prob = models.f64("dbn_offline.log_prob")?;
        let om = index("dbn_offline.om_pointers")?;
        let lambda = match models.f64("dbn_offline.observation_lambda")?.as_slice() {
            &[l] if l > 1.0 => l as f32,
            other => {
                return Err(TensorError::Format(format!(
                    "dbn_offline.observation_lambda: {other:?}"
                )));
            }
        };
        let states = om.len();
        if states == 0
            || indptr.len() != states + 1
            || indptr[states] != prev.len()
            || prev.len() != log_prob.len()
            || prev.iter().any(|&p| p >= states)
            || om.iter().any(|&o| o > 1)
        {
            return Err(TensorError::Format(
                "dbn_offline: malformed state space".into(),
            ));
        }
        if states > usize::from(u16::MAX) {
            return Err(TensorError::Format(
                "dbn_offline: too many states for the back pointers".into(),
            ));
        }
        Ok(OfflineDbn {
            indptr,
            prev,
            log_prob,
            beat_state: om.into_iter().map(|o| o == 1).collect(),
            lambda,
            fps,
            bt: Vec::new(),
        })
    }

    /// The decoded beat times (s from the window's start), as `process_offline` returns them.
    pub fn beats(&mut self, acts: &[f32]) -> Vec<f64> {
        if !acts.iter().any(|&a| a != 0.0) {
            return Vec::new();
        }
        let states = self.beat_state.len();
        let frames = acts.len();
        // (np.log of the uniform initial distribution: log(1 / n).)
        let mut prev_v = vec![(1.0 / states as f64).ln(); states];
        let mut cur = vec![0.0f64; states];
        let bt = &mut self.bt;
        bt.clear();
        bt.resize(frames * states, 0);
        for (f, &a) in acts.iter().enumerate() {
            let (non_beat, beat) = crate::dbn::log_densities(a, self.lambda);
            for s in 0..states {
                let density = if self.beat_state[s] { beat } else { non_beat };
                let mut best = f64::NEG_INFINITY;
                let mut from = 0;
                for k in self.indptr[s]..self.indptr[s + 1] {
                    let p = prev_v[self.prev[k]] + self.log_prob[k] + density;
                    if p > best {
                        best = p;
                        from = self.prev[k];
                    }
                }
                cur[s] = best;
                bt[f * states + s] = from as u16;
            }
            prev_v.copy_from_slice(&cur);
        }
        let mut state = 0;
        for s in 1..states {
            if prev_v[s] > prev_v[state] {
                state = s;
            }
        }
        if prev_v[state] == f64::NEG_INFINITY {
            return Vec::new();
        }
        let mut path = vec![0usize; frames];
        for f in (0..frames).rev() {
            path[f] = state;
            state = usize::from(bt[f * states + state]);
        }
        if path.iter().all(|&s| s == 0) {
            return Vec::new();
        }
        // The correction: the peak activation in each run of beat states.
        let in_beat: Vec<bool> = path.iter().map(|&s| self.beat_state[s]).collect();
        let mut beats = Vec::new();
        let mut f = 0;
        while f < frames {
            if !in_beat[f] {
                f += 1;
                continue;
            }
            let start = f;
            while f < frames && in_beat[f] {
                f += 1;
            }
            let mut peak = start;
            for g in start + 1..f {
                if acts[g] > acts[peak] {
                    peak = g;
                }
            }
            beats.push(peak as f64 / self.fps);
        }
        beats
    }
}

// ---------------------------------------------------------------- the decoder

/// The offline model's whole decode of one window.
pub struct HindsightDecoder {
    fe: OfflineFrontEnd,
    nn: OfflineEnsemble,
    dbn: OfflineDbn,
    sample_rate: usize,
    model_jobs: usize,
}

impl HindsightDecoder {
    pub fn new(models: &Tensors, fps: f64, sample_rate: usize) -> Result<Self, TensorError> {
        let fe = OfflineFrontEnd::new(models)?;
        let nn = OfflineEnsemble::new(models)?;
        if fe.width() != nn.input_width() {
            return Err(TensorError::Format(format!(
                "the offline ensemble takes {} features, the front end gives {}",
                nn.input_width(),
                fe.width()
            )));
        }
        Ok(HindsightDecoder {
            fe,
            nn,
            dbn: OfflineDbn::new(models, fps)?,
            sample_rate,
            model_jobs: 1,
        })
    }

    /// Bound offline ensemble parallelism for batch preparation; live callers keep one job.
    #[must_use]
    pub fn with_model_jobs(mut self, jobs: usize) -> Self {
        self.model_jobs = jobs.clamp(1, self.nn.nets.len());
        self
    }

    /// The decoded beats of a window's samples (s from its start); none under a second of audio
    /// or 101 frames, as Python skips them.
    pub fn decode(&mut self, window: &[f32]) -> Vec<f64> {
        if window.len() < self.sample_rate {
            return Vec::new();
        }
        let (frames, features) = self.fe.process(window);
        let acts = self
            .nn
            .activations_with_jobs(&features, frames, self.model_jobs);
        if acts.len() <= 100 {
            return Vec::new();
        }
        self.dbn.beats(&acts)
    }

    /// The offline front end, ensemble and DBN, for tests of each stage.
    pub fn stages(&mut self) -> (&OfflineFrontEnd, &OfflineEnsemble, &mut OfflineDbn) {
        (&self.fe, &self.nn, &mut self.dbn)
    }
}

// ---------------------------------------------------------------- entries and the phase check

/// A hindsight entry.
#[derive(Clone, Debug, PartialEq)]
pub struct HindsightEntry {
    /// When the phase check may use it (s): the window's end plus `D`.
    pub known_at: f64,
    /// The window's end (s).
    pub window_end: f64,
    /// The offline decode's beats in the kept span.
    pub offline: Vec<f64>,
    /// The causal tracker's believed beats in the kept span, as believed by the window's end.
    pub online: Vec<f64>,
}

/// The causal tracker's believed beats as it goes (`online_beats`): a beat is believed when the
/// most likely state passes it (the time since the last beat starts again).
pub struct OnlineBeats {
    keep_s: f64,
    fps: f64,
    bias_s: f64,
    prev_since: Option<f64>,
    /// (time with biasS, frame) of the recent believed beats.
    beats: VecDeque<(f64, usize)>,
}

impl OnlineBeats {
    pub fn new(keep_s: f64, fps: f64, bias_s: f64) -> Self {
        OnlineBeats {
            keep_s,
            fps,
            bias_s,
            prev_since: None,
            beats: VecDeque::new(),
        }
    }

    /// The DBN's `since` at frame `n` (frames in order).
    pub fn push(&mut self, n: usize, since: f64) {
        if let Some(prev) = self.prev_since
            && since < prev - 1e-9
        {
            self.beats
                .push_back((n as f64 / self.fps - since + self.bias_s, n));
        }
        self.prev_since = Some(since);
        // (Beats older than the kept span before the newest window end can't be kept again.)
        let newest = n as f64 / self.fps;
        while self
            .beats
            .front()
            .is_some_and(|&(b, _)| b < newest - self.keep_s - 2.0)
        {
            self.beats.pop_front();
        }
    }

    /// The believed beats by `frame` within `[lo, hi]`.
    pub fn kept(&self, frame: usize, lo: f64, hi: f64) -> Vec<f64> {
        self.beats
            .iter()
            .filter(|&&(b, n)| n <= frame && lo <= b && b <= hi)
            .map(|&(b, _)| b)
            .collect()
    }
}

/// numpy's `np.exp(2j * np.pi * x / period)` for real `x` (the division by `period` as a
/// complex: multiplied by its reciprocal), as (cos, sin).
pub(crate) fn unit(x: f64, period: f64) -> (f64, f64) {
    let two_pi = 2.0 * PI;
    // (0 + 2πi)(x + 0i), then Smith's division by (period + 0i).
    let re = 0.0 * x - two_pi * 0.0;
    let im = 0.0 * 0.0 + two_pi * x;
    let rat = 0.0 / period;
    let scl = 1.0 / (period + 0.0 * rat);
    let (re, im) = ((re + im * rat) * scl, (im - re * rat) * scl);
    let e = re.exp();
    (e * im.cos(), e * im.sin())
}

/// numpy's complex mean: the pairwise sum of (re, im) pairs, times `1 / n` (Smith's division).
pub(crate) fn complex_mean(z: &[(f64, f64)]) -> (f64, f64) {
    fn pairwise(a: &[f64]) -> (f64, f64) {
        let n = a.len();
        if n < 8 {
            let (mut rr, mut ri) = (-0.0, -0.0);
            for pair in a.as_chunks::<2>().0 {
                rr += pair[0];
                ri += pair[1];
            }
            return (rr, ri);
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
            let (mut rr, mut ri) = ((r[0] + r[2]) + (r[4] + r[6]), (r[1] + r[3]) + (r[5] + r[7]));
            for pair in a[whole..].as_chunks::<2>().0 {
                rr += pair[0];
                ri += pair[1];
            }
            return (rr, ri);
        }
        let mut half = n / 2;
        half -= half % 8;
        let (a1, b1) = pairwise(&a[..half]);
        let (a2, b2) = pairwise(&a[half..]);
        (a1 + a2, b1 + b2)
    }
    let flat: Vec<f64> = z.iter().flat_map(|&(r, i)| [r, i]).collect();
    let (sr, si) = pairwise(&flat);
    let n = z.len() as f64;
    let rat = 0.0 / n;
    let scl = 1.0 / (n + 0.0 * rat);
    ((sr + si * rat) * scl, (si - sr * rat) * scl)
}

/// The phase (in periods, -0.5 to 0.5) of an entry's settled offline beats on the believed grid
/// (a beat at `last`), or none unless they agree on one at the believed tempo.
fn offline_phase(
    entry: &HindsightEntry,
    last: f64,
    period: f64,
    p: &HindsightParams,
) -> Option<f64> {
    let settled: Vec<f64> = entry
        .offline
        .iter()
        .copied()
        .filter(|&b| b <= entry.window_end - p.phase_settle_s)
        .collect();
    if settled.len() < p.phase_min_beats {
        return None;
    }
    let level = settled
        .windows(2)
        .filter(|w| ((w[1] - w[0]) / period - 1.0).abs() <= p.phase_level_tol)
        .count();
    if (level as f64) / ((settled.len() - 1) as f64) < p.phase_level_share {
        return None;
    }
    let z: Vec<(f64, f64)> = settled.iter().map(|&b| unit(b - last, period)).collect();
    let (re, im) = complex_mean(&z);
    if re.hypot(im) < p.phase_min_r {
        return None;
    }
    Some(im.atan2(re) / (2.0 * PI))
}

/// The shift (s) that puts the believed grid on the phase of the latest entry and the
/// `persist - 1` before it, or 0 while they don't agree on one at least `minShift` off.
/// `entries` are the latest usable ones, oldest first.
pub fn phase_shift(entries: &[HindsightEntry], last: f64, period: f64, p: &HindsightParams) -> f64 {
    if entries.len() < p.phase_persist {
        return 0.0;
    }
    let recent = &entries[entries.len() - p.phase_persist..];
    let mut phases = Vec::with_capacity(recent.len());
    for e in recent {
        match offline_phase(e, last, period, p) {
            Some(ph) => phases.push(ph),
            None => return 0.0,
        }
    }
    let z: Vec<(f64, f64)> = phases.iter().map(|&ph| unit(ph, 1.0)).collect();
    let (re, im) = complex_mean(&z);
    if re.hypot(im) < p.phase_agree {
        return 0.0;
    }
    let shift = phases[phases.len() - 1];
    if shift.abs() >= p.phase_min_shift {
        shift * period
    } else {
        0.0
    }
}
