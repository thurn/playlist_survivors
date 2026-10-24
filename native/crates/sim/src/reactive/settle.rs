//! `src/reactive/settle.ts`: reactive mode's settled judgment. Each press is judged again 2 s after
//! it, against a reading of the latest hindsight grid chosen from the player's own presses. A pure
//! step, so its state lives in the simulation's state and snapshots carry it.

#![allow(clippy::manual_midpoint)] // (the TS arithmetic, as written)

use stream::Hindsight;

use crate::content::types::HeroDef;
use crate::jsmath::{abs, max, min, sqrt};
use crate::timing::{
    CountedRating, KeyGroup, PressClass, PressJudgement, PressMemo, classify_press,
};

/// How long after its press a press settles.
pub const SETTLE_DELAY: f64 = 2.0;

/// The reading's knobs (`READING`).
pub struct ReadingKnobs {
    pub window_s: f64,
    pub margin: f64,
    pub min_away: usize,
    pub significance: f64,
    pub back_significance: f64,
    pub hit_s: f64,
    pub min_presses: usize,
    pub match_s: f64,
}

pub const READING: ReadingKnobs = ReadingKnobs {
    window_s: 8.0,
    margin: 0.1,
    min_away: 12,
    significance: 2.0,
    back_significance: 1.0,
    hit_s: 0.07,
    min_presses: 8,
    match_s: 0.07,
};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ReadingName {
    AsTracked,
    OffBeat,
    Double,
    HalfA,
    HalfB,
}

