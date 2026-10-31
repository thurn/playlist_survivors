//! One source's session (docs/design/unity-game.md, "Sources" and "The delay line and Unity's
//! output"): what every source shares. A source feeds two things, each on its own timeline at its
//! own rate: native's delay line (`audio::delay`) with the music's stereo frames, and the
//! [`Analysis`] with the analyzer's mono samples (44.1 kHz). Song time is the position on those
//! timelines: frame `n` of the line is song time `n / rate`.
//!
//! - [`crate::file`]: a File source, decoded and fed up to `M` past the render position, faster
//!   than real time at first.
//! - [`crate::system`]: a System source, an app tapped and muted, fed in real time.
//!
//! The [`Analysis`] runs native's analyzer (with its hindsight worker) in Delayed listening, every
//! group stamped (`Stamps::EveryGroup`). Its entries become `ps_entry` records (`analysis::flat`)
//! in a ring that `ps_poll` drains, and the session's record (`record`).
//!
//! `ps_render_music` (Unity's audio thread) pulls the line and reads each output frame at its song
//! time (interpolated when the line's rate isn't the output's). It starts the music once the line
//! holds the source's start (or the whole source), and from then on song time advances by exactly
//! the frames rendered: a buffer the line can't fill plays silence there and counts as a stall.
//! Once the music stopped (a System source's watchdog fired) it plays silence until the line
//! frame the music resumes at. It records each buffer's anchor (its DSP time, song time and frame)
//! under a sequence lock. It takes no lock, allocates nothing and never waits for the other
//! threads.

use std::cell::UnsafeCell;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU32, AtomicU64, Ordering, fence};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

use analysis::analyzer::{Analyzer, Params};
use analysis::delayed::{DelayedParams, Listening, Stamps};
use analysis::flat::{self, Record};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use audio::delay::{DelayLine, Read};
use audio::follow::Follower;
use capture::clock::now_s;

use crate::decode::SR;
use crate::record::{Meta, Recorder, Release};
use crate::system::Taps;
use crate::{
    Error, PS_ENDED, PS_ERR_ARGUMENT, PS_ERR_FAILED, PS_ERR_MODELS, PS_ERR_SOURCE, PS_PLAYING,
    PS_STOPPED, PS_WAITING, Status,
};

/// The records the ring holds for `ps_poll` (about a minute and a half of them).
const RECORDS: usize = 1 << 14;
/// How long a thread waiting for the render position or for room sleeps between looks.
pub(crate) const NAP: Duration = Duration::from_millis(1);
/// How often `ps_status` looks for a new default output device (s).
const DEVICE_LOOK_S: f64 = 1.0;

/// A session's configuration, from `ps_config`.
pub struct Config {
    pub source: Source,
    pub m: f64,
    pub output_rate: f64,
    pub models: PathBuf,
    pub session_dir: Option<PathBuf>,
}

/// Where the music comes from.
pub enum Source {
    /// An audio file, or an M3U playlist.
    File(PathBuf),
    /// App `pid`'s audio, tapped by `taps` (the platform's, or a test's).
    System { pid: i32, taps: Box<dyn Taps> },
}

/// An `f64` shared through its bits.
pub(crate) struct AtomicF64(AtomicU64);

impl AtomicF64 {
    pub(crate) fn new(x: f64) -> AtomicF64 {
        AtomicF64(AtomicU64::new(x.to_bits()))
    }
    pub(crate) fn load(&self) -> f64 {
        f64::from_bits(self.0.load(Ordering::Acquire))
    }
    pub(crate) fn store(&self, x: f64) {
        self.0.store(x.to_bits(), Ordering::Release);
    }
}

/// The clock anchor, written by the render thread under a sequence lock: the main thread retries
/// a read the render thread overlapped.
struct Anchor {
    seq: AtomicU64,
    dsp: AtomicU64,
    song: AtomicU64,
    frames: AtomicU64,
}

impl Anchor {
    fn write(&self, dsp: f64, song: f64, frames: u64) {
        let s = self.seq.load(Ordering::Relaxed);
        self.seq.store(s.wrapping_add(1), Ordering::Relaxed);
        fence(Ordering::Release);
        self.dsp.store(dsp.to_bits(), Ordering::Relaxed);
        self.song.store(song.to_bits(), Ordering::Relaxed);
        self.frames.store(frames, Ordering::Relaxed);
        self.seq.store(s.wrapping_add(2), Ordering::Release);
    }

