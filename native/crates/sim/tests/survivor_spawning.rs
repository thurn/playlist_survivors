//! Source contracts, admission timing and inheritance independent of the presentation.
#![allow(clippy::float_cmp)]
mod common;
use sim::content::level::load_level;
use sim::schedule::reactive_song_map;
use sim::survivors::{SURVIVOR_CAP, SpawnMinute, Spawner, WIDEN_EASE_S, widen_at};
use stream::Beat;
fn schedule() -> Vec<SpawnMinute> {
    load_level(
        &common::source("levels/survivors.yaml"),
        common::library(),
        &reactive_song_map(f64::INFINITY),
        None,
    )
    .unwrap()
    .survivor_schedule
}
fn role(name: &str) -> String {
    format!("vs-{}", name.to_lowercase().replace('_', "-"))
}
#[test]
fn all_31_schedule_rows_match_the_documented_source_including_duplicate_roles() {
    let schedule = schedule();
    assert_eq!(schedule.len(), 31);
    let appendix = common::source("docs/survivors/appendix-damage-vs-health.md").text;
    let mut rows = 0;
    for line in appendix.lines() {
        let cols: Vec<_> = line.split('|').map(str::trim).collect();
        if cols.len() != 8
            || cols[1].parse::<usize>().is_err()
            || cols[2].parse::<usize>().is_err()
            || cols[3].parse::<f64>().is_err()
        {
            continue;
        }
        let i = cols[1].parse::<usize>().unwrap();
        if i > 30 {
            continue;
        }
        let row = &schedule[i];
        let names = |text: &str| {
            text.split(',')
                .filter_map(|p| p.split_once(" (").map(|(id, _)| role(id.trim())))
                .collect::<Vec<_>>()
        };
        assert_eq!(row.enemies.as_ref().unwrap(), &names(cols[4]));
        assert_eq!(row.minimum, Some(cols[2].parse().unwrap()));
        assert_eq!(row.frequency, Some(cols[3].parse().unwrap()));
        assert_eq!(row.bosses, if i == 30 { vec![] } else { names(cols[5]) });
        rows += 1;
    }
    assert_eq!(rows, 31);
    assert_eq!(schedule.iter().map(|s| s.events.len()).sum::<usize>(), 21);
    assert_eq!(schedule[1].treasure[0].fixed_prizes, ["AMOUNT"]);
    assert_eq!(schedule[25].treasure[0].chances, [3.0, 10.0, 100.0]);
}
#[test]
fn raw_ticks_snap_to_the_next_known_eighth_and_minute_changes_inherit_only_population() {
    let mut schedule = schedule();
    schedule.truncate(2);
    schedule[1].enemies = None;
    schedule[1].minimum = None;
    schedule[1].frequency = None;
    let beats = [
        Beat {
            t: 0.0,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        },
        Beat {
            t: 0.5,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        },
    ];
    let mut state = Spawner::default();
    state.prepare(&schedule, 0.0, 0.0, &beats);
    assert_eq!(state.take_due(0.0).len(), 1);
    // Move this test tick off-grid without changing the population source row.
    schedule[0].frequency = Some(1100.0);
    let mut state = Spawner::default();
    state.prepare(&schedule, 0.0, 0.0, &beats);
    state.take_due(0.0);
    let grid: Vec<_> = (0..130)
        .map(|k| Beat {
            t: f64::from(k) * 0.5,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        })
        .collect();
    state.prepare(&schedule, 1.1, 1.1, &grid);
    assert!(state.take_due(1.249).is_empty());
    assert_eq!(state.take_due(1.25).len(), 1);
    for k in 2..=54 {
        let t = f64::from(k) * 1.1;
        state.prepare(&schedule, t, t, &grid);
        state.take_due(t + 0.25);
    }
    state.prepare(&schedule, 60.0, 60.0, &grid);
    let due = state.take_due(60.0);
    assert_eq!(state.minute, Some(1));
    assert_eq!(state.roster.minimum, 15);
    assert_eq!(state.roster.enemies, ["vs-bat3"]);
    assert_eq!(
        due.iter()
            .flat_map(|d| &d.bosses)
            .map(|b| &b.role)
            .collect::<Vec<_>>(),
        [&"vs-bat4".to_owned()]
    );
    assert!(state.take_due(60.0).is_empty());
}
#[test]
fn future_grid_evidence_cannot_delay_a_raw_tick_and_a_freeze_holds_admission() {
    let mut schedule = schedule();
    schedule.truncate(1);
    schedule[0].frequency = Some(1100.0);
    let future: Vec<_> = (0..8)
        .map(|k| Beat {
            t: f64::from(k) * 0.5,
            known_at: 100.0,
            period: 0.5,
            evidence: 1.0,
        })
        .collect();
    let mut state = Spawner::default();
    state.prepare(&schedule, 0.0, 0.0, &future);
    assert_eq!(state.take_due(0.0).len(), 1);
    state.prepare(&schedule, 1.1, 1.1, &future);
    assert_eq!(state.take_due(1.1).len(), 1);
    let grid: Vec<_> = future
        .iter()
        .copied()
        .map(|b| Beat { known_at: 0.0, ..b })
        .collect();
    state.prepare(&schedule, 2.2, 2.2, &grid);
    state.hold();
    assert!(state.take_due(2.5).is_empty());
    state.resume(2.2, 10.0, &[]);
    assert_eq!(state.take_due(10.0).len(), 1);
}

