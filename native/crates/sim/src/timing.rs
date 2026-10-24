//! `src/sim/timing.ts`: each press in step movement rated against the timing grid and classed, and
//! the groove meter (docs/design/gameplay-redesign.md, "Timing and the Groove"). The groove sets the
//! tier, and the tier the sword's reach and every attack's damage.

// (Midpoints as (a + b) / 2 and the TS's short names: the TS order of operations, bit for bit.)
#![allow(clippy::manual_midpoint, clippy::many_single_char_names)]

use indexmap::{IndexMap, IndexSet};

use crate::content::types::HeroDef;
use crate::core::shaping::clamp;
use crate::jsmath::{abs, max, sign};
use crate::movement::InputKey;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Rating {
    Perfect,
    Great,
    Good,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PressRating {
    /// Press time minus the nearest grid time, in song seconds.
    pub delta: f64,
    /// None for presses outside the Good window.
    pub rating: Option<Rating>,
}

/// Rates a press `delta` song seconds from the nearest grid time.
pub fn rate_press(hero: &HeroDef, delta: f64) -> PressRating {
    let t = &hero.timing;
    let ms = abs(delta) * 1000.0;
    let rating = if ms <= t.perfect_ms {
        Some(Rating::Perfect)
    } else if ms <= t.great_ms {
        Some(Rating::Great)
    } else if ms <= t.good_ms {
        Some(Rating::Good)
    } else {
        None
    };
    PressRating { delta, rating }
}

/// Index of the grid time nearest s in a sorted list (-1 for an empty list).
pub fn nearest_index(times: &[f64], s: f64) -> isize {
    if times.is_empty() {
        return -1;
    }
    let mut lo = 0usize;
    let mut hi = times.len() - 1;
    while hi - lo > 1 {
        let mid = (lo + hi) >> 1;
        if times[mid] <= s {
            lo = mid;
        } else {
            hi = mid;
        }
    }
    if abs(times[hi] - s) < abs(times[lo] - s) {
        hi as isize
    } else {
        lo as isize
    }
}

/// Rates a press at song time s against a sorted, non-empty grid; also returns the grid index.
pub fn rate_press_on_grid(hero: &HeroDef, s: f64, grid: &[f64]) -> (PressRating, usize) {
    let i = nearest_index(grid, s);
    assert!(i >= 0, "rating a press against an empty grid");
    let i = i as usize;
    (rate_press(hero, s - grid[i]), i)
}

/// Index of the first grid time at or after s (within 1e-9; `grid.len()` if none).
pub fn first_at_or_after(grid: &[f64], s: f64) -> usize {
    let mut lo = 0usize;
    let mut hi = grid.len();
    while lo < hi {
        let mid = (lo + hi) >> 1;
        if grid[mid] < s - 1e-9 {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    lo
}

/// A counted press's rating: beyond the Good window it is loose.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum CountedRating {
    Perfect,
    Great,
    Good,
    Loose,
}

/// What a press is, judged against the grid (`PressClass`).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum PressClass {
    Partner,
    Offbeat,
    Counted,
    Double,
    Stray,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PressJudgement {
    pub class: PressClass,
    /// Counted presses: the rating (loose beyond Good); off-beat presses: Perfect or Great.
    pub rating: Option<CountedRating>,
    /// Offset (song seconds, after the drift) from the nearest grid time, or from the off-beat.
    pub delta: f64,
    /// The grid time the press counts for (counted, double tap, stray) or follows (off-beat).
    pub grid_index: isize,
    /// The groove change it makes.
    pub change: f64,
    /// The drift taken off the press's time before it was judged (+ for a late player).
    pub drift: f64,
}

/// The keys that drift together: the directions, and the stomp key.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum KeyGroup {
    Direction,
    Stomp,
}

pub fn key_group(key: InputKey) -> KeyGroup {
    if key == InputKey::Space {
        KeyGroup::Stomp
    } else {
        KeyGroup::Direction
    }
}

/// A grid time's counted press: at its own time (before the drift), and its rating.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct CountedPress {
    pub s: f64,
    pub rating: CountedRating,
}

