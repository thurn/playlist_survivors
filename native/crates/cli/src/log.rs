//! `ps log [<timing.jsonl>] [--from s] [--to s] [--no-table]`: a session's timing log summarised
//! (docs/design/native-app.md, "Timing logs (D4)"; `npm run log:play` as a reference): how late
//! each stream group arrives, keys reach the main thread, the rollbacks and what caused them, the
//! frames' work, the sounds' margins; and, unless `--no-table`, a line per second. The newest
//! session's log by default.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

use serde_json::Value;

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("ps log: {e}");
            2
        }
    }
}

/// The newest session's timing log.
fn newest() -> Option<PathBuf> {
    let root = std::env::var("PS_SESSIONS").map_or_else(
        |_| Path::new(env!("CARGO_MANIFEST_DIR")).join("../../logs/sessions"),
        PathBuf::from,
    );
    let mut dirs: Vec<PathBuf> = std::fs::read_dir(root)
        .ok()?
        .filter_map(Result::ok)
        .map(|e| e.path())
        .filter(|p| p.join("timing.jsonl").exists())
        .collect();
    dirs.sort();
    dirs.pop().map(|d| d.join("timing.jsonl"))
}

fn percentiles(v: &mut [f64]) -> String {
    if v.is_empty() {
        return "none".into();
    }
    v.sort_by(f64::total_cmp);
    let at = |p: f64| v[((v.len() - 1) as f64 * p).round() as usize];
    format!(
        "median {:.1}, p95 {:.1}, max {:.1} (n {})",
        at(0.5),
        at(0.95),
        v[v.len() - 1],
        v.len()
    )
}

#[derive(Default)]
struct Second {
    frames: usize,
    worst_ms: f64,
    rollbacks: u64,
    keys: usize,
    sounds: usize,
    cancels: usize,
}

#[allow(clippy::many_single_char_names, clippy::too_many_lines)]
fn run(args: &[String]) -> Result<(), String> {
    let mut path = None;
    let (mut from, mut to, mut table) = (f64::NEG_INFINITY, f64::INFINITY, true);
    let mut it = args.iter();
    while let Some(a) = it.next() {
        let num = |v: Option<&String>| {
            v.and_then(|v| v.parse::<f64>().ok())
                .ok_or(format!("{a} needs a number"))
        };
        match a.as_str() {
            "--from" => from = num(it.next())?,
            "--to" => to = num(it.next())?,
            "--no-table" => table = false,
            p => path = Some(PathBuf::from(p)),
        }
    }
    let path = path
        .or_else(newest)
        .ok_or("no timing log (and no session has one)")?;
    let text = std::fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    let mut arrivals: BTreeMap<String, Vec<f64>> = BTreeMap::new();
    let (mut keys, mut build, mut encode, mut margins) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    let (mut rollbacks, mut key_rollbacks, mut replayed, mut deepest, mut cancels) =
        (0u64, 0u64, 0.0f64, 0.0f64, 0usize);
    let mut seconds: BTreeMap<i64, Second> = BTreeMap::new();
    let mut devices = Vec::new();
    let mut span = (f64::INFINITY, f64::NEG_INFINITY);
    for line in text.lines() {
        let Ok(v) = serde_json::from_str::<Value>(line) else {
            continue;
        };
        let k = v["k"].as_str().unwrap_or("");
        if k == "dev" {
            devices.push(format!(
                "{} ({:.1} ms)",
                v["name"].as_str().unwrap_or("?"),
                v["lOut"].as_f64().unwrap_or(0.0)
            ));
            continue;
        }
        let Some(s) = v["s"].as_f64() else { continue };
        if s < from || s > to {
            continue;
        }
        span = (span.0.min(s), span.1.max(s));
        let sec = seconds.entry(s.floor() as i64).or_default();
        let num = |key: &str| v[key].as_f64().unwrap_or(0.0);
        match k {
            "arr" => arrivals
                .entry(v["g"].as_str().unwrap_or("?").to_string())
                .or_default()
                .push(num("lag") * 1e3),
            "key" => {
                keys.push(num("lag") * 1e3);
                sec.keys += 1;
            }
            "rb" => {
                let n = v["n"].as_u64().unwrap_or(0);
                rollbacks += n;
                key_rollbacks += v["keys"].as_u64().unwrap_or(0);
                replayed += num("replayed");
                deepest = deepest.max(num("deepest"));
                sec.rollbacks += n;
            }
            "frame" => {
                let (b, e) = (num("build"), num("encode"));
                build.push(b);
                if v["encode"].is_number() {
                    encode.push(e);
                }
                sec.frames += 1;
                sec.worst_ms = sec.worst_ms.max(b + e);
            }
            "snd" => {
                margins.push(num("margin"));
                sec.sounds += 1;
            }
            "cancel" => {
                cancels += 1;
                sec.cancels += 1;
            }
            _ => {}
        }
    }
    println!("{}", path.display());
    if span.0.is_finite() {
        println!("song time {:.1}-{:.1} s", span.0, span.1);
    }
    for d in &devices {
        println!("output {d}");
    }
    for (g, lags) in &mut arrivals {
        println!("arrival lag, {g} (ms): {}", percentiles(lags));
    }
    println!("key to main thread (ms): {}", percentiles(&mut keys));
    let length = (span.1 - span.0).max(1e-9);
    println!(
        "rollbacks: {rollbacks} ({:.1} a second; {key_rollbacks} for a key), {replayed:.1} s re-simulated, deepest {:.0} ms",
        rollbacks as f64 / length,
        deepest * 1e3
    );
    println!("frame, sim and evaluate (ms): {}", percentiles(&mut build));
    println!("frame, encode (ms): {}", percentiles(&mut encode));
    // (For margins, the short end matters: the least, the 5th percentile, the median.)
    margins.sort_by(f64::total_cmp);
    let low = |p: f64| margins[((margins.len() - 1) as f64 * p).round() as usize];
    if margins.is_empty() {
        println!("sound margin before the output renders it: none; {cancels} cancelled");
    } else {
        println!(
            "sound margin before the output renders it (ms): least {:.1}, p5 {:.1}, median {:.1} (n {}); {cancels} cancelled",
            margins[0],
            low(0.05),
            low(0.5),
            margins.len()
        );
    }
    if table {
        println!(
            "\n{:>6} {:>7} {:>9} {:>10} {:>5} {:>7} {:>7}",
            "s", "frames", "worst ms", "rollbacks", "keys", "sounds", "cancel"
        );
        for (s, x) in &seconds {
            println!(
                "{s:>6} {:>7} {:>9.2} {:>10} {:>5} {:>7} {:>7}",
                x.frames, x.worst_ms, x.rollbacks, x.keys, x.sounds, x.cancels
            );
        }
    }
    Ok(())
}
