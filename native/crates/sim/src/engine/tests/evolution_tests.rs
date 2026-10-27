//! Source contracts plus the deliberate musical/per-hit demo adaptations.
use super::*;
use crate::recorder::LogRecorder;
use crate::survivors::{
    Arsenal, Item, PassiveId, SlotError, StatDelta, WeaponId, chest_item, weapon_catalog,
};
use std::{cell::RefCell, collections::BTreeMap};
fn evolved(bpm: f64, id: WeaponId) -> (Rc<RefCell<LogRecorder>>, Sim, usize) {
    let (rec, mut sim, def) = garlic_measurement(bpm, 1);
    let data = weapon_catalog().get(id);
    sim.st.survivor.as_mut().unwrap().arsenal =
        Arsenal::measurement_weapon(data.evolution_of.unwrap(), 8);
    sim.learn_survivor_passive(data.requires.unwrap()).unwrap();
    sim.evolve_survivor_weapon(id).unwrap();
    Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
        .survivors
        .as_mut()
        .unwrap()
        .knockback = 0.0;
    (rec, sim, def)
}
#[test]
fn evolution_all_five_require_max_base_and_partner_and_replace_atomically() {
    use WeaponId::*;
    for id in [BloodyTear, SoulEater, HolyWand, UnholyVespers, ThunderLoop] {
        let (_, mut sim, _) = garlic_measurement(120.0, 1);
        let data = weapon_catalog().get(id);
        let base = data.evolution_of.unwrap();
        sim.st.survivor.as_mut().unwrap().arsenal = Arsenal::measurement_weapon(base, 7);
        assert_eq!(sim.evolve_survivor_weapon(id), Err(SlotError::Unavailable));
        sim.learn_survivor_weapon(base).unwrap();
        assert_eq!(sim.evolve_survivor_weapon(id), Err(SlotError::Unavailable));
        sim.learn_survivor_passive(data.requires.unwrap()).unwrap();
        let inv = sim.survivor_inventory().unwrap();
        assert_ne!(chest_item(inv, weapon_catalog(), false), Item::Weapon(id));
        assert_eq!(chest_item(inv, weapon_catalog(), true), Item::Weapon(id));
        sim.evolve_survivor_weapon(id).unwrap();
        let after = sim.survivor_inventory().unwrap();
        assert_eq!(after.weapons.rank(base), None);
        assert_eq!(after.weapons.rank(id), Some(1));
        assert_eq!(after.weapons.iter().count(), inv.weapons.iter().count());
        assert_eq!(after.passives, inv.passives);
        assert_eq!(sim.evolve_survivor_weapon(id), Err(SlotError::Unavailable));
        assert_eq!(sim.learn_survivor_weapon(base), Err(SlotError::Unavailable));
        eprintln!(
            "{id:?}: max-base+partner eligibility, early chest blocked, slot replacement and repeat/base rejection pass"
        );
    }
}
#[test]
fn evolution_bloody_tear_crit_damage_and_shared_one_second_heal_limit() {
    let (rec, mut sim, def) = evolved(120.0, WeaponId::BloodyTear);
    sim.apply_survivor_stats(StatDelta {
        luck: 9.0,
        ..Default::default()
    }); // source .1*Luck => guaranteed crits
    sim.st.health = 30.0;
    for x in [20.0, 30.0, 40.0] {
        lightning_target(&mut sim, def, x);
    }
    sim.advance_to(1.51);
    let r = rec.borrow();
    let fates: Vec<_> = r.enemies.iter().flat_map(|e| e.fates.iter()).collect();
    assert!(!fates.is_empty() && fates.iter().all(|f| f.critical));
    let expected = 4.0 * (0.5 / 1.35) * 2.0;
    assert!(fates.iter().all(|f| (f.damage - expected).abs() < 1e-9));
    let heals: Vec<_> = r
        .heals
        .iter()
        .filter(|h| h["source"] == "bloody-tear")
        .collect();
    assert!(!heals.is_empty());
    assert!(heals.iter().all(|h| h["amount"].as_f64() == Some(8.0)));
    assert!(
        heals
            .windows(2)
            .all(|w| w[1]["s"].as_f64().unwrap() - w[0]["s"].as_f64().unwrap() >= 1.0 - 1e-9)
    );
    assert!(heals.len() <= 2, "many targets/spins share the heal clock");
    assert!((sim.st.health - (30.0 + 8.0 * heals.len() as f64)).abs() < 1e-9);
    eprintln!(
        "Bloody Tear: {} critical overlaps at damage{expected:.9}; {} shared 8HP heals in1.51s",
        fates.len(),
        heals.len()
    );
}
#[test]
fn evolution_soul_eater_heals_actual_contacts_and_grows_from_actual_recovery_only() {
    let (rec, mut sim, def) = evolved(120.0, WeaponId::SoulEater);
    sim.st.survivor.as_mut().unwrap().stats.max_hp = 1000.0;
    sim.st.health = 20.0;
    sim.heal(600.0, "test-recovery");
    assert_eq!(sim.st.soul_recovered, 600.0);
    for x in [20.0, 40.0] {
        lightning_target(&mut sim, def, x);
    }
    sim.advance_to(0.1);
    {
        let r = rec.borrow();
        assert!((r.weapon_shots[0].damage - 3.0).abs() < 1e-8);
        assert!(
            (r.weapon_shots[0].radius - 0.32 * crate::survivors::PX_PER_VS_UNIT * 3.0).abs() < 1e-9
        );
        let heals: Vec<_> = r
            .heals
            .iter()
            .filter(|h| h["source"] == "soul-eater")
            .collect();
        assert_eq!(heals.len(), 2);
        assert!(
            heals
                .iter()
                .all(|h| (h["amount"].as_f64().unwrap() - 1.2).abs() < 1e-9)
        );
        assert!(
            r.enemies
                .iter()
                .all(|e| e.fates.len() == 1 && !e.fates[0].lethal)
        );
    }
    sim.advance_to(0.9);
    assert!(
        rec.borrow().enemies.iter().all(|e| e.fates.len() == 1),
        "once per target per aura incarnation"
    );
    sim.st.health = 1000.0;
    let recovered = sim.st.soul_recovered;
    sim.heal(10_000.0, "overheal");
    assert_eq!(sim.st.soul_recovered, recovered);
    for _ in 0..5 {
        sim.st.health = 100.0;
        sim.heal(900.0, "test-recovery");
    }
    sim.advance_to(1.01);
    assert!(
        (rec.borrow().weapon_shots.last().unwrap().damage - 8.0).abs() < 1e-9,
        "source growth caps at +6"
    );
    eprintln!(
        "Soul Eater: source radius, +1 power/600 actual recovery capped+6; demo per-contact heal1.2 with Pummarola1, no kill required/overheal growth"
    );
}
#[test]
fn evolution_holy_wand_uses_source_speed_pierce_and_tome_throughput() {
    for bpm in [70.0, 128.0, 174.0] {
        let (rec, mut sim, _) = evolved(bpm, WeaponId::HolyWand);
        for _ in 1..5 {
            sim.learn_survivor_passive(PassiveId::EmptyTome).unwrap();
        }
        for _ in 0..2 {
            sim.learn_survivor_passive(PassiveId::Duplicator).unwrap();
        }
        sim.advance_to(10.0);
        let r = rec.borrow();
        let n = r
            .weapon_shots
            .iter()
            .filter(|s| s.id == WeaponId::HolyWand)
            .count();
        assert!(
            (185..=206).contains(&n),
            "{bpm}: {n} vs200 source bullets/10s, bounded startup/tail delay"
        );
        let first = r
            .weapon_shots
            .iter()
            .find(|s| s.id == WeaponId::HolyWand)
            .unwrap();
        assert_eq!(first.damage, 3.0);
        assert_eq!(first.pierce, 2.0);
        assert_eq!(first.speed, 2.0);
        assert!(
            (r.missiles[0].vx.hypot(r.missiles[0].vy) - 3.3 * crate::survivors::PX_PER_VS_UNIT)
                .abs()
                < 1e-8
        );
        eprintln!("Holy Wand {bpm}BPM: {n}/10s vs200 source bullets, P3/pierce2/speed3.3U/s");
    }
}
#[test]
fn evolution_vespers_keeps_a_visible_orbit_across_grid_gaps_and_source_overlaps() {
    for bpm in [70.0, 128.0, 174.0] {
        let (rec, mut sim, _) = evolved(bpm, WeaponId::UnholyVespers);
        let mut poses = BTreeMap::new();
        let mut seen = 0;
        for i in 1..=720 {
            sim.advance_to(f64::from(i) / 60.0);
            let r = rec.borrow();
            for p in &r.missiles[seen..] {
                if p.alive {
                    poses.insert(p.id, *p);
                } else {
                    poses.remove(&p.id);
                }
            }
            seen = r.missiles.len();
            assert!(!poses.is_empty(), "{bpm} dropped all blades at{}", sim.st.s);
            if sim.st.w > 0.1 {
                assert!(
                    poses.values().any(|p| p.radius > 0.01),
                    "no visible blade at{}",
                    sim.st.s
                );
            }
            assert!(sim.st.bibles.len() <= 3);
        }
        let r = rec.borrow();
        let shots: Vec<_> = r
            .weapon_shots
            .iter()
            .filter(|s| s.id == WeaponId::UnholyVespers)
            .collect();
        assert_eq!(shots[0].damage, 3.0);
        assert_eq!(shots[0].amount, 4);
        assert!((shots[0].duration_s - 3.3).abs() < 1e-9);
        assert!(
            shots
                .windows(2)
                .all(|w| (w[1].ready_run - w[0].ready_run - 3.0).abs() < 1e-9)
        );
        eprintln!(
            "Vespers {bpm}BPM: {} source3s recasts, visible throughout12s with required Spellbinder1/duration3.3s",
            shots.len()
        );
    }
}
#[test]
fn evolution_thunder_loop_returns_to_original_points_after_target_moves() {
    let (rec, mut sim, def) = evolved(120.0, WeaponId::ThunderLoop);
    let old = lightning_target(&mut sim, def, 120.0);
    sim.advance_to(0.9);
    assert_eq!(rec.borrow().lightning_strikes.len(), 7); // source6 + required Duplicator1
    sim.st.foes[old].x = 300.0;
    let new = lightning_target(&mut sim, def, 120.0);
    sim.advance_to(2.6);
    let r = rec.borrow();
    assert_eq!(r.lightning_strikes.len(), 14);
    for (first, second) in r.lightning_strikes[..7]
        .iter()
        .zip(&r.lightning_strikes[7..])
    {
        assert_eq!((first.x, first.y), (second.x, second.y));
        assert!(second.song - first.song >= 1.5 - 1e-9);
        assert!((second.song - first.song - 1.5).abs() < 1e-9);
    }
    assert_eq!(r.enemies[old].fates.len(), 7);
    assert_eq!(r.enemies[new].fates.len(), 7);
    assert!(
        r.enemies[new]
            .fates
            .iter()
            .all(|f| (f.damage - 6.5).abs() < 1e-9)
    );
    eprintln!(
        "Thunder Loop: 7 source+Amount first strikes /7 fixed-point returns at+1.5s, moved old target avoided/new occupant hit"
    );
}

