//! The live analyzer (docs/design/native-app.md, "Phase A: The Analyzer"): every stage, frame by
//! frame, with bounded memory. Feed 44.1 kHz mono samples; it returns the stream's entries as
//! they are finished: committed beats, ticks (with their keys), onsets, hindsight entries,
//! structure's changes, jumps and ends, accent forecasts, and timbre entries; ticks carry their
//! bands and brightness, onsets their band.
//!
//! Per frame, in this order: the online ensemble's activation, the DBN's belief, the onset
//! function and its peak picking, a tick every `tickS` (its evidence then holds for the commits
//! until the next) with the structure it finishes, the hindsight decodes due at this frame (requested when a window ends,
//! usable `D` later), the commit rule with the phase check's shift, and the accent forecasts
//! the frame's onset and commits complete.
//!
//! Delayed listening stamps committed beats `M − A` before the position that committed them;
//! with [`Stamps::EveryGroup`] every other entry is restamped the same way as it leaves `push` or
//! `finish` (the stages read the positions).
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
use crate::bands::{Bands, Timbre};
use crate::beats::{BeatParams, Belief, Beliefs, Commit, Committer, Evidence};
use crate::dbn::OnlineDbn;
use crate::delayed::{Delayed, DelayedParams, Listening, Stamps};
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
    Timbre(Timbre),
}

impl Entry {
    /// When it is known: its stamp.
    pub fn known_at(&self) -> f64 {
        match self {
            Entry::Beat(c) => c.known_at,
            Entry::Tick(t) => t.known_at,
            Entry::Onset(o) => o.known_at,
            Entry::Hindsight(h) => h.known_at,
            Entry::Change(c) => c.known_at,
            Entry::Jump(j) => j.known_at,
            Entry::End(e) => e.known_at,
            Entry::Forecast(f) => f.known_at,
            Entry::Timbre(t) => t.known_at,
        }
    }

    fn known_at_mut(&mut self) -> &mut f64 {
        match self {
            Entry::Beat(c) => &mut c.known_at,
            Entry::Tick(t) => &mut t.known_at,
            Entry::Onset(o) => &mut o.known_at,
            Entry::Hindsight(h) => &mut h.known_at,
            Entry::Change(c) => &mut c.known_at,
            Entry::Jump(j) => &mut j.known_at,
            Entry::End(e) => &mut e.known_at,
            Entry::Forecast(f) => &mut f.known_at,
            Entry::Timbre(t) => &mut t.known_at,
        }
    }
}

/// The time each group took on the analysis thread (the hindsight worker's decodes aside), for
/// real-time factors: `frontend` is everything else, the framing and spectra feeding the online
/// ensemble included.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Timings {
    pub total: Duration,
    pub rnn: Duration,
    pub beats: Duration,
    pub onsets: Duration,
    pub bands: Duration,
    pub structure: Duration,
    pub hindsight: Duration,
    pub accents: Duration,
}

impl Timings {
    /// What the groups' own timers leave: the front end.
    pub fn frontend(&self) -> Duration {
        self.total.saturating_sub(
            self.rnn
                + self.beats
                + self.onsets
                + self.bands
                + self.structure
                + self.hindsight
                + self.accents,
        )
    }
}