/// What the classes remember of earlier presses (`PressMemo`): each grid time's counted press, the
/// off-beats taken, and each key group's latest counted offsets (oldest first: the drift). Grid
/// times are kept by key (`classify_press`'s `key_of`), in insertion order as TS's Map and Set.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PressMemo {
    pub counted: IndexMap<i64, CountedPress>,
    pub offbeats: IndexSet<i64>,
    pub recent_direction: Vec<f64>,
    pub recent_stomp: Vec<f64>,
}

impl PressMemo {
    pub fn recent(&self, group: KeyGroup) -> &Vec<f64> {
        match group {
            KeyGroup::Direction => &self.recent_direction,
            KeyGroup::Stomp => &self.recent_stomp,
        }
    }

    fn recent_mut(&mut self, group: KeyGroup) -> &mut Vec<f64> {
        match group {
            KeyGroup::Direction => &mut self.recent_direction,
            KeyGroup::Stomp => &mut self.recent_stomp,
        }
    }
}

/// The drift: the median of the group's latest counted offsets, once there are enough, less a dead
/// zone, within ±maxMs; 0 before.
pub fn drift_of(hero: &HeroDef, memo: &PressMemo, group: KeyGroup) -> f64 {
    let d = &hero.meter.drift;
    let r = memo.recent(group);
    if (r.len() as f64) < max(1.0, d.min_presses) {
        return 0.0;
    }
    let mut sorted = r.clone();
    // (JS's stable sort by a - b: -0 and 0 are equal and keep their order.)
    sorted.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    let m = sorted.len() >> 1;
    let median = if sorted.len() % 2 == 1 {
        sorted[m]
    } else {
        (sorted[m - 1] + sorted[m]) / 2.0
    };
    let beyond = sign(median) * max(0.0, abs(median) - d.dead_ms / 1000.0);
    clamp(beyond, -d.max_ms / 1000.0, d.max_ms / 1000.0)
}

/// A counted press's rating from its offset to the grid.
pub fn counted_rating(hero: &HeroDef, delta: f64) -> CountedRating {
    let ms = abs(delta) * 1000.0;
    let t = &hero.timing;
    if ms <= t.perfect_ms {
        CountedRating::Perfect
    } else if ms <= t.great_ms {
        CountedRating::Great
    } else if ms <= t.good_ms {
        CountedRating::Good
    } else {
        CountedRating::Loose
    }
}

fn counted_change(hero: &HeroDef, r: CountedRating) -> f64 {
    let c = &hero.meter.counted;
    match r {
        CountedRating::Perfect => c.perfect,
        CountedRating::Great => c.great,
        CountedRating::Good => c.good,
        CountedRating::Loose => c.loose,
    }
}

/// JS's `grid[i]` for an index that may be -1 or past the end: undefined, which reads as NaN.
fn at(grid: &[f64], i: isize) -> f64 {
    if i < 0 {
        f64::NAN
    } else {
        grid.get(i as usize).copied().unwrap_or(f64::NAN)
    }
}

