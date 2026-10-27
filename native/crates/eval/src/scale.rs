//! Explicit synthetic density fixture for offline checks; never used by a play session.
use crate::live::Engine;
use sim::content::{ContentLibrary, SourceFile};

/// Constant 300-enemy population of minute 25's XLFlower roster; effectively unkillable
/// enemies isolate age from density. Faster BAT1 overload is retained separately in evidence.
/// All musical input still passes through normal ingestion, freezes, strides and an explicit legal six-slot weapon loadout.
pub fn density_engine(level: &SourceFile, lib: &ContentLibrary) -> Result<Engine, String> {
    configured_density_engine(level, lib, sim::engine::Sim::seed_scale_check, false)
}
/// Alternative six-slot build with chain and trail, preserving the same population fixture.
pub fn restored_density_engine(level: &SourceFile, lib: &ContentLibrary) -> Result<Engine, String> {
    configured_density_engine(
        level,
        lib,
        sim::engine::Sim::seed_restored_scale_check,
        false,
    )
}
/// SP5 final evolved loadout; the original base-loadout release fixture remains explicit.
pub fn evolved_density_engine(level: &SourceFile, lib: &ContentLibrary) -> Result<Engine, String> {
    configured_density_engine(
        level,
        lib,
        sim::engine::Sim::seed_evolved_scale_check,
        false,
    )
}
/// Actual deaths at the same minute-25 roster, with labelled low HP/zero XP.
pub fn evolved_combat_engine(level: &SourceFile, lib: &ContentLibrary) -> Result<Engine, String> {
    configured_density_engine(level, lib, sim::engine::Sim::seed_evolved_scale_check, true)
}
fn configured_density_engine(
    level: &SourceFile,
    lib: &ContentLibrary,
    setup: impl FnOnce(&mut sim::engine::Sim),
    killable: bool,
) -> Result<Engine, String> {
    let mut rows = 0;
    let text = level.text.lines().map(|line| {
        if let Some(minute) = line.trim().strip_prefix("- {\"minute\":")
            .and_then(|row| row.split(',').next()).and_then(|v| v.parse::<u64>().ok()) {
            rows += 1;
            return format!("    - {{\"minute\":{minute},\"minimum\":300,\"frequency\":1000,\"enemies\":[\"vs-xlflower\"],\"bosses\":[],\"events\":[],\"treasure\":[]}}");
        }
        line.to_owned()
    }).collect::<Vec<_>>().join("\n");
    if rows != 31 {
        return Err(format!(
            "density fixture expected 31 schedule rows, found {rows}"
        ));
    }
    let mut content = lib.clone();
    for enemy in content.enemies.values_mut() {
        enemy.health = if killable { 40.0 } else { 1e12 };
        if killable && let Some(stats) = enemy.survivors.as_mut() {
            stats.xp = 0.0;
        }
    }
    let engine = Engine::new_with_setup(
        &SourceFile {
            name: "density-check.yaml".into(),
            text,
        },
        &content,
        &indexmap::IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
        setup,
    )
    .map_err(|e| format!("{e:?}"))?;
    Ok(engine)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn density_setup_and_cached_view_survive_late_input_with_all_gems() {
        #[derive(serde::Deserialize)]
        struct Fixture {
            arrivals: Vec<crate::session::Arrival>,
        }
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
        let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
            .unwrap()
            .map(|p| p.unwrap().path())
            .filter(|p| p.extension().is_some_and(|s| s == "yaml"))
            .collect();
        paths.sort();
        let sources: Vec<_> = paths
            .iter()
            .map(|p| SourceFile {
                name: p.display().to_string(),
                text: std::fs::read_to_string(p).unwrap(),
            })
            .collect();
        let lib = sim::content::load_content(&sources).unwrap();
        let level = SourceFile {
            name: "survivors.yaml".into(),
            text: std::fs::read_to_string(root.join("levels/survivors.yaml")).unwrap(),
        };
        let mut engine = density_engine(&level, &lib).unwrap();
        assert_eq!(engine.sim.storage().gems, 400);
        engine.frame(0.05, 0.2);
        engine.key(0.01, sim::movement::InputKey::Space, true);
        engine.frame(0.1, 0.2);
        assert!(engine.stats.rollbacks > 0);
        assert_eq!(engine.sim.storage().gems, 400);
        assert_eq!(engine.view.borrow().gems.len(), 400);
        let inventory = engine.sim.survivor_inventory().unwrap();
        let built: Vec<_> = sim::survivors::weapon_catalog()
            .weapons
            .values()
            .filter(|d| d.implemented && d.evolution_of.is_none())
            .take(sim::survivors::ITEM_SLOTS)
            .collect();
        assert_eq!(engine.view.borrow().inventory, Some(inventory));
        assert_eq!(inventory.weapons.iter().count(), built.len());
        for def in built {
            assert_eq!(inventory.weapons.rank(def.id), Some(def.max_rank()));
        }
        assert_eq!(inventory.passives.iter().count(), 6);
        // Check the effects consumed by the real weapon/recovery paths, not just HUD ranks.
        let stats = engine.sim.survivor_progress().unwrap().stats.finalized();
        assert_eq!(stats.amount, 2.0);
        assert!((stats.cooldown - 0.6).abs() < 1e-10);
        assert!((stats.might - 1.5).abs() < 1e-10);
        assert!((stats.recovery - 1.0).abs() < 1e-10);
        assert!((stats.duration - 1.5).abs() < 1e-10);
        assert!((stats.max_hp - 120.0 * 1.2_f64.powi(5)).abs() < 1e-10);
        let fixture: Fixture =
            serde_json::from_str(include_str!("../../../fixtures/survivors-3m.json")).unwrap();
        let mut next = 0;
        for frame in 13..=360 {
            let now = f64::from(frame) / 120.0;
            let from = next;
            while next < fixture.arrivals.len() && fixture.arrivals[next].arrived <= now {
                next += 1;
            }
            if next > from {
                engine.feed(|log| {
                    for arrival in &fixture.arrivals[from..next] {
                        crate::session::rebase_append(log, &arrival.entry, 0.0);
                    }
                });
            }
            if frame % 12 == 0 {
                engine.key(
                    now - 0.025,
                    sim::movement::InputKey::Space,
                    (frame / 12) % 2 == 0,
                );
            }
            engine.frame(now, 0.23); // deliberately non-default lead and late input
            engine.view.borrow_mut().take_moments();
        }
        assert!(engine.stats.rollbacks > 20);
        assert!(engine.settled_log.is_none());
        assert_eq!(
            engine.sim.survivor_progress().unwrap().stats.finalized(),
            stats
        );
    }
}
