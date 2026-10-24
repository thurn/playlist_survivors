//! The spike's live analyzer (online front end, LSTM ensemble, tracker) and the
//! hindsight decoder (offline front end, BiLSTM ensemble, Viterbi).

use crate::dbn::{Beat, Hmm, Tracker};
use crate::frontend::{OfflineFrontEnd, OnlineFrontEnd};
use crate::lstm::{OfflineEnsemble, OnlineEnsemble};
use crate::models::Tensors;

pub struct Analyzer {
    fe: OnlineFrontEnd,
    nn: OnlineEnsemble,
    pub tracker: Tracker,
    /// Every activation so far (for checks).
    pub activations: Vec<f32>,
    pub states: Vec<usize>,
}

impl Analyzer {
    pub fn new(t: &Tensors) -> Self {
        Analyzer {
            fe: OnlineFrontEnd::new(t),
            nn: OnlineEnsemble::load(t),
            tracker: Tracker::new(Hmm::load(t, "dbn_online")),
            activations: Vec::new(),
            states: Vec::new(),
        }
    }

    /// Feed 44.1 kHz mono samples; pushes the beats committed by this block.
    pub fn push(&mut self, samples: &[f32], out: &mut Vec<Beat>) {
        let nn = &mut self.nn;
        let tracker = &mut self.tracker;
        let acts = &mut self.activations;
        let states = &mut self.states;
        self.fe.push(samples, |_, feat| {
            let a = nn.step(feat);
            acts.push(a);
            states.push(tracker.step(a, out));
        });
    }

    /// Feed with a callback per frame (for feature checks).
    pub fn frames(&mut self) -> usize {
        self.fe.next_frame
    }
}

pub struct Hindsight {
    fe: OfflineFrontEnd,
    nn: OfflineEnsemble,
    hmm: Hmm,
}

pub struct Decode {
    pub beats: Vec<f64>,
    pub front_end_s: f64,
    pub nets_s: f64,
    pub viterbi_s: f64,
}

impl Hindsight {
    pub fn new(t: &Tensors) -> Self {
        Hindsight {
            fe: OfflineFrontEnd::new(t),
            nn: OfflineEnsemble::load(t),
            hmm: Hmm::load(t, "dbn_offline"),
        }
    }

    pub fn features(&mut self, y: &[f32]) -> (usize, Vec<f32>) {
        self.fe.process(y)
    }

    pub fn activations(&self, feats: &[f32], frames: usize) -> Vec<f32> {
        self.nn.run(feats, frames)
    }

    /// One decode of a window (madmom's offline RNNBeatProcessor + DBN).
    pub fn decode(&mut self, y: &[f32]) -> Decode {
        let t0 = std::time::Instant::now();
        let (n, feats) = self.fe.process(y);
        let t1 = std::time::Instant::now();
        let act = self.nn.run(&feats, n);
        let t2 = std::time::Instant::now();
        let beats = if act.len() > 100 {
            self.hmm.offline_beats(&act)
        } else {
            Vec::new()
        };
        let t3 = std::time::Instant::now();
        Decode {
            beats,
            front_end_s: (t1 - t0).as_secs_f64(),
            nets_s: (t2 - t1).as_secs_f64(),
            viterbi_s: (t3 - t2).as_secs_f64(),
        }
    }
}
