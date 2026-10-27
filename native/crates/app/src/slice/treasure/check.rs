//! Offscreen fixture: one real boss kill, physical chest, driven pickup, two-bar spin.
use crate::slice::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use sim::{movement::InputKey, survivors::WeaponId};
use std::{io::Write, path::Path};
pub fn run(dir: &Path) -> Result<(), String> {
    capture(dir, false)
}
pub fn bosses(dir: &Path) -> Result<(), String> {
    capture(dir, true)
}
fn capture(dir: &Path, showcase: bool) -> Result<(), String> {
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
            Err(format!("treasure-check child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let mut world = World::load(None)?;
    world
        .lib
        .enemies
        .get_mut("vs-bat4")
        .ok_or("fixture boss missing")?
        .survivors
        .as_mut()
        .ok_or("fixture boss data missing")?
        .xp = 0.0;
    let old = r#"{"minute":0,"minimum":15,"frequency":1000,"enemies":["vs-bat3"],"bosses":[],"events":[],"treasure":[]}"#;
    if !world.level.text.contains(old) {
        return Err("fixture minute-zero changed".into());
    }
    let row = serde_json::json!({"minute":0,"minimum":0,"frequency":60000,"enemies":[],"bosses":["vs-bat4"],"events":[],"treasure":[{"tier":3,"chances":[0,0,0],"level":1,"prizeTypes":"EAAAA","fixedPrizes":[]}]});
    if showcase {
        let empty = serde_json::json!({"minute":0,"minimum":0,"frequency":60000,"enemies":[],"bosses":[],"events":[],"treasure":[]});
        world.level.text = world.level.text.replacen(old, &empty.to_string(), 1);
        let minute_one = world
            .level
            .text
            .lines()
            .find(|l| l.trim().starts_with("- {\"minute\":1,"))
            .ok_or("minute1 row missing")?
            .to_string();
        let mut row = row.clone();
        row["minute"] = 1.into();
        world.level.text = world
            .level
            .text
            .replace(&minute_one, &format!("    - {row}"));
    } else {
        world.level.text = world.level.text.replacen(old, &row.to_string(), 1);
    }
    let period = 60.0 / 128.0;
    let log = stream::StreamLog {
        beats: (0..240)
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
        ticks: (0..2400)
            .map(|i| {
                let t = f64::from(i) * 0.05;
                stream::Tick {
                    t,
                    known_at: t,
                    evidence: 1.0,
                    energy: 0.5,
                    level: None,
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect(),
        onsets: (0..120)
            .map(|i| {
                let t = f64::from(i) * 2.0 * period;
                stream::Onset {
                    t,
                    known_at: t + 0.01,
                    strength: 1.0,
                    strong: true,
                    band: None,
                }
            })
            .collect(),
        forecasts: Some(
            (0..120)
                .map(|i| {
                    let t = f64::from(i) * 2.0 * period;
                    stream::Forecast {
                        t,
                        known_at: (t - 0.2).max(0.0),
                        cycle: 2.0,
                        confidence: 1.0,
                    }
                })
                .collect(),
        ),
        ..Default::default()
    };
    let beats: Vec<_> = log.beats.iter().map(|b| b.t).collect();
    let mut replay = stream::Replay::new(log);
    let mut engine = world.engine(true)?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let mut scene = Scene::default();
    let mut clip = crate::slice::clip::Clip::new(1280, 720, 24)?;
    let mut trace = std::fs::File::create(dir.join("sequence.jsonl")).map_err(|e| e.to_string())?;
    let (mut boss_still, mut field_still, mut spin_still, mut landed_still) =
        (false, false, false, false);
    let (mut start, mut landed, mut opened) = (None, None, None);
    let (mut warning_still, mut entrance_still, mut death_still) = (false, false, false);
    let mut held: Vec<InputKey> = Vec::new();
    for tick in 1..=12000 {
        let s = f64::from(tick) / 120.0;
        engine.feed(|l| replay.feed_to(l, s));
        let view = engine.view.borrow();
        let mut target = Vec::new();
        if view.run.is_some_and(|r| r.frozen.is_none())
            && let Some((c, h)) = view.chests.values().next().zip(view.hero)
        {
            let (dx, dy) = (c.x - h.x, c.y - h.y);
            if dx.abs() > dy.abs() * 0.414_213_562_373_095 {
                target.push(if dx < 0.0 {
                    InputKey::Left
                } else {
                    InputKey::Right
                });
            }
            if dy.abs() > dx.abs() * 0.414_213_562_373_095 {
                target.push(if dy < 0.0 {
                    InputKey::Up
                } else {
                    InputKey::Down
                });
            }
        }
        drop(view);
        for &k in &held {
            if !target.contains(&k) {
                engine.key(s, k, false);
            }
        }
        for &k in &target {
            if !held.contains(&k) {
                engine.key(s, k, true);
            }
        }
        held = target;
        engine.frame(s, 0.0);
        let moments = engine.view.borrow_mut().take_moments();
        scene.moments(
            &moments,
            s,
            &world.lib,
            &world.looks,
            &engine.view.borrow(),
            &damage::Clock {
                beats: &beats,
                start: 0.0,
                free: 0.35,
            },
        );
        let view = engine.view.borrow().clone();
        let run = view.run.ok_or("fixture run absent")?;
        let h = view.hero.ok_or("fixture hero absent")?;
        let distance = view
            .alive()
            .find(|(_, f)| f.kind == "vs-bat4")
            .map(|(_, f)| (f.x - h.x).hypot(f.y - h.y));
        if showcase
            && start.is_none()
            && view
                .boss_cues
                .iter()
                .any(|c| c.kind == sim::survivors::BossCueKind::Warning)
        {
            start = Some(s);
        }
        if start.is_none() && distance.is_some_and(|d| d < 170.0) {
            start = Some(s);
        }
        if !view.chests.is_empty() && !field_still {
            writeln!(trace,"{}",serde_json::json!({"event":"drop","song":s,"run":run.elapsed,"chests":view.chests.len()})).map_err(|e|e.to_string())?;
        }
        if opened.is_none() && run.roulette.is_some() {
            opened = Some(s);
            writeln!(
                trace,
                "{}",
                serde_json::json!({"event":"open","song":s,"run":run.elapsed})
            )
            .map_err(|e| e.to_string())?;
        }
        if landed.is_none() && run.roulette.is_some_and(|r| r.landed.is_some()) {
            landed = Some(s);
            writeln!(trace,"{}",serde_json::json!({"event":"land","song":s,"run":run.elapsed,"sword":view.inventory.map(|i|i.weapons.rank(WeaponId::Sword))})).map_err(|e|e.to_string())?;
        }
        let entrance = view
            .boss_cues
            .iter()
            .find(|c| c.kind == sim::survivors::BossCueKind::Landed);
        let death = view
            .boss_cues
            .iter()
            .find(|c| c.kind == sim::survivors::BossCueKind::Defeated);
        let extra_still = showcase
            && ((!warning_still && start.is_some())
                || (!entrance_still && entrance.is_some())
                || (!death_still && death.is_some()))
            || !field_still && !view.chests.is_empty()
            || !boss_still && distance.is_some_and(|d| d < 100.0);
        if start.is_none() || (tick % 5 != 0 && !extra_still) {
            continue;
        }
        let pulse = scene::Pulse {
            beat: (s / period).fract(),
            bar: (s / (2.0 * period)).fract(),
            index: s / period,
        };
        let ground = world.field(&engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, None, 0.0);
        let mut layers = scene::Layers::default();
        scene.draw(
            &view,
            &ground,
            &art,
            &world.looks,
            &world.lib,
            s,
            pulse,
            1280.0,
            720.0,
            &mut layers,
        );
        let st = HudState {
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
        hud::draw(&view, &st, &art, 1280.0, 720.0, &mut overlay);
        art.fonts["pixel"].text(
            if showcase {
                "DIAGNOSTIC: MINUTE1 / EMPTY FIELD / BOSS XP0 / TIER3 / SYNTHETIC128BPM"
            } else {
                "DIAGNOSTIC: MINUTE-0 BOSS / XP0 / SCRIPTED TIER3 / SYNTHETIC128BPM / BAR ACCENTS"
            },
            640.0,
            700.0,
            1.0,
            Align::Centre,
            [0.8, 0.8, 0.9, 1.0],
            &mut overlay,
        );
        overlay.append(&mut layers.modal);
        gpu.draw(&Frame {
            clear: crate::slice::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        let rgba = gpu.rgba()?;
        if tick % 5 == 0 {
            clip.frame(&rgba)?;
        }
        let save = |name: &str| capture::write_png(&dir.join(name), 1280, 720, &rgba);
        if showcase && !warning_still {
            save("warning.png")?;
            warning_still = true;
            writeln!(trace,"{}",serde_json::json!({"event":"warning","song":s,"cue":view.boss_cues.iter().find(|c|c.kind==sim::survivors::BossCueKind::Warning).map(|c|c.song),"zoom":view.camera.2})).map_err(|e|e.to_string())?;
        }
        if showcase
            && !entrance_still
            && let Some(cue) = entrance
        {
            save("entrance.png")?;
            entrance_still = true;
            writeln!(trace,"{}",serde_json::json!({"event":"entrance","song":s,"cue":cue.song,"run":cue.run,"zoom":view.camera.2})).map_err(|e|e.to_string())?;
        }
        if showcase
            && !death_still
            && let Some(cue) = death
        {
            save("death.png")?;
            death_still = true;
            writeln!(
                trace,
                "{}",
                serde_json::json!({"event":"death","song":s,"cue":cue.song,"run":cue.run})
            )
            .map_err(|e| e.to_string())?;
        }
        if !boss_still && distance.is_some_and(|d| d < 100.0) {
            save("boss-bat.png")?;
            boss_still = true;
        }
        if !field_still && !view.chests.is_empty() {
            save("chest.png")?;
            field_still = true;
        }
        if !spin_still
            && run
                .roulette
                .is_some_and(|r| r.started && r.tick >= 3 && r.landed.is_none())
        {
            save("roulette.png")?;
            spin_still = true;
        }
        if !landed_still && landed.is_some() {
            save("reward.png")?;
            landed_still = true;
        }
        if landed.is_some_and(|at| s > at + 1.3) && run.frozen.is_none() {
            if !boss_still
                || !field_still
                || !spin_still
                || !landed_still
                || (showcase && (!warning_still || !entrance_still || !death_still))
            {
                return Err("fixture missed one of four stills".into());
            }
            if view.inventory.and_then(|i| i.weapons.rank(WeaponId::Sword)) != Some(6) {
                return Err("fixture did not apply five sequential Sword upgrades".into());
            }
            clip.encode(&dir.join("boss-chest.mp4"))?;
            writeln!(trace,"{}",serde_json::json!({"event":"resume","song":s,"run":run.elapsed,"frames":clip.count(),"kills":view.kills,"health":view.health})).map_err(|e|e.to_string())?;
            eprintln!(
                "treasure-check: boss killed normally; four stills and {} silent frames at24fps, tier3 five sequential upgrades Sword1->6; open{opened:?} land{landed:?}; fixture only, no balance/survival claim",
                clip.count()
            );
            return Ok(());
        }
    }
    Err(format!(
        "treasure fixture did not finish: boss{boss_still} field{field_still} spin{spin_still} landed{landed_still}"
    ))
}