    fn read(&self) -> (f64, f64, u64) {
        loop {
            let s = self.seq.load(Ordering::Acquire);
            let dsp = self.dsp.load(Ordering::Relaxed);
            let song = self.song.load(Ordering::Relaxed);
            let frames = self.frames.load(Ordering::Relaxed);
            fence(Ordering::Acquire);
            if s.is_multiple_of(2) && self.seq.load(Ordering::Relaxed) == s {
                return (f64::from_bits(dsp), f64::from_bits(song), frames);
            }
            std::hint::spin_loop();
        }
    }
}

/// What the threads share.
pub(crate) struct Shared {
    pub m: f64,
    pub output_rate: f64,
    /// The delay line's rate: the output's (File) or the tap's (System).
    pub line_rate: f64,
    /// How much music the line holds before it starts playing (s), unless the source is shorter.
    pub start_s: f64,
    pub stop: AtomicBool,
    /// The render position: the song time of the next buffer's first frame.
    pub song: AtomicF64,
    anchor: Anchor,
    state: AtomicI32,
    stalls: AtomicU32,
    gain: AtomicU32,
    /// The source's length in analyzer samples, once it is known (`u64::MAX` until then; always,
    /// for a System source).
    pub total: AtomicU64,
    /// The frames the source has written into the line (at its rate), and the samples the
    /// analyzer has read.
    pub written: AtomicU64,
    pub analysed: AtomicU64,
    pub analysis_done: AtomicBool,
    /// The line frame the music resumes at after it stopped: silence before it, and the music
    /// is stopped while the render position is before it (`u64::MAX` until the app is tapped
    /// again). 0 while nothing stopped.
    pub resume: AtomicU64,
    /// The main thread's last call (host s): its heartbeat, for a System source's watchdog.
    pub heartbeat: AtomicF64,
    /// System: the app is muted (tapped), and how many times the watchdog released it.
    pub muted: AtomicBool,
    pub releases: AtomicU32,
    /// The default output device, its latency (s), when `ps_status` last looked (host s), and
    /// whether it has changed since the session opened.
    device: AtomicU32,
    output_latency: AtomicF64,
    device_looked: AtomicF64,
    device_changed: AtomicBool,
    /// The first failure of a thread: the source, the analysis or the record.
    error: Mutex<Option<String>>,
}

impl Shared {
    pub(crate) fn new(m: f64, output_rate: f64, line_rate: f64, start_s: f64) -> Shared {
        let now = now_s();
        Shared {
            m,
            output_rate,
            line_rate,
            start_s,
            stop: AtomicBool::new(false),
            song: AtomicF64::new(0.0),
            anchor: Anchor {
                seq: AtomicU64::new(0),
                dsp: AtomicU64::new(0),
                song: AtomicU64::new(0),
                frames: AtomicU64::new(0),
            },
            state: AtomicI32::new(PS_WAITING),
            stalls: AtomicU32::new(0),
            gain: AtomicU32::new(1f32.to_bits()),
            total: AtomicU64::new(u64::MAX),
            written: AtomicU64::new(0),
            analysed: AtomicU64::new(0),
            analysis_done: AtomicBool::new(false),
            resume: AtomicU64::new(0),
            heartbeat: AtomicF64::new(now),
            muted: AtomicBool::new(false),
            releases: AtomicU32::new(0),
            device: AtomicU32::new(crate::latency::default_output()),
            output_latency: AtomicF64::new(crate::latency::output_latency()),
            device_looked: AtomicF64::new(now),
            device_changed: AtomicBool::new(false),
            error: Mutex::new(None),
        }
    }

    pub(crate) fn fail(&self, message: String) {
        let mut e = self
            .error
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        e.get_or_insert(message);
    }

