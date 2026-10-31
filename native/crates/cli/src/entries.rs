//! `ps entries`: a File run's stream as the Unity game's plugin sees it (docs/design/unity-game.md,
//! "Stream entries and what the sim may know"), for the sim's tests and sweeps without Unity.
//!
//! It decodes an audio file or an M3U playlist (with `#PS-GAP`) as File mode does, runs the
//! analyzer faster than real time in Delayed listening at `M = 3 s` with every group stamped
//! `M − A` before its analysis position, and writes every entry as JSON lines:
//!
//! ```text
//! {"format":"ps-entries","version":1,"duration":30.0,"m":3.0,"a":0.048,"stamps":"every-group"}
//! {"kind":2,"t":0.05,"known_at":-2.902,"v":[0.0,0.0,0.0,-1.0,0.0,0.0,0.0,0.0,0.0]}
//! ```
//!
//! The first line describes the run; each other line is one record of `analysis::flat` (the
//! plugin's `ps_entry`: its kind, `t`, stamp and the kind's `v` slots, the rest 0), in the
//! analyzer's emission order. Dumps are cached by every analyzer input, as `ps survive` caches
//! its streams, so a warm run decodes nothing; two runs write the same bytes.
use std::io::Write;
use std::path::PathBuf;

use analysis::analyzer::{Analyzer, Params};
use analysis::delayed::{DelayedParams, Listening, Stamps};
use analysis::flat::{self, Record};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use serde::Serialize;
use serde_json::json;
use stream::playlist::{Playlist, is_playlist};

use crate::survive_cache as cache;

const SR: f64 = 44100.0;

struct Options {
    input: PathBuf,
    out: Option<PathBuf>,
    cache: PathBuf,
    seconds: Option<f64>,
}

fn options(args: &[String]) -> Result<Options, String> {
    let mut input = None;
    let mut out = None;
    let mut seconds = None;
    let mut cache = std::env::var_os("PS_ENTRIES_CACHE").map_or_else(
        || {
            PathBuf::from(std::env::var_os("HOME").unwrap_or_default())
                .join("Library/Caches/playlist-survivors/entries")
        },
        PathBuf::from,
    );
    let mut it = args.iter();
    while let Some(arg) = it.next() {
        let mut value = || it.next().ok_or_else(|| format!("{arg} needs a value"));
        match arg.as_str() {
            "--out" => out = Some(value()?.into()),
            "--cache" => cache = value()?.into(),
            "--seconds" => {
                let s: f64 = value()?.parse().map_err(|_| "invalid seconds")?;
                if !(s.is_finite() && s > 0.0) {
                    return Err("--seconds must be positive".into());
                }
                seconds = Some(s);
            }
            a if a.starts_with("--") => return Err(format!("unknown option {a}")),
            a => {
                if input.replace(PathBuf::from(a)).is_some() {
                    return Err("give one audio file or playlist".into());
                }
            }
        }
    }
    Ok(Options {
        input: input.ok_or("give an audio file or playlist")?,
        out,
        cache,
        seconds,
    })
}

fn params() -> Params {
    Params {
        hindsight: HindsightParams::live(),
        listening: Listening::Delayed(DelayedParams {
            stamps: Stamps::EveryGroup,
            ..DelayedParams::chosen()
        }),
        ..Params::default()
    }
}

#[derive(Serialize)]
struct Line<'a> {
    kind: i32,
    t: f64,
    known_at: f64,
    v: &'a [f64],
}

/// Analyses `samples` and writes the dump to `out`; returns how many records it wrote.
fn write(models: &Tensors, samples: &[f32], out: &mut impl Write) -> Result<usize, String> {
    let p = params();
    let Listening::Delayed(d) = p.listening else {
        unreachable!("the dump listens Delayed")
    };
    let mut analyzer = Analyzer::with_hindsight_jobs(models, p, 4).map_err(|e| e.to_string())?;
    let mut entries = Vec::new();
    for block in samples.chunks(4096) {
        analyzer.push(block, &mut entries);
    }
    analyzer.finish(&mut entries);
    let mut records: Vec<Record> = Vec::new();
    for e in &entries {
        flat::push(e, &mut records);
    }
    let header = json!({
        "format": "ps-entries",
        "version": 1,
        "duration": samples.len() as f64 / SR,
        "m": d.m,
        "a": d.a,
        "stamps": "every-group",
    });
    let io = |e: std::io::Error| e.to_string();
    serde_json::to_writer(&mut *out, &header).map_err(|e| e.to_string())?;
    out.write_all(b"\n").map_err(io)?;
    for r in &records {
        let line = Line {
            kind: r.kind,
            t: r.t,
            known_at: r.known_at,
            v: &r.v[..flat::slots(r.kind)],
        };
        serde_json::to_writer(&mut *out, &line).map_err(|e| e.to_string())?;
        out.write_all(b"\n").map_err(io)?;
    }
    Ok(records.len())
}

