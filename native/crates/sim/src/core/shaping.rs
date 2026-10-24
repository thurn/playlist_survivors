//! `src/core/shaping.ts`: the rhythm shaping functions; each takes a phase in [0, 1).

use crate::jsmath::{PI, cos, exp, floor};

pub fn beat_hit(phase: f64) -> f64 {
    exp(-6.0 * phase)
}

pub fn beat_swell(phase: f64) -> f64 {
    0.5 + 0.5 * cos(2.0 * PI * phase)
}

pub fn amount(base: f64, energy: f64) -> f64 {
    base * (0.4 + 0.6 * energy)
}

pub fn clamp(x: f64, lo: f64, hi: f64) -> f64 {
    if x < lo {
        lo
    } else if x > hi {
        hi
    } else {
        x
    }
}

pub fn lerp(a: f64, b: f64, t: f64) -> f64 {
    a + (b - a) * t
}

pub fn smoothstep(t: f64) -> f64 {
    let u = clamp(t, 0.0, 1.0);
    u * u * (3.0 - 2.0 * u)
}

pub fn frac(x: f64) -> f64 {
    x - floor(x)
}
