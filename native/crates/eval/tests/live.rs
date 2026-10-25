//! The engine on a growing stream (G1): the house track's committed stream fed as it would arrive, a
//! press on each committed beat, and the view the drawer reads.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use eval::live::{Engine, Moment, Record, SETTLE_S};
use indexmap::IndexMap;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::core::rng::{Part, Rng};
use sim::movement::InputKey;
use sim::player::KeyEventKind;
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
        engine.feed(|log| replay.feed_to(log, s));
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

fn house() -> StreamFile {
    StreamFile::parse(
        &std::fs::read_to_string(root().join("public/reactive/house-128/stream.json")).unwrap(),
    )
    .unwrap()
}

/// A press and release on each committed beat before `end`: (when it reaches the runner, its
/// time, the key, pressed), in arrival order.
type Arrivals = Vec<(f64, f64, InputKey, bool)>;

fn presses(file: &StreamFile, end: f64, mut late: impl FnMut() -> f64) -> Arrivals {
    let mut keys = Vec::new();
    for (k, b) in file.log.beats.iter().enumerate().filter(|(_, b)| b.t < end) {
        let key = if k % 2 == 0 {
            InputKey::Right
        } else {
            InputKey::Down
        };
        keys.push((b.t + late(), b.t, key, true));
        keys.push((b.t + 0.08 + late(), b.t + 0.08, key, false));
    }
    keys.sort_by(|a, b| a.0.total_cmp(&b.0));
    keys
}

/// Runs the engine over `frames` with `lead`, the stream fed by `feed` before each frame and the
/// keys as they arrive; returns the records as they settled, and the engine.
fn run(
    frames: &[f64],
    lead: f64,
    keys: &Arrivals,
    mut feed: impl FnMut(&mut Engine, f64),
) -> (Vec<(f64, Record)>, Engine) {
    let file = house();
    let mut e = Engine::new(
        &source("levels/reactive.yaml"),
        &library(),
        &IndexMap::new(),
        file.params.gate,
        file.log.hindsight.is_some(),
        f64::INFINITY,
    )
    .unwrap();
    e.settled_log = Some(Vec::new());
    let mut next = 0;
    for &now in frames {
        feed(&mut e, now);
        while next < keys.len() && keys[next].0 <= now {
            let (_, s, key, pressed) = keys[next];
            e.key(s, key, pressed);
            next += 1;
        }
        e.frame(now, lead);
    }
    // (Everything settles, the stream fed to the end.)
    let last = frames.last().copied().unwrap_or(0.0) + 1.0;
    feed(&mut e, last);
    e.frame(last, 0.0);
    (e.settled_log.take().unwrap(), e)
}

/// Frame times to `end`: 1 ms to 50 ms apart, as frames come in bursts.
fn bursty(end: f64, seed: &str) -> Vec<f64> {
    let mut r = Rng::new(&[Part::Str(seed)]);
    let (mut t, mut frames) = (0.0, Vec::new());
    while t < end {
        t = (t + if r.next() < 0.5 {
            0.001
        } else {
            r.range(0.01, 0.05)
        })
        .min(end);
        frames.push(t);
    }
    frames
}

fn assert_same(a: &[(f64, Record)], b: &[(f64, Record)]) {
    assert!(a.len() > 1000, "{} records", a.len());
    if let Some(k) = (0..a.len().min(b.len())).find(|&k| a[k] != b[k]) {
        panic!("record {k} differs:\n  {:?}\n  {:?}", a[k], b[k]);
    }
    assert_eq!(a.len(), b.len());
}

/// Speculation with rollback settles as a plain run fed the keys in time (docs/design/
/// native-app.md, "Arrivals roll back like late keys"): frames in uneven bursts, the simulation
/// 0.2 s ahead, every press reaching it up to just under the settling time late, so each rolls it
/// back.
#[test]
fn speculation_settles_as_a_run_fed_its_keys_in_time() {
    let file = house();
    let end = 20.0;
    let mut r = Rng::new(&[Part::Str("late")]);
    let late = presses(&file, end, || r.range(0.0, SETTLE_S - 0.001));
    let mut replay = Replay::new(file.log.clone());
    let (ahead, e) = run(&bursty(end, "frames"), 0.2, &late, |e, now| {
        e.feed(|log| replay.feed_to(log, now));
    });
    assert!(e.stats.rollbacks > 50, "{:?}", e.stats);
    assert_eq!(e.stats.late_keys, 0);
    let in_time = presses(&file, end, || 0.0);
    let even: Vec<f64> = (1..=(end * 60.0) as usize)
        .map(|k| k as f64 / 60.0)
        .collect();
    let mut replay = Replay::new(file.log.clone());
    let (plain, p) = run(&even, 0.0, &in_time, |e, now| {
        e.feed(|log| replay.feed_to(log, now));
    });
    assert_eq!(p.stats.rollbacks, 0, "fed in time");
    assert_same(&ahead, &plain);
    println!(
        "{} records settled alike; {} rollbacks replaying {:.1} s",
        ahead.len(),
        e.stats.rollbacks,
        e.stats.replayed_s
    );
}

