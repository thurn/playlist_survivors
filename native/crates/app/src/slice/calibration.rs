//! The player's input calibration (`live/input.ts`): taps land this much late on average, for the
//! direction keys, and for the stomp key (a long key may press later) as its difference from theirs,
//! so a change of headphones recalibrated with the arrows moves both. Until the stomp key is
//! calibrated itself, it plays with the directions' offset. Kept in a settings file per Mac
//! (`~/Library/Application Support/PlaylistSurvivors/settings.json`, or `PS_SETTINGS`); D2 adds the
//! calibration screen that writes it.

use std::path::PathBuf;

use serde::{Deserialize, Serialize};
use sim::movement::InputKey;

#[derive(Clone, Copy, Debug, Default, PartialEq, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Calibration {
    /// The direction keys' offset (ms), if calibrated.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub input_offset_ms: Option<f64>,
    /// The stomp key's offset less the directions' (ms), if calibrated.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stomp_extra_ms: Option<f64>,
}

impl Calibration {
    /// Where the settings live.
    pub fn path() -> Option<PathBuf> {
        if let Ok(p) = std::env::var("PS_SETTINGS") {
            return Some(PathBuf::from(p));
        }
        let home = std::env::var("HOME").ok()?;
        Some(
            PathBuf::from(home).join("Library/Application Support/PlaylistSurvivors/settings.json"),
        )
    }

    /// The saved calibration (none when there is no file or it can't be read).
    pub fn load() -> Calibration {
        Self::path()
            .and_then(|p| std::fs::read_to_string(p).ok())
            .and_then(|t| serde_json::from_str(&t).ok())
            .unwrap_or_default()
    }

    /// The stomp key's own offset (ms), if calibrated.
    fn stomp_ms(&self) -> Option<f64> {
        self.stomp_extra_ms
            .map(|extra| self.input_offset_ms.unwrap_or(0.0) + extra)
    }

    /// The offset `key` plays with (s): its group's own, else (for the stomp key) the directions',
    /// else none.
    pub fn offset_s(&self, key: InputKey) -> f64 {
        let ms = match key {
            InputKey::Space => self.stomp_ms().or(self.input_offset_ms),
            _ => self.input_offset_ms,
        };
        ms.unwrap_or(0.0) / 1000.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_stomp_key_follows_the_directions_until_calibrated_itself() {
        let none = Calibration::default();
        assert_eq!(none.offset_s(InputKey::Left), 0.0);
        assert_eq!(none.offset_s(InputKey::Space), 0.0);
        let arrows = Calibration {
            input_offset_ms: Some(30.0),
            stomp_extra_ms: None,
        };
        assert!((arrows.offset_s(InputKey::Up) - 0.03).abs() < 1e-12);
        assert!((arrows.offset_s(InputKey::Space) - 0.03).abs() < 1e-12);
        // The stomp's own, as a difference: recalibrating the arrows moves it too.
        let both = Calibration {
            input_offset_ms: Some(40.0),
            stomp_extra_ms: Some(15.0),
        };
        assert!((both.offset_s(InputKey::Space) - 0.055).abs() < 1e-12);
        assert!((both.offset_s(InputKey::Right) - 0.04).abs() < 1e-12);
        let parsed: Calibration =
            serde_json::from_str(r#"{"inputOffsetMs": 40, "stompExtraMs": 15}"#).unwrap();
        assert_eq!(parsed, both);
    }
}
