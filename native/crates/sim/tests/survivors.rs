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
    let mut level = load_level(&src, lib, &reactive_song_map(f64::INFINITY), None).unwrap();
    // These tests isolate clock/freeze/finale behavior. Combat XP must not open an
    // unrelated offer when weapon reach or enemy movement changes.
    for row in &mut level.survivor_schedule {
        row.minimum = Some(0);
        row.enemies = Some(Vec::new());
        row.bosses.clear();
        row.events.clear();
        row.treasure.clear();
    }
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
    assert!(sim.run_s() < until - 1.0);
    assert_eq!(
        sim.run_state().frozen,
        Some(sim::survivors::FreezeReason::MusicStopped)
    );
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
fn survivors_does_not_read_uncommitted_victory_beats_in_a_free_passage() {
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
    let mut prefix = log.clone();
    prefix.beats.truncate(1);
    let (ra, mut a) = start(&log, 2.2);
    let (rb, mut b) = start(&prefix, 2.2);
    for sim in [&mut a, &mut b] {
        sim.advance_to(2.1);
        assert!(
            sim.end_s().is_infinite(),
            "no finale before the run deadline"
        );
        sim.advance_to(9.9);
        assert!(sim.done());
        assert!((sim.end_s() - 5.45).abs() < 1e-8);
    }
    assert_eq!(common::recording(&ra), common::recording(&rb));
}

fn steady_log(seconds: u32) -> StreamLog {
    StreamLog {
        beats: (0..=seconds * 2)
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
        ticks: (0..=seconds * 20)
            .map(|i| {
                let t = f64::from(i) / 20.0;
                stream::Tick {
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
    }
}

fn key(sim: &mut Sim, s: f64, key: sim::movement::InputKey, pressed: bool) {
    sim.key(sim::player::KeyEvent {
        s,
        key,
        kind: if pressed {
            sim::player::KeyEventKind::Press
        } else {
            sim::player::KeyEventKind::Release
        },
        id: None,
    });
}

#[test]
fn a_pause_holds_every_world_quantity_and_held_movement_requires_release() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let log = steady_log(8);
    let (rec, mut sim) = start(&log, 30.0);
    key(&mut sim, 0.1, K::Right, true);
    key(&mut sim, 1.1, K::Pause, true);
    key(&mut sim, 1.11, K::Pause, false);
    key(&mut sim, 3.1, K::Pause, true);
    key(&mut sim, 3.11, K::Pause, false);
    sim.advance_to(1.2);
    let stopped = sim.observe();
    let world = sim.w();
    let count = rec.borrow().enemies.len();
    let hits = rec.borrow().hits.len();
    sim.advance_to(3.2);
    let held = sim.observe();
    assert!((sim.run_s() - 1.1).abs() < 1e-9);
    assert_eq!(sim.w(), world);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::Paused));
    assert_eq!(stopped.hero, held.hero);
    assert_eq!(stopped.enemies, held.enemies);
    assert_eq!(rec.borrow().enemies.len(), count);
    assert_eq!(rec.borrow().hits.len(), hits);
    sim.advance_to(4.0);
    assert!(sim.run_state().frozen.is_none());
    let at_resume = sim.observe().hero;
    sim.advance_to(4.5);
    assert_eq!(
        (sim.observe().hero.x, sim.observe().hero.y),
        (at_resume.x, at_resume.y)
    );
    key(&mut sim, 4.6, K::Right, false);
    key(&mut sim, 4.7, K::Right, true);
    sim.advance_to(5.3);
    assert!(sim.observe().hero.x > at_resume.x);
}

#[test]
fn keys_held_when_cards_open_never_pick_until_released_and_pressed_again() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let (_, mut sim) = start(&steady_log(6), 30.0);
    key(&mut sim, 0.5, K::Confirm, true);
    sim.freeze_at(1.0, FreezeReason::LevelUp);
    key(&mut sim, 1.2, K::Up, true);
    key(&mut sim, 1.3, K::Up, false);
    key(&mut sim, 1.5, K::Confirm, true);
    sim.advance_to(1.8);
    assert_eq!(sim.run_state().selected, 1);
    assert!(!sim.run_state().confirmed);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::LevelUp));
    key(&mut sim, 2.0, K::Confirm, false);
    key(&mut sim, 2.1, K::Confirm, true);
    key(&mut sim, 2.2, K::Confirm, false);
    sim.advance_to(2.8);
    assert!(sim.run_state().confirmed);
    assert!(sim.run_state().frozen.is_none());
    assert_eq!(sim.observe().hero.x, 0.0);
}

