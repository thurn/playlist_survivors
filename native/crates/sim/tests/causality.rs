//! K1: checks without an oracle, on the Rust sim alone (docs/design/native-app.md, "Checks without an
//! oracle"), ported from `scripts/check-reactive.ts`:
//!
//! - **Causality.** For each cut C, a run on the stream with every entry known after C changed
//!   (perturbation) or deleted (truncation, the live situation) records exactly what the true run
//!   recorded by C. Entries known within one fixed step after C are kept: a step takes in what is
//!   known by its end before moving, so a run to C reads up to one step (1/60 s) past it.
//! - **The negative control.** A sim whose director reads the evidence a second ahead fails it.
//! - **Determinism.** The same stream and keys twice, and the stream arriving in random-sized
//!   bursts while the sim runs, give the identical recording.
//!
//! The gate runs two cuts on the Galop, the negative control and determinism on causality-mix; the
//! release stage every cut on the Galop and causality-mix with two players.
#![allow(clippy::float_cmp)]

mod common;

use common::{
    Spec, first_difference, known_by, log_for, recording, start, stream_file, with_silent_tail,
};
use sim::core::rng::{Part, Rng};
use stream::{Level, StreamLog};

const LEVELS: [Level; 3] = [Level::Quiet, Level::Medium, Level::Loud];

/// What a run to C may read past C: one fixed step, and a little.
const MARGIN: f64 = 1.0 / 60.0 + 1e-3;

/// The players whose keys the checks replay (as TS's check: an honest player and a masher).
const PLAYERS: [&str; 2] = ["reactive-moderate", "mash4"];

/// The stream with every entry known after `c` changed, as `perturbStream` does: later beats moved,
/// dropped and added with new periods and evidence, later ticks, onsets, hindsight, timbre,
/// structure and forecasts given new values. Each group stays in knownAt order.
fn perturb(log: &StreamLog, c: f64, seed: &str) -> StreamLog {
    let mut r = Rng::new(&[
        Part::Num(f64::from(0x5eed)),
        Part::Str("perturb"),
        Part::Str(seed),
        Part::Num((c * 1e6).round()),
    ]);
    let after = |k: f64| k > c;
    let mut out = log.clone();
    // Beats: each later one moved by up to a third of a period, every fifth dropped, an extra one in
    // every seventh gap; committed after c, ahead of its time, in order.
    let keep: Vec<_> = log
        .beats
        .iter()
        .copied()
        .filter(|b| !after(b.known_at))
        .collect();
    let later: Vec<_> = log
        .beats
        .iter()
        .copied()
        .filter(|b| after(b.known_at))
        .collect();
    let mut beats = Beats {
        out: keep.clone(),
        c,
        prev_t: keep.last().map_or(f64::NEG_INFINITY, |b| b.t),
        prev_period: keep.last().or(later.first()).map_or(0.5, |b| b.period),
        prev_known: c,
    };
    for (i, b) in later.iter().enumerate() {
        if i % 7 == 6 {
            let k = b.known_at + r.range(-0.3, 0.0);
            beats.push(&mut r, f64::midpoint(beats.prev_t, b.t), k, b.period);
        }
        if i % 5 == 4 {
            continue;
        }
        let t = b.t + r.range(-0.35, 0.35) * b.period;
        let k = b.known_at + r.range(-0.3, 0.3);
        beats.push(&mut r, t, k, b.period);
    }
    out.beats = beats.out;
    for k in out.ticks.iter_mut().filter(|k| after(k.known_at)) {
        k.evidence = r.next();
        k.energy = r.next();
        if k.level.is_some() {
            k.level = Some(LEVELS[r.pick_index(3)]);
        }
        if let Some(b) = k.bands.as_mut() {
            *b = stream::Bands {
                kick: r.next(),
                snare: r.next(),
                hat: r.next(),
                bass: r.next(),
            };
        }
        if k.brightness.is_some() {
            k.brightness = Some(r.next());
        }
    }
    for o in out.onsets.iter_mut().filter(|o| after(o.known_at)) {
        o.t = (o.t + r.range(-0.1, 0.1)).min(o.known_at);
        o.strength = r.range(0.0, 2.0);
        o.strong = r.next() < 0.5;
    }
    if let Some(h) = out.hindsight.as_mut() {
        for x in h.iter_mut().filter(|x| after(x.known_at)) {
            let shift = r.range(-0.2, 0.2);
            for t in x.offline.iter_mut().chain(x.online.iter_mut()) {
                *t += shift;
            }
            if r.next() < 0.3 {
                x.online.clear();
            }
        }
    }
    if let Some(ts) = out.timbre.as_mut() {
        for x in ts.iter_mut().filter(|x| after(x.known_at)) {
            *x = stream::Timbre {
                known_at: x.known_at,
                brightness: r.next(),
                flatness: r.next(),
                percussive: r.next(),
                density: r.next(),
            };
        }
    }
    // Structure: later entries moved within what they say (before their knownAt), with new
    // strengths, and every third one dropped.
    let mut n = 0;
    if let Some(xs) = out.changes.as_mut() {
        xs.retain(|x| {
            !after(x.known_at) || {
                n += 1;
                n % 3 != 0
            }
        });
        for x in xs.iter_mut().filter(|x| after(x.known_at)) {
            x.t = (x.t + r.range(-1.0, 1.0)).min(x.known_at);
            x.strength = r.next();
        }
    }
    if let Some(xs) = out.jumps.as_mut() {
        xs.retain(|x| {
            !after(x.known_at) || {
                n += 1;
                n % 3 != 0
            }
        });
        for x in xs.iter_mut().filter(|x| after(x.known_at)) {
            x.t = (x.t + r.range(-1.0, 1.0)).min(x.known_at);
            x.db = r.range(-12.0, 12.0);
        }
    }
    if let Some(xs) = out.ends.as_mut() {
        for x in xs.iter_mut().filter(|x| after(x.known_at)) {
            x.t = (x.t + r.range(-1.0, 1.0)).min(x.known_at);
        }
        // And an end that never was, a beat after the cut.
        xs.push(stream::End {
            t: c + 0.5,
            known_at: c + 2.0,
        });
        xs.sort_by(|a, b| a.known_at.total_cmp(&b.known_at));
    }
    if let Some(xs) = out.forecasts.as_mut() {
        for x in xs.iter_mut().filter(|x| after(x.known_at)) {
            x.t = (x.t + r.range(-0.2, 0.2)).max(x.known_at);
            x.confidence = r.next();
        }
    }
    out
}

