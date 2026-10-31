//! A System source, Delayed listening on an app (docs/design/unity-game.md, "Sources" and
//! "Platform capture and crash safety"): the app the player picked is tapped and muted, its stereo
//! captured into the delay line and played `M` late, its mono mixdown analysed as it arrives.
//!
//! The source's timeline is the tap's frames at its rate, from the first tap's first frame: song
//! time 0. The line starts playing once it holds `M`, so the analyzer reads `M` ahead of what is
//! heard. Its one thread, **ps-system**, owns the tap:
//!
//! - It drains the tap's mono samples, lines them up with their buffers on the timeline (silence
//!   for frames the tap dropped), resamples them to 44.1 kHz and feeds the session's
//!   [`Analysis`] in blocks of two analyzer hops (20 ms, inside the capture-to-arrival budget `A`).
//! - It relays the main thread's heartbeat (each `ps_poll` and `ps_status`) to the tap's watchdog
//!   (`capture::watchdog`), which releases the tap, so the app is heard again, if the capture stops
//!   for 0.5 s or the heartbeat for 2 s.
//! - **After a release** the music stops: the render plays silence from there, and the analyzer
//!   hears silence up to `M` past the render position, so its timeline keeps up with song time.
//!   Once the main thread is alive again and the app produces audio, it taps the app again into the
//!   same line, continuing the timeline where the analyzer's silence reached; the music resumes
//!   there, `M` later. macOS releases the tap's mute itself if the process dies.

use std::sync::Arc;
use std::sync::atomic::Ordering;
use std::thread::JoinHandle;

use audio::delay::{DelayLine, Writer};
use capture::Meta;
use capture::analysis::Resampler;
use capture::apps::AudioApp;
use capture::clock::now_s;
use capture::watchdog::MAIN_STALL_S;

use crate::record::Release;
use crate::session::{Analysis, NAP, Shared, spawn};
use crate::{Error, PS_ERR_ARGUMENT, PS_ERR_SOURCE};

/// The analyzer's input blocks: two of its 441-sample hops, so a recorded session's audio
/// analysed in any whole number of hops gives the same entries.
pub(crate) const BLOCK: usize = 2 * 441;
/// After a release, the app is looked for this long later, then this often (s).
const RETAP_AFTER_S: f64 = 1.0;
const LOOK_S: f64 = 0.25;
/// The silence the analyzer hears at a time while the music is stopped (frames).
const SILENCE: usize = 1024;

/// What the System source taps: the platform's app taps, or a test's.
pub trait Taps: Send {
    /// App `pid` (the process responsible for its audio, as `ps_list_apps` lists it, or any one
    /// process with audio), with its processes to tap and whether it is producing audio now; None
    /// once it has none.
    fn app(&mut self, pid: i32) -> Option<AudioApp>;
    /// Taps `app` muted, into a new delay line at the tap's rate holding `keep` s.
    fn first(&mut self, app: &AudioApp, keep: f64) -> Result<(Box<dyn Tap>, DelayLine), String>;
    /// Taps `app` muted again, into the line's `writer` at `rate`, numbering its frames from
    /// `first`; on failure the writer comes back with the error.
    fn again(
        &mut self,
        app: &AudioApp,
        writer: Writer,
        rate: f64,
        first: u64,
    ) -> Result<Box<dyn Tap>, (String, Writer)>;
}

/// One tap of the app, with its watchdog.
pub trait Tap: Send {
    /// Everything captured since the last call: mono samples, and their buffers' [`Meta`]s
    /// (frames numbered on the source's timeline).
    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>);
    /// Relays the main thread's last heartbeat (host s) to the watchdog.
    fn beat(&self, heartbeat: f64);
    /// Why the watchdog released the tap (the app is heard again), once it has.
    fn released(&self) -> Option<&'static str>;
    /// Whether the app is muted: the tap is live.
    fn muted(&self) -> bool;
    /// Destroys the tap (the app is heard again) and hands back the line's writer.
    fn close(self: Box<Self>) -> Writer;
}

/// The platform's taps: Core Audio's process taps on the Mac.
#[cfg(target_os = "macos")]
pub fn platform() -> Box<dyn Taps> {
    Box::new(mac::Mac)
}

