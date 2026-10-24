//! `src/sim/strides.ts`: fixed-speed strides (docs/design/gameplay-redesign.md, "Movement:
//! Fixed-Speed Strides"). The hero moves at one speed whatever the tempo or press rate: ground
//! builds up in a budget as world time passes, never above one stride, and each stride spends all of
//! it. Pure functions of a small state the simulation carries; times are world seconds (grid times
//! song seconds) and distances world pixels.

use crate::content::types::HeroDef;
use crate::jsmath::{hypot, max, min};
use crate::movement::{Clamp, Dir, Key, Step, key_dir};

/// The steps a held key strides on (song times), the stride period at each, and how many steps its
/// grid step has.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct StrideSteps {
    pub s: Vec<f64>,
    pub period: Vec<f64>,
    pub per: Vec<u8>,
    /// Where the last grid time's steps start in `s`: they are provisional, since the last grid time
    /// keeps the step before it until the next grid time arrives.
    tail: usize,
}

impl StrideSteps {
    /// Where the last grid time's (provisional) steps start.
    pub fn tail(&self) -> usize {
        self.tail
    }

    /// The steps for `grid`, which extends the grid these steps were built from (`from` is its old
    /// length): the provisional last grid time's steps are rebuilt, then the new ones appended.
    pub fn extend(&mut self, grid: &[f64], from: usize, max_period_s: f64) {
        let first = if from == 0 {
            0
        } else {
            self.s.truncate(self.tail);
            self.period.truncate(self.tail);
            self.per.truncate(self.tail);
            from - 1
        };
        for i in first..grid.len() {
            self.tail = self.s.len();
            let g0 = grid[i];
            let len = if i + 1 < grid.len() {
                grid[i + 1] - g0
            } else if i > 0 {
                g0 - grid[i - 1]
            } else {
                max_period_s
            };
            let mut n: u32 = 1;
            while len / f64::from(n) > max_period_s + 1e-9 {
                n *= 2;
            }
            for k in 0..n {
                self.s.push(g0 + (f64::from(k) * len) / f64::from(n));
                self.period.push(len / f64::from(n));
                // (TS stores it in a Uint8Array: the count wraps at 256.)
                self.per.push((n & 0xff) as u8);
            }
        }
    }
}

/// Every grid time, plus equal subdivisions of each grid step longer than `max_period_s`: the step
/// is halved until it is not. The last grid time keeps the step before it.
pub fn stride_steps(grid: &[f64], max_period_s: f64) -> StrideSteps {
    let mut out = StrideSteps::default();
    out.extend(grid, 0, max_period_s);
    out
}

/// The stride rule from the hero (`StrideRule`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StrideRule {
    /// The walk speed: px per world second.
    pub speed: f64,
    pub duration_s: f64,
    pub hop: f64,
    pub max_period_s: f64,
    pub free_period_s: f64,
    /// Two opposite keys this close together (s) make one stomp.
    pub stomp_s: f64,
}

pub fn stride_rule(hero: &HeroDef) -> StrideRule {
    StrideRule {
        speed: hero.walk.speed,
        duration_s: hero.stride.duration_ms / 1000.0,
        hop: hero.stride.hop,
        max_period_s: hero.stride.max_period_s,
        free_period_s: hero.stride.free_period_s,
        stomp_s: hero.stride.stomp_ms / 1000.0,
    }
}

/// The stride period at song time s in a section with a steady beat: the period of the step it
/// falls in (0 with no steps).
#[allow(clippy::manual_midpoint)] // (the TS binary search, as written)
pub fn period_at(steps: &StrideSteps, s: f64) -> f64 {
    let t = &steps.s;
    if t.is_empty() {
        return 0.0;
    }
    if s < t[0] {
        return steps.period[0];
    }
    let mut lo = 0usize;
    let mut hi = t.len() - 1;
    while hi > lo {
        let mid = (lo + hi + 1) >> 1;
        if t[mid] <= s + 1e-9 {
            lo = mid;
        } else {
            hi = mid - 1;
        }
    }
    steps.period[lo]
}

