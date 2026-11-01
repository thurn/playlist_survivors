//! Local M3U playlists. Paths are relative to the list; #PS-GAP sets silence before
//! the following track (default 2 s, first track 0 s). No implicit trailing gap.
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, PartialEq)]
pub struct Track {
    pub path: PathBuf,
    pub gap_before: f64,
}
#[derive(Clone, Debug, PartialEq)]
pub struct Playlist {
    pub tracks: Vec<Track>,
}
impl Playlist {
    pub fn load(path: &Path) -> Result<Self, String> {
        let text = std::fs::read_to_string(path).map_err(|e| format!("{}: {e}", path.display()))?;
        Self::parse(&text, path.parent().unwrap_or(Path::new(".")))
    }
    pub fn parse(text: &str, base: &Path) -> Result<Self, String> {
        let mut tracks = Vec::new();
        let mut gap = None;
        for (line, raw) in text.trim_start_matches('\u{feff}').lines().enumerate() {
            let s = raw.trim();
            if let Some(n) = s.strip_prefix("#PS-GAP:") {
                let n: f64 = n
                    .parse()
                    .map_err(|_| format!("playlist line {}: invalid gap", line + 1))?;
                if !n.is_finite() || !(0.0..=60.0).contains(&n) {
                    return Err(format!(
                        "playlist line {}: gap must be 0..60 seconds",
                        line + 1
                    ));
                }
                if gap.replace(n).is_some() {
                    return Err("two gaps before one track".into());
                }
            } else if !s.is_empty() && !s.starts_with('#') {
                if s.contains("://") {
                    return Err("playlists support local audio paths only".into());
                }
                if is_playlist(Path::new(s)) {
                    return Err("nested playlists are unsupported".into());
                }
                tracks.push(Track {
                    path: base.join(s),
                    gap_before: gap
                        .take()
                        .unwrap_or(if tracks.is_empty() { 0.0 } else { 2.0 }),
                });
            }
        }
        if tracks.is_empty() {
            return Err("playlist has no tracks".into());
        }
        if gap.is_some() {
            return Err("playlist ends with a gap but no track".into());
        }
        Ok(Self { tracks })
    }
    /// The tracks in an order `seed` picks (a Fisher-Yates shuffle on SplitMix64), the same for
    /// the same seed. The gaps stay in their slots, so the first track still starts at once.
    /// Seed 0 keeps the list's order.
    pub fn shuffle(&mut self, seed: u64) {
        if seed == 0 {
            return;
        }
        let gaps: Vec<f64> = self.tracks.iter().map(|t| t.gap_before).collect();
        let mut state = seed;
        for i in (1..self.tracks.len()).rev() {
            state = state.wrapping_add(0x9e37_79b9_7f4a_7c15);
            let mut z = state;
            z = (z ^ (z >> 30)).wrapping_mul(0xbf58_476d_1ce4_e5b9);
            z = (z ^ (z >> 27)).wrapping_mul(0x94d0_49bb_1331_11eb);
            z ^= z >> 31;
            self.tracks.swap(i, (z % (i as u64 + 1)) as usize);
        }
        for (track, gap) in self.tracks.iter_mut().zip(gaps) {
            track.gap_before = gap;
        }
    }
    /// The supplied decoder is also the single-file decoder. Gap lengths use the same
    /// integer sample rounding as the stream concatenator.
    pub fn decode(
        &self,
        rate: f64,
        mut decode: impl FnMut(&Path) -> Result<Vec<f32>, String>,
    ) -> Result<Vec<f32>, String> {
        if !rate.is_finite() || rate <= 0.0 {
            return Err("invalid playlist sample rate".into());
        }
        let mut samples = Vec::new();
        for track in &self.tracks {
            let audio = decode(&track.path)?;
            if audio.is_empty() {
                return Err(format!("empty audio: {}", track.path.display()));
            }
            samples.resize(
                samples.len() + (track.gap_before * rate).round() as usize,
                0.0,
            );
            samples.extend(audio);
        }
        Ok(samples)
    }
}
pub fn is_playlist(path: &Path) -> bool {
    path.extension()
        .and_then(|s| s.to_str())
        .is_some_and(|s| s.eq_ignore_ascii_case("m3u") || s.eq_ignore_ascii_case("m3u8"))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn paths_gaps_and_audio_share_one_timeline() {
        let p = Playlist::parse(
            "\u{feff}#EXTM3U\n#EXTINF:1,ignored\na file.ogg\n#PS-GAP:8\nb.ogg\nc.ogg\n",
            Path::new("/music"),
        )
        .unwrap();
        assert_eq!(p.tracks[0].path, Path::new("/music/a file.ogg"));
        assert_eq!(
            p.tracks.iter().map(|t| t.gap_before).collect::<Vec<_>>(),
            [0.0, 8.0, 2.0]
        );
        let samples = p.decode(2.0, |_| Ok(vec![1.0, 1.0])).unwrap();
        assert_eq!(samples.len(), 26);
        assert_eq!(&samples[..2], &[1.0; 2]);
        assert!(samples[2..18].iter().all(|x| *x == 0.0));
        assert_eq!(&samples[18..20], &[1.0; 2]);
        assert!(samples[20..24].iter().all(|x| *x == 0.0));
        assert_eq!(&samples[24..], &[1.0; 2]);
    }
    #[test]
    fn a_shuffle_reorders_the_tracks_by_its_seed_and_keeps_the_gaps_in_place() {
        let text =
            "0.ogg\n1.ogg\n2.ogg\n#PS-GAP:8\n3.ogg\n4.ogg\n5.ogg\n6.ogg\n7.ogg\n8.ogg\n9.ogg\n";
        let list = Playlist::parse(text, Path::new("/m")).unwrap();
        let shuffled = |seed| {
            let mut p = list.clone();
            p.shuffle(seed);
            p
        };
        assert_eq!(shuffled(0), list);
        assert_eq!(shuffled(7), shuffled(7));
        assert_ne!(shuffled(7).tracks, shuffled(8).tracks);
        let p = shuffled(7);
        assert_ne!(p.tracks, list.tracks);
        let mut paths: Vec<_> = p.tracks.iter().map(|t| t.path.clone()).collect();
        paths.sort();
        assert_eq!(
            paths,
            list.tracks
                .iter()
                .map(|t| t.path.clone())
                .collect::<Vec<_>>()
        );
        let gaps = |p: &Playlist| p.tracks.iter().map(|t| t.gap_before).collect::<Vec<_>>();
        assert_eq!(gaps(&p), gaps(&list));
    }
    #[test]
    fn malformed_lists_fail_before_playing() {
        for s in [
            "",
            "#PS-GAP:NaN\na",
            "#PS-GAP:-1\na",
            "#PS-GAP:61\na",
            "a\n#PS-GAP:2",
            "https://example.com/a",
            "#PS-GAP:2\n#PS-GAP:3\na",
        ] {
            assert!(Playlist::parse(s, Path::new(".")).is_err(), "{s}");
        }
    }
}