/// The apps producing audio now, the ones a System source can tap.
#[cfg(target_os = "macos")]
pub fn playing() -> Vec<AudioApp> {
    capture::apps::list()
        .into_iter()
        .filter(|a| a.playing && !a.processes.is_empty())
        .collect()
}

#[cfg(target_os = "macos")]
mod mac {
    use audio::delay::{DelayLine, Writer};
    use capture::Meta;
    use capture::apps::AudioApp;
    use capture::tap::Tap as CoreTap;
    use capture::watchdog::Watchdog;
    use std::sync::atomic::Ordering;

    use super::{Tap, Taps};

    pub(super) struct Mac;

    impl Taps for Mac {
        fn app(&mut self, pid: i32) -> Option<AudioApp> {
            // (An app as `ps_list_apps` lists it, else a process alone: a tool launched from a
            // terminal is grouped under the terminal. Never ours.)
            if pid == std::process::id() as i32 {
                return None;
            }
            capture::apps::list()
                .into_iter()
                .find(|a| a.pid == pid)
                .or_else(|| capture::apps::of_pid(pid))
        }

        fn first(
            &mut self,
            app: &AudioApp,
            keep: f64,
        ) -> Result<(Box<dyn Tap>, DelayLine), String> {
            // (The line is read by frame, so its delay is unused.)
            let (tap, line) = CoreTap::start_processes(&app.processes, 0.0, keep)?;
            Ok((Box::new(Watched::new(tap)), line))
        }

        fn again(
            &mut self,
            app: &AudioApp,
            writer: Writer,
            rate: f64,
            first: u64,
        ) -> Result<Box<dyn Tap>, (String, Writer)> {
            let tap = CoreTap::start_processes_into(&app.processes, writer, rate, first)?;
            Ok(Box::new(Watched::new(tap)))
        }
    }

    /// A process tap and its watchdog (dropped first, so it never destroys the tap as it goes).
    struct Watched {
        watchdog: Watchdog,
        tap: CoreTap,
    }

    impl Watched {
        fn new(tap: CoreTap) -> Watched {
            Watched {
                watchdog: tap.watchdog(),
                tap,
            }
        }
    }

    impl Tap for Watched {
        fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
            self.tap.drain(samples, metas);
        }

        fn beat(&self, heartbeat: f64) {
            self.watchdog.beat(heartbeat);
        }

        fn released(&self) -> Option<&'static str> {
            self.watchdog.tripped()
        }

        fn muted(&self) -> bool {
            !self.tap.destroyed.load(Ordering::SeqCst)
        }

        fn close(self: Box<Self>) -> Writer {
            let Watched { watchdog, tap } = *self;
            drop(watchdog);
            tap.into_writer()
                .expect("an app's tap captures into a line")
        }
    }
}

/// A System source, tapped.
pub(crate) struct System {
    pid: i32,
    taps: Box<dyn Taps>,
    app: AudioApp,
    tap: Box<dyn Tap>,
    line: Option<DelayLine>,
}

impl System {
    /// Taps app `pid` muted, into a line that holds a second more than `M`.
    pub(crate) fn open(pid: i32, mut taps: Box<dyn Taps>, m: f64) -> Result<System, Error> {
        if pid <= 0 {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "the System source needs an app's pid",
            ));
        }
        let app = taps
            .app(pid)
            .ok_or_else(|| Error::new(PS_ERR_SOURCE, format!("no app with pid {pid} has audio")))?;
        let (tap, line) = taps
            .first(&app, m + 1.0)
            .map_err(|e| Error::new(PS_ERR_SOURCE, format!("can't tap {}: {e}", app.name)))?;
        Ok(System {
            pid,
            taps,
            app,
            tap,
            line: Some(line),
        })
    }

    /// The delay line, for the render thread.
    pub(crate) fn take_line(&mut self) -> DelayLine {
        self.line.take().expect("the line is taken once")
    }

    /// Starts the source's thread.
    pub(crate) fn start(
        self,
        shared: &Arc<Shared>,
        mut analysis: Analysis,
    ) -> Result<Vec<JoinHandle<()>>, Error> {
        let rate = shared.line_rate;
        let mut run = Run {
            sh: Arc::clone(shared),
            pid: self.pid,
            taps: self.taps,
            app: self.app,
            tap: Some(self.tap),
            writer: None,
            rate,
            resampler: Resampler::new(rate),
            fed: 0,
            pending: Vec::new(),
            samples: Vec::new(),
            metas: Vec::new(),
            mono: Vec::new(),
            resampled: Vec::new(),
            released_at: 0.0,
            next_look: 0.0,
            releases: Vec::new(),
        };
        shared.muted.store(true, Ordering::Release);
        let thread = spawn("ps-system", move || {
            let result =
                std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| run.go(&mut analysis)));
            // (The app is heard again before the record is finished.)
            drop(run.tap.take());
            run.sh.muted.store(false, Ordering::Release);
            let source = format!("system: {} (pid {})", run.app.name, run.pid);
            analysis.close(result, &source, &run.releases);
        })?;
        Ok(vec![thread])
    }
}

