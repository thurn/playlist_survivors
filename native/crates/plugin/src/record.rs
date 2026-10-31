//! The session's record: what the analyzer heard and every entry it emitted, in the session
//! folder the client names (the client adds its keys and the rest in M3):
//!
//! - `audio.wav`: the analyzer's input, 44.1 kHz mono float, exactly as analysed;
//! - `entries.jsonl`: every record with its stamp, one per line in `ps entries`' line format
//!   (`analysis::flat::Line`), in the analyzer's order;
//! - `meta.json`, written when the analysis ends or the session closes: the source, `M`, `A`,
//!   the output rate, the duration analysed, the records, the hindsight stalls and whether the
//!   analysis reached the source's end.

use std::fs::File;
use std::io::{BufWriter, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

use analysis::flat::Record;
use serde_json::json;

use crate::decode::SR;

/// What `meta.json` describes beyond the records.
pub struct Meta<'a> {
    pub source: &'a Path,
    pub m: f64,
    pub a: f64,
    pub output_rate: f64,
    pub hindsight_stalls: usize,
    pub complete: bool,
}

pub struct Recorder {
    dir: PathBuf,
    audio: BufWriter<File>,
    samples: u64,
    entries: BufWriter<File>,
    records: u64,
}

fn io(path: &Path) -> impl Fn(std::io::Error) -> String + '_ {
    move |e| format!("{}: {e}", path.display())
}

/// A WAV header for `samples` mono float samples at 44.1 kHz.
fn wav_header(samples: u64) -> [u8; 44] {
    let data = (samples * 4).min(u64::from(u32::MAX - 36)) as u32;
    let mut h = [0u8; 44];
    h[..4].copy_from_slice(b"RIFF");
    h[4..8].copy_from_slice(&(36 + data).to_le_bytes());
    h[8..16].copy_from_slice(b"WAVEfmt ");
    h[16..20].copy_from_slice(&16u32.to_le_bytes());
    h[20..22].copy_from_slice(&3u16.to_le_bytes()); // IEEE float
    h[22..24].copy_from_slice(&1u16.to_le_bytes());
    h[24..28].copy_from_slice(&(SR as u32).to_le_bytes());
    h[28..32].copy_from_slice(&(SR as u32 * 4).to_le_bytes());
    h[32..34].copy_from_slice(&4u16.to_le_bytes());
    h[34..36].copy_from_slice(&32u16.to_le_bytes());
    h[36..40].copy_from_slice(b"data");
    h[40..44].copy_from_slice(&data.to_le_bytes());
    h
}

impl Recorder {
    /// Creates the session folder and its files.
    pub fn create(dir: &Path) -> Result<Recorder, String> {
        std::fs::create_dir_all(dir).map_err(io(dir))?;
        let audio_path = dir.join("audio.wav");
        let mut audio = BufWriter::new(File::create(&audio_path).map_err(io(&audio_path))?);
        audio.write_all(&wav_header(0)).map_err(io(&audio_path))?;
        let entries_path = dir.join("entries.jsonl");
        let entries = BufWriter::new(File::create(&entries_path).map_err(io(&entries_path))?);
        Ok(Recorder {
            dir: dir.to_path_buf(),
            audio,
            samples: 0,
            entries,
            records: 0,
        })
    }

    pub fn audio(&mut self, samples: &[f32]) -> Result<(), String> {
        for x in samples {
            self.audio
                .write_all(&x.to_le_bytes())
                .map_err(io(&self.dir))?;
        }
        self.samples += samples.len() as u64;
        Ok(())
    }

    pub fn entries(&mut self, records: &[Record]) -> Result<(), String> {
        for r in records {
            serde_json::to_writer(&mut self.entries, &r.line()).map_err(|e| e.to_string())?;
            self.entries.write_all(b"\n").map_err(io(&self.dir))?;
        }
        self.records += records.len() as u64;
        Ok(())
    }

    /// Completes the audio's header, flushes the entries and writes `meta.json`.
    pub fn finish(mut self, meta: &Meta) -> Result<(), String> {
        let audio_path = self.dir.join("audio.wav");
        let header = wav_header(self.samples);
        self.audio
            .seek(SeekFrom::Start(0))
            .and_then(|_| self.audio.write_all(&header))
            .and_then(|()| self.audio.flush())
            .map_err(io(&audio_path))?;
        self.entries.flush().map_err(io(&self.dir))?;
        let meta = json!({
            "format": "ps-plugin-session",
            "version": 1,
            "source": meta.source.display().to_string(),
            "m": meta.m,
            "a": meta.a,
            "stamps": "every-group",
            "outputRate": meta.output_rate,
            "duration": self.samples as f64 / SR,
            "records": self.records,
            "hindsightStalls": meta.hindsight_stalls,
            "complete": meta.complete,
        });
        let meta_path = self.dir.join("meta.json");
        std::fs::write(&meta_path, format!("{meta:#}\n")).map_err(io(&meta_path))
    }
}
