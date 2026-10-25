//! K1's virtual-player smoke test (docs/design/native-app.md, "Checks without an oracle"): the
//! honest player and the masher on the Galop, driven live (no stream end) through the sim, assert
//! broad outcomes. A rule change may retune them, with its reason in the commit.
#![allow(clippy::case_sensitive_file_extension_comparisons)]

use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use players::{Reference, drive, reactive_player};
use sim::content::level::load_level;
use sim::content::{SourceFile, load_content};
use sim::engine::{Sim, SimInput};
use sim::recorder::LogRecorder;
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

/// What a run of `player` on `track` came to.
#[derive(Debug, PartialEq)]
struct Outcome {
    /// When the hero fell, if it did.
    fell: Option<f64>,
    /// Its average groove over the run, and the share of the run at tier 2 or higher.
    groove: f64,
    tier2: f64,
    presses: usize,
}

fn run(track: &str, player: &str) -> Outcome {
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .filter(|n| n.ends_with(".yaml"))
        .collect();
    names.sort();
    let lib = load_content(
        &names
            .iter()
            .map(|n| source(&format!("content/{n}")))
            .collect::<Vec<_>>(),
    )
    .unwrap();
    let text = std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
        .unwrap();
    let (file, reference) = StreamFile::split(&text).unwrap();
    let reference = Reference::from_json(&reference.expect("a reference")).unwrap();
    let mut song = reactive_song_map(f64::INFINITY);
    grow_song_map(&mut song, &file.log, 0);
    let level = load_level(&source("levels/reactive.yaml"), &lib, &song, None).unwrap();
    let (seed, pace) = (level.seed, level.sections[0].pace);
    let input = SimInput::new(
        level,
        &lib,
        &indexmap::IndexMap::new(),
        file.params.gate,
        file.log.hindsight.is_some(),
    );
    let step = (
        input.lib.hero.step.length,
        input.lib.hero.step.duration_ms / 1000.0,
    );
    let rec = Rc::new(RefCell::new(LogRecorder::default()));
    let mut sim = Sim::new(
        input,
        Rc::new(file.log.clone()),
        Box::new(Rc::clone(&rec)),
        None,
    );
    let mut p = reactive_player(step, seed, &reference, pace, player).expect("a player");
    drive(&mut sim, p.as_mut(), file.audio.duration);
    let r = rec.borrow();
    let fell = r
        .health
        .0
        .iter()
        .zip(&r.health.1)
        .find(|x| *x.1 <= 0.0)
        .map(|x| *x.0);
    // Average groove and tier 2+ share over song time, from their step series.
    let share = |xs: &(Vec<f64>, Vec<f64>), f: &dyn Fn(f64) -> f64| {
        let end = file.audio.duration;
        let mut total = 0.0;
        for i in 0..xs.0.len() {
            let t1 = xs.0.get(i + 1).copied().unwrap_or(end).min(end);
            total += f(xs.1[i]) * (t1 - xs.0[i]).max(0.0);
        }
        total / end
    };
    Outcome {
        fell,
        groove: share(&r.groove, &|v| v),
        tier2: share(&r.tier, &|t| if t >= 2.0 { 1.0 } else { 0.0 }),
        presses: r.inputs.len(),
    }
}

#[test]
fn the_honest_player_outplays_the_masher_on_the_galop() {
    let honest = run("orpheus-galop", "reactive-moderate");
    let masher = run("orpheus-galop", "mash4");
    for (name, o) in [("reactive-moderate", &honest), ("mash4", &masher)] {
        eprintln!(
            "{name}: fell {:?}, groove {:.2}, tier 2+ {:.0}%, {} presses",
            o.fell,
            o.groove,
            100.0 * o.tier2,
            o.presses
        );
    }
    assert!(
        honest.presses > 100 && masher.presses > 100,
        "the players hardly pressed"
    );
    assert!(
        honest.groove > masher.groove,
        "the masher's groove is as good as the honest player's"
    );
    assert!(
        honest.tier2 > masher.tier2,
        "the masher spends as long at tier 2+ as the honest player"
    );
    // The honest player usually lives; the masher usually falls (or at least falls first).
    assert!(
        honest.fell.is_none(),
        "the honest player fell at {:?}",
        honest.fell
    );
    assert!(masher.fell.is_some(), "the masher lived to the end");
}

/// A player driving the sim is as deterministic as replayed keys: the same run twice comes out the same.
#[test]
fn a_driven_run_is_deterministic() {
    assert_eq!(run("causality-mix", "mash4"), run("causality-mix", "mash4"));
}
