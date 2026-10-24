//! `src/core/songmap.ts`: one recording's musical structure and its index; the only place
//! seconds convert to bars and beats. Bar and beat numbers stay f64, as in JS, and reading past an
//! array gives NaN as JS's `undefined` does (`super::at`).

#![allow(clippy::manual_midpoint)] // (the TS binary search, as written)

use super::at;
use crate::jsmath::floor;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Feel {
    Pulsed,
    Free,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Anchor {
    pub time: f64,
    pub strength: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Accent {
    pub time: f64,
    pub strength: f64,
    pub label: Option<String>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct SongSection {
    pub label: String,
    /// Inclusive 1-based bar range.
    pub bars: [f64; 2],
    pub feel: Feel,
    pub key: Option<String>,
    pub anchors: Option<Vec<Anchor>>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MeterRegion {
    pub from_bar: f64,
    pub beats_per_bar: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct SongMap {
    /// The song's name (`reactive` for a reactive song map).
    pub song: String,
    /// The audio's length in seconds (`audio.duration`).
    pub duration: f64,
    pub meter: Vec<MeterRegion>,
    /// One time per beat, strictly increasing.
    pub beats: Vec<f64>,
    /// Which beat is beat 1 of bar 1; earlier beats are a pickup.
    pub first_downbeat_index: f64,
    pub sections: Vec<SongSection>,
    /// Inclusive bar ranges of musical phrases.
    pub phrases: Vec<[f64; 2]>,
    /// 0–1 per beat.
    pub energy: Vec<f64>,
    pub accents: Vec<Accent>,
}

/// A 1-based musical position.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BarBeat {
    pub bar: f64,
    pub beat: f64,
}

/// Index structures derived once from a song map.
#[derive(Clone, Debug, PartialEq)]
pub struct SongIndex {
    /// Beat index of the first beat of each 1-based bar (`bar_start[0]` is NaN).
    pub bar_start: Vec<f64>,
    pub beats_per_bar: Vec<f64>,
    pub bar_count: f64,
}

pub fn index_song(map: &SongMap) -> SongIndex {
    let mut bar_start = vec![f64::NAN];
    let mut beats_per_bar = vec![f64::NAN];
    let mut beat = map.first_downbeat_index;
    let mut bar = 1.0;
    let mut meters = map.meter.clone();
    meters.sort_by(|a, b| {
        (a.from_bar - b.from_bar)
            .partial_cmp(&0.0)
            .unwrap_or(std::cmp::Ordering::Equal)
    });
    while beat < map.beats.len() as f64 {
        let mut bpb = meters[0].beats_per_bar;
        for m in &meters {
            if m.from_bar <= bar {
                bpb = m.beats_per_bar;
            }
        }
        // (bar counts up from 1 by one, so it is always the next index.)
        bar_start.push(beat);
        beats_per_bar.push(bpb);
        beat += bpb;
        bar += 1.0;
    }
    SongIndex {
        bar_start,
        beats_per_bar,
        bar_count: bar - 1.0,
    }
}

/// Fractional 0-based beat index at song time s (piecewise-linear, extrapolated).
pub fn beat_index_at(map: &SongMap, s: f64) -> f64 {
    let b = &map.beats;
    let n = b.len();
    let bf = |i: f64| at(b, i);
    let nf = n as f64;
    if s <= bf(0.0) {
        return (s - bf(0.0)) / (bf(1.0) - bf(0.0));
    }
    if s >= bf(nf - 1.0) {
        return nf - 1.0 + (s - bf(nf - 1.0)) / (bf(nf - 1.0) - bf(nf - 2.0));
    }
    if n < 2 {
        // (JS: every read past the array is undefined, so the result is NaN.)
        return f64::NAN;
    }
    let mut lo = 0usize;
    let mut hi = n - 1;
    while hi - lo > 1 {
        let mid = (lo + hi) >> 1;
        if b[mid] <= s {
            lo = mid;
        } else {
            hi = mid;
        }
    }
    lo as f64 + (s - b[lo]) / (b[hi] - b[lo])
}

/// Song time of a fractional beat index (piecewise-linear, extrapolated).
pub fn time_of_beat_index(map: &SongMap, i: f64) -> f64 {
    let b = &map.beats;
    let bf = |i: f64| at(b, i);
    let nf = b.len() as f64;
    if i <= 0.0 {
        return bf(0.0) + i * (bf(1.0) - bf(0.0));
    }
    if i >= nf - 1.0 {
        return bf(nf - 1.0) + (i - (nf - 1.0)) * (bf(nf - 1.0) - bf(nf - 2.0));
    }
    let k = floor(i);
    bf(k) + (i - k) * (bf(k + 1.0) - bf(k))
}

/// Beat index of a 1-based bar and (possibly fractional) 1-based beat.
pub fn beat_index_of(map: &SongMap, idx: &SongIndex, pos: BarBeat) -> f64 {
    if pos.bar < 1.0 {
        return map.first_downbeat_index
            + (pos.bar - 1.0) * at(&idx.beats_per_bar, 1.0)
            + (pos.beat - 1.0);
    }
    if pos.bar > idx.bar_count {
        let last = idx.bar_count;
        return at(&idx.bar_start, last)
            + (pos.bar - last) * at(&idx.beats_per_bar, last)
            + (pos.beat - 1.0);
    }
    at(&idx.bar_start, pos.bar) + (pos.beat - 1.0)
}

pub fn time_of(map: &SongMap, idx: &SongIndex, pos: BarBeat) -> f64 {
    time_of_beat_index(map, beat_index_of(map, idx, pos))
}

/// 1-based bar containing beat index i (floored).
pub fn bar_of_beat_index(idx: &SongIndex, i: f64) -> f64 {
    let k = floor(i + 1e-9);
    let mut lo = 1.0;
    let mut hi = idx.bar_count;
    if k < at(&idx.bar_start, 1.0) {
        return 0.0;
    }
    while lo < hi {
        let mid = floor((lo + hi + 1.0) / 2.0);
        if at(&idx.bar_start, mid) <= k {
            lo = mid;
        } else {
            hi = mid - 1.0;
        }
    }
    lo
}

/// Energy (0–1) at song time s, interpolated between beats.
pub fn energy_at(map: &SongMap, s: f64) -> f64 {
    let i = beat_index_at(map, s);
    let e = &map.energy;
    let ef = |i: f64| at(e, i);
    let nf = e.len() as f64;
    if i <= 0.0 {
        return ef(0.0);
    }
    if i >= nf - 1.0 {
        return ef(nf - 1.0);
    }
    let k = floor(i);
    ef(k) + (i - k) * (ef(k + 1.0) - ef(k))
}

pub fn section_at_bar(map: &SongMap, bar: f64) -> Option<&SongSection> {
    map.sections
        .iter()
        .find(|sec| bar >= sec.bars[0] && bar <= sec.bars[1])
}

/// Song time of the start of a section's first bar and the end of its last bar.
pub fn section_times(map: &SongMap, idx: &SongIndex, sec: &SongSection) -> (f64, f64) {
    (
        time_of(
            map,
            idx,
            BarBeat {
                bar: sec.bars[0],
                beat: 1.0,
            },
        ),
        time_of(
            map,
            idx,
            BarBeat {
                bar: sec.bars[1] + 1.0,
                beat: 1.0,
            },
        ),
    )
}

pub fn section_at_time<'a>(map: &'a SongMap, idx: &SongIndex, s: f64) -> &'a SongSection {
    let bar = bar_of_beat_index(idx, beat_index_at(map, s));
    let secs = &map.sections;
    if bar < secs[0].bars[0] {
        return &secs[0];
    }
    section_at_bar(map, bar).unwrap_or(&secs[secs.len() - 1])
}
