//! `ps analysis-parity` (docs/design/native-app.md, "Corpus parity"): the Rust analyzer over each
//! track's audio, faster than real time in capture-sized blocks with the live hindsight delay,
//! against the committed `stream.json` Python made from the same samples.
//!
//!     ps analysis-parity                   # three tracks: a synthetic one, the Galop, one from the corpus
//!     ps analysis-parity --all --jobs 8    # every track (the release stage)
//!     ps analysis-parity --out DIR --check # write parity.json and parity.md; exit 1 outside the margins
//!     ps analysis-parity --all --no-live   # the margins only, without the live-delay run (half the time)
//!     ps analysis-parity --delayed 3       # also Delayed listening at M = 3 s (E1), with the lead check
//!     ps analysis-parity --all --delayed-sweep --out DIR  # every grid at every M, by category (delayed.md)

//!
//! The samples are Python's own (`ps_analysis.pcm`, cached under
//! `~/Library/Caches/playlist-survivors/pcm`), so decoders don't differ. The margin is the plan's:
//! committed-beat accuracy against each track's reference (within 45 ms, after 4 s, as
//! `stream.py`'s `accuracy`) at most 1 point under Python's per track and half a point in aggregate,
//! for the analyzer as Python runs it (hindsight usable as soon as decoded, D = 0); each track runs
//! again with the live D (0.67 s) for that delay's cost, the real-time factors and the arrival
//! delays.
#![allow(clippy::many_single_char_names, clippy::format_push_string)]

use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::beats::BeatParams;
use analysis::delayed::{self, DelayedParams, Grid};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use serde_json::{Value, json};

/// Capture-sized blocks (a Core Audio tap's buffer at 44.1-48 kHz).
const BLOCK: usize = 512;
const SR: f64 = 44100.0;
const DEFAULT_TRACKS: [&str; 3] = ["house-128", "orpheus-galop", "cc-kevin-macleod-go-cart"];
/// Per track, Rust's accuracy may trail Python's by at most this.
const MARGIN: f64 = 0.01;
/// In aggregate (beat-weighted), by at most this: about as accurate as Python's (the plan's
/// statistical judgement, 2026-10-24), not never worse.
const AGGREGATE_MARGIN: f64 = 0.005;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

struct Options {
    tracks: Vec<String>,
    jobs: usize,
    out: Option<PathBuf>,
    check: bool,
    live: bool,
    /// Delayed listening at these (grid, M), replayed from each track's live-delay run.
    delayed: Vec<(Grid, f64)>,
    sweep: bool,
}

/// The delays E1 measures (s).
const MS: [f64; 6] = [1.5, 2.0, 2.5, 3.0, 4.0, 5.0];

fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        tracks: DEFAULT_TRACKS.iter().map(|s| (*s).to_string()).collect(),
        jobs: std::thread::available_parallelism().map_or(2, |n| (n.get() / 2).max(1)),
        out: None,
        check: false,
        live: true,
        delayed: Vec::new(),
        sweep: false,
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--tracks" => {
                o.tracks = it
                    .next()
                    .ok_or("--tracks needs ids")?
                    .split(',')
                    .map(str::to_string)
                    .collect();
            }
            "--all" => o.tracks = all_tracks()?,
            "--jobs" => {
                o.jobs = it
                    .next()
                    .and_then(|n| n.parse().ok())
                    .ok_or("--jobs needs a number")?;
            }
            "--out" => o.out = Some(it.next().ok_or("--out needs a directory")?.into()),
            "--check" => o.check = true,
            "--no-live" => o.live = false,
            "--delayed" => {
                let m = it
                    .next()
                    .and_then(|n| n.parse().ok())
                    .ok_or("--delayed needs M in seconds")?;
                o.delayed = vec![(DelayedParams::chosen().grid, m)];
            }
            "--delayed-sweep" => {
                o.sweep = true;
                o.delayed = Grid::ALL
                    .iter()
                    .flat_map(|&g| MS.iter().map(move |&m| (g, m)))
                    .collect();
            }
            other => return Err(format!("unknown argument {other}")),
        }
    }
    // (Delayed listening replays the live-delay run.)
    if !o.delayed.is_empty() {
        o.live = true;
    }
    Ok(o)
}

fn read_json(path: &Path) -> Result<Value, String> {
    let text = std::fs::read_to_string(path).map_err(|e| format!("{}: {e}", path.display()))?;
    serde_json::from_str(&text).map_err(|e| format!("{}: {e}", path.display()))
}

fn index() -> Result<Value, String> {
    read_json(&root().join("public/reactive/index.json"))
}

fn all_tracks() -> Result<Vec<String>, String> {
    Ok(index()?["tracks"]
        .as_array()
        .ok_or("index.json: no tracks")?
        .iter()
        .filter_map(|t| t["id"].as_str().map(str::to_string))
        .collect())
}

