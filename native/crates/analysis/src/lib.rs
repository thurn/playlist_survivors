//! The live analyzer: front end, online RNN, DBN, commits, ticks, onsets, bands, timbre, structure, accents, hindsight (pure).
#![forbid(unsafe_code)]

pub mod accents;
pub mod analyzer;
pub mod bands;
pub mod beats;
pub mod dbn;
pub mod frontend;
pub mod hindsight;
pub mod numeric;
pub mod onsets;
#[cfg(test)]
mod pinned;
pub mod rnn;
pub mod structure;
pub mod tensors;
pub mod ticks;
