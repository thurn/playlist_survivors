use super::*;
use crate::live::{LiveView, Moment, Record};
use sim::survivors::{FreezeReason, RunState};
fn hit(id: WeaponId, damage: f64) -> Record {
    Record::Moment(Moment::Hit {
        x: 0.0,
        y: 0.0,
        lethal: false,
        damage,
        song: 0.0,
        critical: true,
        foe: 0,
        source: "move",
        weight: "heavy",
        what: id.as_str().into(),
        dir: 0.0,
        intensity: 1.0,
    })
}
#[test]
fn records_count_logical_damage_once_keep_rank_time_and_close_base_at_evolution() {
    let mut view = LiveView::default();
    let mut inv = Inventory::default();
    inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
    view.apply(0.0, &Record::Inventory(inv));
    view.apply(0.0, &Record::Run(RunState::default()));
    view.apply(
        2.0,
        &Record::Run(RunState {
            elapsed: 2.0,
            ..RunState::default()
        }),
    );
    inv.weapons.upgrade(WeaponId::Garlic, 8).unwrap();
    view.apply(2.0, &Record::Inventory(inv));
    view.apply(
        4.0,
        &Record::Run(RunState {
            elapsed: 4.0,
            ..RunState::default()
        }),
    );
    inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
    view.apply(4.0, &Record::Inventory(inv));
    let logical = hit(WeaponId::Sword, 2.0);
    view.apply(4.0, &logical);
    let Record::Moment(moment) = logical else {
        unreachable!()
    };
    view.apply(
        4.1,
        &Record::Moment(Moment::EnemyImpact {
            hit: Box::new(moment),
        }),
    );
    // A long menu advances song time but leaves the held run clock at4.
    view.apply(
        14.0,
        &Record::Run(RunState {
            song: 14.0,
            elapsed: 4.0,
            frozen: Some(FreezeReason::Paused),
            ..RunState::default()
        }),
    );
    inv.weapons
        .replace(WeaponId::Sword, WeaponId::BloodyTear)
        .unwrap();
    view.apply(14.0, &Record::Inventory(inv));
    view.apply(
        20.0,
        &Record::Run(RunState {
            song: 20.0,
            elapsed: 10.0,
            ..RunState::default()
        }),
    );
    // An already emitted base projectile remains attributed to its captured identity.
    view.apply(20.0, &hit(WeaponId::Sword, 3.0));
    view.apply(20.0, &hit(WeaponId::BloodyTear, 6.0));
    let Record::Moment(mut finale) = hit(WeaponId::Sword, 1000.0) else {
        unreachable!()
    };
    if let Moment::Hit { what, .. } = &mut finale {
        *what = "finale-blast".into();
    }
    view.apply(20.0, &Record::Moment(finale));
    inv.weapons.upgrade(WeaponId::MagicWand, 8).unwrap();
    view.apply(20.0, &Record::Inventory(inv));
    let rows = view.recap.rows(10.0);
    let row = |id| *rows.iter().find(|r| r.id == id).unwrap();
    assert_eq!(
        row(WeaponId::Sword),
        Row {
            id: WeaponId::Sword,
            damage: 5.0,
            held_s: 4.0,
            dps: 1.25
        }
    );
    assert_eq!(
        row(WeaponId::BloodyTear),
        Row {
            id: WeaponId::BloodyTear,
            damage: 6.0,
            held_s: 6.0,
            dps: 1.0
        }
    );
    assert_eq!(row(WeaponId::Garlic).held_s, 8.0);
    assert_eq!(row(WeaponId::Garlic).damage, 0.0);
    assert_eq!(row(WeaponId::MagicWand).held_s, 0.0);
    assert_eq!(row(WeaponId::MagicWand).dps, 0.0);
}

