//! Actual authored roles and geometry pass through the ordinary main-beat engine.
use super::*;
use crate::engine::{Member, musical::Kind};
use crate::survivors::{FreezeReason, ScheduledBoss, StatDelta};

fn base(bpm: f64) -> (Rc<std::cell::RefCell<crate::recorder::LogRecorder>>, Sim) {
    sword_measurement_base(
        bpm,
        1,
        StatDelta {
            might: -1.0,
            area: -1.0,
            ..Default::default()
        },
        false,
    )
}

#[test]
fn restored_shapes_step_only_on_main_beats_at_three_tempos_and_leave_no_actor_debt() {
    for bpm in [70.0, 128.0, 174.0] {
        let period = 60.0 / bpm;
        for kind in [Kind::Stampede, Kind::Kick, Kind::Wedge, Kind::Ring] {
            let (rec, mut s) = base(bpm);
            s.start_musical(kind, 0);
            let count = s.st.alive.len();
            assert_eq!(
                count,
                match kind {
                    Kind::Stampede | Kind::Ring => 40,
                    Kind::Kick => 12,
                    Kind::Wedge => 9,
                }
            );
            let mut moved = 0;
            for i in 1..=(8.0 * period * 240.0) as usize {
                let time = i as f64 / 240.0;
                let before: Vec<_> =
                    s.st.alive
                        .iter()
                        .map(|&id| (id, s.pos(id, s.st.w)))
                        .collect();
                let previous = s.s();
                s.advance_to(time);
                for (id, p) in before {
                    if !s.st.foes.get(id).is_some_and(|f| f.alive) {
                        continue;
                    }
                    let q = s.pos(id, s.st.w);
                    if (q.0 - p.0).hypot(q.1 - p.1) > 1e-7 {
                        moved += 1;
                        let window = period.mul_add(0.2, 0.0).min(0.1);
                        assert!(
                            s.log
                                .beats
                                .iter()
                                .any(|b| previous <= b.t + 1e-9 && s.s() >= b.t - window - 1e-9),
                            "{bpm} {kind:?}: root glided at {}",
                            s.s()
                        );
                    }
                }
                assert!(s.st.alive.len() <= 40);
            }
            assert!(moved > 0, "{bpm} {kind:?} never moved");
            s.advance_to(55.0);
            assert!(
                s.st.alive.is_empty(),
                "group expiry/crossing must not recycle extra actors"
            );
            assert_eq!(rec.borrow().formations.len(), 1);
            assert!(s.st.n_gems == 0, "retiring actors grants no fake XP");
        }
    }
}

#[test]
fn optional_shapes_share_the_cap_and_cannot_starve_a_scheduled_boss() {
    let (_, mut s) = base(128.0);
    let def = s.env.input.lib.enemies.get_index_of("vs-bat3").unwrap();
    for _ in 0..300 {
        s.spawn_foe(def, 10000.0, 0.0, Some(1e9));
    }
    s.start_musical(Kind::Stampede, 0);
    assert_eq!(s.st.alive.len(), 300);
    assert_eq!(
        s.st.alive
            .iter()
            .filter(|&&i| matches!(s.st.foes[i].member, Some(Member::Musical(_))))
            .count(),
        40
    );
    s.st.spawner.waiting_bosses.push(ScheduledBoss {
        role: "vs-bat4".into(),
        minute: 1,
    });
    s.tick_spawner();
    assert!(s.st.spawner.waiting_bosses.is_empty());
    assert!(s.st.alive.iter().any(|&i| s.st.foes[i].survivor_boss));
    assert_eq!(s.st.alive.len(), 300);
    // A fully reserved source formation must yield a slot too.
    let (_, mut s) = base(128.0);
    for _ in 0..300 {
        s.spawn_member(def, 10000.0, 0.0, Member::SurvivorEvent(0));
    }
    s.st.spawner.waiting_bosses.push(ScheduledBoss {
        role: "vs-bat4".into(),
        minute: 1,
    });
    s.tick_spawner();
    assert!(s.st.spawner.waiting_bosses.is_empty());
    assert_eq!(s.st.alive.len(), 300);
}

