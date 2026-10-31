//! A File source (docs/design/unity-game.md, "Sources"): an audio file, or an M3U playlist. Its
//! threads:
//!
//! - **decoder**: decodes each track (`decode`), a track or two ahead, and hands its mono
//!   samples (44.1 kHz) to the analyzer and its stereo frames (at the output rate) to the player.
//!   Each track starts where the analyzer's timeline puts it, rounded to an output frame, so the
//!   two never drift apart over a playlist.
//! - **player**: writes the stereo frames, after each track's gap of silence, into the delay
//!   line, keeping it filled to `M` past the render position. A frame's "capture time" is its song
//!   time, so the line plays song time `t` at render time `t`.
//! - **analyzer**: the session's [`Analysis`], fed in `ps entries`' blocks up to `M` past the
//!   render position: faster than real time at first, so the music starts at once with `M` of
//!   lookahead, and its entries are the dump's.

use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::Ordering;
use std::sync::mpsc::{Receiver, sync_channel};
use std::thread::JoinHandle;

use audio::delay::{DelayLine, Writer, delay_line};
use stream::playlist::Track;

use crate::decode;
use crate::session::{Analysis, NAP, Shared, guarded, music_frame, spawn};
use crate::{Error, PS_ERR_SOURCE};

/// How much music the line holds before it starts playing (s), unless the source is shorter.
pub const START_S: f64 = 0.25;
/// The analyzer's input blocks, as `ps entries` feeds it, so the entries match its dump.
pub(crate) const BLOCK: usize = 4096;
/// The frames the player writes at a time.
const CHUNK: usize = 1024;

/// A track for the player: its gap and stereo frames; for the analyzer: its gap and mono samples.
struct Part {
    gap: usize,
    samples: Vec<f32>,
}

/// A File source, checked.
pub(crate) struct File {
    path: PathBuf,
    tracks: Vec<Track>,
}

impl File {
    /// Checks that `path` and its tracks exist.
    pub(crate) fn open(path: PathBuf) -> Result<File, Error> {
        let tracks = decode::tracks(&path).map_err(|e| Error::new(PS_ERR_SOURCE, e))?;
        Ok(File { path, tracks })
    }

    /// The delay line, at the output rate: it keeps a second more than the player writes ahead of
    /// the render position.
    pub(crate) fn line(m: f64, output_rate: f64) -> (Writer, DelayLine) {
        delay_line(output_rate, 0.0, m + 1.0)
    }

    /// Starts the decoder, the player and the analyzer.
    pub(crate) fn start(
        self,
        shared: &Arc<Shared>,
        writer: Writer,
        mut analysis: Analysis,
    ) -> Result<Vec<JoinHandle<()>>, Error> {
        let (to_player, player_parts) = sync_channel(1);
        let (to_analyzer, analyzer_parts) = sync_channel(1);
        let mut threads = Vec::new();
        let s = Arc::clone(shared);
        let tracks = self.tracks;
        threads.push(spawn("ps-decoder", move || {
            guarded(&s.clone(), move || {
                let rate = s.output_rate;
                // (The analyzer's timeline, in its samples; the music's follows it.)
                let mut total = 0u64;
                for track in &tracks {
                    if s.stopped() {
                        return Ok(());
                    }
                    let decoded = decode::decode(&track.path, rate)?;
                    let gap = decode::gap_frames(track);
                    let start = total + gap as u64;
                    let end = start + decoded.mono.len() as u64;
                    let mut stereo = decoded.stereo;
                    let frames = music_frame(end, rate) - music_frame(start, rate);
                    stereo.resize(2 * frames as usize, 0.0);
                    let music_gap = music_frame(start, rate) - music_frame(total, rate);
                    total = end;
                    let sent = to_player
                        .send(Part {
                            gap: music_gap as usize,
                            samples: stereo,
                        })
                        .is_ok()
                        && to_analyzer
                            .send(Part {
                                gap,
                                samples: decoded.mono,
                            })
                            .is_ok();
                    if !sent {
                        return Ok(());
                    }
                }
                s.total.store(total, Ordering::Release);
                Ok(())
            });
        })?);
        let s = Arc::clone(shared);
        threads.push(spawn("ps-player", move || {
            guarded(&s.clone(), move || {
                play(&s, player_parts, writer);
                Ok(())
            });
        })?);
        let s = Arc::clone(shared);
        let source = self.path.display().to_string();
        threads.push(spawn("ps-analyzer", move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                analyse(&s, &mut analysis, analyzer_parts)
            }));
            analysis.close(result, &source, &[]);
        })?);
        Ok(threads)
    }
}

/// The player: each track's gap and stereo frames into the delay line (at the output rate), at
/// most `M` past the render position.
fn play(sh: &Shared, parts: Receiver<Part>, mut writer: Writer) {
    let rate = sh.output_rate;
    let mut frame = 0u64;
    let silence = [0.0f32; 2 * CHUNK];
    let mut write = |samples: &[f32]| {
        for chunk in samples.chunks(2 * CHUNK) {
            let frames = chunk.len() / 2;
            loop {
                if !sh.wait_for_render((frame + frames as u64) as f64 / rate) {
                    return false;
                }
                let written = writer.write(frame, frames, frame as f64 / rate, |a, b| {
                    a.copy_from_slice(&chunk[..a.len()]);
                    b.copy_from_slice(&chunk[a.len()..]);
                });
                if written {
                    break;
                }
                // (The line is full until the render thread pulls it.)
                std::thread::sleep(NAP);
            }
            frame += frames as u64;
            sh.written.store(frame, Ordering::Release);
        }
        true
    };
    for part in parts {
        let mut gap = part.gap;
        while gap > 0 {
            let n = gap.min(CHUNK);
            if !write(&silence[..2 * n]) {
                return;
            }
            gap -= n;
        }
        if !write(&part.samples) {
            return;
        }
    }
}

/// Analyses every track, then the stream's end; returns whether it got there (false when the
/// session stopped first or the source failed).
fn analyse(sh: &Shared, analysis: &mut Analysis, parts: Receiver<Part>) -> Result<bool, String> {
    for part in parts {
        let gap = std::iter::repeat_n(0.0, part.gap);
        if !analysis.push(gap.chain(part.samples.iter().copied()))? {
            return Ok(false);
        }
    }
    if sh.failed().is_some() || sh.stopped() {
        return Ok(false);
    }
    analysis.finish()
}
