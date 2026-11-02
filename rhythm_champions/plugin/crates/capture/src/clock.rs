//! The host clock: capture buffers' times ([`crate::Meta`]), the output's and the watchdog's.

/// A reading of the host clock, in the platform's units: mach ticks on the Mac (an
/// `AudioTimeStamp`'s `mHostTime`), nanoseconds elsewhere. Readings are well above zero (the
/// watchdog takes 0 for "no buffer yet").
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord)]
pub struct HostTime(pub u64);

impl HostTime {
    pub fn now() -> HostTime {
        HostTime(platform::now())
    }

    /// Host seconds.
    pub fn secs(self) -> f64 {
        self.0 as f64 * tick_s()
    }
}

/// Seconds per [`HostTime`] unit. Looked up once: call it before a real-time thread first reads
/// the clock, so the lookup never runs there.
pub fn tick_s() -> f64 {
    platform::tick_s()
}

/// The host clock now (s).
pub fn now_s() -> f64 {
    HostTime::now().secs()
}

#[cfg(target_os = "macos")]
mod platform {
    /// Seconds per mach tick.
    pub fn tick_s() -> f64 {
        static TB: std::sync::OnceLock<f64> = std::sync::OnceLock::new();
        *TB.get_or_init(|| {
            let mut info = libc::mach_timebase_info { numer: 0, denom: 0 };
            unsafe { libc::mach_timebase_info(&raw mut info) };
            info.numer as f64 / info.denom as f64 * 1e-9
        })
    }

    pub fn now() -> u64 {
        unsafe { libc::mach_absolute_time() }
    }
}

// (M5's capture brings its own clock's units: WASAPI's are QPC's, which std's Instant reads on
// Windows.)
#[cfg(not(target_os = "macos"))]
mod platform {
    use std::sync::OnceLock;
    use std::time::Instant;

    pub fn tick_s() -> f64 {
        1e-9
    }

    /// (From 1000 s before the clock's first use, as the Mac's clock counts from boot.)
    pub fn now() -> u64 {
        static EPOCH: OnceLock<Instant> = OnceLock::new();
        1_000_000_000_000 + EPOCH.get_or_init(Instant::now).elapsed().as_nanos() as u64
    }
}
