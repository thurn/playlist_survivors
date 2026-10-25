//! `ps replay <session>`: re-runs a recorded listening session headlessly with its recorded
//! stream (docs/design/native-app.md, "Session recording (D3)"): each run's arrivals fed as they
//! arrived and its keys as the live engine applied them; its settled records must hash as the live
//! run's did. This is the test that the live runner's rollbacks settled correctly. With
//! `--reanalyse`, the session's audio goes through the current analyzer instead (live hindsight
//! timing, each entry arriving as known) and each run is replayed on that stream with the recorded
//! keys: a regression test for analyzer changes on real sessions, reported, not judged (the audio
//! was stored at 16 bits, so this is close to, not the same as, what the analyzer heard live).

use std::path::{Path, PathBuf};

use eval::live::Engine;
use eval::session::{AppliedKey, Arrival, RecordedRun, replay_run};
use indexmap::IndexMap;
use serde_json::Value;
use sim::content::{SourceFile, load_content};

/// The app's evidence gate and how far back its run start feeds entries (`slice/mod.rs`).
const GATE: f64 = 0.2;
const RECENT_S: f64 = 20.0;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(ok) => i32::from(!ok),
        Err(e) => {
            eprintln!("ps replay: {e}");
            2
        }
    }
}

fn lines<T: serde::de::DeserializeOwned>(path: &Path) -> Result<Vec<T>, String> {
    let text = std::fs::read_to_string(path).map_err(|e| format!("{}: {e}", path.display()))?;
    text.lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| serde_json::from_str(l).map_err(|e| format!("{}: {e}", path.display())))
        .collect()
}

/// The runs a session's `meta.json` records with their settled hash.
pub fn runs(meta: &Value) -> Vec<RecordedRun> {
    meta["runs"]
        .as_array()
        .map(|rs| {
            rs.iter()
                .enumerate()
                .filter_map(|(index, r)| {
                    Some(RecordedRun {
                        index,
                        s0: r["s0"].as_f64()?,
                        end: r["end"].as_f64()?,
                        hash: u64::from_str_radix(r["settledHash"].as_str()?, 16).ok()?,
                        records: r["settledRecords"].as_u64()? as usize,
                    })
                })
                .collect()
        })
        .unwrap_or_default()
}

/// The engine as the app builds it, in zen mode or not.
pub fn engine(zen: bool) -> Result<Engine, String> {
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .map_err(|e| e.to_string())?
        .filter_map(Result::ok)
        .map(|e| e.file_name().to_string_lossy().into_owned())
        .filter(|n| Path::new(n).extension().is_some_and(|x| x == "yaml"))
        .collect();
    names.sort();
    let read = |rel: &str| -> Result<SourceFile, String> {
        Ok(SourceFile {
            name: rel.to_string(),
            text: std::fs::read_to_string(root().join(rel)).map_err(|e| format!("{rel}: {e}"))?,
        })
    };
    let files = names
        .iter()
        .map(|n| read(&format!("content/{n}")))
        .collect::<Result<Vec<_>, _>>()?;
    let lib = load_content(&files).map_err(|e| format!("content: {e:?}"))?;
    Engine::new(
        &read("levels/reactive.yaml")?,
        &lib,
        &IndexMap::new(),
        GATE,
        true,
        zen,
        f64::INFINITY,
    )
    .map_err(|e| format!("level: {e:?}"))
}

/// The analyzer's input, decoded from the session's audio (FLAC, or the WAV before conversion).
fn audio(dir: &Path) -> Result<Vec<f32>, String> {
    let src = ["audio.flac", "audio.wav"]
        .iter()
        .map(|n| dir.join(n))
        .find(|p| p.exists())
        .ok_or("the session has no audio")?;
    let tmp = std::env::temp_dir().join(format!("ps-replay-{}.wav", std::process::id()));
    let status = std::process::Command::new("/usr/bin/afconvert")
        .args(["-f", "WAVE", "-d", "LEF32@44100", "-c", "1"])
        .arg(&src)
        .arg(&tmp)
        .status()
        .map_err(|e| format!("afconvert: {e}"))?;
    if !status.success() {
        return Err(format!("afconvert failed on {}", src.display()));
    }
    let bytes = std::fs::read(&tmp).map_err(|e| e.to_string())?;
    let _ = std::fs::remove_file(&tmp);
    // (The data chunk's float32 samples.)
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let len = u32::from_le_bytes(bytes[at + 4..at + 8].try_into().unwrap_or_default()) as usize;
        if &bytes[at..at + 4] == b"data" {
            let end = (at + 8 + len).min(bytes.len());
            return Ok(bytes[at + 8..end]
                .as_chunks::<4>()
                .0
                .iter()
                .map(|b| f32::from_le_bytes(*b))
                .collect());
        }
        at += 8 + len + (len & 1);
    }
    Err("afconvert wrote an unexpected WAV".into())
}

/// Whether run `index` of a session ran in zen mode (`meta.json`'s run `zen`; not when absent).
pub fn zen_of(meta: &Value, index: usize) -> bool {
    meta["runs"][index]["zen"].as_bool().unwrap_or(false)
}

/// The listening mode a session ran in (`meta.json`'s `listening` and `m`; Direct when absent).
pub fn listening_of(meta: &Value) -> analysis::delayed::Listening {
    use analysis::delayed::{DelayedParams, Listening};
    match meta["listening"].as_str() {
        Some("delayed") => Listening::Delayed(DelayedParams {
            m: meta["m"].as_f64().unwrap_or(DelayedParams::chosen().m),
            ..DelayedParams::chosen()
        }),
        _ => Listening::Direct,
    }
}

