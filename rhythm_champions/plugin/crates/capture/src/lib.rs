//! Live capture, shared by native's app and the Unity game's plugin (docs/archive/design/unity-game.md,
//! "Shared Rust code with native"; docs/archive/design/native-app.md, "Capture and mute").
//!
//! - [`tap`]: the Core Audio process taps (all output but ours, or one app's processes, muted
//!   while tapped) and the microphone, each into a [`sink`].
//! - [`sink`]: where a capture's buffers go, on any platform: the mono mixdown into a lock-free
//!   ring, with [`Meta`] for each buffer, and the stereo into Delayed listening's delay line.
//! - [`teardown`]: a live capture torn down once, by the app or its watchdog.
//! - [`apps`]: the apps producing audio, the ones Delayed listening can tap.
//! - [`watchdog`]: releases a tap when its capture or the main thread stalls.
//! - [`analysis`]: the analysis thread, a [`analysis::Source`]'s samples through the analyzer and
//!   its hindsight worker.
//! - [`decode`]: audio files and playlists decoded with macOS's `afconvert`.
//! - [`clock`]: the host clock the captures' times are on.
//!
//! The platform's code ([`coreaudio`], [`tap`], [`clock`]'s units and the app list's enumeration)
//! is behind `cfg(target_os = "macos")`; M5 adds Windows's process loopback beside it. The rest
//! compiles everywhere (the checks build it for Windows).

pub mod analysis;
pub mod apps;
#[allow(deprecated)]
pub mod clock;
// The tap and its helpers are native's phase 0 spike's (57e846a^:native/spike), kept as they were.
#[cfg(target_os = "macos")]
#[allow(clippy::pedantic, unused_qualifications)]
pub mod coreaudio;
pub mod decode;
pub mod sink;
#[cfg(target_os = "macos")]
#[allow(clippy::pedantic, unused_qualifications, deprecated)]
pub mod tap;
pub mod teardown;
pub mod watchdog;

use clock::HostTime;

/// One captured (or played) buffer's timing, beside its samples.
#[derive(Clone, Copy, Debug, Default)]
pub struct Meta {
    /// Index (at the source's rate) of the buffer's first frame.
    pub first: u64,
    pub frames: u32,
    /// When the first frame was captured.
    pub input_host: HostTime,
    /// When the capture handed the buffer over (the IOProc ran).
    pub now_host: HostTime,
    /// Peak absolute sample, to tell silence (or a missing grant) from audio.
    pub peak: f32,
    /// Frames dropped so far because the ring was full.
    pub dropped: u64,
}
