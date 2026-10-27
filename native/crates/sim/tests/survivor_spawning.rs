//! Source contracts, admission timing and inheritance independent of the presentation.
#![allow(clippy::float_cmp)]
mod common;
use sim::content::level::load_level;
use sim::schedule::reactive_song_map;
use sim::survivors::{SpawnMinute, Spawner};
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
fn deferred_bosses_wait_for_room_then_the_next_grid_step() {
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
    assert_eq!(state.next_stop(&schedule, 0.1, 0.0), 0.25);
    assert!(!state.bosses_due(0.249, &grid, true));
    assert!(state.bosses_due(0.25, &grid, true));
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