impl ReadingName {
    pub fn name(self) -> &'static str {
        match self {
            ReadingName::AsTracked => "as tracked",
            ReadingName::OffBeat => "off-beat",
            ReadingName::Double => "double",
            ReadingName::HalfA => "half A",
            ReadingName::HalfB => "half B",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GridSource {
    Offline,
    Online,
}

impl GridSource {
    pub fn name(self) -> &'static str {
        match self {
            GridSource::Offline => "offline",
            GridSource::Online => "online",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Candidate {
    pub source: GridSource,
    pub reading: ReadingName,
    pub beats: Vec<f64>,
}

impl Candidate {
    /// Its source and reading, as the settle records name it.
    pub fn label(&self) -> String {
        format!("{} {}", self.source.name(), self.reading.name())
    }
}

/// An entry's ten candidate grids: five readings of each of its two beat lists.
pub fn candidates(entry: &Hindsight) -> Vec<Candidate> {
    let mut out = Vec::with_capacity(10);
    for (source, d) in [
        (GridSource::Offline, &entry.offline),
        (GridSource::Online, &entry.online),
    ] {
        let mids: Vec<f64> = d.windows(2).map(|w| (w[1] + w[0]) / 2.0).collect();
        let mut double: Vec<f64> = d.iter().chain(&mids).copied().collect();
        double.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        out.push(Candidate {
            source,
            reading: ReadingName::AsTracked,
            beats: d.clone(),
        });
        out.push(Candidate {
            source,
            reading: ReadingName::OffBeat,
            beats: mids,
        });
        out.push(Candidate {
            source,
            reading: ReadingName::Double,
            beats: double,
        });
        out.push(Candidate {
            source,
            reading: ReadingName::HalfA,
            beats: d.iter().step_by(2).copied().collect(),
        });
        out.push(Candidate {
            source,
            reading: ReadingName::HalfB,
            beats: d.iter().skip(1).step_by(2).copied().collect(),
        });
    }
    out
}

#[allow(clippy::manual_midpoint)] // (the TS binary search, as written)
fn nearest_dist(times: &[f64], t: f64) -> f64 {
    if times.is_empty() {
        return f64::INFINITY;
    }
    let mut lo = 0usize;
    let mut hi = times.len() - 1;
    while hi - lo > 1 {
        let m = (lo + hi) >> 1;
        if times[m] <= t {
            lo = m;
        } else {
            hi = m;
        }
    }
    min(abs(times[lo] - t), abs(times[hi] - t))
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Score {
    pub hits: f64,
    pub chance: f64,
    pub score: f64,
}

/// A candidate's score for presses: its hit share less the share random presses would hit.
pub fn score(c: &Candidate, presses: &[f64], window_s: f64) -> Score {
    let hits = if presses.is_empty() {
        0.0
    } else {
        presses
            .iter()
            .filter(|&&p| nearest_dist(&c.beats, p) <= READING.hit_s + 1e-9)
            .count() as f64
            / presses.len() as f64
    };
    let chance = min(1.0, 2.0 * READING.hit_s * (c.beats.len() as f64 / window_s));
    Score {
        hits,
        chance,
        score: hits - chance,
    }
}

/// The tolerance for matching beats of a grid: 70 ms, or a quarter of its shortest spacing if less.
pub fn match_tolerance(beats: &[f64]) -> f64 {
    let mut shortest = f64::INFINITY;
    for w in beats.windows(2) {
        shortest = min(shortest, w[1] - w[0]);
    }
    min(READING.match_s, shortest / 4.0)
}

/// How well grid a agrees with grid b where they overlap.
fn agreement(a: &[f64], b: &[f64], tol: f64) -> f64 {
    if a.is_empty() || b.is_empty() {
        return 0.0;
    }
    let lo = max(a[0], b[0]) - tol;
    let hi = min(a[a.len() - 1], b[b.len() - 1]) + tol;
    let mut n = 0.0;
    for &x in a {
        if x >= lo && x <= hi {
            n += if nearest_dist(b, x) <= tol { 1.0 } else { -1.0 };
        }
    }
    n
}

#[derive(Clone, Debug, PartialEq)]
pub struct Reading {
    pub chosen: Candidate,
    /// Did it replace the incumbent?
    pub switched: bool,
}

/// The reading a press settles on (`chooseReading`).
pub fn choose_reading(
    entry: &Hindsight,
    presses: &[f64],
    previous: Option<&[f64]>,
    previous_label: Option<&str>,
) -> Reading {
    let cands = candidates(entry);
    let mut incumbent = 0usize;
    if let Some(previous) = previous.filter(|p| !p.is_empty()) {
        let agree: Vec<f64> = cands
            .iter()
            .map(|c| {
                agreement(
                    &c.beats,
                    previous,
                    match_tolerance(if c.beats.len() > 1 {
                        &c.beats
                    } else {
                        previous
                    }),
                )
            })
            .collect();
        let best = agree.iter().fold(0.0, |m, &a| max(m, a));
        if best > 0.0 {
            incumbent = agree.iter().position(|&a| a == best).unwrap();
        }
        let same = cands
            .iter()
            .position(|c| Some(c.label().as_str()) == previous_label);
        if let Some(same) = same
            && best > 0.0
            && agree[same] >= best - max(1.0, 0.1 * best)
        {
            incumbent = same;
        }
    }
    let n = presses.len();
    if n < READING.min_presses {
        return Reading {
            chosen: cands[incumbent].clone(),
            switched: false,
        };
    }
    let span = max(1.0, min(READING.window_s, entry.known_at));
    let si = score(&cands[incumbent], presses, span);
    let mut top = incumbent;
    let mut st = si;
    for (i, c) in cands.iter().enumerate() {
        let sc = score(c, presses, span);
        if sc.score > st.score + 1e-12 {
            top = i;
            st = sc;
        }
    }
    if top == incumbent {
        return Reading {
            chosen: cands[incumbent].clone(),
            switched: false,
        };
    }
    if cands[incumbent].reading == ReadingName::AsTracked && n < READING.min_away {
        return Reading {
            chosen: cands[incumbent].clone(),
            switched: false,
        };
    }
    let d = st.score - si.score;
    let factor = if cands[top].reading == ReadingName::AsTracked {
        READING.back_significance
    } else {
        READING.significance
    };
    let sig = factor * sqrt((st.hits * (1.0 - st.hits) + si.hits * (1.0 - si.hits)) / n as f64);
    if d > READING.margin && d > sig {
        Reading {
            chosen: cands[top].clone(),
            switched: true,
        }
    } else {
        Reading {
            chosen: cands[incumbent].clone(),
            switched: false,
        }
    }
}

/// A press as the settle step sees it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SettlePress {
    pub s: f64,
    pub partner: bool,
    pub group: KeyGroup,
    /// Unjudged at the press: it settles unjudged too.
    pub unclear: bool,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Settled {
    /// None: unjudged.
    pub judgement: Option<PressJudgement>,
    pub reading: Reading,
    /// The entry's time.
    pub known_at: f64,
}

/// The settle step's state: the beat registry, the settled memory and the latest reading's grid.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Settler {
    pub registry: Vec<f64>,
    pub memo: PressMemo,
    pub incumbent: Option<Vec<f64>>,
    pub incumbent_label: Option<String>,
}

impl Settler {
    /// Starts the reading again from offline "as tracked".
    pub fn restart(&mut self) {
        self.incumbent = None;
        self.incumbent_label = None;
    }

    /// The identity of a grid time: a registered beat within the tolerance, or a new one.
    pub fn identity(&mut self, t: f64, tol: f64) -> i64 {
        for i in (0..self.registry.len()).rev() {
            if abs(self.registry[i] - t) <= tol {
                return i as i64;
            }
        }
        self.registry.push(t);
        self.registry.len() as i64 - 1
    }

    /// Settles a press against an entry (`Settler.settle`).
    pub fn settle(
        &mut self,
        hero: &HeroDef,
        press: &SettlePress,
        entry: &Hindsight,
        window_presses: &[f64],
    ) -> Settled {
        let reading = choose_reading(
            entry,
            window_presses,
            self.incumbent.as_deref(),
            self.incumbent_label.as_deref(),
        );
        self.incumbent = Some(reading.chosen.beats.clone());
        self.incumbent_label = Some(reading.chosen.label());
        let grid = reading.chosen.beats.clone();
        let base = |judgement| Settled {
            judgement,
            reading: reading.clone(),
            known_at: entry.known_at,
        };
        if press.unclear || grid.len() < 2 {
            return base(None);
        }
        let mut lo = 0usize;
        while lo + 1 < grid.len() && grid[lo + 1] <= press.s {
            lo += 1;
        }
        let hi = (grid.len() - 1).min(lo + 1);
        let span = grid[hi] - grid[lo];
        // (JS's `a || b`: a zero span takes the first spacing.)
        let period = if span == 0.0 || span.is_nan() {
            grid[1] - grid[0]
        } else {
            span
        };
        if nearest_dist(&grid, press.s) > period + 1e-9 {
            return base(None);
        }
        let tol = match_tolerance(&grid);
        let mut ids: std::collections::HashMap<isize, i64> = std::collections::HashMap::new();
        let mut memo = std::mem::take(&mut self.memo);
        let mut key_of = |gi: isize| -> i64 {
            if let Some(id) = ids.get(&gi) {
                return *id;
            }
            let id = self.identity(grid[gi as usize], tol);
            ids.insert(gi, id);
            id
        };
        let j = classify_press(
            hero,
            &grid,
            press.s,
            press.partner,
            &mut memo,
            press.group,
            &mut key_of,
        );
        self.memo = memo;
        base(Some(j))
    }
}

/// A judgment as the key display names it (unjudged without one).
pub fn judgement_label(j: Option<&PressJudgement>) -> String {
    let Some(j) = j else { return "unjudged".into() };
    let rating = |r: Option<CountedRating>| match r {
        Some(CountedRating::Perfect) => "perfect",
        Some(CountedRating::Great) => "great",
        Some(CountedRating::Good) => "good",
        Some(CountedRating::Loose) | None => "loose",
    };
    match j.class {
        PressClass::Counted => rating(j.rating).into(),
        PressClass::Offbeat => format!("offbeat-{}", rating(j.rating)),
        PressClass::Partner => "partner".into(),
        PressClass::Double => "double".into(),
        PressClass::Stray => "stray".into(),
    }
}
