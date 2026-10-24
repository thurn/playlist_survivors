//! The online beat model's front end, streaming (madmom's `RNNBeatProcessor(online=True,
//! origin="online", fps=100)` pre-processing): framing, STFT magnitude, log filterbank, positive
//! differences, stacked into 162 features per 10 ms frame.
//!
//! Each step follows madmom's own float path (its dtypes and order of operations; numpy's
//! float32 BLAS sums are accumulated in float64 here, so parity is measured, not exact):
//! - framing (`signal_frame`, `origin='online'`): frame `n` covers samples `n * 441 - 2047` to
//!   `n * 441` inclusive, zeros before the signal; it is known once sample `n * 441` arrives, and
//!   a signal of `len` samples has `ceil(len / 441)` frames, exactly those a stream completes;
//! - the STFT: the float32 frame times numpy's float64 Hann window, a float64 real FFT, the
//!   first 1024 bins rounded to complex64, their float32 magnitudes;
//! - the filterbank (madmom's log filterbank, 12 bands per octave, 30 Hz to 17 kHz,
//!   normalised; exported in `madmom.bin`), then `log10(1 * x + 1)` in float32;
//! - positive differences against the frame `diff_frames` back (zero for the first
//!   `diff_frames` frames, as madmom's batch difference gives), stacked after the spectrum.

use std::collections::VecDeque;
use std::f64::consts::PI;
use std::sync::Arc;

use realfft::num_complex::Complex;
use realfft::{RealFftPlanner, RealToComplex};

use crate::tensors::{TensorError, Tensors};

/// Samples per second the analyzer runs at.
pub const SAMPLE_RATE: u32 = 44_100;
/// Frames per second (madmom's `fps`).
pub const FPS: u32 = 100;
/// Samples between frames: 441, an integer at 44.1 kHz.
pub const HOP: usize = (SAMPLE_RATE / FPS) as usize;
/// The online model's frame size.
pub const FRAME_SIZE: usize = 2048;

/// numpy's `np.hanning(m)` in float64: `0.5 + 0.5 cos(pi n / (m - 1))` for `n = 1 - m, 3 - m, ...`.
pub fn hanning(m: usize) -> Vec<f64> {
    (0..m)
        .map(|k| {
            let n = 1.0 - m as f64 + 2.0 * k as f64;
            0.5 + 0.5 * (PI * n / (m as f64 - 1.0)).cos()
        })
        .collect()
}

/// The streaming online front end: feed samples, get one 162-wide feature frame per 10 ms.
pub struct OnlineFrontEnd {
    window: Vec<f64>,
    /// The filterbank's non-zero weights per FFT bin: (band, weight).
    filterbank: Vec<Vec<(usize, f32)>>,
    bands: usize,
    diff_frames: usize,
    fft: Arc<dyn RealToComplex<f64>>,
    fft_in: Vec<f64>,
    fft_out: Vec<Complex<f64>>,
    fft_scratch: Vec<Complex<f64>>,
    /// The last `FRAME_SIZE` samples; sample `i` (absolute) at `i % FRAME_SIZE`.
    ring: Vec<f32>,
    received: usize,
    next_frame: usize,
    /// The last `diff_frames` log spectra, oldest first.
    history: VecDeque<Vec<f32>>,
    acc: Vec<f64>,
    spec: Vec<f32>,
    features: Vec<f32>,
}

impl OnlineFrontEnd {
    /// The front end with `madmom.bin`'s online filterbank and difference distance.
    pub fn new(models: &Tensors) -> Result<Self, TensorError> {
        let name = "online.2048.filterbank";
        let bands = match models.shape(name)? {
            &[bins, bands] if bins == FRAME_SIZE / 2 && bands > 0 => bands,
            other => {
                return Err(TensorError::Format(format!(
                    "{name}: shape {other:?}, not [{}, bands]",
                    FRAME_SIZE / 2
                )));
            }
        };
        let dense = models.f32(name)?;
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
        let diff_frames = match models.i32("online.2048.diff_frames")?.as_slice() {
            &[d] if d >= 1 => d,
            other => {
                return Err(TensorError::Format(format!(
                    "online.2048.diff_frames: {other:?}, not one positive count"
                )));
            }
        };
        let fft = RealFftPlanner::<f64>::new().plan_fft_forward(FRAME_SIZE);
        Ok(OnlineFrontEnd {
            window: hanning(FRAME_SIZE),
            filterbank,
            bands,
            diff_frames: diff_frames as usize,
            fft_in: fft.make_input_vec(),
            fft_out: fft.make_output_vec(),
            fft_scratch: fft.make_scratch_vec(),
            fft,
            ring: vec![0.0; FRAME_SIZE],
            received: 0,
            next_frame: 0,
            history: VecDeque::with_capacity(diff_frames as usize + 1),
            acc: vec![0.0; bands],
            spec: vec![0.0; bands],
            features: vec![0.0; 2 * bands],
        })
    }

