//! Delayed listening's committed beats (docs/design/native-app.md, "Committed beats from
//! hindsight (E1)"): the music is heard `M` late, so beats are committed from hindsight grids,
//! which hear both sides of each beat, instead of forecasts.
//!
//! - A hindsight entry usable at analysis position `T + D` (its `known_at`) is stamped
//!   `k = T + D − M + A` and commits its grid's beats in the band `[k + H, k + every + H)` that
//!   come at least half a period after the last committed beat, known at `k`. `H` is the commit
//!   horizon (`commit.periods` periods plus `commit.extraS`) at the grid's local period (its median
//!   spacing), or Direct listening's believed period where the grid has fewer than 2 beats.
//! - **Fallback:** where the grid has no beat (fewer than 2 beats in the entry, or a gap of more
//!   than 1.5 periods, the band's ends counting as beats half a period outside it), Direct
//!   listening's forecasts fill the band, known at `k` with the grid's (E0 stamped them `M − A`
//!   before the position that committed them; `k` is later, and still `H` ahead of the band).
//!   At the stream's end, Direct's forecasts past the last band follow, known at the end's
//!   position less `M − A`, so the beat stream never stalls.
//! - So every committed beat is known `M − A` before the audio position that committed it:
//!   `known_at + M − A` is that position, which the stream causality check matches on.
//! - **The lead:** a beat that can't be known `H` (at its own period) before it falls isn't
//!   committed. Grid beats always can; a forecast at a longer period than the grid's may not
//!   (E0's earlier stamps kept them, but a stamp before the decode that chooses the beat can't be
//!   met live).
//!
//! The rule is E0's replay (`tools/analysis`, `ps_analysis/delayed.py`), streamed: Direct
//! listening's commits for a band are all made by the time its entry is usable when
//! `M >= every + A`, so a band never waits. [`replay`] runs it over a Direct run's entries, which
//! gives exactly the analyzer's Delayed beats (both feed it in the analyzer's emission order).

use std::collections::VecDeque;

use crate::analyzer::Entry;
use crate::beats::{BeatParams, Commit};
use crate::hindsight::{HindsightEntry, HindsightParams, phase_shift};

/// The capture → arrival target `A` (docs/design/native-app.md, "Targets"; S0's 48 ms).
pub const ARRIVAL_S: f64 = 0.048;

/// The grid a hindsight entry commits from.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Grid {
    /// The causal tracker's believed beats.
    Online,
    /// The window's offline decode.
    Offline,
    /// The believed beats moved onto the offline decode's phase by the phase check.
    Phased,
}

impl Grid {
    pub const ALL: [Grid; 3] = [Grid::Online, Grid::Offline, Grid::Phased];

    pub fn name(self) -> &'static str {
        match self {
            Grid::Online => "online",
            Grid::Offline => "offline",
            Grid::Phased => "phased",
        }
    }
}

/// Delayed listening's parameters.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct DelayedParams {
    /// How late the music is heard (s).
    pub m: f64,
    /// The capture → arrival target (s).
    pub a: f64,
    pub grid: Grid,
}

impl DelayedParams {
    /// E1's choice: offline hindsight at 3 s (native/evidence/e1/).
    pub fn chosen() -> Self {
        DelayedParams {
            m: 3.0,
            a: ARRIVAL_S,
            grid: Grid::Offline,
        }
    }
}

/// How the analyzer listens.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub enum Listening {
    /// Committed beats are forecasts, stamped when committed (Python's stream).
    #[default]
    Direct,
    /// The music is heard `m` late: committed beats come from hindsight.
    Delayed(DelayedParams),
}

/// The median spacing of a grid's beats (numpy's median), or None with fewer than 2.
fn local_period(beats: &[f64]) -> Option<f64> {
    if beats.len() < 2 {
        return None;
    }
    let mut d: Vec<f64> = beats.windows(2).map(|w| w[1] - w[0]).collect();
    d.sort_by(f64::total_cmp);
    let n = d.len();
    Some(if n % 2 == 1 {
        d[n / 2]
    } else {
        f64::midpoint(d[n / 2 - 1], d[n / 2])
    })
}

