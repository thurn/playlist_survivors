//! Actual Rust timestamp fixture through the app's rollbackable view projection.
use crate::{
    live::{Engine, Record},
    session::{Arrival, rebase_append},
};
#[test]
fn real_music_signature_replays_late_keys_and_publishes_landmark_and_slams() {
    #[derive(serde::Deserialize)]
    struct Fixture {
        arrivals: Vec<Arrival>,
    }
    let fixture: Fixture =
        serde_json::from_str(include_str!("../../../fixtures/survivors-3m.json")).unwrap();
    let (mut level, lib) = crate::musical_tests::content();
    level.text = level.text.replace("afterS: 10000", "afterS: 90");
    let make = || {
        Engine::new_with_setup(
            &level,
            &lib,
            &indexmap::IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
            |s| {
                s.disable_hero_damage_for_check();
                s.apply_survivor_stats(sim::survivors::StatDelta {
                    might: -1.0,
                    area: -1.0,
                    ..Default::default()
                });
            },
        )
        .unwrap()
    };
    let mut a = make();
    let mut b = make();
    a.settled_log = Some(Vec::new());
    let mut next = 0;
    for i in 1..=15000 {
        let now = f64::from(i) * 0.01;
        let from = next;
        while next < fixture.arrivals.len() && fixture.arrivals[next].arrived <= now {
            next += 1;
        }
        if next > from {
            for e in [&mut a, &mut b] {
                e.feed(|log| {
                    for entry in &fixture.arrivals[from..next] {
                        rebase_append(log, &entry.entry, 0.0);
                    }
                });
            }
        }
        if i % 1000 == 0 && i < 15000 {
            a.key(now, sim::movement::InputKey::Space, (i / 1000) % 2 == 0);
        }
        if i > 3 && (i - 3) % 1000 == 0 {
            b.key(
                f64::from(i - 3) * 0.01,
                sim::movement::InputKey::Space,
                ((i - 3) / 1000) % 2 == 0,
            );
        }
        a.frame(now, 0.237);
        b.frame(now, 0.237);
        a.view.borrow_mut().take_moments();
        b.view.borrow_mut().take_moments();
    }
    a.frame(150.1, 0.237);
    b.frame(150.1, 0.237);
    assert!(b.stats.key_rollbacks >= 14);
    assert_eq!(a.settled_hash, b.settled_hash);
    let log = a.settled_log.as_ref().unwrap();
    assert!(log.iter().any(|(_, r)| matches!(r, Record::Signature(..))));
    assert_eq!(
        log.iter()
            .filter(|(_, r)| matches!(r,Record::Landmark(l) if l.id=="standing-stones"))
            .count(),
        1
    );
    let slams = log
        .iter()
        .filter(|(_, r)| matches!(r, Record::Slam(_)))
        .count();
    assert!(slams > 0);
    assert_eq!(a.view.borrow().landmarks, b.view.borrow().landmarks);
    eprintln!(
        "real Rust3m fixture,150s, late30ms lead237ms: {} slams,{} key rollbacks, settled hash{:x}",
        slams, b.stats.key_rollbacks, b.settled_hash
    );
}

#[test]
fn displayed_descent_phase_is_continuous_across_pause_and_replanned_landing() {
    let (mut level, lib) = crate::musical_tests::content();
    level.text = level.text.replace("afterS: 10000", "afterS: 90");
    let mut e = Engine::new(
        &level,
        &lib,
        &indexmap::IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap();
    let log = stream::StreamLog {
        beats: (0..220)
            .map(|i| {
                let t = f64::from(i) * 0.5;
                stream::Beat {
                    t,
                    known_at: (t - 0.5).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..2200)
            .map(|i| {
                let t = f64::from(i) * 0.05;
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
        ..Default::default()
    };
    e.feed(|l| *l = log);
    e.frame(89.65, 0.237);
    e.key(89.7, sim::movement::InputKey::Pause, true);
    e.key(89.71, sim::movement::InputKey::Pause, false);
    e.frame(89.8, 0.237);
    let phase = |v: &crate::live::LiveView| {
        let (_, from, to, _) = v.signature.unwrap();
        let s = v.run.unwrap().presentation_s;
        (s - from) / (to - from)
    };
    let held = phase(&e.view.borrow());
    assert!(held > 0.2 && held < 0.8);
    e.frame(95.0, 0.237);
    assert!((phase(&e.view.borrow()) - held).abs() < 1e-8);
    e.key(95.1, sim::movement::InputKey::Pause, true);
    e.key(95.11, sim::movement::InputKey::Pause, false);
    e.frame(95.51, 0.237);
    assert!(e.view.borrow().run.unwrap().frozen.is_none());
    let resumed = phase(&e.view.borrow());
    assert!(
        (resumed - held).abs() < 0.05,
        "descent jumped from{held} to{resumed}"
    );
}
