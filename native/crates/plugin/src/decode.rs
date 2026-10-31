//! A File source's tracks: an audio file, or an M3U playlist whose `#PS-GAP` lines set the
//! silence before each track (`stream::playlist`), decoded by macOS's `afconvert` at the
//! analyzer's 44.1 kHz. The analyzer hears the mono decode `ps entries` uses, so the plugin's
//! entries are its dump's; the music plays from a stereo decode of the same file.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

use stream::playlist::{Playlist, Track, is_playlist};

/// The analyzer's sample rate, and the decoded music's.
pub const SR: f64 = 44_100.0;

/// The tracks of `source`: a playlist's, or the file alone. Each must exist.
pub fn tracks(source: &Path) -> Result<Vec<Track>, String> {
    let tracks = if is_playlist(source) {
        Playlist::load(source)?.tracks
    } else {
        vec![Track {
            path: source.to_path_buf(),
            gap_before: 0.0,
        }]
    };
    if let Some(t) = tracks.iter().find(|t| !t.path.is_file()) {
        return Err(format!("{}: no such file", t.path.display()));
    }
    Ok(tracks)
}

/// The silence before a track, in frames, rounded as `Playlist::decode` rounds it.
pub fn gap_frames(track: &Track) -> usize {
    (track.gap_before * SR).round() as usize
}

/// A track decoded: the analyzer's mono samples and the music's interleaved stereo frames, as
/// many of each.
pub struct Decoded {
    pub mono: Vec<f32>,
    pub stereo: Vec<f32>,
}

pub fn decode(path: &Path) -> Result<Decoded, String> {
    let mono = convert(path, 1)?;
    let mut stereo = convert(path, 2)?;
    if mono.is_empty() {
        return Err(format!("empty audio: {}", path.display()));
    }
    // (One timeline for both: the stereo decode is cut or padded to the mono's length.)
    stereo.resize(2 * mono.len(), 0.0);
    Ok(Decoded { mono, stereo })
}

/// A temporary folder of the conversion's own, removed with it.
struct Temp(PathBuf);

impl Temp {
    fn new() -> Result<Temp, String> {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        loop {
            let path = std::env::temp_dir().join(format!(
                "ps-plugin-decode-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            match std::fs::create_dir(&path) {
                Ok(()) => return Ok(Temp(path)),
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(e) => return Err(format!("{}: {e}", path.display())),
            }
        }
    }
}

impl Drop for Temp {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

/// `path` as float samples at 44.1 kHz with `channels` channels, interleaved.
fn convert(path: &Path, channels: u32) -> Result<Vec<f32>, String> {
    let temp = Temp::new()?;
    let wav = temp.0.join("decoded.wav");
    let out = std::process::Command::new("/usr/bin/afconvert")
        .args([
            "-f",
            "WAVE",
            "-d",
            "LEF32@44100",
            "-c",
            &channels.to_string(),
        ])
        .arg(path)
        .arg(&wav)
        .output()
        .map_err(|e| format!("afconvert: {e}"))?;
    if !out.status.success() {
        return Err(format!(
            "afconvert failed on {}: {}",
            path.display(),
            String::from_utf8_lossy(&out.stderr).trim()
        ));
    }
    let bytes = std::fs::read(&wav).map_err(|e| e.to_string())?;
    wav_f32(&bytes)
        .ok_or_else(|| format!("afconvert wrote an unexpected WAV for {}", path.display()))
}

/// The float32 samples of a WAV file's `data` chunk.
fn wav_f32(bytes: &[u8]) -> Option<Vec<f32>> {
    if bytes.get(..4)? != b"RIFF" || bytes.get(8..12)? != b"WAVE" {
        return None;
    }
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let len = u32::from_le_bytes(bytes[at + 4..at + 8].try_into().ok()?) as usize;
        let body = bytes.get(at + 8..at + 8 + len)?;
        if &bytes[at..at + 4] == b"data" {
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
