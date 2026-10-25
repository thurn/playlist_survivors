//! The listening session's record (docs/design/native-app.md, "Session recording (D3)"): every
//! session, from listening's start to quit (or a new music source), saved to
//! `native/logs/sessions/<unix time>/` (`PS_SESSIONS` elsewhere), the last 20 and at most 5 GB
//! kept, oldest deleted first:
//!
//! - `audio.flac`: the analyzer's input (44.1 kHz mono, 16-bit; written as `audio.wav`, converted
//!   by `afconvert` when the session closes);
//! - `stream.jsonl`: every entry with its arrival (`eval::session::Arrival`);
//! - `keys.jsonl`: every key as the engine applied it, with its run;
//! - `meta.json`: the app's version, the output device, its latency, the calibration, and the runs:
//!   their start, end, why, and the hash of their settled records (what `ps replay` checks).

use std::fs::File;
use std::io::{BufWriter, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

use eval::session::Arrival;
use serde::{Deserialize, Serialize};
use sim::player::{KeyEvent, KeyEventKind};

use super::calibration::Calibration;
use super::live;

/// How many sessions are kept, and how many bytes they may take in all.
const KEEP: usize = 20;
const MAX_BYTES: u64 = 5 << 30;
/// The analyzer's sample rate.
const RATE: u32 = 44_100;

/// A run as the session records it.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Run {
    /// Its start and end (session song time), and why it ended.
    pub s0: f64,
    pub end: Option<f64>,
    pub why: Option<String>,
    /// The FNV-1a hash of its settled records (hex) and how many there were.
    pub settled_hash: Option<String>,
    pub settled_records: usize,
}

/// `meta.json`.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Meta {
    pub version: String,
    pub started_unix: u64,
    pub file: Option<String>,
    pub device: String,
    pub l_out_ms: f64,
    pub calibration: Calibration,
    pub runs: Vec<Run>,
}

/// `keys.jsonl`: a key as applied, in run time.
pub type Key = eval::session::AppliedKey;

/// A key the engine applied in run `run`.
pub fn key_of(run: usize, k: &KeyEvent) -> Key {
    Key {
        run,
        s: k.s,
        key: format!("{:?}", k.key),
        pressed: k.kind == KeyEventKind::Press,
    }
}

/// A session being recorded.
pub struct Session {
    dir: PathBuf,
    stream: BufWriter<File>,
    keys: BufWriter<File>,
    audio: BufWriter<File>,
    samples: u32,
    pub meta: Meta,
}

/// Where sessions are saved.
pub fn root() -> PathBuf {
    live::resource("sessions", "../../logs/sessions")
}

impl Session {
    /// Starts recording a session (none, logged, when its folder can't be made).
    pub fn start(meta: Meta) -> Option<Session> {
        let root = root();
        prune(&root);
        let dir = root.join(meta.started_unix.to_string());
        let open = |name: &str| File::create(dir.join(name)).map(BufWriter::new);
        let made = std::fs::create_dir_all(&dir).and_then(|()| {
            let mut audio = open("audio.wav")?;
            audio.write_all(&wav_header(0))?;
            Ok((open("stream.jsonl")?, open("keys.jsonl")?, audio))
        });
        match made {
            Ok((stream, keys, audio)) => {
                eprintln!("slice: recording the session to {}", dir.display());
                let s = Session {
                    dir,
                    stream,
                    keys,
                    audio,
                    samples: 0,
                    meta,
                };
                s.write_meta();
                Some(s)
            }
            Err(e) => {
                eprintln!("slice: not recording the session ({}): {e}", dir.display());
                None
            }
        }
    }

    pub fn arrival(&mut self, a: &Arrival) {
        if let Ok(line) = serde_json::to_string(a) {
            let _ = writeln!(self.stream, "{line}");
        }
    }

    pub fn key(&mut self, k: &Key) {
        if let Ok(line) = serde_json::to_string(k) {
            let _ = writeln!(self.keys, "{line}");
        }
    }

    /// The analyzer's input (samples at 44.1 kHz).
    pub fn audio(&mut self, samples: &[f32]) {
        for &x in samples {
            let v = (x.clamp(-1.0, 1.0) * 32767.0).round() as i16;
            let _ = self.audio.write_all(&v.to_le_bytes());
        }
        self.samples = self.samples.saturating_add(samples.len() as u32);
    }

    /// A run started at session time `s0`; returns its index.
    pub fn run_started(&mut self, s0: f64) -> usize {
        self.meta.runs.push(Run {
            s0,
            ..Run::default()
        });
        self.write_meta();
        self.meta.runs.len() - 1
    }

