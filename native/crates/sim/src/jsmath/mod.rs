//! JavaScript's `Math` functions with V8's bits (docs/design/native-app.md, "What makes
//! bit-exactness possible"). `tests/jsmath.rs` checks every function against V8's vectors in
//! `native/oracle/` (F2, Node 24's V8 13.6):
//!
//! - sin, cos, exp, log, log2 and atan2: fdlibm 5.3 as V8 ports it, with the fused multiply-adds
//!   Node's arm64 build makes of it ([`fdlibm`]).
//! - pow: ECMAScript's special cases, x*x for y = 2 and sqrt(x) for y = 0.5 (as V8 returns),
//!   then the platform's `pow`
//!   (macOS libm, which V8 calls too): it matched every vector, where a correctly rounded pow
//!   missed 3 in 5000 and fdlibm's 1 in 20. The app runs only on macOS.
//! - the rest: ECMAScript's and V8's own definitions.

mod fdlibm;
mod rem_pio2_large;

/// `Math.abs`.
pub fn abs(x: f64) -> f64 {
    x.abs()
}

/// `Math.atan2(y, x)`.
pub fn atan2(y: f64, x: f64) -> f64 {
    fdlibm::atan2(y, x)
}

/// `Math.ceil`.
pub fn ceil(x: f64) -> f64 {
    x.ceil()
}

/// `Math.cos`.
pub fn cos(x: f64) -> f64 {
    fdlibm::cos(x)
}

/// `Math.exp`.
pub fn exp(x: f64) -> f64 {
    fdlibm::exp(x)
}

/// `Math.floor`.
pub fn floor(x: f64) -> f64 {
    x.floor()
}

/// `Math.hypot(...values)` as V8 computes it: an infinity wins over NaN, then the values scaled by
/// the largest and summed with Kahan compensation.
pub fn hypot_n(values: &[f64]) -> f64 {
    let mut max = 0.0f64;
    let mut nan = false;
    for &v in values {
        let a = v.abs();
        if a.is_nan() {
            nan = true;
        } else if a > max {
            max = a;
        }
    }
    if max == f64::INFINITY {
        return f64::INFINITY;
    }
    if nan {
        return f64::NAN;
    }
    if max == 0.0 {
        return 0.0;
    }
    let mut sum = 0.0f64;
    let mut compensation = 0.0f64;
    for &v in values {
        let n = v.abs() / max;
        let summand = n * n - compensation;
        let preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
    }
    sum.sqrt() * max
}

/// `Math.hypot(x, y)`.
pub fn hypot(x: f64, y: f64) -> f64 {
    hypot_n(&[x, y])
}

/// `Math.imul(a, b)`: the low 32 bits of the product of the arguments' ToInt32.
pub fn imul(a: f64, b: f64) -> f64 {
    f64::from(to_int32(a).wrapping_mul(to_int32(b)))
}

/// `Math.log`.
pub fn log(x: f64) -> f64 {
    fdlibm::log(x)
}

/// `Math.log2`.
pub fn log2(x: f64) -> f64 {
    fdlibm::log2(x)
}

/// `Math.max(a, b)`: NaN if either is NaN, and +0 above -0.
pub fn max(a: f64, b: f64) -> f64 {
    if a.is_nan() || b.is_nan() {
        f64::NAN
    } else if a == b {
        // (Equal values differ only in a zero's sign: +0 wins.)
        if a.is_sign_negative() { b } else { a }
    } else if a > b {
        a
    } else {
        b
    }
}

/// `Math.min(a, b)`: NaN if either is NaN, and -0 below +0.
pub fn min(a: f64, b: f64) -> f64 {
    if a.is_nan() || b.is_nan() {
        f64::NAN
    } else if a == b {
        if a.is_sign_negative() { a } else { b }
    } else if a < b {
        a
    } else {
        b
    }
}

/// `Math.pow(x, y)` and the `**` operator. ECMAScript differs from C's pow in two cases: a NaN
/// exponent always gives NaN, and ±1 to an infinite power is NaN. V8 returns x*x for y = 2 and
/// sqrt(x) for y = 0.5 (x not negative, nor -0), which the platform's pow doesn't always.
pub fn pow(x: f64, y: f64) -> f64 {
    if y.is_nan() {
        return f64::NAN;
    }
    if y.is_infinite() && x.abs() == 1.0 {
        return f64::NAN;
    }
    // (fdlibm's fast paths, which V8 keeps: the platform's pow is off by an ulp on some x.)
    if y == 2.0 {
        return x * x;
    }
    if y == 0.5 && x.is_sign_positive() {
        return x.sqrt();
    }
    x.powf(y)
}

/// `Math.round`, as V8 computes it: the ceiling, less one if that is more than half above x. Ties
/// go toward +∞, -0.5..-0 give -0, and 0.49999999999999994 gives 0.
pub fn round(x: f64) -> f64 {
    let r = x.ceil();
    if r - 0.5 > x { r - 1.0 } else { r }
}

/// `Math.sign`.
pub fn sign(x: f64) -> f64 {
    if x.is_nan() || x == 0.0 {
        x
    } else if x > 0.0 {
        1.0
    } else {
        -1.0
    }
}

/// `Math.sin`.
pub fn sin(x: f64) -> f64 {
    fdlibm::sin(x)
}

/// `Math.sqrt`.
pub fn sqrt(x: f64) -> f64 {
    x.sqrt()
}

/// `Math.PI`.
pub const PI: f64 = std::f64::consts::PI;

/// ECMAScript's ToInt32 (`x | 0`): truncate, wrap modulo 2^32, NaN and infinities to 0. Rust's
/// `as i32` saturates instead.
pub fn to_int32(x: f64) -> i32 {
    if !x.is_finite() {
        return 0;
    }
    let t = x.trunc();
    let m = t.rem_euclid(4_294_967_296.0);
    // (m is in [0, 2^32): exact as u32, then reinterpreted.)
    (m as u32) as i32
}

/// ECMAScript's ToUint32 (`x >>> 0`).
pub fn to_uint32(x: f64) -> u32 {
    to_int32(x) as u32
}
