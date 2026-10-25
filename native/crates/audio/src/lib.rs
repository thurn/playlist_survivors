//! Core Audio tap, file source, AUHAL output, mixer and the host-time clock.
//!
//! - [`mixer`]: the output's voices, scheduled sample-accurately, and its buses.
//! - [`follow`]: the effects' level, following the music's loudness as it is heard.

pub mod follow;
pub mod mixer;
