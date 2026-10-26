//! Causal fast driving through Engine, using the existing Player interface.
use crate::live::Engine;
use crate::session::{Arrival, rebase_append};
use serde::Serialize;
use sim::player::{KeyEventKind, Player};
use sim::survivors::FreezeReason;

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HealthPoint {
    pub run_s: f64,
    pub hp_share: f64,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LevelPoint {
    pub minute: usize,
    pub level: usize,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WeaponRank {
    pub weapon: String,
    pub rank: u8,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChestPoint {
    pub run_s: f64,
    pub weapons: Vec<WeaponRank>,
}
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Report {
    pub song_s: f64,
    pub survival_s: f64,
    pub lowest_hp_share: f64,
    pub hp: Vec<HealthPoint>,
    pub levels_by_minute: Vec<LevelPoint>,
    pub weapons_at_chests: Vec<ChestPoint>,
    pub evolutions: Vec<String>,
    pub peak_enemies: usize,
    pub level: usize,
    pub kills: usize,
    pub fallen: bool,
    pub done: bool,
    pub settled_hash: String,
    pub settled_records: usize,
}
/// Match replay's 120 Hz grid: feed only arrived entries, then apply observed-state
/// decisions at this frame or later. run_seconds uses the held run clock.
pub fn drive(
    engine: &mut Engine,
    arrivals: &[Arrival],
    player: &mut dyn Player,
    until_song: f64,
    run_seconds: f64,
) -> Report {
    let mut r = Report {
        song_s: 0.0,
        survival_s: 0.0,
        lowest_hp_share: 1.0,
        hp: Vec::new(),
        levels_by_minute: Vec::new(),
        weapons_at_chests: Vec::new(),
        evolutions: Vec::new(),
        peak_enemies: 0,
        level: 1,
        kills: 0,
        fallen: false,
        done: false,
        settled_hash: String::new(),
        settled_records: 0,
    };
    let mut next = 0;
    let mut decision = player.next_decision(-1.0);
    let mut last_second = None;
    let mut last_minute = None;
    let mut chest = false;
    for frame in 1..=((until_song * 120.0).ceil() as u64) {
        let now = frame as f64 / 120.0;
        let start = next;
        while next < arrivals.len() && arrivals[next].arrived <= now {
            next += 1;
        }
        if next > start {
            engine.feed(|log| {
                for a in &arrivals[start..next] {
                    rebase_append(log, &a.entry, 0.0);
                }
            });
        }
        if decision <= now {
            for key in player.decide(&engine.sim.observe()) {
                engine.key(key.s.max(now), key.key, key.kind == KeyEventKind::Press);
            }
            decision = player.next_decision(now + 1e-9);
        }
        engine.frame(now, 0.0);
        let view = engine.view.borrow();
        let run = view.run.unwrap_or_default();
        r.song_s = now;
        r.survival_s = run.elapsed;
        r.lowest_hp_share = r.lowest_hp_share.min(view.health);
        r.peak_enemies = r.peak_enemies.max(view.alive().count());
        r.level = run.level.max(1);
        r.kills = view.kills;
        let second = run.elapsed.floor() as u64;
        if last_second != Some(second) {
            r.hp.push(HealthPoint {
                run_s: run.elapsed,
                hp_share: view.health,
            });
            last_second = Some(second);
        }
        let minute = (run.elapsed / 60.0).floor() as usize;
        if last_minute != Some(minute) {
            r.levels_by_minute.push(LevelPoint {
                minute,
                level: r.level,
            });
            last_minute = Some(minute);
        }
        if let Some(inv) = view.inventory {
            if run.frozen == Some(FreezeReason::Chest) && !chest {
                r.weapons_at_chests.push(ChestPoint {
                    run_s: run.elapsed,
                    weapons: inv
                        .weapons
                        .iter()
                        .map(|w| WeaponRank {
                            weapon: w.id.as_str().into(),
                            rank: w.rank,
                        })
                        .collect(),
                });
            }
            for w in inv.weapons.iter() {
                let name = w.id.as_str();
                if matches!(
                    name,
                    "bloody-tear" | "soul-eater" | "holy-wand" | "unholy-vespers" | "thunder-loop"
                ) && !r.evolutions.iter().any(|w| w == name)
                {
                    r.evolutions.push(name.into());
                }
            }
        }
        chest = run.frozen == Some(FreezeReason::Chest);
        drop(view);
        engine.view.borrow_mut().take_moments();
        if engine.done() || engine.fallen() || (run_seconds < 1800.0 && run.elapsed >= run_seconds)
        {
            break;
        }
    }
    r.fallen = engine.fallen();
    r.done = engine.done();
    r.settled_hash = format!("{:016x}", engine.settled_hash);
    r.settled_records = engine.settled_count;
    r
}
