//! Delayed listening's watchdog (docs/design/native-app.md, "Capture and mute"): a thread that
//! destroys the tap, which unmutes the tapped app, when the capture has stopped for 0.5 s (the
//! delay line would underrun) or the main thread hasn't checked in for 2 s. The app then falls
//! back to Direct listening, ends the run and logs why; so a hang never leaves the app muted for
//! more than about 2 s. (Destroying the aggregate and tap mid-stall works from another thread:
//! ES, hv-t8o8.36.)

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU64, Ordering};
use std::thread::JoinHandle;
use std::time::Duration;

use super::coreaudio::{Id, now_s};
use super::tap;

/// The capture may stop this long, and the main thread go this long without a heartbeat (s).
pub const CAPTURE_STALL_S: f64 = 0.5;
pub const MAIN_STALL_S: f64 = 2.0;
/// The capture's first buffer may take this long (s).
const FIRST_CAPTURE_S: f64 = 2.0;
/// How often it looks (s).
const POLL_S: f64 = 0.05;

/// Why it tripped.
const NONE: u8 = 0;
const CAPTURE: u8 = 1;
const MAIN: u8 = 2;

struct Shared {
    heartbeat: AtomicU64,
    stop: AtomicBool,
    why: AtomicU8,
    /// How long after the last heartbeat, or the last buffer, it tripped (s, an f64's bits).
    after: AtomicU64,
}

pub struct Watchdog {
    shared: Arc<Shared>,
    handle: Option<JoinHandle<()>>,
}

impl Watchdog {
    /// Watches the tap `tap` and its aggregate (`destroyed` set once they are gone), whose
    /// capture stores each buffer's host time in `alive`.
    pub fn start(
        tap_id: Id,
        aggregate: Id,
        destroyed: Arc<AtomicBool>,
        alive: Arc<AtomicU64>,
    ) -> Watchdog {
        let started = now_s();
        let shared = Arc::new(Shared {
            heartbeat: AtomicU64::new(started.to_bits()),
            stop: AtomicBool::new(false),
            why: AtomicU8::new(NONE),
            after: AtomicU64::new(0),
        });
        let s = Arc::clone(&shared);
        let handle = std::thread::Builder::new()
            .name("watchdog".into())
            .spawn(move || {
                while !s.stop.load(Ordering::Relaxed) && !destroyed.load(Ordering::SeqCst) {
                    let now = now_s();
                    let why = trip(
                        now,
                        started,
                        f64::from_bits(s.heartbeat.load(Ordering::Relaxed)),
                        f64::from_bits(alive.load(Ordering::Relaxed)),
                    );
                    if why != NONE {
                        tap::destroy(tap_id, aggregate, &destroyed);
                        let since = if why == MAIN {
                            f64::from_bits(s.heartbeat.load(Ordering::Relaxed))
                        } else {
                            f64::from_bits(alive.load(Ordering::Relaxed)).max(started)
                        };
                        s.after.store((now_s() - since).to_bits(), Ordering::SeqCst);
                        s.why.store(why, Ordering::SeqCst);
                        break;
                    }
                    std::thread::sleep(Duration::from_secs_f64(POLL_S));
                }
            })
            .expect("a watchdog thread");
        Watchdog {
            shared,
            handle: Some(handle),
        }
    }

    /// The main thread checks in (host time `now`).
    pub fn beat(&self, now: f64) {
        self.shared
            .heartbeat
            .store(now.to_bits(), Ordering::Relaxed);
    }

    /// How long after the stall began it destroyed the tap (s), once it has.
    pub fn after(&self) -> f64 {
        f64::from_bits(self.shared.after.load(Ordering::SeqCst))
    }

    /// Why it destroyed the tap, once it has.
    pub fn tripped(&self) -> Option<&'static str> {
        match self.shared.why.load(Ordering::SeqCst) {
            CAPTURE => Some("the capture stopped for 0.5 s"),
            MAIN => Some("the main thread stalled for 2 s"),
            _ => None,
        }
    }
}

/// Whether to trip at host time `now`, watching since `started`, the main thread last seen at
/// `heartbeat` and the latest buffer captured at `alive` (0: none yet).
fn trip(now: f64, started: f64, heartbeat: f64, alive: f64) -> u8 {
    if now - heartbeat > MAIN_STALL_S {
        MAIN
    } else if (alive == 0.0 && now - started > FIRST_CAPTURE_S)
        || (alive > 0.0 && now - alive > CAPTURE_STALL_S)
    {
        CAPTURE
    } else {
        NONE
    }
}

impl Drop for Watchdog {
    fn drop(&mut self) {
        self.shared.stop.store(true, Ordering::Relaxed);
        if let Some(h) = self.handle.take()
            && let Err(panic) = h.join()
        {
            std::panic::resume_unwind(panic);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn silent_buffers_do_not_look_like_a_capture_stall() {
        // tap::io_proc updates alive for every nonempty captured buffer, before any
        // evidence gate or peak test. Its samples can all be zero during this gap.
        for frame in 1..=8 * 120 {
            let now = 100.0 + f64::from(frame) / 120.0;
            assert_eq!(trip(now, 100.0, now - 0.001, now - 0.005), NONE);
        }
    }

    #[test]
    fn it_trips_on_a_stalled_main_thread_or_capture_and_not_before() {
        // (Started at 100 s; a capture every buffer, the main thread every frame.)
        assert_eq!(trip(101.0, 100.0, 100.99, 100.995), NONE);
        assert_eq!(trip(102.1, 100.0, 100.0, 102.09), MAIN);
        assert_eq!(trip(101.0, 100.0, 100.99, 100.4), CAPTURE);
        // The first buffer gets 2 s.
        assert_eq!(trip(101.5, 100.0, 101.49, 0.0), NONE);
        assert_eq!(trip(102.1, 100.0, 102.09, 0.0), CAPTURE);
    }
}
