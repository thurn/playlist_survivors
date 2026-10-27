//! FR2 attack identity: real target-facing animation, radial catalog damage and waves.
use super::*;
use crate::survivors::StatDelta;

#[test]
fn sword_faces_present_targets_but_hits_all_angles_and_keeps_its_pose_during_stride() {
    let (rec, mut sim) = sword_measurement_base(128.0, 1, StatDelta::default(), false);
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    for (x, y) in [(0.0, -100.0), (180.0, 0.0), (0.0, 180.0), (-180.0, 0.0)] {
        sim.spawn_foe(def, x, y, Some(1e9));
    }
    sim.advance_to(1.24);
    assert!(
        (sim.st.facing_angle + std::f64::consts::FRAC_PI_2).abs() < 0.5,
        "nearest target breaks crowd ties"
    );
    // A movement request cannot replace the attack's wind-up/strike animation.
    key(&mut sim, 1.25, KeyEventKind::Press, InputKey::Right);
    sim.advance_to(1.407);
    let active = sim
        .st
        .live_swings
        .iter()
        .map(|&i| &sim.st.swing_pool[i])
        .find(|s| s.struck && (s.s_strike - 1.40625).abs() < 1e-8)
        .unwrap();
    assert!(matches!(
        active.anim.as_str(),
        "slash" | "backslash" | "halfslash"
    ));
    let r = rec.borrow();
    assert!(
        r.enemies.iter().all(|e| !e.fates.is_empty()),
        "radial hits catch all four directions"
    );
    assert!(r.swings.iter().all(|s| s["reach"]["degrees"] == 360.0));
    for shot in &r.weapon_shots {
        let id = shot.attack.expect("primary identity");
        let hit = r
            .hits
            .iter()
            .find(|h| h["id"].as_f64() == Some(id as f64))
            .unwrap();
        assert_eq!(hit["dir"], shot.dir);
        assert_eq!(hit["shape"]["radius"], shot.radius);
    }
}

#[test]
fn no_hittable_nearby_target_retains_facing_at_windup() {
    let (_, mut sim) = sword_measurement_base(128.0, 1, StatDelta::default(), false);
    sim.st.facing_angle = 0.73;
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    sim.spawn_foe(def, -600.0, 0.0, Some(1e9));
    sim.advance_to(1.24);
    assert_eq!(sim.st.facing_angle, 0.73);
}

#[test]
fn forecast_and_nearby_onset_launch_one_wave_and_deduplicate_primary_targets() {
    let (rec, mut sim) = sword_measurement_base(128.0, 1, StatDelta::default(), true);
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    sim.spawn_foe(def, 245.0, 0.0, Some(1e9));
    sim.spawn_foe(def, 350.0, 0.0, Some(1e9));
    sim.log_mut().onsets.push(stream::Onset {
        t: 2.34375,
        known_at: 2.35,
        strength: 1.0,
        strong: true,
        band: None,
    });
    sim.advance_to(3.5);
    let r = rec.borrow();
    assert_eq!(
        r.cleaves.len(),
        1,
        "forecast and detected onset celebrate one attack"
    );
    assert_eq!(
        r.enemies[0].fates.len(),
        1,
        "oversized primary is not hit by its own wave"
    );
    assert_eq!(
        r.enemies[1].fates.len(),
        1,
        "outward wave catches each outer target once"
    );
    assert_eq!(r.enemies[0].fates[0].damage, 2.5);
    assert_eq!(r.enemies[1].fates[0].damage, 1.0);
}