#[test]
fn frozen_streaming_and_batch_runs_record_identically() {
    use sim::movement::InputKey as K;
    let log = steady_log(9);
    let (ra, mut a) = start(&log, 30.0);
    let (rb, mut b) = start(&common::known_by(&log, 0.0), 30.0);
    for sim in [&mut a, &mut b] {
        for (s, k, down) in [
            (0.1, K::Right, true),
            (1.1, K::Pause, true),
            (1.2, K::Pause, false),
            (3.1, K::Pause, true),
            (3.2, K::Pause, false),
            (4.0, K::Right, false),
            (4.1, K::Left, true),
            (5.1, K::Left, false),
        ] {
            key(sim, s, k, down);
        }
    }
    a.advance_to(8.0);
    for i in 1..=160 {
        let t = f64::from(i) * 0.05;
        *b.log_mut() = common::known_by(&log, t);
        b.advance_to(t);
    }
    assert_eq!(common::recording(&ra), common::recording(&rb));
    assert_eq!(a.run_state(), b.run_state());
}

#[test]
fn silence_freezes_at_known_evidence_and_relocks_on_the_second_following_bar() {
    use sim::survivors::FreezeReason;
    let mut log = steady_log(18);
    log.beats.retain(|b| b.t <= 2.0 || b.t >= 11.0);
    for tick in &mut log.ticks {
        if (2.0..10.0).contains(&tick.t) {
            tick.evidence = 0.0;
            tick.energy = 0.0;
        }
    }
    let (rec, mut sim) = start(&log, 30.0);
    sim.advance_to(7.3);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::MusicStopped));
    let elapsed = sim.run_s();
    sim.advance_to(10.5);
    assert_eq!(sim.run_s(), elapsed);
    sim.advance_to(12.4);
    assert_eq!(sim.run_s(), elapsed);
    assert!((elapsed - 7.25).abs() < 1e-9);
    sim.advance_to(12.601);
    assert!(sim.run_state().frozen.is_none());
    let transitions: Vec<_> = rec
        .borrow()
        .run_states
        .windows(2)
        .filter(|w| w[0].frozen != w[1].frozen)
        .map(|w| w[1])
        .collect();
    assert_eq!(transitions.last().unwrap().song, 12.5);
    assert!(
        (sim.run_s() - 7.351).abs() < 1e-9,
        "transitions {transitions:?}"
    );
    eprintln!(
        "silence freeze run clock={elapsed}; resumed at song time 12.5, run clock={}",
        sim.run_s()
    );
}

#[test]
fn ending_a_manual_pause_does_not_dismiss_a_chest_freeze() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let (_, mut sim) = start(&steady_log(7), 30.0);
    sim.freeze_at(1.0, FreezeReason::Paused);
    sim.freeze_at(1.2, FreezeReason::Chest);
    key(&mut sim, 2.1, K::Pause, true);
    key(&mut sim, 2.2, K::Pause, false);
    sim.advance_to(2.3);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::Chest));
    key(&mut sim, 2.4, K::Confirm, true);
    key(&mut sim, 2.45, K::Confirm, false);
    sim.advance_to(3.1);
    assert!(sim.run_state().frozen.is_none());
}

#[test]
fn no_committed_beat_means_held_strides_on_the_free_pulse() {
    use sim::movement::InputKey as K;
    let log = StreamLog::default();
    let (_, mut sim) = start(&log, 30.0);
    key(&mut sim, 0.1, K::Right, true);
    sim.advance_to(0.3);
    assert_eq!(sim.observe().hero.x, 0.0);
    sim.advance_to(0.5);
    assert!(sim.observe().hero.x > 0.0);
    let x = sim.observe().hero.x;
    sim.advance_to(0.6);
    assert_eq!(sim.observe().hero.x, x);
    sim.advance_to(0.85);
    assert!(sim.observe().hero.x > x);
}

#[test]
fn a_chained_card_clears_the_previous_confirm_and_blocks_its_held_key() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let (_, mut sim) = start(&steady_log(6), 30.0);
    sim.freeze_at(1.0, FreezeReason::LevelUp);
    key(&mut sim, 1.2, K::Confirm, true);
    sim.freeze_at(1.3, FreezeReason::LevelUp);
    sim.advance_to(1.8);
    assert!(!sim.run_state().confirmed);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::LevelUp));
    key(&mut sim, 2.0, K::Confirm, false);
    key(&mut sim, 2.1, K::Confirm, true);
    sim.advance_to(2.8);
    assert!(sim.run_state().confirmed);
    assert!(sim.run_state().frozen.is_none());
}

