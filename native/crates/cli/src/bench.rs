//! `ps runner-bench`: the live engine's cost (docs/design/native-app.md, "Targets"). House-128's
//! stream replayed as it would arrive, frames at 120 Hz with a burst now and then, a press on each
//! committed beat reaching the runner 5 to 35 ms late, the simulation 0.2 s ahead: each frame's
//! engine time, and the rollbacks' re-simulation per second of play. `--check` fails above the
//! gates.

use std::path::{Path, PathBuf};
use std::time::Instant;

use eval::live::Engine;
use indexmap::IndexMap;
use sim::content::{SourceFile, load_content};
use sim::core::rng::{Part, Rng};
use sim::movement::InputKey;
use stream::{Replay, StreamFile};

/// The gates, each its first measurement's p95 plus half (docs/design/native-app.md, "Targets").
const FRAME_GATE_MS: f64 = 1.0;
const RESIM_GATE_MS: f64 = 31.0;
const LEAD: f64 = 0.2;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn source(rel: &str) -> Result<SourceFile, String> {
    Ok(SourceFile {
        name: rel.to_string(),
        text: std::fs::read_to_string(root().join(rel)).map_err(|e| format!("{rel}: {e}"))?,
    })
}

fn percentile(v: &mut [f64], p: f64) -> f64 {
    v.sort_by(f64::total_cmp);
    v.get(((v.len().saturating_sub(1)) as f64 * p).round() as usize)
        .copied()
        .unwrap_or(0.0)
}

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(ok) => i32::from(!ok),
        Err(e) => {
            eprintln!("ps runner-bench: {e}");
            2
        }
    }
}

fn run(args: &[String]) -> Result<bool, String> {
    let check = args.iter().any(|a| a == "--check");
    let seconds: f64 = args
        .iter()
        .position(|a| a == "--seconds")
        .and_then(|k| args.get(k + 1))
        .and_then(|v| v.parse().ok())
        .unwrap_or(90.0);
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .map_err(|e| e.to_string())?
        .filter_map(Result::ok)
        .map(|e| e.file_name().to_string_lossy().into_owned())
        .filter(|n| Path::new(n).extension().is_some_and(|x| x == "yaml"))
        .collect();
    names.sort();
    let files = names
        .iter()
        .map(|n| source(&format!("content/{n}")))
        .collect::<Result<Vec<_>, _>>()?;
    let lib = load_content(&files).map_err(|e| format!("content: {e:?}"))?;
    let file = StreamFile::parse(
        &std::fs::read_to_string(root().join("public/reactive/house-128/stream.json"))
            .map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.0)?;
    let mut engine = Engine::new(
        &source("levels/reactive.yaml")?,
        &lib,
        &IndexMap::new(),
        file.params.gate,
        file.log.hindsight.is_some(),
        f64::INFINITY,
    )
    .map_err(|e| format!("{e:?}"))?;
    let mut replay = Replay::new(file.log.clone());
    let mut r = Rng::new(&[Part::Str("runner-bench")]);
    let mut keys: Vec<(f64, f64, InputKey, bool)> = Vec::new();
    for (k, b) in file.log.beats.iter().enumerate() {
        let key = if k % 2 == 0 {
            InputKey::Right
        } else {
            InputKey::Down
        };
        let late = r.range(0.005, 0.035);
        keys.push((b.t + late, b.t, key, true));
        keys.push((b.t + 0.08 + late, b.t + 0.08, key, false));
    }
    keys.sort_by(|a, b| a.0.total_cmp(&b.0));
    let (mut now, mut next) = (0.0, 0);
    let mut frames = Vec::new();
    let mut resim_per_second = Vec::new();
    let (mut second, mut resim_at_second) = (1.0, 0.0);
    while now < seconds {
        // (120 Hz, with a stall of a few frames now and then.)
        now += if r.next() < 0.01 { 0.05 } else { 1.0 / 120.0 };
        engine.feed(|log| replay.feed_to(log, now));
        while next < keys.len() && keys[next].0 <= now {
            let (_, s, key, pressed) = keys[next];
            engine.key(s, key, pressed);
            next += 1;
        }
        let t0 = Instant::now();
        engine.frame(now, LEAD);
        frames.push(t0.elapsed().as_secs_f64() * 1e3);
        if now >= second {
            resim_per_second.push((engine.stats.resim_s - resim_at_second) * 1e3);
            resim_at_second = engine.stats.resim_s;
            second += 1.0;
        }
    }
    let n = frames.len();
    let (f50, f95, f99) = (
        percentile(&mut frames, 0.5),
        percentile(&mut frames, 0.95),
        percentile(&mut frames, 0.99),
    );
    let fmax = frames.last().copied().unwrap_or(0.0);
    let (r50, r95) = (
        percentile(&mut resim_per_second, 0.5),
        percentile(&mut resim_per_second, 0.95),
    );
    let s = engine.stats;
    println!("runner-bench: house-128, {seconds:.0} s, {n} frames at 120 Hz, lead {LEAD} s");
    println!(
        "  engine per frame: median {f50:.3} ms, p95 {f95:.3}, p99 {f99:.3}, max {fmax:.2} (gate p95 <= {FRAME_GATE_MS} ms)"
    );
    println!(
        "  re-simulation per second: median {r50:.1} ms, p95 {r95:.1} (gate p95 <= {RESIM_GATE_MS} ms; target 250)"
    );
    println!(
        "  {} rollbacks replaying {:.1} s of song ({:.1}x real time), {} late keys",
        s.rollbacks,
        s.replayed_s,
        s.replayed_s / seconds,
        s.late_keys
    );
    let ok = f95 <= FRAME_GATE_MS && r95 <= RESIM_GATE_MS;
    if check && !ok {
        eprintln!("runner-bench: over the gate");
    }
    Ok(!check || ok)
}
