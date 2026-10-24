//! madmom's beat-tracking HMM (state space, transitions and observation model
//! exported as tables), the forward step, Viterbi, and the stream's commit rule.

use crate::models::Tensors;

pub const FPS: f64 = 100.0;

pub struct Hmm {
    indptr: Vec<usize>,
    prev: Vec<usize>,
    prob: Vec<f64>,
    log_prob: Vec<f64>,
    om: Vec<u8>,
    pub positions: Vec<f64>,
    pub intervals: Vec<u32>,
    lambda: f64,
}

impl Hmm {
    pub fn load(t: &Tensors, prefix: &str) -> Hmm {
        Hmm {
            indptr: t
                .i32(&format!("{prefix}.indptr"))
                .iter()
                .map(|&x| x as usize)
                .collect(),
            prev: t
                .i32(&format!("{prefix}.prev"))
                .iter()
                .map(|&x| x as usize)
                .collect(),
            prob: t.f64(&format!("{prefix}.prob")),
            log_prob: t.f64(&format!("{prefix}.log_prob")),
            om: t
                .i32(&format!("{prefix}.om_pointers"))
                .iter()
                .map(|&x| x as u8)
                .collect(),
            positions: t
                .f32(&format!("{prefix}.positions"))
                .iter()
                .map(|&x| x as f64)
                .collect(),
            intervals: t
                .i32(&format!("{prefix}.intervals"))
                .iter()
                .map(|&x| x as u32)
                .collect(),
            lambda: t.f32(&format!("{prefix}.observation_lambda"))[0] as f64,
        }
    }

    pub fn states(&self) -> usize {
        self.positions.len()
    }

    fn densities(&self, a: f32) -> [f64; 2] {
        let a = a as f64;
        [(1.0 - a) / (self.lambda - 1.0), a]
    }

    /// Viterbi over a whole activation sequence; the state path (empty if impossible).
    pub fn viterbi(&self, act: &[f32]) -> Vec<usize> {
        let n = self.states();
        let mut prev_v = vec![-(n as f64).ln(); n];
        let mut cur = vec![0.0f64; n];
        let mut bt = vec![0u32; act.len() * n];
        for (f, &a) in act.iter().enumerate() {
            let d = self.densities(a);
            let logd = [d[0].ln(), d[1].ln()];
            let row = &mut bt[f * n..(f + 1) * n];
            for s in 0..n {
                let obs = logd[self.om[s] as usize];
                let mut best = f64::NEG_INFINITY;
                let mut arg = 0u32;
                for k in self.indptr[s]..self.indptr[s + 1] {
                    let p = prev_v[self.prev[k]] + self.log_prob[k] + obs;
                    if p > best {
                        best = p;
                        arg = self.prev[k] as u32;
                    }
                }
                cur[s] = best;
                row[s] = arg;
            }
            std::mem::swap(&mut prev_v, &mut cur);
        }
        let mut state = argmax(&prev_v);
        if prev_v[state].is_infinite() {
            return Vec::new();
        }
        let mut path = vec![0usize; act.len()];
        for f in (0..act.len()).rev() {
            path[f] = state;
            state = bt[f * n + state] as usize;
        }
        path
    }

    /// madmom's offline beats (`correct=True`): the activation peak of each beat range.
    pub fn offline_beats(&self, act: &[f32]) -> Vec<f64> {
        let path = self.viterbi(act);
        if path.is_empty() {
            return Vec::new();
        }
        let range: Vec<bool> = path.iter().map(|&s| self.om[s] == 1).collect();
        let mut idx: Vec<usize> = Vec::new();
        if range[0] {
            idx.push(0);
        }
        for i in 1..range.len() {
            if range[i] != range[i - 1] {
                idx.push(i);
            }
        }
        if range[range.len() - 1] {
            idx.push(range.len());
        }
        idx.chunks_exact(2)
            .map(|lr| {
                let peak = lr[0] + argmax_f32(&act[lr[0]..lr[1]]);
                peak as f64 / FPS
            })
            .collect()
    }
}

fn argmax(v: &[f64]) -> usize {
    let mut best = 0;
    for (i, &x) in v.iter().enumerate() {
        if x > v[best] {
            best = i;
        }
    }
    best
}

fn argmax_f32(v: &[f32]) -> usize {
    let mut best = 0;
    for (i, &x) in v.iter().enumerate() {
        if x > v[best] {
            best = i;
        }
    }
    best
}

/// The forward algorithm, one frame at a time.
pub struct Forward {
    prev: Vec<f64>,
    cur: Vec<f64>,
}

impl Forward {
    pub fn new(hmm: &Hmm) -> Self {
        let n = hmm.states();
        Forward {
            prev: vec![1.0 / n as f64; n],
            cur: vec![0.0; n],
        }
    }

