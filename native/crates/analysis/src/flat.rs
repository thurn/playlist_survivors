//! The stream's entries as fixed-size records: the fields of the game plugin's `ps_entry`
//! (docs/design/unity-game.md, "The C interface"), which `ps entries` writes and the plugin hands
//! Unity. Every record has a kind, a content time `t`, its stamp `known_at` and up to 12 values;
//! unused slots are 0.
//!
//! | kind | group | `t` | `v` |
//! |---|---|---|---|
//! | 1 | beat | when it falls | period, evidence |
//! | 2 | tick | its time | evidence, energy, level (0 quiet, 1 medium, 2 loud), key (index in [`KEYS`], −1 none), kick, snare, hat, bass, brightness |
//! | 3 | onset | when it sounds | strength, strong (0 or 1), band (0 kick, 1 snare, 2 hat, 3 bass, −1 none) |
//! | 4 | hindsight | the window's end | offline count, online count |
//! | 5 | hindsight beat | the beat | grid (0 offline, 1 online), the window's end |
//! | 6 | change | the boundary | strength |
//! | 7 | jump | when it jumps | dB |
//! | 8 | end | where the quiet began | |
//! | 9 | forecast | the accented beat | cycle (beats), confidence |
//! | 10 | timbre | the end of the span it describes | brightness, flatness, percussive, density |
//!
//! A hindsight entry's grids don't fit 12 slots, so it is a header record followed by one
//! hindsight-beat record per beat, offline then online, each with the header's stamp.

use crate::analyzer::Entry;
pub use crate::structure::KEYS;

pub const BEAT: i32 = 1;
pub const TICK: i32 = 2;
pub const ONSET: i32 = 3;
pub const HINDSIGHT: i32 = 4;
pub const HINDSIGHT_BEAT: i32 = 5;
pub const CHANGE: i32 = 6;
pub const JUMP: i32 = 7;
pub const END: i32 = 8;
pub const FORECAST: i32 = 9;
pub const TIMBRE: i32 = 10;

/// The onset bands, in their codes' order.
pub const BANDS: [&str; 4] = ["kick", "snare", "hat", "bass"];

/// One record, laid out as C's `ps_entry`.
#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Record {
    pub kind: i32,
    pub t: f64,
    pub known_at: f64,
    pub v: [f64; 12],
}

/// How many `v` slots a kind uses.
pub fn slots(kind: i32) -> usize {
    match kind {
        TICK => 9,
        TIMBRE => 4,
        ONSET => 3,
        BEAT | HINDSIGHT | HINDSIGHT_BEAT | FORECAST => 2,
        CHANGE | JUMP => 1,
        _ => 0,
    }
}

fn record(kind: i32, t: f64, known_at: f64, values: &[f64]) -> Record {
    debug_assert_eq!(values.len(), slots(kind));
    let mut v = [0.0; 12];
    v[..values.len()].copy_from_slice(values);
    Record {
        kind,
        t,
        known_at,
        v,
    }
}

/// The index of `name` in `names`, or −1.
fn code(names: &[&str], name: &str) -> f64 {
    names
        .iter()
        .position(|&n| n == name)
        .map_or(-1.0, |i| i as f64)
}

/// Appends an entry's records to `out`.
pub fn push(e: &Entry, out: &mut Vec<Record>) {
    match e {
        Entry::Beat(c) => out.push(record(BEAT, c.t, c.known_at, &[c.period, c.evidence])),
        Entry::Tick(x) => out.push(record(
            TICK,
            x.t,
            x.known_at,
            &[
                x.evidence,
                x.energy,
                f64::from(x.level.code()),
                x.key.map_or(-1.0, |k| code(&KEYS, k)),
                x.bands.kick,
                x.bands.snare,
                x.bands.hat,
                x.bands.bass,
                x.brightness,
            ],
        )),
        Entry::Onset(o) => out.push(record(
            ONSET,
            o.t,
            o.known_at,
            &[
                o.strength,
                f64::from(u8::from(o.strong)),
                code(&BANDS, o.band),
            ],
        )),
        Entry::Hindsight(h) => {
            out.push(record(
                HINDSIGHT,
                h.window_end,
                h.known_at,
                &[h.offline.len() as f64, h.online.len() as f64],
            ));
            for (grid, beats) in [(0.0, &h.offline), (1.0, &h.online)] {
                out.extend(
                    beats
                        .iter()
                        .map(|&b| record(HINDSIGHT_BEAT, b, h.known_at, &[grid, h.window_end])),
                );
            }
        }
        Entry::Change(c) => out.push(record(CHANGE, c.t, c.known_at, &[c.strength])),
        Entry::Jump(j) => out.push(record(JUMP, j.t, j.known_at, &[j.db])),
        Entry::End(x) => out.push(record(END, x.t, x.known_at, &[])),
        Entry::Forecast(f) => out.push(record(
            FORECAST,
            f.t,
            f.known_at,
            &[f.cycle as f64, f.confidence],
        )),
        Entry::Timbre(x) => out.push(record(
            TIMBRE,
            x.t,
            x.known_at,
            &[x.brightness, x.flatness, x.percussive, x.density],
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::hindsight::HindsightEntry;

    #[test]
    fn a_hindsight_entry_is_a_header_and_its_beats() {
        let mut out = Vec::new();
        push(
            &Entry::Hindsight(HindsightEntry {
                known_at: 7.67,
                window_end: 7.0,
                offline: vec![6.0, 6.5],
                online: vec![6.52],
            }),
            &mut out,
        );
        let got: Vec<(i32, f64, f64, f64)> = out
            .iter()
            .map(|r| (r.kind, r.t, r.known_at, r.v[0]))
            .collect();
        assert_eq!(
            got,
            [
                (HINDSIGHT, 7.0, 7.67, 2.0),
                (HINDSIGHT_BEAT, 6.0, 7.67, 0.0),
                (HINDSIGHT_BEAT, 6.5, 7.67, 0.0),
                (HINDSIGHT_BEAT, 6.52, 7.67, 1.0),
            ]
        );
        assert!(out[1..].iter().all(|r| r.v[1] == 7.0));
    }

    #[test]
    fn names_are_coded_by_their_tables() {
        assert_eq!(code(&KEYS, "A minor"), 21.0);
        assert_eq!(code(&BANDS, "hat"), 2.0);
        assert_eq!(code(&BANDS, ""), -1.0);
    }
}