/// FNV-1a over byte strings: the PCM cache's key.
fn fnv1a(parts: &[&[u8]]) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for part in parts {
        for &b in *part {
            h ^= u64::from(b);
            h = h.wrapping_mul(0x0100_0000_01b3);
        }
        // (A separator, so moving bytes between parts changes the key.)
        h ^= 0xff;
        h = h.wrapping_mul(0x0100_0000_01b3);
    }
    h
}

/// Python's samples of a track, decoded once into the cache, keyed by the audio file's bytes and
/// the decoder's inputs (`pcm.py`, `stream.py`'s `load_audio`, the analysis tool's lock file).
fn samples(id: &str, audio: &Path) -> Result<Vec<f32>, String> {
    let cache = std::env::var_os("PS_PCM_CACHE").map_or_else(
        || {
            PathBuf::from(std::env::var_os("HOME").unwrap_or_default())
                .join("Library/Caches/playlist-survivors/pcm")
        },
        PathBuf::from,
    );
    let read = |p: &Path| std::fs::read(p).map_err(|e| format!("{}: {e}", p.display()));
    let tool = root().join("tools/analysis");
    let key = fnv1a(&[
        &read(audio)?,
        &read(&tool.join("src/ps_analysis/pcm.py"))?,
        &read(&tool.join("src/ps_analysis/stream.py"))?,
        &read(&tool.join("uv.lock"))?,
    ]);
    let out = cache.join(format!("{id}-{key:016x}.f32"));
    if !out.exists() {
        let status = Command::new("uv")
            .args(["run", "--quiet", "--project"])
            .arg(root().join("tools/analysis"))
            .args(["python", "-m", "ps_analysis.pcm"])
            .arg(audio)
            .arg(&out)
            .stdout(std::process::Stdio::null())
            .status()
            .map_err(|e| format!("uv: {e}"))?;
        if !status.success() {
            return Err(format!("{id}: ps_analysis.pcm failed"));
        }
    }
    let bytes = std::fs::read(&out).map_err(|e| format!("{}: {e}", out.display()))?;
    Ok(bytes
        .as_chunks::<4>()
        .0
        .iter()
        .map(|b| f32::from_le_bytes(*b))
        .collect())
}

/// `stream.py`'s `accuracy`: committed beats at `t >= after` within the reference's span, each
/// against its nearest reference beat; (beats, within 20 ms, within 45 ms, on an off-beat).
fn accuracy(beats: &[f64], r: &[f64], after: f64) -> (usize, f64, f64, f64) {
    if r.len() < 2 {
        return (0, 0.0, 0.0, 0.0);
    }
    let n = r.len();
    let lo = after.max(r[0] - 0.5 * (r[1] - r[0]));
    let hi = r[n - 1] + 0.5 * (r[n - 1] - r[n - 2]);
    let t: Vec<f64> = beats
        .iter()
        .copied()
        .filter(|&b| lo <= b && b <= hi)
        .collect();
    if t.is_empty() {
        return (0, 0.0, 0.0, 0.0);
    }
    let mids: Vec<f64> = r.windows(2).map(|w| f64::midpoint(w[1], w[0])).collect();
    let (mut w20, mut w45, mut off) = (0, 0, 0);
    for &b in &t {
        let i = r.partition_point(|&x| x < b).clamp(1, n - 1);
        let e = if (b - r[i - 1]).abs() <= (b - r[i]).abs() {
            b - r[i - 1]
        } else {
            b - r[i]
        };
        let j = mids
            .partition_point(|&x| x < b)
            .clamp(1, mids.len().max(2) - 1);
        let em = (b - mids[j - 1])
            .abs()
            .min(mids.get(j).map_or(f64::INFINITY, |m| (b - m).abs()));
        let ae = e.abs();
        w20 += usize::from(ae <= 0.020);
        w45 += usize::from(ae <= 0.045);
        off += usize::from(ae > 0.045 && em <= 0.045);
    }
    let len = t.len() as f64;
    (
        t.len(),
        w20 as f64 / len,
        w45 as f64 / len,
        off as f64 / len,
    )
}

/// The share of committed beats (as `accuracy` selects them) within `tol` of their nearest
/// reference beat.
fn within(beats: &[f64], r: &[f64], after: f64, tol: f64) -> f64 {
    if r.len() < 2 {
        return f64::NAN;
    }
    let n = r.len();
    let lo = after.max(r[0] - 0.5 * (r[1] - r[0]));
    let hi = r[n - 1] + 0.5 * (r[n - 1] - r[n - 2]);
    let t: Vec<f64> = beats
        .iter()
        .copied()
        .filter(|&b| lo <= b && b <= hi)
        .collect();
    matched(&t, r, tol)
}

/// Beat F-measure of `ours` against `theirs` within `tol`: (precision, recall, F).
fn f_measure(ours: &[f64], theirs: &[f64], tol: f64) -> (f64, f64, f64) {
    let (p, r) = (matched(ours, theirs, tol), matched(theirs, ours, tol));
    let f = if p + r > 0.0 {
        2.0 * p * r / (p + r)
    } else {
        0.0
    };
    (p, r, f)
}