#[test]
fn freeze_discards_windup_and_resumes_with_one_attack_instead_of_a_burst() {
    let (rec, mut sim) = sword_measurement_base(128.0, 1, StatDelta::default(), false);
    sim.freeze_at(1.3, crate::survivors::FreezeReason::Paused);
    sim.advance_to(3.0);
    assert!(sim.st.live_swings.is_empty());
    let before = rec.borrow().swings.len();
    key(&mut sim, 3.0, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 3.01, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(3.4);
    assert_eq!(rec.borrow().swings.len() - before, 1);
}

#[test]
fn classic_delayed_qualifying_press_keeps_primary_exclusions_after_another_hit() {
    use crate::core::geometry::Shape;
    use crate::engine::combat::HitSpec;
    use crate::recorder::{HitSource, LogRecorder, Weight};
    use std::cell::RefCell;
    let mut f = stream();
    f.log.forecasts = Some(vec![stream::Forecast {
        t: 2.5,
        known_at: 1.9,
        cycle: 4.0,
        confidence: 1.0,
    }]);
    let mut sim = sim_in(&f, false);
    let rec = Rc::new(RefCell::new(LogRecorder::default()));
    sim.rec = Box::new(rec.clone());
    sim.advance_to(2.29);
    let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    let id = sim.spawn_foe(def, 130.0, 0.0, Some(1e9));
    sim.st.foes[id].weight = 1e6;
    sim.advance_to(2.501);
    assert!(
        sim.st
            .pending_cleave
            .as_ref()
            .is_some_and(|c| c.primary_targets.contains(&id))
    );
    sim.st.foes[id].knock_t = 0.0;
    sim.st.foes[id].x = 130.0;
    sim.st.foes[id].y = 0.0;
    let h = sim.add_hit(
        HitSpec {
            source: HitSource::Weapon,
            what: "fire-trail".into(),
            weight: Weight::Light,
            primary: false,
            shape: Shape::Circle {
                x: 130.0,
                y: 0.0,
                radius: 10.0,
            },
            dir: 0.0,
            accent: 0.0,
            damage: 0.01,
            group: None,
        },
        None,
    );
    sim.damage(id, &h);
    sim.st.foes[id].knock_t = 0.0;
    sim.st.foes[id].has_at = true;
    sim.st.foes[id].slot_until = f64::INFINITY;
    key(&mut sim, 2.55, KeyEventKind::Press, InputKey::Right);
    key(&mut sim, 2.56, KeyEventKind::Release, InputKey::Right);
    sim.advance_to(2.95);
    let r = rec.borrow();
    assert_eq!(
        r.cleaves.len(),
        1,
        "late Great/Perfect must actually launch the pending cleave"
    );
    let waves: Vec<_> = r
        .hits
        .iter()
        .filter(|h| h["source"] == "wave")
        .map(|h| h["id"].as_f64().unwrap())
        .collect();
    assert!(!waves.is_empty());
    let target = r.enemies.iter().find(|e| e.id == id as f64).unwrap();
    assert!(
        !target.fates.iter().any(|f| waves.contains(&f.hit)),
        "intervening hit cannot erase primary exclusion"
    );
}

#[test]
fn classic_tiers_change_real_sword_and_weapon_damage_while_zen_stays_full() {
    use crate::survivors::{WeaponId, weapon_catalog};
    for zen in [false, true] {
        for tier in [0.0, 3.0] {
            let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
            Rc::get_mut(&mut sim.env).unwrap().input.zen = zen;
            sim.learn_survivor_weapon(WeaponId::MagicWand).unwrap();
            sim.st.tier = tier;
            sim.st.groove = if tier == 0.0 { 0.0 } else { 1.0 };
            // Isolate the selected earned tier from later drain; judging itself is tested below.
            sim.st.last_raise_s = 100.0;
            sim.advance_to(0.51);
            let r = rec.borrow();
            for shot in &r.weapon_shots {
                let base = weapon_catalog().get(shot.id).level(shot.rank).power;
                let mult = if zen {
                    1.0
                } else {
                    crate::tiers::tier_damage(&sim.env.input.lib.hero, tier)
                };
                assert!((shot.damage - base * mult).abs() < 1e-9);
                if shot.id.is_sword() {
                    let expected = if zen || tier == 3.0 { 190.0 } else { 100.0 };
                    assert!((shot.radius - expected).abs() < 1e-9);
                    let hit = r
                        .hits
                        .iter()
                        .find(|h| h["id"].as_f64() == shot.attack.map(|a| a as f64))
                        .unwrap();
                    assert_eq!(hit["damage"], shot.damage);
                    assert_eq!(hit["shape"]["radius"], shot.radius);
                }
            }
            assert!(r.weapon_shots.iter().any(|s| s.id == WeaponId::MagicWand));
            assert!(r.weapon_shots.iter().any(|s| s.id.is_sword()));
        }
    }
}

#[test]
fn survivor_classic_keeps_judging_rewards_drain_and_optional_hitstop() {
    for zen in [false, true] {
        let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
        let input = &mut Rc::get_mut(&mut sim.env).unwrap().input;
        input.zen = zen;
        input.settling = false;
        sim.st.groove = 0.1;
        sim.st.tier = 0.0;
        sim.advance_to(2.0);
        let before = sim.st.groove;
        key(&mut sim, 2.01, KeyEventKind::Press, InputKey::Right);
        key(&mut sim, 2.1, KeyEventKind::Release, InputKey::Right);
        sim.advance_to(2.11);
        let earned = sim.st.groove;
        assert_eq!(
            rec.borrow().inputs.iter().any(|i| i["rating"].is_string()),
            !zen
        );
        if !zen {
            assert!(earned > before);
        }
        sim.start_hit_stop(0.05);
        assert_eq!(sim.in_stop(sim.st.s + 0.001), !zen);
        assert_eq!(sim.rate_at(sim.st.s + 0.001), if zen { 1.0 } else { 0.0 });
        sim.advance_to(12.0);
        if zen {
            assert_eq!(sim.st.groove, earned);
        } else {
            assert!(sim.st.groove < earned);
        }
    }
}

#[test]
fn survivor_classic_beatless_passages_keep_fair_credit_without_fake_ratings() {
    let (rec, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
    let input = &mut Rc::get_mut(&mut sim.env).unwrap().input;
    input.zen = false;
    input.settling = false;
    sim.st.groove = 0.1;
    sim.st.tier = 0.0;
    for t in &mut sim.log_mut().ticks {
        t.evidence = 0.0;
    }
    sim.advance_to(8.0);
    assert!(
        sim.st.groove > 0.1,
        "unclear music grants fairness credit rather than draining"
    );
    assert!(rec.borrow().inputs.iter().all(|i| !i["rating"].is_string()));
}

#[test]
fn classic_groove_holds_through_an_upgrade_pick() {
    // (Settling, as the app runs: the drain runs 2 s late, as of each beat.)
    for settling in [false, true] {
        let late = if settling {
            crate::reactive::settle::SETTLE_DELAY
        } else {
            0.0
        };
        let (_, mut sim) = sword_measurement_base(120.0, 1, StatDelta::default(), false);
        let input = &mut Rc::get_mut(&mut sim.env).unwrap().input;
        input.zen = false;
        input.settling = settling;
        sim.st.groove = 1.0;
        sim.st.tier = 3.0;
        // Steady on-beat presses up to the menu.
        for b in 2..8 {
            let s = f64::from(b) * 0.5 + 0.01;
            key(&mut sim, s, KeyEventKind::Press, InputKey::Right);
            key(&mut sim, s + 0.05, KeyEventKind::Release, InputKey::Right);
        }
        sim.freeze_at(3.8, crate::survivors::FreezeReason::LevelUp);
        sim.advance_to(3.81);
        assert!(sim.st.freeze.frozen);
        let held = sim.st.groove;
        // A long look at the cards, then a pick.
        sim.advance_to(9.0);
        assert_eq!(
            sim.st.groove, held,
            "settling {settling}: no drain while choosing"
        );
        key(&mut sim, 9.0, KeyEventKind::Press, InputKey::Confirm);
        key(&mut sim, 9.05, KeyEventKind::Release, InputKey::Confirm);
        sim.advance_to(9.51);
        assert!(!sim.st.freeze.frozen);
        // The bar after the menu (1 s here) is the player's to find the beat again: the drain does
        // not count the menu's time against them.
        sim.advance_to(10.45 + late);
        assert_eq!(
            sim.st.groove, held,
            "settling {settling}: no drain for the menu's time"
        );
    }
}