    /// Advance one activation; returns the most likely state.
    pub fn step(&mut self, h: &Hmm, a: f32) -> usize {
        let d = h.densities(a);
        let mut sum = 0.0;
        for s in 0..h.states() {
            let mut v = 0.0;
            for k in h.indptr[s]..h.indptr[s + 1] {
                v += self.prev[h.prev[k]] * h.prob[k];
            }
            v *= d[h.om[s] as usize];
            self.cur[s] = v;
            sum += v;
        }
        let norm = 1.0 / sum;
        for v in &mut self.cur {
            *v *= norm;
        }
        std::mem::swap(&mut self.prev, &mut self.cur);
        argmax(&self.prev)
    }
}

#[derive(Clone, Copy, Debug, serde::Serialize)]
pub struct Beat {
    /// Model time of the beat (seconds of analysed audio, with biasS).
    pub t: f64,
    /// Model time at which it was committed.
    pub known_at: f64,
    pub period: f64,
    pub evidence: f64,
}

/// The stream's parameters the spike uses (stream.py `Params` defaults).
pub struct Params {
    pub period_window_s: f64,
    pub period_band: f64,
    pub commit_periods: f64,
    pub commit_extra_s: f64,
    pub commit_min_lead_s: f64,
    pub bias_s: f64,
    pub evidence_beats: usize,
    pub evidence_half_s: f64,
}

pub const PARAMS: Params = Params {
    period_window_s: 2.0,
    period_band: 0.1,
    commit_periods: 1.0,
    commit_extra_s: 0.35,
    commit_min_lead_s: 0.1,
    bias_s: -0.013,
    evidence_beats: 4,
    evidence_half_s: 0.03,
};

/// The online tracker: forward step, period smoothing, commits.
pub struct Tracker {
    fwd: Forward,
    hmm: Hmm,
    periods: std::collections::VecDeque<f64>,
    act: Vec<f32>,
    /// Last committed beat time.
    c: f64,
    pub frame: usize,
}

impl Tracker {
    pub fn new(hmm: Hmm) -> Self {
        Tracker {
            fwd: Forward::new(&hmm),
            hmm,
            periods: Default::default(),
            act: Vec::new(),
            c: f64::NEG_INFINITY,
            frame: 0,
        }
    }

    /// One activation frame; pushes any beats it commits. Returns the most likely state.
    pub fn step(&mut self, a: f32, out: &mut Vec<Beat>) -> usize {
        let p = &PARAMS;
        let n = self.frame;
        self.frame += 1;
        self.act.push(a);
        let s = self.fwd.step(&self.hmm, a);
        let itv = self.hmm.intervals[s] as f64;
        let since = self.hmm.positions[s] * itv / FPS;
        let raw = itv / FPS;
        let w = (p.period_window_s * FPS).round() as usize;
        self.periods.push_back(raw);
        while self.periods.len() > w {
            self.periods.pop_front();
        }
        let (sum, cnt) = self
            .periods
            .iter()
            .filter(|&&q| (q - raw).abs() <= p.period_band * raw)
            .fold((0.0, 0usize), |(s, c), &q| (s + q, c + 1));
        let per = sum / cnt as f64;
        let t = n as f64 / FPS;
        let last_model = t - since;
        let last = last_model + p.bias_s;
        let horizon = p.commit_periods * per + p.commit_extra_s;
        // Evidence as the stream's ticks compute it (here per frame).
        let evidence = self.evidence(last_model, per, t);
        loop {
            let k =
                (((self.c + 0.5 * per).max(t + p.commit_min_lead_s) - last) / per - 1e-9).ceil();
            let cand = last + k * per;
            let due = self.c - 1.0 / FPS < t && t <= self.c + 1e-9;
            if cand - t > horizon + 1e-9 && !due {
                break;
            }
            out.push(Beat {
                t: cand,
                known_at: t,
                period: per,
                evidence,
            });
            self.c = cand;
        }
        s
    }

    fn evidence(&self, last: f64, period: f64, t: f64) -> f64 {
        let p = &PARAMS;
        let mut vals = Vec::new();
        for j in 0..=p.evidence_beats {
            let b = last - j as f64 * period;
            if b < 0.0 || vals.len() == p.evidence_beats {
                break;
            }
            if b + p.evidence_half_s > t {
                continue;
            }
            let lo = ((b - p.evidence_half_s) * FPS).round().max(0.0) as usize;
            let hi = (((b + p.evidence_half_s) * FPS).round() as usize).min(self.act.len() - 1);
            if hi >= lo {
                vals.push(self.act[lo..=hi].iter().cloned().fold(f32::MIN, f32::max) as f64);
            }
        }
        if vals.is_empty() {
            0.0
        } else {
            vals.iter().sum::<f64>() / vals.len() as f64
        }
    }
}
