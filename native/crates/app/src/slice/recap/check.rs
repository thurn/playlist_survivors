//! End cards from real combat records: shortened victory and normal-contact defeat.
use crate::slice::{Scene, World, damage, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use sim::{
    movement::InputKey,
    survivors::{FreezeReason, WeaponId, weapon_catalog},
};
use std::{io::Write, path::Path};
pub fn run(dir: &Path) -> Result<(), String> {
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
            Err(format!("recap-check child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let mut trace = std::fs::File::create(dir.join("cards.jsonl")).map_err(|e| e.to_string())?;
    for (name, victory) in [("victory", true), ("fell", false)] {
        let mut world = World::load(None)?;
        let foe = world
            .lib
            .enemies
            .get_mut("vs-skeleton")
            .ok_or("skeleton missing")?;
        foe.health = 1000.0;
        let stats = foe.survivors.as_mut().ok_or("stats missing")?;
        stats.xp = 0.0;
        stats.knockback = 0.0;
        if !victory {
            stats.speed = 1200.0;
            stats.power = 10000.0;
        }
        let old = world
            .level
            .text
            .lines()
            .find(|l| l.trim().starts_with("- {\"minute\":0,"))
            .ok_or("minute0 missing")?
            .to_string();
        let row = serde_json::json!({"minute":0,"minimum":40,"frequency":1000,"enemies":["vs-skeleton"],"bosses":[],"events":[],"treasure":[]});
        world.level.text = world
            .level
            .text
            .replace(&old, &format!("    - {row}"))
            .replace(
                "durationS: 1800",
                if victory {
                    "durationS: 20"
                } else {
                    "durationS: 60"
                },
            );
        let mut engine = world.engine(true)?;
        if victory {
            engine.sim.disable_hero_damage_for_check();
        }
        for id in [
            WeaponId::Sword,
            WeaponId::Garlic,
            WeaponId::MagicWand,
            WeaponId::KingBible,
            WeaponId::LightningRing,
        ] {
            while engine
                .sim
                .survivor_inventory()
                .unwrap()
                .weapons
                .rank(id)
                .unwrap_or(0)
                < 8
            {
                engine
                    .sim
                    .learn_survivor_weapon(id)
                    .map_err(|e| format!("{e:?}"))?;
            }
        }
        engine.sim.freeze_at(4.0, FreezeReason::Paused);
        engine.key(6.0, InputKey::Pause, true);
        engine.key(6.01, InputKey::Pause, false);
        let period = 0.5;
        let log = stream::StreamLog {
            beats: (0..=240)
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
            ticks: (0..=2400)
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
        let beats: Vec<_> = log.beats.iter().map(|b| b.t).collect();
        let mut replay = stream::Replay::new(log);
        let mut scene = Scene::default();
        let mut evolved = false;
        let mut captured = false;
        for frame in 1..=14400 {
            let song = f64::from(frame) / 120.0;
            engine.feed(|l| replay.feed_to(l, song));
            engine.frame(song, 0.0);
            if !evolved && engine.view.borrow().run.is_some_and(|r| r.elapsed >= 8.0) {
                for id in [
                    WeaponId::BloodyTear,
                    WeaponId::SoulEater,
                    WeaponId::HolyWand,
                    WeaponId::UnholyVespers,
                    WeaponId::ThunderLoop,
                ] {
                    let def = weapon_catalog().get(id);
                    engine
                        .sim
                        .learn_survivor_passive(def.requires.unwrap())
                        .map_err(|e| format!("{e:?}"))?;
                    engine
                        .sim
                        .evolve_survivor_weapon(id)
                        .map_err(|e| format!("{e:?}"))?;
                }
                evolved = true;
            }
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
                    free: 0.35,
                },
            );
            let ended = if victory {
                engine.done()
            } else {
                engine.fallen()
            };
            if !ended {
                continue;
            }
            let view = engine.view.borrow().clone();
            let run = view.run.ok_or("run missing")?;
            if victory && engine.fallen() {
                return Err("victory fixture fell".into());
            }
            let summary = super::Summary::from_view(&view).ok_or("summary missing")?;
            let rows = &summary.rows;
            if !rows.iter().any(|r| r.damage > 0.0) {
                return Err("no actual combat recap".into());
            }
            let ground = world.field(&engine, run.presentation_s, period)?;
            let pulse = scene::Pulse {
                beat: (song / period).fract(),
                bar: (song / (period * 2.0)).fract(),
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
            super::draw(
                1280.0,
                720.0,
                1.0,
                if victory {
                    scene::End::Victory
                } else {
                    scene::End::Fell
                },
                &summary,
                &art,
                &mut layers.overlay,
            );
            art.fonts["pixel"].text(if victory {"DIAGNOSTIC:20S VICTORY / HERO EXEMPT / HP1000 XP0 KB0 / RANK8 + API EVOS / SYNTHETIC120BPM"}else{"DIAGNOSTIC:FAST LETHAL SKELETONS / HP1000 XP0 KB0 / RANK8 START / SYNTHETIC120BPM"},640.0,704.0,0.9,Align::Centre,[0.75,0.8,0.9,1.0],&mut layers.overlay);
            layers.overlay.append(&mut layers.modal);
            gpu.draw(&Frame {
                clear: crate::slice::CLEAR,
                world: &layers.sprites,
                lights: &layers.lights,
                grade: layers.grade,
                overlay: &layers.overlay,
            });
            capture::write_png(&dir.join(format!("{name}.png")), 1280, 720, &gpu.rgba()?)?;
            writeln!(trace,"{}",serde_json::json!({"case":name,"song":song,"run":summary.elapsed,"kills":summary.kills,"level":summary.level,"health":view.health,"evolved":evolved,"rows":rows.iter().map(|r|serde_json::json!({"weapon":r.id.as_str(),"damage":r.damage,"held_s":r.held_s,"dps":r.dps})).collect::<Vec<_>>()})).map_err(|e|e.to_string())?;
            captured = true;
            break;
        }
        if !captured {
            return Err(format!("{name} fixture did not reach its actual ending"));
        }
    }
    eprintln!(
        "SP4: actual shortened victory and fast-lethal-contact defeat cards from combat/ownership records; no natural progression or full-run survival claim"
    );
    Ok(())
}