fn f(v: &Value, key: &str) -> f64 {
    v[key].as_f64().unwrap_or(f64::NAN)
}

fn list<'a>(v: &'a Value, path: &[&str]) -> Vec<&'a Value> {
    let mut x = v;
    for p in path {
        x = &x[*p];
    }
    x.as_array().map(|a| a.iter().collect()).unwrap_or_default()
}

/// The share of `a` with an element of `b` within `tol`.
fn matched(a: &[f64], b: &[f64], tol: f64) -> f64 {
    if a.is_empty() {
        return 1.0;
    }
    let mut sorted = b.to_vec();
    sorted.sort_by(f64::total_cmp);
    let hits = a
        .iter()
        .filter(|&&x| {
            let i = sorted.partition_point(|&y| y < x);
            [i.checked_sub(1), Some(i)]
                .into_iter()
                .flatten()
                .any(|k| sorted.get(k).is_some_and(|&y| (y - x).abs() <= tol + 1e-9))
        })
        .count();
    hits as f64 / a.len() as f64
}

fn mean(v: &[f64]) -> f64 {
    if v.is_empty() {
        f64::NAN
    } else {
        v.iter().sum::<f64>() / v.len() as f64
    }
}

fn percentile(v: &mut [f64], q: f64) -> f64 {
    if v.is_empty() {
        return f64::NAN;
    }
    v.sort_by(f64::total_cmp);
    v[((v.len() - 1) as f64 * q).round() as usize]
}

