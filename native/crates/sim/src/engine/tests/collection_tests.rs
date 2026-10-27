//! Comfort without changing earned XP, contact protection, or authored geometry.
use super::*;
use crate::survivors::{FreezeReason, PassiveId};

#[test]
fn collection_cue_radius_is_the_actual_magnet_radius() {
    let mut s = survivor_sim();
    assert_eq!(s.run_state().pickup_radius, 120.0);
    s.learn_survivor_passive(PassiveId::Magnet).unwrap();
    assert!((s.run_state().pickup_radius - 156.0).abs() < 1e-9);
}

#[test]
fn clearing_is_bounded_forced_recoil_and_never_moves_bosses_or_formations() {
    let mut s = survivor_sim();
    let def = s.env.input.lib.enemies.get_index_of("skeleton").unwrap();
    let ordinary = s.spawn_foe(def, 30.0, 0.0, Some(1e9));
    let boss = s.spawn_foe(def, 40.0, 0.0, Some(1e9));
    s.mark_survivor_boss(boss, true);
    let pinned = s.spawn_member(def, 50.0, 0.0, super::super::Member::SurvivorEvent(99));
    let outside = s.spawn_foe(def, 180.0, 0.0, Some(1e9));
    s.st.invuln_until = 0.01;
    let hp = s.st.health;
    s.upgrade_pulse();
    assert_eq!(s.st.invuln_until, 0.01);
    assert_eq!(s.st.health, hp);
    let life = s.st.foes[ordinary].recoil_until - s.st.w;
    let displacement = s.st.foes[ordinary].recoil_v.0 * life;
    assert!((displacement - 48.0).abs() < 1e-9);
    assert!(s.st.foes[ordinary].step.is_none());
    for i in [boss, pinned, outside] {
        assert!(s.st.foes[i].recoil_until <= s.st.w);
        assert!(s.st.foes[i].step.is_none());
    }
    // Reaching the end cannot leave a smooth pursuit velocity behind.
    assert!((s.pos(ordinary, s.st.w + life + 1.0).0 - 78.0).abs() < 1e-9);
}

#[test]
fn chained_cards_celebrate_once_on_resume_and_snapshot_replays_that_same_pulse() {
    let mut s = survivor_sim();
    s.st.health = 80.0;
    let r = Rc::new(std::cell::RefCell::new(
        crate::recorder::LogRecorder::default(),
    ));
    r.borrow_mut().breaks = s.st.breaks.iter().copied().collect();
    s.rec = Box::new(r.clone());
    s.gem_arrives(0, 45.0);
    assert_eq!(s.run_state().frozen, Some(FreezeReason::LevelUp));
    for _ in 0..3 {
        let offer = s.run_state().offer.unwrap();
        let at = offer.reveals[2] + 0.01;
        key(&mut s, at, KeyEventKind::Press, InputKey::Confirm);
        key(&mut s, at + 0.001, KeyEventKind::Release, InputKey::Confirm);
        s.advance_to(at + 0.002);
        assert!(
            s.run_state().upgrade.is_none(),
            "no push between chained cards or before resume"
        );
    }
    let snap = s.snapshot();
    let before = r.borrow().clone();
    let at = s.st.freeze.resume.unwrap();
    let mut outputs = Vec::new();
    for _ in 0..2 {
        s.restore(&snap);
        *r.borrow_mut() = before.clone();
        s.advance_to(at + 0.2);
        let pulse = s.run_state().upgrade.unwrap();
        assert_eq!(pulse.song, at);
        assert!(!s.st.freeze.upgrade.pending);
        assert_eq!(
            s.st.health, 80.0,
            "card celebration grants no undocumented heal"
        );
        let identities: std::collections::BTreeSet<_> = r
            .borrow()
            .run_states
            .iter()
            .filter_map(|r| r.upgrade.map(|u| u.song.to_bits()))
            .collect();
        assert_eq!(identities.len(), 1);
        outputs.push(format!("{:?}", r.borrow().run_states));
    }
    assert_eq!(outputs[0], outputs[1]);
}

#[test]
fn resolved_upgrade_waits_for_the_last_nested_pause_reason() {
    let mut s = survivor_sim();
    s.gem_arrives(0, 5.0);
    s.enter_freeze(FreezeReason::Paused);
    let at = s.run_state().offer.unwrap().reveals[2] + 0.01;
    key(&mut s, at, KeyEventKind::Press, InputKey::Confirm);
    key(&mut s, at + 0.001, KeyEventKind::Release, InputKey::Confirm);
    s.advance_to(at + 0.1);
    assert!(s.st.freeze.upgrade.pending);
    assert!(s.run_state().upgrade.is_none());
    assert_eq!(s.run_state().frozen, Some(FreezeReason::Paused));
    key(&mut s, at + 0.2, KeyEventKind::Press, InputKey::Pause);
    key(&mut s, at + 0.201, KeyEventKind::Release, InputKey::Pause);
    let resume = s.st.freeze.resume.unwrap();
    s.advance_to(resume + 0.001);
    assert_eq!(s.run_state().upgrade.unwrap().song, resume);
}

#[test]
fn simultaneous_gem_arrivals_finish_reopening_cards_before_the_resume_celebration() {
    let mut s = survivor_sim();
    s.st.q.push(0.25, Ev::Gem { gem: 0, value: 5.0 });
    s.st.q.push(
        0.25,
        Ev::Gem {
            gem: 1,
            value: 15.0,
        },
    );
    s.advance_to(0.26);
    let at = s.run_state().offer.unwrap().reveals[2] + 0.01;
    key(&mut s, at, KeyEventKind::Press, InputKey::Confirm);
    key(&mut s, at + 0.001, KeyEventKind::Release, InputKey::Confirm);
    let first_resume = s.st.freeze.resume.unwrap();
    s.advance_to(first_resume + 0.001);
    assert_eq!(s.run_state().frozen, Some(FreezeReason::LevelUp));
    assert_eq!(s.run_state().offer.unwrap().level, 3);
    assert!(
        s.run_state().upgrade.is_none(),
        "held same-beat XP immediately reopens a card; no premature pulse or sound"
    );
    assert!(s.st.freeze.upgrade.pending);
    let at = s.run_state().offer.unwrap().reveals[2] + 0.01;
    key(&mut s, at, KeyEventKind::Press, InputKey::Confirm);
    key(&mut s, at + 0.001, KeyEventKind::Release, InputKey::Confirm);
    let final_resume = s.st.freeze.resume.unwrap();
    s.advance_to(final_resume + 0.001);
    let pulse = s.run_state().upgrade.unwrap();
    assert_eq!(pulse.song, final_resume);
    assert!(pulse.song > first_resume);
    assert!((pulse.world - 0.25).abs() < 1e-9);
}
