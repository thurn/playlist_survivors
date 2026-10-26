//! A recorded listening session replayed (docs/design/native-app.md, "Session recording (D3)"): a
//! short one, committed in `native/fixtures/session-house/` (made by `playlist-survivors --file
//! house-128 --headless --seconds 14 --press-beats`: 11 s of a run with a press on every beat,
//! many of them later than the settling window), must settle as it did live. A deliberate change to
//! the sim, the content or the level changes what settles: record it again (from `native/`,
//! `PS_SESSIONS=/tmp/s cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg
//! --headless --seconds 14 --press-beats`, then copy that session's `stream.jsonl`, `keys.jsonl`
//! and `meta.json` here). `native/fixtures/session-house-delayed/` is the same in Delayed
//! listening (`--delayed`): the music heard 3 s late, beats from hindsight, every entry known at
//! its analysis position; it must replay as exactly.
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
fn a_recorded_session_settles_as_it_did_live() {
    settles_as_live("native/fixtures/session-house");
}

#[test]
fn a_delayed_session_settles_as_it_did_live() {
    settles_as_live("native/fixtures/session-house-delayed");
}

/// The session in `rel` replayed: its run settles as it did live.
fn settles_as_live(rel: &str) {
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
    let got = replay_run(&mut engine(), &arrivals, &keys, &run, 20.0).unwrap();
    assert!(got.settled >= run.records, "{got:?}");
    assert_eq!(
        got.hash,
        Some(run.hash),
        "the settled records differ from the live run's"
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