/// Times `$e` into `$field`.
macro_rules! timed {
    ($field:expr, $e:expr) => {{
        let started = Instant::now();
        let value = $e;
        $field += started.elapsed();
        value
    }};
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
    /// Direct listening, or Delayed (committed beats from hindsight).
    pub listening: Listening,
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
    bands: Bands,
    timbre: Vec<Timbre>,
    hindsight: Hindsight,
    /// The latest tick's evidence, which the commits carry.
    tick_evidence: f64,
    last_belief: Option<Belief>,
    commits: Vec<Commit>,
    /// Delayed listening's commit rule (Direct's commits then feed it, unemitted).
    delayed: Option<Delayed>,
    delayed_out: Vec<Commit>,
    params: Params,
    timings: Timings,
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
        Self::with_hindsight_jobs(models, params, 1)
    }

    /// Inline window scheduling with bounded parallel models, for offline batch preparation.
    /// Results retain model order and the same configured hindsight arrival delay.
    pub fn with_hindsight_jobs(
        models: &Tensors,
        params: Params,
        jobs: usize,
    ) -> Result<Self, TensorError> {
        let decoder = HindsightDecoder::new(models, params.beats.fps, SAMPLE_RATE as usize)?
            .with_model_jobs(jobs);
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
            bands: Bands::new(),
            timbre: Vec::new(),
            hindsight,
            nn,
            dbn,
            tick_evidence: 0.0,
            last_belief: None,
            commits: Vec::new(),
            delayed: match p.listening {
                Listening::Direct => None,
                Listening::Delayed(d) => Some(Delayed::new(d, &p.hindsight, &p.beats)),
            },
            delayed_out: Vec::new(),
            params,
            timings: Timings::default(),
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
        let started = Instant::now();
        let before = out.len();
        // (A hop at a time, so the energy's and hindsight's windows are still kept when read.)
        for chunk in samples.chunks(HOP) {
            self.stages.energy.push(chunk);
            self.stages.structure.push_samples(chunk);
            self.stages.bands.push_samples(chunk);
            self.stages.hindsight.push_samples(chunk);
            self.samples += chunk.len();
            let stages = &mut self.stages;
            self.fe.push(chunk, |frame| {
                stages.frame(frame.index, frame.features, frame.spectrum, out);
            });
        }
        stamp(self.stages.params.listening, &mut out[before..]);
        self.stages.timings.total += started.elapsed();
    }

    /// The time each group has taken so far.
    pub fn timings(&self) -> Timings {
        self.stages.timings
    }

    /// The stream's end (Replay with `StreamEnd`): the ticks and hindsight windows Python takes
    /// past the last frame, at the last frame's belief. Once only; no samples may follow.
    pub fn finish(&mut self, out: &mut Vec<Entry>) {
        if std::mem::replace(&mut self.finished, true) {
            return;
        }
        let started = Instant::now();
        let first = out.len();
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
        let before = out.len();
        h.ingest(f64::INFINITY, out);
        Stages::delayed_entries(s.delayed.as_mut(), &mut s.delayed_out, out, before);
        if let Some(d) = s.delayed.as_mut() {
            s.delayed_out.clear();
            d.finish(duration, &mut s.delayed_out);
            out.extend(s.delayed_out.iter().copied().map(Entry::Beat));
        }
        // The timbre entries through the stream's end, from every frame its samples allow.
        let b = &mut s.bands;
        b.advance_to(b.received() / HOP);
        s.timbre.clear();
        b.timbre(duration, &mut s.timbre);
        out.extend(s.timbre.iter().copied().map(Entry::Timbre));
        stamp(s.params.listening, &mut out[first..]);
        s.timings.total += started.elapsed();
    }

    /// The times the analyzer waited for the hindsight worker (an entry due before its decode was
    /// done), and how long in all: the play log records them.
    pub fn hindsight_stalls(&self) -> (usize, Duration) {
        (self.stages.hindsight.stalls, self.stages.hindsight.stalled)
    }
}

/// With [`Stamps::EveryGroup`], restamps entries just emitted (but the committed beats, stamped
/// already) `M − A` before the positions that produced them.
fn stamp(listening: Listening, entries: &mut [Entry]) {
    if let Listening::Delayed(
        p @ DelayedParams {
            stamps: Stamps::EveryGroup,
            ..
        },
    ) = listening
    {
        for e in entries.iter_mut().filter(|e| !matches!(e, Entry::Beat(_))) {
            let k = e.known_at_mut();
            *k = p.stamp(*k);
        }
    }
}

impl Stages {
    fn frame(&mut self, n: usize, features: &[f32], spectrum: &[f32], out: &mut Vec<Entry>) {
        let act = timed!(self.timings.rnn, self.nn.step(features));
        let (state, belief) = timed!(self.timings.beats, {
            let state = self.dbn.step(act);
            let belief = self.beliefs.step(state);
            self.evidence.push(act);
            (state, belief)
        });
        self.hindsight.online.push(n, state.since);
        let picked = timed!(self.timings.onsets, {
            let odf = self.superflux.step(spectrum);
            self.picker
                .step(odf)
                .map(|time| self.strengths.onset(time, odf))
        });
        // (The bands' frame n holds the samples before n * HOP, all heard by this frame.)
        timed!(self.timings.bands, self.bands.advance_to(n));
        if let Some(mut onset) = picked {
            onset.band = timed!(
                self.timings.bands,
                self.bands.onset(
                    (onset.known_at * self.params.beats.fps).round_ties_even() as usize,
                    onset.known_at,
                )
            );
            timed!(self.timings.accents, self.accents.push_onset(&onset));
            out.push(Entry::Onset(onset));
        }
        let tf = self.params.beats.tick_frames;
        let fps = self.params.beats.fps;
        if n > 0 && n.is_multiple_of(tf) {
            self.tick(&belief, n, out);
        }
        self.timbre.clear();
        timed!(
            self.timings.bands,
            self.bands.timbre(n as f64 / fps, &mut self.timbre)
        );
        out.extend(self.timbre.iter().copied().map(Entry::Timbre));
        // The windows ended by this frame are decoded, then each entry due by now becomes usable.
        let started = Instant::now();
        let h = &mut self.hindsight;
        h.request_due(n, fps);
        let before = out.len();
        h.ingest(n as f64 / fps, out);
        Self::delayed_entries(self.delayed.as_mut(), &mut self.delayed_out, out, before);
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
        self.timings.hindsight += started.elapsed();
        self.commits.clear();
        timed!(
            self.timings.beats,
            self.committer
                .step(&belief, shift, self.tick_evidence, &mut self.commits)
        );
        match self.delayed.as_mut() {
            None => out.extend(self.commits.iter().copied().map(Entry::Beat)),
            Some(d) => {
                for &c in &self.commits {
                    d.push_direct(c);
                }
            }
        }
        timed!(self.timings.accents, {
            for c in &self.commits {
                self.accents.push_beat(c);
            }
            self.forecasts.clear();
            self.accents.take(&mut self.forecasts);
        });
        out.extend(self.forecasts.iter().copied().map(Entry::Forecast));
        self.last_belief = Some(belief);
    }

