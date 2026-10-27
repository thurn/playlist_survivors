//! Restored choices use actual inventory deadlines, recorded paths and target identities.
use super::*;
use crate::survivors::{FreezeReason, PassiveId, StatDelta, Stats, WeaponId, weapon_catalog};

#[test]
fn chain_uses_real_three_jump_schedule_and_never_rehits_an_overlapping_target() {
    let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
    sim.learn_survivor_weapon(WeaponId::ChainLightning).unwrap();
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    for x in [220.0, 221.0, 310.0, 400.0] {
        sim.spawn_foe(def, x, 0.0, Some(1e9));
    }
    sim.advance_to(0.4);
    let r = rec.borrow();
    let hits: Vec<_> = r
        .hits
        .iter()
        .filter(|h| h["what"] == "chain-lightning")
        .collect();
    assert_eq!(hits.len(), 3);
    assert_eq!(
        r.weapon_shots
            .iter()
            .filter(|s| s.id == WeaponId::ChainLightning)
            .count(),
        1
    );
    for e in &r.enemies {
        let fates: Vec<_> = e
            .fates
            .iter()
            .filter(|f| hits.iter().any(|h| h["id"].as_f64() == Some(f.hit)))
            .collect();
        assert_eq!(
            fates.len(),
            1,
            "AoE neighbors must not become later jump victims"
        );
        assert!((fates[0].damage - 1.2).abs() < 1e-9);
    }
    assert_eq!(r.chains[0].len(), 4, "hero plus three actual chain points");
}

#[test]
fn fire_trail_hits_recorded_past_positions_once_per_pulse_and_grows_with_rank() {
    let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
    sim.learn_survivor_weapon(WeaponId::FireTrail).unwrap();
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    let target = sim.spawn_foe(def, 0.0, 0.0, Some(1e9));
    key(&mut sim, 0.01, KeyEventKind::Press, InputKey::Right);
    sim.advance_to(2.1);
    assert!(
        sim.hero_pose(sim.st.w).x > 80.0,
        "actual held movement makes a trail"
    );
    let r = rec.borrow();
    let hits: Vec<_> = r
        .hits
        .iter()
        .filter(|h| h["what"] == "fire-trail")
        .collect();
    let fates: Vec<_> = r.enemies[target]
        .fates
        .iter()
        .filter(|f| hits.iter().any(|h| h["id"].as_f64() == Some(f.hit)))
        .collect();
    assert!(!fates.is_empty());
    let mut pulses = std::collections::BTreeSet::new();
    for f in fates {
        let h = hits
            .iter()
            .find(|h| h["id"].as_f64() == Some(f.hit))
            .unwrap();
        assert!(pulses.insert((h["s"].as_f64().unwrap() * 1e6).round() as u64));
        assert!((f.damage - 0.4).abs() < 1e-9);
    }
    drop(r);
    for _ in 1..8 {
        sim.learn_survivor_weapon(WeaponId::FireTrail).unwrap();
    }
    sim.advance_to(3.1);
    let r = rec.borrow();
    let last = r
        .weapon_shots
        .iter()
        .rfind(|s| s.id == WeaponId::FireTrail)
        .unwrap();
    let stats = weapon_catalog().get(WeaponId::FireTrail).level(8);
    assert!((last.damage - stats.power).abs() < 1e-9);
    assert!((last.radius - 20.0 * stats.area).abs() < 1e-9);
    assert!((last.duration_s - stats.duration_ms / 1000.0).abs() < 1e-9);
}

