//! Rust/Delayed per-track cache; no Python stream is substituted on a cache miss.
use analysis::{
    analyzer::{Analyzer, Params},
    delayed::{DelayedParams, Listening},
    hindsight::HindsightParams,
    tensors::Tensors,
};
use eval::session::{Arrival, Entry, from_analyzer};
use serde::{Deserialize, Serialize};
use stream::playlist::Playlist;

static NEXT_TEMP: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct TrackStream {
    pub key: String,
    pub duration: f64,
    pub arrivals: Vec<Arrival>,
}
pub fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}
pub fn hash(parts: &[&[u8]]) -> Result<String, String> {
    let mut child = Command::new("shasum")
        .args(["-a", "256"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .spawn()
        .map_err(|e| format!("shasum: {e}"))?;
    let write = (|| {
        let mut input = child.stdin.take().ok_or("shasum input")?;
        for part in parts {
            input
                .write_all(&(part.len() as u64).to_le_bytes())
                .and_then(|()| input.write_all(part))
                .map_err(|e| e.to_string())?;
        }
        Ok::<_, String>(())
    })();
    let out = child.wait_with_output().map_err(|e| e.to_string())?;
    write?;
    if !out.status.success() {
        return Err(format!("shasum failed: {}", out.status));
    }
    let key = String::from_utf8_lossy(&out.stdout)
        .split_whitespace()
        .next()
        .unwrap_or("")
        .to_string();
    if key.len() != 64 {
        return Err("invalid shasum output".into());
    }
    Ok(key)
}
/// Capture the compiled analyzer/conversion sources, parameters, lock file and actual model
/// bytes. Worktree paths are absent, so a cache survives independent worktrees.
pub fn version() -> Result<String, String> {
    let models =
        std::fs::read(root().join("native/models/madmom.bin")).map_err(|e| e.to_string())?;
    hash(&[
        include_bytes!("survive_cache.rs"),
        include_bytes!("../../../Cargo.lock"),
        include_bytes!("../../eval/src/session.rs"),
        include_bytes!("../../stream/src/lib.rs"),
        include_bytes!("../../analysis/src/analyzer.rs"),
        include_bytes!("../../analysis/src/frontend.rs"),
        include_bytes!("../../analysis/src/rnn.rs"),
        include_bytes!("../../analysis/src/tensors.rs"),
        include_bytes!("../../analysis/src/dbn.rs"),
        include_bytes!("../../analysis/src/beats.rs"),
        include_bytes!("../../analysis/src/hindsight.rs"),
        include_bytes!("../../analysis/src/delayed.rs"),
        include_bytes!("../../analysis/src/ticks.rs"),
        include_bytes!("../../analysis/src/structure.rs"),
        include_bytes!("../../analysis/src/onsets.rs"),
        include_bytes!("../../analysis/src/bands.rs"),
        include_bytes!("../../analysis/src/accents.rs"),
        include_bytes!("../../analysis/src/numeric.rs"),
        &models,
    ])
}
/// Decode to the same 44.1 kHz mono source as File mode. Each thread gets its own temporary
/// path; failed conversions are checked, and all temporary files are removed.
pub fn decode(path: &Path, token: &str) -> Result<Vec<f32>, String> {
    let tmp = std::env::temp_dir().join(format!("ps-survive-{}-{token}.wav", std::process::id()));
    let result = (|| {
        let status = Command::new("/usr/bin/afconvert")
            .args(["-f", "WAVE", "-d", "LEF32@44100", "-c", "1"])
            .arg(path)
            .arg(&tmp)
            .status()
            .map_err(|e| format!("afconvert: {e}"))?;
        if !status.success() {
            return Err(format!("afconvert failed on {}: {status}", path.display()));
        }
        let bytes = std::fs::read(&tmp).map_err(|e| e.to_string())?;
        wav(&bytes).ok_or_else(|| format!("invalid decoded WAV: {}", path.display()))
    })();
    let _ = std::fs::remove_file(tmp);
    result
}
fn wav(bytes: &[u8]) -> Option<Vec<f32>> {
    if bytes.get(..4)? != b"RIFF" || bytes.get(8..12)? != b"WAVE" {
        return None;
    }
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let len = u32::from_le_bytes(bytes[at + 4..at + 8].try_into().ok()?) as usize;
        let body = bytes.get(at + 8..at + 8 + len)?;
        if &bytes[at..at + 4] == b"data" {
            if !body.len().is_multiple_of(4) {
                return None;
            }
            let out: Vec<f32> = body
                .as_chunks::<4>()
                .0
                .iter()
                .map(|b| f32::from_le_bytes(*b))
                .collect();
            return out.iter().all(|v| v.is_finite()).then_some(out);
        }
        at += 8 + len + (len & 1);
    }
    None
}
pub fn analyse(models: &Tensors, samples: &[f32], key: String) -> Result<TrackStream, String> {
    analyse_with_jobs(models, samples, key, 1)
}
fn analyse_with_jobs(
    models: &Tensors,
    samples: &[f32],
    key: String,
    model_jobs: usize,
) -> Result<TrackStream, String> {
    let duration = samples.len() as f64 / 44100.0;
    let params = Params {
        hindsight: HindsightParams::live(),
        listening: Listening::Delayed(DelayedParams::chosen()),
        ..Params::default()
    };
    // Track jobs keep one model job; only the single continuous preparation uses four.
    let mut analyzer =
        Analyzer::with_hindsight_jobs(models, params, model_jobs).map_err(|e| e.to_string())?;
    let mut out = Vec::new();
    for block in samples.chunks(4096) {
        analyzer.push(block, &mut out);
    }
    analyzer.finish(&mut out);
    let mut arrivals: Vec<Arrival> = out
        .iter()
        .map(from_analyzer)
        .filter(|e| {
            e.known_at() <= duration && content_time(e).is_none_or(|t| (0.0..duration).contains(&t))
        })
        .map(|e| {
            let arrived = e.known_at().max(0.0);
            Arrival {
                arrived,
                entry: e.stamped(arrived),
            }
        })
        .collect();
    arrivals.sort_by(|a, b| a.arrived.total_cmp(&b.arrived));
    Ok(TrackStream {
        key,
        duration,
        arrivals,
    })
}
fn content_time(e: &Entry) -> Option<f64> {
    match e {
        Entry::Beat(x) => Some(x.t),
        Entry::Tick(x) => Some(x.t),
        Entry::Onset(x) => Some(x.t),
        Entry::Change(x) => Some(x.t),
        Entry::Jump(x) => Some(x.t),
        Entry::End(x) => Some(x.t),
        Entry::Forecast(x) => Some(x.t),
        _ => None,
    }
}
pub fn cached(
    path: &Path,
    dir: &Path,
    version: &str,
    models: &Tensors,
) -> Result<TrackStream, String> {
    let bytes = std::fs::read(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let key = hash(&[version.as_bytes(), &bytes])?;
    stored(dir, &key, &path.display().to_string(), || {
        analyse(models, &decode(path, &key)?, key.clone())
    })
}

pub fn playlist_key(playlist: &Playlist, version: &str) -> Result<String, String> {
    let mut audio_keys = std::collections::BTreeMap::new();
    let mut order = Vec::new();
    for track in &playlist.tracks {
        if !audio_keys.contains_key(&track.path) {
            let bytes =
                std::fs::read(&track.path).map_err(|e| format!("{}: {e}", track.path.display()))?;
            audio_keys.insert(track.path.clone(), hash(&[&bytes])?);
        }
        order.push((audio_keys[&track.path].clone(), track.gap_before));
    }
    let order = serde_json::to_vec(&order).map_err(|e| e.to_string())?;
    hash(&[
        b"continuous-playlist",
        version.as_bytes(),
        include_bytes!("../../stream/src/playlist.rs"),
        &order,
    ])
}

/// The continuous-analysis release lane shares one source- and audio-keyed cache. A warm
/// read needs no PCM decoding; tracks, order, gaps and every analyzer input enter the key.
pub fn cached_concatenation(
    playlist: &Playlist,
    dir: &Path,
    version: &str,
    models: &Tensors,
) -> Result<TrackStream, String> {
    let key = playlist_key(playlist, version)?;
    stored(dir, &key, "continuous playlist", || {
        let audio = playlist.decode(44100.0, |path| decode(path, "concatenated"))?;
        analyse_with_jobs(models, &audio, key.clone(), 4)
    })
}

fn stored(
    dir: &Path,
    key: &str,
    label: &str,
    build: impl FnOnce() -> Result<TrackStream, String>,
) -> Result<TrackStream, String> {
    let dest = dir.join(format!("{key}.json"));
    if dest.exists() {
        let stream: TrackStream =
            serde_json::from_slice(&std::fs::read(&dest).map_err(|e| e.to_string())?)
                .map_err(|e| format!("{}: {e}", dest.display()))?;
        if stream.key != key
            || !stream.duration.is_finite()
            || stream.duration <= 0.0
            || stream
                .arrivals
                .windows(2)
                .any(|w| w[0].arrived > w[1].arrived)
        {
            return Err(format!("invalid stream cache {}", dest.display()));
        }
        eprintln!("survive: cached {label}");
        return Ok(stream);
    }
    eprintln!("survive: analysing {label}");
    let stream = build()?;
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let serial = NEXT_TEMP.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let temp = dir.join(format!(".{key}-{}-{serial}.tmp", std::process::id()));
    std::fs::write(
        &temp,
        serde_json::to_vec(&stream).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    std::fs::rename(&temp, dest).map_err(|e| e.to_string())?;
    Ok(stream)
}
/// Shift every song-time field, including both hindsight lists. No entries are invented
/// in a gap. The cached stream was clipped to its track before shifting.
pub fn offset(e: &Entry, by: f64) -> Entry {
    let mut e = e.clone();
    macro_rules! timed {
        ($x:expr) => {{
            $x.t += by;
            $x.known_at += by;
        }};
    }
    match &mut e {
        Entry::Beat(x) => timed!(x),
        Entry::Tick(x) => timed!(x),
        Entry::Onset(x) => timed!(x),
        Entry::Change(x) => timed!(x),
        Entry::Jump(x) => timed!(x),
        Entry::End(x) => timed!(x),
        Entry::Forecast(x) => timed!(x),
        Entry::Timbre(x) => x.known_at += by,
        Entry::Hindsight(x) => {
            x.known_at += by;
            for t in x.offline.iter_mut().chain(&mut x.online) {
                *t += by;
            }
        }
    }
    e
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn offset_preserves_lead_and_hindsight_coordinates() {
        let e = Entry::Beat(stream::Beat {
            t: 2.0,
            known_at: 1.0,
            period: 0.5,
            evidence: 0.9,
        });
        let Entry::Beat(b) = offset(&e, 64.5) else {
            panic!()
        };
        assert_eq!((b.t, b.known_at, b.period), (66.5, 65.5, 0.5));
        let e = Entry::Hindsight(stream::Hindsight {
            known_at: 3.0,
            offline: vec![1.0, 2.0],
            online: vec![1.1, 2.1],
        });
        let Entry::Hindsight(h) = offset(&e, 10.0) else {
            panic!()
        };
        assert_eq!(
            (h.known_at, h.offline, h.online),
            (13.0, vec![11.0, 12.0], vec![11.1, 12.1])
        );
    }
    #[test]
    fn continuous_cache_tracks_audio_order_gaps_and_version_without_path_identity() {
        use stream::playlist::Track;
        struct Folder(PathBuf);
        impl Drop for Folder {
            fn drop(&mut self) {
                let _ = std::fs::remove_dir_all(&self.0);
            }
        }
        let serial = NEXT_TEMP.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let folder = Folder(
            std::env::temp_dir().join(format!("ps-playlist-key-{}-{serial}", std::process::id())),
        );
        std::fs::create_dir(&folder.0).unwrap();
        let a = folder.0.join("a");
        let b = folder.0.join("b");
        let copy = folder.0.join("copy");
        std::fs::write(&a, b"audio-a").unwrap();
        std::fs::write(&b, b"audio-b").unwrap();
        std::fs::write(&copy, b"audio-a").unwrap();
        let p = Playlist {
            tracks: vec![
                Track {
                    path: a,
                    gap_before: 0.0,
                },
                Track {
                    path: b.clone(),
                    gap_before: 2.0,
                },
            ],
        };
        let key = playlist_key(&p, "version-1").unwrap();
        assert_ne!(key, playlist_key(&p, "version-2").unwrap());
        let mut changed = p.clone();
        changed.tracks.reverse();
        assert_ne!(key, playlist_key(&changed, "version-1").unwrap());
        changed = p.clone();
        changed.tracks[1].gap_before = 8.0;
        assert_ne!(key, playlist_key(&changed, "version-1").unwrap());
        changed = p.clone();
        changed.tracks[0].path = copy;
        assert_eq!(key, playlist_key(&changed, "version-1").unwrap());
        std::fs::write(b, b"changed-audio").unwrap();
        assert_ne!(key, playlist_key(&p, "version-1").unwrap());
    }

    #[test]
    fn cache_keys_include_audio_and_version() {
        let a = hash(&[b"v1", b"audio"]).unwrap();
        assert_ne!(a, hash(&[b"v2", b"audio"]).unwrap());
        assert_ne!(a, hash(&[b"v1", b"edited"]).unwrap());
        assert_eq!(a, hash(&[b"v1", b"audio"]).unwrap());
    }
}
