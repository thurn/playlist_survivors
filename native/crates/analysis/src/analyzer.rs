//! The live analyzer (docs/design/native-app.md, "Phase A: The Analyzer"): every stage, frame by
//! frame, with bounded memory. Feed 44.1 kHz mono samples; it returns the stream's entries as
//! they are finished: committed beats, ticks (with their keys), onsets, hindsight entries,
//! structure's changes, jumps and ends, and accent forecasts (bands and timbre join in A7).
//!
//! Per frame, in this order: the online ensemble's activation, the DBN's belief, the onset
//! function and its peak picking, a tick every `tickS` (its evidence then holds for the commits
//! until the next) with the structure it finishes, the hindsight decodes due at this frame (requested when a window ends,
//! usable `D` later), the commit rule with the phase check's shift, and the accent forecasts
//! the frame's onset and commits complete.
//!
//! The hindsight decode runs inline (`Analyzer::new`), or on a worker thread
//! (`Analyzer::with_hindsight_worker`, the app's) that the analyzer waits for when an entry is
//! due before its decode is done, counting the stall: either way the entries are a function of
//! the samples alone.

use std::collections::VecDeque;
use std::sync::mpsc;
use std::thread;
use std::time::{Duration, Instant};

use crate::accents::{AccentParams, Accents, Forecast};
use crate::beats::{BeatParams, Belief, Beliefs, Commit, Committer, Evidence};
use crate::dbn::OnlineDbn;
use crate::frontend::{HOP, OnlineFrontEnd, SAMPLE_RATE};
use crate::hindsight::{
    HindsightDecoder, HindsightEntry, HindsightParams, OnlineBeats, phase_shift,
};
use crate::onsets::{Onset, OnsetParams, PeakPicker, Strengths, Superflux};
use crate::rnn::OnlineEnsemble;
use crate::structure::{Change, End, Jump, Structure, StructureParams};
use crate::tensors::{TensorError, Tensors};
use crate::ticks::{Energy, Levels, Tick, TickParams};

/// An entry of the stream, as soon as it is final.
#[derive(Clone, Debug, PartialEq)]
pub enum Entry {
    Beat(Commit),
    Tick(Tick),
    Onset(Onset),
    Hindsight(HindsightEntry),
    Change(Change),
    Jump(Jump),
    End(End),
    Forecast(Forecast),
}

/// The analyzer's parameters (the stream's: `manifest.json`'s `streamParams`, and `D`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Params {
    pub beats: BeatParams,
    pub ticks: TickParams,
    pub onsets: OnsetParams,
    pub hindsight: HindsightParams,
    pub structure: StructureParams,
    pub accents: AccentParams,
}

/// The hindsight worker thread and its channels.
struct Worker {
    requests: Option<mpsc::Sender<Vec<f32>>>,
    results: mpsc::Receiver<Vec<f64>>,
    handle: Option<thread::JoinHandle<()>>,
}

impl Worker {
    /// The worker stopped: re-raise its panic here, or report that it is gone.
    fn failed(&mut self) -> ! {
        self.requests = None;
        if let Some(handle) = self.handle.take()
            && let Err(payload) = handle.join()
        {
            std::panic::resume_unwind(payload);
        }
        panic!("the hindsight worker stopped");
    }
}

impl Drop for Worker {
    fn drop(&mut self) {
        // (Closing the requests ends the worker's loop once its decode in flight is done.)
        self.requests = None;
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}

/// Where hindsight decodes run.
enum Decoder {
    Inline(Box<HindsightDecoder>),
    Worker(Worker),
}

impl Decoder {
    fn request(&mut self, window: Vec<f32>) -> Option<Vec<f64>> {
        match self {
            Decoder::Inline(d) => Some(d.decode(&window)),
            Decoder::Worker(w) => {
                let sent = w.requests.as_ref().is_some_and(|r| r.send(window).is_ok());
                if !sent {
                    w.failed();
                }
                None
            }
        }
    }

