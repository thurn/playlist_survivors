//! The live analysis: a source's samples (a tap, or a file as the output plays it), resampled to
//! 44.1 kHz, through the analyzer and its hindsight worker on their own thread; committed beats
//! go to the game with the host time they are heard.

use std::collections::VecDeque;
use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc;
use std::time::Duration;

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::delayed::Listening;
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;
use audio::follow::Follower;

use crate::Meta;
use crate::clock::host_s;

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
    Follow { gain: f32 },
    /// The analyzer's input (44.1 kHz), for the session's record.
    Audio(Vec<f32>),
}

/// Where the analysed samples come from: mono samples at a rate, each buffer with its [`Meta`].
pub trait Source: Send {
    fn rate(&self) -> f64;
    /// Everything captured since the last call.
    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>);
}

/// A file the output plays, with each played buffer's timing.
pub struct FileSource {
    pub samples: Arc<[f32]>,
    pub metas: rtrb::Consumer<Meta>,
    pub rate: f64,
}

impl Source for FileSource {
    fn rate(&self) -> f64 {
        self.rate
    }

    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        let all = &self.samples;
        while let Ok(meta) = self.metas.pop() {
            let from = (meta.first as usize).min(all.len());
            let to = (meta.first as usize + meta.frames as usize).min(all.len());
            samples.extend_from_slice(&all[from..to]);
            samples.resize(samples.len() + meta.frames as usize - (to - from), 0.0);
            metas.push(meta);
        }
    }
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

/// Starts the analysis thread on `source`, with the analyzer's `models`, until `stop`. It sends
/// each step's messages to `tx`, then calls `wake`. `heard_after` (an f64's bits, so the game can
/// change it when the output device changes) is the time from a buffer's host time to when it is
/// heard (the tap's offset from the output, or the output latency for a played file).
pub fn spawn(
    mut source: Box<dyn Source>,
    models: &Path,
    listening: Listening,
    heard_after: Arc<AtomicU64>,
    tx: mpsc::Sender<Msg>,
    wake: impl Fn() + Send + 'static,
    stop: Arc<AtomicBool>,
) -> Result<std::thread::JoinHandle<()>, String> {
    let models = Tensors::load(models).map_err(|e| format!("models: {e}"))?;
    let params = Params {
        hindsight: HindsightParams::live(),
        listening,
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
                    let _ = tx.send(Msg::Follow { gain: g });
                }
                y.clear();
                rs.push(&raw, &mut y);
                out.clear();
                an.push(&y, &mut out);
                let _ = tx.send(Msg::Audio(y.clone()));
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
                wake();
            }
        })
        .map_err(|e| e.to_string())
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
}
