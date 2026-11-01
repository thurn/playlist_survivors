//! The native trailer (`--trailer <session> [--trailer-spec <yaml>] [--out <mp4>]`, spec
//! `native/trailers/action.yaml`): about a minute of pure play to one continuous window of the
//! song, cut on its beats. Every frame is the game's own simulation, but each shot is staged for
//! it: its own short run of the trailer's level (`levels/trailer.yaml`) with the shot's zone,
//! crowd, set pieces and arsenal, started a few seconds before its cut so the crowd is there, the
//! showcase's virtual player at the keys, fed the recorded session's music stream. The picture is
//! the game's camera with no interface (no HUD, cards, gems, numbers, banners or chests); the
//! sound is the song and each shot's own sound effects. It ends on a cut to black while the song
//! fades out, in a 1080p60 H.264 video with AAC sound, built by `ffmpeg`.
use super::showcase::{Painter, Recorded, on_screen};
use super::{Scene, World, live, sounds};
use eval::session::rebase_append;
use render::capture;
use serde::Deserialize;
use serde_json::{Map, Value, json};
use sim::survivors::{PassiveId, WeaponId, weapon_catalog};
use std::io::Write;
use std::path::Path;
use std::process::{Child, Command, Stdio};

/// The sound's sample rate.
const RATE: f64 = 48_000.0;
/// The simulation's frames a second, as the app runs it.
const SIM_HZ: f64 = 120.0;
/// How far the simulation runs ahead of the frame shown, as the app runs it.
const LEAD: f64 = 0.2;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Spec {
    width: u32,
    height: u32,
    fps: u32,
    /// The song map whose beat grid positions are on, and the shots' base level (both relative
    /// to the repository).
    song_map: String,
    level: String,
    /// The first frame (song s), and the cut to black.
    start: Pos,
    end: Pos,
    /// The black after the cut, over which the song fades out (s).
    tail: f64,
    /// The crossfade of the sound effects at each cut (ms).
    crossfade_ms: f64,
    /// The song's level, and the game's sound effects' level, in the mix.
    music: f32,
    sfx: f32,
    /// How long each shot's run plays before its first frame (s), unless the shot says.
    warmup: f64,
    shots: Vec<Shot>,
}

/// A song position: seconds, or `"bar"` / `"bar:beat"` (both from 1) on the song map's grid.
#[derive(Deserialize, Clone)]
#[serde(untagged)]
enum Pos {
    Seconds(f64),
    Bar(String),
}

