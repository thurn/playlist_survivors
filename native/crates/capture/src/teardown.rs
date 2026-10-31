//! A live capture's teardown, once, from whichever thread comes first: the app's own end or the
//! watchdog's (docs/design/native-app.md, "Capture and mute").
//!
//! `L` is what the capture holds live, each part releasing itself when dropped, in field order
//! (on the Mac: the IOProc, then the aggregate device, then the tap, ps-0e3). It is dropped under
//! the lock, so a second caller returns only once the first has finished: after that, nothing
//! calls into the capture's context and its owner may free it.

use std::sync::{Mutex, MutexGuard, PoisonError};

pub struct Teardown<L>(Mutex<Option<L>>);

impl<L> Teardown<L> {
    pub fn new(live: L) -> Teardown<L> {
        Teardown(Mutex::new(Some(live)))
    }

    /// Tears the capture down, or waits until the caller already tearing it down has finished.
    pub fn release(&self) {
        let mut live = self.lock();
        // (Dropped here, under the lock: taking it out and dropping it after the guard would let
        // a second caller return while the first is still tearing down.)
        *live = None;
    }

    /// Whether it hasn't been torn down yet (waiting while it is being torn down).
    pub fn live(&self) -> bool {
        self.lock().is_some()
    }

    fn lock(&self) -> MutexGuard<'_, Option<L>> {
        self.0.lock().unwrap_or_else(PoisonError::into_inner)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Barrier};
    use std::time::Duration;

    type Log = Arc<Mutex<Vec<&'static str>>>;

    /// A part that takes a while to release, then logs it.
    struct Part(&'static str, Log);

    impl Drop for Part {
        fn drop(&mut self) {
            std::thread::sleep(Duration::from_millis(20));
            self.1.lock().unwrap().push(self.0);
        }
    }

    #[allow(dead_code)]
    struct Live {
        io: Part,
        aggregate: Part,
        tap: Part,
    }

    #[test]
    fn two_releases_at_once_drop_each_part_once_in_order_and_both_wait_for_it() {
        let log = Log::default();
        let part = |name| Part(name, Arc::clone(&log));
        let teardown = Arc::new(Teardown::new(Live {
            io: part("io"),
            aggregate: part("aggregate"),
            tap: part("tap"),
        }));
        assert!(teardown.live());
        let start = Arc::new(Barrier::new(2));
        let callers: Vec<_> = (0..2)
            .map(|_| {
                let (teardown, start, log) =
                    (Arc::clone(&teardown), Arc::clone(&start), Arc::clone(&log));
                std::thread::spawn(move || {
                    start.wait();
                    teardown.release();
                    // (Whichever came second has waited for the first to finish.)
                    log.lock().unwrap().clone()
                })
            })
            .collect();
        for c in callers {
            assert_eq!(c.join().unwrap(), ["io", "aggregate", "tap"]);
        }
        assert!(!teardown.live());
        teardown.release();
        assert_eq!(log.lock().unwrap().len(), 3);
    }
}