/// Perturbed beats after the cut: committed after it, ahead of their time, in order.
struct Beats {
    out: Vec<stream::Beat>,
    c: f64,
    prev_t: f64,
    prev_period: f64,
    prev_known: f64,
}

impl Beats {
    fn push(&mut self, r: &mut Rng, t0: f64, known0: f64, period0: f64) {
        let known_at = self.prev_known.max(self.c + 1e-4).max(known0);
        let t = t0
            .max(known_at + 0.1 + 1e-4)
            .max(self.prev_t + 0.25 * self.prev_period + 1e-4);
        let period = period0 * r.range(0.8, 1.25);
        self.out.push(stream::Beat {
            t,
            known_at,
            period,
            evidence: r.next(),
        });
        self.prev_t = t;
        self.prev_period = period;
        self.prev_known = known_at;
    }
}

/// Cuts on a true run's recording: every `every` seconds, and just after each set piece, zone wave
/// and threat first shows (the first few of each), up to `until`.
fn cuts(rec: &sim::recorder::LogRecorder, every: f64, until: f64) -> Vec<f64> {
    let mut raw: Vec<f64> = Vec::new();
    let mut k = 1.0;
    while every > 0.0 && k * every < until {
        raw.push(k * every);
        k += 1.0;
    }
    raw.extend(rec.formations.iter().take(4).map(|f| f.s_start + 0.01));
    raw.extend(rec.zone_waves.iter().take(4).map(|z| z.s + 0.01));
    raw.extend(rec.threats.iter().take(4).map(|t| t.s + 0.01));
    raw.extend(rec.boss.iter().map(|b| b.1 + 0.01));
    raw.retain(|&c| c > 0.05 && c < until);
    raw.sort_by(f64::total_cmp);
    raw.dedup_by(|a, b| *a - *b < 0.002);
    raw
}

