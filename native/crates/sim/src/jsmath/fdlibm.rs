//! fdlibm 5.3 as V8 ports it (`src/base/ieee754.cc`), as Node's arm64 build compiles it: clang's
//! default `-ffp-contract=on` fuses every `a*b + c`, `a*b - c` and `c - a*b` within one C
//! expression into a fused multiply-add, which changes the last bit on some inputs. Each routine
//! here keeps fdlibm's expressions and fuses exactly where clang does: within one expression only
//! (never across statements), and when both operands of a `+` or `-` are products, the left one
//! (clang emits `llvm.fmuladd` for the left product when it has no other use, before LLVM's DAG
//! combiner sees the expression).
//!
//! The `libm` crate follows musl's later revisions of these routines and doesn't fuse, so it
//! differs from V8 on about 1 input in 150 for sin and cos.
#![allow(
    // fdlibm's idioms: x - x for a NaN of x's kind, its constants as written, its branch order.
    clippy::eq_op,
    clippy::approx_constant,
    clippy::if_not_else,
    clippy::unreadable_literal,
    clippy::excessive_precision,
    clippy::suboptimal_flops,
    clippy::many_single_char_names
)]

/*
 * ====================================================
 * Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
 *
 * Developed at SunSoft, a Sun Microsystems, Inc. business.
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

use super::rem_pio2_large::rem_pio2_large;

fn high(x: f64) -> i32 {
    (x.to_bits() >> 32) as i32
}

fn low(x: f64) -> u32 {
    x.to_bits() as u32
}

fn with_high(x: f64, hi: i32) -> f64 {
    f64::from_bits((u64::from(hi as u32) << 32) | u64::from(low(x)))
}

fn with_low(x: f64, lo: u32) -> f64 {
    f64::from_bits((x.to_bits() & 0xffff_ffff_0000_0000) | u64::from(lo))
}

fn from_words(hi: i32, lo: u32) -> f64 {
    f64::from_bits((u64::from(hi as u32) << 32) | u64::from(lo))
}

/// `a*b + c` as one fused operation.
fn fma(a: f64, b: f64, c: f64) -> f64 {
    a.mul_add(b, c)
}

/// __kernel_sin(x, y, iy): sin on [-pi/4, pi/4], y the tail of x, iy 0 when y is zero.
fn kernel_sin(x: f64, y: f64, iy: i32) -> f64 {
    const HALF: f64 = 5.00000000000000000000e-01;
    const S1: f64 = -1.66666666666666324348e-01;
    const S2: f64 = 8.33333333332248946124e-03;
    const S3: f64 = -1.98412698298579493134e-04;
    const S4: f64 = 2.75573137070700676789e-06;
    const S5: f64 = -2.50507602534068634195e-08;
    const S6: f64 = 1.58969099521155010221e-10;
    let ix = high(x) & 0x7fffffff;
    if ix < 0x3e400000 && x as i32 == 0 {
        return x;
    }
    let z = x * x;
    let v = z * x;
    // r = S2 + z*(S3 + z*(S4 + z*(S5 + z*S6)))
    let r = fma(z, fma(z, fma(z, fma(z, S6, S5), S4), S3), S2);
    if iy == 0 {
        // x + v*(S1 + z*r)
        fma(v, fma(z, r, S1), x)
    } else {
        // x - ((z*(half*y - v*r) - y) - v*S1)
        let inner = fma(HALF, y, -(v * r));
        let t = fma(z, inner, -y);
        x - fma(-v, S1, t)
    }
}

/// __kernel_cos(x, y): cos on [-pi/4, pi/4], y the tail of x.
fn kernel_cos(x: f64, y: f64) -> f64 {
    const ONE: f64 = 1.00000000000000000000e+00;
    const C1: f64 = 4.16666666666666019037e-02;
    const C2: f64 = -1.38888888888741095749e-03;
    const C3: f64 = 2.48015872894767294178e-05;
    const C4: f64 = -2.75573143513906633035e-07;
    const C5: f64 = 2.08757232129817482790e-09;
    const C6: f64 = -1.13596475577881948265e-11;
    let ix = high(x) & 0x7fffffff;
    if ix < 0x3e400000 && x as i32 == 0 {
        return ONE;
    }
    let z = x * x;
    // r = z*(C1 + z*(C2 + z*(C3 + z*(C4 + z*(C5 + z*C6)))))
    let r = z * fma(z, fma(z, fma(z, fma(z, fma(z, C6, C5), C4), C3), C2), C1);
    // (z*r - x*y), the left product fused.
    let zr_xy = fma(z, r, -(x * y));
    if ix < 0x3fd33333 {
        // one - (0.5*z - (z*r - x*y))
        ONE - fma(0.5, z, -zr_xy)
    } else {
        let qx = if ix > 0x3fe90000 {
            0.28125
        } else {
            from_words(ix - 0x00200000, 0)
        };
        let iz = fma(0.5, z, -qx);
        let a = ONE - qx;
        a - (iz - zr_xy)
    }
}

/// __ieee754_rem_pio2(x): n and x - n*pi/2 as y0 + y1.
fn rem_pio2(x: f64) -> (i32, f64, f64) {
    const NPIO2_HW: [i32; 32] = [
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB,
        0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB,
        0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C,
        0x4041475C, 0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB,
        0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB,
    ];
    const HALF: f64 = 5.00000000000000000000e-01;
    const TWO24: f64 = 1.67772160000000000000e+07;
    const INVPIO2: f64 = 6.36619772367581382433e-01;
    const PIO2_1: f64 = 1.57079632673412561417e+00;
    const PIO2_1T: f64 = 6.07710050650619224932e-11;
    const PIO2_2: f64 = 6.07710050630396597660e-11;
    const PIO2_2T: f64 = 2.02226624879595063154e-21;
    const PIO2_3: f64 = 2.02226624871116645580e-21;
    const PIO2_3T: f64 = 8.47842766036889956997e-32;

    let hx = high(x);
    let ix = hx & 0x7fffffff;
    if ix <= 0x3fe921fb {
        return (0, x, 0.0);
    }
    if ix < 0x4002d97c {
        // |x| < 3pi/4: n = ±1.
        return if hx > 0 {
            let mut z = x - PIO2_1;
            if ix != 0x3ff921fb {
                let y0 = z - PIO2_1T;
                (1, y0, (z - y0) - PIO2_1T)
            } else {
                z -= PIO2_2;
                let y0 = z - PIO2_2T;
                (1, y0, (z - y0) - PIO2_2T)
            }
        } else {
            let mut z = x + PIO2_1;
            if ix != 0x3ff921fb {
                let y0 = z + PIO2_1T;
                (-1, y0, (z - y0) + PIO2_1T)
            } else {
                z += PIO2_2;
                let y0 = z + PIO2_2T;
                (-1, y0, (z - y0) + PIO2_2T)
            }
        };
    }
    if ix <= 0x413921fb {
        // |x| <= 2^19 (pi/2): medium.
        let t = x.abs();
        let n = fma(t, INVPIO2, HALF) as i32;
        let fnn = f64::from(n);
        let mut r = fma(-fnn, PIO2_1, t);
        let mut w = fnn * PIO2_1T;
        let mut y0;
        if n < 32 && ix != NPIO2_HW[(n - 1) as usize] {
            y0 = r - w;
        } else {
            let j = ix >> 20;
            y0 = r - w;
            let mut i = j - ((high(y0) >> 20) & 0x7ff);
            if i > 16 {
                let t = r;
                w = fnn * PIO2_2;
                r = t - w;
                w = fma(fnn, PIO2_2T, -((t - r) - w));
                y0 = r - w;
                i = j - ((high(y0) >> 20) & 0x7ff);
                if i > 49 {
                    let t = r;
                    w = fnn * PIO2_3;
                    r = t - w;
                    w = fma(fnn, PIO2_3T, -((t - r) - w));
                    y0 = r - w;
                }
            }
        }
        let y1 = (r - y0) - w;
        return if hx < 0 { (-n, -y0, -y1) } else { (n, y0, y1) };
    }
    if ix >= 0x7ff00000 {
        let nan = x - x;
        return (0, nan, nan);
    }
    // Huge: z = scalbn(|x|, ilogb(x) - 23), split into 24-bit chunks.
    let e0 = (ix >> 20) - 1046;
    let mut z = from_words(ix - (e0 << 20), low(x));
    let mut tx = [0.0f64; 3];
    for t in tx.iter_mut().take(2) {
        *t = f64::from(z as i32);
        z = (z - *t) * TWO24;
    }
    tx[2] = z;
    let mut nx = 3;
    while tx[nx - 1] == 0.0 {
        nx -= 1;
    }
    let mut y = [0.0f64; 2];
    let n = rem_pio2_large(&tx[..nx], &mut y, e0, 1);
    if hx < 0 {
        (-n, -y[0], -y[1])
    } else {
        (n, y[0], y[1])
    }
}

pub fn sin(x: f64) -> f64 {
    let ix = high(x) & 0x7fffffff;
    if ix <= 0x3fe921fb {
        return kernel_sin(x, 0.0, 0);
    }
    if ix >= 0x7ff00000 {
        return x - x;
    }
    let (n, y0, y1) = rem_pio2(x);
    match n & 3 {
        0 => kernel_sin(y0, y1, 1),
        1 => kernel_cos(y0, y1),
        2 => -kernel_sin(y0, y1, 1),
        _ => -kernel_cos(y0, y1),
    }
}

pub fn cos(x: f64) -> f64 {
    let ix = high(x) & 0x7fffffff;
    if ix <= 0x3fe921fb {
        return kernel_cos(x, 0.0);
    }
    if ix >= 0x7ff00000 {
        return x - x;
    }
    let (n, y0, y1) = rem_pio2(x);
    match n & 3 {
        0 => kernel_cos(y0, y1),
        1 => -kernel_sin(y0, y1, 1),
        2 => -kernel_cos(y0, y1),
        _ => kernel_sin(y0, y1, 1),
    }
}

const LG1: f64 = 6.666666666666735130e-01;
const LG2: f64 = 3.999999999940941908e-01;
const LG3: f64 = 2.857142874366239149e-01;
const LG4: f64 = 2.222219843214978396e-01;
const LG5: f64 = 1.818357216161805012e-01;
const LG6: f64 = 1.531383769920937332e-01;
const LG7: f64 = 1.479819860511658591e-01;
const TWO54: f64 = 1.80143985094819840000e+16;

pub fn log(x: f64) -> f64 {
    const LN2_HI: f64 = 6.93147180369123816490e-01;
    const LN2_LO: f64 = 1.90821492927058770002e-10;

    let mut x = x;
    let mut hx = high(x);
    let lx = low(x);
    let mut k: i32 = 0;
    if hx < 0x00100000 {
        // x < 2^-1022
        if ((hx & 0x7fffffff) as u32 | lx) == 0 {
            return f64::NEG_INFINITY;
        }
        if hx < 0 {
            return (x - x) / 0.0;
        }
        k -= 54;
        x *= TWO54;
        hx = high(x);
    }
    if hx >= 0x7ff00000 {
        return x + x;
    }
    k += (hx >> 20) - 1023;
    hx &= 0x000fffff;
    let i = (hx + 0x95f64) & 0x100000;
    x = with_high(x, hx | (i ^ 0x3ff00000));
    k += i >> 20;
    let f = x - 1.0;
    if (0x000fffff & (2 + hx)) < 3 {
        // |f| < 2^-20
        if f == 0.0 {
            if k == 0 {
                return 0.0;
            }
            let dk = f64::from(k);
            // dk*ln2_hi + dk*ln2_lo
            return fma(dk, LN2_HI, dk * LN2_LO);
        }
        // f*f*(0.5 - 0.33333333333333333*f)
        let r = f * f * fma(-0.33333333333333333, f, 0.5);
        if k == 0 {
            return f - r;
        }
        let dk = f64::from(k);
        // dk*ln2_hi - ((R - dk*ln2_lo) - f)
        return fma(dk, LN2_HI, -(fma(-dk, LN2_LO, r) - f));
    }
    let s = f / (2.0 + f);
    let dk = f64::from(k);
    let z = s * s;
    let mut i = hx - 0x6147a;
    let w = z * z;
    let j = 0x6b851 - hx;
    let t1 = w * fma(w, fma(w, LG6, LG4), LG2);
    let t2 = z * fma(w, fma(w, fma(w, LG7, LG5), LG3), LG1);
    i |= j;
    let r = t2 + t1;
    if i > 0 {
        let hfsq = 0.5 * f * f;
        if k == 0 {
            // f - (hfsq - s*(hfsq + R))
            f - fma(-s, hfsq + r, hfsq)
        } else {
            // dk*ln2_hi - ((hfsq - (s*(hfsq + R) + dk*ln2_lo)) - f)
            fma(dk, LN2_HI, -((hfsq - fma(s, hfsq + r, dk * LN2_LO)) - f))
        }
    } else if k == 0 {
        // f - s*(f - R)
        fma(-s, f - r, f)
    } else {
        // dk*ln2_hi - ((s*(f - R) - dk*ln2_lo) - f)
        fma(dk, LN2_HI, -(fma(s, f - r, -(dk * LN2_LO)) - f))
    }
}

/// k_log1p(f): log(1 + f) - f + f*f/2 on [sqrt(2)/2 - 1, sqrt(2) - 1].
fn k_log1p(f: f64) -> f64 {
    let s = f / (2.0 + f);
    let z = s * s;
    let w = z * z;
    let t1 = w * fma(w, fma(w, LG6, LG4), LG2);
    let t2 = z * fma(w, fma(w, fma(w, LG7, LG5), LG3), LG1);
    let r = t2 + t1;
    let hfsq = 0.5 * f * f;
    s * (hfsq + r)
}

pub fn log2(x: f64) -> f64 {
    const IVLN2HI: f64 = 1.44269504072144627571e+00;
    const IVLN2LO: f64 = 1.67517131648865118353e-10;

    let mut x = x;
    let mut hx = high(x);
    let lx = low(x);
    let mut k: i32 = 0;
    if hx < 0x00100000 {
        if ((hx & 0x7fffffff) as u32 | lx) == 0 {
            return f64::NEG_INFINITY;
        }
        if hx < 0 {
            return (x - x) / 0.0;
        }
        k -= 54;
        x *= TWO54;
        hx = high(x);
    }
    if hx >= 0x7ff00000 {
        return x + x;
    }
    if hx == 0x3ff00000 && lx == 0 {
        return 0.0;
    }
    k += (hx >> 20) - 1023;
    hx &= 0x000fffff;
    let i = (hx + 0x95f64) & 0x100000;
    x = with_high(x, hx | (i ^ 0x3ff00000));
    k += i >> 20;
    let y = f64::from(k);
    let f = x - 1.0;
    let hfsq = 0.5 * f * f;
    let r = k_log1p(f);
    let hi = with_low(f - hfsq, 0);
    let lo = (f - hi) - hfsq + r;
    let val_hi = hi * IVLN2HI;
    // (lo + hi)*ivln2lo + lo*ivln2hi
    let mut val_lo = fma(lo + hi, IVLN2LO, lo * IVLN2HI);
    let w = y + val_hi;
    val_lo += (y - w) + val_hi;
    val_lo + w
}

pub fn exp(x: f64) -> f64 {
    const ONE: f64 = 1.0;
    const HALF: [f64; 2] = [0.5, -0.5];
    const O_THRESHOLD: f64 = 7.09782712893383973096e+02;
    const U_THRESHOLD: f64 = -7.45133219101941108420e+02;
    const LN2HI: [f64; 2] = [6.93147180369123816490e-01, -6.93147180369123816490e-01];
    const LN2LO: [f64; 2] = [1.90821492927058770002e-10, -1.90821492927058770002e-10];
    const INVLN2: f64 = 1.44269504088896338700e+00;
    const P1: f64 = 1.66666666666666019037e-01;
    const P2: f64 = -2.77777777770155933842e-03;
    const P3: f64 = 6.61375632143793436117e-05;
    const P4: f64 = -1.65339022054652515390e-06;
    const P5: f64 = 4.13813679705723846039e-08;
    const E: f64 = 2.718281828459045;
    const HUGE: f64 = 1.0e+300;
    const TWOM1000: f64 = 9.33263618503218878990e-302;
    const TWO1023: f64 = 8.988465674311579539e307;

    let mut x = x;
    let hxs = high(x) as u32;
    let xsb = ((hxs >> 31) & 1) as usize;
    let hx = hxs & 0x7fffffff;
    let mut hi = 0.0;
    let mut lo = 0.0;
    let mut k: i32 = 0;
    if hx >= 0x40862E42 {
        if hx >= 0x7ff00000 {
            if ((hx & 0xfffff) | low(x)) != 0 {
                return x + x;
            }
            return if xsb == 0 { x } else { 0.0 };
        }
        if x > O_THRESHOLD {
            return HUGE * HUGE;
        }
        if x < U_THRESHOLD {
            return TWOM1000 * TWOM1000;
        }
    }
    if hx > 0x3fd62e42 {
        if hx < 0x3FF0A2B2 {
            // (V8 special-cases exp(1): the reduction below gets its last bit wrong.)
            if x == 1.0 {
                return E;
            }
            hi = x - LN2HI[xsb];
            lo = LN2LO[xsb];
            k = 1 - 2 * xsb as i32;
        } else {
            k = fma(INVLN2, x, HALF[xsb]) as i32;
            let t = f64::from(k);
            // (t*ln2HI is exact here; fdlibm uses the positive constants in this branch.)
            hi = fma(-t, LN2HI[0], x);
            lo = t * LN2LO[0];
        }
        x = hi - lo;
    } else if hx < 0x3e300000 {
        if HUGE + x > ONE {
            return ONE + x;
        }
    } else {
        k = 0;
    }
    let t = x * x;
    let twopk = if k >= -1021 {
        from_words(0x3ff00000 + (k << 20), 0)
    } else {
        from_words(0x3ff00000 + ((k + 1000) << 20), 0)
    };
    // c = x - t*(P1 + t*(P2 + t*(P3 + t*(P4 + t*P5))))
    let c = fma(-t, fma(t, fma(t, fma(t, fma(t, P5, P4), P3), P2), P1), x);
    if k == 0 {
        return ONE - ((x * c) / (c - 2.0) - x);
    }
    let y = ONE - ((lo - (x * c) / (2.0 - c)) - hi);
    if k >= -1021 {
        if k == 1024 {
            return y * 2.0 * TWO1023;
        }
        y * twopk
    } else {
        y * twopk * TWOM1000
    }
}

pub fn atan(x: f64) -> f64 {
    const ATANHI: [f64; 4] = [
        4.63647609000806093515e-01,
        7.85398163397448278999e-01,
        9.82793723247329054082e-01,
        1.57079632679489655800e+00,
    ];
    const ATANLO: [f64; 4] = [
        2.26987774529616870924e-17,
        3.06161699786838301793e-17,
        1.39033110312309984516e-17,
        6.12323399573676603587e-17,
    ];
    const AT: [f64; 11] = [
        3.33333333333329318027e-01,
        -1.99999999998764832476e-01,
        1.42857142725034663711e-01,
        -1.11111104054623557880e-01,
        9.09088713343650656196e-02,
        -7.69187620504482999495e-02,
        6.66107313738753120669e-02,
        -5.83357013379057348645e-02,
        4.97687799461593236017e-02,
        -3.65315727442169155270e-02,
        1.62858201153657823623e-02,
    ];
    const ONE: f64 = 1.0;
    const HUGE: f64 = 1.0e300;

    let mut x = x;
    let hx = high(x);
    let ix = hx & 0x7fffffff;
    let id: i32;
    if ix >= 0x44100000 {
        if ix > 0x7ff00000 || (ix == 0x7ff00000 && low(x) != 0) {
            return x + x;
        }
        return if hx > 0 {
            ATANHI[3] + ATANLO[3]
        } else {
            -ATANHI[3] - ATANLO[3]
        };
    }
    if ix < 0x3fdc0000 {
        if ix < 0x3e400000 && HUGE + x > ONE {
            return x;
        }
        id = -1;
    } else {
        x = x.abs();
        if ix < 0x3ff30000 {
            if ix < 0x3fe60000 {
                id = 0;
                x = fma(2.0, x, -ONE) / (2.0 + x);
            } else {
                id = 1;
                x = (x - ONE) / (x + ONE);
            }
        } else if ix < 0x40038000 {
            id = 2;
            x = (x - 1.5) / fma(1.5, x, ONE);
        } else {
            id = 3;
            x = -1.0 / x;
        }
    }
    let z = x * x;
    let w = z * z;
    let s1 = z * fma(
        w,
        fma(
            w,
            fma(w, fma(w, fma(w, AT[10], AT[8]), AT[6]), AT[4]),
            AT[2],
        ),
        AT[0],
    );
    let s2 = w * fma(w, fma(w, fma(w, fma(w, AT[9], AT[7]), AT[5]), AT[3]), AT[1]);
    if id < 0 {
        // x - x*(s1 + s2)
        return fma(-x, s1 + s2, x);
    }
    let id = id as usize;
    // atanhi[id] - ((x*(s1 + s2) - atanlo[id]) - x)
    let z = ATANHI[id] - (fma(x, s1 + s2, -ATANLO[id]) - x);
    if hx < 0 { -z } else { z }
}

pub fn atan2(y: f64, x: f64) -> f64 {
    const TINY: f64 = 1.0e-300;
    const PI_O_4: f64 = 7.8539816339744827900E-01;
    const PI_O_2: f64 = 1.5707963267948965580E+00;
    const PI: f64 = 3.1415926535897931160E+00;
    const PI_LO: f64 = 1.2246467991473531772E-16;

    let hx = high(x);
    let lx = low(x);
    let ix = hx & 0x7fffffff;
    let hy = high(y);
    let ly = low(y);
    let iy = hy & 0x7fffffff;
    let nan = |i: i32, l: u32| (i as u32 | ((l | l.wrapping_neg()) >> 31)) > 0x7ff00000;
    if nan(ix, lx) || nan(iy, ly) {
        return x + y;
    }
    if (hx.wrapping_sub(0x3ff00000) as u32 | lx) == 0 {
        return atan(y);
    }
    let mut m = ((hy >> 31) & 1) | ((hx >> 30) & 2);
    if (iy as u32 | ly) == 0 {
        return match m {
            0 | 1 => y,
            2 => PI + TINY,
            _ => -PI - TINY,
        };
    }
    if (ix as u32 | lx) == 0 {
        return if hy < 0 {
            -PI_O_2 - TINY
        } else {
            PI_O_2 + TINY
        };
    }
    if ix == 0x7ff00000 {
        if iy == 0x7ff00000 {
            return match m {
                0 => PI_O_4 + TINY,
                1 => -PI_O_4 - TINY,
                2 => 3.0 * PI_O_4 + TINY,
                _ => -3.0 * PI_O_4 - TINY,
            };
        }
        return match m {
            0 => 0.0,
            1 => -0.0,
            2 => PI + TINY,
            _ => -PI - TINY,
        };
    }
    if iy == 0x7ff00000 {
        return if hy < 0 {
            -PI_O_2 - TINY
        } else {
            PI_O_2 + TINY
        };
    }
    let k = (iy - ix) >> 20;
    let z = if k > 60 {
        m &= 1;
        fma(0.5, PI_LO, PI_O_2)
    } else if hx < 0 && k < -60 {
        0.0
    } else {
        atan((y / x).abs())
    };
    match m {
        0 => z,
        1 => -z,
        2 => PI - (z - PI_LO),
        _ => (z - PI_LO) - PI,
    }
}