const EVOLUTIONS: [WeaponId; 5] = [
    WeaponId::BloodyTear,
    WeaponId::SoulEater,
    WeaponId::HolyWand,
    WeaponId::UnholyVespers,
    WeaponId::ThunderLoop,
];
fn ready_all(bpm: f64) -> (Rc<RefCell<LogRecorder>>, Sim, usize) {
    let (rec, mut sim, def) = garlic_measurement(bpm, 1);
    sim.st.survivor.as_mut().unwrap().arsenal = Arsenal::default();
    for id in EVOLUTIONS {
        let d = weapon_catalog().get(id);
        let base = d.evolution_of.unwrap();
        while sim
            .survivor_inventory()
            .unwrap()
            .weapons
            .rank(base)
            .unwrap_or(0)
            < 8
        {
            sim.learn_survivor_weapon(base).unwrap();
        }
        sim.learn_survivor_passive(d.requires.unwrap()).unwrap();
    }
    Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
        .survivors
        .as_mut()
        .unwrap()
        .knockback = 0.0;
    (rec, sim, def)
}
#[test]
fn evolution_real_roulette_grants_five_replacements_only_from_late_origin() {
    for minute in [5, 25] {
        let (rec, mut sim, _) = ready_all(120.0);
        let h = sim.hero_pose(sim.st.w);
        sim.drop_chest(h.x, h.y, 3, minute);
        // Bound stepping so an invalid fixture clock cannot spin without a deadline.
        for _ in 0..10_000 {
            if sim.st.s >= 5.0 {
                break;
            }
            sim.step();
        }
        assert!(sim.st.s >= 5.0, "roulette clock failed to advance");
        let inv = sim.survivor_inventory().unwrap();
        assert_eq!(inv.weapons.iter().count(), 5);
        for id in EVOLUTIONS {
            if minute == 25 {
                assert_eq!(inv.weapons.rank(id), Some(1));
            } else {
                assert_eq!(inv.weapons.rank(id), None);
            }
        }
        assert!(
            rec.borrow()
                .chests
                .iter()
                .any(|(_, c)| matches!(c, crate::survivors::ChestEvent::Landed { .. }))
        );
        eprintln!(
            "actual tier3 roulette origin{minute}: five sequential grants, evolution grant allowed{}",
            minute == 25
        );
    }
}
#[test]
fn evolution_thunder_charge_and_holy_burst_keep_remaining_time_through_freeze() {
    let (rec, mut sim, def) = evolved(120.0, WeaponId::ThunderLoop);
    lightning_target(&mut sim, def, 120.0);
    sim.freeze_at(0.2, crate::survivors::FreezeReason::Paused);
    key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(5.2);
    let r = rec.borrow();
    assert_eq!(r.lightning_strikes.len(), 14);
    assert!((r.lightning_strikes[7].song - 3.875).abs() < 1e-9);
    assert!(
        r.lightning_strikes
            .iter()
            .all(|s| (s.x - 120.0).abs() < 1e-9)
    );
    eprintln!(
        "Thunder Loop fractional pause0.2..2.5: first return3.875, >=1.5 active seconds after first point"
    );
    drop(r);
    let (rec, mut sim, _) = evolved(120.0, WeaponId::HolyWand);
    sim.freeze_at(0.075, crate::survivors::FreezeReason::Paused);
    key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(3.0);
    let r = rec.borrow();
    let shot = r
        .weapon_shots
        .iter()
        .find(|s| s.id == WeaponId::HolyWand && (s.ready_run - 0.1).abs() < 1e-9)
        .unwrap();
    assert!(
        (shot.song - 2.525).abs() < 1e-9,
        "remaining 25ms of already emitted burst must survive"
    );
}
#[test]
fn evolution_snapshot_restores_all_five_pending_effects_heal_counters_and_crit_results() {
    let (rec, mut sim, def) = ready_all(128.0);
    for id in EVOLUTIONS {
        sim.evolve_survivor_weapon(id).unwrap();
    }
    sim.st.health = 30.0;
    sim.apply_survivor_stats(StatDelta {
        luck: 2.0,
        ..Default::default()
    });
    for x in [20.0, 70.0, 140.0] {
        lightning_target(&mut sim, def, x);
    }
    sim.freeze_at(0.237, crate::survivors::FreezeReason::Paused);
    key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(0.2);
    let snapshot = sim.snapshot();
    let recording = rec.borrow().clone();
    sim.advance_to(8.0);
    let expected = format!("{:?}", rec.borrow());
    let health = (
        sim.st.health,
        sim.st.soul_recovered,
        sim.st.last_bloody_heal,
    );
    assert!(
        rec.borrow()
            .enemies
            .iter()
            .flat_map(|e| e.fates.iter())
            .any(|f| f.critical)
    );
    sim.restore(&snapshot);
    *rec.borrow_mut() = recording;
    let mut t = 0.2_f64;
    while t < 8.0 {
        t = (t + 0.017).min(8.0);
        sim.advance_to(t);
    }
    let actual = format!("{:?}", rec.borrow());
    if actual != expected {
        let i = actual
            .bytes()
            .zip(expected.bytes())
            .position(|(a, b)| a != b)
            .unwrap_or(actual.len().min(expected.len()));
        panic!(
            "snapshot recording differs at {i}: actual{} / expected{}",
            &actual[i..(i + 250).min(actual.len())],
            &expected[i..(i + 250).min(expected.len())]
        );
    }
    assert_eq!(
        (
            sim.st.health,
            sim.st.soul_recovered,
            sim.st.last_bloody_heal
        ),
        health
    );
    eprintln!(
        "all five evolutions: full LogRecorder and heal/critical state identical after snapshot0.2, fractional pause0.237, irregular17ms replay cuts through8s"
    );
}
#[test]
fn evolution_all_five_ignore_poisoned_uncommitted_beats_and_energy() {
    let run = |poison: bool| {
        let (rec, mut sim, def) = ready_all(128.0);
        for id in EVOLUTIONS {
            sim.evolve_survivor_weapon(id).unwrap();
        }
        for x in [20.0, 70.0, 140.0] {
            lightning_target(&mut sim, def, x);
        }
        sim.st.health = 30.0;
        if poison {
            let log = Rc::make_mut(&mut sim.log);
            for b in &mut log.beats {
                if b.known_at > 1.6 {
                    b.t += 10.0;
                    b.period = 0.9;
                    b.evidence = 0.0;
                }
            }
            for t in &mut log.ticks {
                if t.known_at > 1.6 {
                    t.evidence = 0.0;
                    t.energy = 0.0;
                }
            }
        }
        sim.advance_to(1.6);
        format!("{:?}", rec.borrow())
    };
    assert_eq!(run(false), run(true));
}