/// One run of the analyzer over a track.
struct Run {
    out: Vec<Entry>,
    wall: Duration,
    timings: analysis::analyzer::Timings,
    stalls: (usize, Duration),
    /// Per group, the wall time (ms) of each block that emitted one of its entries.
    compute: Vec<(&'static str, f64)>,
    /// Per entry, its group and how long after it was known (in audio time, ms) the analyzer had
    /// heard when it emitted it: the framing, the online origin's lookahead and the delay D.
    lags: Vec<(&'static str, f64)>,
}

fn analyse(models: &Tensors, y: &[f32], hindsight: HindsightParams) -> Result<Run, String> {
    let params = Params {
        hindsight,
        ..Params::default()
    };
    let mut an = Analyzer::with_hindsight_worker(models, params).map_err(|e| e.to_string())?;
    let mut out = Vec::new();
    let (mut compute, mut lags) = (Vec::new(), Vec::new());
    let started = Instant::now();
    let mut heard = 0;
    let mut note = |out: &[Entry], before: usize, heard: usize, ms: f64| {
        let mut groups: Vec<&'static str> = out[before..].iter().map(group).collect();
        groups.sort_unstable();
        groups.dedup();
        compute.extend(groups.into_iter().map(|g| (g, ms)));
        let now = heard as f64 / SR;
        lags.extend(
            out[before..]
                .iter()
                .map(|e| (group(e), (now - e.known_at()) * 1e3)),
        );
    };
    for block in y.chunks(BLOCK) {
        let before = out.len();
        let t = Instant::now();
        an.push(block, &mut out);
        heard += block.len();
        note(&out, before, heard, t.elapsed().as_secs_f64() * 1e3);
    }
    let before = out.len();
    let t = Instant::now();
    an.finish(&mut out);
    note(&out, before, heard, t.elapsed().as_secs_f64() * 1e3);
    Ok(Run {
        out,
        wall: started.elapsed(),
        timings: an.timings(),
        stalls: an.hindsight_stalls(),
        compute,
        lags,
    })
}

/// Python's `round(x, 4)` as `stream.json` stores times (to the nearest 1e-4; Python rounds the
/// decimal value exactly, which can differ in a last-place tie, far inside 45 ms).
fn round4(x: f64) -> f64 {
    (x * 1e4).round_ties_even() / 1e4
}

/// The committed beats' times as `stream.json` would store them.
fn committed(out: &[Entry]) -> Vec<f64> {
    out.iter()
        .filter_map(|e| {
            if let Entry::Beat(c) = e {
                Some(round4(c.t))
            } else {
                None
            }
        })
        .collect()
}

/// One track's comparison.
fn track(
    models: &Tensors,
    id: &str,
    audio: &Path,
    stream: &Value,
    with_live: bool,
    delayed_at: &[(Grid, f64)],
) -> Result<Value, String> {
    let y = samples(id, audio)?;
    let audio_s = y.len() as f64 / SR;
    let as_python = analyse(models, &y, HindsightParams::default())?;
    // (Without the live run, its timings and delays are the D = 0 run's.)
    let live_run = if with_live {
        Some(analyse(models, &y, HindsightParams::live())?)
    } else {
        None
    };
    let live = live_run.as_ref().unwrap_or(&as_python);
    let out = &as_python.out;
    let Run { wall, timings, .. } = live;
    let (wall, timings) = (*wall, *timings);
    let (stalls, stalled) = live.stalls;

    let mut beats = Vec::new();
    let mut evidence = Vec::new();
    let mut ticks = Vec::new();
    let mut onsets = Vec::new();
    let mut strong = 0;
    let (mut changes, mut jumps, mut ends, mut forecasts, mut timbre) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new(), Vec::new());
    for e in out {
        match e {
            Entry::Beat(c) => {
                beats.push(c.t);
                evidence.push(c.evidence);
            }
            Entry::Tick(t) => ticks.push(*t),
            Entry::Onset(o) => {
                onsets.push(o.t);
                strong += usize::from(o.strong);
            }
            Entry::Change(c) => changes.push(c.t),
            Entry::Jump(j) => jumps.push(j.t),
            Entry::End(e) => ends.push(e.t),
            Entry::Forecast(fc) => forecasts.push(fc.t),
            Entry::Timbre(t) => timbre.push(*t),
            Entry::Hindsight(_) => {}
        }
    }
    let py_beats: Vec<f64> = list(stream, &["beats"]).iter().map(|b| f(b, "t")).collect();
    let py_evidence: Vec<f64> = list(stream, &["beats"])
        .iter()
        .map(|b| f(b, "evidence"))
        .collect();
    let py_ticks = list(stream, &["ticks"]);
    let py_onsets: Vec<f64> = list(stream, &["onsets"])
        .iter()
        .map(|o| f(o, "t"))
        .collect();
    let py_strong = list(stream, &["onsets"])
        .iter()
        .filter(|o| o["strong"].as_bool() == Some(true))
        .count();
    let times =
        |path: &[&str]| -> Vec<f64> { list(stream, path).iter().map(|e| f(e, "t")).collect() };
    let reference: Vec<f64> = list(stream, &["reference", "beats"])
        .iter()
        .filter_map(|b| b.as_f64())
        .collect();
    let (n_rust, w20_rust, w45_rust, off_rust) = accuracy(&committed(out), &reference, 4.0);
    let (n_py, w20_py, w45_py, off_py) = accuracy(&py_beats, &reference, 4.0);
    let rust_d0 = committed(out);
    let rust_live = committed(&live.out);
    let w70 = |b: &[f64]| within(b, &reference, 4.0, 0.070);
    let (p70, r70, f70) = f_measure(&rust_d0, &py_beats, 0.070);
    let (lp70, lr70, lf70) = f_measure(&rust_live, &py_beats, 0.070);
    let (n_live, w20_live, w45_live, off_live) = if with_live {
        accuracy(&committed(&live.out), &reference, 4.0)
    } else {
        (0, f64::NAN, f64::NAN, f64::NAN)
    };

    let n = ticks.len().min(py_ticks.len());
    let diff = |a: f64, b: f64| (a - b).abs();
    let energy: Vec<f64> = (0..n)
        .map(|k| diff(ticks[k].energy, f(py_ticks[k], "energy")))
        .collect();
    let bands: Vec<f64> = (0..n)
        .flat_map(|k| {
            let b = ticks[k].bands;
            let w = &py_ticks[k]["bands"];
            [
                diff(b.kick, f(w, "kick")),
                diff(b.snare, f(w, "snare")),
                diff(b.hat, f(w, "hat")),
                diff(b.bass, f(w, "bass")),
            ]
        })
        .collect();
    let keys = (0..n)
        .filter(|&k| ticks[k].key.unwrap_or("") == py_ticks[k]["key"].as_str().unwrap_or(""))
        .count();
    let py_timbre = list(stream, &["timbre"]);
    let m = timbre.len().min(py_timbre.len());
    let timbre_diff: Vec<f64> = (0..m)
        .flat_map(|k| {
            let (a, w) = (timbre[k], py_timbre[k]);
            [
                diff(a.brightness, f(w, "brightness")),
                diff(a.flatness, f(w, "flatness")),
                diff(a.percussive, f(w, "percussive")),
            ]
        })
        .collect();
    let rtf = |d: Duration| d.as_secs_f64() / audio_s;
    let mut by_group = serde_json::Map::new();
    for g in [
        "beats",
        "ticks",
        "onsets",
        "hindsight",
        "structure",
        "forecasts",
        "timbre",
    ] {
        let of = |v: &[(&'static str, f64)]| -> Vec<f64> {
            v.iter()
                .filter(|(x, _)| *x == g)
                .map(|(_, ms)| *ms)
                .collect()
        };
        let (mut lag, mut ms) = (of(&live.lags), of(&live.compute));
        by_group.insert(
            g.into(),
            json!({
                "lagP50Ms": percentile(&mut lag, 0.5),
                "lagP99Ms": percentile(&mut lag, 0.99),
                "entries": lag.len(),
                "blockComputeP50Ms": percentile(&mut ms, 0.5),
                "blockComputeP99Ms": percentile(&mut ms, 0.99),
            }),
        );
    }
    // Delayed listening (E1), replayed from the live-delay run: accuracy and recall against the
    // reference, beside Direct listening's at the same D, and the lead check.
    let recall = |b: &[f64]| {
        let r: Vec<f64> = reference.iter().copied().filter(|&x| x >= 4.0).collect();
        if r.is_empty() {
            f64::NAN
        } else {
            matched(&r, b, 0.045)
        }
    };
    let mut delayed_rows = serde_json::Map::new();
    let bp = BeatParams::default();
    for &(grid, m) in delayed_at {
        let p = DelayedParams {
            m,
            grid,
            ..DelayedParams::chosen()
        };
        let commits = delayed::replay(&live.out, audio_s, p, &HindsightParams::live(), &bp);
        let times: Vec<f64> = commits.iter().map(|c| round4(c.t)).collect();
        let (n, _, w45, _) = accuracy(&times, &reference, 4.0);
        let short = commits
            .iter()
            .filter(|c| c.t - c.known_at < bp.commit_periods * c.period + bp.commit_extra_s - 1e-9)
            .count();
        let row = delayed_rows
            .entry(grid.name())
            .or_insert_with(|| json!({}))
            .as_object_mut()
            .expect("an object");
        row.insert(
            format!("{m}"),
            json!({"beats": n, "within45": w45, "recall": recall(&times), "shortLead": short}),
        );
    }
    Ok(json!({
        "track": id,
        "audioS": audio_s,
        "delayed": if delayed_at.is_empty() { Value::Null } else { json!({
            "direct": {"within45": w45_live, "recall": recall(&rust_live)},
            "grids": delayed_rows,
        }) },
        "accuracy": {
            "rust": {"beats": n_rust, "within20": w20_rust, "within45": w45_rust, "within70": w70(&rust_d0), "offbeat45": off_rust},
            "python": {"beats": n_py, "within20": w20_py, "within45": w45_py, "within70": w70(&py_beats), "offbeat45": off_py},
            "rustLiveDelay": {"beats": n_live, "within20": w20_live, "within45": w45_live, "within70": if with_live { w70(&rust_live) } else { f64::NAN }, "offbeat45": off_live},
        },
        // Committed beats against Python's, within 70 ms.
        "agreement": {
            "rust": {"precision70": p70, "recall70": r70, "f70": f70},
            "rustLiveDelay": if with_live { json!({"precision70": lp70, "recall70": lr70, "f70": lf70}) } else { Value::Null },
        },
        "beats": {"rust": beats.len(), "python": py_beats.len(),
                  "evidenceMean": {"rust": mean(&evidence), "python": mean(&py_evidence)}},
        "ticks": {"rust": ticks.len(), "python": py_ticks.len(), "energyMeanAbsDiff": mean(&energy),
                  "bandsMeanAbsDiff": mean(&bands), "keysEqual": if n > 0 { keys as f64 / n as f64 } else { 1.0 }},
        "onsets": {"rust": onsets.len(), "python": py_onsets.len(), "strongRust": strong, "strongPython": py_strong,
                   "recall10ms": matched(&py_onsets, &onsets, 0.010), "precision10ms": matched(&onsets, &py_onsets, 0.010)},
        "structure": {
            "changes": {"rust": changes.len(), "python": times(&["structure", "changes"]).len(),
                        "matched100ms": matched(&times(&["structure", "changes"]), &changes, 0.1)},
            "jumps": {"rust": jumps.len(), "python": times(&["structure", "jumps"]).len(),
                      "matched100ms": matched(&times(&["structure", "jumps"]), &jumps, 0.1)},
            "ends": {"rust": ends.len(), "python": times(&["structure", "ends"]).len(),
                     "matched100ms": matched(&times(&["structure", "ends"]), &ends, 0.1)},
        },
        "forecasts": {"rust": forecasts.len(), "python": times(&["accents", "forecast"]).len(),
                      "matched1ms": matched(&times(&["accents", "forecast"]), &forecasts, 0.001)},
        "timbre": {"rust": timbre.len(), "python": py_timbre.len(), "meanAbsDiff": mean(&timbre_diff)},
        "realTimeFactor": {
            "whole": rtf(wall), "frontend": rtf(timings.frontend()), "rnn": rtf(timings.rnn),
            "beats": rtf(timings.beats), "onsets": rtf(timings.onsets), "bands": rtf(timings.bands),
            "structure": rtf(timings.structure), "hindsightWithWaits": rtf(timings.hindsight), "accents": rtf(timings.accents),
        },
        "hindsightStalls": {"count": stalls, "s": stalled.as_secs_f64()},
        // (Faster than real time the analysis thread waits for decodes due; live it doesn't.)
        "computeRealTimeFactor": rtf(wall.saturating_sub(stalled)),
        "arrivalDelay": by_group,
    }))
}

fn group(e: &Entry) -> &'static str {
    match e {
        Entry::Beat(_) => "beats",
        Entry::Tick(_) => "ticks",
        Entry::Onset(_) => "onsets",
        Entry::Hindsight(_) => "hindsight",
        Entry::Change(_) | Entry::Jump(_) | Entry::End(_) => "structure",
        Entry::Forecast(_) => "forecasts",
        Entry::Timbre(_) => "timbre",
    }
}

fn markdown(rows: &[Value], aggregate: &Value) -> String {
    let mut s = String::from(
        "| Track | s | Beats (Rust/Py) | Within 45 ms (Rust/Py) | At D = 0.67 s | Within 70 ms (Rust/Py) | F vs Python (D = 0 / 0.67 s) | Onsets recall | Changes | Forecasts | RTF |\n|---|---|---|---|---|---|---|---|---|---|---|\n",
    );
    for r in rows {
        let a = &r["accuracy"];
        s += &format!(
            "| {} | {:.0} | {}/{} | {:.3}/{:.3} | {:.3} | {:.3}/{:.3} | {:.3}/{:.3} | {:.3} | {}/{} | {}/{} | {:.3} |\n",
            r["track"].as_str().unwrap_or(""),
            f(r, "audioS"),
            r["beats"]["rust"],
            r["beats"]["python"],
            f(&a["rust"], "within45"),
            f(&a["python"], "within45"),
            f(&a["rustLiveDelay"], "within45"),
            f(&a["rust"], "within70"),
            f(&a["python"], "within70"),
            f(&r["agreement"]["rust"], "f70"),
            f(&r["agreement"]["rustLiveDelay"], "f70"),
            f(&r["onsets"], "recall10ms"),
            r["structure"]["changes"]["rust"],
            r["structure"]["changes"]["python"],
            r["forecasts"]["rust"],
            r["forecasts"]["python"],
            f(&r["realTimeFactor"], "whole"),
        );
    }
    s += &format!(
        "| **All** | {:.0} | | {:.3}/{:.3} | {:.3} | | | | | | |\n",
        f(aggregate, "audioS"),
        f(aggregate, "within45Rust"),
        f(aggregate, "within45Python"),
        f(aggregate, "within45RustLiveDelay"),
    );
    s
}

/// Delayed listening's summary: per category (index.json's groups, and `all`), the mean of tracks'
/// accuracy and recall for Direct listening at the live D and each grid and M; E1's choice (the
/// grid best in aggregate at its best M that is no worse than Direct on any category, and the
/// smallest M within a point of that grid's best, to 0.5 s); and whether every beat led by H.
fn delayed_summary(rows: &[Value], index: &Value, at: &[(Grid, f64)]) -> ((String, Value), bool) {
    let group_of = |id: &str| -> String {
        index["tracks"]
            .as_array()
            .and_then(|ts| ts.iter().find(|t| t["id"].as_str() == Some(id)))
            .and_then(|t| t["group"].as_str())
            .unwrap_or("unlisted")
            .to_string()
    };
    let mut groups: Vec<String> = rows
        .iter()
        .map(|r| group_of(r["track"].as_str().unwrap_or("")))
        .collect();
    groups.sort();
    groups.dedup();
    groups.insert(0, "all".into());
    let in_group = |g: &str| -> Vec<&Value> {
        rows.iter()
            .filter(|r| g == "all" || group_of(r["track"].as_str().unwrap_or("")) == g)
            .collect()
    };
    let mean_of = |rs: &[&Value], pick: &dyn Fn(&Value) -> f64| -> f64 {
        let v: Vec<f64> = rs
            .iter()
            .map(|r| pick(r))
            .filter(|x| x.is_finite())
            .collect();
        mean(&v)
    };
    let cell = |r: &Value, g: Grid, m: f64, k: &str| {
        f(&r["delayed"]["grids"][g.name()][format!("{m}")], k)
    };
    let short: f64 = rows
        .iter()
        .flat_map(|r| at.iter().map(move |&(g, m)| cell(r, g, m, "shortLead")))
        .filter(|x| x.is_finite())
        .sum();
    let mut md = String::from("| Category | Tracks | Direct |");
    for &(g, m) in at {
        md += &format!(" {} {m} |", g.name());
    }
    md += "\n|---|---|---|";
    md += &"---|".repeat(at.len());
    md += "\n";
    let mut table = serde_json::Map::new();
    let mut acc = std::collections::BTreeMap::new();
    for g in &groups {
        let rs = in_group(g);
        let direct = mean_of(&rs, &|r| f(&r["delayed"]["direct"], "within45"));
        let direct_recall = mean_of(&rs, &|r| f(&r["delayed"]["direct"], "recall"));
        md += &format!(
            "| {g} | {} | {:.1} ({:.1}) |",
            rs.len(),
            100.0 * direct,
            100.0 * direct_recall
        );
        let mut cells = serde_json::Map::new();
        for &(grid, m) in at {
            let w = mean_of(&rs, &|r| cell(r, grid, m, "within45"));
            let rc = mean_of(&rs, &|r| cell(r, grid, m, "recall"));
            md += &format!(" {:.1} ({:.1}) |", 100.0 * w, 100.0 * rc);
            cells.insert(
                format!("{} {m}", grid.name()),
                json!({"within45": w, "recall": rc}),
            );
            acc.insert((g.clone(), grid.name(), (m * 10.0).round() as i64), w);
        }
        md += "\n";
        table.insert(g.clone(), json!({"tracks": rs.len(), "direct": {"within45": direct, "recall": direct_recall}, "delayed": cells}));
    }
    md += "\n(Within 45 ms of the reference, mean of tracks, %; recall in brackets.)\n";
    // E1's choice.
    let direct_of = |g: &str| f(&table[g]["direct"], "within45");
    let mut choice = Value::Null;
    let mut best: Option<(f64, Grid)> = None;
    for &grid in &Grid::ALL {
        let ms: Vec<f64> = at.iter().filter(|x| x.0 == grid).map(|x| x.1).collect();
        let Some(&top) = ms.iter().max_by(|a, b| {
            acc[&("all".to_string(), grid.name(), (**a * 10.0).round() as i64)]
                .total_cmp(&acc[&("all".to_string(), grid.name(), (**b * 10.0).round() as i64)])
        }) else {
            continue;
        };
        let at_top = |g: &str| acc[&(g.to_string(), grid.name(), (top * 10.0).round() as i64)];
        let fair = groups.iter().all(|g| at_top(g) >= direct_of(g) - 1e-9);
        if fair && best.is_none_or(|(b, _)| at_top("all") > b) {
            best = Some((at_top("all"), grid));
        }
    }
    if let Some((top, grid)) = best {
        let m = at
            .iter()
            .filter(|x| x.0 == grid)
            .map(|x| x.1)
            .filter(|&m| {
                acc[&("all".to_string(), grid.name(), (m * 10.0).round() as i64)]
                    >= top - 0.01 - 1e-9
            })
            .fold(f64::INFINITY, f64::min);
        let m = (m * 2.0).round() / 2.0;
        md += &format!(
            "\nChoice: {} at M = {m} s ({:.1}% in aggregate; Direct {:.1}%).\n",
            grid.name(),
            100.0 * acc[&("all".to_string(), grid.name(), (m * 10.0).round() as i64)],
            100.0 * direct_of("all")
        );
        choice = json!({"grid": grid.name(), "m": m});
    } else {
        md += "\nChoice: none: no grid is as good as Direct on every category.\n";
    }
    md += &format!("Lead check: {short} beats known less than H ahead.\n");
    (
        (
            md,
            json!({"categories": table, "choice": choice, "shortLead": short}),
        ),
        short == 0.0,
    )
}

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(ok) => i32::from(!ok),
        Err(e) => {
            eprintln!("ps analysis-parity: {e}");
            2
        }
    }
}

