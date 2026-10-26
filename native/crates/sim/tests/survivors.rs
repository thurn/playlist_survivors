//! Survivors' clock and finale use the run, not a song boundary.
#![allow(clippy::float_cmp)]
mod common;

use sim::{
    content::level::load_level,
    engine::{Sim, SimInput},
    recorder::LogRecorder,
    schedule::reactive_song_map,
};
use std::{cell::RefCell, rc::Rc};
use stream::{Beat, StreamLog};

fn start(log: &StreamLog, duration: f64) -> (Rc<RefCell<LogRecorder>>, Sim) {
    let mut src = common::source("levels/survivors.yaml");
    src.text = src
        .text
        .replace("durationS: 1800", &format!("durationS: {duration}"));
    let lib = common::library();
    let level = load_level(&src, lib, &reactive_song_map(f64::INFINITY), None).unwrap();
    assert!(level.upgrades.loadout.is_empty()); // The sword is intrinsic until SF5.
    assert!(level.upgrades.offer.is_empty());
    let mut input = SimInput::new(level, lib, &common::atlas(), 0.2, true);
    input.zen = true;
    let rec = Rc::new(RefCell::new(LogRecorder::default()));
    let sim = Sim::new(input, Rc::new(log.clone()), Box::new(Rc::clone(&rec)), None);
    (rec, sim)
}

#[test]
fn survivors_ignores_a_confirmed_song_end() {
    let file = common::stream_file("causality-mix");
    let log = common::with_silent_tail(file.log, 10.0);
    let (rec, mut sim) = start(&log, 1800.0);
    let until = file.audio.duration + 8.0;
    sim.advance_to(until);
    assert!(!sim.done());
    assert!(sim.end_s().is_infinite());
    assert!((sim.run_s() - until).abs() < 1e-8);
    assert!(rec.borrow().formations.is_empty());
    assert!(rec.borrow().boss.is_none());
}

#[test]
fn survivors_victory_snaps_to_a_known_downbeat_and_repeats_exactly() {
    let log = StreamLog {
        beats: (0..32)
            .map(|i| {
                let t = f64::from(i) * 0.5;
                Beat {
                    t,
                    known_at: (t - 0.2).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ..StreamLog::default()
    };
    let (ra, mut a) = start(&log, 2.2);
    let (rb, mut b) = start(&log, 2.2);
    // Keys after the victory blast cannot move the hero out of the final pose.
    let key = sim::player::KeyEvent {
        s: 3.5,
        key: sim::movement::InputKey::Right,
        kind: sim::player::KeyEventKind::Press,
        id: None,
    };
    a.key(key);
    b.key(key);
    for i in 1..=61 {
        a.advance_to(f64::from(i) * 0.1);
    }
    b.advance_to(6.1);
    // First bar downbeat after 2.2 is 3.0, then the three-second epilogue.
    assert_eq!(a.end_s(), 6.0);
    assert!(a.done());
    assert_eq!(a.run_s(), 2.2);
    assert!(ra.borrow().inputs.is_empty());
    assert_eq!(common::recording(&ra), common::recording(&rb));
}

#[test]
fn survivors_does_not_use_a_downbeat_before_its_known_at() {
    let log = StreamLog {
        beats: vec![
            Beat {
                t: 0.0,
                known_at: 0.0,
                period: 0.5,
                evidence: 1.0,
            },
            Beat {
                t: 10.5,
                known_at: 10.0,
                period: 0.5,
                evidence: 1.0,
            },
            Beat {
                t: 11.0,
                known_at: 10.0,
                period: 0.5,
                evidence: 1.0,
            },
        ],
        ..StreamLog::default()
    };
    let (_, mut sim) = start(&log, 2.2);
    sim.advance_to(9.9);
    assert!(sim.end_s().is_infinite());
    sim.advance_to(10.1);
    assert_eq!(sim.end_s(), 14.0);
}
