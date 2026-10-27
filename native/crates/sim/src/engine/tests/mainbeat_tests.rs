//! FR1: catalog roles, actual timestamped stream entries, holds, and causal gaps.
use super::*;
use crate::engine::RingPath;

fn tempo(bpm: f64) -> StreamFile {
    let mut f = stream();
    let p = 60.0 / bpm;
    f.log.beats = (0..40)
        .map(|i| Beat {
            t: i as f64 * p,
            known_at: (i as f64 * p - p).max(0.0),
            period: p,
            evidence: 1.0,
        })
        .collect();
    f.log.ticks = (0..240)
        .map(|i| Tick {
            t: i as f64 * 0.05,
            known_at: i as f64 * 0.05,
            evidence: 1.0,
            energy: 0.5,
            level: None,
            key: None,
            bands: None,
            brightness: None,
        })
        .collect();
    f
}

fn check_holds(mut file: StreamFile, end: f64) -> usize {
    let beats = file.log.beats.clone();
    file.audio.duration = end + 10.0;
    let mut sim = survivor_sim_with(file);
    let mut ids = Vec::new();
    for (i, role) in ["vs-bat1", "vs-ghost", "vs-bat-swarm", "vs-xlmantis"]
        .iter()
        .enumerate()
    {
        let def = sim.env.input.lib.enemies.get_index_of(*role).unwrap();
        Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap()
            .knockback = 0.0;
        let id = sim.spawn_foe(def, 300.0, 100.0 + i as f64 * 50.0, Some(100_000.0));
        if i == 2 {
            sim.st.foes[id].swarm_dir = Some((-1.0, 0.0));
        }
        if i == 3 {
            sim.mark_survivor_boss(id, true);
        }
        ids.push(id);
    }
    let mut moved = 0;
    for i in 1..=(end * 240.0) as usize {
        let before = sim.st.s;
        let prev: Vec<_> = ids.iter().map(|&id| sim.pos(id, sim.st.w)).collect();
        sim.st.recycle_k = 0; // Keep the injected swarm visible to this displacement audit.
        let now = i as f64 / 240.0;
        sim.advance_to(now);
        for (j, &id) in ids.iter().enumerate() {
            let p = sim.pos(id, sim.st.w);
            if (p.0 - prev[j].0).hypot(p.1 - prev[j].1) > 1e-8 {
                moved += 1;
                assert!(
                    beats.iter().enumerate().any(|(k, b)| {
                        let period = if k > 0 {
                            b.t - beats[k - 1].t
                        } else {
                            b.period
                        };
                        b.known_at <= now
                            && now >= b.t - (0.2 * period).min(0.1) - 1e-8
                            && before < b.t + 1e-8
                    }),
                    "{id} moved in stationary hold {before}..{now}"
                );
            }
        }
    }
    moved
}

#[test]
fn catalog_bats_swarms_and_bosses_hold_at_70_128_174_bpm() {
    for bpm in [70.0, 128.0, 174.0] {
        let n = check_holds(tempo(bpm), 6.0);
        assert!(n > 100, "must exercise actual animated steps");
        println!("FR1 {bpm} BPM: {n} moving root samples, all admitted; other samples held");
    }
}

#[test]
fn recorded_house_delayed_timestamped_music_has_no_offbeat_travel() {
    let mut f = stream();
    f.log = StreamLog::default();
    for line in include_str!("../../../../../fixtures/session-house-delayed/stream.jsonl").lines() {
        let v: serde_json::Value = serde_json::from_str(line).unwrap();
        match v["group"].as_str().unwrap() {
            "tick" => f
                .log
                .ticks
                .push(serde_json::from_value(v["entry"].clone()).unwrap()),
            "beat" => f
                .log
                .beats
                .push(serde_json::from_value(v["entry"].clone()).unwrap()),
            _ => {}
        }
    }
    let n = check_holds(f, 10.0);
    assert!(n > 0, "real music must exercise locked travel");
    println!("FR1 real recorded house-delayed: {n} moving root samples, all admitted");
}