/// The session's audio through the current analyzer, in the session's listening mode, each
/// entry arriving as it is known.
fn reanalyse(dir: &Path, meta: &Value) -> Result<Vec<Arrival>, String> {
    use analysis::analyzer::{Analyzer, Params};
    use analysis::hindsight::HindsightParams;
    let y = audio(dir)?;
    let models = analysis::tensors::Tensors::load(&root().join("native/models/madmom.bin"))
        .map_err(|e| e.to_string())?;
    let params = Params {
        hindsight: HindsightParams::live(),
        listening: listening_of(meta),
        ..Params::default()
    };
    let mut an = Analyzer::with_hindsight_worker(&models, params).map_err(|e| e.to_string())?;
    let mut out = Vec::new();
    for block in y.chunks(4096) {
        an.push(block, &mut out);
    }
    an.finish(&mut out);
    let mut arrivals: Vec<Arrival> = out
        .iter()
        .map(|e| {
            let entry = eval::session::from_analyzer(e);
            Arrival {
                arrived: entry.known_at(),
                entry,
            }
        })
        .collect();
    // (In the order they become known, as a live session records them.)
    arrivals.sort_by(|a, b| a.arrived.total_cmp(&b.arrived));
    Ok(arrivals)
}

/// How a replayed run went.
fn outcome(e: &Engine) -> String {
    let v = e.view.borrow();
    let [p, g, ok] = v.ratings;
    format!(
        "{} kills, {p} perfect, {g} great, {ok} good, health {:.2}",
        v.kills, v.health
    )
}

/// Replays every recorded run of the session in `dir`; whether each settled as it did live.
pub fn replay(dir: &Path) -> Result<bool, String> {
    let meta: Value = serde_json::from_str(
        &std::fs::read_to_string(dir.join("meta.json")).map_err(|e| format!("meta.json: {e}"))?,
    )
    .map_err(|e| e.to_string())?;
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"))?;
    let keys: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"))?;
    let runs = runs(&meta);
    if runs.is_empty() {
        return Err("no finished run to replay".into());
    }
    let mut ok = true;
    for run in &runs {
        let mut e = engine(zen_of(&meta, run.index))?;
        let got = replay_run(&mut e, &arrivals, &keys, run, RECENT_S);
        let same = got.hash == Some(run.hash);
        ok &= same;
        println!(
            "run {}: {:.1}-{:.1} s, {} records settled live, {} in the replay ({} rollbacks): {}; {}",
            run.index,
            run.s0,
            run.end,
            run.records,
            got.settled,
            got.rollbacks,
            if same { "the same" } else { "DIFFERENT" },
            outcome(&e)
        );
    }
    Ok(ok)
}

/// Replays every recorded run on the re-analysed stream (a report).
pub fn replay_reanalysed(dir: &Path) -> Result<bool, String> {
    let meta: Value = serde_json::from_str(
        &std::fs::read_to_string(dir.join("meta.json")).map_err(|e| format!("meta.json: {e}"))?,
    )
    .map_err(|e| e.to_string())?;
    let recorded: Vec<Arrival> = lines(&dir.join("stream.jsonl"))?;
    let arrivals = reanalyse(dir, &meta)?;
    let keys: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"))?;
    let count = |a: &[Arrival], group: &str| {
        a.iter()
            .filter(|x| {
                serde_json::to_value(&x.entry)
                    .ok()
                    .and_then(|v| v["group"].as_str().map(str::to_string))
                    == Some(group.to_string())
            })
            .count()
    };
    for g in ["beat", "tick", "onset", "change", "end"] {
        println!(
            "{g}: {} recorded live, {} re-analysed",
            count(&recorded, g),
            count(&arrivals, g)
        );
    }
    for run in &runs(&meta) {
        let mut e = engine(zen_of(&meta, run.index))?;
        // (Its hash can't be the live run's: compare outcomes.)
        let got = replay_run(&mut e, &arrivals, &keys, run, RECENT_S);
        println!(
            "run {} re-analysed: {} records settled; {}",
            run.index,
            got.settled,
            outcome(&e)
        );
    }
    Ok(true)
}

fn run(args: &[String]) -> Result<bool, String> {
    let dir = args
        .iter()
        .find(|a| !a.starts_with("--"))
        .ok_or("usage: ps replay <session folder> [--reanalyse]")?;
    if args.iter().any(|a| a == "--reanalyse") {
        replay_reanalysed(Path::new(dir))
    } else {
        replay(Path::new(dir))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use analysis::delayed::Listening;

    #[test]
    fn a_session_replays_in_the_mode_it_ran_in() {
        let delayed = listening_of(&serde_json::json!({"listening": "delayed", "m": 2.5}));
        assert!(matches!(delayed, Listening::Delayed(d) if (d.m - 2.5).abs() < 1e-12));
        // (Sessions from before Delayed listening ran Direct.)
        assert_eq!(listening_of(&serde_json::json!({})), Listening::Direct);
    }

    #[test]
    fn a_zen_run_replays_in_zen_mode() {
        let meta = serde_json::json!({"runs": [{"s0": 0.0}, {"s0": 9.0, "zen": true}]});
        assert!(!zen_of(&meta, 0));
        assert!(zen_of(&meta, 1));
    }
}
