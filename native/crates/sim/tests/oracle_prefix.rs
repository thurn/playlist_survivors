//! Native replay invariants. (The TS judging reference retired with classic mode's fairer rules.)
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
use serde_json::Value;
use sim::content::level::load_level;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::core::warp::WarpBreak;
use sim::engine::{AnimInfo, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::{self, Recorder};
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
