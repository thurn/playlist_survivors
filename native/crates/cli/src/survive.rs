//! Playlist/cache input, stand-in player, and audio-free session output.
use crate::survive_cache::{self as cache, TrackStream};
use analysis::tensors::Tensors;
use eval::session::{AppliedKey, Arrival};
use serde_json::json;
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind, Observation, Player};
use std::collections::BTreeMap;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::Instant;
use stream::playlist::Playlist;
struct Options {
    playlist: PathBuf,
    fixture: Option<PathBuf>,
    write_fixture: Option<PathBuf>,
    record: Option<PathBuf>,
    cache: PathBuf,
    minutes: f64,
    jobs: usize,
    seed: u64,
    concatenated: bool,
    runner_soak: bool,
    only: Option<usize>,
}
fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        playlist: cache::root().join("native/playlists/survivors-30.m3u"),
        fixture: None,
        write_fixture: None,
        record: None,
        cache: std::env::var_os("PS_SURVIVE_CACHE").map_or_else(
            || {
                PathBuf::from(std::env::var_os("HOME").unwrap_or_default())
                    .join("Library/Caches/playlist-survivors/survive")
            },
            PathBuf::from,
        ),
        minutes: 30.0,
        jobs: 4,
        seed: 1,
        concatenated: false,
        runner_soak: false,
        only: None,
    };
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        let mut value = || args.next().ok_or_else(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--playlist" => o.playlist = value()?.into(),
            "--fixture" => o.fixture = Some(value()?.into()),
            "--write-fixture" => o.write_fixture = Some(value()?.into()),
            "--record" => o.record = Some(value()?.into()),
            "--cache" => o.cache = value()?.into(),
            "--minutes" => o.minutes = value()?.parse().map_err(|_| "invalid minutes")?,
            "--seed" => o.seed = value()?.parse().map_err(|_| "invalid seed")?,
            "--jobs" => o.jobs = value()?.parse().map_err(|_| "invalid jobs")?,
            "--only" => o.only = Some(value()?.parse().map_err(|_| "invalid track count")?),
            "--concatenated" => o.concatenated = true,
            "--runner-soak" => o.runner_soak = true,
            "--bot" => {
                if value()? != "held" {
                    return Err(
                        "SF8 supplies --bot held; SF9 adds positioning/afk/random-pick".into(),
                    );
                }
            }
            other => return Err(format!("unknown survive argument {other}")),
        }
    }
    if !o.minutes.is_finite() || !(0.0..=40.0).contains(&o.minutes) || o.minutes == 0.0 {
        return Err("minutes must be positive and <= 40".into());
    }
    if !(1..=16).contains(&o.jobs) {
        return Err("jobs must be 1..16".into());
    }
    if o.only == Some(0) {
        return Err("--only needs at least one track".into());
    }
    if o.concatenated && o.fixture.is_some() {
        return Err("choose --fixture or --concatenated".into());
    }
    Ok(o)
}
#[derive(Default)]
struct Holder {
    moving: bool,
    confirm_release: bool,
    picked: Option<(usize, u64)>,
}
impl Player for Holder {
    fn next_decision(&mut self, after: f64) -> f64 {
        (after.max(0.0) * 60.0).floor() / 60.0 + 1.0 / 60.0
    }
    fn decide(&mut self, obs: &Observation) -> Vec<KeyEvent> {
        let mut keys = Vec::new();
        let mut key = |key, pressed| {
            keys.push(KeyEvent {
                s: obs.s,
                key,
                kind: if pressed {
                    KeyEventKind::Press
                } else {
                    KeyEventKind::Release
                },
                id: None,
            });
        };
        if self.confirm_release {
            key(InputKey::Confirm, false);
            self.confirm_release = false;
        }
        if let Some(run) = obs.run {
            if let Some(offer) = run.offer {
                if self.moving {
                    key(InputKey::Right, false);
                    self.moving = false;
                }
                let token = (offer.level, offer.reveals[2].to_bits());
                if run.song >= offer.reveals[2] && self.picked != Some(token) {
                    key(InputKey::Confirm, true);
                    self.confirm_release = true;
                    self.picked = Some(token);
                }
                return keys;
            }
            self.picked = None;
            if run.frozen.is_some() {
                if self.moving {
                    key(InputKey::Right, false);
                    self.moving = false;
                }
                return keys;
            }
        }
        if !self.moving {
            key(InputKey::Right, true);
            self.moving = true;
        }
        keys
    }
}
fn prepare(o: &Options, playlist: &Playlist) -> Result<(Vec<Arrival>, f64), String> {
    if let Some(path) = &o.fixture {
        let fixture: TrackStream = serde_json::from_slice(
            &std::fs::read(path).map_err(|e| format!("{}: {e}", path.display()))?,
        )
        .map_err(|e| e.to_string())?;
        if !fixture.duration.is_finite()
            || fixture.duration <= 0.0
            || fixture
                .arrivals
                .windows(2)
                .any(|w| w[0].arrived > w[1].arrived)
        {
            return Err("invalid fixture duration/order".into());
        }
        return Ok((fixture.arrivals, fixture.duration));
    }
    let models = Tensors::load(&cache::root().join("native/models/madmom.bin"))
        .map_err(|e| e.to_string())?;
    if o.concatenated {
        let audio = playlist.decode(44100.0, |path| cache::decode(path, "concatenated"))?;
        let stream = cache::analyse(&models, &audio, "concatenated".into())?;
        return Ok((stream.arrivals, stream.duration));
    }
    let version = cache::version()?;
    let unique: Vec<PathBuf> = playlist
        .tracks
        .iter()
        .map(|t| t.path.clone())
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect();
    let mut streams = BTreeMap::<PathBuf, TrackStream>::new();
    for chunk in unique.chunks(o.jobs) {
        let results = std::thread::scope(|scope| {
            let handles: Vec<_> = chunk
                .iter()
                .map(|path| {
                    let models = &models;
                    let version = &version;
                    scope.spawn(move || {
                        cache::cached(path, &o.cache, version, models).map(|s| (path.clone(), s))
                    })
                })
                .collect();
            handles
                .into_iter()
                .map(|h| {
                    h.join()
                        .unwrap_or_else(|panic| std::panic::resume_unwind(panic))
                })
                .collect::<Vec<_>>()
        });
        for result in results {
            let (path, stream) = result?;
            streams.insert(path, stream);
        }
    }
    let mut all = Vec::new();
    let mut samples = 0u64;
    for track in &playlist.tracks {
        samples += (track.gap_before * 44100.0).round() as u64;
        let start = samples as f64 / 44100.0;
        let stream = &streams[&track.path];
        for a in &stream.arrivals {
            all.push(Arrival {
                arrived: a.arrived + start,
                entry: cache::offset(&a.entry, start),
            });
        }
        samples += (stream.duration * 44100.0).round() as u64;
    }
    all.sort_by(|a, b| a.arrived.total_cmp(&b.arrived));
    Ok((all, samples as f64 / 44100.0))
}
fn write_lines<T: serde::Serialize>(path: &Path, values: &[T]) -> Result<(), String> {
    let mut out = std::io::BufWriter::new(std::fs::File::create(path).map_err(|e| e.to_string())?);
    for value in values {
        serde_json::to_writer(&mut out, value).map_err(|e| e.to_string())?;
        writeln!(out).map_err(|e| e.to_string())?;
    }
    out.flush().map_err(|e| e.to_string())
}
fn run(args: &[String]) -> Result<(), String> {
    let o = options(args)?;
    crate::batch::enter()?;
    let started = Instant::now();
    let mut playlist = Playlist::load(&o.playlist)?;
    if let Some(n) = o.only {
        playlist.tracks.truncate(n);
    }
    if let Some(dir) = &o.record
        && dir.exists()
    {
        return Err(format!(
            "record directory already exists: {}",
            dir.display()
        ));
    }
    let (arrivals, duration) = prepare(&o, &playlist)?;
    let prepared = started.elapsed().as_secs_f64();
    if let Some(path) = &o.write_fixture {
        let prefix: Vec<_> = arrivals
            .iter()
            .take_while(|a| a.arrived <= 180.0)
            .cloned()
            .collect();
        if path.exists() {
            return Err(format!("fixture already exists: {}", path.display()));
        }
        let fixture = TrackStream {
            key: format!("Rust-Delayed-3s {}", cache::version()?),
            duration: duration.min(180.0),
            arrivals: prefix,
        };
        std::fs::write(
            path,
            serde_json::to_vec(&fixture).map_err(|e| e.to_string())?,
        )
        .map_err(|e| e.to_string())?;
    }
    let mut level = std::fs::read_to_string(cache::root().join("levels/survivors.yaml"))
        .map_err(|e| e.to_string())?;
    let seed_line = level
        .lines()
        .find(|line| line.trim_start().starts_with("seed:"))
        .map(str::to_string)
        .ok_or("survivors level has no seed")?;
    let spaces = seed_line.len() - seed_line.trim_start().len();
    level = level.replace(
        &seed_line,
        &format!("{}seed: {}", " ".repeat(spaces), o.seed),
    );
    let mut engine = crate::replay::engine_with_level(true, Some(&level))?;
    engine.key_log = Some(Vec::new());
    if o.runner_soak {
        engine.sim.disable_hero_damage_for_check();
    }
    let mut actor = Holder::default();
    let run_started = Instant::now();
    let report = eval::survive::drive(
        &mut engine,
        &arrivals,
        &mut actor,
        duration,
        o.minutes * 60.0,
    );
    let output = json!({"bot":"held","seed":o.seed,"requestedMinutes":o.minutes,"playlistSeconds":duration,"prepareWallSeconds":prepared,"runWallSeconds":run_started.elapsed().as_secs_f64(),"noHeroDamage":o.runner_soak,"streamMode":if o.fixture.is_some(){"fixture"}else if o.concatenated{"concatenated"}else{"cached-tracks"},"report":report});
    if let Some(dir) = &o.record {
        std::fs::create_dir(dir).map_err(|e| e.to_string())?;
        write_lines(&dir.join("stream.jsonl"), &arrivals)?;
        let keys: Vec<_> = engine
            .key_log
            .as_deref()
            .unwrap_or_default()
            .iter()
            .map(|k| AppliedKey {
                run: 0,
                s: k.s,
                key: format!("{:?}", k.key),
                pressed: k.kind == KeyEventKind::Press,
            })
            .collect();
        write_lines(&dir.join("keys.jsonl"), &keys)?;
        let meta = json!({"version":1,"listening":"delayed","m":3.0,"runs":[{"s0":0.0,"end":report.song_s,"zen":true,"level":level,"noHeroDamage":o.runner_soak,"settledHash":report.settled_hash,"settledRecords":report.settled_records}]});
        std::fs::write(
            dir.join("meta.json"),
            serde_json::to_vec_pretty(&meta).map_err(|e| e.to_string())?,
        )
        .map_err(|e| e.to_string())?;
    }
    println!(
        "{}",
        serde_json::to_string_pretty(&output).map_err(|e| e.to_string())?
    );
    Ok(())
}
pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("ps survive: {e}");
            2
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn held_actor_releases_at_cards_waits_for_reveal_then_represses() {
        use sim::survivors::{Card, FreezeReason, Item, Offer, WeaponId};
        let level = std::fs::read_to_string(cache::root().join("levels/survivors.yaml")).unwrap();
        let e = crate::replay::engine_with_level(true, Some(&level)).unwrap();
        let mut obs = e.sim.observe();
        let mut p = Holder::default();
        assert_eq!(p.decide(&obs)[0].kind, KeyEventKind::Press);
        let mut run = obs.run.unwrap();
        let card = Card {
            item: Item::Weapon(WeaponId::Sword),
            before: 1,
            after: 2,
        };
        run.offer = Some(Offer {
            level: 2,
            cards: [card; 3],
            reveals: [1.0, 1.5, 2.0],
        });
        run.frozen = Some(FreezeReason::LevelUp);
        run.song = 1.0;
        obs.run = Some(run);
        let keys = p.decide(&obs);
        assert_eq!(keys.len(), 1);
        assert_eq!(
            (keys[0].key, keys[0].kind),
            (InputKey::Right, KeyEventKind::Release)
        );
        run.song = 2.0;
        obs.run = Some(run);
        let keys = p.decide(&obs);
        assert_eq!(
            (keys[0].key, keys[0].kind),
            (InputKey::Confirm, KeyEventKind::Press)
        );
        let keys = p.decide(&obs);
        assert_eq!(keys.len(), 1);
        assert_eq!(
            (keys[0].key, keys[0].kind),
            (InputKey::Confirm, KeyEventKind::Release)
        );
        assert!(p.decide(&obs).is_empty());
        run.offer = None;
        run.frozen = None;
        obs.run = Some(run);
        let keys = p.decide(&obs);
        assert_eq!(
            (keys[0].key, keys[0].kind),
            (InputKey::Right, KeyEventKind::Press)
        );
    }
}