    /// The next requested decode's result, waiting for the worker if it isn't done; returns
    /// how long it waited when it had to.
    fn wait(&mut self) -> (Vec<f64>, Option<Duration>) {
        match self {
            Decoder::Inline(_) => unreachable!("inline decodes return at once"),
            Decoder::Worker(w) => match w.results.try_recv() {
                Ok(beats) => (beats, None),
                Err(mpsc::TryRecvError::Empty) => {
                    let t0 = Instant::now();
                    match w.results.recv() {
                        Ok(beats) => (beats, Some(t0.elapsed())),
                        Err(_) => w.failed(),
                    }
                }
                Err(mpsc::TryRecvError::Disconnected) => w.failed(),
            },
        }
    }
}

/// A requested decode not yet usable.
struct Pending {
    window_end: f64,
    window_start: f64,
    online: Vec<f64>,
    beats: Option<Vec<f64>>,
}

/// The hindsight stage: the samples of the last window, the believed beats, the decodes in
/// flight and the latest usable entries.
struct Hindsight {
    p: HindsightParams,
    decoder: Decoder,
    samples: VecDeque<f32>,
    keep: usize,
    received: usize,
    online: OnlineBeats,
    /// The next window's index (its end is `next * everyS`).
    next: usize,
    pending: VecDeque<Pending>,
    /// The latest usable entries (as many as the phase check reads), oldest first.
    usable: VecDeque<HindsightEntry>,
    stalls: usize,
    stalled: Duration,
}

impl Hindsight {
    fn push_samples(&mut self, chunk: &[f32]) {
        for &s in chunk {
            if self.samples.len() == self.keep {
                self.samples.pop_front();
            }
            self.samples.push_back(s);
        }
        self.received += chunk.len();
    }

    /// Window `k`'s end (s): `k * everyS` to the microsecond (Python's `round(k * everyS, 6)`;
    /// exact for the stream's whole seconds).
    fn window_end(&self, k: usize) -> f64 {
        (k as f64 * self.p.every_s * 1e6).round_ties_even() / 1e6
    }

    fn window_samples(&self, t_end: f64) -> (usize, usize) {
        let sr = f64::from(SAMPLE_RATE);
        let start = (t_end - self.p.window_s).max(0.0);
        (
            (start * sr).round_ties_even() as usize,
            (t_end * sr).round_ties_even() as usize,
        )
    }

    /// Requests the decode of the window ending at `T` (its samples as far as they go: all of
    /// them, but at the stream's end), with the believed beats by `frame`.
    fn request(&mut self, t_end: f64, frame: usize) {
        let (lo, hi) = self.window_samples(t_end);
        let hi = hi.min(self.received);
        let first = self.received - self.samples.len();
        assert!(
            lo >= first,
            "hindsight reads sample {lo}, older than the {} kept",
            self.keep
        );
        let window: Vec<f32> = self
            .samples
            .range(lo - first..hi - first)
            .copied()
            .collect();
        let online = self.online.kept(frame, t_end - self.p.keep_s, t_end);
        let beats = self.decoder.request(window);
        let window_start = (t_end - self.p.window_s).max(0.0);
        self.pending.push_back(Pending {
            window_end: t_end,
            window_start,
            online,
            beats,
        });
    }

    /// Requests every window that has ended by frame `n` (its frame reached and its samples in).
    fn request_due(&mut self, n: usize, fps: f64) {
        loop {
            let t_end = self.window_end(self.next);
            let (_, hi) = self.window_samples(t_end);
            if n < (t_end * fps).round_ties_even() as usize || hi > self.received {
                break;
            }
            self.request(t_end, n);
            self.next += 1;
        }
    }

