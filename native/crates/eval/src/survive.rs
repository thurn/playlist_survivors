//! Causal fast driving through Engine, using the existing Player interface.
use crate::live::Engine;
use crate::session::{Arrival, rebase_append};
use serde::Serialize;
use sim::player::{KeyEventKind, Player};
use sim::survivors::{Inventory, RunState};

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
    pub id: usize,
    pub origin_minute: usize,
    pub tier: u8,
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
/// Sample the inventory once when a physical chest opens. A music/level freeze
/// may temporarily take precedence over TREASURE without opening another chest.
fn sample_chest(points: &mut Vec<ChestPoint>, run: RunState, inv: Inventory) {
    let Some(chest) = run.roulette else { return };
    if points.last().is_some_and(|point| point.id == chest.id) {
        return;
    }
    points.push(ChestPoint {
        id: chest.id,
        origin_minute: chest.minute,
        tier: chest.tier,
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
            sample_chest(&mut r.weapons_at_chests, run, inv);
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

#[cfg(test)]
mod tests {
    use super::*;
    use sim::survivors::{FreezeReason, Roulette, WeaponId};

    #[test]
    fn interrupted_roulette_is_one_chest_with_its_origin_and_pre_reward_ranks() {
        let mut points = Vec::new();
        let mut inv = Inventory::default();
        inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        let mut run = RunState {
            elapsed: 725.0,
            frozen: Some(FreezeReason::Chest),
            roulette: Some(Roulette {
                id: 7,
                tier: 1,
                minute: 10,
                opened: 725.0,
                tick: 0,
                next_song: 726.0,
                next_beat: None,
                started: false,
                landed: None,
                rewards: [None; 5],
            }),
            ..RunState::default()
        };
        sample_chest(&mut points, run, inv);
        inv.weapons.upgrade(WeaponId::Sword, 8).unwrap();
        for reason in [
            FreezeReason::MusicStopped,
            FreezeReason::LevelUp,
            FreezeReason::Chest,
        ] {
            run.frozen = Some(reason);
            sample_chest(&mut points, run, inv);
        }
        run.roulette = None;
        sample_chest(&mut points, run, inv);
        assert_eq!(points.len(), 1);
        assert_eq!(
            (points[0].id, points[0].origin_minute, points[0].tier),
            (7, 10, 1)
        );
        assert_eq!(points[0].weapons[0].rank, 1);
        // A second physical chest can open at the same held run time.
        run.roulette = Some(Roulette {
            id: 8,
            tier: 3,
            minute: 15,
            opened: 730.0,
            tick: 0,
            next_song: 731.0,
            next_beat: None,
            started: false,
            landed: None,
            rewards: [None; 5],
        });
        sample_chest(&mut points, run, inv);
        assert_eq!(points.len(), 2);
        assert_eq!(
            (
                points[1].origin_minute,
                points[1].tier,
                points[1].weapons[0].rank
            ),
            (15, 3, 2)
        );
    }
}