/// The features a cut's check covers (TS's `FEATURES`).
const FEATURES: [&str; 14] = [
    "level-ups",
    "zone waves",
    "section starts",
    "reading switches",
    "forecasts",
    "forecast cleaves",
    "onset cleaves",
    "rings",
    "stampedes",
    "volleys",
    "bee wedges",
    "kick lines",
    "the boss",
    "the finale",
];

/// The features the true run had fired before `last` (the finale fired at `finale`).
fn fired(rec: &sim::recorder::LogRecorder, last: f64, finale: Option<f64>) -> Vec<&'static str> {
    let num = |v: &serde_json::Value, k: &str| v[k].as_f64().unwrap_or(f64::INFINITY);
    let piece = |def: &str| {
        rec.formations
            .iter()
            .any(|f| f.def == def && f.s_start < last)
    };
    let cleave = |onset: bool| {
        rec.cleaves.iter().any(|c| {
            c["onset"].as_bool().unwrap_or(false) == onset
                && c["launch"].as_f64().unwrap_or(num(c, "s")) < last
        })
    };
    let has = [
        rec.upgrades.iter().any(|u| u.s < last),
        rec.zone_waves.iter().any(|z| z.s < last),
        rec.zone_waves
            .iter()
            .any(|z| z.section.is_some() && z.s < last),
        rec.settles
            .iter()
            .any(|x| x["switched"] == true && num(x, "s") < last),
        rec.swings
            .iter()
            .any(|w| w["kind"] == "heavy" && num(w, "s") < last),
        cleave(false),
        cleave(true),
        piece("ring"),
        piece("stampede"),
        rec.arrows.iter().any(|a| a.s_release < last),
        piece("bee-wedge"),
        // (The kick line records its formation, `skeleton-kick-line`.)
        rec.formations
            .iter()
            .any(|f| f.def.ends_with("kick-line") && f.s_start < last),
        rec.boss.is_some_and(|b| b.1 < last),
        finale.is_some_and(|f| f < last),
    ];
    FEATURES
        .iter()
        .zip(has)
        .filter(|x| x.1)
        .map(|x| *x.0)
        .collect()
}

#[derive(Clone, Copy, PartialEq)]
enum Change {
    Perturb,
    Truncate,
}

/// The causality check on one track for each player at `cuts` (or the true run's own cuts every
/// `every` seconds): failures, and the cuts checked.
fn causality(
    spec: Spec,
    cuts_at: Option<&[f64]>,
    every: f64,
    stop_at_first: bool,
) -> (Vec<String>, usize, Vec<&'static str>) {
    let file = stream_file(spec.track);
    let log = log_for(&spec);
    let mut failures = Vec::new();
    let mut checked = 0;
    let mut covered: Vec<&'static str> = Vec::new();
    for profile in PLAYERS {
        let spec = Spec { profile, ..spec };
        // The true run to its end: its cuts (to its finale and just after it) and what fired.
        let (rec, mut sim) = start(&spec, &log);
        sim.advance_to(file.audio.duration + 10.0);
        let finale = rec
            .borrow()
            .hits
            .iter()
            .find(|h| h["what"] == "finale-blast")
            .and_then(|h| h["s"].as_f64());
        let cs = if let Some(cs) = cuts_at {
            cs.to_vec()
        } else {
            let until = if sim.done() {
                sim.end_s()
            } else {
                file.audio.duration
            };
            let mut cs = cuts(&rec.borrow(), every, until);
            cs.extend(finale.map(|f| f + 0.01));
            cs.sort_by(f64::total_cmp);
            cs
        };
        let last = cs.last().copied().unwrap_or(0.0);
        for f in fired(&rec.borrow(), last, finale) {
            if !covered.contains(&f) {
                covered.push(f);
            }
        }
        let (truth, mut sim) = start(&spec, &log);
        for &c in &cs {
            sim.advance_to(c);
            let want = recording(&truth);
            for change in [Change::Perturb, Change::Truncate] {
                checked += 1;
                let changed = match change {
                    Change::Perturb => perturb(&log, c + MARGIN, spec.track),
                    Change::Truncate => known_by(&log, c + MARGIN),
                };
                let (rec, mut run) = start(&spec, &changed);
                run.advance_to(c);
                if let Some(d) = first_difference(&want, &recording(&rec)) {
                    let what = if change == Change::Perturb {
                        "perturbed"
                    } else {
                        "truncated"
                    };
                    failures.push(format!(
                        "{} {profile}, cut at {c:.3} ({what}): {d}",
                        spec.track
                    ));
                    if stop_at_first {
                        return (failures, checked, covered);
                    }
                }
            }
        }
    }
    (failures, checked, covered)
}

