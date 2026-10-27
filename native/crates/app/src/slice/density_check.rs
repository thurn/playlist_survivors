//! SP5 actual-engine density stills: explicit injected final loadouts and diagnostic actors.
use super::{HudState, KeyLight, Scene, World, damage, hud, live, scene};
use render::{atlas::Atlases, capture, font::Align, post::Frame};
use sim::movement::InputKey;
use std::{io::Write, path::Path};
pub fn run(dir: &Path) -> Result<(), String> {
    run_cases(dir, false)
}
pub fn restored(dir: &Path) -> Result<(), String> {
    run_cases(dir, true)
}
fn run_cases(dir: &Path, restored: bool) -> Result<(), String> {
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
            Err(format!("density child:{status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let world = World::load(None)?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let text = std::fs::read_to_string(
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../../public/reactive/house-128/stream.json"),
    )
    .map_err(|e| e.to_string())?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let beats: Vec<_> = file.log.beats.iter().map(|b| b.t).collect();
    let mut gpu = capture::Offscreen::new(&art.pages, 1280, 720)?;
    let mut trace = std::fs::File::create(dir.join("density.jsonl")).map_err(|e| e.to_string())?;
    let cases = if restored {
        vec![("restored", true, false)]
    } else {
        vec![
            ("evolved", false, false),
            ("moving", true, false),
            ("deaths", false, true),
        ]
    };
    for (name, moving, killable) in cases {
        let mut engine = if restored {
            eval::scale::restored_density_engine(&world.level, &world.lib)?
        } else if killable {
            eval::scale::evolved_combat_engine(&world.level, &world.lib)?
        } else {
            eval::scale::evolved_density_engine(&world.level, &world.lib)?
        };
        let mut replay = stream::Replay::new(file.log.clone());
        let mut scene = Scene::default();
        let (mut next, mut saved, mut pickups, mut hits, mut kills, mut peak_poses) =
            (0, 0, 0, 0, 0, 0);
        for frame in 1..=3600 {
            let s = f64::from(frame) / 120.0;
            engine.feed(|l| replay.feed_to(l, s));
            while next < beats.len() && beats[next] + 0.025 <= s {
                let key = if moving {
                    [
                        InputKey::Right,
                        InputKey::Down,
                        InputKey::Left,
                        InputKey::Up,
                    ][next / 8 % 4]
                } else {
                    InputKey::Space
                };
                engine.key(beats[next], key, true);
                engine.key(beats[next] + 0.08, key, false);
                next += 1;
            }
            engine.frame(s, 0.2);
            let moments = engine.view.borrow_mut().take_moments();
            let mut lethal = false;
            for m in &moments {
                match m {
                    eval::live::Moment::GemPickup => pickups += 1,
                    eval::live::Moment::EnemyImpact { hit } => {
                        hits += 1;
                        if matches!(hit.as_ref(), eval::live::Moment::Hit { lethal: true, .. }) {
                            kills += 1;
                            lethal = true;
                        }
                    }
                    _ => {}
                }
            }
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
            peak_poses = peak_poses.max(view.gem_poses.len());
            let desired = [2.0, 10.0, 20.0];
            if saved >= 3 || s < desired[saved] || killable && !lethal {
                continue;
            }
            let run = view.run.ok_or("run missing")?;
            let period = 60.0 / 128.0;
            let pulse = scene::Pulse {
                beat: (s / period).fract(),
                bar: (s / (period * 2.0)).fract(),
                index: s / period,
            };
            let ground = world.field(&engine, run.presentation_s, period)?;
            scene.fast_lane(engine.stream(), 0.2, s, s, None);
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
            let upcoming: Vec<_> = beats
                .iter()
                .map(|b| b - s)
                .filter(|d| d.abs() < 2.5)
                .collect();
            let st = HudState {
                keys: [KeyLight::default(); 5],
                presses: &[],
                beats: &upcoming,
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
            hud::draw(&view, &st, &art, 1280.0, 720.0, &mut layers.overlay);
            art.fonts["pixel"].text(
                &format!(
                    "{} / {} ALIVE / {} GEMS",
                    name.to_uppercase(),
                    view.alive().count(),
                    view.gems.len()
                ),
                640.0,
                680.0,
                1.3,
                Align::Centre,
                [1.0; 4],
                &mut layers.overlay,
            );
            art.fonts["pixel"].text(if restored{"DIAGNOSTIC: 300 FOES / HERO EXEMPT / SIX MAX WEAPONS, CHAIN + TRAIL / SIX PASSIVES"}else if killable{"DIAGNOSTIC: MINUTE25 ROSTER / HP40 XP0 / HERO EXEMPT / FIVE EVOLUTIONS + SIX MAX PASSIVES"}else{"DIAGNOSTIC: MINUTE25 ROSTER / HP1E12 / HERO EXEMPT / FIVE EVOLUTIONS + SIX MAX PASSIVES"},640.0,705.0,0.9,Align::Centre,[0.85,0.9,1.0,1.0],&mut layers.overlay);
            gpu.draw(&Frame {
                clear: super::CLEAR,
                world: &layers.sprites,
                lights: &layers.lights,
                grade: layers.grade,
                overlay: &layers.overlay,
            });
            capture::write_png(
                &dir.join(format!("{name}-{saved}.png")),
                1280,
                720,
                &gpu.rgba()?,
            )?;
            writeln!(trace,"{}",serde_json::json!({"case":name,"song":s,"run":run.elapsed,"alive":view.alive().count(),"gems":view.gems.len(),"gemPoses":view.gem_poses.len(),"pickups":pickups,"presentedHits":hits,"presentedKills":kills,"aftermathMarks":view.aftermath.len(),"impactCounts":scene.impact_counts(),"sprites":layers.sprites.len(),"overlay":layers.overlay.len(),"lights":layers.lights.len(),"inventory":format!("{:?}",view.inventory)})).map_err(|e|e.to_string())?;
            if killable && saved == 2 {
                let mut isolated = view.clone();
                isolated.foes.clear();
                isolated.gems.clear();
                isolated.gem_poses.clear();
                isolated.missiles.clear();
                isolated.bolts.clear();
                isolated.fire_trail = None;
                let mut clean = Scene::default();
                clean.fast_lane(engine.stream(), 0.2, s, s, None);
                let mut marks = scene::Layers::default();
                clean.draw(
                    &isolated,
                    &ground,
                    &art,
                    &world.looks,
                    &world.lib,
                    s,
                    pulse,
                    1280.0,
                    720.0,
                    &mut marks,
                );
                art.fonts["pixel"].text(
                    "DIAGNOSTIC: ACTUAL VIEW DEATH MARKS / OTHER COMBAT EFFECTS HIDDEN",
                    640.0,
                    700.0,
                    1.0,
                    Align::Centre,
                    [0.85, 0.9, 1.0, 1.0],
                    &mut marks.overlay,
                );
                gpu.draw(&Frame {
                    clear: super::CLEAR,
                    world: &marks.sprites,
                    lights: &marks.lights,
                    grade: marks.grade,
                    overlay: &marks.overlay,
                });
                capture::write_png(
                    &dir.join("aftermath-isolation.png"),
                    1280,
                    720,
                    &gpu.rgba()?,
                )?;
            }
            saved += 1;
        }
        if saved != 3
            || hits == 0
            || moving && (pickups == 0 || peak_poses == 0)
            || killable && kills == 0
        {
            return Err(format!(
                "{name}: saved{saved} hits{hits} kills{kills} pickups{pickups} poses{peak_poses}"
            ));
        }
        eprintln!(
            "SP5 {name}:3 actual stills, hits{hits} kills{kills} pickups{pickups} peak moving poses{peak_poses}, impact budget{:?}",
            scene.impact_counts()
        );
    }
    Ok(())
}
