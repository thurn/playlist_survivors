//! `ps replay <session>`: re-runs a recorded listening session headlessly with its recorded
//! stream (docs/design/native-app.md, "Session recording (D3)"): each run's arrivals fed as they
//! arrived and its keys as the live engine applied them; its settled records must hash as the live
//! run's did. This is the test that the live runner's rollbacks settled correctly.

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

/// The engine as the app builds it.
pub fn engine() -> Result<Engine, String> {
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
        f64::INFINITY,
    )
    .map_err(|e| format!("level: {e:?}"))
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
        let mut e = engine()?;
        let got = replay_run(&mut e, &arrivals, &keys, run, RECENT_S);
        let same = got.hash == Some(run.hash);
        ok &= same;
        println!(
            "run {}: {:.1}-{:.1} s, {} records settled live, {} in the replay ({} rollbacks): {}",
            run.index,
            run.s0,
            run.end,
            run.records,
            got.settled,
            got.rollbacks,
            if same { "the same" } else { "DIFFERENT" }
        );
    }
    Ok(ok)
}

fn run(args: &[String]) -> Result<bool, String> {
    let dir = args.first().ok_or("usage: ps replay <session folder>")?;
    replay(Path::new(dir))
}