    /// Makes the pending entries usable by time `t` usable, and emits them.
    fn ingest(&mut self, t: f64, out: &mut Vec<Entry>) {
        while self
            .pending
            .front()
            .is_some_and(|e| e.window_end + self.p.delay_s <= t + 1e-9)
        {
            let e = self.pending.pop_front().expect("pending");
            let beats = if let Some(beats) = e.beats {
                beats
            } else {
                let (beats, waited) = self.decoder.wait();
                if let Some(w) = waited {
                    self.stalls += 1;
                    self.stalled += w;
                }
                beats
            };
            let lo = e.window_end - self.p.keep_s;
            let offline = beats
                .into_iter()
                .map(|b| b + e.window_start + self.p.bias_s)
                .filter(|&b| lo <= b && b <= e.window_end)
                .collect();
            let entry = HindsightEntry {
                known_at: e.window_end + self.p.delay_s,
                window_end: e.window_end,
                offline,
                online: e.online,
            };
            out.push(Entry::Hindsight(entry.clone()));
            self.usable.push_back(entry);
            while self.usable.len() > self.p.phase_persist.max(1) {
                self.usable.pop_front();
            }
        }
    }
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
    structure: Structure,
    accents: Accents,
    forecasts: Vec<Forecast>,
    hindsight: Hindsight,
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
    /// The analyzer, decoding hindsight windows inline (files, tests).
    pub fn new(models: &Tensors, params: Params) -> Result<Self, TensorError> {
        let decoder = HindsightDecoder::new(models, params.beats.fps, SAMPLE_RATE as usize)?;
        Analyzer::build(models, params, Decoder::Inline(Box::new(decoder)))
    }

    /// The analyzer, decoding hindsight windows on a worker thread (the app's live analysis).
    pub fn with_hindsight_worker(models: &Tensors, params: Params) -> Result<Self, TensorError> {
        let mut decoder = HindsightDecoder::new(models, params.beats.fps, SAMPLE_RATE as usize)?;
        let (requests, rx) = mpsc::channel::<Vec<f32>>();
        let (tx, results) = mpsc::channel();
        let handle = thread::Builder::new()
            .name("hindsight".into())
            .spawn(move || {
                for window in rx {
                    if tx.send(decoder.decode(&window)).is_err() {
                        break;
                    }
                }
            })
            .map_err(|e| TensorError::Format(format!("can't start the hindsight worker: {e}")))?;
        Analyzer::build(
            models,
            params,
            Decoder::Worker(Worker {
                requests: Some(requests),
                results,
                handle: Some(handle),
            }),
        )
    }

    fn build(models: &Tensors, params: Params, decoder: Decoder) -> Result<Self, TensorError> {
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
        let window = (p.hindsight.window_s * f64::from(SAMPLE_RATE)).round_ties_even() as usize;
        let hindsight = Hindsight {
            p: p.hindsight.clone(),
            decoder,
            // (A window, and the hop a chunk may run past its end.)
            keep: window + HOP + 2,
            samples: VecDeque::new(),
            received: 0,
            online: OnlineBeats::new(p.hindsight.keep_s, p.beats.fps, p.beats.bias_s),
            next: 1,
            pending: VecDeque::new(),
            usable: VecDeque::new(),
            stalls: 0,
            stalled: Duration::ZERO,
        };
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
            structure: Structure::new(
                models,
                &p.structure,
                f64::from(SAMPLE_RATE),
                p.beats.fps / p.beats.tick_frames as f64,
            )?,
            accents: Accents::new(&p.accents),
            forecasts: Vec::new(),
            hindsight,
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
        // (A hop at a time, so the energy's and hindsight's windows are still kept when read.)
        for chunk in samples.chunks(HOP) {
            self.stages.energy.push(chunk);
            self.stages.structure.push_samples(chunk);
            self.stages.hindsight.push_samples(chunk);
            self.samples += chunk.len();
            let stages = &mut self.stages;
            self.fe.push(chunk, |frame| {
                stages.frame(frame.index, frame.features, frame.spectrum, out);
            });
        }
    }