fn run(args: &[String]) -> Result<bool, String> {
    let o = options(args)?;
    let index = index()?;
    let models =
        Tensors::load(&root().join("native/models/madmom.bin")).map_err(|e| e.to_string())?;
    let entries: Vec<(String, PathBuf, PathBuf)> = o
        .tracks
        .iter()
        .map(|id| {
            let t = index["tracks"]
                .as_array()
                .and_then(|ts| ts.iter().find(|t| t["id"].as_str() == Some(id)))
                .ok_or_else(|| format!("no track {id}"))?;
            let dir = root().join("public/reactive");
            Ok((
                id.clone(),
                dir.join(t["audio"].as_str().ok_or("no audio")?),
                dir.join(t["stream"].as_str().ok_or("no stream")?),
            ))
        })
        .collect::<Result<_, String>>()?;
    let rows: Mutex<Vec<Value>> = Mutex::new(Vec::new());
    let next = Mutex::new(0usize);
    let failures: Mutex<Vec<String>> = Mutex::new(Vec::new());
    std::thread::scope(|scope| {
        for _ in 0..o.jobs.max(1).min(entries.len().max(1)) {
            scope.spawn(|| {
                loop {
                    let k = {
                        let mut n = next.lock().unwrap();
                        let k = *n;
                        *n += 1;
                        k
                    };
                    // (After a failure, no new track starts; every failure is reported.)
                    if !failures.lock().unwrap().is_empty() {
                        break;
                    }
                    let Some((id, audio, stream)) = entries.get(k) else {
                        break;
                    };
                    let result = read_json(stream).and_then(|s| track(&models, id, audio, &s, o.live, &o.delayed));
                    match result {
                        Ok(row) => {
                            eprintln!(
                                "{id}: within 45 ms {:.3} (Python {:.3}; {:.3} at the live delay), real-time factor {:.3}",
                                f(&row["accuracy"]["rust"], "within45"),
                                f(&row["accuracy"]["python"], "within45"),
                                f(&row["accuracy"]["rustLiveDelay"], "within45"),
                                f(&row["realTimeFactor"], "whole")
                            );
                            rows.lock().unwrap().push(row);
                        }
                        Err(e) => {
                            failures.lock().unwrap().push(e);
                        }
                    }
                }
            });
        }
    });
    let failures = failures.into_inner().unwrap();
    if !failures.is_empty() {
        return Err(failures.join("; "));
    }
    let mut rows = rows.into_inner().unwrap();
    rows.sort_by(|a, b| a["track"].as_str().cmp(&b["track"].as_str()));
    // Aggregates weight each track by its accuracy's beat count.
    let weighted = |who: &str| {
        let (mut num, mut den) = (0.0, 0.0);
        for r in &rows {
            let a = &r["accuracy"][who];
            let n = a["beats"].as_f64().unwrap_or(0.0);
            num += n * f(a, "within45");
            den += n;
        }
        if den > 0.0 { num / den } else { 0.0 }
    };
    let failing: Vec<&str> = rows
        .iter()
        .filter(|r| {
            f(&r["accuracy"]["rust"], "within45") < f(&r["accuracy"]["python"], "within45") - MARGIN
        })
        .filter_map(|r| r["track"].as_str())
        .collect();
    let (rust, python) = (weighted("rust"), weighted("python"));
    let aggregate = json!({
        "tracks": rows.len(),
        "audioS": rows.iter().map(|r| f(r, "audioS")).sum::<f64>(),
        "within45Rust": rust,
        "within45Python": python,
        "within45RustLiveDelay": weighted("rustLiveDelay"),
        "tracksOutsideMargin": failing,
    });
    let mut ok = failing.is_empty() && rust >= python - AGGREGATE_MARGIN;
    let delayed_report = if o.delayed.is_empty() {
        None
    } else {
        let (report, lead_ok) = delayed_summary(&rows, &index, &o.delayed);
        ok &= lead_ok;
        Some(report)
    };
    let table = markdown(&rows, &aggregate);
    print!("{table}");
    println!(
        "{} tracks: within 45 ms {rust:.4} (Python {python:.4}; {:.4} at the live delay); outside the 1-point margin: {failing:?}",
        rows.len(),
        f(&aggregate, "within45RustLiveDelay")
    );
    if let Some(dir) = &o.out {
        std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
        let doc = json!({"aggregate": aggregate, "tracks": rows});
        std::fs::write(
            dir.join("parity.json"),
            serde_json::to_string_pretty(&doc).unwrap(),
        )
        .map_err(|e| e.to_string())?;
        std::fs::write(dir.join("parity.md"), &table).map_err(|e| e.to_string())?;
        if let Some((md, doc)) = &delayed_report {
            std::fs::write(dir.join("delayed.md"), md).map_err(|e| e.to_string())?;
            std::fs::write(
                dir.join("delayed.json"),
                serde_json::to_string_pretty(doc).unwrap(),
            )
            .map_err(|e| e.to_string())?;
        }
    }
    if let Some((md, _)) = &delayed_report {
        print!("{md}");
    }
    Ok(!o.check || ok)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accuracy_is_stream_pys() {
        // Reference beats every 0.5 s; committed beats on, 30 ms off, an off-beat, before 4 s.
        let r: Vec<f64> = (0..20).map(|k| f64::from(k) * 0.5).collect();
        let beats = [1.0, 4.5, 5.03, 5.75, 7.0, 7.01];
        let (n, w20, w45, off) = accuracy(&beats, &r, 4.0);
        assert_eq!(n, 5);
        assert!((w20 - 3.0 / 5.0).abs() < 1e-12);
        assert!((w45 - 4.0 / 5.0).abs() < 1e-12);
        assert!((off - 1.0 / 5.0).abs() < 1e-12);
        assert_eq!(accuracy(&beats, &r[..1], 0.0).0, 0);
    }

    #[test]
    fn matching_counts_neighbours_within_the_tolerance() {
        assert!((matched(&[1.0, 2.0, 3.0], &[1.005, 2.02, 2.995], 0.01) - 2.0 / 3.0).abs() < 1e-12);
        assert!((matched(&[], &[1.0], 0.01) - 1.0).abs() < 1e-12);
    }
}

#[cfg(test)]
mod python {
    use super::*;

    #[test]
    fn accuracy_equals_pythons_on_committed_streams() {
        // stream.py's accuracy(beats, reference, 4.0) on two committed streams, from Python.
        for (track, beats, w20, w45, off) in [
            ("orpheus-galop", 347, 0.2939, 0.5821, 0.0807),
            ("cc-kevin-macleod-go-cart", 400, 0.84, 0.89, 0.1),
        ] {
            let s =
                read_json(&root().join(format!("public/reactive/{track}/stream.json"))).unwrap();
            let b: Vec<f64> = list(&s, &["beats"]).iter().map(|b| f(b, "t")).collect();
            let r: Vec<f64> = list(&s, &["reference", "beats"])
                .iter()
                .filter_map(|b| b.as_f64())
                .collect();
            let (n, a20, a45, aoff) = accuracy(&b, &r, 4.0);
            assert_eq!(n, beats, "{track}");
            // (Python rounds its fractions to 4 places.)
            for (x, y) in [(a20, w20), (a45, w45), (aoff, off)] {
                assert!((x - y).abs() < 5e-5, "{track}: {x} against {y}");
            }
        }
    }
}
