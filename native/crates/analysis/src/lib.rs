//! The live analyzer: front end, online RNN, DBN, commits, ticks, onsets, bands, timbre, structure, accents, hindsight (pure).
#![forbid(unsafe_code)]

pub mod analyzer;
pub mod beats;
pub mod dbn;
pub mod frontend;
pub mod hindsight;
pub mod numeric;
pub mod onsets;
pub mod rnn;
pub mod tensors;
pub mod ticks;
