//! madmom's LSTM layers (peephole gates, tanh cell) and the ensembles built on them.
//!
//! Gates: `act(x W + b + c p + h R)`; the input and forget gates peep at the
//! previous cell state, the output gate at the new one.

use crate::models::Tensors;

pub struct Lstm {
    pub inputs: usize,
    pub hidden: usize,
    /// [input][4 * hidden], gates in order input, forget, cell, output.
    w: Vec<f32>,
    /// [hidden][4 * hidden].
    r: Vec<f32>,
    b: Vec<f32>,
    /// Peepholes for the input, forget and output gates.
    p_i: Vec<f32>,
    p_f: Vec<f32>,
    p_o: Vec<f32>,
}

const GATES: [&str; 4] = ["input_gate", "forget_gate", "cell", "output_gate"];

impl Lstm {
    pub fn load(t: &Tensors, prefix: &str) -> Lstm {
        let shape = t.shape(&format!("{prefix}.input_gate.w")).to_vec();
        let (inputs, hidden) = (shape[0], shape[1]);
        let mut w = vec![0.0; inputs * 4 * hidden];
        let mut r = vec![0.0; hidden * 4 * hidden];
        let mut b = vec![0.0; 4 * hidden];
        for (g, name) in GATES.iter().enumerate() {
            let gw = t.f32(&format!("{prefix}.{name}.w"));
            let gr = t.f32(&format!("{prefix}.{name}.r"));
            let gb = t.f32(&format!("{prefix}.{name}.b"));
            for i in 0..inputs {
                for h in 0..hidden {
                    w[i * 4 * hidden + g * hidden + h] = gw[i * hidden + h];
                }
            }
            for i in 0..hidden {
                for h in 0..hidden {
                    r[i * 4 * hidden + g * hidden + h] = gr[i * hidden + h];
                }
            }
            b[g * hidden..(g + 1) * hidden].copy_from_slice(&gb);
        }
        Lstm {
            inputs,
            hidden,
            w,
            r,
            b,
            p_i: t.f32(&format!("{prefix}.input_gate.p")),
            p_f: t.f32(&format!("{prefix}.forget_gate.p")),
            p_o: t.f32(&format!("{prefix}.output_gate.p")),
        }
    }

    /// x W + b for every frame of `xs` (`frames x inputs`), into `frames x 4H`.
    pub fn project(&self, xs: &[f32], frames: usize) -> Vec<f32> {
        let g4 = 4 * self.hidden;
        let mut out = vec![0.0f32; frames * g4];
        unsafe {
            matrixmultiply::sgemm(
                frames,
                self.inputs,
                g4,
                1.0,
                xs.as_ptr(),
                self.inputs as isize,
                1,
                self.w.as_ptr(),
                g4 as isize,
                1,
                0.0,
                out.as_mut_ptr(),
                g4 as isize,
                1,
            );
        }
        for row in out.chunks_exact_mut(g4) {
            for (o, b) in row.iter_mut().zip(&self.b) {
                *o += b;
            }
        }
        out
    }

    /// One step from a projected input row (`x W + b`, 4H wide).
    pub fn step(&self, proj: &[f32], state: &mut LstmState) {
        let hd = self.hidden;
        let g4 = 4 * hd;
        let z = &mut state.z;
        z.copy_from_slice(proj);
        for (i, &hp) in state.h.iter().enumerate() {
            if hp == 0.0 {
                continue;
            }
            let row = &self.r[i * g4..(i + 1) * g4];
            for (zz, rr) in z.iter_mut().zip(row) {
                *zz += hp * rr;
            }
        }
        for k in 0..hd {
            let c_prev = state.c[k];
            let ig = sigmoid(z[k] + c_prev * self.p_i[k]);
            let fg = sigmoid(z[hd + k] + c_prev * self.p_f[k]);
            let cell = z[2 * hd + k].tanh();
            let c = cell * ig + c_prev * fg;
            let og = sigmoid(z[3 * hd + k] + c * self.p_o[k]);
            state.c[k] = c;
            state.h[k] = c.tanh() * og;
        }
    }

    /// Run over a whole sequence (`frames x inputs`), forwards or backwards.
    pub fn run(&self, xs: &[f32], frames: usize, reverse: bool) -> Vec<f32> {
        let proj = self.project(xs, frames);
        let mut st = LstmState::new(self.hidden);
        let hd = self.hidden;
        let mut out = vec![0.0f32; frames * hd];
        let g4 = 4 * hd;
        for n in 0..frames {
            let f = if reverse { frames - 1 - n } else { n };
            self.step(&proj[f * g4..(f + 1) * g4], &mut st);
            out[f * hd..(f + 1) * hd].copy_from_slice(&st.h);
        }
        out
    }
}

pub struct LstmState {
    pub h: Vec<f32>,
    pub c: Vec<f32>,
    z: Vec<f32>,
}