impl Pos {
    fn song(&self, beats: &[f64]) -> Result<f64, String> {
        let at = match self {
            Pos::Seconds(s) => return Ok(*s),
            Pos::Bar(at) => at,
        };
        let (bar, beat) = at.split_once(':').unwrap_or((at, "1"));
        let num = |s: &str| {
            s.trim()
                .parse::<usize>()
                .ok()
                .filter(|n| *n >= 1)
                .ok_or(format!("trailer: bad position {at:?}"))
        };
        let i = (num(bar)? - 1) * 4 + num(beat)? - 1;
        beats
            .get(i)
            .copied()
            .ok_or(format!("trailer: {at} is past the song map's beats"))
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Shot {
    /// Its cut in (the first shot starts at the spec's start).
    #[serde(default)]
    at: Option<Pos>,
    zone: String,
    /// Fields over the base schedule row (crowd, enemies, set pieces, widening).
    #[serde(default)]
    row: Map<String, Value>,
    /// Weapons (`id` or `id:rank`, max rank when absent; an evolution brings its base to max and
    /// its passive) and passives the hero starts with.
    #[serde(default)]
    arsenal: Vec<String>,
    #[serde(default)]
    passives: Vec<String>,
    /// Where the finale's blast lands (the run's deadline is set just before).
    #[serde(default)]
    finale: Option<Pos>,
    /// The virtual player's seed, and the level's.
    #[serde(default = "one")]
    player: u64,
    #[serde(default)]
    seed: Option<f64>,
    #[serde(default)]
    warmup: Option<f64>,
    /// Anything else over the level, merged key by key.
    #[serde(default)]
    level: Map<String, Value>,
}

fn one() -> u64 {
    1
}

/// Song time `s` as `bar:beat` on `beats`.
fn bar_of(s: f64, beats: &[f64]) -> String {
    let i = beats.partition_point(|b| *b <= s).saturating_sub(1);
    let period = beats.get(i + 1).map_or(0.5, |n| n - beats[i]);
    format!(
        "{}:{}+{:.2}",
        i / 4 + 1,
        i % 4 + 1,
        ((s - beats[i]) / period).clamp(0.0, 0.99)
    )
}

/// `over` merged into `base`: objects key by key, anything else replaced.
fn merge(base: &mut Value, over: &Value) {
    match (base, over) {
        (Value::Object(b), Value::Object(o)) => {
            for (k, v) in o {
                merge(b.entry(k.clone()).or_insert(Value::Null), v);
            }
        }
        (b, o) => *b = o.clone(),
    }
}

/// The shot's level: the base with its zone, its one schedule row, its seed and its deadline.
fn level_of(base: &Value, shot: &Shot, deadline: Option<f64>) -> Result<String, String> {
    let mut level = base.clone();
    // (A song-clock row, whose minimum is the crowd on screen, unless it has set pieces, which
    // need a run-clock row, whose minimum is the crowd alive. Its widening becomes the camera's
    // zoom either way.)
    let mut row = json!({"minute": 0, "xpScale": 0, "minimum": 0, "frequency": 250,
        "enemies": ["vs-skeleton"], "bosses": [], "events": [], "treasure": []});
    merge(&mut row, &Value::Object(shot.row.clone()));
    if row["events"].as_array().is_none_or(Vec::is_empty) {
        row["songS"] = json!(0);
    }
    let widen = row
        .as_object_mut()
        .and_then(|r| r.remove("widen"))
        .and_then(|w| w.as_f64())
        .unwrap_or(1.0);
    level["survivors"]["schedule"] = json!([row]);
    let zoom = |z: &Value| json!(z.as_f64().unwrap_or(1.2) / widen);
    level["sections"][0]["camera"]["zoom"] = zoom(&level["sections"][0]["camera"]["zoom"]);
    for loudness in ["quiet", "medium", "loud"] {
        let z = &level["director"]["camera"][loudness]["zoom"];
        level["director"]["camera"][loudness]["zoom"] = zoom(z);
    }
    if let Some(d) = deadline {
        level["survivors"]["durationS"] = json!(d);
    }
    level["zones"] = json!([shot.zone]);
    level["sections"][0]["zone"] = json!(shot.zone);
    if let Some(seed) = shot.seed {
        level["seed"] = json!(seed);
    }
    merge(&mut level, &Value::Object(shot.level.clone()));
    serde_json::to_string(&level).map_err(|e| e.to_string())
}

/// Gives the hero the shot's arsenal (at the run's start, before it has moved on).
fn arm(sim: &mut sim::engine::Sim, shot: &Shot) -> Result<(), String> {
    let id_of = |s: &str| -> Result<(String, Option<u8>), String> {
        let (id, rank) = s.split_once(':').map_or((s, None), |(i, r)| (i, Some(r)));
        let rank = rank
            .map(|r| {
                r.parse::<u8>()
                    .map_err(|_| format!("trailer: bad rank in {s:?}"))
            })
            .transpose()?;
        Ok((id.to_string(), rank))
    };
    let err = |e| format!("trailer: arsenal: {e:?}");
    let rank_of = |sim: &sim::engine::Sim, id: WeaponId| {
        sim.survivor_inventory()
            .and_then(|i| i.weapons.rank(id))
            .unwrap_or(0)
    };
    for p in &shot.passives {
        let (id, rank) = id_of(p)?;
        let id: PassiveId = serde_json::from_value(json!(id)).map_err(|e| e.to_string())?;
        let held = |sim: &sim::engine::Sim| {
            sim.survivor_inventory()
                .and_then(|i| i.passives.rank(id))
                .unwrap_or(0)
        };
        while held(sim) < rank.unwrap_or(id.max_rank()) {
            sim.learn_survivor_passive(id).map_err(err)?;
        }
    }
    for w in &shot.arsenal {
        let (id, rank) = id_of(w)?;
        let id: WeaponId = serde_json::from_value(json!(id)).map_err(|e| e.to_string())?;
        let def = weapon_catalog().get(id);
        if let Some(parent) = def.evolution_of {
            while rank_of(sim, parent) < weapon_catalog().get(parent).max_rank() {
                sim.learn_survivor_weapon(parent).map_err(err)?;
            }
            if let Some(passive) = def.requires {
                let held = sim
                    .survivor_inventory()
                    .and_then(|i| i.passives.rank(passive))
                    .is_some();
                if !held {
                    sim.learn_survivor_passive(passive).map_err(err)?;
                }
            }
            sim.evolve_survivor_weapon(id).map_err(err)?;
        } else {
            while rank_of(sim, id) < rank.unwrap_or(def.max_rank()) {
                sim.learn_survivor_weapon(id).map_err(err)?;
            }
        }
    }
    Ok(())
}

/// An `ffmpeg` taking raw RGBA frames on its input into an H.264 file.
fn encoder(file: &Path, (w, h): (u32, u32), fps: u32) -> Result<Child, String> {
    Command::new("ffmpeg")
        .args([
            "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s",
        ])
        .arg(format!("{w}x{h}"))
        .arg("-r")
        .arg(fps.to_string())
        .args([
            "-i",
            "-",
            "-vf",
            "scale=out_color_matrix=bt709:out_range=tv",
        ])
        .args(["-c:v", "libx264", "-preset", "slow", "-crf", "14"])
        .args(["-pix_fmt", "yuv420p", "-colorspace", "bt709"])
        .args(["-color_primaries", "bt709", "-color_trc", "bt709"])
        .arg(file)
        .stdin(Stdio::piped())
        .spawn()
        .map_err(|e| format!("ffmpeg: {e}"))
}

pub fn run(dir: &Path, spec_path: &Path, out: &Path, file: Option<&Path>) -> Result<(), String> {
    let spec: Spec = serde_saphyr::from_str(
        &std::fs::read_to_string(spec_path).map_err(|e| format!("{}: {e}", spec_path.display()))?,
    )
    .map_err(|e| format!("{}: {e}", spec_path.display()))?;
    if spec.fps == 0 || !(SIM_HZ as u32).is_multiple_of(spec.fps) {
        return Err(format!("trailer: fps must divide {SIM_HZ}"));
    }
    let step = u64::from(SIM_HZ as u32 / spec.fps);
    let fps = f64::from(spec.fps);
    let rec = Recorded::load(dir)?;
    let repo = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    let read = |p: &str| std::fs::read_to_string(repo.join(p)).map_err(|e| format!("{p}: {e}"));
    let map: Value = serde_json::from_str(&read(&spec.song_map)?).map_err(|e| e.to_string())?;
    let beats: Vec<f64> = map["beats"]
        .as_array()
        .ok_or("the song map has no beats")?
        .iter()
        .filter_map(Value::as_f64)
        .collect();
    let base: Value = serde_saphyr::from_str(&read(&spec.level)?).map_err(|e| e.to_string())?;

    // The trailer's frames: frame k shows song time start + k / fps. Each shot holds the frames
    // from its cut to the next.
    let start = spec.start.song(&beats)?;
    let frame_at = |p: &Pos| -> Result<u64, String> {
        let k = ((p.song(&beats)? - start) * fps).round();
        if k < 0.0 {
            return Err("trailer: a cut before the start".into());
        }
        Ok(k as u64)
    };
    let mut cuts = vec![0u64];
    for shot in spec.shots.iter().skip(1) {
        cuts.push(frame_at(
            shot.at.as_ref().ok_or("trailer: a shot without its cut")?,
        )?);
    }
    let black = frame_at(&spec.end)?;
    cuts.push(black);
    if cuts.windows(2).any(|w| w[1] <= w[0]) {
        return Err("trailer: the cuts must move forward".into());
    }
    let tail = (spec.tail * fps).round() as u64;
    let total = black + tail;
    let song_of = |k: u64| start + k as f64 / fps;

    let world0 = World::load(None)?;
    let (sfx0, bank) = sounds::Sounds::load(
        &live::resource("sfx", "../../../public/assets/sfx"),
        RATE,
        &world0.lib,
        true,
    )?;
    let mut painter = Painter::new(rec.delayed)?;
    painter.clean = true;
    let size = (spec.width, spec.height);
    let mut gpu = capture::Offscreen::new(&painter.art.pages, size.0, size.1)?;
    let temp = std::env::temp_dir().join(format!("ps-trailer-{}", std::process::id()));
    std::fs::create_dir_all(&temp).map_err(|e| e.to_string())?;
    let video = temp.join("picture.mp4");
    let mut enc = encoder(&video, size, spec.fps)?;
    let mut input = enc.stdin.take().ok_or("ffmpeg has no input")?;
    let xf = (spec.crossfade_ms / 1e3 * RATE).round() as i64;
    let mut effects_mix = vec![0.0f32; (total as f64 / fps * RATE).round() as usize * 2];
    let host0 = 1000.0;

    for (i, shot) in spec.shots.iter().enumerate() {
        let (first, last) = (cuts[i], cuts[i + 1]);
        let is_last = i + 1 == spec.shots.len();
        // The run starts `warmup` before the cut, on the simulation's frame grid: trailer frame
        // k is its frame (k - first) x step + lead_in.
        let lead_in = (shot.warmup.unwrap_or(spec.warmup) * SIM_HZ).round() as u64;
        let s0 = song_of(first) - lead_in as f64 / SIM_HZ;
        let sim_frame = |k: u64| (k - first) * step + lead_in;
        // (The last shot runs on through the black for its sound; every shot past its cut for
        // the crossfade.)
        let until = if is_last {
            sim_frame(total)
        } else {
            sim_frame(last) + (xf as f64 / RATE * SIM_HZ).ceil() as u64 + 1
        };
        let deadline = shot
            .finale
            .as_ref()
            .map(|p| p.song(&beats).map(|s| s - s0 - 0.01))
            .transpose()?;

        let mut world = World::load(None)?;
        world.level.text = level_of(&base, shot, deadline)?;
        let mut engine = world.engine(true)?;
        engine.sim.disable_hero_damage_for_check();
        let mut log = stream::StreamLog::default();
        for a in &rec.arrivals {
            rebase_append(&mut log, &a.entry, s0);
        }
        let mut replay = stream::Replay::new(log);
        let mut bot = players::SurvivorBot::new(players::SurvivorBotKind::Positioning, shot.player);
        let mut sfx = sfx0.clone();
        let mut mixer = audio::mixer::Mixer::new(RATE, bank.clone(), None, 1.0);
        let mut effects = Vec::<f32>::new();
        let mut report = |_| {};
        painter.scene = Scene::default();
        painter.scene.clean = true;
        // (The arsenal goes in at the very start, before any speculation could undo it.)
        engine.feed(|l| replay.feed_to(l, LEAD));
        engine.frame(0.0, 0.0);
        arm(&mut engine.sim, shot)?;
        let (mut blast, mut cleared, mut was_alive) = (None, None, 0);
        for f in 1..=until {
            let now = f as f64 / SIM_HZ;
            engine.feed(|l| replay.feed_to(l, now + LEAD));
            engine.frame(now, LEAD);
            if !engine.sim.done() {
                use sim::player::{KeyEventKind, Player};
                for k in bot.decide(&engine.sim.observe()) {
                    engine.key(k.s, k.key, k.kind == KeyEventKind::Press);
                }
            }
            painter.keep_up(&world, &mut engine, now);

            // The sound effects, as the app schedules them, on a clock hearing run time 0 at
            // host0.
            let caused: Vec<_> = engine.causes().cloned().collect();
            let alive = engine.view.borrow().alive().count();
            if shot.finale.is_some() && f >= lead_in {
                if blast.is_none() && alive * 2 < was_alive {
                    blast = Some(now + s0);
                }
                if cleared.is_none() && alive == 0 {
                    cleared = Some(now + s0);
                }
            }
            was_alive = alive;
            let played = effects.len() / 2;
            let camera = engine.view.borrow().camera;
            let at = sounds::At {
                seed: engine.seed,
                final_before: engine.sound_final_before(),
                run_start: s0,
                beats: &engine.stream().beats,
                camera,
                clock: (s0, host0),
                l_out: 0.0,
                never_late: sounds::NEVER_LATE,
                now: host0 + played as f64 / RATE,
                ahead: 0.0,
            };
            let mut commands = Vec::new();
            sfx.reconcile(&caused, &at, &mut commands);
            for c in commands {
                mixer.apply(c, &mut report);
            }
            let due = (now * RATE).floor() as usize;
            if due > played {
                let mut buffer = vec![0.0; (due - played) * 2];
                mixer.render(host0 + played as f64 / RATE, &mut buffer, &mut report);
                effects.extend_from_slice(&buffer);
            }

            // The picture, on this shot's frames.
            if f < lead_in || !(f - lead_in).is_multiple_of(step) {
                continue;
            }
            let k = first + (f - lead_in) / step;
            if k >= last {
                continue;
            }
            if k == first || k == last - 1 {
                let view = engine.view.borrow();
                eprintln!(
                    "trailer: shot {} {} at song {:.3} ({}): {} on screen, {} alive, zone {}",
                    i + 1,
                    if k == first { "opens" } else { "closes" },
                    song_of(k),
                    bar_of(song_of(k), &beats),
                    on_screen(&view),
                    view.alive().count(),
                    view.zone.as_deref().unwrap_or("none"),
                );
            }
            painter.draw(&world, &mut engine, &mut gpu, size, now)?;
            input.write_all(&gpu.rgba()?).map_err(|e| e.to_string())?;
        }
        for (what, at) in [("blast", blast), ("field cleared", cleared)] {
            if let Some(s) = at {
                eprintln!(
                    "trailer: shot {}'s finale: {what} at song {s:.3} ({})",
                    i + 1,
                    bar_of(s, &beats)
                );
            }
        }

        // Its sound effects into the trailer's, faded across its cuts.
        let at = (first as f64 / fps * RATE).round() as i64;
        let from = (lead_in as f64 / SIM_HZ * RATE).round() as i64;
        let span = &effects[(from as usize * 2).min(effects.len())..];
        eprintln!(
            "trailer: shot {}'s sound effects: rms {:.3}",
            i + 1,
            (span.iter().map(|x| x * x).sum::<f32>() / span.len().max(1) as f32).sqrt()
        );
        let n = ((if is_last { total } else { last } - first) as f64 / fps * RATE).round() as i64;
        let (pre, post) = (
            if i == 0 { 0 } else { xf / 2 },
            if is_last { 0 } else { xf - xf / 2 },
        );
        for j in -pre..n + post {
            let o = (at + j).max(0) as usize;
            let Some(slot) = effects_mix.get_mut(o * 2..o * 2 + 2) else {
                continue;
            };
            let mut g = 1.0f64;
            if i > 0 && j < xf - pre {
                g *= ((j + pre) as f64 / xf as f64 * std::f64::consts::FRAC_PI_2).sin();
            }
            if !is_last && j >= n - (xf - post) {
                g *= ((n + post - j) as f64 / xf as f64 * std::f64::consts::FRAC_PI_2).sin();
            }
            for (c, v) in slot.iter_mut().enumerate() {
                let e = effects
                    .get(((from + j) * 2) as usize + c)
                    .copied()
                    .unwrap_or(0.0);
                *v += g as f32 * e;
            }
        }
    }
    let frame = vec![0u8; (size.0 * size.1 * 4) as usize];
    for _ in 0..tail {
        input.write_all(&frame).map_err(|e| e.to_string())?;
    }
    drop(input);
    let status = enc.wait().map_err(|e| e.to_string())?;
    if !status.success() {
        return Err(format!("ffmpeg: {status}"));
    }

    // The sound: the song from the first frame under the effects, both faded out over the black.
    let song = file
        .map(Path::to_path_buf)
        .or_else(|| rec.file.as_deref().map(Into::into))
        .ok_or("trailer: the session names no music file: pass --file")?;
    let music = ::capture::decode::convert(&song, 2, RATE)?;
    let song0 = (start * RATE).round() as usize;
    let (fade_in, fade_from) = (0.01 * RATE, black as f64 / fps * RATE);
    let fade_len = (tail as f64 / fps * RATE).max(1.0);
    let mut mix = effects_mix;
    for (j, pair) in mix.chunks_mut(2).enumerate() {
        let x = j as f64;
        let mut g = (x / fade_in).min(1.0);
        if x >= fade_from {
            g *= (1.0 - (x - fade_from) / fade_len).clamp(0.0, 1.0).powi(2);
        }
        for (c, v) in pair.iter_mut().enumerate() {
            let m = music.get((song0 + j) * 2 + c).copied().unwrap_or(0.0);
            *v = g as f32 * (m * spec.music + *v * spec.sfx);
        }
    }
    let peak = mix.iter().fold(0.0f32, |p, x| p.max(x.abs()));
    if peak > 0.99 {
        let k = 0.99 / peak;
        for x in &mut mix {
            *x *= k;
        }
    }
    let pcm = temp.join("sound.f32");
    std::fs::write(
        &pcm,
        mix.iter()
            .flat_map(|x| x.to_le_bytes())
            .collect::<Vec<u8>>(),
    )
    .map_err(|e| e.to_string())?;
    if let Some(parent) = out.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    let status = Command::new("ffmpeg")
        .args(["-v", "error", "-y", "-i"])
        .arg(&video)
        .args(["-f", "f32le", "-ar", "48000", "-ac", "2", "-i"])
        .arg(&pcm)
        .args([
            "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "320k",
        ])
        .args(["-movflags", "+faststart"])
        .arg(out)
        .status()
        .map_err(|e| format!("ffmpeg: {e}"))?;
    if !status.success() {
        return Err(format!("ffmpeg: {status}"));
    }
    let _ = std::fs::remove_dir_all(&temp);
    eprintln!(
        "trailer: {} ({:.2} s, {} shots, sound peak {peak:.2} before trim)",
        out.display(),
        total as f64 / fps,
        spec.shots.len()
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn positions_are_bars_and_beats_on_the_grid_or_seconds() {
        let beats: Vec<f64> = (0..16).map(|i| f64::from(i) * 0.5).collect();
        assert_eq!(Pos::Bar("2".into()).song(&beats), Ok(2.0));
        assert_eq!(Pos::Bar("2:3".into()).song(&beats), Ok(3.0));
        assert_eq!(Pos::Seconds(1.25).song(&beats), Ok(1.25));
        assert!(Pos::Bar("5".into()).song(&beats).is_err());
        assert!(Pos::Bar("0:1".into()).song(&beats).is_err());
    }

    #[test]
    fn a_shot_level_takes_its_zone_row_zoom_and_deadline() {
        let base: Value = serde_saphyr::from_str(
            &std::fs::read_to_string(
                Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../levels/trailer.yaml"),
            )
            .unwrap(),
        )
        .unwrap();
        let shot: Shot = serde_saphyr::from_str(
            "zone: ember-hollow\nrow: { minimum: 300, widen: 1.2, enemies: [vs-bat3] }\n",
        )
        .unwrap();
        let level: Value =
            serde_json::from_str(&level_of(&base, &shot, Some(9.5)).unwrap()).unwrap();
        let row = &level["survivors"]["schedule"][0];
        assert_eq!(row["minimum"], json!(300));
        assert_eq!(row["songS"], json!(0), "no set pieces: the crowd on screen");
        assert!(row.get("widen").is_none());
        assert_eq!(level["zones"], json!(["ember-hollow"]));
        assert_eq!(level["sections"][0]["zone"], json!("ember-hollow"));
        assert!((level["sections"][0]["camera"]["zoom"].as_f64().unwrap() - 1.0).abs() < 1e-9);
        assert_eq!(level["survivors"]["durationS"], json!(9.5));

        let swarm: Shot = serde_saphyr::from_str(
            "zone: ember-hollow\nrow: { events: [{ kind: BAT_SWARM, delayMs: 0, extraRepeats: 0, durationMs: 1000, count: 5 }] }\n",
        )
        .unwrap();
        let level: Value = serde_json::from_str(&level_of(&base, &swarm, None).unwrap()).unwrap();
        assert!(
            level["survivors"]["schedule"][0].get("songS").is_none(),
            "set pieces run on the run clock"
        );
    }

    #[test]
    fn the_trailer_spec_loads() {
        let text = std::fs::read_to_string(
            Path::new(env!("CARGO_MANIFEST_DIR")).join("../../trailers/action.yaml"),
        )
        .unwrap();
        let spec: Spec = serde_saphyr::from_str(&text).unwrap();
        assert_eq!(spec.shots.len(), 8);
        assert!(spec.shots.iter().skip(1).all(|s| s.at.is_some()));
    }
}
