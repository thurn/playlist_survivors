//! One real simulated role per clip; no app window, input sound or survival claim.
use crate::slice::{HudState, KeyLight, Scene, World, clip::Clip, damage, hud, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use std::path::Path;
const ROLES: [&str; 8] = [
    "vs-mudman1",
    "vs-mudman2",
    "vs-werewolf",
    "vs-xlbat",
    "vs-xlmantis",
    "vs-xlmummy",
    "vs-flower",
    "vs-xlflower",
];
const BOSSES: [&str; 4] = [
    "vs-boss-xlmantis",
    "vs-boss-werewolf",
    "vs-boss-xlmummy",
    "vs-boss-xlflower",
];
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
            Err(format!("roster-check child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let mut video = capture::Offscreen::new(&art.pages, 640, 360)?;
    let mut still = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let selected = std::env::var("PS_ROSTER_ROLES").unwrap_or_else(|_| ROLES.join(","));
    for role in selected.split(',') {
        if !ROLES.contains(&role) && !BOSSES.contains(&role) {
            return Err(format!("unsupported roster role {role}"));
        }
        let boss = BOSSES.contains(&role);
        let mut world = World::load(None)?;
        for zone in world.lib.zones.values_mut() {
            zone.props.clear();
        }
        let actor = world
            .lib
            .enemies
            .get_mut(role)
            .ok_or("roster role missing")?;
        if !boss {
            actor.health = 1_000_000.0;
            actor.survivors.as_mut().ok_or("role stats missing")?.xp = 0.0;
        }
        let begin = world
            .level
            .text
            .find("  schedule:\n")
            .ok_or("roster fixture schedule missing")?
            + "  schedule:\n".len();
        let tail = &world.level.text[begin..];
        let mut len = 0;
        for line in tail.split_inclusive('\n') {
            if !line.trim().is_empty() && !line.starts_with("    ") {
                break;
            }
            len += line.len();
        }
        let row = if boss {
            serde_json::json!({"minute":0,"minimum":0,"frequency":600_000,"enemies":[],"bosses":[role],"events":[],"treasure":[]})
        } else {
            serde_json::json!({"minute":0,"minimum":1,"frequency":600_000,"enemies":[role],"bosses":[],"events":[],"treasure":[]})
        };
        world
            .level
            .text
            .replace_range(begin..begin + len, &format!("    - {row}\n"));
        let period = 60.0 / 128.0;
        let log = stream::StreamLog {
            beats: (0..=390)
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
            ticks: (0..=3600)
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
            forecasts: boss.then(|| {
                (0..=195)
                    .map(|i| {
                        let t = f64::from(i) * 2.0 * period;
                        stream::Forecast {
                            t,
                            known_at: (t - 0.2).max(0.0),
                            cycle: 2.0,
                            confidence: 1.0,
                        }
                    })
                    .collect()
            }),
            onsets: if boss {
                (0..195)
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
                    .collect()
            } else {
                Vec::new()
            },
            ..Default::default()
        };
        let beats: Vec<_> = log.beats.iter().map(|b| b.t).collect();
        let mut replay = stream::Replay::new(log);
        let mut engine = world.engine(true)?;
        engine.feed(|l| replay.feed_to(l, 0.0));
        engine.frame(0.0, 0.0);
        engine.sim.disable_hero_damage_for_check();
        engine.sim.apply_survivor_stats(sim::survivors::StatDelta {
            // Zero power alone still recoils a target; zero area keeps the
            // diagnostic sword out of reach so slow flowers can approach.
            might: -1.0,
            area: -1.0,
            ..Default::default()
        });
        let mut scene = Scene::default();
        let mut clip = Clip::new(640, 360, 24)?;
        let (mut start, mut wrote) = (None, false);
        let mut stop = None;
        let mut last_distance = f64::INFINITY;
        for tick in 1..=21600 {
            let s = f64::from(tick) / 120.0;
            engine.feed(|l| replay.feed_to(l, s));
            engine.frame(s, 0.0);
            let mut moments = engine.view.borrow_mut().take_moments();
            moments.retain(|m| !matches!(m, eval::live::Moment::WeaponFire(_)));
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
            let view = engine.view.borrow();
            let hero = view.hero.ok_or("roster hero missing")?;
            let foe = view
                .alive()
                .find(|(_, f)| f.kind == role)
                .map(|(_, f)| f)
                .ok_or("roster actor missing")?;
            let distance = (foe.x - hero.x).hypot(foe.y - hero.y);
            last_distance = distance;
            if start.is_none() && distance < 35.0 {
                start = Some(((s / period / 4.0).floor() + 1.0) * 4.0 * period);
            }
            drop(view);
            // Keep the still before contact so the hero cannot obscure a small plant.
            if !wrote && distance < 75.0 {
                let view = engine.view.borrow().clone();
                draw(
                    &world,
                    &mut engine,
                    &mut scene,
                    &art,
                    &mut still,
                    &view,
                    s,
                    period,
                    role,
                    1280.0,
                    720.0,
                )?;
                capture::write_png(&dir.join(format!("{role}.png")), 1280, 720, &still.rgba()?)?;
                wrote = true;
            }
            let Some(at) = start else {
                continue;
            };
            if s < at || tick % 5 != 0 {
                continue;
            }
            let view = engine.view.borrow().clone();
            draw(
                &world,
                &mut engine,
                &mut scene,
                &art,
                &mut video,
                &view,
                s,
                period,
                role,
                640.0,
                360.0,
            )?;
            clip.frame(&video.rgba()?)?;
            if s >= at + period * 4.5 {
                stop = Some(s);
                break;
            }
        }
        let end = stop.ok_or_else(|| {
            format!("{role} never reached the contact capture; last distance {last_distance:.6}")
        })?;
        if !wrote {
            return Err(format!("{role} missed still"));
        }
        clip.encode(&dir.join(format!("{role}.mp4")))?;
        eprintln!(
            "roster-check {role}: start{:.6}, end{end:.6}, {} frames/24fps at640x360; source gait/actual approach and lunge, HP/XP documented below for bosses (otherwise1000000/0), hero damage exemption, Might0/Area0, scenery suppressed; synthetic128BPM, lead0, silent, no balance/survival claim",
            start.unwrap(),
            clip.count()
        );
        if boss {
            eprintln!(
                "boss fixture: {role} scheduled at minute0/playerlevel1, source HP/XP retained, bar charge and synthetic bar accents; source speed/contact damage unchanged; not a late-run survival test"
            );
        }
    }
    Ok(())
}
#[allow(clippy::too_many_arguments)]
fn draw(
    world: &World,
    engine: &mut eval::live::Engine,
    scene: &mut Scene,
    art: &Atlases,
    gpu: &mut capture::Offscreen,
    view: &eval::live::LiveView,
    s: f64,
    period: f64,
    role: &str,
    w: f32,
    h: f32,
) -> Result<(), String> {
    let phase = (s / period).rem_euclid(1.0);
    let pulse = scene::Pulse {
        beat: phase,
        bar: (s
            / period
            / if role.starts_with("vs-boss-") {
                2.0
            } else {
                4.0
            })
        .rem_euclid(1.0),
        index: s / period,
    };
    let run = view.run.ok_or("roster run missing")?;
    let ground = world.field(engine, run.presentation_s, period)?;
    scene.fast_lane(engine.stream(), 0.2, s, s, None, 0.0);
    let mut layers = scene::Layers::default();
    scene.draw(
        view,
        &ground,
        art,
        &world.looks,
        &world.lib,
        s,
        pulse,
        w,
        h,
        &mut layers,
    );
    let state = HudState {
        keys: [KeyLight::default(); 5],
        presses: &[],
        beats: &[],
        beat_phase: phase,
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
    hud::draw(view, &state, art, w, h, &mut overlay);
    art.fonts["pixel"].text(
        &role.to_uppercase(),
        w / 2.0,
        h * if role.starts_with("vs-boss-") {
            0.80
        } else {
            0.22
        },
        w / 640.0 * 1.5,
        Align::Centre,
        [1.0; 4],
        &mut overlay,
    );
    art.fonts["pixel"].text(
        if role.starts_with("vs-boss-") {
            "DIAGNOSTIC: DAMAGE EXEMPT / MIGHT0 / AREA0 / SYNTHETIC128BPM"
        } else {
            "DIAGNOSTIC: DAMAGE EXEMPT / WEAPONS SUPPRESSED"
        },
        w / 2.0,
        h - 15.0,
        w / 640.0 * 0.75,
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
    Ok(())
}