#[test]
fn no_beats_low_confidence_and_late_evidence_hold_without_travel_debt() {
    for count in [0, 1] {
        let mut f = tempo(128.0);
        f.log.beats.truncate(count);
        let mut sim = survivor_sim_with(f);
        let id = rhythm_foe(&mut sim, 300.0);
        sim.advance_to(4.0);
        assert_eq!(sim.pos(id, sim.st.w), (300.0, 0.0));
        assert_eq!(sim.st.health, 120.0);
    }
    let mut f = tempo(128.0);
    for t in &mut f.log.ticks {
        if (2.0..3.0).contains(&t.t) {
            t.evidence = 0.0;
        }
    }
    // Entries learned after their timestamp cannot move anything retroactively.
    for b in &mut f.log.beats {
        if (2.0..4.0).contains(&b.t) {
            b.known_at = b.t + 0.02;
        }
    }
    let mut sim = survivor_sim_with(f);
    let id = rhythm_foe(&mut sim, 300.0);
    sim.advance_to(2.1);
    let held = sim.pos(id, sim.st.w);
    sim.advance_to(3.99);
    assert_eq!(sim.pos(id, sim.st.w), held);
    let snap = sim.snapshot();
    sim.advance_to(4.8);
    let expected = sim.pos(id, sim.st.w);
    assert!(expected.0 < held.0);
    sim.restore(&snap);
    sim.advance_to(4.8);
    assert_eq!(expected, sim.pos(id, sim.st.w));
}

