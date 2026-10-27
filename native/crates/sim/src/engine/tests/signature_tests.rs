//! Actual minotaur catalog and admitted beats, not an alternate damage implementation.
use super::*;
use crate::survivors::{FreezeReason, StatDelta};
fn base(bpm: f64) -> (Rc<std::cell::RefCell<crate::recorder::LogRecorder>>, Sim) {
    let (rec, mut s) = sword_measurement_base(
        bpm,
        1,
        StatDelta {
            might: -1.0,
            area: -1.0,
            ..Default::default()
        },
        false,
    );
    let period = 60.0 / bpm;
    s.log_mut().beats = (0..(220.0 / period) as usize)
        .map(|i| {
            let t = i as f64 * period;
            Beat {
                t,
                known_at: (t - period).max(0.0),
                period,
                evidence: 1.0,
            }
        })
        .collect();
    s.log_mut().ticks = (0..4400)
        .map(|i| {
            let t = f64::from(i) * 0.05;
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
        .collect();
    (rec, s)
}
#[test]
fn early_minotaur_warns_enters_slams_on_main_beats_and_keeps_source_health() {
    for bpm in [70.0, 128.0, 174.0] {
        let (rec, mut s) = base(bpm);
        s.advance_to(89.0);
        assert!(
            rec.borrow()
                .boss_cues
                .iter()
                .any(|c| c.kind == crate::survivors::BossCueKind::Warning)
        );
        s.advance_to(103.0);
        let id = s.st.boss_foe.unwrap();
        assert_eq!(s.env.input.lib.enemies[s.st.foes[id].def].id, "minotaur");
        assert_eq!(s.st.foes[id].health0, 175.0);
        let r = rec.borrow();
        assert_eq!(
            r.landmarks
                .iter()
                .filter(|l| l.id == "standing-stones")
                .count(),
            1
        );
        assert!(!r.slams.is_empty(), "{bpm}: no slams");
        assert!(
            r.slams
                .iter()
                .all(|m| s.log.beats.iter().any(|b| (b.t - m.s_land).abs() < 1e-8))
        );
        let land = r
            .boss_cues
            .iter()
            .find(|c| c.kind == crate::survivors::BossCueKind::Landed)
            .unwrap();
        assert!(land.run >= 90.0 - 1e-8 && land.run < 90.0 + 60.0 / bpm);
        eprintln!(
            "{bpm}BPM landed{} first slam{} HP175",
            land.run, r.slams[0].s_land
        );
    }
}
#[test]
fn signature_uses_one_slot_of_a_full_field_and_does_not_respawn_as_an_ordinary_enemy() {
    let (rec, mut s) = base(128.0);
    s.advance_to(89.3);
    let def = s.env.input.lib.enemies.get_index_of("vs-bat3").unwrap();
    for _ in 0..300 {
        s.spawn_foe(def, 10000.0, 0.0, Some(1e9));
    }
    s.advance_to(91.0);
    assert_eq!(s.st.alive.len(), 300);
    let id = s.st.boss_foe.unwrap();
    s.st.foes[id].x = 10000.0;
    s.st.foes[id].y = 10000.0;
    s.recycle();
    assert!(s.st.foes[id].alive && s.st.boss_foe == Some(id));
    assert_eq!(
        rec.borrow()
            .enemies
            .iter()
            .filter(|e| e.kind == "minotaur")
            .count(),
        1
    );
}
#[test]
fn signature_freeze_cancels_a_mark_and_snapshot_replay_keeps_one_landmark() {
    let (rec, mut s) = base(128.0);
    for i in 21600..24000 {
        s.advance_to(f64::from(i) / 240.0);
        if !s.st.slam_marks.is_empty() {
            break;
        }
    }
    assert!(!s.st.slam_marks.is_empty());
    let snap = s.snapshot();
    let prefix = rec.borrow().clone();
    let mut out = Vec::new();
    for _ in 0..2 {
        s.restore(&snap);
        *rec.borrow_mut() = prefix.clone();
        s.enter_freeze(FreezeReason::Paused);
        assert!(s.st.slam_marks.is_empty());
        let before = s.st.run_s;
        s.advance_to(100.0);
        assert_eq!(s.st.run_s, before);
        key(&mut s, 100.01, KeyEventKind::Press, InputKey::Pause);
        key(&mut s, 100.02, KeyEventKind::Release, InputKey::Pause);
        s.advance_to(110.0);
        let r = rec.borrow();
        assert_eq!(
            r.landmarks
                .iter()
                .filter(|l| l.id == "standing-stones")
                .count(),
            1
        );
        out.push((format!("{:?}", r.slams), format!("{:?}", r.boss_cues)));
    }
    assert_eq!(out[0], out[1]);
}
#[test]
fn confidence_loss_at_landing_holds_descent_and_replans_without_a_duplicate_actor() {
    let (rec, mut s) = base(120.0);
    for t in &mut s.log_mut().ticks {
        if (89.8..97.0).contains(&t.t) {
            t.evidence = 0.0;
        }
    }
    s.advance_to(96.0);
    assert!(
        !rec.borrow()
            .boss_cues
            .iter()
            .any(|c| c.kind == crate::survivors::BossCueKind::Landed)
    );
    s.advance_to(100.0);
    assert_eq!(
        rec.borrow()
            .enemies
            .iter()
            .filter(|e| e.kind == "minotaur")
            .count(),
        1
    );
    assert_eq!(
        rec.borrow()
            .landmarks
            .iter()
            .filter(|l| l.id == "standing-stones")
            .count(),
        1
    );
}

#[test]
fn source_boss_and_signature_never_land_on_the_same_main_beat() {
    let (rec, mut s) = base(120.0);
    let full = load_level(
        &source("levels/survivors.yaml"),
        &s.env.input.lib,
        &reactive_song_map(f64::INFINITY),
        None,
    )
    .unwrap();
    let mut rows = full.survivor_schedule;
    for row in &mut rows {
        row.minimum = Some(0);
        row.enemies = Some(Vec::new());
        row.events.clear();
        row.treasure.clear();
        row.bosses.clear();
        if row.minute == 3 {
            row.bosses.push("vs-bat4".into());
        }
    }
    let env = Rc::get_mut(&mut s.env).unwrap();
    env.input.level.survivor_schedule = rows;
    env.director.set_pieces.as_mut().unwrap().boss.after_s = 180.0;
    s.advance_to(179.7); // Real source notice and signature notice both reserve landings.
    assert_eq!(s.st.boss_presentation.pending, 2);
    s.advance_to(182.0);
    let r = rec.borrow();
    let land: Vec<_> = r
        .boss_cues
        .iter()
        .filter(|c| c.kind == crate::survivors::BossCueKind::Landed)
        .collect();
    assert_eq!(land.len(), 2);
    assert!((land[0].song - land[1].song).abs() >= 0.5);
    assert_eq!(r.enemies.iter().filter(|e| e.kind == "minotaur").count(), 1);
}
#[test]
fn pause_during_descent_replans_a_future_main_beat_without_duplicate_landmark() {
    let (rec, mut s) = base(120.0);
    s.advance_to(89.7);
    assert!(s.st.boss_foe.is_some());
    s.enter_freeze(FreezeReason::Paused);
    s.advance_to(96.0);
    key(&mut s, 96.01, KeyEventKind::Press, InputKey::Pause);
    key(&mut s, 96.02, KeyEventKind::Release, InputKey::Pause);
    s.advance_to(98.0);
    let r = rec.borrow();
    assert_eq!(r.enemies.iter().filter(|e| e.kind == "minotaur").count(), 1);
    assert_eq!(
        r.landmarks
            .iter()
            .filter(|l| l.id == "standing-stones")
            .count(),
        1
    );
    let l = r
        .boss_cues
        .iter()
        .find(|c| c.kind == crate::survivors::BossCueKind::Landed)
        .unwrap();
    assert!(l.song >= 96.5 && l.run >= 90.0);
}

#[test]
fn actual_sword_damages_and_kills_the_signature_with_normal_survivor_rewards() {
    let (rec, mut s) = base(120.0);
    s.advance_to(90.1);
    let id = s.st.boss_foe.unwrap();
    s.apply_survivor_stats(StatDelta {
        might: 1.0,
        area: 1.0,
        ..Default::default()
    });
    s.advance_to(91.1);
    assert!(s.st.foes[id].health > 0.0 && s.st.foes[id].health < 175.0);
    assert_eq!(
        s.env.input.lib.enemies[s.st.foes[id].def]
            .survivors
            .unwrap()
            .xp,
        50.0
    );
    // Keep the real rank-one sword; no injected lethal damage or HP changes.
    s.advance_to(179.9);
    assert!(!s.st.foes[id].alive);
    assert!(s.st.n_gems > 0);
    assert!(
        rec.borrow()
            .boss_cues
            .iter()
            .any(|c| c.kind == crate::survivors::BossCueKind::Defeated)
    );
}

#[test]
fn signature_warning_restarts_camera_after_a_previous_source_boss() {
    let (_, mut s) = base(120.0);
    s.st.boss_presentation.warning_run = Some(58.0);
    s.st.boss_presentation.landed_run = Some(60.0);
    s.st.boss_presentation.last_landed_run = Some(60.0);
    s.advance_to(89.0);
    assert!(s.st.boss_presentation.zoom(s.st.run_s) < 0.9);
    assert!(s.st.boss_presentation.landed_run.is_none());
}

#[test]
fn signature_death_and_retreat_cancel_pending_slam_deadlines() {
    for dead in [false, true] {
        let (_, mut s) = base(128.0);
        for i in 10800..12600 {
            s.advance_to(f64::from(i) / 120.0);
            if !s.st.slam_marks.is_empty() {
                break;
            }
        }
        assert!(
            !s.st.slam_marks.is_empty(),
            "actual boss never marked a slam"
        );
        let id = s.st.boss_foe.unwrap();
        if dead {
            s.st.foes[id].alive = false;
        } else {
            s.st.run_s = 200.0;
        }
        let cancelled_deadline = s.signature_stop();
        assert!(cancelled_deadline.is_finite());
        s.tick_signature();
        assert!(
            s.signature_stop().is_infinite(),
            "dead{dead}: cancelled slam still owns a deadline"
        );
        assert!(s.st.slam_marks.is_empty());
        s.advance_to(cancelled_deadline + 0.2);
        assert!(
            s.st.s > cancelled_deadline,
            "dead{dead}: clock{} deadline{} end{}",
            s.st.s,
            cancelled_deadline,
            s.st.end_s
        );
    }
}
