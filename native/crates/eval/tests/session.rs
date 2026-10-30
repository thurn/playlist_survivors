//! Recorded listening inputs and stream arrivals remain replayable across deliberate
//! game changes. Historical full-world hashes encode obsolete enemy travel; compare
//! current settled records across settlement windows instead. Preserve raw sessions.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use eval::live::Engine;
use eval::session::{AppliedKey, Arrival, RecordedRun, replay_run};
use indexmap::IndexMap;
use sim::content::{SourceFile, load_content};

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn source(rel: &str) -> SourceFile {
    SourceFile {
        name: rel.to_string(),
        text: std::fs::read_to_string(root().join(rel)).unwrap(),
    }
}

/// The engine as the app builds it (`slice/mod.rs`, `World::engine`).
fn engine() -> Engine {
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .filter(|n| Path::new(n).extension().is_some_and(|x| x == "yaml"))
        .collect();
    names.sort();
    let files: Vec<SourceFile> = names
        .iter()
        .map(|n| source(&format!("content/{n}")))
        .collect();
    let lib = load_content(&files).unwrap();
    Engine::new(
        &source("levels/reactive.yaml"),
        &lib,
        &IndexMap::new(),
        0.2,
        true,
        false,
        f64::INFINITY,
    )
    .unwrap()
}

fn lines<T: serde::de::DeserializeOwned>(path: &Path) -> Vec<T> {
    std::fs::read_to_string(path)
        .unwrap()
        .lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| serde_json::from_str(l).unwrap())
        .collect()
}

#[test]
fn a_recorded_session_replays_across_settlement_windows() {
    replays_across_windows("native/fixtures/session-house");
}

#[test]
fn a_delayed_session_replays_across_settlement_windows() {
    replays_across_windows("native/fixtures/session-house-delayed");
}

/// Real arrivals/keys replay identically with different rollback horizons.
fn replays_across_windows(rel: &str) {
    let dir = root().join(rel);
    let meta: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(dir.join("meta.json")).unwrap()).unwrap();
    let r = &meta["runs"][0];
    let run = RecordedRun {
        index: 0,
        s0: r["s0"].as_f64().unwrap(),
        end: r["end"].as_f64().unwrap(),
        hash: u64::from_str_radix(r["settledHash"].as_str().unwrap(), 16).unwrap(),
        records: r["settledRecords"].as_u64().unwrap() as usize,
    };
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"));
    let keys: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"));
    assert!(keys.len() > 20, "{} keys", keys.len());
    let mut kept = engine();
    kept.settled_log = Some(Vec::new());
    let all = RecordedRun {
        records: usize::MAX,
        ..run
    };
    replay_run(&mut kept, &arrivals, &keys, &all, 20.0).unwrap();
    let records = kept.settled_log.as_ref().unwrap();
    // Stay before the terminal settlement boundary in both horizons.
    let n = records.partition_point(|(s, _)| *s < run.end - run.s0 - 0.5);
    assert!(
        n > 1000,
        "recorded session must exercise substantial gameplay"
    );
    let expected = eval::live::hash_records(&records[..n]);
    let prefix = RecordedRun { records: n, ..run };
    let mut wider = engine();
    wider.settle_s = 0.4;
    let got = replay_run(&mut wider, &arrivals, &keys, &prefix, 20.0).unwrap();
    assert_eq!(
        got.hash,
        Some(expected),
        "recorded keys and arrivals must settle identically"
    );
}

#[test]
fn an_unknown_recorded_key_fails_before_replay() {
    let keys = vec![AppliedKey {
        run: 0,
        s: 0.1,
        key: "Confrim".into(),
        pressed: true,
    }];
    let run = RecordedRun {
        index: 0,
        s0: 0.0,
        end: 1.0,
        hash: 0,
        records: 0,
    };
    let error = replay_run(&mut engine(), &[], &keys, &run, 20.0).unwrap_err();
    assert!(error.contains("Confrim"));
}

#[test]
fn prefix_hash_matches_the_kept_records_without_enabling_retention() {
    let dir = root().join("native/fixtures/session-house-delayed");
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"));
    let keys: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"));
    let base = RecordedRun {
        index: 0,
        s0: 0.0,
        end: 8.0,
        hash: 0,
        records: usize::MAX,
    };
    let mut kept = engine();
    kept.settled_log = Some(Vec::new());
    let all = replay_run(&mut kept, &arrivals, &keys, &base, 20.0).unwrap();
    let records = kept.settled_log.as_ref().unwrap();
    assert!(records.len() > 100);
    assert_eq!(all.hash, None);
    for n in [0, 1, records.len() / 2, records.len(), records.len() + 1] {
        let run = RecordedRun { records: n, ..base };
        let mut bounded = engine();
        let got = replay_run(&mut bounded, &arrivals, &keys, &run, 20.0).unwrap();
        assert!(bounded.settled_log.is_none());
        assert!(
            bounded.view.borrow().moments.is_empty(),
            "offline replay consumes transient presentation"
        );
        let expected = (n <= records.len()).then(|| eval::live::hash_records(&records[..n]));
        assert_eq!(got.hash, expected, "prefix {n}");
        assert_eq!(got.settled, records.len());
    }
}

/// An arrival heard before the settled time of a frame drawn ahead of it (the display's offset)
/// is stamped at the run's known floor, so the engine is fed what the session records: a replay,
/// which settles behind its arrivals, then sees the same entry (ps-8sb.4).
#[test]
fn an_arrival_stamped_at_the_known_floor_is_fed_as_the_session_records_it() {
    use eval::session::{Entry, known_floor, rebase_append};
    let s0 = 6.97;
    let mut e = engine();
    e.frame(1.045, 0.2);
    let floor = known_floor(e.settled_s(), s0);
    assert!(floor > 1.0 + s0, "the frame settled past the arrival");
    let entry = Entry::Beat(stream::Beat {
        t: s0 + 1.3,
        known_at: s0 + 1.0,
        period: 0.5,
        evidence: 1.0,
    })
    .stamped(floor);
    let mut recorded = stream::StreamLog::default();
    rebase_append(&mut recorded, &entry, s0);
    e.feed(|log| rebase_append(log, &entry, s0));
    let fed = e.stream().beats.last().unwrap().known_at;
    assert_eq!(fed, recorded.beats[0].known_at);
    assert!(fed >= e.settled_s());
}
