//! madmom's beat front ends: framing, STFT magnitude, log filterbank, positive
//! differences, stacked.
//!
//! - Online (one 2048 frame, 12 bands per octave): frame `n` ends at sample
//!   `floor(n * 441)` inclusive (madmom's `origin='online'`), so it is known
//!   once that sample has arrived.
//! - Offline (1024/2048/4096, 6 bands): frames centred on `floor(n * 441)`,
//!   batch over a whole window, as hindsight needs.

use std::sync::Arc;

use realfft::{RealFftPlanner, RealToComplex};

use crate::models::Tensors;

pub const HOP: f64 = 44100.0 / 100.0;

/// One resolution: window, filterbank, diff distance.
struct Resolution {
    size: usize,
    window: Vec<f32>,
    /// The filterbank's non-zeros per bin: (band, weight).
    filterbank: Vec<Vec<(usize, f32)>>,
    bands: usize,
    diff_frames: usize,
    fft: Arc<dyn RealToComplex<f32>>,
    scratch_in: Vec<f32>,
    scratch_out: Vec<realfft::num_complex::Complex<f32>>,
    scratch: Vec<realfft::num_complex::Complex<f32>>,
}

impl Resolution {
    fn new(t: &Tensors, prefix: &str, size: usize, planner: &mut RealFftPlanner<f32>) -> Self {
        let fb_name = format!("{prefix}.{size}.filterbank");
        let bands = t.shape(&fb_name)[1];
        let fft = planner.plan_fft_forward(size);
        let scratch_in = fft.make_input_vec();
        let scratch_out = fft.make_output_vec();
        let scratch = fft.make_scratch_vec();
        let dense = t.f32(&fb_name);
        let filterbank = dense
            .chunks_exact(bands)
            .map(|row| {
                row.iter()
                    .enumerate()
                    .filter(|(_, w)| **w != 0.0)
                    .map(|(b, w)| (b, *w))
                    .collect()
            })
            .collect();
        Resolution {
            size,
            window: t.f32(&format!("{prefix}.{size}.window")),
            filterbank,
            bands,
            diff_frames: t.i32(&format!("{prefix}.{size}.diff_frames"))[0] as usize,
            fft,
            scratch_in,
            scratch_out,
            scratch,
        }
    }

    /// Log filtered spectrum of one frame whose first sample is `start`
    /// (absolute), reading `get(i)` (zero outside the signal).
    fn log_spec(&mut self, frame: impl Fn(usize) -> f32, out: &mut [f32]) {
        for i in 0..self.size {
            self.scratch_in[i] = frame(i) * self.window[i];
        }
        self.fft
            .process_with_scratch(
                &mut self.scratch_in,
                &mut self.scratch_out,
                &mut self.scratch,
            )
            .unwrap();
        out.fill(0.0);
        let half = self.size / 2;
        for bin in 0..half {
            let row = &self.filterbank[bin];
            if row.is_empty() {
                continue;
            }
            let mag = self.scratch_out[bin].norm();
            for &(b, w) in row {
                out[b] += mag * w;
            }
        }
        for o in out.iter_mut() {
            *o = (*o + 1.0).log10();
        }
    }
}

/// The online front end, streaming: feed samples, get 162-wide feature frames.
pub struct OnlineFrontEnd {
    res: Resolution,
    /// The last `size` samples, ending at absolute sample `received - 1`.
    ring: Vec<f32>,
    received: usize,
    /// Index of the next frame to emit.
    pub next_frame: usize,
    /// Past log spectra, newest last, `diff_frames + 1` deep.
    history: std::collections::VecDeque<Vec<f32>>,
}

impl OnlineFrontEnd {
    pub fn new(t: &Tensors) -> Self {
        let mut planner = RealFftPlanner::new();
        let res = Resolution::new(t, "online", 2048, &mut planner);
        OnlineFrontEnd {
            ring: vec![0.0; res.size],
            res,
            received: 0,
            next_frame: 0,
            history: Default::default(),
        }
    }

    pub fn width(&self) -> usize {
        2 * self.res.bands
    }

    /// Push one sample; calls `emit(frame_index, features)` for every frame it completes.
    pub fn push(&mut self, samples: &[f32], mut emit: impl FnMut(usize, &[f32])) {
        let size = self.res.size;
        let mut feat = vec![0.0f32; self.width()];
        for &s in samples {
            self.ring[self.received % size] = s;
            self.received += 1;
            // Frame n ends at floor(n * HOP): complete once that sample is in.
            while ((self.next_frame as f64 * HOP) as usize) < self.received {
                let end = (self.next_frame as f64 * HOP) as usize; // inclusive
                let received = self.received;
                let ring = &self.ring;
                let mut spec = vec![0.0f32; self.res.bands];
                self.res.log_spec(
                    |i| {
                        // Absolute sample of frame position i.
                        let abs = end as isize - (size as isize - 1) + i as isize;
                        if abs < 0 || abs as usize >= received {
                            0.0
                        } else {
                            ring[abs as usize % size]
                        }
                    },
                    &mut spec,
                );
                let df = self.res.diff_frames;
                let bands = self.res.bands;
                feat[..bands].copy_from_slice(&spec);
                if self.history.len() >= df {
                    let past = &self.history[self.history.len() - df];
                    for b in 0..bands {
                        feat[bands + b] = (spec[b] - past[b]).max(0.0);
                    }
                } else {
                    feat[bands..].fill(0.0);
                }
                self.history.push_back(spec);
                while self.history.len() > df {
                    self.history.pop_front();
                }
                emit(self.next_frame, &feat);
                self.next_frame += 1;
            }
        }
    }
}

/// The offline front end over a whole window: `num_frames x 266`, row-major.
pub struct OfflineFrontEnd {
    res: Vec<Resolution>,
}

impl OfflineFrontEnd {
    pub fn new(t: &Tensors) -> Self {
        let mut planner = RealFftPlanner::new();
        let res = [1024, 2048, 4096]
            .iter()
            .map(|&s| Resolution::new(t, "offline", s, &mut planner))
            .collect();
        OfflineFrontEnd { res }
    }

    pub fn width(&self) -> usize {
        self.res.iter().map(|r| 2 * r.bands).sum()
    }

    pub fn process(&mut self, y: &[f32]) -> (usize, Vec<f32>) {
        let n = (y.len() as f64 / HOP).ceil() as usize;
        let width = self.width();
        let mut out = vec![0.0f32; n * width];
        let mut col = 0;
        for r in &mut self.res {
            let bands = r.bands;
            let half = (r.size / 2) as isize;
            let mut specs = vec![0.0f32; n * bands];
            for f in 0..n {
                let start = (f as f64 * HOP) as isize - half;
                r.log_spec(
                    |i| {
                        let abs = start + i as isize;
                        if abs < 0 || abs as usize >= y.len() {
                            0.0
                        } else {
                            y[abs as usize]
                        }
                    },
                    &mut specs[f * bands..(f + 1) * bands],
                );
            }
            let df = r.diff_frames;
            for f in 0..n {
                let row = &mut out[f * width + col..f * width + col + 2 * bands];
                row[..bands].copy_from_slice(&specs[f * bands..(f + 1) * bands]);
                for b in 0..bands {
                    row[bands + b] = if f >= df {
                        (specs[f * bands + b] - specs[(f - df) * bands + b]).max(0.0)
                    } else {
                        0.0
                    };
                }
            }
            col += 2 * bands;
        }
        (n, out)
    }
}