    /// The stream's end (Replay with `StreamEnd`): the ticks and hindsight windows Python takes
    /// past the last frame, at the last frame's belief. Once only; no samples may follow.
    pub fn finish(&mut self, out: &mut Vec<Entry>) {
        if std::mem::replace(&mut self.finished, true) {
            return;
        }
        let s = &mut self.stages;
        let Some(belief) = s.last_belief else { return };
        let tf = s.params.beats.tick_frames;
        let fps = s.params.beats.fps;
        let duration = self.samples as f64 / f64::from(SAMPLE_RATE);
        // (Python: floor(len / sr * fps / tick_frames + 1e-9) ticks.)
        let ticks = (duration * fps / tf as f64 + 1e-9).floor() as usize;
        let mut f = (belief.frame / tf + 1) * tf;
        while f <= ticks * tf {
            s.tick(&belief, f, out);
            f += tf;
        }
        // (Python: a window for every k with k * everyS <= duration + 1e-9.)
        let h = &mut s.hindsight;
        while h.next as f64 * h.p.every_s <= duration + 1e-9 {
            let t_end = h.window_end(h.next);
            h.request(t_end, usize::MAX);
            h.next += 1;
        }
        h.ingest(f64::INFINITY, out);
    }

    /// The times the analyzer waited for the hindsight worker (an entry due before its decode was
    /// done), and how long in all: the play log records them.
    pub fn hindsight_stalls(&self) -> (usize, Duration) {
        (self.stages.hindsight.stalls, self.stages.hindsight.stalled)
    }
}

impl Stages {
    fn frame(&mut self, n: usize, features: &[f32], spectrum: &[f32], out: &mut Vec<Entry>) {
        let act = self.nn.step(features);
        let state = self.dbn.step(act);
        let belief = self.beliefs.step(state);
        self.evidence.push(act);
        self.hindsight.online.push(n, state.since);
        let odf = self.superflux.step(spectrum);
        if let Some(time) = self.picker.step(odf) {
            let onset = self.strengths.onset(time, odf);
            self.accents.push_onset(&onset);
            out.push(Entry::Onset(onset));
        }
        let tf = self.params.beats.tick_frames;
        let fps = self.params.beats.fps;
        if n > 0 && n.is_multiple_of(tf) {
            self.tick(&belief, n, out);
        }
        // The windows ended by this frame are decoded, then each entry due by now becomes usable.
        let h = &mut self.hindsight;
        h.request_due(n, fps);
        h.ingest(n as f64 / fps, out);
        let shift = if h.usable.is_empty() {
            0.0
        } else {
            h.usable.make_contiguous();
            phase_shift(
                h.usable.as_slices().0,
                self.committer.biased_last(&belief),
                belief.period,
                &h.p,
            )
        };
        self.commits.clear();
        self.committer
            .step(&belief, shift, self.tick_evidence, &mut self.commits);
        out.extend(self.commits.iter().copied().map(Entry::Beat));
        for c in &self.commits {
            self.accents.push_beat(c);
        }
        self.forecasts.clear();
        self.accents.take(&mut self.forecasts);
        out.extend(self.forecasts.iter().copied().map(Entry::Forecast));
        self.last_belief = Some(belief);
    }

    /// The tick at frame `f`, from `belief` (the frame's, or the last one's past the end).
    fn tick(&mut self, belief: &Belief, f: usize, out: &mut Vec<Entry>) {
        let t = f as f64 / self.params.beats.fps;
        let evidence = self.evidence.at(belief.last, belief.period, t);
        let energy = self.energy.at(t);
        let level = self.levels.step(energy);
        self.tick_evidence = evidence;
        let form = self.structure.tick_at(t, f * HOP);
        out.push(Entry::Tick(Tick {
            t,
            evidence,
            energy,
            level,
            key: form.key,
        }));
        out.extend(form.change.map(Entry::Change));
        out.extend(form.jump.map(Entry::Jump));
        out.extend(form.end.map(Entry::End));
    }
}
