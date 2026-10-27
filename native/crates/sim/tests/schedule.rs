//! The reactive level and schedule against the TS at ts-freeze.
//!
//! - The reactive level, loaded against each fixture track's whole reactive song map, has the
//!   canonical hash in F2's `meta.json` (`levelHash`).
//! - The schedule, grown beat by beat as Replay feeds the stream, equals the TS batch schedule at
//!   the end, bit for bit, on the oracle's core tracks (`schedule-vectors.json.gz`, from
//!   `gen-schedule-vectors.mts`).
#![allow(clippy::case_sensitive_file_extension_comparisons)]

use std::io::Read;
use std::path::{Path, PathBuf};

use serde_json::Value;
use sim::canonical::hash_hex;
use sim::content::level::{LevelScript, load_level};
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::schedule::{ReactiveSchedule, SlotKind, grow_song_map, reactive_song_map};
use sim::strides::stride_steps;
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
        .filter(|n| n.ends_with(".yaml"))
        .collect();
    names.sort();
    let files: Vec<SourceFile> = names
        .iter()
        .map(|n| source(&format!("content/{n}")))
        .collect();
    load_content(&files).unwrap()
}

fn stream(track: &str) -> StreamFile {
    StreamFile::parse(
        &std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
            .unwrap(),
    )
    .unwrap()
}

fn resolve(name: &str) -> Option<SourceFile> {
    let rel = format!("levels/{name}.yaml");
    root().join(&rel).exists().then(|| source(&rel))
}

/// The TS game's groove meter, as the fixtures' level hashes were taken with it.
fn ts_meter() -> Value {
    let mut text = String::new();
    flate2::read::GzDecoder::new(
        std::fs::File::open(root().join("native/oracle/content.json.gz")).unwrap(),
    )
    .read_to_string(&mut text)
    .unwrap();
    let ts: Value = serde_json::from_str(&text).unwrap();
    ts["hero"]["meter"].clone()
}

/// The reactive level, loaded against a stream's whole reactive song map.
fn level_for(lib: &ContentLibrary, file: &StreamFile) -> LevelScript {
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    load_level(&source("levels/reactive.yaml"), lib, &song, Some(&resolve))
        .unwrap_or_else(|e| panic!("{e}"))
}

#[test]
fn reactive_level_hash_matches_every_fixture() {
    let lib = library();
    let fixtures = root().join("native/oracle/fixtures");
    let mut tracks: Vec<String> = std::fs::read_dir(&fixtures)
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .collect();
    tracks.sort();
    assert!(tracks.len() >= 60);
    // (Classic mode's gentler groove costs, 2026-10-27, are not the TS game's: its costs stand in.)
    let ts = ts_meter();
    for track in tracks {
        let mut level = level_for(&lib, &stream(&track));
        for cost in ["counted", "offbeat", "stray"] {
            level.value["hero"]["meter"][cost] = ts[cost].clone();
        }
        let ours = hash_hex(&level.value);
        for p in std::fs::read_dir(fixtures.join(&track)).unwrap() {
            let meta: Value = serde_json::from_str(
                &std::fs::read_to_string(p.unwrap().path().join("meta.json")).unwrap(),
            )
            .unwrap();
            assert_eq!(
                ours,
                meta["levelHash"].as_str().unwrap(),
                "{track}: the loaded level's hash"
            );
        }
    }
}

#[test]
fn level_errors_name_the_file_and_path() {
    let lib = library();
    let file = stream("orpheus-galop");
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    let bad = |text: &str| {
        let f = SourceFile {
            name: "levels/bad.yaml".into(),
            text: text.into(),
        };
        load_level(&f, &lib, &song, Some(&resolve))
            .unwrap_err()
            .to_string()
    };
    let base = std::fs::read_to_string(root().join("levels/reactive.yaml")).unwrap();
    let e = bad(&base.replace("zone: twilight-glade", "zone: nowhere"));
    assert!(
        e.contains("levels/bad.yaml")
            && e.contains("sections[0].zone")
            && e.contains("unknown zone \"nowhere\""),
        "{e}"
    );
    let e = bad(&base.replace("seed: 2026", "seed: 2026.5"));
    assert!(e.contains("seed"), "{e}");
    let e = bad("extends: missing-level\n");
    assert!(e.contains("unknown level \"missing-level\""), "{e}");
    let e = bad(&base.replace(
        "    pace: 50\n",
        "    pace: 50\n    roster: { skeleton: 1 }\n",
    ));
    assert!(
        e.contains("a reactive level's section has no roster"),
        "{e}"
    );
}

