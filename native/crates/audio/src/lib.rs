//! Core Audio tap, file source, AUHAL output, mixer and the host-time clock.
//!
//! - [`mixer`]: the output's voices, scheduled sample-accurately, and its buses.
//! - [`follow`]: the effects' level, following the music's loudness as it is heard.
//! - [`delay`]: Delayed listening's delay line, the tapped app's capture played `M` late.

pub mod delay;
pub mod follow;
pub mod mixer;