#[test]
fn actual_sim_fates_match_live_recap_for_all_five_evolutions_through_pause() {
    use crate::live::Engine;
    use sim::{
        content::level::load_level,
        content::{SourceFile, load_content},
        engine::{Sim, SimInput},
        movement::InputKey,
        player::{KeyEvent, KeyEventKind},
        recorder::LogRecorder,
        schedule::reactive_song_map,
    };
    use std::{cell::RefCell, path::Path, rc::Rc};
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let source = |p: &Path| SourceFile {
        name: p.to_string_lossy().into(),
        text: std::fs::read_to_string(p).unwrap(),
    };
    let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
        .unwrap()
        .map(|e| e.unwrap().path())
        .filter(|p| p.extension().is_some_and(|e| e == "yaml"))
        .collect();
    paths.sort();
    let mut lib = load_content(&paths.iter().map(|p| source(p)).collect::<Vec<_>>()).unwrap();
    let foe = lib.enemies.get_mut("vs-skeleton").unwrap();
    foe.health = 1000.0;
    let stats = foe.survivors.as_mut().unwrap();
    stats.xp = 0.0;
    stats.speed = 700.0;
    stats.knockback = 0.0;
    // Isolate producer attribution: durable, fast, unrecoiling actors; not a balance fixture.
    let mut level = source(&root.join("levels/survivors.yaml"));
    let old = level
        .text
        .lines()
        .find(|l| l.trim().starts_with("- {\"minute\":0,"))
        .unwrap()
        .to_string();
    let row = serde_json::json!({"minute":0,"minimum":40,"frequency":1000,"enemies":["vs-skeleton"],"bosses":[],"events":[],"treasure":[]});
    level.text = level.text.replace(&old, &format!("    - {row}"));
    let setup = |sim: &mut Sim| {
        sim.disable_hero_damage_for_check();
        for id in [
            WeaponId::BloodyTear,
            WeaponId::SoulEater,
            WeaponId::HolyWand,
            WeaponId::UnholyVespers,
            WeaponId::ThunderLoop,
        ] {
            let def = sim::survivors::weapon_catalog().get(id);
            let base = def.evolution_of.unwrap();
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
            sim.learn_survivor_passive(def.requires.unwrap()).unwrap();
            sim.evolve_survivor_weapon(id).unwrap();
        }
        sim.freeze_at(3.0, FreezeReason::Paused);
    };
    let mut engine = Engine::new_with_setup(
        &level,
        &lib,
        &indexmap::IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
        setup,
    )
    .unwrap();
    let loaded = load_level(&level, &lib, &reactive_song_map(f64::INFINITY), None).unwrap();
    let mut input = SimInput::new(loaded, &lib, &indexmap::IndexMap::new(), 0.2, true);
    input.zen = true;
    let producer = Rc::new(RefCell::new(LogRecorder::default()));
    let mut sim = Sim::new(
        input,
        Rc::new(stream::StreamLog::default()),
        Box::new(producer.clone()),
        None,
    );
    setup(&mut sim);
    let log = stream::StreamLog {
        beats: (0..50)
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
        ..Default::default()
    };
    let mut replay = stream::Replay::new(log.clone());
    let mut source_replay = stream::Replay::new(log);
    for (s, kind) in [(5.0, KeyEventKind::Press), (5.01, KeyEventKind::Release)] {
        engine.key(s, InputKey::Pause, kind == KeyEventKind::Press);
        sim.key(KeyEvent {
            s,
            key: InputKey::Pause,
            kind,
            id: None,
        });
    }
    for frame in 1..=2400 {
        let now = f64::from(frame) / 120.0;
        engine.feed(|l| replay.feed_to(l, now));
        source_replay.feed_to(sim.log_mut(), now);
        engine.frame(now, 0.0);
        sim.advance_to(now);
    }
    let rec = producer.borrow();
    let hits: std::collections::HashMap<u64, WeaponId> = rec
        .hits
        .iter()
        .filter_map(|h| {
            Some((
                h["id"].as_f64()? as u64,
                WeaponId::from_name(h["what"].as_str()?)?,
            ))
        })
        .collect();
    let mut expected = BTreeMap::<WeaponId, f64>::new();
    let mut fates = 0;
    for enemy in &rec.enemies {
        for fate in &enemy.fates {
            if let Some(&id) = hits.get(&(fate.hit as u64)) {
                *expected.entry(id).or_default() += fate.damage;
                fates += 1;
            }
        }
    }
    let view = engine.view.borrow();
    let elapsed = view.run.unwrap().elapsed;
    assert!((elapsed - sim.run_s()).abs() < 1e-8);
    assert!(elapsed < 18.0);
    for row in view.recap.rows(elapsed) {
        let actual = expected.get(&row.id).copied().unwrap_or(0.0);
        assert!(
            (row.damage - actual).abs() < 1e-7,
            "{} projection{} actual{actual}",
            row.id.as_str(),
            row.damage
        );
        if sim::survivors::weapon_catalog()
            .get(row.id)
            .evolution_of
            .is_some()
        {
            assert!(row.damage > 0.0, "{} actually hit enemies", row.id.as_str());
            assert!((row.held_s - elapsed).abs() < 1e-8);
            assert!((row.dps - actual / elapsed).abs() < 1e-7);
        }
    }
    println!(
        "SP4 producer: {fates} actual applied fates, run{elapsed:.9}, per-weapon {:?}",
        view.recap.rows(elapsed)
    );
}