    pub(crate) fn failed(&self) -> Option<String> {
        self.error
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    pub(crate) fn stopped(&self) -> bool {
        self.stop.load(Ordering::Relaxed)
    }

    /// Waits until song position `until` (s) is at most `M` past the render position; false if
    /// the session stopped first.
    pub(crate) fn wait_for_render(&self, until: f64) -> bool {
        while until > self.song.load() + self.m {
            if self.stopped() {
                return false;
            }
            std::thread::sleep(NAP);
        }
        !self.stopped()
    }

    /// The main thread checks in; at most once a second, it also looks for a new default output
    /// device.
    fn beat(&self) {
        let now = now_s();
        self.heartbeat.store(now);
        if now - self.device_looked.load() < DEVICE_LOOK_S {
            return;
        }
        self.device_looked.store(now);
        let device = crate::latency::default_output();
        if device != self.device.swap(device, Ordering::Relaxed) {
            self.device_changed.store(true, Ordering::Relaxed);
            self.output_latency.store(crate::latency::output_latency());
        }
    }
}

/// The render thread's own state.
struct Render {
    line: DelayLine,
    follower: Follower,
    /// The output frames rendered since the music started.
    frames: u64,
    started: bool,
}

/// An open session (`ps_session`). `ps_render_music` uses only its render state and `ps_poll`
/// only its records, each from one thread at a time, so the two run concurrently.
pub struct Session {
    render: UnsafeCell<Render>,
    records: UnsafeCell<rtrb::Consumer<Record>>,
    shared: Arc<Shared>,
    threads: Vec<JoinHandle<()>>,
}

/// The line frame at analyzer sample `n`, at the line's `rate`: where the music puts what the
/// analyzer hears there.
pub(crate) fn music_frame(n: u64, rate: f64) -> u64 {
    (n as f64 * rate / SR).round() as u64
}

/// Starts a thread, or fails as the source does.
pub(crate) fn spawn(
    name: &str,
    f: impl FnOnce() + Send + 'static,
) -> Result<JoinHandle<()>, Error> {
    std::thread::Builder::new()
        .name(name.into())
        .spawn(f)
        .map_err(|e| Error::new(PS_ERR_SOURCE, format!("can't start a thread: {e}")))
}

/// Runs a thread's work, recording its error or panic as the session's failure.
pub(crate) fn guarded(shared: &Shared, f: impl FnOnce() -> Result<(), String>) {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(f)) {
        Ok(Ok(())) => {}
        Ok(Err(e)) => shared.fail(e),
        Err(panic) => shared.fail(crate::panic_message(&*panic)),
    }
}

