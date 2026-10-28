//! 48 kHz float stereo WAVs, with a sidecar CSV of each capture packet's first frame and QPC time.

use std::fs::File;
use std::io::{BufWriter, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

use crate::R;

pub const RATE: u32 = 48_000;

pub struct Writer {
    out: BufWriter<File>,
    side: BufWriter<File>,
    frames: u64,
}

fn header(frames: u64) -> Vec<u8> {
    let data = (frames * 8) as u32;
    let mut h = Vec::with_capacity(44);
    h.extend_from_slice(b"RIFF");
    h.extend_from_slice(&(36 + data).to_le_bytes());
    h.extend_from_slice(b"WAVEfmt ");
    h.extend_from_slice(&16u32.to_le_bytes());
    h.extend_from_slice(&3u16.to_le_bytes()); // IEEE float
    h.extend_from_slice(&2u16.to_le_bytes());
    h.extend_from_slice(&RATE.to_le_bytes());
    h.extend_from_slice(&(RATE * 8).to_le_bytes());
    h.extend_from_slice(&8u16.to_le_bytes());
    h.extend_from_slice(&32u16.to_le_bytes());
    h.extend_from_slice(b"data");
    h.extend_from_slice(&data.to_le_bytes());
    h
}

pub fn sidecar(path: &Path) -> PathBuf {
    path.with_extension("packets.csv")
}

impl Writer {
    pub fn create(path: &Path) -> R<Writer> {
        let mut out = BufWriter::new(File::create(path)?);
        out.write_all(&header(0))?;
        let mut side = BufWriter::new(File::create(sidecar(path))?);
        writeln!(side, "wav_frame,frames,devpos,qpc_100ns,flags")?;
        Ok(Writer { out, side, frames: 0 })
    }

    /// Appends a packet's interleaved stereo frames.
    pub fn packet(&mut self, data: &[f32], devpos: u64, qpc: u64, flags: u32) -> R<()> {
        let frames = data.len() as u64 / 2;
        writeln!(self.side, "{},{},{},{},{}", self.frames, frames, devpos, qpc, flags)?;
        for s in data {
            self.out.write_all(&s.to_le_bytes())?;
        }
        self.frames += frames;
        Ok(())
    }

    pub fn finish(mut self) -> R<()> {
        self.side.flush()?;
        self.out.seek(SeekFrom::Start(0))?;
        self.out.write_all(&header(self.frames))?;
        self.out.flush()?;
        Ok(())
    }
}

/// Reads one of our WAVs as mono (the mean of the channels).
pub fn read_mono(path: &Path) -> R<Vec<f32>> {
    let mut b = Vec::new();
    File::open(path)?.read_to_end(&mut b)?;
    // (Our own files: a 44-byte header, float stereo.)
    if b.len() < 44 || &b[0..4] != b"RIFF" || u16::from_le_bytes([b[20], b[21]]) != 3 {
        return Err(format!("{}: not a float WAV of ours", path.display()).into());
    }
    Ok(b[44..]
        .chunks_exact(8)
        .map(|c| {
            let l = f32::from_le_bytes([c[0], c[1], c[2], c[3]]);
            let r = f32::from_le_bytes([c[4], c[5], c[6], c[7]]);
            0.5 * (l + r)
        })
        .collect())
}

/// The sidecar's packets: (first frame in the WAV, QPC 100 ns).
pub fn read_packets(path: &Path) -> R<Vec<(u64, u64)>> {
    let s = std::fs::read_to_string(sidecar(path))?;
    Ok(s.lines()
        .skip(1)
        .filter_map(|l| {
            let f: Vec<&str> = l.split(',').collect();
            Some((f.first()?.parse().ok()?, f.get(3)?.parse().ok()?))
        })
        .collect())
}
