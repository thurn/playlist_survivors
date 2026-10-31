//! A File source's tracks: an audio file, or an M3U playlist whose `#PS-GAP` lines set the
//! silence before each track (`stream::playlist`), decoded by macOS's `afconvert`
//! (`capture::decode`). The analyzer hears the 44.1 kHz mono decode `ps entries` uses, so the
//! plugin's entries are its dump's; the music plays from a stereo decode at the output rate, so it
//! is resampled once, by macOS's resampler: the delay line's linear interpolation would dull the
//! treble and alias it (a 15 kHz tone at 44.1 → 48 kHz loses 3 dB and gains a spur 11 dB down at
//! 18.9 kHz).

use std::path::Path;

use capture::decode::convert;
use stream::playlist::{Playlist, Track, is_playlist};

/// The analyzer's sample rate.
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

/// A track decoded: the analyzer's mono samples at [`SR`], and the music's interleaved stereo
/// frames at the output rate.
pub struct Decoded {
    pub mono: Vec<f32>,
    pub stereo: Vec<f32>,
}

/// Decodes `path` for the analyzer, and for the music at `rate`.
pub fn decode(path: &Path, rate: f64) -> Result<Decoded, String> {
    let mono = convert(path, 1, SR)?;
    if mono.is_empty() {
        return Err(format!("empty audio: {}", path.display()));
    }
    let stereo = convert(path, 2, rate)?;
    Ok(Decoded { mono, stereo })
}
