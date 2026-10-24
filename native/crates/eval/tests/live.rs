//! The engine on a growing stream (G1): the house track's committed stream fed as it would arrive, a
//! press on each committed beat, and the view the drawer reads.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use eval::live::{Engine, Moment};
use indexmap::IndexMap;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::movement::InputKey;
use stream::{Replay, StreamFile};

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
        .filter(|n| Path::new(n).extension().is_some_and(|x| x == "yaml"))
        .collect();
    names.sort();
    let files: Vec<SourceFile> = names
        .iter()
        .map(|n| source(&format!("content/{n}")))
        .collect();
    load_content(&files).unwrap()
}

#[test]
fn the_engine_plays_a_growing_stream() {
    let file = StreamFile::parse(
        &std::fs::read_to_string(root().join("public/reactive/house-128/stream.json")).unwrap(),
    )
    .unwrap();
    let lib = library();
    let mut engine = Engine::new(
        &source("levels/reactive.yaml"),
        &lib,
        &IndexMap::new(),
        file.params.gate,
        file.log.hindsight.is_some(),
        f64::INFINITY,
    )
    .unwrap();
    let mut replay = Replay::new(file.log.clone());
    let beats: Vec<f64> = file.log.beats.iter().map(|b| b.t).collect();
    let mut next_beat = 0;
    let (mut presses, mut judged, mut deaths, mut most_foes, mut settled) = (0, 0, 0, 0, 0);
    for frame in 1..=(30 * 60) {
        let s = f64::from(frame) / 60.0;
        replay.feed_to(engine.log(), s);
        // A press (and release) on each committed beat as its time comes round.
        while next_beat < beats.len() && beats[next_beat] <= s {
            // (Right on even beats, Down on odd ones: the hero strides on the beat.)
            let key = if next_beat % 2 == 0 {
                InputKey::Right
            } else {
                InputKey::Down
            };
            engine.key(beats[next_beat], key, true);
            engine.key(beats[next_beat] + 0.08, key, false);
            next_beat += 1;
        }
        engine.advance_to(s);
        let mut v = engine.view.borrow_mut();
        most_foes = most_foes.max(v.alive().count());
        for m in v.take_moments() {
            match m {
                Moment::Press { rating, .. } => {
                    presses += 1;
                    judged += usize::from(rating.is_some());
                }
                Moment::Settled { .. } => settled += 1,
                Moment::Death { .. } => deaths += 1,
                _ => {}
            }
        }
    }
    let v = engine.view.borrow();
    let hero = v.hero.expect("the hero is placed");
    assert!(hero.x.is_finite() && hero.y.is_finite());
    assert!(
        presses > 20 && judged > 0,
        "{presses} presses, {judged} judged"
    );
    assert!(most_foes > 0, "no enemy came");
    assert!((0.0..=1.0).contains(&v.health));
    println!(
        "30 s: {presses} presses ({judged} judged, {settled} settled), up to {most_foes} enemies, {deaths} deaths, health {:.2}, groove {:.2}",
        v.health, v.groove
    );
}
