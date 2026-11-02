//! madmom's online beat ensemble (`BEATS_LSTM`, exported as `online.<net>.<layer>` in
//! `madmom.bin`), one frame at a time with bounded memory: eight networks of three LSTM layers
//! (25 units) and a sigmoid output unit, averaged.
//!
//! The layers follow madmom's `ml/nn/layers.py` in float32, in its order of operations:
//! - a gate is `act((x W + b) + c p + h R)`, where the input and forget gates peep at the
//!   previous cell state, the output gate at the new one, and the cell (`tanh`) has no peephole;
//! - the new cell state is `cell * input + c * forget`, the output `tanh(c) * output`;
//! - the output layer is `sigmoid(x W + b)`, and the ensemble `sum / 8` in float32, summed in
//!   network order (`average_predictions`).
//!
//! Dot products accumulate in float64 and round once, where numpy's float32 BLAS accumulates in
//! float32 in its own order: parity with Python is measured (A1's stage parity test), not exact.

use crate::tensors::{TensorError, Tensors};

/// The most units a layer may have (the dot products' fixed scratch).
const MAX_UNITS: usize = 64;

/// A float32 tensor that must hold exactly `len` values.
pub(crate) fn sized(t: &Tensors, name: &str, len: usize) -> Result<Vec<f32>, TensorError> {
    let v = t.f32(name)?;
    if v.len() == len {
        Ok(v)
    } else {
        Err(TensorError::Format(format!(
            "{name}: {} values, not {len}",
            v.len()
        )))
    }
}

#[inline]
pub(crate) fn sigmoid(x: f32) -> f32 {
    1.0 / (1.0 + (-x).exp())
}

/// `x W` for a row vector `x` and a row-major `inputs x outputs` matrix, plus `b`, into `out`.
pub(crate) fn affine(x: &[f32], w: &[f32], b: &[f32], out: &mut [f32]) {
    let outputs = out.len();
    debug_assert_eq!(w.len(), x.len() * outputs);
    let mut acc = [0.0f64; MAX_UNITS];
    let acc = &mut acc[..outputs];
    for (i, &xi) in x.iter().enumerate() {
        if xi == 0.0 {
            continue;
        }
        let row = &w[i * outputs..(i + 1) * outputs];
        for (a, &wv) in acc.iter_mut().zip(row) {
            *a += f64::from(xi * wv);
        }
    }
    for ((o, a), bv) in out.iter_mut().zip(acc.iter()).zip(b) {
        *o = *a as f32 + bv;
    }
}

/// `h R` for a row vector `h` and a row-major square matrix, added to `out`.
fn add_recurrent(h: &[f32], r: &[f32], out: &mut [f32]) {
    let n = out.len();
    let mut acc = [0.0f64; MAX_UNITS];
    let acc = &mut acc[..n];
    for (i, &hi) in h.iter().enumerate() {
        if hi == 0.0 {
            continue;
        }
        for (a, &rv) in acc.iter_mut().zip(&r[i * n..(i + 1) * n]) {
            *a += f64::from(hi * rv);
        }
    }
    for (o, a) in out.iter_mut().zip(acc.iter()) {
        *o += *a as f32;
    }
}

struct Gate {
    w: Vec<f32>,
    r: Vec<f32>,
    b: Vec<f32>,
    /// Peephole weights (none for the cell).
    p: Option<Vec<f32>>,
}

impl Gate {
    fn load(
        t: &Tensors,
        prefix: &str,
        inputs: usize,
        hidden: usize,
        peephole: bool,
    ) -> Result<Gate, TensorError> {
        let gate = Gate {
            w: sized(t, &format!("{prefix}.w"), inputs * hidden)?,
            r: sized(t, &format!("{prefix}.r"), hidden * hidden)?,
            b: sized(t, &format!("{prefix}.b"), hidden)?,
            p: if peephole {
                Some(sized(t, &format!("{prefix}.p"), hidden)?)
            } else {
                None
            },
        };
        Ok(gate)
    }

    /// The gate's pre-activation `(x W + b) + c p + h R` into `out`.
    fn pre(&self, x: &[f32], h: &[f32], c: &[f32], out: &mut [f32]) {
        affine(x, &self.w, &self.b, out);
        if let Some(p) = &self.p {
            for ((o, cv), pv) in out.iter_mut().zip(c).zip(p) {
                *o += cv * pv;
            }
        }
        add_recurrent(h, &self.r, out);
    }
}

/// One LSTM layer with peephole gates.
pub(crate) struct Lstm {
    pub(crate) hidden: usize,
    input: Gate,
    forget: Gate,
    cell: Gate,
    output: Gate,
}

/// An LSTM layer's state: the previous output and cell state, and scratch.
pub(crate) struct LstmState {
    pub(crate) h: Vec<f32>,
    c: Vec<f32>,
    ig: Vec<f32>,
    fg: Vec<f32>,
    cg: Vec<f32>,
    og: Vec<f32>,
}