#[test]
fn an_existing_shape_holds_through_pause_and_replays_from_its_snapshot() {
    let (rec, mut s) = base(128.0);
    s.start_musical(Kind::Kick, 0);
    s.advance_to(1.0);
    s.enter_freeze(FreezeReason::Paused);
    let poses: Vec<_> = s.st.alive.iter().map(|&id| s.pos(id, s.st.w)).collect();
    let snap = s.snapshot();
    let prefix = rec.borrow().clone();
    let mut results = Vec::new();
    for _ in 0..2 {
        s.restore(&snap);
        *rec.borrow_mut() = prefix.clone();
        s.advance_to(4.0);
        assert_eq!(
            poses,
            s.st.alive
                .iter()
                .map(|&id| s.pos(id, s.st.w))
                .collect::<Vec<_>>()
        );
        key(&mut s, 4.01, KeyEventKind::Press, InputKey::Pause);
        key(&mut s, 4.02, KeyEventKind::Release, InputKey::Pause);
        s.advance_to(30.0);
        results.push(format!("{:?}", rec.borrow().formations));
        assert!(s.st.alive.is_empty());
    }
    assert_eq!(results[0], results[1]);
}

#[test]
fn pressure_uses_the_real_roster_and_known_loudness_without_an_additive_spawner() {
    let mut counts = Vec::new();
    for level in [stream::Level::Quiet, stream::Level::Loud] {
        let (_, mut s) = base(128.0);
        for tick in &mut s.log_mut().ticks {
            tick.level = Some(level);
        }
        s.advance_to(2.0);
        let full = load_level(
            &source("levels/survivors.yaml"),
            &s.env.input.lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        let mut row = full.survivor_schedule[0].clone();
        row.frequency = Some(1_000_000.0); // Isolate one real roster refill.
        Rc::get_mut(&mut s.env)
            .unwrap()
            .input
            .level
            .survivor_schedule = vec![row];
        s.tick_spawner();
        s.advance_to(2.5);
        counts.push(s.st.alive.len());
    }
    assert_eq!(
        counts,
        [9, 20],
        "source minute0 minimum15 is bounded by configured0.6/1.3 loudness scale"
    );
}

#[test]
fn unreliable_entrance_deadline_is_discarded_without_a_clock_loop_or_catchup() {
    let make = |drop: bool| {
        let (_, mut s) = base(128.0);
        extend_music(&mut s, stream::Level::Medium, drop);
        s
    };
    let mut control = make(false);
    control.advance_to(181.5);
    assert_eq!(
        control.st.alive.len(),
        12,
        "the real medium-beat path stages a kick line here"
    );
    let mut dropped = make(true);
    dropped.advance_to(183.0);
    assert!(
        dropped.st.alive.is_empty(),
        "a confidence drop discards that staged entrance"
    );
    assert!(dropped.s() >= 182.98);
}

fn extend_music(s: &mut Sim, level: stream::Level, drop: bool) {
    let period = 60.0 / 128.0;
    s.log_mut().beats = (0..850)
        .map(|i| {
            let t = f64::from(i) * period;
            Beat {
                t,
                known_at: (t - period).max(0.0),
                period,
                evidence: 1.0,
            }
        })
        .collect();
    s.log_mut().ticks = (0..8000)
        .map(|i| {
            let t = f64::from(i) * 0.05;
            Tick {
                t,
                known_at: t,
                energy: 0.5,
                evidence: if drop && (181.1..183.0).contains(&t) {
                    0.0
                } else {
                    1.0
                },
                level: Some(level),
                key: None,
                bands: None,
                brightness: None,
            }
        })
        .collect();
}

#[test]
fn causal_music_selects_all_four_shapes_and_unknown_jump_cannot_enter_early() {
    for (level, until, role) in [
        (stream::Level::Quiet, 249.0, "bee-wedge"),
        (stream::Level::Medium, 182.0, "skeleton-kick-line"),
        (stream::Level::Loud, 302.0, "ring"),
    ] {
        let (rec, mut s) = base(128.0);
        extend_music(&mut s, level, false);
        s.advance_to(until);
        assert!(!s.st.alive.is_empty(), "{role}: {:?}", s.st.musical);
        s.retire_musical();
        assert!(rec.borrow().formations.iter().any(|f| f.def == role));
    }
    let (rec, mut s) = base(128.0);
    extend_music(&mut s, stream::Level::Quiet, false);
    s.log_mut().jumps = Some(vec![stream::Jump {
        t: 120.0,
        known_at: 121.0,
        db: 9.0,
    }]);
    s.advance_to(120.9);
    assert!(
        s.st.alive.is_empty(),
        "unknown jumps cannot select an encounter"
    );
    s.advance_to(122.0);
    assert_eq!(s.st.alive.len(), 40);
    assert_eq!(rec.borrow().formations[0].def, "stampede");
    assert!(rec.borrow().formations[0].s_start >= 121.35);
}

#[test]
fn freeze_discards_a_pending_entrance_and_beatless_hold_never_replays_it() {
    let (_, mut s) = base(128.0);
    extend_music(&mut s, stream::Level::Medium, false);
    s.advance_to(181.0); // Kick staged at180.9375; entrance181.40625.
    assert!(format!("{:?}", s.st.musical).contains("pending: Some"));
    s.enter_freeze(FreezeReason::Paused);
    s.advance_to(184.0);
    key(&mut s, 184.01, KeyEventKind::Press, InputKey::Pause);
    key(&mut s, 184.02, KeyEventKind::Release, InputKey::Pause);
    s.advance_to(185.0);
    assert!(s.st.alive.is_empty());
    assert!(format!("{:?}", s.st.musical).contains("pending: None"));
}

#[test]
fn closed_goblin_ring_has_three_contact_safe_exits_with_extra_clearance() {
    let (_, mut s) = base(128.0);
    s.start_musical(Kind::Ring, 0);
    let mut angles: Vec<_> =
        s.st.alive
            .iter()
            .map(|&id| {
                let p = s.st.foes[id].ring_path.unwrap();
                assert_eq!(p.to, 120.0);
                p.sn.atan2(p.c).rem_euclid(std::f64::consts::TAU)
            })
            .collect();
    angles.sort_by(f64::total_cmp);
    let radius = s.st.foes[s.st.alive[0]].r;
    let clearance = super::super::HURTBOX_RADIUS
        + radius
        + super::super::CONTACT_GAP
        + super::super::BITE_SLACK;
    let exits = (0..angles.len())
        .filter(|&i| {
            let next = angles[(i + 1) % angles.len()]
                + if i + 1 == angles.len() {
                    std::f64::consts::TAU
                } else {
                    0.0
                };
            2.0 * 120.0 * ((next - angles[i]) * 0.5).sin() > 2.0 * clearance + 16.0
        })
        .count();
    assert_eq!(
        exits, 3,
        "actual goblin bite geometry must leave a comfortable gap"
    );
}

#[test]
fn released_kick_keeps_its_remaining_charge_across_a_long_pause() {
    let (_, mut s) = base(128.0);
    s.start_musical(Kind::Kick, 0);
    let id = s.st.alive[0];
    for i in 1..=3000 {
        s.advance_to(f64::from(i) * 0.01);
        if s.st.foes[id].charge_until > s.s() {
            break;
        }
    }
    let remaining = s.st.foes[id].charge_until - s.s();
    assert!(remaining > 0.0);
    s.enter_freeze(FreezeReason::Paused);
    let paused = s.s();
    s.advance_to(paused + 5.0);
    key(&mut s, paused + 5.01, KeyEventKind::Press, InputKey::Pause);
    key(
        &mut s,
        paused + 5.02,
        KeyEventKind::Release,
        InputKey::Pause,
    );
    s.advance_to(paused + 5.55);
    assert!(!s.st.freeze.frozen);
    assert!(
        s.st.foes[id].charge_until > s.s(),
        "menu must not spend the remaining charge"
    );
    assert!(s.st.foes[id].charge_until - s.s() <= remaining + 1e-8);
}