/// f64 bits as written by the generator.
fn f(v: &Value) -> f64 {
    f64::from_bits(u64::from_str_radix(v.as_str().unwrap(), 16).unwrap())
}

fn fs(v: &Value) -> Vec<f64> {
    v.as_array().unwrap().iter().map(f).collect()
}

fn bits(xs: &[f64]) -> Vec<u64> {
    xs.iter().map(|x| x.to_bits()).collect()
}

#[test]
fn growing_schedule_equals_ts_batch_schedule() {
    let mut gz = String::new();
    let raw = std::fs::read(
        Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/data/schedule-vectors.json.gz"),
    )
    .unwrap();
    flate2::read::GzDecoder::new(&raw[..])
        .read_to_string(&mut gz)
        .unwrap();
    let vectors: Value = serde_json::from_str(&gz).unwrap();
    let lib = library();
    let tracks = vectors["tracks"].as_array().unwrap();
    assert!(tracks.len() >= 13);
    for t in tracks {
        let track = t["track"].as_str().unwrap();
        let file = stream(track);
        let level = level_for(&lib, &file);
        let mut sc = ReactiveSchedule::new(&level, file.audio.duration);
        let mut replay = Replay::new(file.log.clone());
        let mut log = replay.empty_log();
        let mut s = replay.next_known_at();
        let mut grew = 0;
        while s.is_finite() {
            replay.feed_to(&mut log, s);
            let before = sc.grid_times.len();
            sc.grow(&log);
            if sc.grid_times.len() > before {
                grew += 1;
                // At every moment, the strides are the batch strides of the grid so far.
                assert_eq!(
                    sc.stride_steps.s,
                    stride_steps(&sc.grid_times, level.hero.stride.max_period_s).s,
                    "{track} at {s}"
                );
            }
            s = replay.next_known_at();
        }
        assert!(grew > 50, "{track}: the schedule grew {grew} times");

        let sec = sc.section(Some(&file.end()));
        assert_eq!(
            bits(&[sec.s0, sec.s1, sec.b0, sec.b1]),
            bits(&fs(&t["sections"][0])),
            "{track}: section bounds"
        );
        assert_eq!(t["sections"].as_array().unwrap().len(), 1);
        assert_eq!(
            bits(&sc.grid_times),
            bits(&fs(&t["gridTimes"])),
            "{track}: gridTimes"
        );
        assert_eq!(
            bits(&sc.grid_times),
            bits(&fs(&t["strikeTimes"])),
            "{track}: strikeTimes"
        );
        let slots = t["gridSlots"].as_array().unwrap();
        assert_eq!(sc.grid_slots.len(), slots.len(), "{track}: gridSlots");
        for (ours, ts) in sc.grid_slots.iter().zip(slots) {
            let ts = ts.as_array().unwrap();
            let want: Vec<f64> = ts[..6].iter().map(f).collect();
            assert_eq!(
                bits(&[
                    ours.s,
                    ours.bi,
                    ours.bar,
                    ours.slot,
                    ours.section,
                    ours.accent
                ]),
                bits(&want),
                "{track}: slot {}",
                ours.bi
            );
            assert_eq!(
                ours.kind,
                if ts[6] == "grid" {
                    SlotKind::Grid
                } else {
                    SlotKind::Auto
                }
            );
            assert_eq!(ours.accent_label.as_deref(), ts[7].as_str());
        }
        let st = &t["strideSteps"];
        assert_eq!(
            bits(&sc.stride_steps.s),
            bits(&fs(&st["s"])),
            "{track}: strideSteps.s"
        );
        assert_eq!(
            bits(&sc.stride_steps.period),
            bits(&fs(&st["period"])),
            "{track}: strideSteps.period"
        );
        let per: Vec<u8> = st["per"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| u8::try_from(x.as_u64().unwrap()).unwrap())
            .collect();
        assert_eq!(sc.stride_steps.per, per, "{track}: strideSteps.per");
        let base: Vec<Vec<u64>> = sc.base_steps.iter().map(|x| bits(&[x.s, x.rate])).collect();
        let want: Vec<Vec<u64>> = t["baseSteps"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| bits(&fs(x)))
            .collect();
        assert_eq!(base, want, "{track}: baseSteps");
        let ups: Vec<(String, u64, u64)> = sc
            .level_ups
            .iter()
            .map(|x| (x.id.clone(), x.s.to_bits(), x.xp.to_bits()))
            .collect();
        let want: Vec<(String, u64, u64)> = t["levelUps"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| {
                (
                    x[0].as_str().unwrap().to_string(),
                    f(&x[1]).to_bits(),
                    f(&x[2]).to_bits(),
                )
            })
            .collect();
        assert_eq!(ups, want, "{track}: levelUps");
        // The song map grown live is TS's (its beats, and each beat's energy as known when committed)...
        assert_eq!(
            bits(&sc.song.beats),
            bits(&fs(&t["beats"])),
            "{track}: song map beats"
        );
        assert_eq!(
            bits(&sc.song.energy),
            bits(&fs(&t["energy"])),
            "{track}: song map energy"
        );
        // ...and the batch one.
        let mut batch = reactive_song_map(file.audio.duration);
        grow_song_map(&mut batch, &file.log, file.log.beats.len());
        assert_eq!(sc.song, batch, "{track}: song map");
    }
}

