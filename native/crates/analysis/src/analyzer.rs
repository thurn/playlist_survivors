//! The live analyzer (docs/design/native-app.md, "Phase A: The Analyzer"): every stage, frame by
//! frame, with bounded memory. Feed 44.1 kHz mono samples; it returns the stream's entries as
//! they are finished: committed beats, ticks and onsets so far (the hindsight grids, the phase
//! check, structure, accents, bands and timbre join in later beads).
//!
//! Per frame, in this order: the online ensemble's activation, the DBN's belief, the onset
//! function and its peak picking, a tick every `tickS` (its evidence then holds for the commits
//! until the next), and the commit rule.

use crate::beats::{BeatParams, Belief, Beliefs, Commit, Committer, Evidence};
use crate::dbn::OnlineDbn;
use crate::frontend::{HOP, OnlineFrontEnd};
use crate::onsets::{Onset, OnsetParams, PeakPicker, Strengths, Superflux};
use crate::rnn::OnlineEnsemble;
use crate::tensors::{TensorError, Tensors};
use crate::ticks::{Energy, Levels, Tick, TickParams};

/// An entry of the stream, as soon as it is final.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Entry {
    Beat(Commit),
    Tick(Tick),
    Onset(Onset),
}

/// The analyzer's parameters (the stream's: `manifest.json`'s `streamParams`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Params {
    pub beats: BeatParams,
    pub ticks: TickParams,
    pub onsets: OnsetParams,
}

/// Every stage's state.
struct Stages {
    nn: OnlineEnsemble,
    dbn: OnlineDbn,
    beliefs: Beliefs,
    evidence: Evidence,
    committer: Committer,
    superflux: Superflux,
    picker: PeakPicker,
    strengths: Strengths,
    energy: Energy,
    levels: Levels,
    /// The latest tick's evidence, which the commits carry.
    tick_evidence: f64,
    last_belief: Option<Belief>,
    commits: Vec<Commit>,
    params: Params,
}

/// The streaming analyzer.
pub struct Analyzer {
    fe: OnlineFrontEnd,
    stages: Stages,
    samples: usize,
    finished: bool,
}

impl Analyzer {
    pub fn new(models: &Tensors, params: Params) -> Result<Self, TensorError> {
        let fe = OnlineFrontEnd::new(models)?;
        let nn = OnlineEnsemble::new(models)?;
        if nn.input_width() != fe.width() {
            return Err(TensorError::Format(format!(
                "the ensemble takes {} features, the front end gives {}",
                nn.input_width(),
                fe.width()
            )));
        }
        let dbn = OnlineDbn::new(models, "dbn_online", params.beats.fps)?;
        let p = &params;
        let stages = Stages {
            evidence: Evidence::new(&p.beats, dbn.max_period()),
            beliefs: Beliefs::new(&p.beats),
            committer: Committer::new(&p.beats),
            superflux: Superflux::new(&p.onsets, fe.diff_frames()),
            picker: PeakPicker::new(&p.onsets),
            strengths: Strengths::new(&p.onsets),
            // (Samples are fed a hop at a time, so a tick's energy is taken at most a hop late.)
            energy: Energy::new(&p.ticks, HOP),
            levels: Levels::new(&p.ticks),
            nn,
            dbn,
            tick_evidence: 0.0,
            last_belief: None,
            commits: Vec::new(),
            params,
        };
        Ok(Analyzer {
            fe,
            stages,
            samples: 0,
            finished: false,
        })
    }

    /// Feeds samples; appends the entries they finish to `out`, in order.
    pub fn push(&mut self, samples: &[f32], out: &mut Vec<Entry>) {
        assert!(!self.finished, "samples pushed after the stream's end");
        // (A hop at a time, so the energy's window is still kept when a tick is taken.)
        for chunk in samples.chunks(HOP) {
            self.stages.energy.push(chunk);
            self.samples += chunk.len();
            let stages = &mut self.stages;
            self.fe.push(chunk, |frame| {
                stages.frame(frame.index, frame.features, frame.spectrum, out);
            });
        }
    }

    /// The stream's end (Replay with `StreamEnd`): the ticks Python takes past the last frame,
    /// at the last frame's belief. Once only; no samples may follow.
    pub fn finish(&mut self, out: &mut Vec<Entry>) {
        if std::mem::replace(&mut self.finished, true) {
            return;
        }
        let s = &mut self.stages;
        let Some(belief) = s.last_belief else { return };
        let tf = s.params.beats.tick_frames;
        let fps = s.params.beats.fps;
        // (Python: floor(len / sr * fps / tick_frames + 1e-9) ticks.)
        let ticks = (self.samples as f64 / s.params.ticks.sample_rate * fps / tf as f64 + 1e-9)
            .floor() as usize;
        let mut f = (belief.frame / tf + 1) * tf;
        while f <= ticks * tf {
            s.tick(&belief, f, out);
            f += tf;
        }
    }
}

impl Stages {
    fn frame(&mut self, n: usize, features: &[f32], spectrum: &[f32], out: &mut Vec<Entry>) {
        let act = self.nn.step(features);
        let belief = self.beliefs.step(self.dbn.step(act));
        self.evidence.push(act);
        let odf = self.superflux.step(spectrum);
        if let Some(time) = self.picker.step(odf) {
            out.push(Entry::Onset(self.strengths.onset(time, odf)));
        }
        let tf = self.params.beats.tick_frames;
        if n > 0 && n.is_multiple_of(tf) {
            self.tick(&belief, n, out);
        }
        self.commits.clear();
        self.committer
            .step(&belief, 0.0, self.tick_evidence, &mut self.commits);
        out.extend(self.commits.iter().copied().map(Entry::Beat));
        self.last_belief = Some(belief);
    }

    /// The tick at frame `f`, from `belief` (the frame's, or the last one's past the end).
    fn tick(&mut self, belief: &Belief, f: usize, out: &mut Vec<Entry>) {
        let t = f as f64 / self.params.beats.fps;
        let evidence = self.evidence.at(belief.last, belief.period, t);
        let energy = self.energy.at(t);
        let level = self.levels.step(energy);
        self.tick_evidence = evidence;
        out.push(Entry::Tick(Tick {
            t,
            evidence,
            energy,
            level,
        }));
    }
}
