//! `ps report [<session>]`: a session's latency chain against the targets (docs/design/
//! native-app.md, "The latency report and QA (D5)" and "Targets"), from its `timing.jsonl`. Links
//! the timing log can't measure are listed as such. The newest session by default.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

use serde_json::Value;

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("ps report: {e}");
            2
        }
    }
}

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
    dirs.pop()
}

fn at(v: &mut [f64], p: f64) -> Option<f64> {
    if v.is_empty() {
        return None;
    }
    v.sort_by(f64::total_cmp);
    Some(v[((v.len() - 1) as f64 * p).round() as usize])
}

/// One row: the link, what was measured, the target, and whether it is met.
fn row(link: &str, measured: Option<String>, target: &str, met: Option<bool>) {
    println!(
        "{link:<44} {:<34} {target:<22} {}",
        measured.unwrap_or_else(|| "not measured here".into()),
        match met {
            Some(true) => "met",
            Some(false) => "OVER",
            None => "",
        }
    );
}

#[allow(clippy::too_many_lines)]
fn run(args: &[String]) -> Result<(), String> {
    let dir = args
        .first()
        .map(PathBuf::from)
        .or_else(newest)
        .ok_or("no session with a timing log")?;
    let path = dir.join("timing.jsonl");
    let text = std::fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    let (mut ticks, mut keys, mut frames, mut margins) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    let mut resim: BTreeMap<i64, f64> = BTreeMap::new();
    let mut replayed = 0.0;
    let (mut heard_after, mut l_out, mut device) = (0.0, None, String::new());
    let mut span = (f64::INFINITY, f64::NEG_INFINITY);
    for line in text.lines() {
        let Ok(v) = serde_json::from_str::<Value>(line) else {
            continue;
        };
        let num = |key: &str| v[key].as_f64().unwrap_or(0.0);
        match v["k"].as_str().unwrap_or("") {
            "dev" => {
                heard_after = num("heardAfter");
                l_out = Some(num("lOut"));
                device = v["name"].as_str().unwrap_or("?").to_string();
            }
            "arr" if v["g"] == "tick" => ticks.push(num("lag") * 1e3 + heard_after),
            "key" => keys.push(num("lag") * 1e3),
            "frame" => frames.push(num("build") + num("encode")),
            "snd" => margins.push(num("margin")),
            "rb" => {
                *resim.entry(num("s").floor() as i64).or_default() += num("resim");
                replayed += num("replayed");
            }
            _ => {}
        }
        if let Some(s) = v["s"].as_f64() {
            span = (span.0.min(s), span.1.max(s));
        }
    }
    let length = (span.1 - span.0).max(1e-9);
    println!("{}", dir.display());
    println!("output {device}, {length:.0} s of song\n");
    println!("{:<44} {:<34} {:<22}", "link", "measured", "target");
    let ms = |x: f64| format!("{x:.1} ms");
    let tick95 = at(&mut ticks, 0.95);
    row(
        "capture to arrival, from the capture's stamp",
        tick95.map(|x| format!("ticks p95 {}", ms(x))),
        "<= 48 ms (report)",
        tick95.map(|x| x <= 48.0),
    );
    let key95 = at(&mut keys, 0.95);
    row(
        "key event to main thread",
        key95.map(|x| format!("p95 {}", ms(x))),
        "<= 46 ms (gate)",
        key95.map(|x| x <= 46.0),
    );
    let frame99 = at(&mut frames, 0.99);
    row(
        "sim + evaluate + encode per frame",
        frame99.map(|x| format!("p99 {}", ms(x))),
        "<= 4 ms",
        frame99.map(|x| x <= 4.0),
    );
    let mut per_second: Vec<f64> = resim.values().copied().collect();
    let resim95 = at(&mut per_second, 0.95);
    row(
        "rollback re-simulation per second",
        resim95.map(|x| format!("p95 {} ({:.1}x song replayed)", ms(x), replayed / length)),
        "<= 250 ms; <= 7.2x",
        resim95.map(|x| x <= 250.0 && replayed / length <= 7.2),
    );
    row(
        "output latency (reported, corrected)",
        l_out.map(ms),
        "report",
        None,
    );
    let least = margins.iter().copied().fold(f64::INFINITY, f64::min);
    let late = margins.iter().filter(|&&m| m < 0.0).count();
    row(
        "sounds: margin before the output renders",
        (!margins.is_empty())
            .then(|| format!("least {}, {late} of {} late", ms(least), margins.len())),
        "none late",
        (!margins.is_empty()).then_some(late == 0),
    );
    for link in [
        "capture to arrival, the app's share",
        "hindsight decode to arrival",
        "analyzer real-time factor",
        "main thread to present",
        "committed beat to heard beat",
    ] {
        row(link, None, "see Targets", None);
    }
    Ok(())
}