#[test]
fn freezing_a_planned_victory_replans_it_once_without_duplicate_landmarks() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let (rec, mut sim) = start(&steady_log(12), 2.2);
    sim.freeze_at(2.9, FreezeReason::Paused);
    key(&mut sim, 5.1, K::Pause, true);
    key(&mut sim, 5.2, K::Pause, false);
    sim.advance_to(10.1);
    assert!(sim.done());
    assert_eq!(sim.end_s(), 9.0);
    assert_eq!(rec.borrow().landmarks.len(), 1);
}

#[test]
fn section_changes_learned_during_pause_never_rewind_the_clock() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let mut log = steady_log(12);
    for beat in &mut log.beats {
        beat.known_at = (beat.t - 0.85).max(0.0);
    }
    log.changes = Some(vec![stream::Change {
        t: 2.0,
        known_at: 2.0,
        strength: 1.0,
    }]);
    log.jumps = Some(Vec::new());
    let (rec, mut sim) = start(&log, 1800.0);
    sim.freeze_at(1.1, FreezeReason::Paused);
    key(&mut sim, 5.1, K::Pause, true);
    key(&mut sim, 5.2, K::Pause, false);
    sim.advance_to(9.0);
    let rec = rec.borrow();
    assert!(
        rec.run_states.windows(2).all(|w| w[1].song >= w[0].song),
        "backwards {:?}",
        rec.run_states
            .windows(2)
            .filter(|w| w[1].song < w[0].song)
            .collect::<Vec<_>>()
    );
    assert_eq!(rec.zone_waves.len(), 1);
    assert!(rec.zone_waves[0].s >= 5.5);
}

#[test]
fn a_long_pause_preserves_the_zone_fallbacks_remaining_time() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    for (pause, resume, fallback) in [(1.1, 70.1, 309.5), (59.3, 70.1, 251.5)] {
        let log = steady_log(330);
        let (rec, mut sim) = start(&log, 1800.0);
        sim.freeze_at(pause, FreezeReason::Paused);
        key(&mut sim, resume, K::Pause, true);
        key(&mut sim, resume + 0.1, K::Pause, false);
        sim.advance_to(fallback - 0.1);
        assert!(rec.borrow().zone_waves.is_empty());
        sim.advance_to(fallback + 0.1);
        let rec = rec.borrow();
        assert!(rec.run_states.windows(2).all(|w| w[1].song >= w[0].song));
        assert_eq!(rec.zone_waves.len(), 1);
        assert!((rec.zone_waves[0].s - fallback).abs() < 1e-9);
    }
}

#[test]
fn a_second_silent_gap_inside_a_pause_cannot_resume_the_world() {
    use sim::{movement::InputKey as K, survivors::FreezeReason};
    let mut log = steady_log(28);
    for tick in &mut log.ticks {
        if (2.0..8.0).contains(&tick.t) || (13.0..20.0).contains(&tick.t) {
            tick.evidence = 0.0;
            tick.energy = 0.0;
        }
    }
    let (rec, mut sim) = start(&log, 1800.0);
    sim.freeze_at(1.1, FreezeReason::Paused);
    key(&mut sim, 18.1, K::Pause, true);
    key(&mut sim, 18.2, K::Pause, false);
    sim.advance_to(19.5);
    assert_eq!(sim.run_state().frozen, Some(FreezeReason::MusicStopped));
    assert!((sim.run_s() - 1.1).abs() < 1e-9);
    assert!(
        rec.borrow()
            .run_states
            .iter()
            .filter(|r| r.song > 1.1 && r.song < 19.5)
            .all(|r| r.frozen.is_some())
    );
    sim.advance_to(23.0);
    assert!(sim.run_state().frozen.is_none());
}

#[test]
fn victory_holds_the_hero_when_the_last_beat_ends_and_free_pulses_continue() {
    use sim::movement::InputKey as K;
    let mut log = steady_log(8);
    log.beats.retain(|b| b.t <= 3.0);
    let (_, mut sim) = start(&log, 2.2);
    key(&mut sim, 0.1, K::Right, true);
    sim.advance_to(3.1);
    let pose = sim.observe().hero;
    sim.advance_to(4.5);
    let after = sim.observe().hero;
    assert_eq!(
        (after.x, after.y, after.stepping),
        (pose.x, pose.y, pose.stepping)
    );
    assert_eq!(sim.end_s(), 6.0);
}

