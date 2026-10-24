//! `src/core/geometry.ts`: hit shapes in world pixels and the vector helpers.
// (Midpoints as (a + b) / 2: the TS order of operations, bit for bit.)
#![allow(clippy::manual_midpoint)]

use crate::jsmath::{PI, abs, atan2, cos, hypot, max, min, pow, sign, sin, sqrt};

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Vec2 {
    pub x: f64,
    pub y: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Shape {
    /// A circular sector: within `radius` and `half_angle` of `dir` (and beyond `inner`, a band).
    Arc {
        x: f64,
        y: f64,
        radius: f64,
        dir: f64,
        half_angle: f64,
        inner: Option<f64>,
    },
    Circle {
        x: f64,
        y: f64,
        radius: f64,
    },
    /// The segment (x0, y0)–(x1, y1) thickened by `radius`.
    Capsule {
        x0: f64,
        y0: f64,
        x1: f64,
        y1: f64,
        radius: f64,
    },
    Ring {
        x: f64,
        y: f64,
        inner: f64,
        outer: f64,
    },
}

pub const TAU: f64 = PI * 2.0;

pub fn wrap_angle(a: f64) -> f64 {
    let mut r = (a + PI) % TAU;
    if r < 0.0 {
        r += TAU;
    }
    r - PI
}

pub fn dist(ax: f64, ay: f64, bx: f64, by: f64) -> f64 {
    hypot(ax - bx, ay - by)
}

fn seg_t(px: f64, py: f64, x0: f64, y0: f64, dx: f64, dy: f64) -> f64 {
    let l2 = dx * dx + dy * dy;
    if l2 == 0.0 {
        0.0
    } else {
        max(0.0, min(1.0, ((px - x0) * dx + (py - y0) * dy) / l2))
    }
}

fn seg_dist(px: f64, py: f64, x0: f64, y0: f64, x1: f64, y1: f64) -> f64 {
    let dx = x1 - x0;
    let dy = y1 - y0;
    let t = seg_t(px, py, x0, y0, dx, dy);
    hypot(px - (x0 + t * dx), py - (y0 + t * dy))
}

/// True if point p is inside the shape grown by `margin` pixels.
pub fn shape_contains(s: &Shape, px: f64, py: f64, margin: f64) -> bool {
    match *s {
        Shape::Circle { x, y, radius } => dist(px, py, x, y) <= radius + margin,
        Shape::Ring { x, y, inner, outer } => {
            let d = dist(px, py, x, y);
            d >= inner - margin && d <= outer + margin
        }
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => seg_dist(px, py, x0, y0, x1, y1) <= radius + margin,
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            inner,
        } => {
            let d = dist(px, py, x, y);
            if d > radius + margin {
                return false;
            }
            if let Some(inner) = inner
                && d < inner - margin
            {
                return false;
            }
            if d <= margin {
                return true;
            }
            let da = abs(wrap_angle(atan2(py - y, px - x) - dir));
            if da <= half_angle {
                return true;
            }
            // Distance from the point to the nearer edge ray of the sector.
            let edge = dir + sign(wrap_angle(atan2(py - y, px - x) - dir)) * half_angle;
            seg_dist(px, py, x, y, x + cos(edge) * radius, y + sin(edge) * radius) <= margin
        }
    }
}

pub fn shape_area(s: &Shape) -> f64 {
    match *s {
        Shape::Circle { radius, .. } => PI * radius * radius,
        Shape::Ring { inner, outer, .. } => PI * (outer * outer - inner * inner),
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => PI * radius * radius + 2.0 * radius * dist(x0, y0, x1, y1),
        Shape::Arc {
            radius,
            half_angle,
            inner,
            ..
        } => half_angle * (radius * radius - pow(inner.unwrap_or(0.0), 2.0)),
    }
}

/// A rough centre of the shape.
pub fn shape_centre(s: &Shape) -> Vec2 {
    match *s {
        Shape::Circle { x, y, .. } | Shape::Ring { x, y, .. } | Shape::Arc { x, y, .. } => {
            Vec2 { x, y }
        }
        Shape::Capsule { x0, y0, x1, y1, .. } => Vec2 {
            x: (x0 + x1) / 2.0,
            y: (y0 + y1) / 2.0,
        },
    }
}

/// Axis-aligned bounds [minX, minY, maxX, maxY].
pub fn shape_bounds(s: &Shape) -> [f64; 4] {
    match *s {
        Shape::Circle { x, y, radius } => [x - radius, y - radius, x + radius, y + radius],
        Shape::Ring { x, y, outer: r, .. }
        | Shape::Arc {
            x, y, radius: r, ..
        } => [x - r, y - r, x + r, y + r],
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => [
            min(x0, x1) - radius,
            min(y0, y1) - radius,
            max(x0, x1) + radius,
            max(y0, y1) + radius,
        ],
    }
}

