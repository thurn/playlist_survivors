//! Actual opening-minute zone fallbacks, offscreen in an explicitly empty synthetic run.
use crate::slice::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use sim::survivors::{Inventory, WeaponId, weapon_catalog};
use std::{io::Write, path::Path};
pub fn run(dir: &Path) -> Result<(), String> {
    capture(dir, false)
}
pub fn auras(dir: &Path) -> Result<(), String> {
    capture(dir, true)
}
fn capture(dir: &Path, aura_only: bool) -> Result<(), String> {
    if std::env::var_os("PS_NATIVE_BATCH").is_none() {
        let status = std::process::Command::new("taskpolicy")
            .arg("-b")
            .arg(std::env::current_exe().map_err(|e| e.to_string())?)
            .args(std::env::args_os().skip(1))
            .env("PS_NATIVE_BATCH", "1")
            .status()
            .map_err(|e| e.to_string())?;
        return if status.success() {
            Ok(())
        } else {
            Err(format!("zone-check child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let mut world = World::load(None)?;
    let mut lines = world
        .level
        .text
        .lines()
        .map(str::to_string)
        .collect::<Vec<_>>();
    for line in &mut lines {
        if line.trim().starts_with("- {\"minute\":") {
            let mut row: serde_json::Value =
                serde_json::from_str(line.trim().strip_prefix("- ").unwrap())
                    .map_err(|e| e.to_string())?;
            row["minimum"] = 0.into();
            for key in ["enemies", "bosses", "events", "treasure"] {
                row[key] = serde_json::json!([]);
            }
            *line = format!("    - {row}");
        }
    }
    // Keep this authoring diagnostic empty even as gameplay gains choreography.
    for formation in world.lib.formations.values_mut() {
        formation.count = 0.0;
    }
    world.level.text = lines
        .join("\n")
        .replace("durationS: 1800", "durationS: 962")
        .replace("afterS: 90", "afterS: 10000");
    let period = 0.5;
    let log = stream::StreamLog {
        beats: (0..=1940)
            .map(|i| {
                let t = f64::from(i) * period;
                stream::Beat {
                    t,
                    known_at: (t - period).max(0.0),
                    period,
                    evidence: 1.0,
                }
            })
            .collect(),
        ticks: (0..=19400)
            .map(|i| {
                let t = f64::from(i) * 0.05;
                let (energy, level) = match (t / 240.0) as usize {
                    0 | 3 => (0.15, stream::Level::Quiet),
                    1 => (0.5, stream::Level::Medium),
                    _ => (0.9, stream::Level::Loud),
                };
                stream::Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy,
                    level: Some(level),
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        ..Default::default()
    };
    let beats: Vec<_> = log.beats.iter().map(|b| b.t).collect();
    let mut replay = stream::Replay::new(log);
    let mut engine = world.engine(true)?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let mut scene = Scene::default();
    let mut clip = crate::slice::clip::Clip::new(1280, 720, 24)?;
    let mut trace = std::fs::File::create(dir.join(if aura_only {
        "auras.jsonl"
    } else {
        "zones.jsonl"
    }))
    .map_err(|e| e.to_string())?;
    let targets = [
        ("twilight-glade", 2.0),
        ("galop-woods", 62.0),
        ("mushroom-grove", 122.0),
        ("ember-hollow", 182.0),
        ("infernal-grove", 242.0),
    ];
    let mut next = 0;
    for tick in 1..=23090 {
        let song = f64::from(tick) / 24.0;
        engine.feed(|l| replay.feed_to(l, song));
        engine.frame(song, 0.0);
        let moments = engine.view.borrow_mut().take_moments();
        scene.moments(
            &moments,
            song,
            &world.lib,
            &world.looks,
            &engine.view.borrow(),
            &damage::Clock {
                beats: &beats,
                start: 0.0,
                free: 0.5,
            },
        );
        let save = next < targets.len() && song >= targets[next].1;
        let record = (239.5..244.0).contains(&song);
        if !save && !record {
            continue;
        }
        let view = engine.view.borrow().clone();
        let run = view.run.ok_or("missing run")?;
        let ground = world.field(&engine, run.presentation_s, period)?;
        let pulse = scene::Pulse {
            beat: (song / period).fract(),
            bar: (song / (2.0 * period)).fract(),
            index: song / period,
        };
        scene.fast_lane(
            engine.stream(),
            0.2,
            song,
            song,
            view.wave.as_ref().map(|w| w.s),
        );
        let mut layers = scene::Layers::default();
        scene.draw(
            &view,
            &ground,
            &art,
            &world.looks,
            &world.lib,
            song,
            pulse,
            1280.0,
            720.0,
            &mut layers,
        );
        let state = HudState {
            keys: [KeyLight::default(); 5],
            presses: &[],
            beats: &[],
            beat_phase: pulse.beat,
            elapsed: run.elapsed,
            bitten_ago: f64::INFINITY,
            head: layers.head,
            next_down: false,
            tier_flash: 0.0,
            delayed: 3.0,
            zen: true,
            rhythm: scene.hud_rhythm(),
            keys_visible: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(&view, &state, &art, 1280.0, 720.0, &mut overlay);
        art.fonts["pixel"].text(
            "DIAGNOSTIC: ACTUAL OPENING1MIN FALLBACK / EMPTY FIELD / SYNTHETIC120BPM",
            640.0,
            700.0,
            1.0,
            Align::Centre,
            [0.85, 0.85, 0.95, 1.0],
            &mut overlay,
        );
        gpu.draw(&Frame {
            clear: crate::slice::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        let rgba = gpu.rgba()?;
        if record {
            clip.frame(&rgba)?;
        }
        if save {
            let name = targets[next].0;
            if ground.theme.id != name {
                return Err(format!(
                    "zone mismatch at{song}: {} expected{name}",
                    ground.theme.id
                ));
            }
            capture::write_png(&dir.join(format!("{name}.png")), 1280, 720, &rgba)?;
            writeln!(trace,"{}",serde_json::json!({"song":song,"run":run.elapsed,"zone":ground.theme.id,"zoom":view.camera.2,"bloom":layers.grade.bloom,"ambient":layers.grade.ambient})).map_err(|e|e.to_string())?;
            if next == 0 {
                for (label, inventory) in aura_builds() {
                    let mut fixture = view.clone();
                    fixture.inventory = Some(inventory);
                    scene.fast_lane(engine.stream(), 0.2, song, song, None);
                    let mut layers = scene::Layers::default();
                    scene.draw(
                        &fixture,
                        &ground,
                        &art,
                        &world.looks,
                        &world.lib,
                        song,
                        pulse,
                        1280.0,
                        720.0,
                        &mut layers,
                    );
                    art.fonts["pixel"].text(
                        &format!("DIAGNOSTIC INVENTORY OVERRIDE: {label} / AURA ONLY"),
                        640.0,
                        700.0,
                        1.0,
                        Align::Centre,
                        [0.85, 0.85, 0.95, 1.0],
                        &mut layers.overlay,
                    );
                    gpu.draw(&Frame {
                        clear: crate::slice::CLEAR,
                        world: &layers.sprites,
                        lights: &layers.lights,
                        grade: layers.grade,
                        overlay: &layers.overlay,
                    });
                    capture::write_png(
                        &dir.join(format!("aura-{label}.png")),
                        1280,
                        720,
                        &gpu.rgba()?,
                    )?;
                }
            }
            if aura_only {
                eprintln!(
                    "FR9: eight labelled aura-only legal rank/evolution builds; no acquisition/weapon-behavior claim"
                );
                return Ok(());
            }
            next += 1;
        }
        if next == targets.len() {
            break;
        }
    }
    if next != targets.len() {
        return Err("missed a zone".into());
    }
    clip.encode(&dir.join("opening-minute-sweep.mp4"))?;
    eprintln!(
        "FR9: five actual zone stills, eight labelled inventory aura poses, {} silent24fps sweep frames; no human feel/density/survival claim",
        clip.count()
    );
    Ok(())
}

/// Legal slot/rank states for visual comparison; this never grants a gameplay upgrade.
fn aura_builds() -> Vec<(String, Inventory)> {
    use sim::survivors::PassiveId;
    let mut opening = Inventory::default();
    opening.weapons.upgrade(WeaponId::Sword, 8).unwrap();
    let mut growing = opening;
    for (id, rank) in [
        (WeaponId::Sword, 4),
        (WeaponId::ChainLightning, 4),
        (WeaponId::FireTrail, 3),
    ] {
        while growing.weapons.rank(id).unwrap_or(0) < rank {
            growing
                .weapons
                .upgrade(id, weapon_catalog().get(id).max_rank())
                .unwrap();
        }
    }
    for id in [PassiveId::Magnet, PassiveId::Armour] {
        for _ in 0..3 {
            growing.passives.upgrade(id, id.max_rank()).unwrap();
        }
    }
    let mut maxed = Inventory::default();
    for id in [
        WeaponId::Sword,
        WeaponId::Garlic,
        WeaponId::MagicWand,
        WeaponId::KingBible,
        WeaponId::LightningRing,
        WeaponId::ChainLightning,
    ] {
        for _ in 0..weapon_catalog().get(id).max_rank() {
            maxed
                .weapons
                .upgrade(id, weapon_catalog().get(id).max_rank())
                .unwrap();
        }
    }
    for id in [
        PassiveId::Spinach,
        PassiveId::HollowHeart,
        PassiveId::Pummarola,
        PassiveId::Duplicator,
        PassiveId::EmptyTome,
        PassiveId::Spellbinder,
    ] {
        for _ in 0..id.max_rank() {
            maxed.passives.upgrade(id, id.max_rank()).unwrap();
        }
    }
    let mut builds = vec![
        ("opening".into(), opening),
        ("growing-unevolved".into(), growing),
        ("max-base".into(), maxed),
    ];
    for (i, id) in [
        WeaponId::BloodyTear,
        WeaponId::SoulEater,
        WeaponId::HolyWand,
        WeaponId::UnholyVespers,
        WeaponId::ThunderLoop,
    ]
    .into_iter()
    .enumerate()
    {
        maxed
            .weapons
            .replace(weapon_catalog().get(id).evolution_of.unwrap(), id)
            .unwrap();
        builds.push((format!("evolved-{}", i + 1), maxed));
    }
    builds
}