#[test]
fn stride_steps_subdivide_slow_steps_and_keep_the_last_step() {
    let st = stride_steps(&[0.0, 1.0, 1.3, 1.6], 0.4);
    assert_eq!(st.s, vec![0.0, 0.25, 0.5, 0.75, 1.0, 1.3, 1.6]);
    assert_eq!(st.per, vec![4, 4, 4, 4, 1, 1, 1]);
    // One grid time strides on its own, at the longest period.
    let one = stride_steps(&[2.0], 0.4);
    assert_eq!(
        (one.s, one.period, one.per),
        (vec![2.0].into(), vec![0.4].into(), vec![1].into())
    );
    assert!(stride_steps(&[], 0.4).s.is_empty());
}

#[test]
fn extends_merges_sections_by_label_and_hero_deeply() {
    let lib = library();
    let file = stream("orpheus-galop");
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    let child = SourceFile {
        name: "levels/child.yaml".into(),
        text: "extends: reactive\nseed: 7\nhero: { walk: { speed: 150 } }\nsections:\n  - section: reactive\n    pace: 60\n    camera: { zoom: 1.1 }\n".into(),
    };
    let level = load_level(&child, &lib, &song, Some(&resolve)).unwrap_or_else(|e| panic!("{e}"));
    let parent = level_for(&lib, &file);
    assert_eq!(level.seed, 7.0);
    assert_eq!(level.extends.as_deref(), Some("reactive"));
    assert_eq!(level.sections[0].pace, 60.0);
    // The camera merges field by field: zoom from the child, lead from the parent.
    assert_eq!(
        (level.sections[0].camera.zoom, level.sections[0].camera.lead),
        (1.1, 50.0)
    );
    assert_eq!(level.sections[0].zone, parent.sections[0].zone);
    // The hero merges deeply over the parent's (its health) and content/hero.yaml.
    assert_eq!(level.hero.walk.speed, 150.0);
    assert_eq!(level.hero.health.max, parent.hero.health.max);
    let unknown = SourceFile {
        name: "levels/child.yaml".into(),
        text: "extends: reactive\nsections:\n  - section: chorus\n    pace: 60\n".into(),
    };
    let e = load_level(&unknown, &lib, &song, Some(&resolve))
        .unwrap_err()
        .to_string();
    assert!(
        e.contains("unknown section \"chorus\" in the level this extends (reactive)"),
        "{e}"
    );
}