#[test]
fn evolution_soul_recovery_bonus_is_added_after_non_default_might() {
    let (rec, mut sim, def) = evolved(120.0, WeaponId::SoulEater);
    for _ in 0..5 {
        sim.learn_survivor_passive(PassiveId::Spinach).unwrap();
    }
    sim.st.survivor.as_mut().unwrap().stats.max_hp = 1000.0;
    sim.st.health = 20.0;
    sim.heal(600.0, "test-recovery");
    lightning_target(&mut sim, def, 20.0);
    sim.advance_to(0.1);
    assert!(
        (rec.borrow().weapon_shots[0].damage - 4.0).abs() < 1e-9,
        "source: 2*1.5+1, not (2+1)*1.5"
    );
    assert!((rec.borrow().enemies[0].fates[0].damage - 4.0).abs() < 1e-9);
    for _ in 0..5 {
        sim.st.health = 100.0;
        sim.heal(900.0, "test-recovery");
    }
    sim.advance_to(1.01);
    assert!(
        (rec.borrow().weapon_shots.last().unwrap().damage - 9.0).abs() < 1e-9,
        "source cap: 2*1.5+6, not12"
    );
    assert!((rec.borrow().enemies[0].fates.last().unwrap().damage - 9.0).abs() < 1e-9);
    eprintln!(
        "Soul Eater +Spinach5/Might1.5: 600 recovered HP gives actual4 damage; +6 recovery cap gives9, bonus added after Might"
    );
}

#[test]
fn holy_wand_burst_repeats_use_source_speed_including_non_default_character_speed() {
    for bonus in [0.0, 1.0] {
        let (rec, mut sim, _) = evolved(120.0, WeaponId::HolyWand);
        sim.apply_survivor_stats(StatDelta {
            amount: 2.0,
            projectile_speed: bonus,
            ..Default::default()
        });
        sim.advance_to(0.26);
        let r = rec.borrow();
        let shots: Vec<_> = r
            .weapon_shots
            .iter()
            .filter(|s| s.id == WeaponId::HolyWand)
            .collect();
        assert_eq!(shots.len(), 6);
        // Curated HOLY_MISSILE RepeatInterval100ms / (weapon Speed2 * character Speed).
        let repeat = 0.1 / (2.0 * (1.0 + bonus));
        for (i, shot) in shots.iter().enumerate() {
            assert!((shot.song - i as f64 * repeat).abs() < 1e-9);
            assert!((shot.ready_run - i as f64 * repeat).abs() < 1e-9);
        }
        eprintln!(
            "Holy Wand Speed{} six-shot burst interval{repeat:.3}s final{:.3}s",
            2.0 * (1.0 + bonus),
            shots.last().unwrap().song
        );
    }
}
