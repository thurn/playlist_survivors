//! `src/reactive/director.ts`: what drives the horde in a reactive level in place of the song map's
//! sections. (The zone-wave plan and the sound palette's switches come with B6 and B7.)

use indexmap::IndexMap;
use stream::{Level, StreamLog};

use crate::content::level::{ByLevel, Director};
use crate::jsmath::{max, min};

/// The target population at song time s: min(max, (start + perMinute * s / 60) * scale).
pub fn director_population(d: &Director, s: f64, scale: f64) -> f64 {
    let p = &d.population;
    min(p.max, (p.start + (p.per_minute * s) / 60.0) * scale)
}

/// The roster of the k-th detected section (0 before the first), holding the last entry.
pub fn section_roster(d: &Director, k: usize) -> &IndexMap<String, f64> {
    &d.sections[k.min(d.sections.len() - 1)]
}

/// A director value by loudness level.
pub fn by_level<T: Copy>(b: &ByLevel<T>, l: Level) -> T {
    match l {
        Level::Quiet => b.quiet,
        Level::Medium => b.medium,
        Level::Loud => b.loud,
    }
}

/// The mean of `value` over the loudness levels of the ticks in the last `window_s` before s (each
/// tick's level holding until the next; medium before the first). `ticks` are the ticks known.
pub fn level_blend(
    log: &StreamLog,
    ticks: usize,
    s: f64,
    window_s: f64,
    value: &dyn Fn(Level) -> f64,
) -> f64 {
    let all = &log.ticks[..ticks];
    let mut i = tick_index_in(all, s);
    if i < 0 {
        return value(Level::Medium);
    }
    let t0 = s - window_s;
    let mut sum = 0.0;
    let mut hi = s;
    while i >= 0 && hi > t0 + 1e-12 {
        let tick = &all[i as usize];
        if tick.known_at > s + 1e-9 {
            i -= 1;
            continue;
        }
        let lo = max(tick.t, t0);
        sum += (hi - lo) * value(tick.level.unwrap_or(Level::Medium));
        hi = lo;
        i -= 1;
    }
    if hi > t0 + 1e-12 {
        sum += (hi - t0) * value(Level::Medium);
    }
    sum / window_s
}

/// `tickIndexAt` over a slice of ticks: the latest at or before s (within 1e-9), -1 before the first.
#[allow(clippy::manual_midpoint)] // (the TS binary search, as written)
pub fn tick_index_in(ticks: &[stream::Tick], s: f64) -> isize {
    let mut lo = 0usize;
    let mut hi = ticks.len();
    while lo < hi {
        let mid = (lo + hi) >> 1;
        if ticks[mid].t <= s + 1e-9 {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    lo as isize - 1
}

#[cfg(test)]
mod knowledge_tests {
    use super::*;
    #[test]
    fn late_tick_cannot_change_camera_before_its_known_time() {
        let mut log = StreamLog::default();
        log.ticks.push(stream::Tick {
            t: 0.0,
            known_at: 0.0,
            evidence: 1.0,
            energy: 0.5,
            level: Some(Level::Medium),
            key: None,
            bands: None,
            brightness: None,
        });
        let value = |l| match l {
            Level::Quiet => 1.3,
            Level::Medium => 1.2,
            Level::Loud => 1.05,
        };
        let before = level_blend(&log, 1, 2.0, 2.0, &value);
        log.ticks.push(stream::Tick {
            t: 1.0,
            known_at: 3.0,
            level: Some(Level::Loud),
            ..log.ticks[0].clone()
        });
        assert_eq!(level_blend(&log, 2, 2.0, 2.0, &value), before);
        assert_eq!(level_blend(&log, 2, 3.1, 2.0, &value), 1.05);
    }
}