#[test]
fn beats_arriving_one_at_a_time_grow_the_batch_schedule() {
    let lib = library();
    let file = stream("orpheus-galop");
    let level = level_for(&lib, &file);
    let mut batch = ReactiveSchedule::new(&level, file.audio.duration);
    batch.grow(&file.log);
    let mut sc = ReactiveSchedule::new(&level, file.audio.duration);
    let mut log = file.log.clone();
    log.beats.clear();
    for b in &file.log.beats {
        log.beats.push(*b);
        sc.grow(&log);
        assert!(sc.grid_times.iter().all(|t| t.is_finite()));
    }
    assert_eq!(bits(&sc.grid_times), bits(&batch.grid_times));
    assert_eq!(sc.grid_slots, batch.grid_slots);
    assert_eq!(sc.stride_steps, batch.stride_steps);
    assert_eq!(sc.song, batch.song);
    // Live, the section runs one period past the latest beat; with the end known, to the audio's end.
    let last = file.log.beats[file.log.beats.len() - 1];
    assert_eq!(sc.section(None).s1, last.t + last.period);
    assert!(sc.section(Some(&file.end())).s1 >= file.audio.duration);
}

#[test]
fn hero_errors_and_empty_section_labels_read_as_ts() {
    let lib = library();
    let file = stream("orpheus-galop");
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    let base = std::fs::read_to_string(root().join("levels/reactive.yaml")).unwrap();
    let load = |text: String| {
        let f = SourceFile {
            name: "levels/bad.yaml".into(),
            text,
        };
        load_level(&f, &lib, &song, Some(&resolve))
            .unwrap_err()
            .to_string()
    };
    let e = load(base.replace("hero: { health: { max: 200 } }", "hero: { health: 5 }"));
    assert!(e.contains("hero.health"), "{e}");
    // An empty section label is no label (TS tests it for truth).
    let e = load(base.replace(
        "  - section: reactive\n    bars: all\n",
        "  - section: \"\"\n",
    ));
    assert!(e.contains("a section needs bars or section"), "{e}");
}

#[test]
fn extends_matches_events_whatever_their_number_spelling() {
    // Two levels whose events differ only in writing bar 3 as 3 and 3.0: the child's event merges
    // into the parent's (TS compares their canonical JSON) rather than adding one.
    let lib = library();
    let file = stream("orpheus-galop");
    let mut song = reactive_song_map(file.audio.duration);
    grow_song_map(&mut song, &file.log, file.log.beats.len());
    let enemy = lib.enemies.keys().next().unwrap();
    let parent = format!(
        "song: reactive\nseed: 1\ntitle: {{ piece: p, composer: c, performer: q }}\nzones: [twilight-glade]\nsections:\n  - section: reactive\n    pace: 50\n    presses: xx\n    zone: twilight-glade\n    sfxPalette: crisp-steel\n    roster: {{ {enemy}: 1 }}\n    population: [1, 2]\nevents:\n  - {{ at: {{ bar: 3 }}, banner: one }}\n"
    );
    let resolve_parent = |name: &str| {
        (name == "parent").then(|| SourceFile {
            name: "levels/parent.yaml".into(),
            text: parent.clone(),
        })
    };
    let child = SourceFile {
        name: "levels/child.yaml".into(),
        text: "extends: parent\nevents:\n  - { at: { bar: 3.0 }, banner: two }\n".into(),
    };
    let mut issues = Vec::new();
    match load_level(&child, &lib, &song, Some(&resolve_parent)) {
        Ok(level) => {
            assert_eq!(level.events.len(), 1);
            assert_eq!(level.events[0]["banner"], "two");
        }
        Err(e) => issues.push(e.to_string()),
    }
    assert!(issues.is_empty(), "{issues:?}");
}
