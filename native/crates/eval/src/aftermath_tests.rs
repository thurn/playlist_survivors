use crate::{
    live::{AFTERMATH_CAP, Moment, Record},
    session::{Arrival, rebase_append},
};
#[test]
fn real_combat_aftermath_rebuilds_identically_with_late_movement() {
    #[derive(serde::Deserialize)]
    struct Fixture {
        arrivals: Vec<Arrival>,
    }
    let fixture: Fixture =
        serde_json::from_str(include_str!("../../../fixtures/survivors-3m.json")).unwrap();
    let (level, lib) = crate::musical_tests::content();
    let make = || crate::scale::evolved_combat_engine(&level, &lib).unwrap();
    let mut a = make();
    let mut b = make();
    let mut next = 0;
    for i in 1..=600 {
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
        if i % 20 == 0 && i < 600 {
            a.key(now, sim::movement::InputKey::Right, (i / 20) % 2 == 0);
        }
        if i > 3 && (i - 3) % 20 == 0 {
            b.key(
                f64::from(i - 3) * 0.01,
                sim::movement::InputKey::Right,
                ((i - 3) / 20) % 2 == 0,
            );
        }
        a.frame(now, 0.237);
        b.frame(now, 0.237);
        a.view.borrow_mut().take_moments();
        b.view.borrow_mut().take_moments();
    }
    a.frame(6.1, 0.237);
    b.frame(6.1, 0.237);
    assert_eq!(a.settled_hash, b.settled_hash);
    assert!(b.stats.key_rollbacks > 20);
    let v = a.view.borrow();
    assert!(v.kills > 0 && !v.aftermath.is_empty());
    assert!(v.aftermath.len() <= AFTERMATH_CAP);
    assert_eq!(v.aftermath, b.view.borrow().aftermath);
    eprintln!(
        "actual6s Rust fixture + injected evolved300-foe40HP/zeroXP combat: {} kills,{} causal marks,{} late-key rollbacks lead237ms",
        v.kills,
        v.aftermath.len(),
        b.stats.key_rollbacks
    );
}
fn lethal(id: usize, x: f64) -> Record {
    Record::Moment(Moment::EnemyImpact {
        hit: Box::new(Moment::Hit {
            x,
            y: 0.0,
            lethal: true,
            damage: 1.0,
            song: 1.0,
            critical: false,
            foe: id,
            source: "move",
            weight: "light",
            what: "sword".into(),
            dir: 0.0,
            intensity: 1.0,
        }),
    })
}
#[test]
fn marks_are_bounded_expire_in_world_time_and_do_not_survive_a_discarded_death() {
    let (level, lib) = crate::musical_tests::content();
    let mut v = crate::scale::evolved_combat_engine(&level, &lib)
        .unwrap()
        .view
        .borrow()
        .clone();
    assert!(!v.aftermath_kinds.contains("bat"));

    v.apply(
        1.0,
        &Record::Run(sim::survivors::RunState {
            world: 1.0,
            ..Default::default()
        }),
    );
    // A flyer has no catalog decal; it cannot reserve a cell or evict a real mark.
    v.apply(
        1.0,
        &Record::Spawn {
            id: 9999,
            kind: "bat".into(),
        },
    );
    v.apply(1.0, &lethal(9999, 0.0));
    assert!(v.aftermath.is_empty());
    let prefix = v.clone();
    for id in 0..300 {
        v.apply(
            1.0,
            &Record::Spawn {
                id,
                kind: "vs-skeleton".into(),
            },
        );
        v.apply(
            1.0,
            &Record::Sample {
                id,
                x: id as f64 * 48.0,
                y: 0.0,
            },
        );
        v.apply(
            1.0,
            &Record::Death {
                id,
                w: 1.0,
                recycled: false,
            },
        );
        v.apply(1.0, &lethal(id, id as f64 * 48.0));
    }
    assert_eq!(v.aftermath.len(), 96);
    let killed = v.clone();
    v = prefix; // The view's journal overlay discards speculative death records.
    assert!(v.aftermath.is_empty());
    v = killed;
    v.apply(
        99.0,
        &Record::Run(sim::survivors::RunState {
            world: 1.0,
            frozen: Some(sim::survivors::FreezeReason::Paused),
            ..Default::default()
        }),
    );
    assert_eq!(
        v.aftermath.len(),
        96,
        "song time cannot spend marks during a menu"
    );
    v.apply(
        121.0,
        &Record::Run(sim::survivors::RunState {
            world: 22.0,
            ..Default::default()
        }),
    );
    assert!(v.aftermath.is_empty());
}
