//! One source's session (docs/design/unity-game.md, "Sources" and "The delay line and Unity's
//! output"). A File source's threads:
//!
//! - **decoder**: decodes each track (`decode`), a track or two ahead, and hands its mono
//!   samples to the analyzer and its stereo frames to the player.
//! - **player**: writes the stereo frames, after each track's gap of silence, into native's
//!   delay line (`audio::delay`), keeping it filled to `M` past the render position. A frame's
//!   "capture time" is its song time, so the line plays song time `t` at render time `t`.
//! - **analyzer**: feeds the mono samples to native's analyzer (with its hindsight worker) in
//!   Delayed listening, every group stamped (`Stamps::EveryGroup`), up to `M` past the render
//!   position: faster than real time at first, so the music starts at once with `M` of lookahead.
//!   Its entries become `ps_entry` records (`analysis::flat`) in a ring that `ps_poll` drains, and
//!   the session's record (`record`).
//!
//! `ps_render_music` (Unity's audio thread) pulls the line and reads each output frame at its song
//! time, linearly interpolated from 44.1 kHz to the output rate. It starts the music once the
//! line holds [`START_S`] (or the whole source), and from then on song time advances by exactly
//! the frames rendered: a buffer the line can't fill plays silence there and counts as a stall.
//! It records each buffer's anchor (its DSP time, song time and frame) under a sequence lock. It
//! takes no lock, allocates nothing and never waits for the other threads.

use std::cell::UnsafeCell;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU32, AtomicU64, Ordering, fence};
use std::sync::mpsc::{Receiver, sync_channel};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

use analysis::analyzer::{Analyzer, Params};
use analysis::delayed::{DelayedParams, Listening, Stamps};
use analysis::flat::{self, Record};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use audio::delay::{DelayLine, Read, Writer, delay_line};
use audio::follow::Follower;

use crate::decode::{self, SR};
use crate::record::{Meta, Recorder};
use crate::{
    Error, PS_ENDED, PS_ERR_ARGUMENT, PS_ERR_FAILED, PS_ERR_MODELS, PS_ERR_SOURCE, PS_PLAYING,
    PS_WAITING, Status,
};

/// How much music the line holds before it starts playing (s), unless the source is shorter.
pub const START_S: f64 = 0.25;
/// The analyzer's input blocks, as `ps entries` feeds it, so the entries match its dump.
const BLOCK: usize = 4096;
/// The frames the player writes at a time.
const CHUNK: usize = 1024;
/// The records the ring holds for `ps_poll` (about a minute and a half of them).
const RECORDS: usize = 1 << 14;
/// How long a thread waiting for the render position or for room sleeps between looks.
const NAP: Duration = Duration::from_millis(1);

/// A session's configuration, from `ps_config`.
pub struct Config {
    pub source: PathBuf,
    pub m: f64,
    pub output_rate: f64,
    pub models: PathBuf,
    pub session_dir: Option<PathBuf>,
}

/// An `f64` shared through its bits.
struct AtomicF64(AtomicU64);

