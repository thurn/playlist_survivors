//! The reactive song map, its section and schedule, growing as committed beats arrive
//! (docs/design/native-app.md, "Growable inputs"): `src/reactive/songmap.ts`'s `reactiveSongMap`,
//! `src/planner/context.ts`'s section bounds and the parts of `src/sim/schedule.ts`'s
//! `buildSchedule` a reactive level has before its set pieces (the grid, strides, base rate and
//! level-ups). Built to the end of a stream, every array equals the TS batch schedule's.
//!
//! The reactive level fixes most of the schedule (`load_level` refuses anything else): one pulsed
//! section on the beat grid, with no accents, events, time scale or free movement. So its grids,
//! slots and grid slots are one list, one slot per committed beat, and its base rate is 1
//! throughout. Set pieces and the finale come later (B6).

use stream::{StreamEnd, StreamLog};

use crate::content::level::{LevelScript, Movement, TimingGrid};
use crate::core::songmap::{Feel, MeterRegion, SongMap, SongSection, time_of_beat_index};
use crate::jsmath::{ceil, max, round};
use crate::strides::StrideSteps;

/// The reactive song map's name and its one section's label (`REACTIVE_SONG`, `REACTIVE_SECTION`).
pub const REACTIVE_SONG: &str = "reactive";
pub const REACTIVE_SECTION: &str = "reactive";
/// Committed beats make 2-beat bars, counted from the first.
pub const REACTIVE_BEATS_PER_BAR: f64 = 2.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SlotKind {
    /// A step section's timing grid: presses arm swings for these.
    Grid,
    /// A free section's automatic swing.
    Auto,
}

#[derive(Clone, Debug, PartialEq)]
pub struct GridSlot {
    pub s: f64,
    /// Fractional beat index.
    pub bi: f64,
    pub bar: f64,
    /// Position in its bar, in grid steps.
    pub slot: f64,
    pub section: f64,
    /// Strength of an accent within 40 ms (0 if none), and its label.
    pub accent: f64,
    pub accent_label: Option<String>,
    pub kind: SlotKind,
}

/// A section's bounds: song times `[s0, s1)` and beat indices `[b0, b1)`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SectionBounds {
    pub s0: f64,
    pub s1: f64,
    pub b0: f64,
    pub b1: f64,
}

/// A constant world rate from song time `s`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RateStep {
    pub s: f64,
    pub rate: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct LevelUp {
    pub id: String,
    /// When it is scheduled (infinite for a director's, which fall due as the run goes).
    pub s: f64,
    pub xp: f64,
}

/// A reactive song map with no beats yet, grown by [`grow_song_map`].
///
/// The loaded level copies its one section's bar range from the song map it was loaded against,
/// and that range is in the level's canonical value. The oracle's `levelHash` is the level loaded
/// against the whole stream's song map (Replay); live, a level loaded before the stream has a
/// provisional range, which nothing in the schedule reads.
pub fn reactive_song_map(file_duration: f64) -> SongMap {
    SongMap {
        song: REACTIVE_SONG.into(),
        duration: file_duration,
        meter: vec![MeterRegion {
            from_bar: 1.0,
            beats_per_bar: REACTIVE_BEATS_PER_BAR,
        }],
        beats: Vec::new(),
        first_downbeat_index: 0.0,
        sections: vec![SongSection {
            label: REACTIVE_SECTION.into(),
            bars: [1.0, 0.0],
            feel: Feel::Pulsed,
            key: None,
            anchors: None,
        }],
        phrases: Vec::new(),
        energy: Vec::new(),
        accents: Vec::new(),
    }
}

/// Appends the log's committed beats that the song map lacks: each beat's time, and its energy as
/// known when it was committed. The bar count follows.
///
/// A beat's energy is read once, when it is taken in, from the ticks in the log by then. Replay
/// feeds every group to a moment together, so that is TS's energy. A live source must append a
/// tick before a beat it knew no later than the tick, or the beat keeps an older energy.
pub fn grow_song_map(song: &mut SongMap, log: &StreamLog, upto: usize) {
    for b in &log.beats[song.beats.len()..upto] {
        song.beats.push(b.t);
        song.energy.push(log.energy_at(b.known_at));
    }
    song.sections[0].bars[1] = ceil(song.beats.len() as f64 / REACTIVE_BEATS_PER_BAR);
}

/// A reactive level's schedule, growing with the committed beats.
#[derive(Clone, Debug)]
pub struct ReactiveSchedule {
    pub song: SongMap,
    /// The grid: one slot per committed beat (the schedule's `grids[0]`, `slots` and `gridSlots`).
    pub grid_slots: Vec<GridSlot>,
    /// Its times (`gridTimes`, and `strikeTimes`, which are the same).
    pub grid_times: Vec<f64>,
    /// The last grid time's steps are provisional until the next beat arrives.
    pub stride_steps: StrideSteps,
    pub base_steps: Vec<RateStep>,
    pub level_ups: Vec<LevelUp>,
    max_period_s: f64,
    /// The latest committed beat's period (NaN before the first).
    latest_period: f64,
}