    /// The last run ended at session time `end`, for `why`, its settled records hashed.
    pub fn run_ended(&mut self, end: f64, why: &str, hash: u64, records: usize) {
        if let Some(r) = self.meta.runs.last_mut() {
            r.end = Some(end);
            r.why = Some(why.to_string());
            r.settled_hash = Some(format!("{hash:016x}"));
            r.settled_records = records;
        }
        let _ = self.stream.flush();
        let _ = self.keys.flush();
        self.write_meta();
    }

    fn write_meta(&self) {
        if let Ok(text) = serde_json::to_string_pretty(&self.meta) {
            let _ = std::fs::write(self.dir.join("meta.json"), text + "\n");
        }
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        let _ = self.stream.flush();
        let _ = self.keys.flush();
        self.write_meta();
        // The WAV's sizes, now known; then FLAC, in the background.
        let ok = self.audio.flush().is_ok()
            && self.audio.seek(SeekFrom::Start(0)).is_ok()
            && self.audio.write_all(&wav_header(self.samples)).is_ok()
            && self.audio.flush().is_ok();
        if ok {
            let wav = self.dir.join("audio.wav");
            let flac = self.dir.join("audio.flac");
            let _ = std::process::Command::new("/bin/sh")
                .arg("-c")
                .arg("/usr/bin/afconvert -f flac -d flac \"$0\" \"$1\" && rm \"$0\"")
                .arg(&wav)
                .arg(&flac)
                .spawn();
        }
    }
}

/// A 16-bit mono WAV header for `samples` samples at 44.1 kHz.
fn wav_header(samples: u32) -> Vec<u8> {
    let data = samples * 2;
    let mut h = Vec::with_capacity(44);
    h.extend_from_slice(b"RIFF");
    h.extend_from_slice(&(36 + data).to_le_bytes());
    h.extend_from_slice(b"WAVEfmt ");
    h.extend_from_slice(&16u32.to_le_bytes());
    h.extend_from_slice(&1u16.to_le_bytes());
    h.extend_from_slice(&1u16.to_le_bytes());
    h.extend_from_slice(&RATE.to_le_bytes());
    h.extend_from_slice(&(RATE * 2).to_le_bytes());
    h.extend_from_slice(&2u16.to_le_bytes());
    h.extend_from_slice(&16u16.to_le_bytes());
    h.extend_from_slice(b"data");
    h.extend_from_slice(&data.to_le_bytes());
    h
}

/// Deletes the oldest sessions beyond the last `KEEP - 1` (one is about to start) and beyond
/// `MAX_BYTES` in all.
fn prune(root: &Path) {
    let Ok(read) = std::fs::read_dir(root) else {
        return;
    };
    let mut dirs: Vec<(String, PathBuf, u64)> = read
        .filter_map(Result::ok)
        .filter(|e| e.path().is_dir())
        .map(|e| {
            let size = std::fs::read_dir(e.path()).map_or(0, |fs| {
                fs.filter_map(Result::ok)
                    .filter_map(|f| f.metadata().ok())
                    .map(|m| m.len())
                    .sum()
            });
            (e.file_name().to_string_lossy().into_owned(), e.path(), size)
        })
        .collect();
    // (Newest first: the folders are named by their start time.)
    dirs.sort_by(|a, b| b.0.cmp(&a.0));
    let mut total = 0;
    for (k, (_, path, size)) in dirs.iter().enumerate() {
        total += size;
        if k + 1 >= KEEP || total > MAX_BYTES {
            let _ = std::fs::remove_dir_all(path);
        }
    }
}

/// Seconds since 1970.
pub fn unix_now() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |d| d.as_secs())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_wav_header_says_its_sizes() {
        let h = wav_header(1000);
        assert_eq!(h.len(), 44);
        assert_eq!(&h[0..4], b"RIFF");
        assert_eq!(u32::from_le_bytes(h[40..44].try_into().unwrap()), 2000);
        assert_eq!(u32::from_le_bytes(h[24..28].try_into().unwrap()), 44_100);
    }

    #[test]
    fn pruning_keeps_the_newest() {
        let root = std::env::temp_dir().join(format!("ps-prune-{}", std::process::id()));
        for k in 0..25 {
            std::fs::create_dir_all(root.join(format!("{}", 1000 + k))).unwrap();
        }
        prune(&root);
        let mut left: Vec<String> = std::fs::read_dir(&root)
            .unwrap()
            .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
            .collect();
        left.sort();
        assert_eq!(left.len(), KEEP - 1);
        assert_eq!(left[0], "1006");
        let _ = std::fs::remove_dir_all(&root);
    }
}
