//! G2: whole live runs with a fixture's keys: the director's set pieces, then the finale on a
//! confirmed end and the run's end.
#![allow(clippy::float_cmp)]

mod common;

use common::{live_run, stream_file, with_silent_tail};

/// A whole live run: it ends on a confirmed end (after 30 s of judged time) with the finale's
/// epilogue, and the director's set pieces and zone waves came before it.
fn check_whole_run(track: &str, profile: &str) {
    let file = stream_file(track);
    let log = with_silent_tail(file.log.clone(), 10.0);
    let (rec, sim) = live_run(track, profile, &log, file.audio.duration + 10.0);
    let r = rec.borrow();
    assert!(
        sim.done(),
        "{track}/{profile}: no confirmed end by {}",
        sim.s()
    );
    let end = sim.end_s();
    // The finale fired on an `ends` entry confirmed by 3 s of quiet below the gate, 3 s before the end.
    let finale = end - 3.0;
    let gate = file.params.gate;
    assert!(
        log.ends.as_ref().unwrap().iter().any(|e| {
            let at = e.known_at.max(e.t + 3.0);
            at <= finale + 1e-6
                && log
                    .ticks
                    .iter()
                    .filter(|k| k.t >= e.t && k.t <= at)
                    .all(|k| k.evidence < gate)
        }),
        "{track}/{profile}: the finale at {finale} has no confirmed end before it"
    );
    assert!(!r.zone_waves.is_empty(), "{track}/{profile}: no zone waves");
    assert!(!r.formations.is_empty(), "{track}/{profile}: no set pieces");
    assert!(
        r.formations.iter().all(|f| f.s_start < finale),
        "{track}/{profile}: a set piece after the finale"
    );
    eprintln!(
        "{track}/{profile}: end {end:.1} (audio {:.1}), {} set pieces, {} zone waves, {} threats, boss {:?}",
        file.audio.duration,
        r.formations.len(),
        r.zone_waves.len(),
        r.threats.len(),
        r.boss
    );
}

#[test]
fn a_live_run_ends_on_a_confirmed_end() {
    check_whole_run("causality-mix", "mash4");
}

#[test]
fn live_runs_end_on_confirmed_ends() {
    for t in [
        "beethoven-fifth",
        "house-128",
        "william-tell-finale",
        "dnb-174",
        "notjam-lich-king-ascendant",
    ] {
        check_whole_run(t, "mash4");
    }
}