impl AtomicF64 {
    fn new(x: f64) -> AtomicF64 {
        AtomicF64(AtomicU64::new(x.to_bits()))
    }
    fn load(&self) -> f64 {
        f64::from_bits(self.0.load(Ordering::Acquire))
    }
    fn store(&self, x: f64) {
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
struct Shared {
    m: f64,
    output_rate: f64,
    output_latency: f64,
    stop: AtomicBool,
    /// The render position: the song time of the next buffer's first frame.
    song: AtomicF64,
    anchor: Anchor,
    state: AtomicI32,
    stalls: AtomicU32,
    gain: AtomicU32,
    /// The source's length in frames, once every track is decoded (`u64::MAX` until then).
    total: AtomicU64,
    /// The frames the player has written into the line, and the samples the analyzer has read.
    written: AtomicU64,
    analysed: AtomicU64,
    analysis_done: AtomicBool,
    /// The first failure of a thread: the source, the analysis or the record.
    error: Mutex<Option<String>>,
}

impl Shared {
    fn fail(&self, message: String) {
        let mut e = self
            .error
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        e.get_or_insert(message);
    }

    fn failed(&self) -> Option<String> {
        self.error
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    fn stopped(&self) -> bool {
        self.stop.load(Ordering::Relaxed)
    }

    /// Waits until song position `until` (s) is at most `M` past the render position; false if
    /// the session stopped first.
    fn wait_for_render(&self, until: f64) -> bool {
        while until > self.song.load() + self.m {
            if self.stopped() {
                return false;
            }
            std::thread::sleep(NAP);
        }
        !self.stopped()
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

/// A track for the player: its gap and stereo frames; for the analyzer: its gap and mono samples.
struct Part {
    gap: usize,
    samples: Vec<f32>,
}

impl Session {
    /// Opens `config`'s File source: checks it, loads the models and starts the threads.
    pub fn open(config: &Config) -> Result<Session, Error> {
        if !(config.m.is_finite() && (1.0..=10.0).contains(&config.m)) {
            return Err(Error::new(PS_ERR_ARGUMENT, "M must be 1 to 10 s"));
        }
        if !(config.output_rate.is_finite() && (8_000.0..=384_000.0).contains(&config.output_rate))
        {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "the output rate must be 8 to 384 kHz",
            ));
        }
        let tracks = decode::tracks(&config.source).map_err(|e| Error::new(PS_ERR_SOURCE, e))?;
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

        let shared = Arc::new(Shared {
            m: config.m,
            output_rate: config.output_rate,
            output_latency: crate::latency::output_latency(),
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
            error: Mutex::new(None),
        });
        // (The line keeps a second more than the player writes ahead of the render position.)
        let (writer, line) = delay_line(SR, 0.0, config.m + 1.0);
        let (entries, records) = rtrb::RingBuffer::new(RECORDS);
        let (to_player, player_parts) = sync_channel(1);
        let (to_analyzer, analyzer_parts) = sync_channel(1);

        let spawn = |name: &str, f: Box<dyn FnOnce() + Send>| {
            std::thread::Builder::new()
                .name(name.into())
                .spawn(f)
                .map_err(|e| Error::new(PS_ERR_SOURCE, format!("can't start a thread: {e}")))
        };
        let mut threads = Vec::new();
        let s = shared.clone();
        threads.push(spawn(
            "ps-decoder",
            Box::new(move || {
                guarded(&s.clone(), move || {
                    let mut total = 0u64;
                    for track in &tracks {
                        let decoded = decode::decode(&track.path)?;
                        let gap = decode::gap_frames(track);
                        total += (gap + decoded.mono.len()) as u64;
                        let sent = to_player
                            .send(Part {
                                gap,
                                samples: decoded.stereo,
                            })
                            .is_ok()
                            && to_analyzer
                                .send(Part {
                                    gap,
                                    samples: decoded.mono,
                                })
                                .is_ok();
                        if !sent {
                            return Ok(());
                        }
                    }
                    s.total.store(total, Ordering::Release);
                    Ok(())
                });
            }),
        )?);
        let s = shared.clone();
        threads.push(spawn(
            "ps-player",
            Box::new(move || {
                guarded(&s.clone(), move || {
                    play(&s, player_parts, writer);
                    Ok(())
                });
            }),
        )?);
        let s = shared.clone();
        let source = config.source.clone();
        threads.push(spawn(
            "ps-analyzer",
            Box::new(move || {
                let mut work = Analysis {
                    shared: s.clone(),
                    analyzer,
                    entries,
                    recorder,
                    out: Vec::new(),
                    records: Vec::new(),
                };
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    work.run(analyzer_parts)
                }));
                let complete = match result {
                    Ok(Ok(complete)) => complete,
                    Ok(Err(e)) => {
                        s.fail(e);
                        false
                    }
                    Err(panic) => {
                        s.fail(crate::panic_message(&*panic));
                        false
                    }
                };
                if let Some(recorder) = work.recorder.take() {
                    let meta = Meta {
                        source: &source,
                        m: delayed.m,
                        a: delayed.a,
                        output_rate: s.output_rate,
                        hindsight_stalls: work.analyzer.hindsight_stalls().0,
                        complete,
                    };
                    if let Err(e) = recorder.finish(&meta) {
                        s.fail(e);
                    }
                }
            }),
        )?);

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

    /// Copies new records into `out`; returns how many, or the failure once none are left.
    pub fn poll(&self, out: &mut [Record]) -> Result<usize, Error> {
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
        r.line.pull();
        let total = sh.total.load(Ordering::Acquire);
        let end = if total == u64::MAX {
            f64::INFINITY
        } else {
            total as f64 / SR
        };
        if !r.started {
            let ready = r.line.fill(0.0).is_some_and(|f| f >= START_S.min(end));
            if !ready {
                out.fill(0.0);
                return;
            }
            r.started = true;
            sh.state.store(PS_PLAYING, Ordering::Release);
        }
        let rate = sh.output_rate;
        sh.anchor.write(dsp, r.frames as f64 / rate, r.frames);
        let mut gain = None;
        let mut stalled = false;
        for (k, frame) in out.chunks_exact_mut(channels).enumerate() {
            let t = (r.frames + k as u64) as f64 / rate;
            let (left, right) = match r.line.at(t) {
                Read::Frame(left, right) => (left, right),
                // (The source's last frame has nothing after it to interpolate to.)
                Read::After if t * SR + 1.0 < total as f64 => {
                    stalled = true;
                    (0.0, 0.0)
                }
                Read::After | Read::Before => (0.0, 0.0),
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
        if song >= end {
            sh.state.store(PS_ENDED, Ordering::Release);
        }
    }

    pub fn status(&self) -> Status {
        let sh = &*self.shared;
        let (anchor_dsp, anchor_song, anchor_frames) = sh.anchor.read();
        let song = sh.song.load();
        Status {
            anchor_dsp,
            anchor_song,
            anchor_frames: anchor_frames as i64,
            output_latency: sh.output_latency,
            buffered: sh.written.load(Ordering::Acquire) as f64 / SR - song,
            analysed: sh.analysed.load(Ordering::Acquire) as f64 / SR,
            stalls: sh.stalls.load(Ordering::Relaxed) as i32,
            state: sh.state.load(Ordering::Acquire),
            muted: 0,
            analysis_done: i32::from(sh.analysis_done.load(Ordering::Acquire)),
            failed: i32::from(sh.failed().is_some()),
            follower_gain: f32::from_bits(sh.gain.load(Ordering::Relaxed)),
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

/// Runs a thread's work, recording its error or panic as the session's failure.
fn guarded(shared: &Shared, f: impl FnOnce() -> Result<(), String>) {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(f)) {
        Ok(Ok(())) => {}
        Ok(Err(e)) => shared.fail(e),
        Err(panic) => shared.fail(crate::panic_message(&*panic)),
    }
}

/// The player: each track's gap and stereo frames into the delay line, at most `M` past the
/// render position.
fn play(sh: &Shared, parts: Receiver<Part>, mut writer: Writer) {
    let mut frame = 0u64;
    let silence = [0.0f32; 2 * CHUNK];
    let mut write = |samples: &[f32]| {
        for chunk in samples.chunks(2 * CHUNK) {
            let frames = chunk.len() / 2;
            loop {
                if !sh.wait_for_render((frame + frames as u64) as f64 / SR) {
                    return false;
                }
                let written = writer.write(frame, frames, frame as f64 / SR, |a, b| {
                    a.copy_from_slice(&chunk[..a.len()]);
                    b.copy_from_slice(&chunk[a.len()..]);
                });
                if written {
                    break;
                }
                // (The line is full until the render thread pulls it.)
                std::thread::sleep(NAP);
            }
            frame += frames as u64;
            sh.written.store(frame, Ordering::Release);
        }
        true
    };
    for part in parts {
        let mut gap = part.gap;
        while gap > 0 {
            let n = gap.min(CHUNK);
            if !write(&silence[..2 * n]) {
                return;
            }
            gap -= n;
        }
        if !write(&part.samples) {
            return;
        }
    }
}

/// The analyzer's thread.
struct Analysis {
    shared: Arc<Shared>,
    analyzer: Analyzer,
    entries: rtrb::Producer<Record>,
    recorder: Option<Recorder>,
    out: Vec<analysis::analyzer::Entry>,
    records: Vec<Record>,
}

impl Analysis {
    /// Analyses every track in `BLOCK`s, then the stream's end; returns whether it got there
    /// (false when the session stopped first or the source failed).
    fn run(&mut self, parts: Receiver<Part>) -> Result<bool, String> {
        let mut block = Vec::with_capacity(BLOCK);
        let mut read = 0u64;
        for part in parts {
            let gap = std::iter::repeat_n(0.0, part.gap);
            for x in gap.chain(part.samples.iter().copied()) {
                block.push(x);
                if block.len() == BLOCK {
                    if !self.feed(&block, read)? {
                        return Ok(false);
                    }
                    read += BLOCK as u64;
                    block.clear();
                }
            }
        }
        if self.shared.failed().is_some() || self.shared.stopped() {
            return Ok(false);
        }
        if !block.is_empty() && !self.feed(&block, read)? {
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

    /// Feeds one block (from sample `read`) once the render position allows it, and delivers
    /// its entries; false if the session stopped.
    fn feed(&mut self, block: &[f32], read: u64) -> Result<bool, String> {
        let end = read + block.len() as u64;
        if !self.shared.wait_for_render(end as f64 / SR) {
            return Ok(false);
        }
        self.out.clear();
        self.analyzer.push(block, &mut self.out);
        if let Some(r) = self.recorder.as_mut() {
            r.audio(block)?;
        }
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
}
