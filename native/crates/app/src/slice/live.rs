//! The live analysis: captured samples (the system tap, or a `--file` as the output plays it),
//! resampled to 44.1 kHz, through the analyzer on its own thread; committed beats go to the main
//! thread with the host time they are heard.

use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc;
use std::time::Duration;

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use audio::follow::Follower;
use winit::event_loop::EventLoopProxy;

use super::coreaudio::host_s;
use super::tap::{Meta, Tap};

const SR: f64 = 44100.0;

/// What the analysis thread tells the game.
#[derive(Clone, Debug)]
pub enum Msg {
    /// An analyzer entry, as soon as it is final (times in song seconds: the analysed stream's).
    Entry(Entry),
    /// Song time `song` (the samples analysed so far) is heard at host time `heard` (s).
    Clock { song: f64, heard: f64 },
    /// The loudest sample of a captured buffer and the host time it is heard.
    Level { peak: f32, heard: f64 },
    /// The effects' gain, following the music's loudness (`audio::follow`).
    Follow(f32),
}

/// Where the analyzed samples come from.
pub enum Source {
    /// All system output but ours.
    Tap(Tap),
    /// The file the output plays, with each played buffer's timing.
    File {
        samples: Arc<[f32]>,
        metas: rtrb::Consumer<Meta>,
        rate: f64,
    },
}

impl Source {
    fn rate(&self) -> f64 {
        match self {
            Source::Tap(t) => t.rate,
            Source::File { rate, .. } => *rate,
        }
    }

    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        match self {
            Source::Tap(t) => t.drain(samples, metas),
            Source::File {
                samples: all,
                metas: m,
                ..
            } => {
                while let Ok(meta) = m.pop() {
                    let from = (meta.first as usize).min(all.len());
                    let to = (meta.first as usize + meta.frames as usize).min(all.len());
                    samples.extend_from_slice(&all[from..to]);
                    samples.resize(samples.len() + meta.frames as usize - (to - from), 0.0);
                    metas.push(meta);
                }
            }
        }
    }
}

/// A resource: `PS_<NAME>` (upper case), the bundle's `Resources/<name>`, or `fallback` relative
/// to this crate in the checkout.
pub fn resource(name: &str, fallback: &str) -> PathBuf {
    if let Ok(p) = std::env::var(format!("PS_{}", name.replace('.', "_").to_uppercase())) {
        return PathBuf::from(p);
    }
    if let Some(res) = std::env::current_exe()
        .ok()
        .and_then(|e| Some(e.parent()?.parent()?.join("Resources").join(name)))
        && res.exists()
    {
        return res;
    }
    Path::new(env!("CARGO_MANIFEST_DIR")).join(fallback)
}

/// The analyzer's models: `PS_MODELS`, the bundle's Resources, or the checkout's.
pub fn models_path() -> PathBuf {
    if let Ok(p) = std::env::var("PS_MODELS") {
        return PathBuf::from(p);
    }
    resource("madmom.bin", "../../models/madmom.bin")
}

/// Linear resampler from the source's rate to 44.1 kHz.
struct Resampler {
    ratio: f64,
    /// Input position (in input samples) of the next output sample.
    pos: f64,
    /// Input samples consumed before `buf[0]`.
    base: u64,
    buf: Vec<f32>,
}

impl Resampler {
    fn push(&mut self, input: &[f32], out: &mut Vec<f32>) {
        self.buf.extend_from_slice(input);
        loop {
            let i = (self.pos - self.base as f64).floor() as usize;
            if i + 1 >= self.buf.len() {
                break;
            }
            let frac = (self.pos - self.base as f64 - i as f64) as f32;
            out.push(self.buf[i] * (1.0 - frac) + self.buf[i + 1] * frac);
            self.pos += self.ratio;
        }
        let keep_from = ((self.pos - self.base as f64).floor() as usize).min(self.buf.len());
        self.buf.drain(..keep_from);
        self.base += keep_from as u64;
    }
}

/// Host time of source sample `i` from the recent buffer metas (extrapolating at the rate).
fn host_of(metas: &VecDeque<Meta>, rate: f64, i: f64) -> Option<f64> {
    let m = metas
        .iter()
        .rev()
        .find(|m| (m.first as f64) <= i)
        .or_else(|| metas.front())?;
    Some(host_s(m.input_host) + (i - m.first as f64) / rate)
}

