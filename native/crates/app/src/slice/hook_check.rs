//! Offscreen evidence from an actual normal-damage five-minute bot recording.
use super::{HudState, KeyLight, Scene, World, damage, hud, scene};
use eval::session::{AppliedKey, Arrival, rebase_append};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use sim::movement::InputKey;
use std::{io::Write, path::Path};
fn lines<T: serde::de::DeserializeOwned>(p: &Path) -> Result<Vec<T>, String> {
    std::fs::read_to_string(p)
        .map_err(|e| format!("{}: {e}", p.display()))?
        .lines()
        .map(|s| serde_json::from_str(s).map_err(|e| e.to_string()))
        .collect()
}
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
            Err(format!("hook-check child: {status}"))
        };
    }
    let meta: serde_json::Value =
        serde_json::from_slice(&std::fs::read(dir.join("meta.json")).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let runs = meta["runs"].as_array().ok_or("missing runs")?;
    if runs.len() != 1 || runs[0]["s0"] != 0.0 || runs[0]["noHeroDamage"] != false {
        return Err("hook-check needs one normal-damage survive recording from zero".into());
    }
    let end = runs[0]["end"]
        .as_f64()
        .filter(|s| s.is_finite() && *s > 0.0 && *s < 900.0)
        .ok_or("invalid hook span")?;
    let expected_hash = runs[0]["settledHash"].as_str().ok_or("missing hash")?;
    let expected_count = runs[0]["settledRecords"].as_u64().ok_or("missing count")?;
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"))?;
    let recorded: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"))?;
    let keys: Vec<_> = recorded
        .iter()
        .map(|k| InputKey::parse_recorded(&k.key).map(|key| (k.s, key, k.pressed)))
        .collect::<Result<_, _>>()?;
    let mut world = World::load(None)?;
    world.level.text = runs[0]["level"]
        .as_str()
        .ok_or("missing level source")?
        .into();
    let mut engine = world.engine(true)?;
    let art = Atlases::load(&super::live::resource("assets", "../../../public/assets"))?;
    let out = dir.join("hook");
    std::fs::create_dir_all(&out).map_err(|e| e.to_string())?;
    let mut trace = std::fs::File::create(out.join("frames.jsonl")).map_err(|e| e.to_string())?;
    let (w, h) = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&art.pages, w, h)?;
    let mut clip = super::clip::Clip::new(w, h, 24)?;
    let mut scene = Scene::default();
    let (mut next, mut next_key) = (0, 0);
    let mut stills = [false; 6];
    let (mut cards, mut boss, mut chest) = (false, false, false);
    for frame in 1..=(end * 120.0).round() as u64 {
        let s = frame as f64 / 120.0;
        let start = next;
        while next < arrivals.len() && arrivals[next].arrived <= s {
            next += 1;
        }
        if next > start {
            engine.feed(|log| {
                for a in &arrivals[start..next] {
                    rebase_append(log, &a.entry, 0.0);
                }
            });
        }
        while next_key < keys.len() && keys[next_key].0 <= s {
            let (at, key, down) = keys[next_key];
            engine.key(at, key, down);
            next_key += 1;
        }
        engine.frame(s, 0.0);
        let beats: Vec<_> = engine.stream().beats.iter().map(|b| b.t).collect();
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
                free: world.lib.hero.stride.free_period_s,
            },
        );
        scene.level(&engine.view.borrow(), s, &world.lib);
        let view = engine.view.borrow();
        let run = view.run.ok_or("missing run")?;
        let minute = (run.elapsed / 60.0).floor() as usize;
        let card_still = !cards && run.offer.is_some_and(|o| s >= o.reveals[2]);
        let boss_still = !boss
            && view.hero.is_some_and(|hero| {
                view.alive()
                    .any(|(_, f)| f.survivor_boss && (f.x - hero.x).hypot(f.y - hero.y) < 160.0)
            });
        let chest_still = !chest
            && run
                .roulette
                .is_some_and(|r| r.started && r.landed.is_none());
        let minute_still = minute < 6 && !stills[minute] && !beats.is_empty();
        // Chronological eight-second windows; menus keep their real duration.
        // Every simulation frame runs even when no image is captured.
        let in_clip = [0.0, 58.0, 118.0, 178.0, 238.0, 292.0]
            .iter()
            .any(|&start| (start..start + 8.0).contains(&run.elapsed));
        let video = frame % 5 == 0 && in_clip;
        if !video && !minute_still && !card_still && !boss_still && !chest_still {
            continue;
        }
        let wave = view.wave.as_ref().map(|v| v.s);
        drop(view);
        let last = engine.stream().beats.iter().rev().find(|b| b.t <= s);
        let period = last.map_or(world.lib.hero.stride.free_period_s, |b| b.period);
        let index = last.map_or(0.0, |b| {
            beats.partition_point(|t| *t <= b.t).saturating_sub(1) as f64
                + ((s - b.t) / period).clamp(0.0, 1.0)
        });
        let pulse = scene::Pulse {
            beat: index.fract(),
            bar: (index / 2.0).fract(),
            index,
        };
        let ground = world.field(&engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, wave);
        let view = engine.view.borrow();
        let mut layers = scene::Layers::default();
        scene.draw(
            &view,
            &ground,
            &art,
            &world.looks,
            &world.lib,
            s,
            pulse,
            w as f32,
            h as f32,
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
            feet: layers.feet,
            next_down: false,
            tier_flash: scene.tier_flash(s),
            delayed: 3.0,
            zen: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(&view, &st, &art, w as f32, h as f32, &mut overlay);
        art.fonts["pixel"].text(
            "RECORDED POSITIONING BOT / NORMAL DAMAGE / SILENT MONTAGE",
            640.0,
            700.0,
            1.0,
            Align::Centre,
            [0.8, 0.8, 0.9, 1.0],
            &mut overlay,
        );
        gpu.draw(&Frame {
            clear: super::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        let rgba = gpu.rgba()?;
        if video {
            clip.frame(&rgba)?;
        }
        let mut saved = Vec::new();
        if minute_still {
            stills[minute] = true;
            saved.push(format!("minute-{minute}.png"));
        }
        if card_still {
            cards = true;
            saved.push("cards.png".into());
        }
        if boss_still {
            boss = true;
            saved.push("boss.png".into());
        }
        if chest_still {
            chest = true;
            saved.push("roulette.png".into());
        }
        for name in &saved {
            capture::write_png(&out.join(name), w, h, &rgba)?;
        }
        writeln!(trace,"{}",serde_json::json!({"song":s,"run":run.elapsed,"level":run.level,"hp":view.health,"alive":view.alive().count(),"video":video,"stills":saved})).map_err(|e|e.to_string())?;
    }
    let hash = format!("{:016x}", engine.settled_hash);
    if hash != expected_hash || engine.settled_count as u64 != expected_count {
        return Err(format!(
            "capture replay mismatch: {hash}/{} expected {expected_hash}/{expected_count}",
            engine.settled_count
        ));
    }
    if !stills.iter().all(|v| *v) || !cards || !boss {
        return Err(format!(
            "missing hook stills: {stills:?} cards{cards} boss{boss}"
        ));
    }
    clip.encode(&out.join("first-five.mp4"))?;
    eprintln!(
        "hook-check: {} frames at24fps; exact {hash}/{} records; six minute stills, cards{cards}, boss{boss}, roulette{chest}; normal damage, silent montage",
        clip.count(),
        engine.settled_count
    );
    Ok(())
}