#[test]
fn enemy_roles_match_documented_health_speed_damage_xp_and_scaling() {
    let appendix = common::source("docs/survivors/appendix-damage-vs-health.md").text;
    let table = appendix
        .split("### The enemies")
        .nth(1)
        .unwrap()
        .split("## Player damage")
        .next()
        .unwrap();
    let mut checked = 0;
    for line in table.lines() {
        let cols: Vec<_> = line.split('|').map(str::trim).collect();
        if cols.len() != 9 || cols[3].parse::<f64>().is_err() || cols[1] == "BOSS_XLDEATH" {
            continue;
        }
        for id in cols[1].split(',').map(str::trim) {
            let def = &common::library().enemies[&role(id)];
            let stats = def.survivors.unwrap();
            assert_eq!(def.health, cols[3].parse::<f64>().unwrap());
            assert_eq!(stats.level_scaled, cols[4] == "yes");
            assert_eq!(stats.power, cols[5].parse::<f64>().unwrap());
            assert_eq!(stats.speed, cols[6].parse::<f64>().unwrap());
            assert_eq!(stats.xp, cols[7].parse::<f64>().unwrap());
            checked += 1;
        }
    }
    assert_eq!(checked, 20);
}
#[test]
fn a_single_committed_future_beat_is_a_grid_step() {
    let schedule = schedule();
    let mut state = Spawner::default();
    state.prepare(
        &schedule,
        0.0,
        0.0,
        &[Beat {
            t: 0.3,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        }],
    );
    assert!(state.take_due(0.299).is_empty());
    assert_eq!(state.take_due(0.3).len(), 1);
}

#[test]
fn deferred_bosses_wait_for_room_then_the_next_native_downbeat() {
    let schedule = schedule();
    let mut state = Spawner::default();
    state.waiting_bosses.push(sim::survivors::ScheduledBoss {
        role: "vs-bat4".into(),
        minute: 1,
    });
    let grid = [
        Beat {
            t: 0.0,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        },
        Beat {
            t: 0.5,
            known_at: 0.0,
            period: 0.5,
            evidence: 1.0,
        },
    ];
    assert!(!state.bosses_due(0.1, &grid, false));
    assert!(!state.bosses_due(0.1, &grid, true));
    state.prepare(&schedule, 0.1, 0.1, &grid);
    state.take_due(0.25);
    assert_eq!(state.next_stop(&schedule, 0.1, 0.0), 1.0);
    assert!(!state.bosses_due(0.999, &grid, true));
    assert!(state.bosses_due(1.0, &grid, true));
}

#[test]
fn enemy_knockback_fields_match_the_feel_appendix() {
    let text = common::source("docs/survivors/appendix-feel.md").text;
    let mut checked = 0;
    for line in text.lines() {
        let cols: Vec<_> = line.split('|').map(str::trim).collect();
        if cols.len() != 11 || cols[2].parse::<f64>().is_err() {
            continue;
        }
        let Some(def) = common::library().enemies.get(&role(cols[1])) else {
            continue;
        };
        let stats = def.survivors.unwrap();
        assert_eq!(stats.knockback, cols[4].parse::<f64>().unwrap());
        assert_eq!(stats.max_knockback, cols[5].parse::<f64>().unwrap());
        assert_eq!(stats.res_debuffs, cols[7].parse::<f64>().unwrap());
        checked += 1;
    }
    assert_eq!(checked, 20);
}

#[test]
fn treasure_validation_rejects_invalid_scripted_tiers_and_chance_shapes() {
    let source = common::source("levels/survivors.yaml");
    for replacement in [
        "\"chances\":[0,30]",
        "\"chances\":[0,0,30],\"tier\":0",
        "\"chances\":[0,0,30],\"tier\":4",
        "\"chances\":[0,0,30],\"tier\":1.5",
    ] {
        let mut bad = source.clone();
        bad.text = bad.text.replacen("\"chances\":[0,0,30]", replacement, 1);
        assert!(
            load_level(
                &bad,
                common::library(),
                &reactive_song_map(f64::INFINITY),
                None
            )
            .is_err(),
            "{replacement}"
        );
    }
}