fn assert_causal(spec: Spec, cuts_at: Option<&[f64]>, every: f64) -> Vec<&'static str> {
    let (failures, checked, covered) = causality(spec, cuts_at, every, false);
    eprintln!(
        "causality on {}: {checked} cut runs, {} failing; fired before a cut: {}",
        spec.track,
        failures.len(),
        covered.join(", ")
    );
    assert!(failures.is_empty(), "{}", failures.join("\n"));
    covered
}

/// Gate: two cuts on the Galop (just after its first set piece shows, and mid-song).
#[test]
fn the_galop_is_causal_at_two_cuts() {
    let _ = assert_causal(Spec::new("orpheus-galop", ""), Some(&[62.0, 110.0]), 0.0);
}

/// Gate: a sim whose director reads the evidence a second ahead fails the check.
#[test]
fn the_negative_control_fails() {
    let spec = Spec {
        leak_future_evidence: true,
        ..Spec::new("causality-mix", "")
    };
    let (failures, _, _) = causality(spec, Some(&[20.0, 30.0, 40.0]), 0.0, true);
    assert!(
        !failures.is_empty(),
        "the causality check passed a sim that reads the evidence a second ahead"
    );
    eprintln!(
        "negative control caught: {}",
        failures[0].lines().next().unwrap()
    );
}

#[test]
#[ignore = "release: every cut on the Galop and causality-mix, two players, perturbed and truncated"]
fn every_cut_is_causal() {
    let check = Spec {
        level: "levels/reactive-check.yaml",
        no_hero_damage: true,
        ..Spec::new("causality-mix", "")
    };
    let mut covered = assert_causal(check, None, 5.0);
    covered.extend(assert_causal(Spec::new("orpheus-galop", ""), None, 10.0));
    // (E1: a Delayed stream of the Galop, at the chosen M.)
    let delayed = Spec {
        delayed: Some(analysis::delayed::DelayedParams::chosen().m),
        ..Spec::new("orpheus-galop", "")
    };
    covered.extend(assert_causal(delayed, None, 10.0));
    // (Reading switches need hindsight grids, which these streams have.)
    let missing: Vec<_> = FEATURES.iter().filter(|f| !covered.contains(f)).collect();
    assert!(
        missing.is_empty(),
        "never fired before a cut, so unchecked: {missing:?}"
    );
}

/// The same stream and keys twice give the identical recording, to the run's end.
#[test]
fn the_same_stream_and_keys_give_the_same_recording() {
    let file = stream_file("causality-mix");
    let log = with_silent_tail(file.log.clone(), 10.0);
    let spec = Spec::new("causality-mix", "reactive-moderate");
    let run = || {
        let (rec, mut sim) = start(&spec, &log);
        sim.advance_to(file.audio.duration + 10.0);
        assert!(sim.done(), "the run reached no confirmed end");
        recording(&rec)
    };
    let (a, b) = (run(), run());
    assert!(
        first_difference(&a, &b).is_none(),
        "two runs differ: {}",
        first_difference(&a, &b).unwrap()
    );
}

/// The stream arriving in random-sized bursts while the sim runs (each burst's entries appended
/// at their knownAt, the sim stepped to a random time between) gives the recording of the stream
/// given whole: streaming equals batch.
#[test]
fn bursts_of_arrivals_give_the_same_recording() {
    let delayed_galop = Spec {
        delayed: Some(analysis::delayed::DelayedParams::chosen().m),
        ..Spec::new("orpheus-galop", "mash4")
    };
    for spec in [
        Spec::new("causality-mix", "mash4"),
        Spec::new("orpheus-galop", "mash4"),
        delayed_galop,
    ] {
        let track = spec.track;
        let file = stream_file(track);
        let log = log_for(&spec);
        let until = file.audio.duration + 10.0;
        let (whole, mut sim) = start(&spec, &log);
        sim.advance_to(until);
        let (bursts, mut sim) = start(&spec, &known_by(&log, 0.0));
        let mut r = Rng::new(&[Part::Str("bursts"), Part::Str(track)]);
        let mut t = 0.0;
        while t < until {
            let next = (t + r.range(0.01, 3.0)).min(until);
            *sim.log_mut() = known_by(&log, next);
            // (A burst lands in the middle of a frame: the sim is stepped to before it arrives.)
            sim.advance_to(t + r.range(0.0, next - t));
            sim.advance_to(next);
            t = next;
        }
        assert!(
            first_difference(&recording(&whole), &recording(&bursts)).is_none(),
            "{track}: bursts differ: {}",
            first_difference(&recording(&whole), &recording(&bursts)).unwrap()
        );
    }
}

