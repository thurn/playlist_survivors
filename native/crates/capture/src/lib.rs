//! Live capture, shared by native's app and the Unity game's plugin (docs/design/unity-game.md,
//! "Shared Rust code with native"; docs/design/native-app.md, "Capture and mute").
//!
//! - [`tap`]: the Core Audio process taps (all output but ours, or one app's processes, muted
//!   while tapped) and the microphone, each into a lock-free ring, with [`Meta`] for each buffer.
//! - [`apps`]: the apps producing audio, the ones Delayed listening can tap.
//! - [`watchdog`]: releases a tap when its capture or the main thread stalls.
//! - [`analysis`]: the analysis thread, a [`analysis::Source`]'s samples through the analyzer and
//!   its hindsight worker.
//! - [`decode`]: audio files and playlists decoded with macOS's `afconvert`.
//! - [`clock`]: the host clock the captures' times are on.
//!
//! The platform's code ([`coreaudio`], [`tap`], [`clock`] and the app list's enumeration) is
//! behind `cfg(target_os = "macos")`; M5 adds Windows's process loopback beside it.

pub mod analysis;
pub mod apps;
#[allow(deprecated)]
pub mod clock;
// The tap and its helpers are native's phase 0 spike's (57e846a^:native/spike), kept as they were.
#[cfg(target_os = "macos")]
#[allow(clippy::pedantic, unused_qualifications)]
pub mod coreaudio;
pub mod decode;
#[cfg(target_os = "macos")]
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated
)]
pub mod tap;
pub mod watchdog;

/// One captured (or played) buffer's timing, beside its samples.
#[derive(Clone, Copy, Debug, Default)]
pub struct Meta {
    /// Index (at the source's rate) of the buffer's first frame.
    pub first: u64,
    pub frames: u32,
    /// The host clock's ticks ([`clock::host_s`]) when the first frame was captured.
    pub input_host: u64,
    /// The host clock's ticks when the IOProc ran.
    pub now_host: u64,
    /// Peak absolute sample, to tell silence (or a missing grant) from audio.
    pub peak: f32,
    /// Frames dropped so far because the ring was full.
    pub dropped: u64,
}
