//! Ports of `src/core/*` at `ts-freeze`: seeded randomness, the rhythm shaping functions, the time
//! warp, hit shapes and the song map. Every number is an f64 computed in the TS order of
//! operations, through `jsmath` for `Math` functions.

pub mod geometry;
pub mod rng;
pub mod shaping;
pub mod songmap;
pub mod warp;

/// `array[index]` as JS reads it in arithmetic: `undefined` (NaN) when the index is negative,
/// fractional or past the end.
pub fn at(values: &[f64], index: f64) -> f64 {
    if index >= 0.0 && index.fract() == 0.0 && index < values.len() as f64 {
        values[index as usize]
    } else {
        f64::NAN
    }
}
