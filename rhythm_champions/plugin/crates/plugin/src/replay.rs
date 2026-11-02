//! A Replay source (docs/archive/design/unity-game.md, "Sources" and "Sessions and Replay"): a session
//! folder the plugin recorded (`record`), played again. Its music is the recorded `audio.wav`, the
//! analyzer's input (44.1 kHz mono, played on both channels), and its entries are
//! `entries.jsonl`'s, exactly as recorded, so a run replayed on them sees what the live one saw.
//! There is no analyzer. Its threads:
//!
//! - **player**: reads the audio a second at a time into the delay line (at 44.1 kHz, so the
//!   render thread interpolates it to the output rate, as a System source's), `M` past the render
//!   position. Song time is the recording's: sample `n` is song time `n / 44100`.
//! - **entries**: every recorded record into the ring, as fast as `ps_poll` makes room. Nothing
//!   is held back: the client applies each when the record of its run says it was taken.

use std::fs::File;
use std::io::{BufReader, Read};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::Ordering;
use std::thread::JoinHandle;

use analysis::flat::{Record, slots};
use audio::delay::{DelayLine, Writer, delay_line};
use serde::Deserialize;

use crate::decode::SR;
use crate::file::{Part, play};
use crate::session::{NAP, Shared, guarded, spawn};
use crate::{Error, PS_ERR_SOURCE};

/// The WAV header the record writes (`record::wav_header`).
const HEADER: u64 = 44;

/// A recorded session, checked.
pub(crate) struct Replay {
    audio: PathBuf,
    /// The recording's length, in samples.
    samples: u64,
    records: Vec<Record>,
}

/// An `entries.jsonl` line (`analysis::flat::Line`).
#[derive(Deserialize)]
struct Line {
    kind: i32,
    t: f64,
    known_at: f64,
    v: Vec<f64>,
}

fn io(path: &Path) -> impl Fn(std::io::Error) -> Error + '_ {
    move |e| Error::new(PS_ERR_SOURCE, format!("{}: {e}", path.display()))
}

/// The records of `entries.jsonl`, parsed back exactly (serde_json's `float_roundtrip`).
fn records(path: &Path) -> Result<Vec<Record>, Error> {
    let text = std::fs::read_to_string(path).map_err(io(path))?;
    text.lines()
        .enumerate()
        .filter(|(_, l)| !l.is_empty())
        .map(|(i, l)| {
            let bad = |why: String| {
                Error::new(
                    PS_ERR_SOURCE,
                    format!("{}:{}: {why}", path.display(), i + 1),
                )
            };
            let line: Line = serde_json::from_str(l).map_err(|e| bad(e.to_string()))?;
            if line.v.len() != slots(line.kind) || !(1..=10).contains(&line.kind) {
                return Err(bad(format!(
                    "kind {} with {} slots",
                    line.kind,
                    line.v.len()
                )));
            }
            let mut v = [0.0; 12];
            v[..line.v.len()].copy_from_slice(&line.v);
            Ok(Record {
                kind: line.kind,
                t: line.t,
                known_at: line.known_at,
                v,
            })
        })
        .collect()
}

impl Replay {
    /// Checks the session folder `dir` and reads its entries.
    pub(crate) fn open(dir: &Path) -> Result<Replay, Error> {
        let audio = dir.join("audio.wav");
        // (A record cut short keeps a header that says 0 samples: its file's length is the truth.)
        let len = std::fs::metadata(&audio).map_err(io(&audio))?.len();
        if len < HEADER {
            return Err(Error::new(
                PS_ERR_SOURCE,
                format!("{}: not a session's audio", audio.display()),
            ));
        }
        let records = records(&dir.join("entries.jsonl"))?;
        Ok(Replay {
            audio,
            samples: (len - HEADER) / 4,
            records,
        })
    }

    /// The delay line, at the recording's rate: it keeps a second more than the player writes
    /// ahead of the render position.
    pub(crate) fn line(m: f64) -> (Writer, DelayLine) {
        delay_line(SR, 0.0, m + 1.0)
    }

    /// Starts the player and the entries' thread.
    pub(crate) fn start(
        self,
        shared: &Arc<Shared>,
        writer: Writer,
        mut entries: rtrb::Producer<Record>,
    ) -> Result<Vec<JoinHandle<()>>, Error> {
        shared.total.store(self.samples, Ordering::Release);
        shared.analysed.store(self.samples, Ordering::Release);
        let mut threads = Vec::new();
        let s = Arc::clone(shared);
        let (audio, samples) = (self.audio, self.samples);
        threads.push(spawn("ps-player", move || {
            guarded(&s.clone(), move || {
                let mut file = BufReader::new(File::open(&audio).map_err(|e| e.to_string())?);
                let mut header = [0u8; HEADER as usize];
                file.read_exact(&mut header).map_err(|e| e.to_string())?;
                let mut failure = None;
                play(&s, Seconds::new(file, samples, &mut failure), writer);
                failure.map_or(Ok(()), |e| Err(format!("{}: {e}", audio.display())))
            });
        })?);
        let s = Arc::clone(shared);
        let records = self.records;
        threads.push(spawn("ps-entries", move || {
            for &record in &records {
                while entries.push(record).is_err() {
                    if s.stopped() {
                        return;
                    }
                    std::thread::sleep(NAP);
                }
            }
            s.analysis_done.store(true, Ordering::Release);
        })?);
        Ok(threads)
    }
}

/// The recording's mono samples as the player's stereo parts, a second at a time; a read that
/// fails ends them and leaves its error in `failure`.
struct Seconds<'a> {
    file: BufReader<File>,
    left: u64,
    bytes: Vec<u8>,
    failure: &'a mut Option<std::io::Error>,
}

impl<'a> Seconds<'a> {
    fn new(file: BufReader<File>, samples: u64, failure: &'a mut Option<std::io::Error>) -> Self {
        Seconds {
            file,
            left: samples,
            bytes: Vec::new(),
            failure,
        }
    }
}

impl Iterator for Seconds<'_> {
    type Item = Part;

    fn next(&mut self) -> Option<Part> {
        let n = self.left.min(SR as u64) as usize;
        if n == 0 {
            return None;
        }
        self.bytes.resize(4 * n, 0);
        if let Err(e) = self.file.read_exact(&mut self.bytes) {
            *self.failure = Some(e);
            return None;
        }
        self.left -= n as u64;
        let samples = self
            .bytes
            .as_chunks::<4>()
            .0
            .iter()
            .flat_map(|b| {
                let x = f32::from_le_bytes(*b);
                [x, x]
            })
            .collect();
        Some(Part { gap: 0, samples })
    }
}
