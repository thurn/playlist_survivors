//! The host clock: capture buffers' times ([`crate::Meta`]), the output's and the watchdog's.

#[cfg(target_os = "macos")]
pub use mac::*;

#[cfg(target_os = "macos")]
mod mac {
    /// Seconds per mach tick.
    pub fn tick_s() -> f64 {
        static TB: std::sync::OnceLock<f64> = std::sync::OnceLock::new();
        *TB.get_or_init(|| {
            let mut info = libc::mach_timebase_info { numer: 0, denom: 0 };
            unsafe { libc::mach_timebase_info(&raw mut info) };
            info.numer as f64 / info.denom as f64 * 1e-9
        })
    }

    pub fn now_ticks() -> u64 {
        unsafe { libc::mach_absolute_time() }
    }

    pub fn host_s(ticks: u64) -> f64 {
        ticks as f64 * tick_s()
    }

    pub fn now_s() -> f64 {
        host_s(now_ticks())
    }
}