#[test]
fn swarm_roles_match_the_curated_event_only_combat_rows() {
    let doc = common::source("docs/survivors/appendix-feel.md").text;
    let table = doc
        .split("### Swarm combat fields")
        .nth(1)
        .unwrap()
        .split("## Mad Forest events")
        .next()
        .unwrap();
    let mut checked = 0;
    for line in table.lines() {
        let cols: Vec<_> = line.split('|').map(str::trim).collect();
        if cols.len() != 9 || !["BATSWARM", "GHOSTSWARM"].contains(&cols[1]) {
            continue;
        }
        let id = if cols[1] == "BATSWARM" {
            "vs-bat-swarm"
        } else {
            "vs-ghost-swarm"
        };
        let d = &common::library().enemies[id];
        let stats = d.survivors.unwrap();
        assert_eq!(d.health, cols[2].parse::<f64>().unwrap());
        assert_eq!(stats.level_scaled, cols[3] == "yes");
        assert_eq!(stats.power, cols[4].parse::<f64>().unwrap());
        assert_eq!(stats.speed, cols[5].parse::<f64>().unwrap());
        assert_eq!(stats.xp, cols[6].parse::<f64>().unwrap());
        assert_eq!(cols[7], "yes");
        checked += 1;
    }
    assert_eq!(checked, 2);
}
#[test]
fn unknown_survivor_event_kinds_are_rejected() {
    let mut src = common::source("levels/survivors.yaml");
    src.text = src.text.replace("BAT_SWARM", "UNKNOWN_SWARM");
    assert!(
        load_level(
            &src,
            common::library(),
            &reactive_song_map(f64::INFINITY),
            None
        )
        .is_err()
    );
}

fn load(src: &sim::content::SourceFile) -> Result<sim::content::level::LevelScript, String> {
    load_level(
        src,
        common::library(),
        &reactive_song_map(f64::INFINITY),
        None,
    )
    .map_err(|e| e.to_string())
}

#[test]
fn song_clock_rows_begin_on_the_song_whatever_the_run_clock_says() {
    let level = load(&common::source("levels/action-showcase.yaml")).unwrap();
    let schedule = &level.survivor_schedule[..2];
    let groove = schedule[1].song_s.unwrap();
    let mut state = Spawner::default();
    state.prepare(schedule, 0.0, 0.0, &[]);
    assert_eq!(state.minute, Some(0));
    assert_eq!(state.take_due(0.0).len(), 1);
    // Freezes have held the run clock 100 s behind the song: the next stop is still the row's
    // song time (a run-clock row would wait for run minute 1).
    assert_eq!(state.next_stop(schedule, groove - 0.1, 100.0), groove);
    state.prepare(schedule, 0.5, groove - 0.5, &[]);
    assert_eq!(state.minute, Some(0));
    state.prepare(schedule, 1.0, groove, &[]);
    assert_eq!(state.minute, Some(1));
}

#[test]
fn the_view_widens_from_each_song_clock_row_eased_and_never_on_the_run_clock() {
    let level = load(&common::source("levels/action-showcase.yaml")).unwrap();
    let rows = &level.survivor_schedule;
    let peak = rows.iter().position(|r| r.widen == Some(1.35)).unwrap();
    let (at, before) = (rows[peak].song_s.unwrap(), rows[peak - 1].widen.unwrap());
    assert_eq!(widen_at(rows, 0.0), 1.0);
    assert_eq!(widen_at(rows, at - 0.01), before);
    assert_eq!(widen_at(rows, at), before);
    let middle = widen_at(rows, at + WIDEN_EASE_S / 2.0);
    assert!((middle - before.midpoint(1.35)).abs() < 1e-9, "{middle}");
    assert_eq!(widen_at(rows, at + WIDEN_EASE_S), 1.35);
    // A run-clock schedule (every existing level) keeps the level's own camera.
    assert!((0..1800).all(|s| widen_at(&schedule(), f64::from(s)) == 1.0));
}

#[test]
fn the_cap_defaults_to_survivor_cap_and_a_level_may_raise_it() {
    let survivors = load(&common::source("levels/survivors.yaml")).unwrap();
    assert_eq!(survivors.survivors.unwrap().cap(), SURVIVOR_CAP);
    let showcase = load(&common::source("levels/action-showcase.yaml")).unwrap();
    assert!(showcase.survivors.unwrap().cap() >= 400);
}

#[test]
fn song_clock_schedules_and_caps_are_validated() {
    let showcase = common::source("levels/action-showcase.yaml");
    let survivors = common::source("levels/survivors.yaml");
    let edit = |src: &sim::content::SourceFile, from: &str, to: &str| {
        let mut bad = src.clone();
        assert!(bad.text.contains(from), "{from}");
        bad.text = bad.text.replacen(from, to, 1);
        bad
    };
    for (bad, why) in [
        // songS on some rows only, or not rising.
        (edit(&showcase, "\"songS\":5.4,", ""), "songS"),
        (edit(&showcase, "\"songS\":5.4,", "\"songS\":0,"), "songS"),
        // widen without song-clock rows.
        (
            edit(&survivors, "{\"minute\":0,", "{\"minute\":0,\"widen\":1.2,"),
            "widen",
        ),
        // A crowd above the cap: the default 300, or the level's own.
        (
            edit(&survivors, "\"minimum\":30,", "\"minimum\":301,"),
            "minimum above the cap 300",
        ),
        (
            edit(&showcase, "cap: 1000", "cap: 300"),
            "minimum above the cap",
        ),
        (edit(&showcase, "cap: 1000", "cap: 2001"), "cap"),
    ] {
        let e = load(&bad).expect_err(why);
        assert!(e.contains(why), "{why}: {e}");
    }
}
