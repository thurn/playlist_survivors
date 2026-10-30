//! A recorded showcase replayed offscreen (epic ps-8sb). `--showcase-stills <session>` replays the
//! session's run (failing unless its settled records are the live run's) for three stills:
//! the first level-up's cards, the biggest crowd, and the finale's blast. `--showcase-density
//! <session>` plays the session's music again on the showcase level as it is now, with the virtual
//! player at the keys, and measures each song section's crowd on screen, a still of each, and the
//! frame's work at the final peak.
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
    /// Delayed listening's delay (s), or 0.
    delayed: f64,
    /// The level source the run played.
    level: String,
}

impl Recorded {
    fn load(dir: &Path) -> Result<Recorded, String> {
        let meta: serde_json::Value = serde_json::from_slice(
            &std::fs::read(dir.join("meta.json")).map_err(|e| e.to_string())?,
        )
        .map_err(|e| e.to_string())?;
        let run = &meta["runs"][0];
        let num = |k: &str| run[k].as_f64().ok_or(format!("the run has no {k}"));
        Ok(Recorded {
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
            delayed: if meta["listening"] == "delayed" {
                meta["m"].as_f64().unwrap_or(0.0)
            } else {
                0.0
            },
            level: run["level"].as_str().ok_or("no level source")?.into(),
        })
    }
}

/// Replays `rec` on `engine` as the app ran it (arrivals from `super::RECENT_S` before the start,
/// 120 frames a second), with the recorded keys or, given `bot`, the virtual player pressing where
/// the simulation has run to, as the app's showcase does. Calls `each` after every frame with the
/// run time and the frame's engine work (this thread's CPU ms).
fn replay(
    engine: &mut Engine,
    rec: &Recorded,
    mut bot: Option<players::SurvivorBot>,
    mut each: impl FnMut(&mut Engine, f64, f64) -> Result<(), String>,
) -> Result<(), String> {
    use sim::player::{KeyEventKind, Player};
    let mut next = rec
        .arrivals
        .iter()
        .position(|a| a.arrived >= rec.s0 - super::RECENT_S)
        .unwrap_or(rec.arrivals.len());
    let mut next_key = 0;
    let span = rec.end - rec.s0;
    for frame in 1..=(span * 120.0).ceil() as u64 {
        let now = frame as f64 / 120.0;
        let t0 = super::thread_cpu_s();
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
        if bot.is_none() {
            while next_key < rec.keys.len() && rec.keys[next_key].0 <= now {
                let (at, key, down) = rec.keys[next_key];
                engine.key(at, key, down);
                next_key += 1;
            }
        }
        engine.frame(now, 0.0);
        if let Some(bot) = bot.as_mut()
            && !engine.sim.done()
        {
            for k in bot.decide(&engine.sim.observe()) {
                engine.key(k.s, k.key, k.kind == KeyEventKind::Press);
            }
        }
        let work = (super::thread_cpu_s() - t0) * 1e3;
        each(engine, now, work)?;
    }
    let hash = format!("{:016x}", engine.settled_hash);
    if bot.is_none() && (hash != rec.hash || engine.settled_count < rec.records) {
        return Err(format!(
            "showcase: not exact: the replay settled {hash}/{}, the live run {}/{}",
            engine.settled_count, rec.hash, rec.records
        ));
    }
    Ok(())
}

/// Offscreen drawing of a replay: the scene kept up every frame, and drawn when asked.
struct Painter {
    art: Atlases,
    scene: Scene,
    delayed: f64,
}

impl Painter {
    fn new(delayed: f64) -> Result<Painter, String> {
        Ok(Painter {
            art: Atlases::load(&super::live::resource("assets", "../../../public/assets"))?,
            scene: Scene::default(),
            delayed,
        })
    }