/// The source's thread's state.
struct Run {
    sh: Arc<Shared>,
    pid: i32,
    taps: Box<dyn Taps>,
    app: AudioApp,
    /// The live tap, or (after a release, until the app is tapped again) the line's writer.
    tap: Option<Box<dyn Tap>>,
    writer: Option<Writer>,
    rate: f64,
    resampler: Resampler,
    /// The frames of the timeline the analyzer has been given.
    fed: u64,
    /// Samples drained ahead of their buffer's meta.
    pending: Vec<f32>,
    samples: Vec<f32>,
    metas: Vec<Meta>,
    mono: Vec<f32>,
    resampled: Vec<f32>,
    /// When the watchdog last released the tap, and when to look for the app next (host s).
    released_at: f64,
    next_look: f64,
    releases: Vec<Release>,
}

impl Run {
    /// Runs until the session stops; Ok(false), since a live source has no end.
    fn go(&mut self, analysis: &mut Analysis) -> Result<bool, String> {
        while !self.sh.stopped() {
            let busy = if self.tap.is_some() {
                self.listen(analysis)?
            } else {
                self.wait(analysis)?
            };
            match busy {
                None => break,
                Some(false) => std::thread::sleep(NAP),
                Some(true) => {}
            }
        }
        Ok(false)
    }

    /// The tap's new samples to the analyzer, or its release; whether there was any (None once
    /// the session stopped).
    fn listen(&mut self, analysis: &mut Analysis) -> Result<Option<bool>, String> {
        let tap = self.tap.as_mut().expect("a tap");
        tap.beat(self.sh.heartbeat.load());
        if let Some(why) = tap.released() {
            let tap = self.tap.take().expect("a tap");
            self.sh.resume.store(u64::MAX, Ordering::Release);
            self.writer = Some(tap.close());
            self.sh.muted.store(false, Ordering::Release);
            self.sh.releases.fetch_add(1, Ordering::Release);
            self.releases.push(Release {
                song: self.sh.song.load(),
                why,
            });
            self.released_at = now_s();
            self.next_look = self.released_at + RETAP_AFTER_S;
            return Ok(Some(true));
        }
        self.sh.muted.store(tap.muted(), Ordering::Release);
        self.samples.clear();
        self.metas.clear();
        tap.drain(&mut self.samples, &mut self.metas);
        self.pending.extend_from_slice(&self.samples);
        self.mono.clear();
        align(
            &mut self.pending,
            &self.metas,
            &mut self.fed,
            &mut self.mono,
        );
        if self.mono.is_empty() {
            return Ok(Some(false));
        }
        // (The tap wrote those frames into the line as it captured them.)
        self.sh.written.store(self.fed, Ordering::Release);
        self.resampled.clear();
        self.resampler.push(&self.mono, &mut self.resampled);
        let more = analysis.push(self.resampled.iter().copied())?;
        Ok(more.then_some(true))
    }