impl Session {
    /// Opens `config`'s source: checks it, loads the models and starts the threads.
    pub fn open(config: Config) -> Result<Session, Error> {
        if !(config.m.is_finite() && (1.0..=10.0).contains(&config.m)) {
            return Err(Error::new(PS_ERR_ARGUMENT, "M must be 1 to 10 s"));
        }
        if !((8_000.0..=384_000.0).contains(&config.output_rate)
            && config.output_rate.fract() == 0.0)
        {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "the output rate must be a whole 8 to 384 kHz",
            ));
        }
        // (A file is checked first, its errors being the likeliest; an app is tapped, so muted,
        // once the rest is ready.)
        let source = match config.source {
            Source::File(path) => Opening::File(crate::file::File::open(path)?),
            Source::System { pid, taps } => Opening::System(pid, taps),
        };
        let models = Tensors::load(&config.models)
            .map_err(|e| Error::new(PS_ERR_MODELS, format!("{}: {e}", config.models.display())))?;
        let delayed = DelayedParams {
            m: config.m,
            stamps: Stamps::EveryGroup,
            ..DelayedParams::chosen()
        };
        let params = Params {
            hindsight: HindsightParams::live(),
            listening: Listening::Delayed(delayed),
            ..Params::default()
        };
        let analyzer = Analyzer::with_hindsight_worker(&models, params)
            .map_err(|e| Error::new(PS_ERR_MODELS, e.to_string()))?;
        let recorder = config
            .session_dir
            .as_deref()
            .map(Recorder::create)
            .transpose()
            .map_err(|e| Error::new(PS_ERR_SOURCE, e))?;
        let (entries, records) = rtrb::RingBuffer::new(RECORDS);
        let analysis = |shared: &Arc<Shared>, block, paced| Analysis {
            shared: Arc::clone(shared),
            analyzer,
            entries,
            recorder,
            delayed,
            block: Vec::with_capacity(block),
            size: block,
            paced,
            read: 0,
            out: Vec::new(),
            records: Vec::new(),
        };
        let (line, shared, threads) = match source {
            Opening::File(file) => {
                let (writer, line) = crate::file::File::line(config.m, config.output_rate);
                let shared = Arc::new(Shared::new(
                    config.m,
                    config.output_rate,
                    config.output_rate,
                    crate::file::START_S,
                ));
                let a = analysis(&shared, crate::file::BLOCK, true);
                let threads = file.start(&shared, writer, a)?;
                (line, shared, threads)
            }
            Opening::System(pid, taps) => {
                let mut system = crate::system::System::open(pid, taps, config.m)?;
                let line = system.take_line();
                let shared = Arc::new(Shared::new(
                    config.m,
                    config.output_rate,
                    line.rate(),
                    config.m,
                ));
                let a = analysis(&shared, crate::system::BLOCK, false);
                let threads = system.start(&shared, a)?;
                (line, shared, threads)
            }
        };
        Ok(Session {
            render: UnsafeCell::new(Render {
                line,
                follower: Follower::new(config.output_rate),
                frames: 0,
                started: false,
            }),
            records: UnsafeCell::new(records),
            shared,
            threads,
        })
    }

    /// Copies new records into `out`; returns how many, or the failure once none are left. A
    /// call is the main thread's heartbeat.
    pub fn poll(&self, out: &mut [Record]) -> Result<usize, Error> {
        self.shared.beat();
        // SAFETY: only `ps_poll` uses the records, from one thread at a time.
        let records = unsafe { &mut *self.records.get() };
        let n = records.slots().min(out.len());
        if n > 0 {
            let chunk = records.read_chunk(n).expect("n records are ready");
            let (a, b) = chunk.as_slices();
            out[..a.len()].copy_from_slice(a);
            out[a.len()..n].copy_from_slice(b);
            chunk.commit_all();
            return Ok(n);
        }
        match self.shared.failed() {
            Some(e) => Err(Error::new(PS_ERR_FAILED, e)),
            None => Ok(0),
        }
    }

    /// Writes one output buffer of `channels` interleaved channels and records its anchor.
    pub fn render(&self, out: &mut [f32], channels: usize, dsp: f64) {
        // SAFETY: only `ps_render_music` uses the render state, from one thread at a time.
        let r = unsafe { &mut *self.render.get() };
        let sh = &*self.shared;
        let rate = sh.output_rate;
        r.line.pull();
        // (The source's end, in output frames, once it is known.)
        let total = sh.total.load(Ordering::Acquire);
        let end = (total != u64::MAX).then(|| music_frame(total, rate));
        let end_s = end.map_or(f64::INFINITY, |e| e as f64 / rate);
        if !r.started {
            if (r.line.end() as f64 / sh.line_rate) < sh.start_s.min(end_s) {
                out.fill(0.0);
                return;
            }
            r.started = true;
            sh.state.store(PS_PLAYING, Ordering::Release);
        }
        sh.anchor.write(dsp, r.frames as f64 / rate, r.frames);
        let resume = sh.resume.load(Ordering::Acquire) as f64;
        let step = sh.line_rate / rate;
        let mut gain = None;
        let mut stalled = false;
        for (k, frame) in out.chunks_exact_mut(channels).enumerate() {
            let n = r.frames + k as u64;
            let x = n as f64 * step;
            let (left, right) = if x < resume {
                (0.0, 0.0)
            } else {
                match r.line.frame(x) {
                    Read::Frame(left, right) => (left, right),
                    // (The source's last frame has nothing after it to interpolate to.)
                    Read::After if end.is_none_or(|e| n + 1 < e) => {
                        stalled = true;
                        (0.0, 0.0)
                    }
                    Read::After | Read::Before => (0.0, 0.0),
                }
            };
            if channels == 1 {
                frame[0] = f32::midpoint(left, right);
            } else {
                frame[0] = left;
                frame[1] = right;
                frame[2..].fill(0.0);
            }
            if let Some(g) = r.follower.push(&[f32::midpoint(left, right)]) {
                gain = Some(g);
            }
        }
        r.frames += (out.len() / channels) as u64;
        let song = r.frames as f64 / rate;
        sh.song.store(song);
        if let Some(g) = gain {
            sh.gain.store(g.to_bits(), Ordering::Relaxed);
        }
        if stalled {
            sh.stalls.fetch_add(1, Ordering::Relaxed);
        }
        if song >= end_s {
            sh.state.store(PS_ENDED, Ordering::Release);
        }
    }

    /// The anchor, latency, progress and the rest. A call is the main thread's heartbeat.
    pub fn status(&self) -> Status {
        let sh = &*self.shared;
        sh.beat();
        let (anchor_dsp, anchor_song, anchor_frames) = sh.anchor.read();
        let song = sh.song.load();
        let state = match sh.state.load(Ordering::Acquire) {
            PS_ENDED => PS_ENDED,
            _ if sh.resume.load(Ordering::Acquire) as f64 > song * sh.line_rate => PS_STOPPED,
            s => s,
        };
        Status {
            anchor_dsp,
            anchor_song,
            anchor_frames: anchor_frames as i64,
            output_latency: sh.output_latency.load(),
            buffered: sh.written.load(Ordering::Acquire) as f64 / sh.line_rate - song,
            analysed: sh.analysed.load(Ordering::Acquire) as f64 / SR,
            stalls: sh.stalls.load(Ordering::Relaxed) as i32,
            state,
            muted: i32::from(sh.muted.load(Ordering::Acquire)),
            analysis_done: i32::from(sh.analysis_done.load(Ordering::Acquire)),
            failed: i32::from(sh.failed().is_some()),
            follower_gain: f32::from_bits(sh.gain.load(Ordering::Relaxed)),
            releases: sh.releases.load(Ordering::Acquire) as i32,
            device_changed: i32::from(sh.device_changed.load(Ordering::Relaxed)),
        }
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        self.shared.stop.store(true, Ordering::Relaxed);
        for t in self.threads.drain(..) {
            let _ = t.join();
        }
    }
}

