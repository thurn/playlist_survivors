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
        false,
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
    feed: impl FnMut(&mut Engine, f64),
) -> (Vec<(f64, Record)>, Engine) {
    run_with(frames, lead, keys, feed, false, false)
}

fn run_with(
    frames: &[f64],
    lead: f64,
    keys: &Arrivals,
    mut feed: impl FnMut(&mut Engine, f64),
    keep_keys: bool,
    zen: bool,
) -> (Vec<(f64, Record)>, Engine) {
    let file = house();
    let mut e = Engine::new(
        &source("levels/reactive.yaml"),
        &library(),
        &IndexMap::new(),
        file.params.gate,
        file.log.hindsight.is_some(),
        zen,
        f64::INFINITY,
    )
    .unwrap();
    e.settled_log = Some(Vec::new());
    if keep_keys {
        e.key_log = Some(Vec::new());
    }
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

/// `run`, keeping the keys as the engine applied them.
fn run_keeping(
    frames: &[f64],
    lead: f64,
    keys: &Arrivals,
    applied: &mut Vec<sim::player::KeyEvent>,
    feed: impl FnMut(&mut Engine, f64),
) -> (Vec<(f64, Record)>, Engine) {
    let (log, mut e) = run_with(frames, lead, keys, feed, true, false);
    *applied = e.key_log.take().unwrap();
    (log, e)
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

/// Zen mode's waiting taps and stomps roll back as cleanly: late presses settle as presses fed in
/// time.
#[test]
fn zen_speculation_settles_as_a_run_fed_its_keys_in_time() {
    let file = house();
    let end = 20.0;
    let mut r = Rng::new(&[Part::Str("zen late")]);
    let late = presses(&file, end, || r.range(0.0, SETTLE_S - 0.001));
    let mut replay = Replay::new(file.log.clone());
    let (ahead, e) = run_with(
        &bursty(end, "zen frames"),
        0.2,
        &late,
        |e, now| e.feed(|log| replay.feed_to(log, now)),
        false,
        true,
    );
    assert!(e.stats.rollbacks > 50, "{:?}", e.stats);
    let in_time = presses(&file, end, || 0.0);
    let even: Vec<f64> = (1..=(end * 60.0) as usize)
        .map(|k| k as f64 / 60.0)
        .collect();
    let mut replay = Replay::new(file.log.clone());
    let (plain, _) = run_with(
        &even,
        0.0,
        &in_time,
        |e, now| e.feed(|log| replay.feed_to(log, now)),
        false,
        true,
    );
    assert_same(&ahead, &plain);
}

/// Keys later than the settling time are applied at the settled time, and the run settles as a
/// plain run fed those applied times in time (hv-t8o8.80: the sim's stops, and so where it took
/// stream entries in, followed when a future key was queued).
#[test]
fn very_late_keys_settle_as_a_run_fed_their_applied_times() {
    for seed in ["very late keys", "a", "b", "c", "d", "e"] {
        very_late_keys_with(seed);
    }
    // The case G5 saw: every key 70 ms late, frames every 1/60 s, no lead.
    let file = house();
    let late = presses(&file, 5.0, || 0.07);
    let even: Vec<f64> = (1..=300).map(|k| f64::from(k) / 60.0).collect();
    let mut replay = Replay::new(file.log.clone());
    let mut keys_applied = Vec::new();
    let (ahead, e) = run_keeping(&even, 0.0, &late, &mut keys_applied, |e, now| {
        e.feed(|log| replay.feed_to(log, now));
    });
    assert!(e.stats.late_keys > 5, "{:?}", e.stats);
    let in_time: Arrivals = keys_applied
        .iter()
        .map(|k| (k.s, k.s, k.key, k.kind == KeyEventKind::Press))
        .collect();
    let mut replay = Replay::new(file.log.clone());
    let (plain, _) = run(&even, 0.0, &in_time, |e, now| {
        e.feed(|log| replay.feed_to(log, now));
    });
    assert_same(&ahead, &plain);
}

fn very_late_keys_with(seed: &str) {
    let file = house();
    let end = 20.0;
    let mut r = Rng::new(&[Part::Str(seed)]);
    let late = presses(&file, end, || r.range(SETTLE_S, SETTLE_S + 0.3));
    let mut replay = Replay::new(file.log.clone());
    let mut keys_applied = Vec::new();
    let (ahead, e) = run_keeping(
        &bursty(end, seed),
        0.2,
        &late,
        &mut keys_applied,
        |e, now| {
            e.feed(|log| replay.feed_to(log, now));
        },
    );
    assert!(e.stats.late_keys > 10, "{:?}", e.stats);
    let in_time: Arrivals = keys_applied
        .iter()
        .map(|k| (k.s, k.s, k.key, k.kind == KeyEventKind::Press))
        .collect();
    let even: Vec<f64> = (1..=(end * 60.0) as usize)
        .map(|k| k as f64 / 60.0)
        .collect();
    let mut replay = Replay::new(file.log.clone());
    let (plain, _) = run(&even, 0.0, &in_time, |e, now| {
        e.feed(|log| replay.feed_to(log, now));
    });
    assert_same(&ahead, &plain);
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
            false,
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

#[test]
fn a_late_confirm_rolls_back_across_freeze_open_and_settles_as_on_time() {
    use sim::survivors::FreezeReason;
    use stream::{Beat, StreamLog, Tick};
    let content = library();
    let src = source("levels/survivors.yaml");
    let log = StreamLog {
        beats: (0..=40)
            .map(|i| {
                let t = 0.5 + f64::from(i) * 0.5;
                Beat {
                    t,
                    known_at: (t - 0.2).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..=400)
            .map(|i| {
                let t = f64::from(i) / 20.0;
                Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy: 0.5,
                    level: Some(stream::Level::Medium),
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        ..StreamLog::default()
    };
    let run = |lead: f64, late: f64| {
        let mut engine = Engine::new(
            &src,
            &content,
            &IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .unwrap();
        engine.settle_s = 0.1;
        engine.settled_log = Some(Vec::new());
        engine.feed(|out| *out = log.clone());
        engine.frame(0.5, lead);
        engine.sim.freeze_at(1.0, FreezeReason::LevelUp);
        let mut next = 0;
        let keys = [(1.02, true), (1.03, false)];
        for i in 51..=600 {
            let now = f64::from(i) / 100.0;
            while next < keys.len() && keys[next].0 + late <= now + 1e-9 {
                engine.key(keys[next].0, InputKey::Confirm, keys[next].1);
                next += 1;
            }
            engine.frame(now, lead);
        }
        engine.frame(6.5, 0.0);
        (engine.settled_log.take().unwrap(), engine)
    };
    let (on_time, plain) = run(0.0, 0.0);
    let (late, ahead) = run(0.4, 0.03);
    assert!(ahead.stats.key_rollbacks > 0);
    assert!(ahead.stats.deepest_s > 0.3);
    assert_eq!(on_time.len(), late.len(), "settled record counts");
    let n = on_time.len();
    let hash = eval::live::hash_records(&on_time);
    assert_eq!(hash, eval::live::hash_records(&late));
    for (i, (a, b)) in on_time[..n].iter().zip(&late[..n]).enumerate() {
        assert_eq!(format!("{a:?}"), format!("{b:?}"), "record {i}");
    }
    assert!(plain.view.borrow().run.unwrap().confirmed);
    assert!(ahead.view.borrow().run.unwrap().confirmed);
    assert_eq!(plain.sim.run_state(), ahead.sim.run_state());
    eprintln!(
        "freeze Confirm: {} matching settled records, hash {hash:016x}, {} key rollbacks, deepest {} s",
        n, ahead.stats.key_rollbacks, ahead.stats.deepest_s
    );
}

#[test]
fn a_growing_gem_keeps_its_xp_value_for_survivors_color_selection() {
    let mut view = eval::live::LiveView::default();
    view.apply(
        1.0,
        &Record::Gem {
            x: 0.0,
            y: 0.0,
            s1: f64::INFINITY,
            big: false,
        },
    );
    view.apply(1.0, &Record::GemValue { gem: 0, value: 2.0 });
    assert_eq!(
        sim::survivors::gem_color(view.gems[&0].value),
        sim::survivors::GemColor::Blue
    );
    view.apply(2.0, &Record::GemGrow { gem: 0, big: true });
    view.apply(
        2.0,
        &Record::GemValue {
            gem: 0,
            value: 11.0,
        },
    );
    assert_eq!(
        sim::survivors::gem_color(view.gems[&0].value),
        sim::survivors::GemColor::Red
    );
}

#[test]
fn survivor_spawner_speculation_matches_on_time_at_density() {
    use stream::{Beat, StreamLog, Tick};
    let content = library();
    let mut src = source("levels/survivors.yaml");
    assert!(src.text.contains("\"minimum\":15"));
    src.text = src.text.replacen("\"minimum\":15", "\"minimum\":300", 1);
    let log = StreamLog {
        beats: (0..=24)
            .map(|i| {
                let t = f64::from(i) * 0.5;
                Beat {
                    t,
                    known_at: (t - 3.0).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..=240)
            .map(|i| {
                let t = f64::from(i) / 20.0;
                Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy: 0.5,
                    level: Some(stream::Level::Medium),
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        ..StreamLog::default()
    };
    let fresh = || {
        let mut e = Engine::new(
            &src,
            &content,
            &IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .unwrap();
        e.sim.disable_hero_damage_for_check();
        e.settle_s = 0.1;
        e.settled_log = Some(Vec::new());
        e.feed(|out| *out = log.clone());
        e.key(0.0, InputKey::Right, true);
        e.sim.freeze_at(1.123, sim::survivors::FreezeReason::Paused);
        e.key(1.4, InputKey::Right, false);
        e.key(2.137, InputKey::Pause, true);
        e.key(2.14, InputKey::Pause, false);
        e.key(3.0, InputKey::Right, true);
        e
    };
    let mut a = fresh();
    let mut b = fresh();
    let mut compared = 0;
    let mut peak = 0;
    for i in 1..=800 {
        let now = f64::from(i) / 100.0;
        a.frame(now, 0.0);
        b.frame(now, 0.2);
        peak = peak.max(b.view.borrow().alive().count());
        let ar = a.settled_log.as_mut().unwrap();
        let br = b.settled_log.as_mut().unwrap();
        let n = ar.len().min(br.len());
        for k in 0..n {
            assert_eq!(
                format!("{:?}", ar[k]),
                format!("{:?}", br[k]),
                "at {now}, record {}",
                compared + k
            );
        }
        ar.drain(..n);
        br.drain(..n);
        compared += n;
    }
    assert_eq!(peak, 300);
    assert!(a.settled_log.as_ref().unwrap().is_empty());
    assert!(b.settled_log.as_ref().unwrap().is_empty());
    eprintln!("density speculation with off-grid pause: {compared} matching settled records");
}

fn fast_offer_fixture() -> (ContentLibrary, stream::StreamLog) {
    use stream::{Beat, StreamLog, Tick};
    let mut content = library();
    let bat = content.enemies.get_mut("vs-bat3").unwrap();
    bat.health = 0.0001;
    let stats = bat.survivors.as_mut().unwrap();
    stats.speed = 1000.0;
    stats.xp = 20.0;
    let log = StreamLog {
        beats: (0..=100)
            .map(|i| {
                let t = f64::from(i) * 0.5;
                Beat {
                    t,
                    known_at: (t - 3.0).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..=1000)
            .map(|i| {
                let t = f64::from(i) / 20.0;
                Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy: 0.5,
                    level: Some(stream::Level::Medium),
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        ..StreamLog::default()
    };
    (content, log)
}

// Actual-card/record-projection coverage, not pacing: weak XP carriers fund the
// cards; a separate durable cohort remains hittable by every weapon after Garlic8.
fn all_weapons_offer_fixture() -> (ContentLibrary, stream::StreamLog, SourceFile) {
    let (mut content, mut log) = fast_offer_fixture();
    let carrier = content
        .enemies
        .get_mut("vs-bat3")
        .unwrap()
        .survivors
        .as_mut()
        .unwrap();
    carrier.xp = sim::survivors::total_xp(22);
    // This projection fixture needs real collected XP, not an AFK balance test.
    // Finish a carrier's admitted step inside pickup reach before the 190px hit.
    carrier.speed = 10_000.0;
    let target = content.enemies.get_mut("vs-bat2").unwrap();
    target.health = 1_000_000.0;
    target.radius = 120.0; // Durable fixture body overlaps the maxed aura and orbit while holding at contact.
    let stats = target.survivors.as_mut().unwrap();
    stats.speed = 1000.0;
    stats.xp = 0.0;
    stats.knockback = 0.0;
    stats.res_debuffs = 1.0; // Garlic cannot grow recoil on this durable contact fixture.
    let mut level = source("levels/survivors.yaml");
    let row = serde_json::json!({"minute":0,"minimum":2,"frequency":1_000_000,
        "enemies":["vs-bat3"],"bosses":["vs-bat2"],"events":[],"treasure":[]});
    let mut inserted = false;
    level.text = level
        .text
        .lines()
        .filter_map(|line| {
            if line.trim_start().starts_with("- {\"minute\":") {
                if inserted {
                    None
                } else {
                    inserted = true;
                    Some(format!("    - {row}"))
                }
            } else {
                Some(line.to_string())
            }
        })
        .collect::<Vec<_>>()
        .join("\n");
    for i in 101..=240 {
        let t = f64::from(i) * 0.5;
        log.beats.push(stream::Beat {
            t,
            known_at: (t - 3.0).max(0.0),
            period: 0.5,
            evidence: 1.0,
        });
    }
    for i in 1001..=2400 {
        let t = f64::from(i) / 20.0;
        log.ticks.push(stream::Tick {
            t,
            known_at: t,
            evidence: 1.0,
            energy: 0.5,
            level: Some(stream::Level::Medium),
            key: None,
            bands: None,
            brightness: None,
        });
    }
    (content, log, level)
}

#[test]
fn a_real_xp_offer_pick_applies_once_across_speculative_rollback() {
    let (content, log, level) = all_weapons_offer_fixture();
    let run = |lead: f64, late: f64| {
        let mut e = Engine::new(
            &level,
            &content,
            &IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .unwrap();
        e.sim.disable_hero_damage_for_check();
        e.settle_s = 0.1;
        e.settled_log = Some(Vec::new());
        e.feed(|out| *out = log.clone());
        let mut picked = None;
        let mut picks = 0;
        for k in 1..=10000 {
            let now = f64::from(k) / 100.0;
            let offer = e.view.borrow().run.and_then(|r| r.offer);
            if let Some(o) = offer {
                let token = (o.level, o.reveals[2].to_bits());
                let at = o.reveals[2] + 0.02;
                if picked != Some(token) && now + 1e-9 >= at + late {
                    e.key(at, InputKey::Confirm, true);
                    e.key(at + 0.005, InputKey::Confirm, false);
                    picked = Some(token);
                    picks += 1;
                }
            }
            e.frame(now, lead);
        }
        e.frame(100.8, 0.0);
        let inventory = e.sim.survivor_inventory().unwrap();
        let ranks: usize = inventory
            .weapons
            .iter()
            .map(|s| usize::from(s.rank))
            .sum::<usize>()
            + inventory
                .passives
                .iter()
                .map(|s| usize::from(s.rank))
                .sum::<usize>();
        assert!(picks > 0, "real gem XP opened at least one offer");
        assert!(
            inventory
                .weapons
                .rank(sim::survivors::WeaponId::Garlic)
                .is_some(),
            "real XP choices exercise the persistent aura across rollback"
        );
        assert!(
            inventory
                .weapons
                .rank(sim::survivors::WeaponId::MagicWand)
                .is_some(),
            "real XP choices exercise projectiles and bursts across rollback"
        );
        assert!(
            inventory
                .weapons
                .rank(sim::survivors::WeaponId::KingBible)
                .is_some(),
            "real XP choices exercise orbit cycles and per-blade memories across rollback"
        );
        assert!(
            inventory
                .weapons
                .rank(sim::survivors::WeaponId::LightningRing)
                .is_some(),
            "actual offers reach Lightning Ring before comparing rollback"
        );
        assert_eq!(ranks, 1 + picks, "each confirm applied exactly one rank");
        (
            e.settled_log.take().unwrap(),
            inventory,
            e.view.borrow().recap.clone(),
            e.stats,
            picks,
        )
    };
    let (plain, pi, pr, _, picks) = run(0.0, 0.0);
    let (late, li, lr, stats, late_picks) = run(0.4, 0.03);
    assert_eq!(
        pr, lr,
        "recap timers and damage survive actual late offer-pick rollback"
    );
    assert_eq!(pi, li);
    assert_eq!(picks, late_picks);
    assert!(stats.key_rollbacks > 0);
    assert_eq!(plain.len(), late.len());
    assert_same(&plain, &late);
    let pulses: std::collections::BTreeSet<_> = plain
        .iter()
        .filter_map(|(_, r)| {
            if let Record::Run(run) = r {
                run.upgrade.map(|p| p.song.to_bits())
            } else {
                None
            }
        })
        .collect();
    let sounds: Vec<_> = plain
        .iter()
        .filter_map(|(_, r)| {
            if let Record::Sound(eval::live::Caused {
                cause: eval::live::Cause::LevelUp { s },
                ..
            }) = r
            {
                Some(s.to_bits())
            } else {
                None
            }
        })
        .collect();
    assert!(
        !pulses.is_empty(),
        "actual card resolution resumes with a world-space celebration"
    );
    assert_eq!(
        sounds.len(),
        pulses.len(),
        "one cue per resumed celebration, including chained cards"
    );
    assert_eq!(
        sounds
            .into_iter()
            .collect::<std::collections::BTreeSet<_>>(),
        pulses
    );
    let strike_count = plain
        .iter()
        .filter(|(_, r)| matches!(r, Record::Moment(Moment::LightningStrike(_))))
        .count();
    assert!(
        strike_count > 0,
        "actual positioned lightning strikes survive rollback"
    );
    eprintln!("actual lightning strike records in rollback fixture: {strike_count}");
    let mut orbit_lives = std::collections::BTreeMap::new();
    for (_, record) in &plain {
        if let Record::Missile(p) = record
            && p.weapon == sim::survivors::WeaponId::KingBible
        {
            let life = orbit_lives.entry(p.id).or_insert((p.w, p.w));
            life.1 = p.w;
        }
    }
    assert!(
        orbit_lives.values().any(|(first, last)| last - first > 1.7),
        "actual orbital motion spans a hit-memory reset across rollback"
    );
    eprintln!("actual Bible blade lifetimes in rollback fixture: {orbit_lives:?}");
    eprintln!(
        "real XP offers: {picks} picks applied once, {} matching records, {} key rollbacks, deepest {:.3}s",
        plain.len(),
        stats.key_rollbacks,
        stats.deepest_s
    );
}

#[test]
fn rewinding_before_real_xp_retracts_the_projected_offer_without_a_pick() {
    let (content, log) = fast_offer_fixture();
    let mut e = Engine::new(
        &source("levels/survivors.yaml"),
        &content,
        &IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    e.sim.disable_hero_damage_for_check();
    e.feed(|out| *out = log);
    let before = e.sim.snapshot();
    let mark = e.journal.borrow().mark();
    e.sim.advance_to(10.0);
    let project = |e: &Engine| {
        let mut view = eval::live::LiveView::default();
        let journal = e.journal.borrow();
        for k in 0..journal.end() {
            let (s, r) = journal.get(k).unwrap();
            view.apply(*s, r);
        }
        view
    };
    assert!(project(&e).run.unwrap().offer.is_some());
    e.sim.restore(&before);
    e.journal.borrow_mut().rewind(&mark);
    e.sim.advance_to(0.1);
    let view = project(&e);
    assert!(view.run.unwrap().offer.is_none());
    assert_eq!(
        view.inventory
            .unwrap()
            .weapons
            .rank(sim::survivors::WeaponId::Sword),
        Some(1)
    );
    assert!(!view.run.unwrap().confirmed);
}

#[test]
fn survivor_grid_reactions_hold_a_dead_body_until_its_impact_even_through_a_freeze() {
    use eval::live::{LiveView, Moment, Record};
    let mut view = LiveView::default();
    view.apply(0.0, &Record::Run(sim::survivors::RunState::default()));
    let hit = Moment::Hit {
        x: 12.0,
        y: 20.0,
        lethal: true,
        damage: 1.0,
        song: 0.6,
        critical: false,
        foe: 0,
        source: "weapon",
        weight: "light",
        what: "sword".into(),
        dir: 0.0,
        intensity: 1.0,
    };
    view.apply(
        0.0,
        &Record::Spawn {
            id: 0,
            kind: "vs-bat3".into(),
        },
    );
    view.apply(0.6, &Record::Moment(hit.clone()));
    view.apply(
        0.6,
        &Record::Death {
            id: 0,
            w: 0.6,
            recycled: false,
        },
    );
    assert_eq!(view.alive().count(), 0, "logical death counts at once");
    assert!(view.foes[&0].pending_impact);
    view.apply(
        5.0,
        &Record::Spawn {
            id: 1,
            kind: "vs-bat3".into(),
        },
    );
    view.apply(
        5.0,
        &Record::Death {
            id: 1,
            w: 5.0,
            recycled: false,
        },
    );
    assert!(
        view.foes.contains_key(&0),
        "unseen grid reaction survives dead-body pruning"
    );
    view.apply(
        5.125,
        &Record::Moment(Moment::EnemyImpact { hit: Box::new(hit) }),
    );
    assert!(!view.foes[&0].pending_impact);
    assert_eq!(view.kills, 2, "visual reactions never count another kill");
}

#[test]
fn speculative_pickup_sounds_derive_from_actual_gem_records_without_hash_records() {
    use eval::live::Cause;
    let (lib, log) = fast_offer_fixture();
    let mut engine = Engine::new(
        &source("levels/survivors.yaml"),
        &lib,
        &IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    engine.settled_log = Some(Vec::new());
    engine.feed(|l| *l = log);
    let mut seen = std::collections::BTreeSet::new();
    for i in 1..=600 {
        engine.frame(f64::from(i) / 120.0, 0.3);
        let j = engine.journal.borrow();
        for c in engine.causes() {
            if let Cause::Gem { s, gem } = c.cause {
                assert!(c.known <= engine.frontier());
                assert!(
                    (0..j.end())
                        .filter_map(|k| j.get(k))
                        .chain(engine.settled_log.as_ref().unwrap().iter())
                        .any(|(t, r)| *t == s && matches!(r,Record::GemArrive{gem:id} if *id==gem))
                );
                seen.insert((s.to_bits(), gem));
            }
        }
        assert!(
            (0..j.end())
                .filter_map(|k| j.get(k))
                .all(|(_, r)| !matches!(
                    r,
                    Record::Sound(eval::live::Caused {
                        cause: Cause::Gem { .. },
                        ..
                    })
                ))
        );
    }
    assert!(!seen.is_empty(), "the actual pickup path must be exercised");
}

#[test]
fn sword_impact_sounds_follow_existing_grid_records_without_hash_records() {
    use eval::live::Cause;
    let (mut lib, log) = fast_offer_fixture();
    // Isolate grid reactions from an XP offer that would intentionally hold them.
    lib.enemies
        .get_mut("vs-bat3")
        .unwrap()
        .survivors
        .as_mut()
        .unwrap()
        .xp = 0.0;
    let mut engine = Engine::new(
        &source("levels/survivors.yaml"),
        &lib,
        &IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    engine.settled_log = Some(Vec::new());
    engine.feed(|l| *l = log);
    let mut seen = 0;
    for i in 1..=600 {
        engine.frame(f64::from(i) / 120.0, 0.3);
        let journal = engine.journal.borrow();
        let all = || {
            (0..journal.end())
                .filter_map(|k| journal.get(k))
                .chain(engine.settled_log.as_ref().unwrap().iter())
        };
        for c in engine.causes() {
            if let Cause::WeaponImpact { s, what, accent } = c.cause {
                assert!(all().any(|(t,r)| *t==s && matches!(r,Record::Moment(Moment::EnemyImpact{hit}) if matches!(hit.as_ref(),Moment::Hit{what:id,weight,..} if id==what.as_str() && (*weight=="heavy")==accent))));
                seen += 1;
            }
        }
        assert!(all().all(|(_, r)| !matches!(
            r,
            Record::Sound(eval::live::Caused {
                cause: Cause::WeaponImpact { .. },
                ..
            })
        )));
    }
    assert!(seen > 0, "actual sword impacts must be exercised");
}

#[test]
fn survivor_weapon_sounds_and_missile_trails_follow_actual_records() {
    use eval::live::Cause;
    let (content, log, level) = all_weapons_offer_fixture();
    let mut e = Engine::new(
        &level,
        &content,
        &IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    e.sim.disable_hero_damage_for_check();
    e.settled_log = Some(Vec::new());
    e.feed(|l| *l = log);
    let mut picked = None;
    let mut pulses = std::collections::BTreeMap::new();
    let mut contacts = std::collections::BTreeMap::new();
    let mut peak_missiles = 0;
    for k in 1..=12000 {
        let now = f64::from(k) / 120.0;
        let offer = e.view.borrow().run.and_then(|r| r.offer);
        if let Some(o) = offer {
            let token = (o.level, o.reveals[2].to_bits());
            if picked != Some(token) && now >= o.reveals[2] + 0.03 {
                picked = Some(token);
                e.key(now, InputKey::Confirm, true);
                e.key(now + 0.005, InputKey::Confirm, false);
            }
        }
        e.frame(now, 0.3);
        let j = e.journal.borrow();
        let all = || {
            (0..j.end())
                .filter_map(|k| j.get(k))
                .chain(e.settled_log.as_ref().unwrap().iter())
        };
        for c in e.causes() {
            match c.cause {
                Cause::WeaponPulse {
                    s, what, accent, ..
                } if matches!(
                    what,
                    sim::survivors::WeaponId::Garlic
                        | sim::survivors::WeaponId::MagicWand
                        | sim::survivors::WeaponId::KingBible
                        | sim::survivors::WeaponId::LightningRing
                ) =>
                {
                    if what == sim::survivors::WeaponId::LightningRing {
                        assert!(all().any(|(t,r)| *t==s && matches!(r,Record::Moment(Moment::LightningStrike(strike)) if strike.weapon==what && strike.accent==accent)));
                    } else {
                        assert!(all().any(|(t,r)| *t==s && matches!(r,Record::Moment(Moment::WeaponFire(shot)) if shot.id==what && shot.accent==accent)));
                    }
                    *pulses.entry(what).or_insert(0usize) += 1;
                }
                Cause::WeaponImpact { s, what, .. }
                    if matches!(
                        what,
                        sim::survivors::WeaponId::Garlic
                            | sim::survivors::WeaponId::MagicWand
                            | sim::survivors::WeaponId::KingBible
                            | sim::survivors::WeaponId::LightningRing
                    ) =>
                {
                    assert!(all().any(|(t,r)| *t==s && matches!(r,Record::Moment(Moment::EnemyImpact{hit}) if matches!(hit.as_ref(),Moment::Hit{what:id,..} if id==what.as_str()))));
                    *contacts.entry(what).or_insert(0usize) += 1;
                }
                Cause::Weapon { ref what, .. } => assert!(
                    !matches!(
                        what.as_str(),
                        "garlic" | "magic-wand" | "king-bible" | "lightning-ring"
                    ),
                    "legacy fire is replaced by typed projection"
                ),
                _ => {}
            }
        }
        let view = e.view.borrow();
        peak_missiles = peak_missiles.max(view.missiles.len());
        for trail in view.missiles.values() {
            assert!((1..=10).contains(&trail.len()));
            assert!(trail.windows(2).all(|p| p[0].w <= p[1].w));
            let head = trail.last().unwrap();
            assert!(all().any(|(_, r)| matches!(r,Record::Missile(p) if p==head)));
        }
        assert!(all().all(|(_, r)| !matches!(
            r,
            Record::Sound(eval::live::Caused {
                cause: Cause::WeaponPulse { .. } | Cause::WeaponImpact { .. },
                ..
            })
        )));
    }
    for id in [
        sim::survivors::WeaponId::Garlic,
        sim::survivors::WeaponId::MagicWand,
        sim::survivors::WeaponId::KingBible,
        sim::survivors::WeaponId::LightningRing,
    ] {
        assert!(
            pulses.get(&id).copied().unwrap_or(0) > 0
                && contacts.get(&id).copied().unwrap_or(0) > 0,
            "actual pulse/contact paths exercised for {id:?}: {pulses:?}/{contacts:?}"
        );
    }
    assert!(peak_missiles > 0, "actual projectile view path exercised");
}

#[test]
fn pickup_records_use_the_fixed_stop_timestamp_and_rewind_with_the_journal() {
    use eval::live::{Journal, LiveRecorder, LiveView};
    use sim::{
        recorder::Recorder,
        survivors::{FloorChicken, GemPose},
    };
    let journal = std::rc::Rc::new(std::cell::RefCell::new(Journal::default()));
    let mut recorder = LiveRecorder(std::rc::Rc::clone(&journal));
    let mark = journal.borrow().mark();
    recorder.gem_pose(
        0.017,
        GemPose {
            id: 7,
            w: 0.017,
            x: 60.0,
            y: 2.0,
        },
    );
    recorder.floor_chicken(
        0.017,
        FloorChicken::Drop {
            id: 2,
            x: 5.0,
            y: 6.0,
        },
    );
    let mut view = LiveView::default();
    for i in 0..journal.borrow().end() {
        let j = journal.borrow();
        let (s, record) = j.get(i).unwrap();
        assert_eq!(
            *s, 0.017,
            "callback precedes clock() but owns its canonical stop"
        );
        view.apply(*s, record);
    }
    assert_eq!(view.gem_poses[&7].x, 60.0);
    assert_eq!(view.chickens[&2], (5.0, 6.0));
    journal.borrow_mut().rewind(&mark);
    assert_eq!(journal.borrow().end(), 0);
    recorder.floor_chicken(0.025, FloorChicken::Taken { id: 2 });
    recorder.floor_chicken(
        0.125,
        FloorChicken::Healed {
            id: 2,
            amount: 30.0,
        },
    );
    for i in 0..journal.borrow().end() {
        let j = journal.borrow();
        let (s, record) = j.get(i).unwrap();
        view.apply(*s, record);
    }
    assert!(view.chickens.is_empty());
    assert!(matches!(
        view.take_moments().as_slice(),
        [Moment::ChickenHeal {
            id: 2,
            amount: 30.0
        }]
    ));
}

#[test]
fn chest_freeze_rewards_and_field_records_survive_late_input_from_a_real_boss_kill() {
    use sim::survivors::{ChestEvent, WeaponId};
    fn engine() -> Engine {
        let mut content = library();
        content
            .enemies
            .get_mut("vs-bat4")
            .unwrap()
            .survivors
            .as_mut()
            .unwrap()
            .xp = 0.0;
        let mut level = source("levels/survivors.yaml");
        let old = r#"{"minute":0,"minimum":15,"frequency":1000,"enemies":["vs-bat3"],"bosses":[],"events":[],"treasure":[]}"#;
        assert!(level.text.contains(old));
        let row = serde_json::json!({"minute":0,"minimum":0,"frequency":60000,"enemies":[],"bosses":["vs-bat4"],"events":[],"treasure":[{"tier":3,"chances":[0,0,0],"level":1,"prizeTypes":"EAAAA","fixedPrizes":[]}]});
        level.text = level.text.replacen(old, &row.to_string(), 1);
        let mut out = Engine::new(
            &level,
            &content,
            &IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .unwrap();
        out.settled_log = Some(Vec::new());
        out
    }
    let file = house();
    let mut a = engine();
    let mut replay = Replay::new(file.log.clone());
    let mut held = Vec::new();
    let mut keys = Vec::new();
    let mut opened = None;
    for frame in 1..=3600 {
        let now = f64::from(frame) / 120.0;
        a.feed(|l| replay.feed_to(l, now));
        let view = a.view.borrow();
        let mut target = Vec::new();
        if let Some(r) = view.run.and_then(|r| r.roulette) {
            opened.get_or_insert(r.opened);
        }
        if view.run.is_some_and(|r| r.frozen.is_none())
            && let Some((c, h)) = view.chests.values().next().zip(view.hero)
        {
            let (dx, dy) = (c.x - h.x, c.y - h.y);
            if dx.abs() > dy.abs() * 0.414_213_562_373_095 {
                target.push(if dx < 0.0 {
                    InputKey::Left
                } else {
                    InputKey::Right
                });
            }
            if dy.abs() > dx.abs() * 0.414_213_562_373_095 {
                target.push(if dy < 0.0 {
                    InputKey::Up
                } else {
                    InputKey::Down
                });
            }
        }
        drop(view);
        for &k in &held {
            if !target.contains(&k) {
                a.key(now, k, false);
                keys.push((now, k, false));
            }
        }
        for &k in &target {
            if !held.contains(&k) {
                a.key(now, k, true);
                keys.push((now, k, true));
            }
        }
        held = target;
        a.frame(now, 0.0);
        a.view.borrow_mut().take_moments();
    }
    let opened = opened.expect("discovery run opened a real dropped chest");
    // A harmless confirm straddles the actual opening, forcing replay back across
    // it at nondefault lookahead. Chest input must not dismiss the roulette.
    keys.extend([
        (opened - 0.001, InputKey::Confirm, true),
        (opened + 0.001, InputKey::Confirm, false),
    ]);
    keys.sort_by(|a, b| a.0.total_cmp(&b.0));
    let run = |lead: f64, late: f64| {
        let mut e = engine();
        let mut replay = Replay::new(file.log.clone());
        let mut next = 0;
        for frame in 1..=3600 {
            let now = f64::from(frame) / 120.0;
            e.feed(|l| replay.feed_to(l, now));
            while next < keys.len() && keys[next].0 + late <= now + 1e-9 {
                let (s, k, down) = keys[next];
                e.key(s, k, down);
                next += 1;
            }
            e.frame(now, lead);
            e.view.borrow_mut().take_moments();
        }
        e.frame(30.5, 0.0);
        e
    };
    a = run(0.0, 0.0);
    let b = run(0.23, 0.025);
    assert_eq!(
        a.view
            .borrow()
            .inventory
            .unwrap()
            .weapons
            .rank(WeaponId::Sword),
        Some(6),
        "real kill/drop/touch applied five items"
    );
    let records = a.settled_log.as_ref().unwrap();
    let ticks: Vec<_> = records
        .iter()
        .filter_map(|(s, r)| matches!(r, Record::Chest(ChestEvent::Tick { .. })).then_some(*s))
        .collect();
    assert_eq!(ticks.len(), 8);
    assert!(
        records
            .iter()
            .any(|(_, r)| matches!(r, Record::Chest(ChestEvent::Drop(_))))
    );
    assert!(
        records
            .iter()
            .any(|(_, r)| matches!(r, Record::Chest(ChestEvent::Taken { .. })))
    );
    assert_eq!(
        records
            .iter()
            .filter(|(_, r)| matches!(r, Record::Chest(ChestEvent::Landed { .. })))
            .count(),
        1
    );
    assert!(b.stats.key_rollbacks > 0);
    assert!(b.stats.deepest_s > 0.2);
    assert_eq!(b.stats.late_keys, 0);
    assert_same(records, b.settled_log.as_ref().unwrap());
    println!(
        "SH10 chest actual-boss replay: {} records, hash{:016x}, {} key rollbacks, depth{:.6}s,8eighth ticks/one landing; lead0.23 late25ms",
        records.len(),
        eval::live::hash_records(records),
        b.stats.key_rollbacks,
        b.stats.deepest_s
    );
}

#[test]
fn survivor_swarm_drop_and_expiry_replay_exactly_across_late_pause_keys() {
    use stream::{Beat, Jump, StreamLog, Tick};
    let mut content = library();
    // Fixture data is installed before Engine captures its initial snapshot.
    // Durable XP-free event actors isolate scheduling/expiry from card freezes.
    let bat = content.enemies.get_mut("vs-bat-swarm").unwrap();
    bat.health = 1_000_000.0;
    bat.survivors.as_mut().unwrap().xp = 0.0;
    let mut level = source("levels/survivors.yaml");
    // Move the source minute-2 bat event to minute zero solely for a short
    // real-Engine rollback smoke. Keep source stats/count/delay/duration.
    let mut lines = level.text.lines().map(str::to_string).collect::<Vec<_>>();
    for line in &mut lines {
        if line.trim_start().starts_with("- {\"minute\":") {
            let mut row: serde_json::Value =
                serde_json::from_str(line.trim_start().strip_prefix("- ").unwrap()).unwrap();
            let minute = row["minute"].as_u64().unwrap();
            row["enemies"] = serde_json::json!([]);
            row["minimum"] = serde_json::json!(0);
            row["bosses"] = serde_json::json!([]);
            row["events"] = serde_json::json!([]);
            if minute == 0 {
                row["events"] = serde_json::json!([{"kind":"BAT_SWARM","delayMs":5000,"extraRepeats":2,"durationMs":10000,"count":50}]);
            }
            *line = format!("    - {row}");
        }
    }
    level.text = lines.join("\n");
    let log = StreamLog {
        beats: (0..=60)
            .map(|i| {
                let t = f64::from(i) * 0.5;
                Beat {
                    t,
                    known_at: (t - 0.5).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..=600)
            .map(|i| {
                let t = f64::from(i) * 0.05;
                Tick {
                    t,
                    known_at: t,
                    energy: 0.5,
                    evidence: 1.0,
                    level: None,
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        jumps: Some(vec![Jump {
            t: 2.0,
            known_at: 2.0,
            db: 9.0,
        }]),
        ..Default::default()
    };
    let keys = [
        (1.999, InputKey::Pause, true),
        (2.001, InputKey::Pause, false),
        (3.0, InputKey::Pause, true),
        (3.01, InputKey::Pause, false),
        (5.0, InputKey::Right, true),
        (12.0, InputKey::Right, false),
    ];
    let run = |lead, late| {
        let mut e = Engine::new(
            &level,
            &content,
            &IndexMap::new(),
            0.2,
            false,
            false,
            f64::INFINITY,
        )
        .unwrap();
        e.settled_log = Some(Vec::new());
        let mut replay = Replay::new(log.clone());
        let mut next = 0;
        let mut peak = 0;
        for frame in 1..=3000 {
            let now = f64::from(frame) / 120.0;
            e.feed(|l| replay.feed_to(l, now));
            while next < keys.len() && keys[next].0 + late <= now + 1e-9 {
                let (s, key, pressed) = keys[next];
                e.key(s, key, pressed);
                next += 1;
            }
            e.frame(now, lead);
            peak = peak.max(e.view.borrow().alive().count());
            e.view.borrow_mut().take_moments();
        }
        e.frame(25.5, 0.0);
        (e, peak)
    };
    let (a, peak) = run(0.0, 0.0);
    let (b, other) = run(0.23, 0.025);
    assert!(
        peak >= 50 && other >= 50,
        "actual event members were not admitted"
    );
    assert!(
        a.view.borrow().alive().next().is_none(),
        "all three attempt lifetimes expired"
    );
    assert!(b.stats.key_rollbacks > 0);
    assert!(b.stats.deepest_s > 0.2);
    let records = a.settled_log.as_ref().unwrap();
    assert_same(records, b.settled_log.as_ref().unwrap());
    println!(
        "SW6 actual event replay: {} records hash{:016x}, peak{}, {} late-key rollbacks depth{:.6}s, lead0.23 late25ms; normal-hero-damage HP1e6/XP0 event fixture",
        records.len(),
        eval::live::hash_records(records),
        peak,
        b.stats.key_rollbacks,
        b.stats.deepest_s
    );
}

#[test]
fn boss_notices_landings_and_camera_replay_across_late_pause_keys() {
    use sim::survivors::BossCueKind;
    let content = library();
    let mut level = source("levels/survivors.yaml");
    for minute in [0, 1] {
        let line = level
            .text
            .lines()
            .find(|l| l.trim().starts_with(&format!("- {{\"minute\":{minute},")))
            .unwrap()
            .to_string();
        let row = serde_json::json!({"minute":minute,"minimum":0,"frequency":1000,"enemies":[],"bosses":if minute==1 {vec!["vs-bat4"]} else {vec![]},"events":[],"treasure":[]});
        level.text = level.text.replace(&line, &format!("    - {row}"));
    }
    let period = 60.0 / 128.0;
    let log = stream::StreamLog {
        beats: (0..150)
            .map(|i| {
                let t = f64::from(i) * period;
                stream::Beat {
                    t,
                    known_at: (t - period).max(0.0),
                    period,
                    evidence: 1.0,
                }
            })
            .collect(),
        ..Default::default()
    };
    let keys = [
        (58.55, InputKey::Pause, true),
        (58.56, InputKey::Pause, false),
        (59.0, InputKey::Pause, true),
        (59.01, InputKey::Pause, false),
    ];
    let run = |lead: f64, late: f64, cut: f64| {
        let mut e = Engine::new(
            &level,
            &content,
            &IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
        )
        .unwrap();
        e.settled_log = Some(Vec::new());
        let mut replay = Replay::new(log.clone());
        let mut next = 0;
        for frame in 1..=(63.0 / cut).ceil() as usize {
            let now = frame as f64 * cut;
            e.feed(|l| replay.feed_to(l, now));
            while next < keys.len() && keys[next].0 + late <= now + 1e-9 {
                let (s, k, down) = keys[next];
                e.key(s, k, down);
                next += 1;
            }
            e.frame(now, lead);
            e.view.borrow_mut().take_moments();
        }
        e.frame(63.5, 0.0);
        e
    };
    let a = run(0.0, 0.0, 1.0 / 120.0);
    let b = run(0.237, 0.025, 0.017);
    let records = a.settled_log.as_ref().unwrap();
    assert_same(records, b.settled_log.as_ref().unwrap());
    let cues: Vec<_> = records
        .iter()
        .filter_map(|(_, r)| {
            if let Record::BossCue(c) = r {
                Some(*c)
            } else {
                None
            }
        })
        .collect();
    assert_eq!(cues.len(), 2);
    assert_eq!(cues[0].kind, BossCueKind::Warning);
    assert_eq!(cues[0].song, 58.125);
    assert_eq!(cues[1].kind, BossCueKind::Landed);
    assert!(cues[1].run >= 60.0);
    assert!((cues[1].song / (2.0 * period)).fract().abs() < 1e-8);
    assert!(b.stats.key_rollbacks > 0);
    println!(
        "SP2: {} exact records, warning{}, landing song{} run{}, late-key rollbacks{} at lead0.237/17ms cuts; normal source boss on isolated field",
        records.len(),
        cues[0].song,
        cues[1].song,
        cues[1].run,
        b.stats.key_rollbacks
    );
}

#[test]
fn restored_inventory_deliveries_project_one_fire_cue_and_actual_recap_damage() {
    use sim::survivors::WeaponId;
    let lib = library();
    let mut engine =
        eval::scale::restored_density_engine(&source("levels/survivors.yaml"), &lib).unwrap();
    let mut file = StreamFile::parse(
        &std::fs::read_to_string(root().join("public/reactive/house-128/stream.json")).unwrap(),
    )
    .unwrap();
    // Deliberately inject known forecasts on the real track's quarter grid to
    // exercise actual captured accent shots in this short production-routing check.
    file.log.forecasts = Some(
        file.log
            .beats
            .windows(2)
            .flat_map(|v| {
                (0..4).map(move |i| stream::Forecast {
                    t: v[0].t + (v[1].t - v[0].t) * f64::from(i) / 4.0,
                    known_at: v[0].known_at.max(v[1].known_at),
                    cycle: 4.0,
                    confidence: 1.0,
                })
            })
            .collect(),
    );
    let mut replay = Replay::new(file.log);
    engine.key(0.01, InputKey::Right, true);
    let mut index = engine.journal.borrow().end();
    let mut shots = std::collections::BTreeMap::<WeaponId, usize>::new();
    let mut fires = std::collections::BTreeMap::<WeaponId, usize>::new();
    let mut projected = std::collections::BTreeSet::new();
    let mut accented = std::collections::BTreeSet::new();
    let mut impacts = std::collections::BTreeSet::new();
    for frame in 1..=1200 {
        let s = f64::from(frame) / 120.0;
        engine.feed(|l| replay.feed_to(l, s));
        engine.frame(s, 0.0);
        for caused in engine.causes() {
            match caused.cause {
                eval::live::Cause::WeaponPulse {
                    s,
                    what: id @ (WeaponId::ChainLightning | WeaponId::FireTrail),
                    accent,
                    ..
                } => {
                    projected.insert((id, s.to_bits()));
                    if accent {
                        accented.insert(id);
                    }
                }
                eval::live::Cause::WeaponImpact {
                    what: id @ (WeaponId::ChainLightning | WeaponId::FireTrail),
                    ..
                } => {
                    impacts.insert(id);
                }
                eval::live::Cause::Weapon { ref what, .. } => assert!(
                    !matches!(
                        WeaponId::from_name(what),
                        Some(WeaponId::ChainLightning | WeaponId::FireTrail)
                    ),
                    "generic cue must be suppressed"
                ),
                _ => {}
            }
        }
        let journal = engine.journal.borrow();
        while index < journal.end() {
            let (_, record) = journal
                .get(index)
                .expect("collect every frame before pruning");
            match record {
                Record::Moment(Moment::WeaponFire(shot))
                    if matches!(shot.id, WeaponId::ChainLightning | WeaponId::FireTrail) =>
                {
                    *shots.entry(shot.id).or_default() += 1;
                }
                Record::Sound(eval::live::Caused {
                    cause:
                        eval::live::Cause::Weapon {
                            what,
                            caught: false,
                            ..
                        },
                    ..
                }) => {
                    if let Some(id @ (WeaponId::ChainLightning | WeaponId::FireTrail)) =
                        WeaponId::from_name(what)
                    {
                        *fires.entry(id).or_default() += 1;
                    }
                }
                _ => {}
            }
            index += 1;
        }
    }
    assert_eq!(
        shots, fires,
        "trail circles and chain jumps must not duplicate the shot's fire cue"
    );
    let view = engine.view.borrow();
    for id in [WeaponId::ChainLightning, WeaponId::FireTrail] {
        assert!(shots[&id] > 0);
        assert_eq!(
            projected.iter().filter(|(w, _)| *w == id).count(),
            shots[&id]
        );
        assert!(
            accented.contains(&id),
            "real captured accent reaches production pulse routing"
        );
        assert!(
            impacts.contains(&id),
            "actual admitted impacts reach production audio"
        );
        assert!(
            view.weapons.contains(&(id.as_str().into(), false)),
            "inventory projects its actual existing recipe"
        );
        let row = view
            .recap
            .rows(view.run.unwrap().elapsed)
            .into_iter()
            .find(|r| r.id == id)
            .unwrap();
        assert!(
            row.held_s > 0.0 && row.damage > 0.0 && row.dps.is_finite(),
            "actual delivery feeds recap: {row:?}"
        );
        eprintln!(
            "restored recap {row:?}; {} shots and matching fire cues",
            shots[&id]
        );
    }
}

#[test]
fn authoritative_fire_path_replays_late_movement_and_holds_through_a_menu() {
    let lib = library();
    let file = StreamFile::parse(
        &std::fs::read_to_string(root().join("public/reactive/house-128/stream.json")).unwrap(),
    )
    .unwrap();
    let run = |late: f64| {
        let mut e =
            eval::scale::restored_density_engine(&source("levels/survivors.yaml"), &lib).unwrap();
        let mut replay = Replay::new(file.log.clone());
        let keys = [
            (0.01, InputKey::Right, true),
            (0.21, InputKey::Right, false),
            (0.22, InputKey::Up, true),
            (0.6, InputKey::Pause, true),
            (0.61, InputKey::Pause, false),
            (1.0, InputKey::Pause, true),
            (1.01, InputKey::Pause, false),
        ];
        let mut next = 0;
        let mut held = None;
        for i in 1..=240 {
            let now = f64::from(i) / 120.0;
            e.feed(|l| replay.feed_to(l, now));
            while next < keys.len() && keys[next].0 + late <= now {
                let (at, key, down) = keys[next];
                e.key(at, key, down);
                next += 1;
            }
            e.frame(now, 0.23);
            if now > 0.85 && now < 0.95 {
                let v = e.view.borrow();
                if let Some(ref path) = held {
                    assert_eq!(path, &v.hero_trail);
                }
                held = Some(v.hero_trail.clone());
            }
        }
        e.frame(2.25, 0.0);
        e
    };
    let a = run(0.0);
    let b = run(0.025);
    assert!(b.stats.key_rollbacks > 0);
    let a = a.view.borrow();
    let b = b.view.borrow();
    assert!(a.hero_trail.len() > 100);
    assert_eq!(
        a.hero_trail, b.hero_trail,
        "rendered past poses must be corrected just like actual trail damage after late keys"
    );
    assert!(a.hero_trail.windows(2).all(|v| v[0].0 < v[1].0));
}

#[test]
fn zero_xp_kills_keep_real_impact_death_and_kill_audio_without_fake_gems() {
    let (mut lib, log) = fast_offer_fixture();
    lib.enemies
        .get_mut("vs-bat3")
        .unwrap()
        .survivors
        .as_mut()
        .unwrap()
        .xp = 0.0;
    let mut e = Engine::new(
        &source("levels/survivors.yaml"),
        &lib,
        &IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    e.settled_log = Some(Vec::new());
    e.feed(|l| *l = log);
    for i in 1..=600 {
        e.frame(f64::from(i) / 120.0, 0.2);
    }
    e.frame(5.5, 0.0);
    let r = e.settled_log.as_ref().unwrap();
    assert!(r.iter().any(|(_, r)| matches!(
        r,
        Record::Death {
            recycled: false,
            ..
        }
    )));
    assert!(
        r.iter()
            .any(|(_, r)| matches!(r, Record::Moment(Moment::EnemyImpact { .. })))
    );
    assert!(r.iter().any(|(_, r)| matches!(
        r,
        Record::Sound(eval::live::Caused {
            cause: eval::live::Cause::Kill { .. },
            ..
        })
    )));
    assert!(!r.iter().any(|(_, r)| matches!(r, Record::Gem { .. })));
    assert_eq!(e.view.borrow().xp, 0.0);
}
