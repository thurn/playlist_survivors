//! G2: whole live runs with a fixture's keys: the director's set pieces, the
//! finale on a confirmed end and the run's end, deterministic for the same stream and keys.
#![allow(clippy::float_cmp, clippy::case_sensitive_file_extension_comparisons)]

use std::cell::RefCell;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use indexmap::IndexMap;
use serde_json::Value;
use sim::content::level::load_level;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::engine::{AnimInfo, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::LogRecorder;
use sim::schedule::{grow_song_map, reactive_song_map};
use stream::{End, Level, StreamFile, StreamLog, Tick};

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn source(rel: &str) -> SourceFile {
    SourceFile {
        name: rel.to_string(),
        text: std::fs::read_to_string(root().join(rel)).unwrap(),
    }
}

fn library() -> ContentLibrary {
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .filter(|n| n.ends_with(".yaml"))
        .collect();
    names.sort();
    let files: Vec<SourceFile> = names
        .iter()
        .map(|n| source(&format!("content/{n}")))
        .collect();
    load_content(&files).unwrap()
}

fn atlas() -> IndexMap<String, AnimInfo> {
    let a: Value = serde_json::from_str(
        &std::fs::read_to_string(root().join("public/assets/characters.json")).unwrap(),
    )
    .unwrap();
    a["sprites"]["hero"]["animations"]
        .as_object()
        .unwrap()
        .iter()
        .map(|(k, v)| {
            (
                k.clone(),
                AnimInfo {
                    frames: v["frames"]["down"].as_array().unwrap().len() as f64,
                    impact_frame: v["impactFrame"].as_f64(),
                },
            )
        })
        .collect()
}

fn gz_lines(p: &Path) -> Vec<Value> {
    let raw = std::fs::read(p).unwrap();
    let mut text = String::new();
    flate2::read::GzDecoder::new(&raw[..])
        .read_to_string(&mut text)
        .unwrap();
    text.lines()
        .map(|l| serde_json::from_str(l).unwrap())
        .collect()
}

fn key(name: &str) -> InputKey {
    match name {
        "up" => InputKey::Up,
        "down" => InputKey::Down,
        "left" => InputKey::Left,
        "right" => InputKey::Right,
        "space" => InputKey::Space,
        _ => panic!("key {name}"),
    }
}

/// The stream as the live analyzer goes on giving it after the music stops: `secs` of silent ticks
/// past the file's last, and the `ends` entry 1.5 s into the silence that the analyzer gives.
/// (A Python stream stops at the file's end.)
fn with_silent_tail(mut log: StreamLog, secs: f64) -> StreamLog {
    let last = log.ticks.last().expect("ticks").clone();
    let mut t = last.t;
    while t < last.t + secs {
        t = (t * 20.0 + 1.0).round() / 20.0;
        log.ticks.push(Tick {
            t,
            known_at: t,
            evidence: 0.0,
            energy: 0.0,
            level: Some(Level::Quiet),
            ..last.clone()
        });
    }
    let ends = log.ends.get_or_insert_with(Vec::new);
    if !ends.last().is_some_and(|e| e.t >= last.t) {
        ends.push(End {
            t: last.t,
            known_at: last.t + 1.5,
        });
    }
    log
}

fn stream_file(track: &str) -> StreamFile {
    StreamFile::parse(
        &std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
            .unwrap(),
    )
    .unwrap()
}

/// A live run on `log` with `track`'s gate and `profile`'s keys, advanced to `until`
/// (or its end, if sooner).
fn live_run(
    track: &str,
    profile: &str,
    log: &StreamLog,
    until: f64,
) -> (Rc<RefCell<LogRecorder>>, Sim) {
    let lib = library();
    let file = stream_file(track);
    // (The level is loaded against a song map with no beats: live, its range is provisional.)
    let mut song = reactive_song_map(f64::INFINITY);
    grow_song_map(&mut song, log, 0);
    let level = load_level(&source("levels/reactive.yaml"), &lib, &song, None).unwrap();
    let input = SimInput::new(
        level,
        &lib,
        &atlas(),
        file.params.gate,
        file.log.hindsight.is_some(),
    );
    let rec = Rc::new(RefCell::new(LogRecorder::default()));
    let mut sim = Sim::new(input, Rc::new(log.clone()), Box::new(Rc::clone(&rec)), None);
    let dir = root().join(format!("native/oracle/fixtures/{track}/{profile}"));
    for k in gz_lines(&dir.join("keys.jsonl.gz")) {
        sim.key(KeyEvent {
            s: k["s"].as_f64().unwrap(),
            kind: if k["kind"] == "press" {
                KeyEventKind::Press
            } else {
                KeyEventKind::Release
            },
            key: key(k["key"].as_str().unwrap()),
            id: None,
        });
    }
    sim.advance_to(until);
    (rec, sim)
}

/// The stream as it stood at song time `c` (with `margin` for entries a step reads ahead): every
/// entry known by then.
fn known_by(log: &StreamLog, c: f64) -> StreamLog {
    fn cut<T: Clone>(xs: &[T], known: impl Fn(&T) -> f64, c: f64) -> Vec<T> {
        xs.iter().filter(|x| known(x) <= c).cloned().collect()
    }
    StreamLog {
        beats: cut(&log.beats, |b| b.known_at, c),
        ticks: cut(&log.ticks, |t| t.known_at, c),
        onsets: cut(&log.onsets, |o| o.known_at, c),
        hindsight: log.hindsight.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        timbre: log.timbre.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        changes: log.changes.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        jumps: log.jumps.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        ends: log.ends.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        forecasts: log.forecasts.as_ref().map(|h| cut(h, |x| x.known_at, c)),
    }
}

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

/// The same stream and keys give the same recording.
fn check_deterministic(track: &str, profile: &str, until: f64) {
    let file = stream_file(track);
    let log = with_silent_tail(file.log.clone(), 10.0);
    let (a, _) = live_run(track, profile, &log, until);
    let (b, _) = live_run(track, profile, &log, until);
    assert!(
        format!("{:?}", a.borrow()) == format!("{:?}", b.borrow()),
        "{track}/{profile}: two runs differ by {until}"
    );
}

/// Causality: run to song time c on the whole stream and on only what was known by then (and a
/// step's margin): the recordings are the same.
fn check_causal(track: &str, profile: &str, c: f64) {
    let file = stream_file(track);
    let log = with_silent_tail(file.log.clone(), 10.0);
    let (a, _) = live_run(track, profile, &log, c);
    let (b, _) = live_run(track, profile, &known_by(&log, c + 0.1), c);
    let (a, b) = (format!("{:?}", a.borrow()), format!("{:?}", b.borrow()));
    if a != b {
        let i = a
            .bytes()
            .zip(b.bytes())
            .position(|(x, y)| x != y)
            .unwrap_or(a.len().min(b.len()));
        let lo = i.saturating_sub(300);
        panic!(
            "{track}/{profile}: the run to {c} read entries known after it\n  whole  …{}\n  cut    …{}",
            &a[lo..(i + 200).min(a.len())],
            &b[lo..(i + 200).min(b.len())]
        );
    }
}

#[test]
fn a_live_run_ends_on_a_confirmed_end() {
    check_whole_run("causality-mix", "mash4");
}

#[test]
fn a_live_run_is_deterministic() {
    check_deterministic("causality-mix", "mash4", 45.0);
}

#[test]
fn a_live_run_reads_nothing_before_it_is_known() {
    check_causal("causality-mix", "mash4", 40.0);
}

const TRACKS: [&str; 5] = [
    "beethoven-fifth",
    "house-128",
    "william-tell-finale",
    "dnb-174",
    "notjam-lich-king-ascendant",
];

#[test]
fn live_runs_end_on_confirmed_ends() {
    for t in TRACKS {
        check_whole_run(t, "mash4");
    }
}

#[test]
fn live_runs_read_nothing_before_it_is_known() {
    for t in TRACKS {
        for c in [25.0, 50.0, 100.0] {
            check_causal(t, "mash4", c);
        }
    }
}