/// Classes a press at song time s against a sorted grid, in the design's order, and remembers what
/// it takes. `partner`: the press bends or rocks the one before. The press is judged at s less its
/// key group's drift. `key_of` maps a grid index to the key the memory keeps it by (the index
/// itself, unless the grid changes from press to press).
pub fn classify_press(
    hero: &HeroDef,
    grid: &[f64],
    s: f64,
    partner: bool,
    memo: &mut PressMemo,
    group: KeyGroup,
    key_of: &mut dyn FnMut(isize) -> i64,
) -> PressJudgement {
    let m = &hero.meter;
    let drift = drift_of(hero, memo, group);
    let sj = s - drift;
    let gi = nearest_index(grid, sj);
    if partner {
        return PressJudgement {
            class: PressClass::Partner,
            rating: None,
            delta: sj - at(grid, gi),
            grid_index: gi,
            change: 0.0,
            drift,
        };
    }
    // The off-beat nearest the press: half-way between the grid time at or before it and the next.
    let n = grid.len() as isize;
    let mut i = gi;
    if at(grid, i) > sj && i > 0 {
        i -= 1;
    }
    let near = |k: isize| {
        if k >= 0 && k + 1 < n {
            abs(sj - (grid[k as usize] + grid[k as usize + 1]) / 2.0)
        } else {
            f64::INFINITY
        }
    };
    if near(i - 1) < near(i) {
        i -= 1;
    }
    if i >= 0 && i + 1 < n {
        let off = sj - (grid[i as usize] + grid[i as usize + 1]) / 2.0;
        let counted = memo.counted.get(&key_of(i)).copied();
        if abs(off) * 1000.0 <= hero.timing.great_ms
            && !memo.offbeats.contains(&key_of(i))
            && counted.is_some_and(|c| c.rating != CountedRating::Loose)
        {
            memo.offbeats.insert(key_of(i));
            let (rating, change) = if abs(off) * 1000.0 <= hero.timing.perfect_ms {
                (CountedRating::Perfect, m.offbeat.perfect)
            } else {
                (CountedRating::Great, m.offbeat.great)
            };
            return PressJudgement {
                class: PressClass::Offbeat,
                rating: Some(rating),
                delta: off,
                grid_index: i,
                change,
                drift,
            };
        }
    }
    let delta = sj - at(grid, gi);
    let Some(counted) = memo.counted.get(&key_of(gi)).copied() else {
        let rating = counted_rating(hero, delta);
        memo.counted.insert(key_of(gi), CountedPress { s, rating });
        // A loose press is too far off to say how late the player runs.
        if rating != CountedRating::Loose {
            let offset = s - at(grid, gi);
            let r = memo.recent_mut(group);
            r.push(offset);
            if (r.len() as f64) > m.drift.presses {
                r.remove(0);
            }
        }
        return PressJudgement {
            class: PressClass::Counted,
            rating: Some(rating),
            delta,
            grid_index: gi,
            change: counted_change(hero, rating),
            drift,
        };
    };
    if s - counted.s <= m.double_tap_ms / 1000.0 + 1e-9 {
        return PressJudgement {
            class: PressClass::Double,
            rating: None,
            delta,
            grid_index: gi,
            change: m.double_tap,
            drift,
        };
    }
    PressJudgement {
        class: PressClass::Stray,
        rating: None,
        delta,
        grid_index: gi,
        change: m.stray,
        drift,
    }
}

/// The identity key: a grid index kept as itself.
pub fn index_key(gi: isize) -> i64 {
    gi as i64
}

/// Never late: a sound plays exactly on its slot only when it is known at least this long before it.
pub const NEVER_LATE: f64 = 0.06;

/// The groove after a change, on its 0-to-1 scale.
pub fn groove_after_change(groove: f64, change: f64) -> f64 {
    clamp(groove + change, 0.0, 1.0)
}

/// The drain over one beat of a bar of `beats_per_bar` beats: full to empty in drain.emptyInBars bars.
pub fn drain_per_beat(hero: &HeroDef, beats_per_bar: f64) -> f64 {
    1.0 / (hero.meter.drain.empty_in_bars * max(1.0, beats_per_bar))
}

/// The tier for a groove, from the tier before: it rises when the groove reaches a threshold, and
/// drops only when the groove falls `hysteresis` below one.
pub fn tier_for(hero: &HeroDef, tier: f64, groove: f64) -> f64 {
    let th = &hero.meter.tiers;
    let n = th.len() as f64;
    let mut t = tier;
    // (JS reads th[t] for a whole t; the TS callers only pass whole tiers.)
    let get = |k: f64| {
        if k >= 0.0 && k < n && k.fract() == 0.0 {
            th[k as usize]
        } else {
            f64::NAN
        }
    };
    while t < n && groove >= get(t) - 1e-9 {
        t += 1.0;
    }
    while t > 0.0 && groove < get(t - 1.0) - hero.meter.hysteresis - 1e-9 {
        t -= 1.0;
    }
    t
}