#[test]
fn pause_during_step_captures_pose_and_cancels_remaining_distance() {
    let mut sim = survivor_sim_with(tempo(128.0));
    let id = rhythm_foe(&mut sim, 300.0);
    sim.advance_to(1.84);
    assert!(sim.st.foes[id].step.is_some());
    let p = sim.pos(id, sim.st.w);
    sim.freeze_at(1.84, crate::survivors::FreezeReason::Paused);
    sim.advance_to(3.0);
    assert_eq!(p, sim.pos(id, sim.st.w));
    key(&mut sim, 3.0, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 3.01, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(3.3);
    assert_eq!(p, sim.pos(id, sim.st.w));
}

#[test]
fn ring_holds_until_close_and_cancelled_travel_is_not_repaid() {
    let mut sim = survivor_sim_with(tempo(128.0));
    let id = rhythm_foe(&mut sim, 300.0);
    sim.st.foes[id].has_at = true;
    sim.st.foes[id].slot_until = f64::INFINITY;
    sim.st.foes[id].ring_path = Some(RingPath {
        x: 0.0,
        y: 0.0,
        c: 300.0,
        sn: 0.0,
        from: 1.0,
        to: 0.5,
        u: 0.0,
        du: 0.0,
    });
    sim.advance_to(1.6);
    assert_eq!(sim.pos(id, sim.st.w), (300.0, 0.0));
    sim.st.foes[id].ring_path.as_mut().unwrap().du = 0.2;
    sim.advance_to(1.84);
    let p = sim.pos(id, sim.st.w);
    assert!(p.0 < 300.0);
    sim.freeze_at(1.84, crate::survivors::FreezeReason::Paused);
    sim.advance_to(1.841);
    let path = sim.st.foes[id].ring_path.unwrap();
    assert!((300.0 - 150.0 * path.u - p.0).abs() < 1e-8);
    sim.advance_to(3.0);
    key(&mut sim, 3.0, KeyEventKind::Press, InputKey::Pause);
    key(&mut sim, 3.01, KeyEventKind::Release, InputKey::Pause);
    sim.advance_to(3.76);
    let q = sim.pos(id, sim.st.w);
    assert!(
        (p.0 - q.0 - 150.0 * 0.2 * 60.0 / 128.0).abs() < 1e-7,
        "only one new beat of ring travel"
    );
}

#[test]
fn classic_rate_changes_keep_steps_on_heard_main_beats() {
    let file = tempo(128.0);
    let beats = file.log.beats.clone();
    let mut sim = sim_in(&file, false);
    // No unrelated director spawns; a catalog bat still uses the shared travel path.
    sim.st.q.remove(&|ev| matches!(ev, Ev::Auto { .. }));
    let def = sim.env.input.lib.enemies.get_index_of("vs-bat1").unwrap();
    let id = sim.spawn_foe(def, 300.0, 100.0, Some(100_000.0));
    sim.st.sch.base_steps[0].rate = 0.35;
    let mut moves = 0;
    for i in 1..=1440 {
        let now = i as f64 / 240.0;
        let before = sim.st.s;
        let p = sim.pos(id, sim.st.w);
        let forced = sim.st.foes[id].knock_t > 0.0;
        sim.st.recycle_k = 0;
        sim.advance_to(now);
        if i == 436 {
            sim.st.dip_start = now;
        }
        let q = sim.pos(id, sim.st.w);
        if (p.0 - q.0).hypot(p.1 - q.1) > 1e-8 && !forced && sim.st.foes[id].knock_t <= 0.0 {
            moves += 1;
            assert!(
                beats
                    .iter()
                    .any(|b| now >= b.t - (0.2 * b.period).min(0.1) - 1e-8 && before < b.t + 1e-8),
                "movement {before}..{now}: {p:?}->{q:?} step {:?} knock {}",
                sim.st.foes[id].step,
                sim.st.foes[id].knock_t
            );
        }
    }
    assert!(moves > 20);
}

#[test]
fn classic_armour_reduces_main_beat_contact() {
    let f = tempo(128.0);
    let run = |armour| {
        let mut sim = sim_in(&f, false);
        sim.st.q.remove(&|ev| matches!(ev, Ev::Auto { .. }));
        sim.st.bonus.armour = armour;
        let def = sim.env.input.lib.enemies.get_index_of("vs-zombie").unwrap();
        sim.spawn_foe(def, 30.0, 0.0, Some(100_000.0));
        sim.advance_to(1.41);
        sim.st.health
    };
    let a = run(2.0);
    let b = run(0.0);
    assert!(a > b, "armour {a} versus {b}");
}

#[test]
fn contact_notice_is_published_before_attack_without_root_motion() {
    use std::cell::RefCell;
    struct Notice(Rc<RefCell<Vec<(usize, f64)>>>);
    impl crate::recorder::Recorder for Notice {
        fn enemy_notice(&mut self, id: usize, peak_s: f64) {
            self.0.borrow_mut().push((id, peak_s));
        }
    }
    let mut sim = survivor_sim_with(tempo(128.0));
    let id = rhythm_foe(&mut sim, 30.0);
    let notes = Rc::new(RefCell::new(Vec::new()));
    sim.rec = Box::new(Notice(notes.clone()));
    sim.advance_to(1.2);
    assert!(
        notes
            .borrow()
            .iter()
            .any(|&(foe, peak)| foe == id && peak > sim.st.s)
    );
    let p = sim.pos(id, sim.st.w);
    sim.advance_to(1.28);
    assert_eq!(p, sim.pos(id, sim.st.w));
}

#[test]
fn sparse_committed_beats_do_not_repay_missing_distance_on_relock() {
    let mut f = tempo(128.0);
    f.log.beats.retain(|b| b.t < 2.0 || b.t > 4.0);
    let mut sim = survivor_sim_with(f);
    let id = rhythm_foe(&mut sim, 300.0);
    sim.advance_to(3.9);
    let p = sim.pos(id, sim.st.w);
    sim.advance_to(4.22);
    let q = sim.pos(id, sim.st.w);
    let speed = sim.env.input.lib.enemies[sim.st.foes[id].def].speed;
    assert!((p.0 - q.0).abs() <= speed * 60.0 / 128.0 + 1e-7);
}

#[test]
fn separation_runs_only_for_new_steps_but_admits_a_late_spawn_in_the_window() {
    let mut sim = survivor_sim_with(tempo(120.0));
    let id = rhythm_foe(&mut sim, 300.0);
    sim.advance_to(2.92);
    let work = sim.spatial.cache.hits + sim.spatial.cache.misses;
    let first = sim.st.foes[id].step.expect("admitted step");
    sim.advance_to(2.96);
    assert_eq!(sim.spatial.cache.hits + sim.spatial.cache.misses, work);
    assert_eq!(sim.st.foes[id].step.unwrap().dx, first.dx);
    assert_eq!(sim.st.foes[id].step.unwrap().dy, first.dy);
    let late = rhythm_foe(&mut sim, 350.0);
    let recoil = rhythm_foe(&mut sim, 400.0);
    let landing = rhythm_foe(&mut sim, 450.0);
    sim.st.foes[recoil].recoil_until = 2.975;
    sim.st.foes[recoil].recoil_v = (1.0, 0.0);
    sim.st.foes[landing].land_s = Some(2.975);
    sim.advance_to(2.99);
    assert!(
        sim.st.foes[late].step.is_some(),
        "late spawn still gets a causal shortened step"
    );
    assert!(
        sim.st.foes[recoil].step.is_some(),
        "recoil expiry still admits a step"
    );
    assert!(
        sim.st.foes[landing].step.is_some(),
        "landing still admits a step"
    );
    assert!(sim.spatial.cache.hits + sim.spatial.cache.misses > work);
    sim.advance_to(3.01);
    let pos = sim.pos(late, sim.st.w);
    sim.advance_to(3.2);
    assert_eq!(sim.pos(late, sim.st.w), pos, "stationary hold is unchanged");
}
