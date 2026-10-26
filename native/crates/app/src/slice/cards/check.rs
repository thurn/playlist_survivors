//! Reproducible offscreen presentation fixture, using real offer input/freeze/resume paths.
use crate::slice::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, post::Frame};
use sim::movement::InputKey;
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
            Err(format!("cards-check child: {status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let mut world = World::load(None)?;
    // One quick XP source and twelve durable background actors; no gameplay source is edited.
    let bat = world
        .lib
        .enemies
        .get_mut("vs-bat3")
        .ok_or("no fixture bat")?;
    bat.health = 0.0001;
    let stats = bat.survivors.as_mut().ok_or("no bat stats")?;
    stats.speed = 1000.0;
    stats.xp = 10.0;
    world
        .lib
        .enemies
        .get_mut("vs-zombie")
        .ok_or("no fixture zombie")?
        .health = 1_000_000.0;
    let old = "{\"minute\":0,\"minimum\":15,\"frequency\":1000,\"enemies\":[\"vs-bat3\"],\"bosses\":[],\"events\":[],\"treasure\":[]}";
    if !world.level.text.contains(old) {
        return Err("minute-zero fixture source changed".into());
    }
    let mut enemies = vec!["vs-bat3"];
    enemies.extend(std::iter::repeat_n("vs-zombie", 12));
    let row = serde_json::json!({"minute":0,"minimum":13,"frequency":60000,"enemies":enemies,"bosses":[],"events":[],"treasure":[]});
    world.level.text = world.level.text.replacen(old, &row.to_string(), 1);
    eprintln!(
        "cards-check: presentation fixture: bat health0.0001 speed1000 xp10, 12 zombies health1000000, minute0 one roster; normal hero damage, no pacing/survival claim"
    );
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let text = std::fs::read_to_string(
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../../public/reactive/house-128/stream.json"),
    )
    .map_err(|e| e.to_string())?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let beats: Vec<_> = file.log.beats.iter().map(|b| b.t).collect();
    let mut replay = stream::Replay::new(file.log);
    let mut engine = world.engine(true)?;
    let mut scene = Scene::default();
    let (w, h) = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&art.pages, w, h)?;
    let mut clip = crate::slice::clip::Clip::new(w, h, 30)?;
    let mut trace = std::fs::File::create(dir.join("sequence.jsonl")).map_err(|e| e.to_string())?;
    let (mut opened, mut chosen, mut resumed) = (None, None, None);
    let (mut highlight, mut open_still, mut confirm_still) = (false, false, false);
    for tick in 1..=7200 {
        let s = f64::from(tick) / 120.0;
        engine.feed(|log| replay.feed_to(log, s));
        if let Some(o) = opened {
            if !highlight && s >= o + 0.8 {
                engine.key(s, InputKey::Right, true);
                engine.key(s + 0.001, InputKey::Right, false);
                highlight = true;
            }
            let current_offer = engine.view.borrow().run.and_then(|r| r.offer);
            if chosen.is_none()
                && let Some(offer) = current_offer
                && s >= offer.reveals[2] + 0.25
            {
                engine.key(s, InputKey::Confirm, true);
                engine.key(s + 0.001, InputKey::Confirm, false);
                chosen = Some(s);
            }
        }
        engine.frame(s, 0.3);
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
                free: 0.5,
            },
        );
        let run = engine.view.borrow().run.ok_or("fixture has no run")?;
        if opened.is_none() && run.offer.is_some() {
            opened = Some(s);
            engine.key(s, InputKey::Right, false);
            writeln!(trace,"{}",serde_json::json!({"event":"open","song":s,"run":run.elapsed,"reveals":run.offer.map(|o|o.reveals)})).map_err(|e|e.to_string())?;
        }
        if chosen.is_some() && resumed.is_none() && run.frozen.is_none() {
            resumed = Some(s);
            writeln!(trace,"{}",serde_json::json!({"event":"resume","song":s,"run":run.elapsed,"selected":run.selected})).map_err(|e|e.to_string())?;
        }
        if opened.is_none() || tick % 4 != 0 {
            continue;
        }
        let mut layers = scene::Layers::default();
        let last = beats
            .iter()
            .copied()
            .filter(|b| *b <= s)
            .fold(0.0, f64::max);
        let period = beats
            .windows(2)
            .find(|b| b[1] >= s)
            .map_or(0.5, |b| b[1] - b[0]);
        let pulse = scene::Pulse {
            beat: ((s - last) / period).clamp(0.0, 1.0),
            bar: 0.5,
            index: beats.iter().filter(|b| **b <= s).count() as f64,
        };
        let ground = world.field(&engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, None);
        scene.draw(
            &engine.view.borrow(),
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
            tier_flash: 0.0,
            delayed: 3.0,
            zen: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(
            &engine.view.borrow(),
            &st,
            &art,
            w as f32,
            h as f32,
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
        clip.frame(&rgba)?;
        if !open_still && run.offer.is_some_and(|o| s >= o.reveals[2] + 0.1) {
            capture::write_png(&dir.join("open.png"), w, h, &rgba)?;
            open_still = true;
            writeln!(trace,"{}",serde_json::json!({"event":"fully-open","song":s,"run":run.elapsed,"selected":run.selected,"alive":engine.view.borrow().alive().count()})).map_err(|e|e.to_string())?;
        }
        if !confirm_still && chosen.is_some_and(|at| s >= at + 0.1) {
            capture::write_png(&dir.join("confirm.png"), w, h, &rgba)?;
            confirm_still = true;
            writeln!(trace,"{}",serde_json::json!({"event":"confirm","song":s,"run":run.elapsed,"selected":run.selected})).map_err(|e|e.to_string())?;
        }
        if resumed.is_some_and(|at| s >= at + 0.5) {
            capture::write_png(&dir.join("resumed.png"), w, h, &rgba)?;
            break;
        }
    }
    if !open_still || !confirm_still || resumed.is_none() {
        return Err(format!(
            "fixture did not open, pick and resume: run {:?}, HP {}, kills {}, gems {}",
            engine.view.borrow().run,
            engine.view.borrow().health,
            engine.view.borrow().kills,
            engine.view.borrow().gems.len()
        ));
    }
    clip.encode(&dir.join("open-pick-resume.mp4"))?;
    let n = clip.count();
    eprintln!(
        "cards-check: {n} offscreen frames at30fps, open {opened:?}, confirm {chosen:?}, resume {resumed:?}; silent capture, operator sound/feel pending"
    );
    Ok(())
}