/// Starts the analysis thread. `heard_after` (an f64's bits, so the app can change it when the
/// output device changes) is the time from a buffer's host time to when it
/// is heard (the tap's offset from the output, or the output latency for a played file).
pub fn spawn(
    mut source: Source,
    heard_after: Arc<AtomicU64>,
    tx: mpsc::Sender<Msg>,
    proxy: EventLoopProxy<()>,
    stop: Arc<AtomicBool>,
) -> Result<std::thread::JoinHandle<()>, String> {
    let models = Tensors::load(&models_path()).map_err(|e| format!("models: {e}"))?;
    let params = Params {
        hindsight: HindsightParams::live(),
        ..Params::default()
    };
    let mut an = Analyzer::with_hindsight_worker(&models, params).map_err(|e| e.to_string())?;
    std::thread::Builder::new()
        .name("analyzer".into())
        .spawn(move || {
            let rate = source.rate();
            let mut rs = Resampler {
                ratio: rate / SR,
                pos: 0.0,
                base: 0,
                buf: Vec::new(),
            };
            let mut metas: VecDeque<Meta> = VecDeque::new();
            let (mut raw, mut y, mut new_metas, mut out) =
                (Vec::new(), Vec::new(), Vec::new(), Vec::new());
            let mut analysed = 0usize;
            let mut follower = Follower::new(rate);
            while !stop.load(Ordering::Relaxed) {
                raw.clear();
                new_metas.clear();
                source.drain(&mut raw, &mut new_metas);
                for m in &new_metas {
                    let _ = tx.send(Msg::Level {
                        peak: m.peak,
                        heard: host_s(m.input_host)
                            + f64::from_bits(heard_after.load(Ordering::Relaxed)),
                    });
                }
                metas.extend(new_metas.drain(..));
                while metas.len() > 6000 {
                    metas.pop_front();
                }
                if raw.is_empty() {
                    std::thread::sleep(Duration::from_millis(1));
                    continue;
                }
                if let Some(g) = follower.push(&raw) {
                    let _ = tx.send(Msg::Follow(g));
                }
                y.clear();
                rs.push(&raw, &mut y);
                out.clear();
                an.push(&y, &mut out);
                analysed += y.len();
                let song = analysed as f64 / SR;
                if let Some(h) = host_of(&metas, rate, song * SR * rs.ratio) {
                    let _ = tx.send(Msg::Clock {
                        song,
                        heard: h + f64::from_bits(heard_after.load(Ordering::Relaxed)),
                    });
                }
                for e in out.drain(..) {
                    let _ = tx.send(Msg::Entry(e));
                }
                let _ = proxy.send_event(());
            }
        })
        .map_err(|e| e.to_string())
}

/// Decodes `path` to mono float samples at `rate` with macOS's `afconvert`.
pub fn decode(path: &Path, rate: f64) -> Result<Vec<f32>, String> {
    let tmp = std::env::temp_dir().join(format!("ps-slice-{}.wav", std::process::id()));
    let status = std::process::Command::new("/usr/bin/afconvert")
        .arg("-f")
        .arg("WAVE")
        .arg("-d")
        .arg(format!("LEF32@{}", rate.round() as u32))
        .arg("-c")
        .arg("1")
        .arg(path)
        .arg(&tmp)
        .status()
        .map_err(|e| format!("afconvert: {e}"))?;
    if !status.success() {
        return Err(format!("afconvert failed on {}", path.display()));
    }
    let bytes = std::fs::read(&tmp).map_err(|e| e.to_string())?;
    let _ = std::fs::remove_file(&tmp);
    wav_f32(&bytes).ok_or_else(|| "afconvert wrote an unexpected WAV".into())
}

/// The float32 samples of a WAV file's `data` chunk.
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
            return Some(
                body.as_chunks::<4>()
                    .0
                    .iter()
                    .map(|b| f32::from_le_bytes(*b))
                    .collect(),
            );
        }
        at += 8 + len + (len & 1);
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_resampler_keeps_a_constant_and_its_rate() {
        let mut rs = Resampler {
            ratio: 48000.0 / SR,
            pos: 0.0,
            base: 0,
            buf: Vec::new(),
        };
        let mut out = Vec::new();
        for _ in 0..100 {
            rs.push(&[0.5; 480], &mut out);
        }
        assert!(out.iter().all(|&x| (x - 0.5).abs() < 1e-6));
        let want = 48000.0 / (48000.0 / SR);
        assert!(
            (out.len() as f64 - want).abs() < 2.0,
            "{} samples",
            out.len()
        );
    }

    #[test]
    fn a_wav_data_chunk_is_read() {
        let mut b = b"RIFF\0\0\0\0WAVE".to_vec();
        b.extend(b"fmt \x04\0\0\0abcd");
        b.extend(b"data\x08\0\0\0");
        b.extend(0.25f32.to_le_bytes());
        b.extend((-1.0f32).to_le_bytes());
        assert_eq!(wav_f32(&b), Some(vec![0.25, -1.0]));
    }
}