#[test]
fn audible_weak_beat_evidence_does_not_mean_music_stopped() {
    let mut log = steady_log(12);
    for tick in &mut log.ticks {
        tick.evidence = 0.0;
    }
    let (_, mut sim) = start(&log, 30.0);
    sim.disable_hero_damage_for_check();
    sim.advance_to(8.0);
    assert_eq!(sim.run_state().frozen, None);
    assert!(sim.run_s() > 7.5, "run {}", sim.run_s());
}

#[test]
fn audible_evidence_dips_do_not_keep_a_silent_gap_frozen() {
    let mut log = steady_log(18);
    for (i, tick) in log.ticks.iter_mut().enumerate() {
        if (2.0..8.0).contains(&tick.t) {
            tick.energy = 0.0;
            tick.evidence = 0.0;
        } else if tick.t >= 8.0 {
            tick.evidence = if i % 2 == 0 { 0.9 } else { 0.0 };
        }
    }
    let (_, mut sim) = start(&log, 30.0);
    sim.advance_to(7.8);
    assert_eq!(
        sim.run_state().frozen,
        Some(sim::survivors::FreezeReason::MusicStopped)
    );
    let held = sim.run_s();
    sim.advance_to(12.0);
    assert_eq!(sim.run_state().frozen, None);
    assert!(sim.run_s() > held + 1.0);
}

#[test]
fn audible_beatless_victory_uses_free_pulses_and_ignores_uncommitted_beats() {
    for n in [0, 1, 3] {
        let mut log = steady_log(12);
        log.beats.truncate(n);
        let mut poisoned = log.clone();
        poisoned.beats.push(Beat {
            t: 1.2,
            known_at: 100.0,
            period: 0.1,
            evidence: 1.0,
        });
        let (ra, mut a) = start(&log, 2.2);
        let (rb, mut b) = start(&poisoned, 2.2);
        for frame in 1..=72 {
            a.advance_to(f64::from(frame) * 0.1);
        }
        b.advance_to(7.2);
        assert!(a.done() && b.done(), "{n} beats: {} / {}", a.s(), b.s());
        assert!(
            (a.end_s() - if n == 3 { 5.4 } else { 5.45 }).abs() < 1e-8,
            "{n} beats: {}",
            a.end_s()
        );
        assert_eq!(a.run_s(), 2.2);
        assert_eq!(common::recording(&ra), common::recording(&rb));
        assert!(ra.borrow().hits.iter().any(|h| h["what"] == "finale-blast"));
    }
}

#[test]
fn four_minute_fallback_tours_five_zones_and_victory_always_returns_to_infernal() {
    for grid in [true, false] {
        let mut log = steady_log(1210);
        if !grid {
            log.beats.clear();
        }
        let (rec, mut sim) = start(&log, 1201.0);
        sim.advance_to(1199.9);
        let expected = [
            "galop-woods",
            "mushroom-grove",
            "ember-hollow",
            "infernal-grove",
        ];
        {
            let rec = rec.borrow();
            assert_eq!(rec.zone_waves.len(), 4);
            for (i, zone) in expected.into_iter().enumerate() {
                assert_eq!(rec.zone_waves[i].zone, zone);
                let previous = if i == 0 { 0.0 } else { rec.zone_waves[i - 1].s };
                let gap = rec.zone_waves[i].s - previous;
                let pulse = if grid {
                    0.5
                } else {
                    common::library().hero.stride.free_period_s
                };
                assert!(gap >= 240.0 - 1e-7 && gap <= 240.0 + pulse + 1e-7);
            }
        }
        // The fifth fallback cycles away immediately before victory; the finale must restore infernal.
        sim.advance_to(1205.0);
        let rec = rec.borrow();
        assert!(
            rec.zone_waves
                .iter()
                .any(|z| z.s >= 1200.0 && z.zone == "twilight-glade")
        );
        assert_eq!(rec.zone_waves.last().unwrap().zone, "infernal-grove");
        println!(
            "SP3 grid{grid}: {:?}",
            rec.zone_waves
                .iter()
                .map(|z| (&z.zone, z.s))
                .collect::<Vec<_>>()
        );
    }
}