/// The cache key: every analyzer input (sources, models, parameters), this dump's own sources,
/// the audio's bytes (a playlist's tracks, order and gaps), and the excerpt's length.
fn key(o: &Options) -> Result<String, String> {
    let version = cache::version()?;
    let audio = if is_playlist(&o.input) {
        cache::playlist_key(&Playlist::load(&o.input)?, &version)?
    } else {
        let bytes = std::fs::read(&o.input).map_err(|e| format!("{}: {e}", o.input.display()))?;
        cache::hash(&[&bytes])?
    };
    let seconds = o.seconds.map_or_else(String::new, |s| s.to_string());
    cache::hash(&[
        b"entries",
        version.as_bytes(),
        include_bytes!("entries.rs"),
        include_bytes!("../../analysis/src/flat.rs"),
        audio.as_bytes(),
        seconds.as_bytes(),
    ])
}

fn decode(o: &Options, key: &str) -> Result<Vec<f32>, String> {
    let mut samples = if is_playlist(&o.input) {
        Playlist::load(&o.input)?.decode(SR, |path| cache::decode(path, key))?
    } else {
        cache::decode(&o.input, key)?
    };
    if let Some(s) = o.seconds {
        samples.truncate((s * SR).round() as usize);
    }
    Ok(samples)
}

/// The cached dump of `o`'s input, written first if absent; returns its path and whether it was
/// cached.
fn cached(o: &Options) -> Result<(PathBuf, bool), String> {
    let key = key(o)?;
    let dest = o.cache.join(format!("{key}.jsonl"));
    if dest.exists() {
        return Ok((dest, true));
    }
    eprintln!("entries: analysing {}", o.input.display());
    let models = Tensors::load(&cache::root().join("native/models/madmom.bin"))
        .map_err(|e| e.to_string())?;
    let samples = decode(o, &key)?;
    std::fs::create_dir_all(&o.cache).map_err(|e| e.to_string())?;
    let temp = o.cache.join(format!(".{key}-{}.tmp", std::process::id()));
    let written = (|| {
        let file = std::fs::File::create(&temp).map_err(|e| e.to_string())?;
        let mut w = std::io::BufWriter::new(file);
        write(&models, &samples, &mut w)?;
        w.flush().map_err(|e| e.to_string())
    })();
    if let Err(e) = written {
        let _ = std::fs::remove_file(&temp);
        return Err(e);
    }
    std::fs::rename(&temp, &dest).map_err(|e| e.to_string())?;
    Ok((dest, false))
}

fn run(args: &[String]) -> Result<(), String> {
    let o = options(args)?;
    let (path, was_cached) = cached(&o)?;
    if let Some(out) = &o.out {
        std::fs::copy(&path, out).map_err(|e| format!("{}: {e}", out.display()))?;
    }
    println!(
        "{}",
        json!({
            "input": o.input.display().to_string(),
            "cache": path.display().to_string(),
            "cached": was_cached,
            "out": o.out.as_deref().map(|p| p.display().to_string()),
        })
    );
    Ok(())
}

pub fn main(args: &[String]) -> i32 {
    match run(args) {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("ps entries: {e}");
            2
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn models() -> Tensors {
        Tensors::load(&cache::root().join("native/models/madmom.bin")).unwrap()
    }

    fn excerpt() -> Vec<f32> {
        let pcm = Tensors::load(&cache::root().join("native/models/golden/house-128.bin"))
            .unwrap()
            .f32("pcm")
            .unwrap();
        pcm[..(6.0 * SR) as usize].to_vec()
    }

    #[test]
    fn a_dump_is_reproducible_and_one_record_per_line() {
        let (models, samples) = (models(), excerpt());
        let (mut first, mut second) = (Vec::new(), Vec::new());
        let records = write(&models, &samples, &mut first).unwrap();
        write(&models, &samples, &mut second).unwrap();
        assert!(first == second, "two dumps differ");
        let text = String::from_utf8(first).unwrap();
        let lines: Vec<serde_json::Value> = text
            .lines()
            .map(|l| serde_json::from_str(l).unwrap())
            .collect();
        assert_eq!(lines.len(), records + 1);
        assert_eq!(lines[0]["format"], "ps-entries");
        assert_eq!(lines[0]["duration"], 6.0);
        let tick = lines
            .iter()
            .find(|l| l["kind"] == flat::TICK)
            .expect("a tick");
        // A tick is known M − A before its time, and carries its kind's slots.
        let (t, k) = (
            tick["t"].as_f64().unwrap(),
            tick["known_at"].as_f64().unwrap(),
        );
        assert!((t - k - (3.0 - 0.048)).abs() < 1e-9, "{tick}");
        assert_eq!(tick["v"].as_array().unwrap().len(), flat::slots(flat::TICK));
    }

    #[test]
    fn options_need_one_input() {
        let parse =
            |args: &[&str]| options(&args.iter().map(|a| (*a).to_string()).collect::<Vec<_>>());
        assert!(parse(&[]).is_err());
        assert!(parse(&["a.ogg", "b.ogg"]).is_err());
        assert!(parse(&["a.ogg", "--seconds", "0"]).is_err());
        let o = parse(&["a.m3u", "--out", "x.jsonl", "--seconds", "20"]).unwrap();
        assert_eq!((o.input, o.seconds), (PathBuf::from("a.m3u"), Some(20.0)));
    }
}