#[test]
fn completed_fatal_summary_is_identical_across_settlement_windows() {
    use crate::live::Engine;
    use sim::{
        content::{SourceFile, load_content},
        movement::InputKey,
    };
    use std::path::Path;
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let source = |p: &Path| SourceFile {
        name: p.to_string_lossy().into(),
        text: std::fs::read_to_string(p).unwrap(),
    };
    let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
        .unwrap()
        .map(|e| e.unwrap().path())
        .filter(|p| p.extension().is_some_and(|e| e == "yaml"))
        .collect();
    paths.sort();
    let mut lib = load_content(&paths.iter().map(|p| source(p)).collect::<Vec<_>>()).unwrap();
    let foe = lib.enemies.get_mut("vs-skeleton").unwrap();
    foe.health = 1000.0;
    let stats = foe.survivors.as_mut().unwrap();
    stats.xp = 0.0;
    stats.speed = 1200.0;
    stats.power = 10000.0;
    stats.knockback = 0.0;
    let mut level = source(&root.join("levels/survivors.yaml"));
    let old = level
        .text
        .lines()
        .find(|l| l.trim().starts_with("- {\"minute\":0,"))
        .unwrap()
        .to_string();
    let row = serde_json::json!({"minute":0,"minimum":40,"frequency":1000,"enemies":["vs-skeleton"],"bosses":[],"events":[],"treasure":[]});
    level.text = level.text.replace(&old, &format!("    - {row}"));
    let log = stream::StreamLog {
        beats: (0..30)
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
        ticks: (0..=1200)
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
    let run = |settle| {
        let mut engine = Engine::new_with_setup(
            &level,
            &lib,
            &indexmap::IndexMap::new(),
            0.2,
            true,
            true,
            f64::INFINITY,
            |sim| {
                for id in [WeaponId::Sword] {
                    while sim
                        .survivor_inventory()
                        .unwrap()
                        .weapons
                        .rank(id)
                        .unwrap_or(0)
                        < 8
                    {
                        sim.learn_survivor_weapon(id).unwrap();
                    }
                }
                sim.freeze_at(0.2, FreezeReason::Paused);
            },
        )
        .unwrap();
        engine.settle_s = settle;
        engine.key(1.0, InputKey::Pause, true);
        engine.key(1.01, InputKey::Pause, false);
        let mut replay = stream::Replay::new(log.clone());
        for frame in 1..=3600 {
            let now = f64::from(frame) / 120.0;
            engine.feed(|l| replay.feed_to(l, now));
            engine.frame(now, 0.237);
            if engine.fallen() {
                let view = engine.view.borrow();
                let summary = Summary::from_view(&view).unwrap();
                let raw_damage = view
                    .recap
                    .rows(view.run.unwrap().elapsed)
                    .iter()
                    .map(|r| r.damage)
                    .sum::<f64>();
                return (summary, view.run.unwrap().elapsed, raw_damage);
            }
        }
        panic!(
            "fixture did not fall: run {:?}, health {}, enemies {}",
            engine.view.borrow().run,
            engine.view.borrow().health,
            engine.view.borrow().foes.len()
        );
    };
    let (fast, fast_clock, _) = run(0.04);
    let (slow, slow_clock, slow_raw) = run(0.4);
    assert_eq!(
        fast, slow,
        "confirmation delay cannot add post-death time, damage, kills or levels"
    );
    assert!(slow_clock > fast_clock);
    assert!(slow_clock > slow.elapsed);
    let damage = slow.rows.iter().map(|r| r.damage).sum::<f64>();
    assert!(
        slow_raw > damage,
        "the regression actually crosses later combat records"
    );
    println!(
        "SP4 fatal boundary: completed run{} damage{}; displayed clocks{fast_clock}/{slow_clock}, later raw damage{slow_raw}, identical summaries at settle0.04/0.4 lead0.237 across pause",
        slow.elapsed, damage
    );
}