/// Bursts against batch: the stream arriving in random bursts, each rolling the simulation back,
/// settles as the stream given whole, under the same frames and keys (some later than the
/// settling time, applied at it). (A burst more than the settling time late is stamped at the
/// settled time, so it is not the batch's.)
#[test]
fn bursts_of_arrivals_settle_as_the_stream_given_whole() {
    let file = house();
    let end = 20.0;
    let frames = bursty(end, "frames");
    let mut r = Rng::new(&[Part::Str("very late")]);
    let keys = presses(&file, end, || r.range(0.0, 0.1));
    let mut replay = Replay::new(file.log.clone());
    let mut r = Rng::new(&[Part::Str("bursts")]);
    let mut burst_at = 0.0;
    let (bursts, b) = run(&frames, 0.2, &keys, |e, now| {
        if now >= burst_at {
            e.feed(|log| replay.feed_to(log, now));
            burst_at = now + r.range(0.0, SETTLE_S - 0.012);
        }
    });
    let mut whole = Some(file.log.clone());
    let (batch, w) = run(&frames, 0.2, &keys, |e, _| {
        if let Some(log) = whole.take() {
            e.feed(|l| *l = log);
        }
    });
    assert!(b.stats.late_keys > 0 && b.stats.late_keys == w.stats.late_keys);
    assert_same(&bursts, &batch);
}

/// A restored snapshot carries on as the run it was taken from would.
#[test]
fn a_restored_snapshot_carries_on_as_the_run_would() {
    let file = house();
    let key = sim::player::KeyEvent {
        s: 0.27667,
        kind: KeyEventKind::Press,
        key: InputKey::Right,
        id: None,
    };
    let start = |frames: usize| {
        let mut e = Engine::new(
            &source("levels/reactive.yaml"),
            &library(),
            &IndexMap::new(),
            file.params.gate,
            file.log.hindsight.is_some(),
            f64::INFINITY,
        )
        .unwrap();
        let mut replay = Replay::new(file.log.clone());
        for k in 1..=frames {
            let now = k as f64 / 60.0;
            e.feed(|log| replay.feed_to(log, now));
            e.sim.advance_to(now);
        }
        e.feed(|log| replay.feed_to(log, 0.3));
        e
    };
    let mut a = start(16);
    let (snap, mark) = (a.sim.snapshot(), a.journal.borrow().mark());
    a.sim.advance_to(0.3);
    a.sim.restore(&snap);
    a.journal.borrow_mut().rewind(&mark);
    a.sim.key(key);
    a.sim.advance_to(0.32);
    let mut b = start(16);
    b.sim.key(key);
    b.sim.advance_to(0.32);
    let (ja, jb) = (a.journal.borrow(), b.journal.borrow());
    assert_eq!(ja.end(), jb.end());
    for k in 0..ja.end() {
        assert_eq!(ja.get(k), jb.get(k), "record {k}");
    }
}

/// Each moment reaches the drawer once: under speculation the stream's arrivals roll the
/// simulation back over and over, and the unsettled records are laid over the view afresh each
/// frame, yet the moments taken match a plain run's, one for one.
#[test]
fn each_moment_is_passed_on_once() {
    let file = house();
    let end = 20.0;
    let moments_of = |frames: &[f64], lead: f64| {
        let mut replay = Replay::new(file.log.clone());
        let mut taken = Vec::new();
        let (_, e) = run(frames, lead, &Vec::new(), |e, now| {
            e.feed(|log| replay.feed_to(log, now));
            taken.extend(e.view.borrow_mut().take_moments());
        });
        taken.extend(e.view.borrow_mut().take_moments());
        let mut d: Vec<String> = taken.iter().map(|m| format!("{m:?}")).collect();
        d.sort();
        (d, e.stats.rollbacks)
    };
    let even: Vec<f64> = (1..=(end * 60.0) as usize)
        .map(|k| k as f64 / 60.0)
        .collect();
    let (plain, _) = moments_of(&even, 0.0);
    let (ahead, rollbacks) = moments_of(&bursty(end, "moments"), 0.2);
    assert!(
        rollbacks > 50 && plain.len() > 100,
        "{rollbacks} rollbacks, {} moments",
        plain.len()
    );
    assert_eq!(ahead, plain);
}

/// An arrival stamped before the settled time (a stall) is stamped at it, and rolls nothing
/// before it back.
#[test]
fn a_stalled_arrival_is_stamped_at_the_settled_time() {
    let (_, mut e) = run(&[1.0, 2.0], 0.2, &Vec::new(), |_, _| {});
    let settled = 3.0 - SETTLE_S;
    e.frame(3.0, 0.2);
    let rollbacks = e.stats.rollbacks;
    let beat = stream::Beat {
        t: 3.5,
        known_at: 2.5,
        period: 0.47,
        evidence: 0.9,
    };
    e.feed(|log| log.beats.push(beat));
    let stamped = e.stream().beats.last().unwrap().known_at;
    assert!((stamped - settled).abs() < 1e-12, "{stamped}");
    e.frame(3.01, 0.2);
    assert_eq!(e.stats.rollbacks, rollbacks + 1);
}