    /// Features per frame: the log spectrum's bands, then their positive differences.
    pub fn width(&self) -> usize {
        2 * self.bands
    }

    /// Frames emitted so far.
    pub fn frames(&self) -> usize {
        self.next_frame
    }

    /// Sample `i` of the frame ending at absolute sample `end` (zero before the signal).
    fn frame_sample(&self, end: usize, i: usize) -> f32 {
        // Frame position i holds absolute sample end - (FRAME_SIZE - 1) + i.
        match (end + i + 1).checked_sub(FRAME_SIZE) {
            Some(abs) => self.ring[abs % FRAME_SIZE],
            None => 0.0,
        }
    }

    /// Feeds samples (mono, 44.1 kHz) and calls `emit(frame, features)` for each frame they
    /// complete, in order.
    pub fn push(&mut self, samples: &[f32], mut emit: impl FnMut(usize, &[f32])) {
        for &s in samples {
            self.ring[self.received % FRAME_SIZE] = s;
            self.received += 1;
            // Frame n ends at sample n * HOP: complete once that sample is in.
            while self.next_frame * HOP < self.received {
                self.compute_frame();
                emit(self.next_frame, &self.features);
                self.next_frame += 1;
            }
        }
    }

    /// The raw (unwindowed) samples of the last frame emitted, as madmom frames it; only while
    /// no sample has arrived since it completed (for tests).
    #[doc(hidden)]
    pub fn last_frame_samples(&self) -> Option<Vec<f32>> {
        let n = self.next_frame.checked_sub(1)?;
        let end = n * HOP;
        (end + 1 == self.received)
            .then(|| (0..FRAME_SIZE).map(|i| self.frame_sample(end, i)).collect())
    }

    fn compute_frame(&mut self) {
        let end = self.next_frame * HOP;
        for i in 0..FRAME_SIZE {
            // (numpy: float32 frame times float64 window, in float64.)
            self.fft_in[i] = f64::from(self.frame_sample(end, i)) * self.window[i];
        }
        self.fft
            .process_with_scratch(&mut self.fft_in, &mut self.fft_out, &mut self.fft_scratch)
            .expect("FFT buffers are sized by the plan");
        // The filtered spectrum: complex64 bins, float32 magnitudes, weighted sums (accumulated
        // in float64 and rounded once), then log10(x + 1).
        let acc = &mut self.acc;
        acc.fill(0.0);
        for (bin, row) in self.filterbank.iter().enumerate() {
            if row.is_empty() {
                continue;
            }
            let c = self.fft_out[bin];
            let mag = (c.re as f32).hypot(c.im as f32);
            for &(band, w) in row {
                acc[band] += f64::from(mag * w);
            }
        }
        for (s, a) in self.spec.iter_mut().zip(acc.iter()) {
            *s = (*a as f32 * 1.0 + 1.0).log10();
        }
        let bands = self.bands;
        self.features[..bands].copy_from_slice(&self.spec);
        if self.history.len() == self.diff_frames {
            let past = &self.history[0];
            for ((d, now), before) in self.features[bands..].iter_mut().zip(&self.spec).zip(past) {
                *d = (now - before).max(0.0);
            }
        } else {
            self.features[bands..].fill(0.0);
        }
        if self.history.len() == self.diff_frames {
            let mut oldest = self.history.pop_front().expect("history is full");
            oldest.copy_from_slice(&self.spec);
            self.history.push_back(oldest);
        } else {
            self.history.push_back(self.spec.clone());
        }
    }
}
