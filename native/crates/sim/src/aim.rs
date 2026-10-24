//! `src/sim/aim.ts`: auto-aim. The sword picks its own direction, like a Vampire Survivors weapon,
//! reading only the game state at the start of the wind-up.

use crate::core::geometry::{Shape, TAU, shape_contains, wrap_angle};
use crate::jsmath::{abs, atan2, hypot};

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct AimTarget {
    pub x: f64,
    pub y: f64,
    /// Body radius: the swing catches a target whose body overlaps the arc.
    pub r: f64,
    /// Boss 5, elites 2, regular enemies 1.
    pub weight: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ArcReach {
    pub radius: f64,
    /// Half the arc's opening angle, in radians.
    pub half_angle: f64,
}

pub const AIM_DIRECTIONS: u32 = 16;

/// The arc a swing from (x, y) toward `dir` covers.
pub fn swing_arc(x: f64, y: f64, dir: f64, reach: ArcReach) -> Shape {
    Shape::Arc {
        x,
        y,
        radius: reach.radius,
        dir,
        half_angle: reach.half_angle,
        inner: None,
    }
}

/// Does a hit shape catch this body? Its centre lies within the shape grown by half its radius.
pub fn catches(shape: &Shape, x: f64, y: f64, r: f64) -> bool {
    shape_contains(shape, x, y, 0.5 * r)
}

/// The direction that hits the most valuable targets. Ties go to the direction nearest the nearest
/// enemy. With nobody in reach, aim at the nearest enemy within twice the reach; with nobody near,
/// keep the current facing.
pub fn aim(hx: f64, hy: f64, facing: f64, targets: &[AimTarget], reach: ArcReach) -> f64 {
    let mut nearest: Option<&AimTarget> = None;
    let mut nearest_d = f64::INFINITY;
    for t in targets {
        let d = hypot(t.x - hx, t.y - hy);
        if d < nearest_d {
            nearest_d = d;
            nearest = Some(t);
        }
    }
    let to_nearest = nearest.map_or(facing, |n| atan2(n.y - hy, n.x - hx));
    let mut best_score = 0.0;
    let mut best_dir = facing;
    let mut best_off = f64::INFINITY;
    for k in 0..AIM_DIRECTIONS {
        let dir = wrap_angle((f64::from(k) / f64::from(AIM_DIRECTIONS)) * TAU);
        let arc = swing_arc(hx, hy, dir, reach);
        // Aim counts enemies whose centre is in the arc.
        let mut score = 0.0;
        for t in targets {
            if shape_contains(&arc, t.x, t.y, 0.0) {
                score += t.weight;
            }
        }
        if score <= 0.0 {
            continue;
        }
        let off = abs(wrap_angle(dir - to_nearest));
        if score > best_score + 1e-9 || (abs(score - best_score) <= 1e-9 && off < best_off - 1e-9) {
            best_score = score;
            best_dir = dir;
            best_off = off;
        }
    }
    if best_score > 0.0 {
        return best_dir;
    }
    if nearest.is_some() && nearest_d <= 2.0 * reach.radius {
        return to_nearest;
    }
    facing
}