/// A source checked (a file) or ready to tap (an app).
enum Opening {
    File(crate::file::File),
    System(i32, Box<dyn Taps>),
}

/// The analysis: the source's samples (44.1 kHz) through the analyzer in blocks, its entries as
/// records into the ring (for `ps_poll`) and the record.
pub(crate) struct Analysis {
    shared: Arc<Shared>,
    analyzer: Analyzer,
    entries: rtrb::Producer<Record>,
    recorder: Option<Recorder>,
    delayed: DelayedParams,
    /// The samples waiting for a whole block, and its size.
    block: Vec<f32>,
    size: usize,
    /// Each block waits until it is at most `M` past the render position (a File source, read
    /// faster than real time); otherwise it is analysed as it arrives.
    paced: bool,
    /// The samples analysed.
    read: u64,
    out: Vec<analysis::analyzer::Entry>,
    records: Vec<Record>,
}

impl Analysis {
    /// Analyses `samples`, the ones after the last call's, a block at a time; false once the
    /// session stopped.
    pub(crate) fn push(&mut self, samples: impl IntoIterator<Item = f32>) -> Result<bool, String> {
        for x in samples {
            self.block.push(x);
            if self.block.len() == self.size && !self.feed()? {
                return Ok(false);
            }
        }
        Ok(true)
    }

    /// Analyses the last part of a block and the stream's end; false if the session stopped
    /// first.
    pub(crate) fn finish(&mut self) -> Result<bool, String> {
        if !self.block.is_empty() && !self.feed()? {
            return Ok(false);
        }
        self.out.clear();
        self.analyzer.finish(&mut self.out);
        if !self.deliver()? {
            return Ok(false);
        }
        self.shared.analysis_done.store(true, Ordering::Release);
        Ok(true)
    }

    /// Feeds the block (once the render position allows it, when paced) and delivers its
    /// entries; false if the session stopped.
    fn feed(&mut self) -> Result<bool, String> {
        let end = self.read + self.block.len() as u64;
        if self.paced && !self.shared.wait_for_render(end as f64 / SR) {
            return Ok(false);
        }
        self.out.clear();
        self.analyzer.push(&self.block, &mut self.out);
        if let Some(r) = self.recorder.as_mut() {
            r.audio(&self.block)?;
        }
        self.read = end;
        self.block.clear();
        self.shared.analysed.store(end, Ordering::Release);
        self.deliver()
    }

    /// The entries in `out` as records, into the ring (waiting for room) and the record.
    fn deliver(&mut self) -> Result<bool, String> {
        self.records.clear();
        for e in &self.out {
            flat::push(e, &mut self.records);
        }
        if let Some(r) = self.recorder.as_mut() {
            r.entries(&self.records)?;
        }
        for &record in &self.records {
            while self.entries.push(record).is_err() {
                if self.shared.stopped() {
                    return Ok(false);
                }
                std::thread::sleep(NAP);
            }
        }
        Ok(true)
    }

    /// Ends the analysis: records `result` (whether it reached the source's end, or how it
    /// failed) and finishes the record, describing the `source` and the watchdog's `releases`.
    pub(crate) fn close(
        mut self,
        result: std::thread::Result<Result<bool, String>>,
        source: &str,
        releases: &[Release],
    ) {
        let sh = Arc::clone(&self.shared);
        let complete = match result {
            Ok(Ok(complete)) => complete,
            Ok(Err(e)) => {
                sh.fail(e);
                false
            }
            Err(panic) => {
                sh.fail(crate::panic_message(&*panic));
                false
            }
        };
        if let Some(recorder) = self.recorder.take() {
            let meta = Meta {
                source,
                m: self.delayed.m,
                a: self.delayed.a,
                output_rate: sh.output_rate,
                hindsight_stalls: self.analyzer.hindsight_stalls().0,
                complete,
                releases,
            };
            if let Err(e) = recorder.finish(&meta) {
                sh.fail(e);
            }
        }
    }
}
