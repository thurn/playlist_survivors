//! madmom's online beat-tracking DBN (`DBNBeatTrackingProcessor(online=True)` with the stream's
//! tempo range), one forward step per activation frame: the most likely state gives the believed
//! beat's age and period (`stream.py`'s `forward_beliefs`).
//!
//! The state space and transition model are exported in `madmom.bin` (`dbn_online.*`: CSR
//! pointers, previous states and float64 probabilities; each state's position and interval; the
//! observation lambda). The step follows madmom's `HiddenMarkovModel.forward` exactly:
//! - from the uniform initial distribution, each state's forward value is the sum, in pointer
//!   order, of the previous values times their transition probabilities, times the observation
//!   density, all in float64; the values are then scaled by `1 / sum`;
//! - the densities are madmom's `exp(log_densities)`: `log((1 - a) / (lambda - 1))` for the
//!   non-beat states and `log(a)` for the beat states (position under `1 / lambda`), computed in
//!   float32 as numpy does for float32 activations, then exponentiated in float64;
//! - the most likely state is the first maximum (`np.argmax`).
//!
//! Bit-equality with Python also rests on the platform's float32 `log` and float64 `exp`: on macOS
//! arm64 numpy and Rust both call the system's, and the golden data is generated there (numpy's
//! own vectorised versions on x86 with AVX512 can differ by an ulp).

use crate::tensors::{TensorError, Tensors};

/// madmom's `RNNBeatTrackingObservationModel.log_densities` for one float32 activation: the
/// non-beat states' `log((1 - a) / (lambda - 1))` and the beat states' `log(a)`, in float32 as numpy
/// computes them for float32 activations, then stored as float64 (the forward step takes their
/// `exp` in float64, the Viterbi decode uses them as they are).
pub(crate) fn log_densities(activation: f32, lambda: f32) -> (f64, f64) {
    (
        f64::from(((1.0f32 - activation) / (lambda - 1.0)).ln()),
        f64::from(activation.ln()),
    )
}

/// The online DBN with its forward variables.
pub struct OnlineDbn {
    /// CSR: state `s`'s transitions are `indptr[s]..indptr[s + 1]`.
    indptr: Vec<usize>,
    prev: Vec<usize>,
    prob: Vec<f64>,
    /// 1 for the beat states (observing `a`), 0 for the others.
    beat_state: Vec<bool>,
    positions: Vec<f64>,
    intervals: Vec<f64>,
    lambda: f32,
    fps: f64,
    fwd_prev: Vec<f64>,
    fwd: Vec<f64>,
}

/// The tracker's belief at one frame (before smoothing and bias).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct State {
    /// The most likely state's index.
    pub state: usize,
    /// Seconds since the believed last beat (`position * interval / fps`).
    pub since: f64,
    /// The believed period in seconds (`interval / fps`).
    pub period: f64,
}

fn index(t: &Tensors, name: &str) -> Result<Vec<usize>, TensorError> {
    t.i32(name)?
        .into_iter()
        .map(|v| {
            usize::try_from(v)
                .map_err(|_| TensorError::Format(format!("{name}: negative index {v}")))
        })
        .collect()
}

impl OnlineDbn {
    /// The DBN at `prefix` (`dbn_online`) in `madmom.bin`, stepping `fps` frames a second.
    pub fn new(models: &Tensors, prefix: &str, fps: f64) -> Result<Self, TensorError> {
        let indptr = index(models, &format!("{prefix}.indptr"))?;
        let prev = index(models, &format!("{prefix}.prev"))?;
        let prob = models.f64(&format!("{prefix}.prob"))?;
        let om = index(models, &format!("{prefix}.om_pointers"))?;
        let positions = models.f64(&format!("{prefix}.positions"))?;
        let intervals: Vec<f64> = models
            .i32(&format!("{prefix}.intervals"))?
            .into_iter()
            .map(f64::from)
            .collect();
        let lambda = match models
            .f64(&format!("{prefix}.observation_lambda"))?
            .as_slice()
        {
            &[l] if l > 1.0 => l as f32,
            other => {
                return Err(TensorError::Format(format!(
                    "{prefix}.observation_lambda: {other:?}"
                )));
            }
        };
        let states = positions.len();
        let fmt = |m: &str| TensorError::Format(format!("{prefix}: {m}"));
        if states == 0
            || indptr.len() != states + 1
            || om.len() != states
            || intervals.len() != states
        {
            return Err(fmt("the state arrays disagree in length"));
        }
        if prev.len() != prob.len()
            || indptr[states] != prev.len()
            || indptr.windows(2).any(|w| w[0] > w[1])
        {
            return Err(fmt("malformed CSR transitions"));
        }
        if prev.iter().any(|&p| p >= states) || om.iter().any(|&o| o > 1) {
            return Err(fmt("an index is out of range"));
        }
        let initial = 1.0 / states as f64;
        Ok(OnlineDbn {
            indptr,
            prev,
            prob,
            beat_state: om.into_iter().map(|o| o == 1).collect(),
            positions,
            intervals,
            lambda,
            fps,
            fwd_prev: vec![initial; states],
            fwd: vec![0.0; states],
        })
    }

    /// The slowest believed period (s): the largest interval. The smoothed period, a mean of
    /// believed ones, never exceeds it.
    pub fn max_period(&self) -> f64 {
        self.intervals.iter().copied().fold(0.0, f64::max) / self.fps
    }

    /// The number of states.
    pub fn states(&self) -> usize {
        self.positions.len()
    }

    /// One forward step for an activation frame; returns the most likely state.
    pub fn step(&mut self, activation: f32) -> State {
        // (numpy: float32 activations, float32 log, cast to float64, exp in float64.)
        let (non_beat, beat) = log_densities(activation, self.lambda);
        let (non_beat, beat) = (non_beat.exp(), beat.exp());
        let mut sum = 0.0f64;
        for s in 0..self.fwd.len() {
            let mut v = 0.0f64;
            for k in self.indptr[s]..self.indptr[s + 1] {
                v += self.fwd_prev[self.prev[k]] * self.prob[k];
            }
            v *= if self.beat_state[s] { beat } else { non_beat };
            self.fwd[s] = v;
            sum += v;
        }
        let norm = 1.0 / sum;
        let mut best = 0;
        for s in 0..self.fwd.len() {
            self.fwd[s] *= norm;
            self.fwd_prev[s] = self.fwd[s];
            if self.fwd[s] > self.fwd[best] {
                best = s;
            }
        }
        let interval = self.intervals[best];
        State {
            state: best,
            since: self.positions[best] * interval / self.fps,
            period: interval / self.fps,
        }
    }
}