impl LstmState {
    pub(crate) fn new(hidden: usize) -> Self {
        let z = || vec![0.0; hidden];
        LstmState {
            h: z(),
            c: z(),
            ig: z(),
            fg: z(),
            cg: z(),
            og: z(),
        }
    }
}

impl Lstm {
    /// The layer at `prefix`, taking `inputs` values per frame (checked against every tensor).
    pub(crate) fn load(t: &Tensors, prefix: &str, inputs: usize) -> Result<Lstm, TensorError> {
        let hidden = match t.shape(&format!("{prefix}.cell.b"))? {
            &[h] if (1..=MAX_UNITS).contains(&h) => h,
            other => {
                return Err(TensorError::Format(format!(
                    "{prefix}.cell.b: shape {other:?}, not 1 to {MAX_UNITS} units"
                )));
            }
        };
        Ok(Lstm {
            hidden,
            input: Gate::load(t, &format!("{prefix}.input_gate"), inputs, hidden, true)?,
            forget: Gate::load(t, &format!("{prefix}.forget_gate"), inputs, hidden, true)?,
            cell: Gate::load(t, &format!("{prefix}.cell"), inputs, hidden, false)?,
            output: Gate::load(t, &format!("{prefix}.output_gate"), inputs, hidden, true)?,
        })
    }

    /// One time step: `st.h` becomes this frame's output.
    pub(crate) fn step(&self, x: &[f32], st: &mut LstmState) {
        let LstmState {
            h,
            c,
            ig,
            fg,
            cg,
            og,
        } = st;
        self.input.pre(x, h, c, ig);
        self.forget.pre(x, h, c, fg);
        self.cell.pre(x, h, c, cg);
        for k in 0..self.hidden {
            ig[k] = sigmoid(ig[k]);
            fg[k] = sigmoid(fg[k]);
            c[k] = cg[k].tanh() * ig[k] + c[k] * fg[k];
        }
        // (The output gate peeps at the new cell state.)
        self.output.pre(x, h, c, og);
        for k in 0..self.hidden {
            h[k] = c[k].tanh() * sigmoid(og[k]);
        }
    }
}

/// One network: three LSTM layers and a sigmoid output unit.
struct Network {
    layers: Vec<Lstm>,
    out_w: Vec<f32>,
    out_b: Vec<f32>,
}

/// The online ensemble with its state: feed feature frames, get activations.
pub struct OnlineEnsemble {
    nets: Vec<Network>,
    states: Vec<Vec<LstmState>>,
    width: usize,
    out: [f32; 1],
}

impl OnlineEnsemble {
    /// Every network `online.<n>` in `madmom.bin`.
    pub fn new(models: &Tensors) -> Result<Self, TensorError> {
        // Every network takes the first layer's input width (the front end's features).
        let width = match models.shape("online.0.0.input_gate.w")? {
            &[inputs, _] => inputs,
            other => {
                return Err(TensorError::Format(format!(
                    "online.0.0.input_gate.w: shape {other:?}"
                )));
            }
        };
        let mut nets = Vec::new();
        while models
            .shape(&format!("online.{}.0.cell.b", nets.len()))
            .is_ok()
        {
            let n = nets.len();
            let mut layers: Vec<Lstm> = Vec::new();
            while models
                .shape(&format!("online.{n}.{}.cell.b", layers.len()))
                .is_ok()
            {
                let inputs = layers.last().map_or(width, |l| l.hidden);
                layers.push(Lstm::load(
                    models,
                    &format!("online.{n}.{}", layers.len()),
                    inputs,
                )?);
            }
            let hidden = layers.last().map_or(width, |l| l.hidden);
            let ff = format!("online.{n}.{}.ff", layers.len());
            nets.push(Network {
                out_w: sized(models, &format!("{ff}.w"), hidden)?,
                out_b: sized(models, &format!("{ff}.b"), 1)?,
                layers,
            });
        }
        if nets.is_empty() {
            return Err(TensorError::Missing("online.0.0.cell.b".into()));
        }
        let states = nets
            .iter()
            .map(|n| n.layers.iter().map(|l| LstmState::new(l.hidden)).collect())
            .collect();
        Ok(OnlineEnsemble {
            nets,
            states,
            width,
            out: [0.0],
        })
    }

    /// Features per frame the ensemble takes (the front end's `width()`).
    pub fn input_width(&self) -> usize {
        self.width
    }

    /// The number of networks averaged.
    pub fn networks(&self) -> usize {
        self.nets.len()
    }

    /// The beat activation for the next feature frame.
    pub fn step(&mut self, features: &[f32]) -> f32 {
        assert_eq!(features.len(), self.width, "features per frame");
        let mut sum = 0.0f32;
        for (net, states) in self.nets.iter().zip(&mut self.states) {
            let mut x: &[f32] = features;
            for (layer, st) in net.layers.iter().zip(states.iter_mut()) {
                layer.step(x, st);
                x = &st.h;
            }
            affine(x, &net.out_w, &net.out_b, &mut self.out);
            sum += sigmoid(self.out[0]);
        }
        sum / self.nets.len() as f32
    }
}
