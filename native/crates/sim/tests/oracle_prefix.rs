//! Native replay invariants and the retained legacy judging reference.
#![allow(
    clippy::case_sensitive_file_extension_comparisons,
    clippy::float_cmp,
    clippy::many_single_char_names
)]

use std::cell::RefCell;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use indexmap::IndexMap;
use serde_json::{Value, json};
use sim::content::level::load_level;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::core::warp::WarpBreak;
use sim::engine::{AnimInfo, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::{self, LogRecorder, Recorder};
use sim::schedule::{grow_song_map, reactive_song_map};
use stream::StreamFile;

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

/// The hero atlas's animations (`heroAnimsFromAtlas`).
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

/// A recorder the test keeps a handle on.
struct Shared(Rc<RefCell<LogRecorder>>);

impl Recorder for Shared {
    fn warp(&mut self, b: WarpBreak, r: bool) {
        self.0.borrow_mut().warp(b, r);
    }
    fn spawn(&mut self, rec: recorder::EnemyRecord) {
        self.0.borrow_mut().spawn(rec);
    }
    fn sample(&mut self, id: usize, x: f64, y: f64) {
        self.0.borrow_mut().sample(id, x, y);
    }
    fn fate(&mut self, id: usize, f: recorder::Fate) {
        self.0.borrow_mut().fate(id, f);
    }
    fn death(&mut self, id: usize, w: f64, r: bool) {
        self.0.borrow_mut().death(id, w, r);
    }
    fn hit(&mut self, h: recorder::Hit) {
        self.0.borrow_mut().hit(h);
    }
    fn swing(&mut self, sw: recorder::PlanSwing) {
        self.0.borrow_mut().swing(sw);
    }
    fn input(&mut self, i: recorder::PlanInput) {
        self.0.borrow_mut().input(i);
    }
    fn groove(&mut self, s: f64, v: f64) {
        self.0.borrow_mut().groove(s, v);
    }
    fn tier(&mut self, s: f64, w: f64, t: f64, ss: f64) {
        self.0.borrow_mut().tier(s, w, t, ss);
    }
    fn health(&mut self, s: f64, f: f64) {
        self.0.borrow_mut().health(s, f);
    }
    fn weapon(&mut self, id: &str, s: f64) {
        self.0.borrow_mut().weapon(id, s);
    }
    fn fire(&mut self, w: &str, s: f64) {
        self.0.borrow_mut().fire(w, s);
    }
    fn chain(&mut self, p: recorder::ChainPoint) {
        self.0.borrow_mut().chain(p);
    }
    fn chain_point(&mut self, c: usize, p: recorder::ChainPoint) {
        self.0.borrow_mut().chain_point(c, p);
    }
    fn bite(&mut self, b: recorder::BiteRecord) {
        self.0.borrow_mut().bite(b);
    }
    fn heal(&mut self, h: recorder::HealRecord) {
        self.0.borrow_mut().heal(h);
    }
    fn chicken(&mut self, c: recorder::ChickenRecord) {
        self.0.borrow_mut().chicken(c);
    }
    fn chicken_taken(&mut self, i: usize, s: f64, w: f64, h: f64) {
        self.0.borrow_mut().chicken_taken(i, s, w, h);
    }
    fn vacuum(&mut self, v: recorder::VacuumRecord) {
        self.0.borrow_mut().vacuum(v);
    }
    fn vacuum_touch(&mut self, i: usize, s: f64, w: f64, g: f64) {
        self.0.borrow_mut().vacuum_touch(i, s, w, g);
    }
    fn upgrade(&mut self, u: sim::upgrades::UpgradeRecord) {
        self.0.borrow_mut().upgrade(u);
    }
    fn upgrade_taken(&mut self, i: usize, k: usize, s: f64, w: f64, a: f64, wk: bool) {
        self.0.borrow_mut().upgrade_taken(i, k, s, w, a, wk);
    }
    fn xp(&mut self, s: f64, f: f64) {
        self.0.borrow_mut().xp(s, f);
    }
    fn wave(&mut self, c: recorder::Cleave) {
        self.0.borrow_mut().wave(c);
    }
    fn enemy_step(&mut self, id: usize, w: f64, d: f64) {
        self.0.borrow_mut().enemy_step(id, w, d);
    }
    fn settle(&mut self, r: recorder::SettleRecord) {
        self.0.borrow_mut().settle(r);
    }
}

/// Two JSON values equal as the oracle compares them: numbers by value (JSON can't carry -0, NaN or
/// infinities: they read 0, null and null); objects field by field.
fn same(a: &Value, b: &Value) -> bool {
    match (a, b) {
        (Value::Number(x), Value::Number(y)) => x.as_f64() == y.as_f64(),
        (Value::Object(x), Value::Object(y)) => {
            x.len() == y.len() && x.iter().all(|(k, v)| y.get(k).is_some_and(|w| same(v, w)))
        }
        (Value::Array(x), Value::Array(y)) => {
            x.len() == y.len() && x.iter().zip(y).all(|(v, w)| same(v, w))
        }
        _ => a == b,
    }
}

/// The first difference between two values, as a path.
fn diff(a: &Value, b: &Value, path: &str) -> Option<String> {
    match (a, b) {
        (Value::Object(x), Value::Object(y)) => {
            for k in x.keys().chain(y.keys()) {
                match (x.get(k), y.get(k)) {
                    (Some(p), Some(q)) => {
                        if let Some(d) = diff(p, q, &format!("{path}.{k}")) {
                            return Some(d);
                        }
                    }
                    (p, q) => return Some(format!("{path}.{k}: ours {p:?}, TS {q:?}")),
                }
            }
            None
        }
        (Value::Array(x), Value::Array(y)) if x.len() == y.len() => x
            .iter()
            .zip(y)
            .enumerate()
            .find_map(|(i, (p, q))| diff(p, q, &format!("{path}[{i}]"))),
        _ => (!same(a, b)).then(|| format!("{path}: ours {a}, TS {b}")),
    }
}

// FR1 deliberately replaces TS enemy glide, gait slots, contact and the resulting
// target/kill timelines. Exact TS event-log comparisons are retired. The native
// frame/snapshot checks below retain deterministic replay; FR1's catalog and
// recorded-music displacement tests validate the new beat/hold behavior.

/// A recorder that keeps every call it gets, serialized, for comparing two runs' sequences.
#[derive(Clone, Default)]
struct Calls(Rc<RefCell<Vec<String>>>);

impl Calls {
    fn add(&self, s: String) {
        self.0.borrow_mut().push(s);
    }
}

impl Recorder for Calls {
    fn warp(&mut self, b: WarpBreak, r: bool) {
        self.add(format!("warp {b:?} {r}"));
    }
    fn hero(&mut self, x: f64, y: f64, z: f64, f: f64, a: f64, fr: f64) {
        self.add(format!("hero {x} {y} {z} {f} {a} {fr}"));
    }
    fn camera(&mut self, x: f64, y: f64, zoom: f64) {
        self.add(format!("camera {x} {y} {zoom}"));
    }
    fn spawn(&mut self, rec: recorder::EnemyRecord) {
        self.add(format!("spawn {}", recorder::record_json(&rec)));
    }
    fn sample(&mut self, id: usize, x: f64, y: f64) {
        self.add(format!("sample {id} {x} {y}"));
    }
    fn fate(&mut self, id: usize, f: recorder::Fate) {
        self.add(format!("fate {id} {}", recorder::record_json(&f)));
    }
    fn death(&mut self, id: usize, w: f64, r: bool) {
        self.add(format!("death {id} {w} {r}"));
    }
    fn hit(&mut self, h: recorder::Hit) {
        self.add(format!("hit {}", recorder::record_json(&h)));
    }
    fn swing(&mut self, sw: recorder::PlanSwing) {
        self.add(format!("swing {}", recorder::record_json(&sw)));
    }
    fn input(&mut self, i: recorder::PlanInput) {
        self.add(format!("input {}", recorder::record_json(&i)));
    }
    fn groove(&mut self, s: f64, v: f64) {
        self.add(format!("groove {s} {v}"));
    }
    fn settle(&mut self, r: recorder::SettleRecord) {
        self.add(format!("settle {}", recorder::record_json(&r)));
    }
    fn enemy_step(&mut self, id: usize, w: f64, d: f64) {
        self.add(format!("step {id} {w} {d}"));
    }
}

/// A run of a fixture track's keys to `until`, recording every call into `calls`.
fn keyed_sim(track: &str, until: f64, calls: &Calls) -> Sim {
    let lib = library();
    let file = StreamFile::parse(
        &std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
            .unwrap(),
    )
    .unwrap();
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    let level = load_level(&source("levels/reactive.yaml"), &lib, &song, None).unwrap();
    let input = SimInput::new(
        level,
        &lib,
        &atlas(),
        file.params.gate,
        file.log.hindsight.is_some(),
    );
    let mut sim = Sim::new(
        input,
        Rc::new(file.log.clone()),
        Box::new(calls.clone()),
        Some(until),
    );
    let dir = root().join(format!("native/oracle/fixtures/{track}/reactive-moderate"));
    for k in gz_lines(&dir.join("keys.jsonl.gz")) {
        let e = KeyEvent {
            s: k["s"].as_f64().unwrap(),
            kind: if k["kind"] == "press" {
                KeyEventKind::Press
            } else {
                KeyEventKind::Release
            },
            key: key(k["key"].as_str().unwrap()),
            id: None,
        };
        if e.s < until {
            sim.key(e);
        }
    }
    sim
}

/// FORMAT.md's frame procedure (advancing to each frame's time plus SPECULATE_S, with the clock
/// moves between the loop's own stops that this makes) records exactly what one run to the end does,
/// stream ingestion included.
#[test]
fn frames_change_nothing() {
    for track in ["house-128", "cc-kimiko-ishizaka-aria"] {
        let until = 20.0;
        let plain = Calls::default();
        keyed_sim(track, until, &plain).run_to_end();
        let framed = Calls::default();
        let mut sim = keyed_sim(track, until, &framed);
        let mut k = 0.0;
        while k / 60.0 < sim.end_s() {
            sim.advance_to(f64::min(sim.end_s(), k / 60.0 + 0.2));
            k += 1.0;
        }
        sim.run_to_end();
        let (a, b) = (plain.0.borrow(), framed.0.borrow());
        let first = a.iter().zip(b.iter()).position(|(x, y)| x != y);
        assert!(
            first.is_none() && a.len() == b.len(),
            "{track}: call {first:?} differs ({} calls, framed {})",
            a.len(),
            b.len()
        );
    }
}

/// Restoring a snapshot and running on records exactly what running on from it did the first time.
#[test]
fn a_restored_snapshot_runs_on_the_same() {
    let calls = Calls::default();
    let mut sim = keyed_sim("orpheus-galop", 30.0, &calls);
    sim.advance_to(8.0);
    let snap = sim.snapshot();
    let first = Calls::default();
    sim.rec = Box::new(first.clone());
    sim.advance_to(16.0);
    sim.restore(&snap);
    let again = Calls::default();
    sim.rec = Box::new(again.clone());
    sim.advance_to(16.0);
    let (a, b) = (first.0.borrow(), again.0.borrow());
    assert!(a.len() > 1000, "{} calls", a.len());
    let diff = a.iter().zip(b.iter()).position(|(x, y)| x != y);
    assert!(
        diff.is_none() && a.len() == b.len(),
        "call {diff:?} differs ({} calls, again {})",
        a.len(),
        b.len()
    );
}

/// An input's judging fields: what the press was and how it was judged.
fn judging_fields(v: &Value) -> Value {
    let mut out = serde_json::Map::new();
    for k in [
        "s", "kind", "key", "rating", "delta", "class", "extra", "offbeat", "unjudged", "stomp",
        "rock", "bend", "steps",
    ] {
        if let Some(x) = v.get(k) {
            out.insert(k.into(), x.clone());
        }
    }
    Value::Object(out)
}

/// B5: judging without hindsight (presses change the groove when pressed; the drain runs on each
/// beat), which no shipped stream exercises: against TS runs of three streams with their hindsight
/// removed (`judging-vectors.json.gz`, from `gen-judging-vectors.mts`).
#[test]
fn judging_without_hindsight_matches_ts() {
    let raw = std::fs::read(
        Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/data/judging-vectors.json.gz"),
    )
    .unwrap();
    let mut text = String::new();
    flate2::read::GzDecoder::new(&raw[..])
        .read_to_string(&mut text)
        .unwrap();
    let vectors: Value = serde_json::from_str(&text).unwrap();
    let lib = library();
    let anims = atlas();
    for run in vectors["runs"].as_array().unwrap() {
        let (track, profile) = (
            run["track"].as_str().unwrap(),
            run["profile"].as_str().unwrap(),
        );
        assert_eq!(run["settles"], 0);
        let mut file = StreamFile::parse(
            &std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
                .unwrap(),
        )
        .unwrap();
        file.log.hindsight = None;
        let mut song = reactive_song_map(file.audio.duration);
        grow_song_map(&mut song, &file.log, file.log.beats.len());
        let level = load_level(&source("levels/reactive.yaml"), &lib, &song, None).unwrap();
        let input = SimInput::new(level, &lib, &anims, file.params.gate, false);
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        let mut sim = Sim::new(
            input,
            Rc::new(file.log.clone()),
            Box::new(Shared(Rc::clone(&rec))),
            // (Live runs end on a confirmed end; this one is bounded where TS's ran to at least.)
            Some(file.audio.duration + 1.0),
        );
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
        sim.run_to_end();
        let end = sim.end_s();
        let rec = rec.borrow();
        assert!(rec.settles.is_empty());
        let before = |v: &Value| v[0].as_f64().is_some_and(|t| t < end);
        let series = |s: &(Vec<f64>, Vec<f64>)| {
            s.0.iter()
                .zip(&s.1)
                .map(|(&t, &v)| json!([t, v]))
                .collect::<Vec<_>>()
        };
        let inputs_before = |list: Vec<Value>| {
            list.into_iter()
                .filter(|v| v["s"].as_f64().is_some_and(|t| t < end))
                .collect::<Vec<_>>()
        };
        let cases: Vec<(&str, Vec<Value>, Vec<Value>)> = vec![
            (
                "groove",
                series(&rec.groove),
                run["groove"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .filter(|v| before(v))
                    .cloned()
                    .collect(),
            ),
            (
                "tier",
                series(&rec.tier),
                run["tier"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .filter(|v| before(v))
                    .cloned()
                    .collect(),
            ),
            (
                "input",
                inputs_before(rec.inputs.iter().map(judging_fields).collect()),
                inputs_before(run["inputs"].as_array().unwrap().clone()),
            ),
        ];
        for (k, ours, theirs) in cases {
            for (i, (a, b)) in ours.iter().zip(&theirs).enumerate() {
                assert!(
                    same(a, b),
                    "{track}/{profile} without hindsight: {k} #{i} differs\n  ours {a}\n  TS   {b}\n  {}",
                    diff(a, b, "v").unwrap_or_default()
                );
            }
            assert_eq!(
                ours.len(),
                theirs.len(),
                "{track}/{profile} without hindsight: {k} lines"
            );
            assert!(
                ours.len() > 3,
                "{track}/{profile}: {k} has {} lines",
                ours.len()
            );
        }
    }
}