#[test]
fn chain_pending_jumps_hold_through_pause_and_snapshot_replay() {
    let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
    sim.learn_survivor_weapon(WeaponId::ChainLightning).unwrap();
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    for x in [220.0, 310.0, 400.0] {
        sim.spawn_foe(def, x, 0.0, Some(1e9));
    }
    sim.advance_to(0.05);
    let snap = sim.snapshot();
    let recorded = rec.borrow().clone();
    let mut outputs = Vec::new();
    for _ in 0..2 {
        sim.restore(&snap);
        *rec.borrow_mut() = recorded.clone();
        let before = rec.borrow().hits.len();
        sim.enter_freeze(FreezeReason::Paused);
        sim.advance_to(1.1);
        assert_eq!(rec.borrow().hits.len(), before);
        key(&mut sim, 1.11, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 1.12, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(2.1);
        let r = rec.borrow();
        outputs.push((format!("{:?}", r.hits), format!("{:?}", r.chains)));
        assert!(
            r.hits
                .iter()
                .filter(|h| h["what"] == "chain-lightning")
                .count()
                >= 3
        );
    }
    assert_eq!(outputs[0], outputs[1]);
}

#[test]
fn restored_weapons_keep_working_without_beats_and_passives_change_real_stats() {
    let mut file = stream();
    file.log.beats.clear();
    let mut sim = survivor_sim_with(file);
    for id in [WeaponId::ChainLightning, WeaponId::FireTrail] {
        sim.learn_survivor_weapon(id).unwrap();
    }
    let rec = Rc::new(std::cell::RefCell::new(
        crate::recorder::LogRecorder::default(),
    ));
    sim.rec = Box::new(rec.clone());
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    sim.spawn_foe(def, 220.0, 0.0, Some(1e9));
    sim.spawn_foe(def, 310.0, 0.0, Some(1e9));
    sim.advance_to(2.1);
    assert!(
        rec.borrow()
            .weapon_shots
            .iter()
            .any(|s| s.id == WeaponId::ChainLightning)
    );
    assert!(
        rec.borrow()
            .weapon_shots
            .iter()
            .any(|s| s.id == WeaponId::FireTrail)
    );
    assert!(
        rec.borrow()
            .hits
            .iter()
            .any(|h| h["what"] == "chain-lightning")
    );
    let base = sim.pickup_radius();
    sim.learn_survivor_passive(PassiveId::Magnet).unwrap();
    assert!((sim.pickup_radius() - base * 1.3).abs() < 1e-9);
    sim.learn_survivor_passive(PassiveId::Armour).unwrap();
    let stats = sim.survivor_progress().unwrap().stats;
    assert_eq!(
        stats.contact_damage(10.0, 1.0),
        Stats::default().contact_damage(10.0, 1.0) - 1.0
    );
}

#[test]
fn a_chain_hit_catches_the_actual_sharp_step_pose_between_physics_samples() {
    use crate::core::geometry::Shape;
    use crate::engine::{FoeStep, combat::HitSpec};
    use crate::recorder::{HitSource, Weight};
    let (_, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
    let def = sim.env.input.lib.enemies.get_index_of("vs-bat1").unwrap();
    let id = sim.spawn_foe(def, 280.0, 0.0, Some(1e9));
    sim.st.foes[id].r = 10.0;
    // A 350 px fast-bat beat step: the event is 4 ms past its retained physics sample.
    sim.st.foes[id].step = Some(FoeStep {
        s0: 0.0,
        peak_s: 0.1,
        last_s: 0.0,
        w0: 0.0,
        dur: 0.1,
        dx: -350.0,
        dy: 0.0,
        span: 0.0,
    });
    sim.st.s = 0.004;
    sim.st.w = 0.004;
    let p = sim.pos(id, sim.st.w);
    assert!(p.0 < 260.0 && sim.st.foes[id].x - p.0 > 26.0);
    let h = sim.add_hit(
        HitSpec {
            source: HitSource::Weapon,
            what: "chain-lightning".into(),
            weight: Weight::Medium,
            primary: false,
            shape: Shape::Circle {
                x: p.0,
                y: p.1,
                radius: 16.0,
            },
            dir: 0.0,
            accent: 0.0,
            damage: 1.2,
            group: None,
        },
        None,
    );
    assert!(
        sim.apply_hit(&h, None).contains(&id),
        "target selected at its real pose cannot miss its own impact"
    );
}

#[test]
fn beatless_chain_survives_a_long_menu_and_pool_retirement_then_finishes() {
    let mut file = stream();
    file.log.beats.clear();
    let mut sim = survivor_sim_with(file);
    sim.learn_survivor_weapon(WeaponId::ChainLightning).unwrap();
    let rec = Rc::new(std::cell::RefCell::new(
        crate::recorder::LogRecorder::default(),
    ));
    sim.rec = Box::new(rec.clone());
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    for x in [220.0, 310.0, 400.0] {
        sim.spawn_foe(def, x, 0.0, Some(1e9));
    }
    sim.advance_to(0.05);
    assert_eq!(rec.borrow().chains[0].len(), 2);
    sim.enter_freeze(FreezeReason::Paused);
    sim.advance_to(10.1);
    sim.trim_state();
    assert!(
        sim.st.chains.get(0).is_some(),
        "held jump owns its chain state"
    );
    key(&mut sim, 10.11, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 10.12, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(10.6);
    sim.trim_state();
    sim.advance_to(11.1);
    assert_eq!(
        rec.borrow().chains[0].len(),
        4,
        "original pending jumps finish without a beat"
    );
}