impl ReactiveSchedule {
    /// The schedule before any beat: `level` is the reactive level, loaded against a reactive song
    /// map; `file_duration` is the audio's length when known (Replay and File), or infinite (System).
    pub fn new(level: &LevelScript, file_duration: f64) -> ReactiveSchedule {
        assert!(level.reactive, "{} is not a reactive level", level.file);
        let sec = &level.sections[0];
        assert!(
            level.sections.len() == 1
                && sec.movement == Movement::Steps
                && sec.timing_grid == TimingGrid::Beat
                && sec.time_scale.from == sec.time_scale.to
                && level.events.is_empty(),
            "the reactive level's checks were skipped"
        );
        // One section with a constant time scale and no timeScale events: one step at its rate.
        let base_steps = vec![RateStep {
            s: 0.0,
            rate: round(sec.time_scale.from * 1e6) / 1e6,
        }];
        let level_ups = level.director.as_ref().map_or_else(Vec::new, |d| {
            d.level_ups
                .iter()
                .map(|lu| LevelUp {
                    id: "offer".into(),
                    s: f64::INFINITY,
                    xp: lu.xp,
                })
                .collect()
        });
        ReactiveSchedule {
            song: reactive_song_map(file_duration),
            grid_slots: Vec::new(),
            grid_times: Vec::new(),
            stride_steps: StrideSteps::default(),
            base_steps,
            level_ups,
            max_period_s: level.hero.stride.max_period_s,
            latest_period: f64::NAN,
        }
    }

    /// Takes in the log's newly committed beats.
    pub fn grow(&mut self, log: &StreamLog) {
        self.grow_to(log, log.beats.len());
    }

    /// Takes in the log's committed beats up to `upto` (those the simulation has ingested).
    pub fn grow_to(&mut self, log: &StreamLog, upto: usize) {
        let from = self.song.beats.len();
        if upto <= from {
            return;
        }
        grow_song_map(&mut self.song, log, upto);
        self.latest_period = log.beats[upto - 1].period;
        for i in from..upto {
            let bi = i as f64;
            // (TS walks the bars: bar b's beats are 2(b - 1) and 2(b - 1) + 1, slots 0 and 1.)
            let bar = (i / 2 + 1) as f64;
            // (TS's timeOfBeatIndex at a whole index is that beat's time, given two beats; live, the
            // first beat may arrive alone, where it would be NaN.)
            let s = self.song.beats[i];
            self.grid_slots.push(GridSlot {
                s,
                bi,
                bar,
                slot: (i % 2) as f64,
                section: 0.0,
                accent: 0.0,
                accent_label: None,
                kind: SlotKind::Grid,
            });
            self.grid_times.push(s);
        }
        self.stride_steps
            .extend(&self.grid_times, from, self.max_period_s);
    }

    /// The one section's bounds. With the stream's end known (Replay) it runs, as TS's batch
    /// context has it, to one beat past the last committed beat (the last gap extrapolated) and at
    /// least to the end of the audio. Live it runs one period past the latest committed beat (the
    /// live exception for extrapolation), which is provisional.
    pub fn section(&self, end: Option<&StreamEnd>) -> SectionBounds {
        let b1 = self.song.beats.len() as f64;
        let s1 = match end {
            Some(e) => max(time_of_beat_index(&self.song, b1), e.duration),
            None => self
                .song
                .beats
                .last()
                .map_or(f64::NAN, |t| t + self.latest_period),
        };
        SectionBounds {
            s0: 0.0,
            s1,
            b0: 0.0,
            b1,
        }
    }
}

/// Rate of a step function at song time s (`stepRate`).
#[allow(clippy::manual_midpoint)] // (the TS binary search, as written)
pub fn step_rate(steps: &[RateStep], s: f64) -> f64 {
    let (mut lo, mut hi) = (0usize, steps.len() - 1);
    while lo < hi {
        let mid = (lo + hi + 1) >> 1;
        if steps[mid].s <= s {
            lo = mid;
        } else {
            hi = mid - 1;
        }
    }
    steps[lo].rate
}

/// Song time of the next step boundary after s, infinite if none (`nextStepAfter`).
#[allow(clippy::manual_midpoint)]
pub fn next_step_after(steps: &[RateStep], s: f64) -> f64 {
    let (mut lo, mut hi) = (0usize, steps.len());
    while lo < hi {
        let mid = (lo + hi) >> 1;
        if steps[mid].s <= s + 1e-12 {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    if lo < steps.len() {
        steps[lo].s
    } else {
        f64::INFINITY
    }
}

/// World time between song times s0 and s1 (s0 <= s1) at the steps' rates (`stepSpan`).
pub fn step_span(steps: &[RateStep], s0: f64, s1: f64) -> f64 {
    let mut w = 0.0;
    let mut a = s0;
    while a < s1 {
        let b = crate::jsmath::min(s1, next_step_after(steps, a));
        w += (b - a) * step_rate(steps, a);
        a = b;
    }
    w
}