    /// While the music is stopped: silence to the analyzer up to `M` past the render position, and
    /// the app tapped again once it plays.
    fn wait(&mut self, analysis: &mut Analysis) -> Result<Option<bool>, String> {
        let target = ((self.sh.song.load() + self.sh.m) * self.rate) as u64;
        let busy = self.fed < target;
        let silence = [0.0f32; SILENCE];
        while self.fed < target {
            let n = ((target - self.fed) as usize).min(SILENCE);
            self.resampled.clear();
            self.resampler.push(&silence[..n], &mut self.resampled);
            if !analysis.push(self.resampled.iter().copied())? {
                return Ok(None);
            }
            self.fed += n as u64;
        }
        self.sh.written.store(self.fed, Ordering::Release);
        let now = now_s();
        if now < self.next_look || !alive(now, self.sh.heartbeat.load()) {
            return Ok(Some(busy));
        }
        self.next_look = now + LOOK_S;
        let Some(app) = self.taps.app(self.pid).filter(|a| a.playing) else {
            return Ok(Some(busy));
        };
        let writer = self
            .writer
            .take()
            .expect("the line's writer, while untapped");
        match self.taps.again(&app, writer, self.rate, self.fed) {
            Ok(tap) => {
                self.sh.muted.store(tap.muted(), Ordering::Release);
                self.sh.resume.store(self.fed, Ordering::Release);
                self.tap = Some(tap);
                self.app = app;
            }
            // (Tried again at the next look.)
            Err((_, writer)) => self.writer = Some(writer),
        }
        Ok(Some(true))
    }
}

/// Whether the main thread is alive at host time `now`, last seen at `heartbeat`: a tap now would
/// not be released at once.
fn alive(now: f64, heartbeat: f64) -> bool {
    now - heartbeat < MAIN_STALL_S
}

/// Lines drained samples up with their buffers on the timeline: each buffer's frames go to `out`
/// after silence for any frames dropped before it (the tap's ring was full), and `fed` advances
/// past them. `pending` keeps the samples whose buffer hasn't been drained yet.
fn align(pending: &mut Vec<f32>, metas: &[Meta], fed: &mut u64, out: &mut Vec<f32>) {
    let mut used = 0;
    for m in metas {
        if m.first > *fed {
            out.resize(out.len() + (m.first - *fed) as usize, 0.0);
            *fed = m.first;
        }
        let frames = (m.frames as usize).min(pending.len() - used);
        // (Frames before what was fed already, which a tap never sends, are skipped.)
        let skip = ((*fed - m.first) as usize).min(frames);
        out.extend_from_slice(&pending[used + skip..used + frames]);
        used += frames;
        *fed = (*fed).max(m.first + frames as u64);
    }
    pending.drain(..used);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn meta(first: u64, frames: u32) -> Meta {
        Meta {
            first,
            frames,
            ..Meta::default()
        }
    }

    #[test]
    fn dropped_frames_are_heard_as_silence_and_early_samples_wait_for_their_buffer() {
        let mut pending = vec![1.0, 2.0, 3.0];
        let (mut fed, mut out) = (0, Vec::new());
        align(&mut pending, &[meta(0, 2)], &mut fed, &mut out);
        assert_eq!((out.as_slice(), fed), (&[1.0, 2.0][..], 2));
        // (Frames 2-4 were dropped; sample 3.0 is the next buffer's, drained before its meta.)
        assert_eq!(pending, [3.0]);
        pending.extend([4.0, 5.0]);
        out.clear();
        align(&mut pending, &[meta(5, 3)], &mut fed, &mut out);
        assert_eq!(out, [0.0, 0.0, 0.0, 3.0, 4.0, 5.0]);
        assert_eq!((fed, pending.len()), (8, 0));
    }

    #[test]
    fn a_retap_continues_the_timeline_where_the_silence_reached() {
        // (The analyzer heard up to frame 9000; the new tap numbers its frames from there.)
        let mut pending = vec![0.5; 4];
        let (mut fed, mut out) = (9000, Vec::new());
        align(&mut pending, &[meta(9000, 4)], &mut fed, &mut out);
        assert_eq!((out.len(), fed), (4, 9004));
    }

    #[test]
    fn the_app_is_tapped_again_only_while_the_main_thread_is_alive() {
        assert!(alive(100.0, 99.0));
        assert!(!alive(100.0, 97.5));
    }
}
