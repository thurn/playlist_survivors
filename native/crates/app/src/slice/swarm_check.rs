//! Bounded offscreen actual-event captures; durable XP-free enemies, normal hero damage.
use super::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
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
            Err(format!("swarm-check child: {status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let mut trace = std::fs::File::create(dir.join("events.jsonl")).map_err(|e| e.to_string())?;
    for (name, kind, role, count, duration, until) in [
        ("bat-drop", "BAT_SWARM", "vs-bat-swarm", 50, 10000, 8.0),
        (
            "ghost-drop",
            "GHOST_SWARM",
            "vs-ghost-swarm",
            20,
            10000,
            8.0,
        ),
        ("flower-wall", "FLOWER_WALL", "vs-flower", 100, 30000, 25.0),
    ] {
        let mut world = World::load(None)?;
        let d = world
            .lib
            .enemies
            .get_mut(role)
            .ok_or("fixture role missing")?;
        d.health = 1_000_000.0;
        d.survivors.as_mut().ok_or("fixture stats missing")?.xp = 0.0;
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
                row["enemies"] = serde_json::json!([]);
                row["minimum"] = serde_json::json!(0);
                row["bosses"] = serde_json::json!([]);
                row["events"] = serde_json::json!([]);
                if row["minute"] == 0 {
                    row["events"] = serde_json::json!([{"kind":kind,"delayMs":0,"extraRepeats":0,"durationMs":duration,"count":count}]);
                }
                *line = format!("    - {row}");
            }
        }
        world.level.text = lines.join("\n");
        let period = 60.0 / 128.0;
        let log = stream::StreamLog {
            beats: (0..=100)
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
            ticks: (0..=1000)
                .map(|i| {
                    let t = f64::from(i) * 0.05;
                    stream::Tick {
                        t,
                        known_at: t,
                        energy: if t < 3.0 { 0.25 } else { 0.9 },
                        evidence: 1.0,
                        level: None,
                        key: None,
                        bands: None,
                        brightness: None,
                    }
                })
                .collect(),
            jumps: Some(vec![stream::Jump {
                t: 3.0,
                known_at: 3.0,
                db: 9.0,
            }]),
            onsets: vec![stream::Onset {
                t: 3.0,
                known_at: 3.0,
                strength: 1.0,
                strong: true,
                band: None,
            }],
            ..Default::default()
        };
        let beats: Vec<_> = log.beats.iter().map(|b| b.t).collect();
        let mut replay = stream::Replay::new(log);
        let mut engine = world.engine(true)?;
        let mut scene = Scene::default();
        let mut clip = super::clip::Clip::new(1280, 720, 24)?;
        let (mut started, mut still, mut closed) = (false, false, false);
        let mut peak = 0;
        for tick in 1..=(until * 120.0) as u32 {
            let s = f64::from(tick) / 120.0;
            engine.feed(|l| replay.feed_to(l, s));
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
            scene.level(&engine.view.borrow(), s, &world.lib);
            let n = engine
                .view
                .borrow()
                .alive()
                .filter(|(_, f)| f.kind == role)
                .count();
            peak = peak.max(n);
            if !started && n > 0 {
                started = true;
                writeln!(trace,"{}",serde_json::json!({"case":name,"event":"admission","song":s,"count":n,"jumpKnownAt":3.0})).map_err(|e|e.to_string())?;
            }
            if s < 2.5 || tick % 5 != 0 {
                continue;
            }
            // A short drop clip and (for the wall) an additional closing window.
            let record = s < 6.5 || name == "flower-wall" && (20.0..24.0).contains(&s);
            let save = !still && s >= 3.5 || name == "flower-wall" && !closed && s >= 22.0;
            if !record && !save {
                continue;
            }
            let mut view = engine.view.borrow().clone();
            view.camera.2 *= 0.55; // Presentation-only wide view exposes the source ellipse.
            let run = view.run.ok_or("fixture run missing")?;
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
                tier_flash: scene.tier_flash(s),
                delayed: 3.0,
                zen: true,
                rhythm: scene.hud_rhythm(),
                keys_visible: true,
            };
            let mut overlay = std::mem::take(&mut layers.overlay);
            hud::draw(&view, &st, &art, 1280.0, 720.0, &mut overlay);
            art.fonts["pixel"].text(
                &format!("{} / DROP +9DB AT3S", name.to_uppercase()),
                640.0,
                600.0,
                1.3,
                Align::Centre,
                [1.0; 4],
                &mut overlay,
            );
            art.fonts["pixel"].text("PRESENTATION: HP1E6 / XP0 / NORMAL HERO DAMAGE / VIEW0.55 / SYNTHETIC128BPM / SILENT",640.0,700.0,1.0,Align::Centre,[0.8,0.8,0.9,1.0],&mut overlay);
            overlay.append(&mut layers.modal);
            gpu.draw(&Frame {
                clear: super::CLEAR,
                world: &layers.sprites,
                lights: &layers.lights,
                grade: layers.grade,
                overlay: &overlay,
            });
            let rgba = gpu.rgba()?;
            if record {
                clip.frame(&rgba)?;
            }
            if !still && s >= 3.5 {
                capture::write_png(&dir.join(format!("{name}.png")), 1280, 720, &rgba)?;
                still = true;
            }
            if name == "flower-wall" && !closed && s >= 22.0 {
                capture::write_png(&dir.join("flower-wall-closing.png"), 1280, 720, &rgba)?;
                closed = true;
            }
        }
        if !started || !still || peak != count {
            return Err(format!(
                "{name} missing admission/still: peak{peak} expected{count}"
            ));
        }
        clip.encode(&dir.join(format!("{name}.mp4")))?;
        writeln!(trace,"{}",serde_json::json!({"case":name,"event":"complete","peak":peak,"frames":clip.count(),"heroHealth":engine.view.borrow().health})).map_err(|e|e.to_string())?;
        eprintln!(
            "swarm-check {name}: actual event peak{peak}, {} silent frames24fps; HP1e6/XP0, normal hero damage, diagnostic view0.55, source first attempt/count/speed/contact/expiry; synthetic128BPM",
            clip.count()
        );
    }
    Ok(())
}
