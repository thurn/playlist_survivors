//! Stills of a recorded showcase (`--showcase-stills <session>`, epic ps-8sb): the session's run
//! replayed offscreen (it reports whether its settled records match the live run's), with three stills:
//! the first level-up's cards, the biggest crowd, and the finale's blast.
use super::{HudState, KeyLight, Scene, World, damage, hud, scene};
use eval::live::Engine;
use eval::session::{AppliedKey, Arrival, rebase_append};
use render::{atlas::Atlases, capture, post::Frame};
use sim::movement::InputKey;
use std::path::Path;

fn lines<T: serde::de::DeserializeOwned>(p: &Path) -> Result<Vec<T>, String> {
    std::fs::read_to_string(p)
        .map_err(|e| format!("{}: {e}", p.display()))?
        .lines()
        .map(|s| serde_json::from_str(s).map_err(|e| e.to_string()))
        .collect()
}

/// The recorded run: its start (song s), its end, the live run's settled hash and count, and its
/// arrivals and keys.
struct Recorded {
    s0: f64,
    end: f64,
    hash: String,
    records: usize,
    arrivals: Vec<Arrival>,
    keys: Vec<(f64, InputKey, bool)>,
}

/// Replays `rec` on `engine` as the app ran it (arrivals from `super::RECENT_S` before the start,
/// keys as applied, 120 frames a second), calling `each` after every frame with the run time.
fn replay(
    engine: &mut Engine,
    rec: &Recorded,
    mut each: impl FnMut(&mut Engine, f64) -> Result<(), String>,
) -> Result<(), String> {
    let mut next = rec
        .arrivals
        .iter()
        .position(|a| a.arrived >= rec.s0 - super::RECENT_S)
        .unwrap_or(rec.arrivals.len());
    let mut next_key = 0;
    let span = rec.end - rec.s0;
    for frame in 1..=(span * 120.0).ceil() as u64 {
        let now = frame as f64 / 120.0;
        let fed = &rec.arrivals[next..];
        let n = fed.iter().take_while(|a| a.arrived - rec.s0 <= now).count();
        if n > 0 {
            engine.feed(|log| {
                for a in &fed[..n] {
                    rebase_append(log, &a.entry, rec.s0);
                }
            });
            next += n;
        }
        while next_key < rec.keys.len() && rec.keys[next_key].0 <= now {
            let (at, key, down) = rec.keys[next_key];
            engine.key(at, key, down);
            next_key += 1;
        }
        engine.frame(now, 0.0);
        each(engine, now)?;
    }
    // (A Delayed showcase's replay can settle differently from the live run, ps-8sb.1's notes:
    // the stills are then a near replay, and say so.)
    let hash = format!("{:016x}", engine.settled_hash);
    if hash != rec.hash || engine.settled_count < rec.records {
        eprintln!(
            "showcase-stills: not exact: the replay settled {hash}/{}, the live run {}/{}",
            engine.settled_count, rec.hash, rec.records
        );
    }
    Ok(())
}

pub fn stills(dir: &Path) -> Result<(), String> {
    let meta: serde_json::Value =
        serde_json::from_slice(&std::fs::read(dir.join("meta.json")).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let run = &meta["runs"][0];
    let num = |k: &str| run[k].as_f64().ok_or(format!("the run has no {k}"));
    let rec = Recorded {
        s0: num("s0")?,
        end: num("end")?,
        hash: run["settledHash"].as_str().ok_or("no hash")?.into(),
        records: run["settledRecords"].as_u64().ok_or("no record count")? as usize,
        arrivals: lines(&dir.join("stream.jsonl"))?,
        keys: lines::<AppliedKey>(&dir.join("keys.jsonl"))?
            .iter()
            .filter(|k| k.run == 0)
            .map(|k| InputKey::parse_recorded(&k.key).map(|key| (k.s, key, k.pressed)))
            .collect::<Result<_, _>>()?,
    };
    let delayed = if meta["listening"] == "delayed" {
        meta["m"].as_f64().unwrap_or(0.0)
    } else {
        0.0
    };
    let mut world = World::load(None)?;
    world.level.text = run["level"].as_str().ok_or("no level source")?.into();

    // First pass: when the moments are.
    let (mut cards, mut crowd) = (None, (0, 0.0));
    replay(&mut world.engine(true)?, &rec, |engine, now| {
        engine.view.borrow_mut().take_moments();
        let view = engine.view.borrow();
        let run = view.run.unwrap_or_default();
        if cards.is_none() && run.offer.is_some_and(|o| run.song >= o.reveals[2]) {
            cards = Some(now);
        }
        let alive = view.alive().count();
        if alive > crowd.0 && run.frozen.is_none() {
            crowd = (alive, now);
        }
        Ok(())
    })?;
    // (The finale's blast lands as its three-second epilogue begins.)
    let mut shots = vec![
        (crowd.1, "crowd.png"),
        ((rec.end - rec.s0 - 2.9).max(0.0), "finale.png"),
    ];
    shots.extend(cards.map(|s| (s, "cards.png")));
    shots.sort_by(|a, b| a.0.total_cmp(&b.0));

    let art = Atlases::load(&super::live::resource("assets", "../../../public/assets"))?;
    let (w, h) = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&art.pages, w, h)?;
    let mut scene = Scene::default();
    let mut next = 0;
    replay(&mut world.engine(true)?, &rec, |engine, s| {
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
        let Some(&(at, name)) = shots.get(next).filter(|(at, _)| s >= *at) else {
            return Ok(());
        };
        next += 1;
        let view = engine.view.borrow();
        let run = view.run.unwrap_or_default();
        let wave = view.wave.as_ref().map(|v| v.s);
        let (alive, health) = (view.alive().count(), view.health);
        drop(view);
        let last = engine.stream().beats.iter().rev().find(|b| b.t <= s);
        let period = last.map_or(world.lib.hero.stride.free_period_s, |b| b.period);
        let index = last.map_or(0.0, |b| {
            beats.partition_point(|t| *t <= b.t).saturating_sub(1) as f64
                + ((s - b.t) / period).clamp(0.0, 1.0)
        });
        let pulse = scene::Pulse {
            beat: index.fract(),
            bar: (index / 4.0).fract(),
            index,
        };
        let ground = world.field(engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, wave, delayed);
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
            next_down: false,
            tier_flash: scene.tier_flash(s),
            delayed,
            zen: true,
            rhythm: scene.hud_rhythm(),
            keys_visible: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(&view, &st, &art, w as f32, h as f32, &mut overlay);
        overlay.append(&mut layers.modal);
        gpu.draw(&Frame {
            clear: super::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        capture::write_png(&dir.join(name), w, h, &gpu.rgba()?)?;
        eprintln!(
            "showcase-stills: {name} at run {at:.2} s (song {:.2}), level {}, {alive} alive, health {health:.2}",
            s + rec.s0,
            run.level
        );
        Ok(())
    })?;
    if next < shots.len() {
        return Err(format!("only {next} of {} stills", shots.len()));
    }
    Ok(())
}