/// Song time of the last held step a stomp at song time s cancels: the held strides of one grid
/// step, from the first step at or after s.
pub fn stomp_cancels_until(steps: &StrideSteps, s: f64) -> f64 {
    let t = &steps.s;
    let mut j = 0usize;
    while j < t.len() && t[j] < s - 1e-9 {
        j += 1;
    }
    if j >= t.len() {
        return s;
    }
    let at = if t[j] <= s + 1e-9 {
        j
    } else {
        j.saturating_sub(1)
    };
    // (A count of 0, a Uint8Array's wrap of 256, reads one step back, or undefined before the first.)
    let k = (t.len() as isize - 1).min(j as isize + isize::from(steps.per[at]) - 1);
    if k < 0 { f64::NAN } else { t[k as usize] }
}

/// The direction of the held keys, in the order they were pressed: of two opposite keys the more
/// recent wins, and two perpendicular keys make a diagonal. None when no key is held.
pub fn held_direction(held: &[Key]) -> Option<Dir> {
    let mut x = 0.0;
    let mut y = 0.0;
    for k in held {
        let d = key_dir(*k);
        if d.x == 0.0 {
            y = d.y;
        } else {
            x = d.x;
        }
    }
    let l = hypot(x, y);
    (l > 0.0).then(|| Dir { x: x / l, y: y / l })
}

/// What the simulation carries for strides (`StrideState`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StrideState {
    /// The ground the hero may still cover (px), as of world time `w`.
    pub budget: f64,
    pub w: f64,
    /// World time from which a walk spends the refill (free sections), or infinity.
    pub walk_from: f64,
    /// A stomp cancels the held strides up to this song time.
    pub stomp_until: f64,
}

pub fn fresh_strides() -> StrideState {
    StrideState {
        budget: 0.0,
        w: 0.0,
        walk_from: f64::INFINITY,
        stomp_until: f64::NEG_INFINITY,
    }
}

/// The budget at world time w (at or after st.w): refilled at the walk speed, never above one stride.
pub fn budget_at(rule: &StrideRule, st: &StrideState, w: f64, one_stride: f64) -> f64 {
    let until = min(w, max(st.w, st.walk_from));
    min(one_stride, st.budget + rule.speed * max(0.0, until - st.w))
}

/// Brings the budget up to world time w.
pub fn settle(rule: &StrideRule, st: &mut StrideState, w: f64, one_stride: f64) {
    st.budget = budget_at(rule, st, w, one_stride);
    st.w = w;
}

/// Is the stride still moving at world time w?
pub fn striding(rule: &StrideRule, stride: Option<&Step>, w: f64) -> bool {
    stride.is_some_and(|s| w < s.w0 + rule.duration_s - 1e-9)
}

/// Stops a stride at world time w where it is, at (x, y): the ground it had not covered goes back to
/// the budget (still at most one stride). Returns that ground.
#[allow(clippy::too_many_arguments)] // (the TS signature)
pub fn interrupt(
    rule: &StrideRule,
    st: &mut StrideState,
    stride: &Step,
    w: f64,
    x: f64,
    y: f64,
    one_stride: f64,
    walk_back: f64,
) -> f64 {
    settle(rule, st, w, one_stride);
    // (A rock step that returns past the stride's start, to its press's, pays for that walk too.)
    let back = hypot(stride.ex, stride.ey) - hypot(x - stride.x0, y - stride.y0) - walk_back;
    if back <= 0.0 {
        return 0.0;
    }
    let before = st.budget;
    st.budget = min(one_stride, st.budget + back);
    st.budget - before
}

/// Starts a stride at world time w from (x, y) toward the unit vector `dir`: it spends the whole
/// budget.
#[allow(clippy::too_many_arguments)] // (the TS signature)
pub fn start_stride(
    rule: &StrideRule,
    st: &mut StrideState,
    w: f64,
    x: f64,
    y: f64,
    dir: Dir,
    one_stride: f64,
    key: Option<Key>,
    held: bool,
    walls: &Clamp,
) -> Step {
    settle(rule, st, w, one_stride);
    let len = st.budget;
    st.budget = 0.0;
    let len1 = walls(x, y, dir.x, dir.y, len);
    Step {
        w0: w,
        x0: x,
        y0: y,
        dx: dir.x,
        dy: dir.y,
        len1,
        ex: dir.x * len1,
        ey: dir.y * len1,
        bend_e: None,
        keys: key.into_iter().collect(),
        len: Some(len),
        held: Some(held),
        s0: None,
        press: None,
        rock: None,
    }
}