/// The parts of the band `[lo, hi)` where a grid has no beat.
fn uncovered(beats: &[f64], per: f64, lo: f64, hi: f64) -> Vec<(f64, f64)> {
    if beats.len() < 2 {
        return vec![(lo, hi)];
    }
    let mut edges = vec![lo - 0.5 * per];
    edges.extend(
        beats
            .iter()
            .copied()
            .filter(|&b| lo - 0.5 * per < b && b < hi + 0.5 * per),
    );
    edges.push(hi + 0.5 * per);
    edges
        .windows(2)
        .filter(|w| w[1] - w[0] > 1.5 * per + 1e-9)
        .map(|w| (w[0] + 0.5 * per, w[1] - 0.5 * per))
        .collect()
}

/// The delayed commit rule, streaming.
pub struct Delayed {
    p: DelayedParams,
    h: HindsightParams,
    commit_periods: f64,
    commit_extra_s: f64,
    /// Direct listening's commits not yet past a band (their `known_at`: the position that
    /// committed them).
    direct: VecDeque<Commit>,
    /// The latest usable entries, for the phase check (oldest first).
    usable: VecDeque<HindsightEntry>,
    /// The last committed beat's time, the latest stamp, and the last band's end.
    last_t: f64,
    last_known: f64,
    hi: f64,
    /// Direct's believed period by the latest commit.
    period: f64,
}

impl Delayed {
    pub fn new(p: DelayedParams, h: &HindsightParams, b: &BeatParams) -> Self {
        Delayed {
            p,
            h: h.clone(),
            commit_periods: b.commit_periods,
            commit_extra_s: b.commit_extra_s,
            direct: VecDeque::new(),
            usable: VecDeque::new(),
            last_t: f64::NEG_INFINITY,
            last_known: f64::NEG_INFINITY,
            hi: f64::NEG_INFINITY,
            period: 0.5,
        }
    }

    /// A Direct listening commit (a forecast), in order.
    pub fn push_direct(&mut self, c: Commit) {
        self.period = c.period;
        self.direct.push_back(c);
    }

    /// A Direct forecast committed from audio position `at`.
    fn forecast(&self, c: &Commit, at: f64) -> Commit {
        Commit {
            known_at: at - self.p.m + self.p.a,
            ..*c
        }
    }

    /// Commits candidates in time order, each half a period after the last and known `H` ahead,
    /// stamps never going back.
    fn commit(&mut self, mut cands: Vec<Commit>, out: &mut Vec<Commit>) {
        cands.sort_by(|a, b| a.t.total_cmp(&b.t));
        for c in cands {
            let lead = self.commit_periods * c.period + self.commit_extra_s;
            if c.t >= self.last_t + 0.5 * c.period - 1e-9
                && c.t - c.known_at.max(self.last_known) >= lead - 1e-9
            {
                let known_at = c.known_at.max(self.last_known);
                out.push(Commit { known_at, ..c });
                self.last_t = c.t;
                self.last_known = known_at;
            }
        }
    }

    /// A hindsight entry has become usable: its band's beats.
    pub fn entry(&mut self, e: &HindsightEntry, out: &mut Vec<Commit>) {
        self.usable.push_back(e.clone());
        while self.usable.len() > self.h.phase_persist.max(1) {
            self.usable.pop_front();
        }
        let k = e.known_at - self.p.m + self.p.a;
        let mut beats = match self.p.grid {
            Grid::Offline => e.offline.clone(),
            Grid::Online | Grid::Phased => e.online.clone(),
        };
        beats.sort_by(f64::total_cmp);
        if self.p.grid == Grid::Phased
            && let Some(per) = local_period(&beats)
        {
            self.usable.make_contiguous();
            let last = *beats.last().expect("beats");
            let shift = phase_shift(self.usable.as_slices().0, last, per, &self.h);
            for b in &mut beats {
                *b += shift;
            }
        }
        let per = local_period(&beats).unwrap_or(self.period);
        let h = self.commit_periods * per + self.commit_extra_s;
        let (lo, hi) = (k + h, k + self.h.every_s + h);
        self.hi = hi;
        let gaps = uncovered(&beats, per, lo, hi);
        let mut cands: Vec<Commit> = beats
            .iter()
            .filter(|&&b| lo <= b && b < hi)
            .map(|&t| Commit {
                t,
                known_at: k,
                period: per,
                evidence: self.direct.back().map_or(0.0, |c| c.evidence),
            })
            .collect();
        cands.extend(
            self.direct
                .iter()
                .filter(|c| {
                    lo <= c.t && c.t < hi && gaps.iter().any(|&(x, y)| x <= c.t && c.t <= y)
                })
                .map(|c| self.forecast(c, e.known_at)),
        );
        self.commit(cands, out);
        // (Direct's beats before this band's end are spent.)
        while self.direct.front().is_some_and(|c| c.t < hi) {
            self.direct.pop_front();
        }
    }

