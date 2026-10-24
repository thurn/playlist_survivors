//! The slice's sound effects: samples from `public/assets/sfx` (`sounds.json`), picked from the
//! TS game's crisp-steel palette (`content/sounds.yaml`), started at their onsets so they land at
//! once, and levelled by their loudness.

use std::path::Path;
use std::sync::Arc;

use serde::Deserialize;

use super::live::decode;
use super::output::Play;

/// What a sound marks.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Cue {
    /// An on-beat attack that hit nothing.
    Swing,
    Kill,
    KillMulti,
    /// An off-beat attack.
    Miss,
    Hurt,
    Death,
}

const CUES: [(Cue, &[&str]); 6] = [
    (
        Cue::Swing,
        &["rpgpack/swing", "rpgpack/swing3", "rpg80/blade_01"],
    ),
    (
        Cue::Kill,
        &[
            "kenney/impactSoft_medium_000",
            "kenney/impactSoft_medium_001",
            "kenney/impactSoft_medium_003",
        ],
    ),
    (
        Cue::KillMulti,
        &[
            "kenney/impactPunch_heavy_000",
            "kenney/impactPunch_heavy_001",
            "kenney/impactPunch_heavy_002",
        ],
    ),
    (Cue::Miss, &["rpgpack/swing2", "rpgpack/swing3"]),
    (
        Cue::Hurt,
        &[
            "kenney/impactPunch_medium_000",
            "kenney/impactPunch_medium_001",
        ],
    ),
    (
        Cue::Death,
        &["kenney/impactPlate_heavy_001", "fantasy/Trap_00"],
    ),
];
/// Effects are levelled to this loudness, then scaled by `GAIN`, under the music.
const TARGET_LUFS: f32 = -18.0;
const GAIN: f32 = 0.6;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Entry {
    file: String,
    lufs: f32,
    onset_s: f64,
}

#[derive(Deserialize)]
struct Catalogue {
    sounds: std::collections::HashMap<String, Entry>,
}

/// Decoded samples, for the output's bank.
pub type Bank = Vec<Arc<[f32]>>;

pub struct Sfx {
    /// Per cue: (bank index, onset sample, gain) of each variant.
    cues: Vec<(Cue, Vec<Play>)>,
    turn: usize,
}

impl Sfx {
    /// Decodes the cues' samples from `dir` at `rate`: the effects and the bank for the output.
    pub fn load(dir: &Path, rate: f64) -> Result<(Sfx, Bank), String> {
        let text = std::fs::read_to_string(dir.join("sounds.json"))
            .map_err(|e| format!("{}: {e}", dir.display()))?;
        let catalogue: Catalogue = serde_json::from_str(&text).map_err(|e| e.to_string())?;
        let mut bank: Bank = Vec::new();
        let mut names: Vec<&str> = Vec::new();
        let mut cues = Vec::new();
        for (cue, ids) in CUES {
            let mut plays = Vec::new();
            for &id in ids {
                let entry = catalogue
                    .sounds
                    .get(id)
                    .ok_or_else(|| format!("no sound {id}"))?;
                let sound = if let Some(k) = names.iter().position(|&n| n == id) {
                    k
                } else {
                    bank.push(decode(&dir.join(&entry.file), rate)?.into());
                    names.push(id);
                    bank.len() - 1
                };
                // (From just before the onset, so the sound lands at once.)
                let from = ((entry.onset_s - 0.005).max(0.0) * rate) as usize;
                let gain = GAIN * 10f32.powf((TARGET_LUFS - entry.lufs) / 20.0);
                plays.push(Play { sound, from, gain });
            }
            cues.push((cue, plays));
        }
        Ok((Sfx { cues, turn: 0 }, bank))
    }

    /// The next variant of `cue` to play.
    pub fn next(&mut self, cue: Cue) -> Option<Play> {
        let (_, plays) = self.cues.iter().find(|(c, _)| *c == cue)?;
        self.turn = self.turn.wrapping_add(1);
        plays.get(self.turn % plays.len().max(1)).copied()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_cue_names_a_catalogued_sound() {
        let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets/sfx");
        let text = std::fs::read_to_string(dir.join("sounds.json")).unwrap();
        let catalogue: Catalogue = serde_json::from_str(&text).unwrap();
        for (cue, ids) in CUES {
            for id in ids {
                let e = catalogue
                    .sounds
                    .get(*id)
                    .unwrap_or_else(|| panic!("{cue:?}: {id}"));
                assert!(dir.join(&e.file).exists(), "{}", e.file);
            }
        }
    }
}