/// When a key is queued doesn't matter, only its time: the same key queued at a stop well before
/// it, or just before it falls, gives the identical recording (hv-t8o8.80: the stops, and so where
/// stream entries are taken in, followed the queued keys).
#[test]
fn a_keys_queueing_time_changes_nothing() {
    use sim::player::KeyEvent;
    for track in ["house-128", "causality-mix"] {
        let spec = Spec::new(track, "reactive-moderate");
        let log = log_for(&spec);
        let keys = common::keys(track, spec.profile);
        let mut runs = Vec::new();
        for early in [true, false] {
            let (rec, mut sim) = start(
                &Spec {
                    profile: "",
                    ..spec
                },
                &log,
            );
            let mut queued = 0;
            // (Frames between the sim's own fixed stops, as a display's rarely line up with them.)
            let mut t = 0.5 / 60.0;
            while t < 20.0 {
                // Each key queued half a second ahead, or in the frame before it falls.
                let horizon = t + if early { 0.5 } else { 1.0 / 60.0 };
                while queued < keys.len() && keys[queued].s <= horizon {
                    let k = keys[queued];
                    sim.key(KeyEvent {
                        s: k.s.max(sim.s()),
                        ..k
                    });
                    queued += 1;
                }
                t += 1.0 / 60.0;
                sim.advance_to(t);
            }
            runs.push(recording(&rec));
        }
        if let Some(d) = first_difference(&runs[0], &runs[1]) {
            panic!("{track}: early and late queueing differ: {d}");
        }
    }
}

/// The sim's own stops (clock moves that aren't a frame's), as a recorder sees them.
#[derive(Default)]
struct Stops(Vec<(f64, f64)>);

impl sim::recorder::Recorder for Stops {
    fn clock(&mut self, s: f64, w: f64, between: bool) {
        if !between {
            self.0.push((s, w));
        }
    }
}

/// hv-t8o8.80: a key queued at the run's start, or just before it falls (the sim having passed
/// its fixed stops on the way), makes the same stops, for key times all through
/// house-128's first seconds.
#[test]
fn a_key_queued_early_or_late_makes_the_same_stops() {
    use std::cell::RefCell;
    use std::rc::Rc;

    use sim::engine::Sim;
    use sim::movement::InputKey;
    use sim::player::{KeyEvent, KeyEventKind};
    let spec = Spec::new("house-128", "");
    let log = log_for(&spec);
    let mut k = 0.3;
    while k < 4.0 {
        let mut runs = Vec::new();
        for late in [false, true] {
            let stops = Rc::new(RefCell::new(Stops::default()));
            let mut sim = Sim::new(
                common::input_for(&spec, &log),
                Rc::new(log.clone()),
                Box::new(Rc::clone(&stops)),
                None,
            );
            if late {
                sim.advance_to(k - 0.004);
            }
            for (s, kind) in [(k, KeyEventKind::Press), (k + 0.08, KeyEventKind::Release)] {
                sim.key(KeyEvent {
                    s,
                    kind,
                    key: InputKey::Right,
                    id: None,
                });
            }
            sim.advance_to(k + 2.0);
            runs.push(stops.borrow().0.clone());
        }
        let first = runs[0].iter().zip(&runs[1]).position(|(a, b)| a != b);
        assert!(
            first.is_none() && runs[0].len() == runs[1].len(),
            "a key at {k:.4}: the stops differ from {first:?}: {:?} against {:?}",
            first.map(|j| &runs[0][j..(j + 3).min(runs[0].len())]),
            first.map(|j| &runs[1][j..(j + 3).min(runs[1].len())])
        );
        k += 0.0137;
    }
}