    /// The frame's moments and level, at run time `s`.
    fn keep_up(&mut self, world: &World, engine: &mut Engine, s: f64) {
        let beats: Vec<_> = engine.stream().beats.iter().map(|b| b.t).collect();
        let moments = engine.view.borrow_mut().take_moments();
        self.scene.moments(
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
        self.scene.level(&engine.view.borrow(), s, &world.lib);
    }

    /// Builds the frame at run time `s` and encodes it into `gpu` (the frame's scene and HUD, as
    /// the app draws them); returns the encode's CPU ms.
    fn draw(
        &mut self,
        world: &World,
        engine: &mut Engine,
        gpu: &mut capture::Offscreen,
        size: (u32, u32),
        s: f64,
    ) -> Result<f64, String> {
        let beats: Vec<_> = engine.stream().beats.iter().map(|b| b.t).collect();
        let run = engine.view.borrow().run.unwrap_or_default();
        let wave = engine.view.borrow().wave.as_ref().map(|v| v.s);
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
        self.scene
            .fast_lane(engine.stream(), 0.2, s, s, wave, self.delayed);
        let view = engine.view.borrow();
        let (w, h) = (size.0 as f32, size.1 as f32);
        let mut layers = scene::Layers::default();
        self.scene.draw(
            &view,
            &ground,
            &self.art,
            &world.looks,
            &world.lib,
            s,
            pulse,
            w,
            h,
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
            tier_flash: self.scene.tier_flash(s),
            delayed: self.delayed,
            zen: true,
            rhythm: self.scene.hud_rhythm(),
            keys_visible: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(&view, &st, &self.art, w, h, &mut overlay);
        overlay.append(&mut layers.modal);
        let t0 = super::thread_cpu_s();
        gpu.draw(&Frame {
            clear: super::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        Ok((super::thread_cpu_s() - t0) * 1e3)
    }
}

pub fn stills(dir: &Path) -> Result<(), String> {
    let rec = Recorded::load(dir)?;
    let mut world = World::load(None)?;
    world.level.text.clone_from(&rec.level);

    // First pass: when the moments are.
    let (mut cards, mut crowd) = (None, (0, 0.0));
    replay(&mut world.engine(true)?, &rec, None, |engine, now, _| {
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

    let mut painter = Painter::new(rec.delayed)?;
    let size = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&painter.art.pages, size.0, size.1)?;
    let mut next = 0;
    replay(&mut world.engine(true)?, &rec, None, |engine, s, _| {
        painter.keep_up(&world, engine, s);
        let Some(&(at, name)) = shots.get(next).filter(|(at, _)| s >= *at) else {
            return Ok(());
        };
        next += 1;
        painter.draw(&world, engine, &mut gpu, size, s)?;
        capture::write_png(&dir.join(name), size.0, size.1, &gpu.rgba()?)?;
        let view = engine.view.borrow();
        eprintln!(
            "showcase-stills: {name} at run {at:.2} s (song {:.2}), level {}, {} alive, health {:.2}",
            s + rec.s0,
            view.run.unwrap_or_default().level,
            view.alive().count(),
            view.health
        );
        Ok(())
    })?;
    if next < shots.len() {
        return Err(format!("only {next} of {} stills", shots.len()));
    }
    Ok(())
}

/// A song section of *Action* (songs/action/songmap.json): its label and start (song s).
struct Section {
    label: String,
    start: f64,
}

/// The song's sections, and its end (its last beat).
fn sections() -> Result<(Vec<Section>, f64), String> {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../songs/action/songmap.json");
    let map: serde_json::Value = serde_json::from_slice(
        &std::fs::read(&path).map_err(|e| format!("{}: {e}", path.display()))?,
    )
    .map_err(|e| e.to_string())?;
    let beats = map["beats"].as_array().ok_or("the song map has no beats")?;
    let per_bar = map["meter"][0]["beatsPerBar"].as_u64().unwrap_or(4) as usize;
    let end = beats
        .last()
        .and_then(serde_json::Value::as_f64)
        .ok_or("the song map has no beats")?;
    let sections = map["sections"]
        .as_array()
        .ok_or("the song map has no sections")?
        .iter()
        .map(|s| {
            let bar = s["bars"][0].as_u64().ok_or("a section without bars")? as usize;
            Ok(Section {
                label: s["label"].as_str().unwrap_or_default().into(),
                start: beats
                    .get((bar - 1) * per_bar)
                    .and_then(serde_json::Value::as_f64)
                    .ok_or("a section past the beats")?,
            })
        })
        .collect::<Result<_, String>>()?;
    Ok((sections, end))
}

/// demo2d's crowd on screen per section (demo2d/Assets/Demo2D/Data/action-level.json: each
/// section's `onScreen` fraction of its 400 envelope), by label.
fn demo2d_targets() -> Result<Vec<(String, f64)>, String> {
    let path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../demo2d/Assets/Demo2D/Data/action-level.json");
    let level: serde_json::Value = serde_json::from_slice(
        &std::fs::read(&path).map_err(|e| format!("{}: {e}", path.display()))?,
    )
    .map_err(|e| e.to_string())?;
    let envelope = level["envelope"]["onScreen"].as_f64().unwrap_or(400.0);
    Ok(level["sections"]
        .as_array()
        .ok_or("demo2d's level has no sections")?
        .iter()
        .map(|s| {
            (
                s["section"].as_str().unwrap_or_default().to_string(),
                s["onScreen"].as_f64().unwrap_or(0.0) * envelope,
            )
        })
        .collect())
}

/// One section's measurements: frames counted (unfrozen, once the section has settled), their
/// summed on-screen and alive counts and camera zooms, and the peak on screen.
#[derive(Default)]
struct Tally {
    frames: usize,
    on_screen: usize,
    alive: usize,
    peak: usize,
    zoom: f64,
}

/// The enemies inside the camera's view (the sim's 960 x 540 world view at the camera's zoom).
fn on_screen(view: &eval::live::LiveView) -> usize {
    let (cx, cy, zoom) = view.camera;
    let (hw, hh) = (
        sim::camera::VIEW_W / 2.0 / zoom,
        sim::camera::VIEW_H / 2.0 / zoom,
    );
    view.alive()
        .filter(|(_, f)| (f.x - cx).abs() <= hw && (f.y - cy).abs() <= hh)
        .count()
}

/// How many virtual players' seeds `--showcase-density` plays the level with.
const DENSITY_SEEDS: u64 = 5;

fn percentile(sorted: &[f64], p: f64) -> f64 {
    sorted
        .get(((sorted.len().max(1) - 1) as f64 * p) as usize)
        .copied()
        .unwrap_or(0.0)
}

/// `--showcase-density <session> [--level <path>]`: the recorded session's music on `level` with
/// the virtual player; prints each section's crowd against demo2d's and the final peak's frame
/// work, and writes `density.json` and a still per section to `<session>/density/`.
pub fn density(dir: &Path, level: &Path) -> Result<(), String> {
    let rec = Recorded::load(dir)?;
    let world = World::load(Some(level))?;
    let (sections, song_end) = sections()?;
    let targets = demo2d_targets()?;
    let out = dir.join("density");
    std::fs::create_dir_all(&out).map_err(|e| e.to_string())?;
    // Each section's span in run time (the run's song 0 is the recording's song s0); the last
    // ends with the song's last beat.
    let span = |i: usize| {
        let end = sections.get(i + 1).map_or(song_end, |n| n.start);
        (sections[i].start - rec.s0, end - rec.s0)
    };
    let section_at = |s: f64| (0..sections.len()).rev().find(|&i| span(i).0 <= s);
    let peak = sections
        .iter()
        .position(|s| s.label == "final-peak")
        .ok_or("no final-peak section")?;

    let mut painter = Painter::new(rec.delayed)?;
    let still = (960u32, 540u32);
    let bench = (1920u32, 1080u32);
    let mut still_gpu = capture::Offscreen::new(&painter.art.pages, still.0, still.1)?;
    let mut bench_gpu = capture::Offscreen::new(&painter.art.pages, bench.0, bench.1)?;
    let mut shot = vec![false; sections.len()];
    let (mut frame_ms, mut engine_ms, mut scene_ms, mut encode_ms) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    // Each virtual player's seed plays the level again: the crowd on screen moves with the hero's
    // kills and path, so one run is a sample. The first also draws the stills and is timed.
    let mut runs: Vec<Vec<Tally>> = Vec::new();
    for seed in 1..=DENSITY_SEEDS {
        let first = seed == 1;
        let mut tallies: Vec<Tally> = (0..sections.len()).map(|_| Tally::default()).collect();
        let bot = players::SurvivorBot::new(players::SurvivorBotKind::Positioning, seed);
        replay(
            &mut world.engine(true)?,
            &rec,
            Some(bot),
            |engine, s, work| {
                let t0 = super::thread_cpu_s();
                if first {
                    painter.keep_up(&world, engine, s);
                } else {
                    engine.view.borrow_mut().take_moments();
                }
                let Some(i) = section_at(s).filter(|&i| s < span(i).1) else {
                    return Ok(());
                };
                let (start, end) = span(i);
                // (The crowd is counted once a section has settled: past its first quarter, at
                // most 2 s.)
                let settled = s >= start + (0.25 * (end - start)).min(2.0);
                let (frozen, count, alive, zoom) = {
                    let view = engine.view.borrow();
                    let run = view.run.unwrap_or_default();
                    (
                        run.frozen.is_some(),
                        on_screen(&view),
                        view.alive().count(),
                        view.camera.2,
                    )
                };
                if !frozen && settled {
                    let t = &mut tallies[i];
                    t.frames += 1;
                    t.on_screen += count;
                    t.alive += alive;
                    t.peak = t.peak.max(count);
                    t.zoom += zoom;
                }
                if !first {
                    return Ok(());
                }
                if i == peak && !frozen && settled {
                    let encode = painter.draw(&world, engine, &mut bench_gpu, bench, s)?;
                    let scene = (super::thread_cpu_s() - t0) * 1e3 - encode;
                    engine_ms.push(work);
                    scene_ms.push(scene);
                    encode_ms.push(encode);
                    frame_ms.push(work + scene + encode);
                }
                if !shot[i] && !frozen && s >= start.midpoint(end) {
                    shot[i] = true;
                    painter.draw(&world, engine, &mut still_gpu, still, s)?;
                    let name = format!("{:02}-{}.png", i + 1, sections[i].label);
                    capture::write_png(&out.join(&name), still.0, still.1, &still_gpu.rgba()?)?;
                    eprintln!(
                        "showcase-density: {name} at run {s:.2} (song {:.2}): {count} on screen, {alive} alive, zoom {zoom:.3}",
                        s + rec.s0
                    );
                }
                Ok(())
            },
        )?;
        runs.push(tallies);
    }

    let mut rows = Vec::new();
    println!(
        "section      song s  demo2d  on screen  ratio  alive  peak   zoom  | ratio over {DENSITY_SEEDS} seeds: mean  min   max"
    );
    for (i, section) in sections.iter().enumerate() {
        let target = targets
            .iter()
            .find(|(l, _)| *l == section.label)
            .map_or(0.0, |(_, n)| *n);
        let ratio = |t: &Tally| {
            let mean = t.on_screen as f64 / t.frames.max(1) as f64;
            if target > 0.0 { mean / target } else { 0.0 }
        };
        let ratios: Vec<f64> = runs.iter().map(|r| ratio(&r[i])).collect();
        let mean_ratio = ratios.iter().sum::<f64>() / ratios.len() as f64;
        let (lo, hi) = ratios.iter().fold((f64::INFINITY, 0.0f64), |(lo, hi), &r| {
            (lo.min(r), hi.max(r))
        });
        let t = &runs[0][i];
        let n = t.frames.max(1) as f64;
        let (mean, alive) = (t.on_screen as f64 / n, t.alive as f64 / n);
        println!(
            "{:<12} {:>6.1}  {:>6.0}  {:>9.1}  {:>5.2}  {:>5.0}  {:>4}  {:>5.3}  |  {:>14.2} {:>4.2} {:>5.2}",
            section.label,
            section.start,
            target,
            mean,
            ratio(t),
            alive,
            t.peak,
            t.zoom / n,
            mean_ratio,
            lo,
            hi
        );
        rows.push(serde_json::json!({
            "section": section.label, "songS": section.start, "frames": t.frames,
            "demo2d": target, "onScreen": mean, "ratio": ratio(t), "alive": alive,
            "peakOnScreen": t.peak, "zoom": t.zoom / n, "still": shot[i],
            "seedRatios": ratios, "meanRatio": mean_ratio,
        }));
    }
    let mut stats = serde_json::Map::new();
    for (name, values) in [
        ("frame", &mut frame_ms),
        ("engine", &mut engine_ms),
        ("scene", &mut scene_ms),
        ("encode", &mut encode_ms),
    ] {
        values.sort_by(f64::total_cmp);
        let at = |p| percentile(values, p);
        println!(
            "final peak {name} CPU ms ({} frames at {}x{}): p50 {:.2}, p95 {:.2}, p99 {:.2}, max {:.2}",
            values.len(),
            bench.0,
            bench.1,
            at(0.5),
            at(0.95),
            at(0.99),
            at(1.0)
        );
        stats.insert(
            name.into(),
            serde_json::json!({"p50": at(0.5), "p95": at(0.95), "p99": at(0.99), "max": at(1.0)}),
        );
    }
    let summary = serde_json::json!({
        "session": dir.display().to_string(), "level": level.display().to_string(),
        "runStartSongS": rec.s0, "sections": rows, "finalPeakFrameMs": stats,
        "finalPeakFrames": frame_ms.len(),
    });
    std::fs::write(
        out.join("density.json"),
        serde_json::to_string_pretty(&summary).map_err(|e| e.to_string())? + "\n",
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

/// The hero's arsenal: each weapon (evolved ones by their own name) and passive with its rank.
fn arsenal(view: &eval::live::LiveView) -> Vec<String> {
    use sim::survivors::Item;
    let Some(inv) = view.inventory else {
        return Vec::new();
    };
    inv.weapons
        .iter()
        .map(|s| format!("{} {}", Item::Weapon(s.id).name(), s.rank))
        .chain(
            inv.passives
                .iter()
                .map(|s| format!("{} {}", Item::Passive(s.id).name(), s.rank)),
        )
        .collect()
}

/// One section's progression: the level it ends on, its level-ups and chests, the song time it
/// spent frozen on cards or a chest, and the arsenal it ends with.
#[derive(Default)]
struct Growth {
    level: usize,
    level_ups: usize,
    chests: usize,
    frozen_s: f64,
    arsenal: Vec<String>,
}

/// Sections whose middle `--showcase-arsenal` draws: the drops and the final peak.
const ARSENAL_STILLS: [&str; 4] = ["drop-1", "groove-2", "drop-2", "final-peak"];

/// `--showcase-arsenal <session> [--level <path>]`: the recorded session's music on `level` with
/// the virtual player, as `density` plays it; prints each section's progression (level, level-ups,
/// chests, time frozen, the arsenal it ends with) and writes `arsenal.json` and stills of the drops
/// and the final peak to `<session>/final/`.
pub fn arsenal_growth(dir: &Path, level: &Path) -> Result<(), String> {
    let rec = Recorded::load(dir)?;
    let world = World::load(Some(level))?;
    let (sections, song_end) = sections()?;
    let out = dir.join("final");
    std::fs::create_dir_all(&out).map_err(|e| e.to_string())?;
    let span = |i: usize| {
        let end = sections.get(i + 1).map_or(song_end, |n| n.start);
        (sections[i].start - rec.s0, end - rec.s0)
    };
    let section_at = |s: f64| (0..sections.len()).rev().find(|&i| span(i).0 <= s);
    let mut painter = Painter::new(rec.delayed)?;
    let still = (960u32, 540u32);
    let mut gpu = capture::Offscreen::new(&painter.art.pages, still.0, still.1)?;
    let mut shot = vec![false; sections.len()];
    // Each seed's level-ups, chests and time frozen over the whole song, and its level as the
    // final peak ends.
    let mut totals = Vec::new();
    let mut table: Vec<Growth> = Vec::new();
    for seed in 1..=DENSITY_SEEDS {
        let first = seed == 1;
        let mut growth: Vec<Growth> = (0..sections.len()).map(|_| Growth::default()).collect();
        let (mut last_level, mut last_roulette) = (1, None);
        // (The song time each chest opened.)
        let mut opened = Vec::new();
        let bot = players::SurvivorBot::new(players::SurvivorBotKind::Positioning, seed);
        replay(&mut world.engine(true)?, &rec, Some(bot), |engine, s, _| {
            if first {
                painter.keep_up(&world, engine, s);
            } else {
                engine.view.borrow_mut().take_moments();
            }
            let Some(i) = section_at(s).filter(|&i| s < span(i).1) else {
                return Ok(());
            };
            let (run, items) = {
                let view = engine.view.borrow();
                (view.run.unwrap_or_default(), arsenal(&view))
            };
            let g = &mut growth[i];
            g.level_ups += run.level.saturating_sub(last_level);
            last_level = last_level.max(run.level);
            g.level = last_level;
            let roulette = run.roulette.map(|r| r.id);
            if roulette.is_some() && roulette != last_roulette {
                g.chests += 1;
                opened.push(((s + rec.s0) * 10.0).round() / 10.0);
                last_roulette = roulette;
            }
            if run.frozen.is_some() {
                g.frozen_s += 1.0 / 120.0;
            }
            g.arsenal = items;
            let (start, end) = span(i);
            if first
                && !shot[i]
                && run.frozen.is_none()
                && s >= start.midpoint(end)
                && ARSENAL_STILLS.contains(&sections[i].label.as_str())
            {
                shot[i] = true;
                painter.draw(&world, engine, &mut gpu, still, s)?;
                let name = format!("{:02}-{}.png", i + 1, sections[i].label);
                capture::write_png(&out.join(&name), still.0, still.1, &gpu.rgba()?)?;
                eprintln!(
                    "showcase-arsenal: {name} at run {s:.2} (song {:.2}), level {}",
                    s + rec.s0,
                    run.level
                );
            }
            Ok(())
        })?;
        let peak = sections
            .iter()
            .position(|s| s.label == "final-peak")
            .ok_or("no final-peak section")?;
        totals.push(serde_json::json!({
            "seed": seed,
            "levelUps": growth.iter().map(|g| g.level_ups).sum::<usize>(),
            "chests": growth.iter().map(|g| g.chests).sum::<usize>(),
            "chestsOpenedAtSongS": opened,
            "frozenS": growth.iter().map(|g| g.frozen_s).sum::<f64>(),
            "finalPeakLevel": growth[peak].level,
            "finalPeakArsenal": growth[peak].arsenal,
        }));
        if first {
            table = growth;
        }
    }

    println!("section      song s  level  level-ups  chests  frozen s  | arsenal at its end");
    let mut rows = Vec::new();
    let mut before: Vec<String> = Vec::new();
    for (i, (section, g)) in sections.iter().zip(&table).enumerate() {
        // (What the section added: items new to the arsenal or a rank up.)
        let gained: Vec<&String> = g.arsenal.iter().filter(|a| !before.contains(a)).collect();
        println!(
            "{:<12} {:>6.1}  {:>5}  {:>9}  {:>6}  {:>8.1}  | {}",
            section.label,
            section.start,
            g.level,
            g.level_ups,
            g.chests,
            g.frozen_s,
            g.arsenal.join(", ")
        );
        rows.push(serde_json::json!({
            "section": section.label, "songS": section.start, "level": g.level,
            "levelUps": g.level_ups, "chests": g.chests, "frozenS": g.frozen_s,
            "gained": gained, "arsenal": g.arsenal, "still": shot[i],
        }));
        before.clone_from(&g.arsenal);
    }
    for t in &totals {
        println!("seed {}: {t}", t["seed"]);
    }
    let summary = serde_json::json!({
        "session": dir.display().to_string(), "level": level.display().to_string(),
        "runStartSongS": rec.s0, "sections": rows, "seeds": totals,
    });
    std::fs::write(
        out.join("arsenal.json"),
        serde_json::to_string_pretty(&summary).map_err(|e| e.to_string())? + "\n",
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}
