//! Audio files and playlists decoded to float samples by macOS's `afconvert`, for native's File
//! mode and the plugin's File source.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

use stream::playlist::{Playlist, is_playlist};

/// `path` as mono samples at `rate`; a playlist's tracks after their gaps (`stream::playlist`).
pub fn mono(path: &Path, rate: f64) -> Result<Vec<f32>, String> {
    if is_playlist(path) {
        return Playlist::load(path)?.decode(rate, |p| mono(p, rate));
    }
    convert(path, 1, rate)
}

/// `path` as float samples at `rate` with `channels` channels, interleaved.
pub fn convert(path: &Path, channels: u32, rate: f64) -> Result<Vec<f32>, String> {
    convert_in(path, channels, rate, Temp::new()?)
}

fn convert_in(path: &Path, channels: u32, rate: f64, temp: Temp) -> Result<Vec<f32>, String> {
    let wav = temp.0.join("decoded.wav");
    let out = std::process::Command::new("/usr/bin/afconvert")
        .args([
            "-f",
            "WAVE",
            "-d",
            &format!("LEF32@{}", rate.round() as u32),
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
    drop(temp);
    wav_f32(&bytes)
        .ok_or_else(|| format!("afconvert wrote an unexpected WAV for {}", path.display()))
}

/// A temporary folder of each conversion's own, removed with it (failures and malformed output
/// included).
struct Temp(PathBuf);

impl Temp {
    fn new() -> Result<Temp, String> {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        loop {
            let path = std::env::temp_dir().join(format!(
                "ps-decode-{}-{}",
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

/// The float32 samples of a WAV file's `data` chunk (to the file's end, if it is cut short), if
/// all are finite.
fn wav_f32(bytes: &[u8]) -> Option<Vec<f32>> {
    if bytes.get(..4)? != b"RIFF" || bytes.get(8..12)? != b"WAVE" {
        return None;
    }
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let id = &bytes[at..at + 4];
        let len = u32::from_le_bytes(bytes[at + 4..at + 8].try_into().ok()?) as usize;
        let body = bytes.get(at + 8..(at + 8 + len).min(bytes.len()))?;
        if id == b"data" {
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

#[cfg(test)]
mod tests {
    use super::*;

    fn constant_wav(value: f32) -> Vec<u8> {
        let n = 1600u32;
        let mut bytes = b"RIFF".to_vec();
        bytes.extend((36 + n * 4).to_le_bytes());
        bytes.extend(b"WAVEfmt ");
        bytes.extend(16u32.to_le_bytes());
        bytes.extend(3u16.to_le_bytes());
        bytes.extend(1u16.to_le_bytes());
        bytes.extend(8000u32.to_le_bytes());
        bytes.extend(32000u32.to_le_bytes());
        bytes.extend(4u16.to_le_bytes());
        bytes.extend(32u16.to_le_bytes());
        bytes.extend(b"data");
        bytes.extend((n * 4).to_le_bytes());
        for _ in 0..n {
            bytes.extend(value.to_le_bytes());
        }
        bytes
    }

    #[test]
    fn a_wav_data_chunk_is_read() {
        let mut b = b"RIFF\0\0\0\0WAVE".to_vec();
        b.extend(b"fmt \x04\0\0\0abcd");
        b.extend(b"data\x08\0\0\0");
        b.extend(0.25f32.to_le_bytes());
        b.extend((-1.0f32).to_le_bytes());
        assert_eq!(wav_f32(&b), Some(vec![0.25, -1.0]));
        b.extend(f32::NAN.to_le_bytes());
        b[28] = 12;
        assert_eq!(wav_f32(&b), None, "a non-finite sample");
    }

    #[test]
    fn concurrent_decodes_preserve_each_sources_real_pcm() {
        let inputs = Temp::new().unwrap();
        let a = inputs.0.join("a.wav");
        let b = inputs.0.join("b.wav");
        std::fs::write(&a, constant_wav(0.125)).unwrap();
        std::fs::write(&b, constant_wav(-0.25)).unwrap();
        let barrier = std::sync::Barrier::new(2);
        let (a, b) = std::thread::scope(|scope| {
            let x = scope.spawn(|| {
                barrier.wait();
                mono(&a, 8000.0).unwrap()
            });
            let y = scope.spawn(|| {
                barrier.wait();
                mono(&b, 8000.0).unwrap()
            });
            (x.join().unwrap(), y.join().unwrap())
        });
        assert_eq!(a.len(), 1600);
        assert_eq!(b.len(), 1600);
        assert!(a.iter().all(|x| (*x - 0.125).abs() < 1e-6));
        assert!(b.iter().all(|x| (*x + 0.25).abs() < 1e-6));
    }

    #[test]
    fn conversion_directories_are_unique_and_failures_clean_them() {
        let a = Temp::new().unwrap();
        let b = Temp::new().unwrap();
        assert_ne!(a.0, b.0);
        let path = a.0.clone();
        std::fs::write(path.join("partial.wav"), b"partial").unwrap();
        let missing = b.0.join("does-not-exist.wav");
        assert!(convert_in(&missing, 1, 8000.0, a).is_err());
        assert!(
            !path.exists(),
            "failed conversions must not retain partial output"
        );
        let path = b.0.clone();
        drop(b);
        assert!(!path.exists());
    }
}