/// Direction to push a point out of the shape (unit vector).
pub fn push_out_dir(s: &Shape, px: f64, py: f64) -> Vec2 {
    let (cx, cy) = match *s {
        Shape::Capsule { x0, y0, x1, y1, .. } => {
            let dx = x1 - x0;
            let dy = y1 - y0;
            let t = seg_t(px, py, x0, y0, dx, dy);
            (x0 + t * dx, y0 + t * dy)
        }
        Shape::Circle { x, y, .. } | Shape::Ring { x, y, .. } | Shape::Arc { x, y, .. } => (x, y),
    };
    let mut vx = px - cx;
    let mut vy = py - cy;
    if let Shape::Ring { inner, outer, .. } = *s {
        let d = hypot(vx, vy);
        let mid = (inner + outer) / 2.0;
        if d < mid {
            vx = -vx;
            vy = -vy;
        }
    }
    let l = hypot(vx, vy);
    if l < 1e-6 {
        return Vec2 { x: 1.0, y: 0.0 };
    }
    Vec2 {
        x: vx / l,
        y: vy / l,
    }
}

/// JS `a || b` for numbers: b when a is 0 or NaN.
fn or(a: f64, b: f64) -> f64 {
    if a == 0.0 || a.is_nan() { b } else { a }
}

/// A uniformly distributed point inside the shape, shrunk by `inset`; `r` draws in [0, 1).
pub fn sample_in_shape(s: &Shape, r: &mut dyn FnMut() -> f64, inset: f64) -> Vec2 {
    match *s {
        Shape::Circle { x, y, radius } => {
            let rad = max(0.0, radius - inset) * sqrt(r());
            let a = r() * TAU;
            Vec2 {
                x: x + cos(a) * rad,
                y: y + sin(a) * rad,
            }
        }
        Shape::Ring { x, y, inner, outer } => {
            let lo = inner + inset;
            let hi = max(lo, outer - inset);
            let rad = sqrt(lo * lo + r() * (hi * hi - lo * lo));
            let a = r() * TAU;
            Vec2 {
                x: x + cos(a) * rad,
                y: y + sin(a) * rad,
            }
        }
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            inner,
        } => {
            let min_r = max(min(radius * 0.35, 20.0), inner.unwrap_or(0.0) + inset);
            let max_r = max(min_r, radius - inset);
            let rad = sqrt(min_r * min_r + r() * (max_r * max_r - min_r * min_r));
            let ang_inset = min(half_angle * 0.9, inset / max(rad, 1.0));
            let a = dir + (r() * 2.0 - 1.0) * (half_angle - ang_inset);
            Vec2 {
                x: x + cos(a) * rad,
                y: y + sin(a) * rad,
            }
        }
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => {
            let t = r();
            let rad = max(0.0, radius - inset) * (r() * 2.0 - 1.0);
            let dx = x1 - x0;
            let dy = y1 - y0;
            let l = or(hypot(dx, dy), 1.0);
            Vec2 {
                x: x0 + dx * t - (dy / l) * rad,
                y: y0 + dy * t + (dx / l) * rad,
            }
        }
    }
}

/// Translate a shape.
pub fn move_shape(s: &Shape, dx: f64, dy: f64) -> Shape {
    match *s {
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => Shape::Capsule {
            x0: x0 + dx,
            y0: y0 + dy,
            x1: x1 + dx,
            y1: y1 + dy,
            radius,
        },
        Shape::Circle { x, y, radius } => Shape::Circle {
            x: x + dx,
            y: y + dy,
            radius,
        },
        Shape::Ring { x, y, inner, outer } => Shape::Ring {
            x: x + dx,
            y: y + dy,
            inner,
            outer,
        },
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            inner,
        } => Shape::Arc {
            x: x + dx,
            y: y + dy,
            radius,
            dir,
            half_angle,
            inner,
        },
    }
}

/// The nearest point inside the shape (shrunk by `inset`) to p.
pub fn nearest_inside(s: &Shape, px: f64, py: f64, inset: f64) -> Vec2 {
    match *s {
        Shape::Circle { x, y, radius } => {
            let d = dist(px, py, x, y);
            let r = max(0.0, radius - inset);
            if d <= r {
                return Vec2 { x: px, y: py };
            }
            Vec2 {
                x: x + ((px - x) / d) * r,
                y: y + ((py - y) / d) * r,
            }
        }
        Shape::Ring { x, y, inner, outer } => {
            let d = or(dist(px, py, x, y), 1e-6);
            let r = min(outer - inset, max(inner + inset, d));
            Vec2 {
                x: x + ((px - x) / d) * r,
                y: y + ((py - y) / d) * r,
            }
        }
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => {
            let dx = x1 - x0;
            let dy = y1 - y0;
            let t = seg_t(px, py, x0, y0, dx, dy);
            let cx = x0 + t * dx;
            let cy = y0 + t * dy;
            let d = dist(px, py, cx, cy);
            let r = max(0.0, radius - inset);
            if d <= r {
                return Vec2 { x: px, y: py };
            }
            Vec2 {
                x: cx + ((px - cx) / d) * r,
                y: cy + ((py - cy) / d) * r,
            }
        }
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            inner,
        } => {
            let inner = inner.unwrap_or(0.0);
            let d = dist(px, py, x, y);
            let a = wrap_angle(atan2(py - y, px - x) - dir);
            let r = min(max(max(d, 2.0), inner + inset), radius - inset);
            let lim = max(0.0, half_angle - inset / max(r, 1.0));
            let aa = max(-lim, min(lim, a)) + dir;
            if d <= radius - inset && d >= inner + inset && abs(a) <= lim {
                return Vec2 { x: px, y: py };
            }
            Vec2 {
                x: x + cos(aa) * r,
                y: y + sin(aa) * r,
            }
        }
    }
}