    /// The stream's end, at audio position `end`: Direct's forecasts past the last band.
    pub fn finish(&mut self, end: f64, out: &mut Vec<Commit>) {
        let hi = self.hi;
        let cands: Vec<Commit> = self
            .direct
            .iter()
            .filter(|c| c.t >= hi)
            .map(|c| self.forecast(c, end))
            .collect();
        self.direct.clear();
        self.commit(cands, out);
    }
}

/// Delayed listening's committed beats from a Direct listening run's entries (in the analyzer's
/// emission order, its stream's end included): its forecasts and its hindsight entries; `end` is
/// the stream's length (s).
pub fn replay(
    entries: &[Entry],
    end: f64,
    params: DelayedParams,
    hindsight: &HindsightParams,
    beats: &BeatParams,
) -> Vec<Commit> {
    let mut rule = Delayed::new(params, hindsight, beats);
    let mut out = Vec::new();
    for entry in entries {
        match entry {
            Entry::Beat(c) => rule.push_direct(*c),
            Entry::Hindsight(x) => rule.entry(x, &mut out),
            _ => {}
        }
    }
    rule.finish(end, &mut out);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn local_period_is_the_median_spacing() {
        assert_eq!(local_period(&[0.0, 0.5, 1.0, 2.0]), Some(0.5));
        assert_eq!(local_period(&[0.0, 0.5, 1.5, 2.0, 3.0]), Some(0.75));
        assert_eq!(local_period(&[1.0]), None);
    }

    #[test]
    fn uncovered_finds_gaps_and_empty_bands() {
        assert_eq!(uncovered(&[1.0], 0.5, 0.0, 1.0), vec![(0.0, 1.0)]);
        // A steady grid covers the band.
        let grid: Vec<f64> = (0..10).map(|i| f64::from(i) * 0.5).collect();
        assert!(uncovered(&grid, 0.5, 1.0, 2.0).is_empty());
        // A missing stretch of two beats leaves a gap between the beats around it.
        let holes = [0.0, 0.5, 1.0, 2.5, 3.0];
        assert_eq!(uncovered(&holes, 0.5, 0.0, 3.0), vec![(1.25, 2.25)]);
    }

    fn entry(known_at: f64, offline: Vec<f64>) -> HindsightEntry {
        HindsightEntry {
            known_at,
            window_end: known_at - 0.67,
            online: offline.clone(),
            offline,
        }
    }

    fn direct(t: f64, known_at: f64) -> Commit {
        Commit {
            t,
            known_at,
            period: 0.5,
            evidence: 0.5,
        }
    }

    #[test]
    fn a_band_commits_its_grid_and_fills_its_gaps_with_forecasts() {
        let p = DelayedParams {
            m: 3.0,
            a: ARRIVAL_S,
            grid: Grid::Offline,
        };
        let mut d = Delayed::new(p, &HindsightParams::live(), &BeatParams::default());
        // Direct's forecasts every half second, each committed 0.85 s ahead.
        for i in 0..40 {
            let t = f64::from(i) * 0.5;
            d.push_direct(direct(t + 0.01, t - 0.85));
        }
        // An entry at 10 s whose grid lacks the beats around 7.5 s.
        let grid: Vec<f64> = (0..21)
            .map(|i| f64::from(i) * 0.5)
            .filter(|b| (*b - 7.5).abs() > 0.3 && (*b - 8.0).abs() > 0.3)
            .collect();
        let mut out = Vec::new();
        d.entry(&entry(10.0, grid), &mut out);
        let k = 10.0 - 3.0 + ARRIVAL_S;
        let h = 0.5 + 0.35;
        assert!(!out.is_empty());
        for c in &out {
            assert!(
                c.t >= k + h - 1e-9 && c.t < k + 1.0 + h,
                "{c:?} outside the band"
            );
            // Every beat is known at least H before it falls.
            assert!(c.t - c.known_at >= h - 1e-9, "{c:?} has too little lead");
        }
        // The gap at 7.5–8 s is filled from Direct's forecasts (their times are 10 ms later),
        // known with the grid's beats.
        assert!(out.iter().any(|c| (c.t - 8.01).abs() < 1e-9), "{out:?}");
        assert!(
            out.iter().all(|c| (c.known_at - k).abs() < 1e-12),
            "{out:?}"
        );
        // Stamps never go back.
        assert!(out.windows(2).all(|w| w[1].known_at >= w[0].known_at));
    }
}