impl LstmState {
    pub fn new(hidden: usize) -> Self {
        LstmState {
            h: vec![0.0; hidden],
            c: vec![0.0; hidden],
            z: vec![0.0; 4 * hidden],
        }
    }
}

#[inline]
fn sigmoid(x: f32) -> f32 {
    1.0 / (1.0 + (-x).exp())
}

pub struct Dense {
    w: Vec<f32>,
    b: f32,
}

impl Dense {
    fn load(t: &Tensors, prefix: &str) -> Dense {
        Dense {
            w: t.f32(&format!("{prefix}.ff.w")),
            b: t.f32(&format!("{prefix}.ff.b"))[0],
        }
    }

    fn apply(&self, h: &[f32]) -> f32 {
        sigmoid(h.iter().zip(&self.w).map(|(a, b)| a * b).sum::<f32>() + self.b)
    }
}

/// The online ensemble: 8 nets of 3 unidirectional LSTM layers, averaged.
pub struct OnlineEnsemble {
    nets: Vec<(Vec<Lstm>, Dense)>,
    states: Vec<Vec<LstmState>>,
    proj: Vec<f32>,
}

impl OnlineEnsemble {
    pub fn load(t: &Tensors) -> Self {
        let mut nets = Vec::new();
        let mut i = 0;
        while t.has(&format!("online.{i}.0.input_gate.w")) {
            let mut layers = Vec::new();
            let mut j = 0;
            while t.has(&format!("online.{i}.{j}.input_gate.w")) {
                layers.push(Lstm::load(t, &format!("online.{i}.{j}")));
                j += 1;
            }
            nets.push((layers, Dense::load(t, &format!("online.{i}.{j}"))));
            i += 1;
        }
        let states = nets
            .iter()
            .map(|(ls, _)| ls.iter().map(|l| LstmState::new(l.hidden)).collect())
            .collect();
        OnlineEnsemble {
            nets,
            states,
            proj: Vec::new(),
        }
    }

    /// One activation from one feature frame.
    pub fn step(&mut self, x: &[f32]) -> f32 {
        let mut sum = 0.0;
        for ((layers, dense), states) in self.nets.iter().zip(&mut self.states) {
            for (k, layer) in layers.iter().enumerate() {
                let g4 = 4 * layer.hidden;
                self.proj.resize(g4, 0.0);
                let input: &[f32] = if k == 0 { x } else { &states[k - 1].h };
                // x W + b
                self.proj.copy_from_slice(&layer.b);
                for (i, &xi) in input.iter().enumerate() {
                    if xi == 0.0 {
                        continue;
                    }
                    let row = &layer.w[i * g4..(i + 1) * g4];
                    for (p, w) in self.proj.iter_mut().zip(row) {
                        *p += xi * w;
                    }
                }
                let (before, rest) = states.split_at_mut(k);
                let _ = before;
                layer.step(&self.proj, &mut rest[0]);
            }
            sum += dense.apply(&states[layers.len() - 1].h);
        }
        sum / self.nets.len() as f32
    }
}

/// The offline ensemble: 8 nets of 3 bidirectional LSTM layers, averaged.
pub struct OfflineEnsemble {
    nets: Vec<(Vec<(Lstm, Lstm)>, Dense)>,
}

impl OfflineEnsemble {
    pub fn load(t: &Tensors) -> Self {
        let mut nets = Vec::new();
        let mut i = 0;
        while t.has(&format!("offline.{i}.0.fwd.input_gate.w")) {
            let mut layers = Vec::new();
            let mut j = 0;
            while t.has(&format!("offline.{i}.{j}.fwd.input_gate.w")) {
                layers.push((
                    Lstm::load(t, &format!("offline.{i}.{j}.fwd")),
                    Lstm::load(t, &format!("offline.{i}.{j}.bwd")),
                ));
                j += 1;
            }
            nets.push((layers, Dense::load(t, &format!("offline.{i}.{j}"))));
            i += 1;
        }
        OfflineEnsemble { nets }
    }

    pub fn run(&self, xs: &[f32], frames: usize) -> Vec<f32> {
        let mut acc = vec![0.0f32; frames];
        for (layers, dense) in &self.nets {
            let mut cur = xs.to_vec();
            for (f, b) in layers {
                let fo = f.run(&cur, frames, false);
                let bo = b.run(&cur, frames, true);
                let hd = f.hidden;
                let mut next = vec![0.0f32; frames * 2 * hd];
                for n in 0..frames {
                    next[n * 2 * hd..n * 2 * hd + hd].copy_from_slice(&fo[n * hd..(n + 1) * hd]);
                    next[n * 2 * hd + hd..(n + 1) * 2 * hd]
                        .copy_from_slice(&bo[n * hd..(n + 1) * hd]);
                }
                cur = next;
            }
            let w = 2 * layers.last().unwrap().0.hidden;
            for n in 0..frames {
                acc[n] += dense.apply(&cur[n * w..(n + 1) * w]);
            }
        }
        let k = self.nets.len() as f32;
        acc.iter_mut().for_each(|a| *a /= k);
        acc
    }
}
