//! Key press times from the window server: `NSEvent.timestamp` (host seconds, as `now_s`), taken
//! by a local event monitor beside winit, which drops it (docs/design/native-app.md, "One clock").
//! winit's key event is matched to the monitor's latest event of the same kind.

use std::collections::VecDeque;
use std::ptr::NonNull;
use std::sync::{Arc, Mutex};

use block2::RcBlock;
use objc2::rc::Retained;
use objc2::runtime::AnyObject;
use objc2_app_kit::{NSEvent, NSEventMask, NSEventType};

/// The monitor and the press and release times it has seen, newest last.
pub struct KeyTimes {
    seen: Arc<Mutex<VecDeque<(f64, bool)>>>,
    _monitor: Option<Retained<AnyObject>>,
}

impl KeyTimes {
    /// Installs the monitor (on the main thread, once the application exists).
    pub fn install() -> KeyTimes {
        let seen: Arc<Mutex<VecDeque<(f64, bool)>>> = Arc::default();
        let log = Arc::clone(&seen);
        let block = RcBlock::new(move |ev: NonNull<NSEvent>| -> *mut NSEvent {
            // SAFETY: AppKit hands the monitor a valid event for the duration of the call.
            let e = unsafe { ev.as_ref() };
            let down = e.r#type() == NSEventType::KeyDown;
            if !(down && e.isARepeat()) {
                let mut s = log.lock().expect("key times");
                s.push_back((e.timestamp(), down));
                while s.len() > 32 {
                    s.pop_front();
                }
            }
            ev.as_ptr()
        });
        // SAFETY: the handler returns the event it was given, as AppKit requires.
        let monitor = unsafe {
            NSEvent::addLocalMonitorForEventsMatchingMask_handler(
                NSEventMask::KeyDown | NSEventMask::KeyUp,
                &block,
            )
        };
        KeyTimes {
            seen,
            _monitor: monitor,
        }
    }

    /// When the press (`down`) or release winit just delivered happened: the monitor's latest
    /// event of that kind within the last quarter second (taken), else `now`.
    pub fn take(&self, down: bool, now: f64) -> f64 {
        let mut s = self.seen.lock().expect("key times");
        match s
            .iter()
            .rposition(|&(t, d)| d == down && now - t < 0.25 && t <= now + 1e-3)
        {
            Some(i) => s.remove(i).map_or(now, |(t, _)| t),
            None => now,
        }
    }
}