    /// Delayed listening: the hindsight entries just emitted (from `before` in `out`) commit their
    /// bands, emitted right after them.
    fn delayed_entries(
        delayed: Option<&mut Delayed>,
        buf: &mut Vec<Commit>,
        out: &mut Vec<Entry>,
        before: usize,
    ) {
        let Some(d) = delayed else { return };
        buf.clear();
        for e in &out[before..] {
            if let Entry::Hindsight(x) = e {
                d.entry(x, buf);
            }
        }
        out.extend(buf.iter().copied().map(Entry::Beat));
    }

    /// The tick at frame `f`, from `belief` (the frame's, or the last one's past the end).
    fn tick(&mut self, belief: &Belief, f: usize, out: &mut Vec<Entry>) {
        let t = f as f64 / self.params.beats.fps;
        let evidence = self.evidence.at(belief.last, belief.period, t);
        let energy = self.energy.at(t);
        let level = self.levels.step(energy);
        self.tick_evidence = evidence;
        let form = timed!(self.timings.structure, self.structure.tick_at(t, f * HOP));
        // (Past the stream's end, the last frame the samples allow.)
        let (bands, brightness) = timed!(self.timings.bands, {
            self.bands.advance_to(f.min(self.bands.received() / HOP));
            self.bands.tick(f, self.params.beats.tick_frames)
        });
        out.push(Entry::Tick(Tick {
            t,
            known_at: t,
            evidence,
            energy,
            level,
            key: form.key,
            bands,
            brightness,
        }));
        out.extend(form.change.map(Entry::Change));
        out.extend(form.jump.map(Entry::Jump));
        out.extend(form.end.map(Entry::End));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bands::TickBands;
    use crate::ticks::Level;

    /// One entry of every group, each known at 10 s.
    fn every_group() -> Vec<Entry> {
        let k = 10.0;
        vec![
            Entry::Beat(Commit {
                t: 11.0,
                known_at: k,
                period: 0.5,
                evidence: 0.9,
            }),
            Entry::Tick(Tick {
                t: k,
                known_at: k,
                evidence: 0.5,
                energy: 0.4,
                level: Level::Loud,
                key: Some("A minor"),
                bands: TickBands::default(),
                brightness: 0.3,
            }),
            Entry::Onset(Onset {
                t: 9.98,
                known_at: k,
                strength: 0.7,
                strong: true,
                band: "kick",
            }),
            Entry::Hindsight(HindsightEntry {
                known_at: k,
                window_end: 9.33,
                offline: vec![9.0],
                online: vec![9.01],
            }),
            Entry::Change(Change {
                t: 8.0,
                known_at: k,
                strength: 0.6,
            }),
            Entry::Jump(Jump {
                t: 9.9,
                known_at: k,
                db: 6.0,
            }),
            Entry::End(End {
                t: 1.0,
                known_at: k,
            }),
            Entry::Forecast(Forecast {
                t: 12.0,
                known_at: k,
                cycle: 4,
                confidence: 0.8,
            }),
            Entry::Timbre(Timbre {
                t: k,
                known_at: k,
                brightness: 0.2,
                flatness: 0.1,
                percussive: 0.5,
                density: 3.0,
            }),
        ]
    }

    #[test]
    fn every_group_but_the_beats_is_restamped_only_in_its_mode() {
        let p = DelayedParams {
            stamps: Stamps::EveryGroup,
            ..DelayedParams::chosen()
        };
        let mut got = every_group();
        stamp(Listening::Delayed(p), &mut got);
        for (g, w) in got.iter().zip(every_group()) {
            let want = if matches!(w, Entry::Beat(_)) {
                w.known_at()
            } else {
                p.stamp(w.known_at())
            };
            assert_eq!(g.known_at(), want, "{g:?}");
            let mut g = g.clone();
            *g.known_at_mut() = w.known_at();
            assert_eq!(g, w, "content changed");
        }
        for listening in [
            Listening::Direct,
            Listening::Delayed(DelayedParams::chosen()),
        ] {
            let mut got = every_group();
            stamp(listening, &mut got);
            assert_eq!(got, every_group());
        }
    }
}
