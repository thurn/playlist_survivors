//! `src/core/rng.ts`: deterministic seeded random numbers (sfc32 seeded by an FNV-1a style hash
//! of integers and of strings by UTF-16 code units), with the TS 32-bit integer semantics.

use crate::jsmath::{floor, to_int32};

/// One part of a `hash32` seed: a number (floored, then ToInt32) or a string.
#[derive(Clone, Copy, Debug)]
pub enum Part<'a> {
    Num(f64),
    Str(&'a str),
}

impl From<f64> for Part<'_> {
    fn from(v: f64) -> Self {
        Part::Num(v)
    }
}

impl<'a> From<&'a str> for Part<'a> {
    fn from(v: &'a str) -> Self {
        Part::Str(v)
    }
}

const FNV_PRIME: i32 = 0x0100_0193;

/// `hash32(...parts)`: a 32-bit hash, as an unsigned integer (`h >>> 0`).
pub fn hash32(parts: &[Part]) -> f64 {
    let mut h: i32 = 0x811c_9dc5_u32 as i32;
    for p in parts {
        match *p {
            Part::Str(s) => {
                for unit in s.encode_utf16() {
                    h ^= i32::from(unit);
                    h = h.wrapping_mul(FNV_PRIME);
                }
                h ^= 0xff;
                h = h.wrapping_mul(FNV_PRIME);
            }
            Part::Num(v) => {
                let mut v = to_int32(floor(v)) as u32;
                for _ in 0..4 {
                    h ^= (v & 0xff) as i32;
                    h = h.wrapping_mul(FNV_PRIME);
                    v >>= 8;
                }
            }
        }
    }
    // Final avalanche (murmur3 fmix32).
    h ^= ((h as u32) >> 16) as i32;
    h = h.wrapping_mul(0x85eb_ca6b_u32 as i32);
    h ^= ((h as u32) >> 13) as i32;
    h = h.wrapping_mul(0xc2b2_ae35_u32 as i32);
    h ^= ((h as u32) >> 16) as i32;
    f64::from(h as u32)
}

/// The sfc32 generator (`Rng`). Its state is 32-bit integers, as the TS `| 0` keeps it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Rng {
    a: i32,
    b: i32,
    c: i32,
    d: i32,
}

impl Rng {
    /// `new Rng(...seed)`.
    pub fn new(seed: &[Part]) -> Rng {
        let with = |k: f64| {
            let mut parts = Vec::with_capacity(seed.len() + 1);
            parts.push(Part::Num(k));
            parts.extend_from_slice(seed);
            // (The hash is a uint32; the state holds its 32 bits.)
            hash32(&parts) as u32 as i32
        };
        let mut rng = Rng {
            a: with(1.0),
            b: with(2.0),
            c: with(3.0),
            d: 1,
        };
        for _ in 0..12 {
            rng.next();
        }
        rng
    }

    /// Uniform in [0, 1).
    #[allow(clippy::should_implement_trait)] // (`next` as the TS names it; it never ends.)
    pub fn next(&mut self) -> f64 {
        let t = self.a.wrapping_add(self.b).wrapping_add(self.d);
        self.d = self.d.wrapping_add(1);
        self.a = self.b ^ ((self.b as u32) >> 9) as i32;
        self.b = self.c.wrapping_add(self.c.wrapping_shl(3));
        self.c = self.c.wrapping_shl(21) | ((self.c as u32) >> 11) as i32;
        self.c = self.c.wrapping_add(t);
        f64::from(t as u32) / 4_294_967_296.0
    }

    pub fn range(&mut self, lo: f64, hi: f64) -> f64 {
        lo + (hi - lo) * self.next()
    }

    pub fn int(&mut self, lo: f64, hi_inclusive: f64) -> f64 {
        lo + floor(self.next() * (hi_inclusive - lo + 1.0))
    }

    /// The index `pick` takes from `len` items.
    pub fn pick_index(&mut self, len: usize) -> usize {
        floor(self.next() * len as f64) as usize
    }

    pub fn pick<'a, T>(&mut self, items: &'a [T]) -> &'a T {
        &items[self.pick_index(items.len())]
    }

    /// The index `weighted(items, weights)` picks among `len` items by relative weights (a
    /// missing weight reads as NaN, as JS's `undefined`).
    pub fn weighted_index(&mut self, len: usize, weights: &[f64]) -> usize {
        let mut total = 0.0;
        for w in weights {
            total += w;
        }
        let mut r = self.next() * total;
        for i in 0..len {
            r -= weights.get(i).copied().unwrap_or(f64::NAN);
            if r < 0.0 {
                return i;
            }
        }
        len - 1
    }

    /// Round x up or down at random so the expected value is x.
    pub fn round_stochastic(&mut self, x: f64) -> f64 {
        let f = floor(x);
        f + if self.next() < x - f { 1.0 } else { 0.0 }
    }

    /// Signed value in [-1, 1).
    pub fn signed(&mut self) -> f64 {
        self.next() * 2.0 - 1.0
    }
}

/// Stateless uniform [0, 1) from a seed and an index.
pub fn rand01(seed: f64, i: f64, k: f64) -> f64 {
    hash32(&[Part::Num(seed), Part::Num(i), Part::Num(k)]) / 4_294_967_296.0
}

/// Smooth 1D value noise in [-1, 1].
#[allow(clippy::many_single_char_names)] // (the TS names)
pub fn noise1(seed: f64, x: f64) -> f64 {
    let i = floor(x);
    let f = x - i;
    let a = rand01(seed, i, 0.0) * 2.0 - 1.0;
    let b = rand01(seed, i + 1.0, 0.0) * 2.0 - 1.0;
    let u = f * f * (3.0 - 2.0 * f);
    a + (b - a) * u
}

/// Smooth 2D value noise in [0, 1].
#[allow(clippy::many_single_char_names)] // (the TS names)
pub fn noise2(seed: f64, x: f64, y: f64) -> f64 {
    let ix = floor(x);
    let iy = floor(y);
    let fx = x - ix;
    let fy = y - iy;
    let ux = fx * fx * (3.0 - 2.0 * fx);
    let uy = fy * fy * (3.0 - 2.0 * fy);
    let a = rand01(seed, ix, iy);
    let b = rand01(seed, ix + 1.0, iy);
    let c = rand01(seed, ix, iy + 1.0);
    let d = rand01(seed, ix + 1.0, iy + 1.0);
    a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy
}
