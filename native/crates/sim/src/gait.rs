//! `src/sim/gait.ts`: beat steps for enemies in reactive mode, like the monsters of Crypt of the
//! NecroDancer. An enemy with a gait walks in quick steps on every eighth note or beat (counted from
//! the song's first beat), on the times its pattern marks with "x", repeating. Pure functions of the
//! song's beat index.

use crate::content::types::{Gait, Subdivision};
use crate::jsmath::floor;

/// A gait's times per beat. (The schema allows only eighth or beat.)
pub fn gait_per_beat(g: &Gait) -> f64 {
    if g.every == Subdivision::Eighth {
        2.0
    } else {
        1.0
    }
}

/// The index of the gait time at or before beat index b.
pub fn gait_slot(g: &Gait, b: f64) -> f64 {
    floor(b * gait_per_beat(g) + 1e-9)
}

/// The beat index of gait time k.
pub fn gait_beat(g: &Gait, k: f64) -> f64 {
    k / gait_per_beat(g)
}

/// Does the enemy step on gait time k? (A fractional or NaN k reads past the pattern's letters in
/// TS, undefined: no step.)
pub fn gait_steps(g: &Gait, k: f64) -> bool {
    let n = g.pattern.len() as f64;
    let place = ((k % n) + n) % n;
    place.fract() == 0.0 && g.pattern.as_bytes()[place as usize] == b'x'
}

/// Steps in one pass of the pattern.
fn steps_per(g: &Gait) -> f64 {
    g.pattern.bytes().filter(|&c| c == b'x').count() as f64
}

/// The gait time of the enemy's latest step at or before gait time k.
pub fn gait_step_at_or_before(g: &Gait, k: f64) -> f64 {
    let mut j = k;
    while !gait_steps(g, j) {
        j -= 1.0;
    }
    j
}

/// The gait time of the enemy's first step after gait time k.
pub fn gait_step_after(g: &Gait, k: f64) -> f64 {
    let mut j = k + 1.0;
    while !gait_steps(g, j) {
        j += 1.0;
    }
    j
}

/// Gait times of ground each step covers: the pattern's length over its steps.
pub fn gait_ground(g: &Gait) -> f64 {
    g.pattern.len() as f64 / steps_per(g)
}
