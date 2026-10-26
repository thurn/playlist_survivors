//! A real first minute on the Rust fixture, then a separately labelled 400-gem layout.
use crate::slice::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, post::Frame};
use std::path::Path;
#[derive(serde::Deserialize)]
struct Fixture {
    arrivals: Vec<eval::session::Arrival>,
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
            Err(format!("pickups-check child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../fixtures/survivors-3m.json");
    let fixture: Fixture =
        serde_json::from_str(&std::fs::read_to_string(path).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let world = World::load(None)?;
    let mut engine = world.engine(true)?;
    let mut scene = Scene::default();
    let (mut next, mut picked) = (0, None);
    let mut reached = None;
    for frame in 1..=21600 {
        let now = f64::from(frame) / 120.0;
        let start = next;
        while next < fixture.arrivals.len() && fixture.arrivals[next].arrived <= now {
            next += 1;
        }
        if next > start {
            engine.feed(|l| {
                for a in &fixture.arrivals[start..next] {
                    eval::session::rebase_append(l, &a.entry, 0.0);
                }
            });
        }
        let offer = engine.view.borrow().run.and_then(|r| r.offer);
        if let Some(o) = offer {
            let token = (o.level, o.reveals[2].to_bits());
            if picked != Some(token) && now >= o.reveals[2] + 0.01 {
                picked = Some(token);
                engine.key(now, sim::movement::InputKey::Left, true);
                engine.key(now + 0.001, sim::movement::InputKey::Left, false);
                engine.key(now + 0.002, sim::movement::InputKey::Confirm, true);
                engine.key(now + 0.003, sim::movement::InputKey::Confirm, false);
            }
        }
        engine.frame(now, 0.0);
        let moments = engine.view.borrow_mut().take_moments();
        let run = engine.view.borrow().run.ok_or("no pickup fixture run")?;
        if engine.view.borrow().health <= 0.0 {
            return Err(format!(
                "normal stationary pickup fixture fell at {:.3}",
                run.elapsed
            ));
        }
        if run.elapsed >= 58.0 {
            let beats: Vec<_> = engine.stream().beats.iter().map(|b| b.t).collect();
            scene.moments(
                &moments,
                now,
                &world.lib,
                &world.looks,
                &engine.view.borrow(),
                &damage::Clock {
                    beats: &beats,
                    start: 0.0,
                    free: 0.35,
                },
            );
        }
        if run.elapsed >= 60.0 {
            reached = Some(now);
            break;
        }
    }
    let now = reached.ok_or("pickup fixture did not reach minute1")?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let minute = engine.view.borrow().clone();
    draw(
        &world,
        &mut engine,
        &mut scene,
        &art,
        &mut gpu,
        now,
        &minute,
        &dir.join("minute-1.png"),
    )?;
    eprintln!(
        "pickups-check minute1: real Rust3m arrivals, normal damage/stationary/first-card picks, song{now:.6}, run{:.6}, HP{:.6}, level{}, kills{}, gems{}, chickens{}; scene history last2run seconds, silent offscreen still",
        minute.run.unwrap().elapsed,
        minute.health,
        minute.level,
        minute.kills,
        minute.gems.len(),
        minute.chickens.len()
    );
    let mut cap = minute.clone();
    cap.gems.clear();
    cap.gem_poses.clear();
    cap.chickens.clear();
    let hero = cap.hero.ok_or("no cap hero")?;
    cap.camera = (hero.x, hero.y - 30.0, 1.0);
    let values = [1.0, 2.0, 3.0, 9.0, 10.0, 100.0];
    'rows: for row in 0..16 {
        for col in 0..26 {
            let x = hero.x + (f64::from(col) - 12.5) * 32.0;
            let y = hero.y + (f64::from(row) - 7.5) * 32.0;
            if (x - hero.x).hypot(y - hero.y) < 64.0 {
                continue;
            }
            let id = cap.gems.len();
            let value = values[id % values.len()];
            cap.gems.insert(
                id,
                eval::live::Gem {
                    value,
                    s0: now - 1.0,
                    x0: x,
                    y0: y,
                    fly: None,
                    s1: f64::INFINITY,
                    big: value >= 10.0,
                },
            );
            if cap.gems.len() == 400 {
                break 'rows;
            }
        }
    }
    cap.chickens.insert(0, (hero.x - 100.0, hero.y + 45.0));
    if cap.gems.len() != 400 {
        return Err("cap layout is not400".into());
    }
    draw(
        &world,
        &mut engine,
        &mut scene,
        &art,
        &mut gpu,
        now,
        &cap,
        &dir.join("cap-400.png"),
    )?;
    eprintln!(
        "pickups-check cap: synthetic400 resting-gem layout and1 floor chicken over the minute1 view; values1,2,3,9,10,100; outside magnet, fixed layout camera; actual cap/merge/drop/heal rules checked separately, no natural density/survival claim"
    );
    Ok(())
}
#[allow(clippy::too_many_arguments)]
fn draw(
    world: &World,
    engine: &mut eval::live::Engine,
    scene: &mut Scene,
    art: &Atlases,
    gpu: &mut capture::Offscreen,
    now: f64,
    view: &eval::live::LiveView,
    path: &Path,
) -> Result<(), String> {
    let beats = engine.stream().beats.clone();
    let last = beats.iter().enumerate().rfind(|(_, b)| b.t <= now);
    let (t, period, index) = last.map_or((0.0, 0.35, 0.0), |(i, b)| (b.t, b.period, i as f64));
    let phase = ((now - t) / period).clamp(0.0, 1.0);
    let pulse = scene::Pulse {
        beat: phase,
        bar: ((index + phase) % 4.0) / 4.0,
        index: index + phase,
    };
    let run = view.run.ok_or("no view run")?;
    let ground = world.field(engine, run.presentation_s, period)?;
    scene.fast_lane(engine.stream(), 0.2, now, now, None);
    let mut layers = scene::Layers::default();
    scene.draw(
        view,
        &ground,
        art,
        &world.looks,
        &world.lib,
        now,
        pulse,
        1280.0,
        720.0,
        &mut layers,
    );
    let st = HudState {
        keys: [KeyLight::default(); 5],
        presses: &[],
        beats: &[],
        beat_phase: phase,
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
    hud::draw(view, &st, art, 1280.0, 720.0, &mut overlay);
    gpu.draw(&Frame {
        clear: crate::slice::CLEAR,
        world: &layers.sprites,
        lights: &layers.lights,
        grade: layers.grade,
        overlay: &overlay,
    });
    capture::write_png(path, 1280, 720, &gpu.rgba()?)
}
