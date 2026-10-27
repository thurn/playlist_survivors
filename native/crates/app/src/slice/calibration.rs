//! The player's calibration, per output device (docs/design/native-app.md, "Calibration (D2)";
//! `live/input.ts`): taps land this much late on average, for the direction keys, and for the stomp
//! key (a long key may press later) as its difference from theirs, so a change of headphones
//! recalibrated with the arrows moves both; until the stomp key is calibrated itself, it plays with
//! the directions' offset. Also the display's offset (the visual test) and an optional correction
//! to the device's reported latency. Kept in `~/Library/Application Support/PlaylistSurvivors/
//! settings.json` (or `PS_SETTINGS`), keyed by the output device's UID; values at the top level
//! are the default for a device not calibrated yet.

use std::collections::BTreeMap;
use std::path::PathBuf;

use serde::{Deserialize, Serialize};
use sim::movement::InputKey;

/// One device's calibration (in ms, as the settings file keeps it).
#[allow(clippy::struct_field_names)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Calibration {
    /// The direction keys' offset (ms), if calibrated.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub input_offset_ms: Option<f64>,
    /// The stomp key's offset less the directions' (ms), if calibrated.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stomp_extra_ms: Option<f64>,
    /// How much later the display shows a frame than the app thinks (ms): frames are drawn for
    /// that much later.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub visual_offset_ms: Option<f64>,
    /// Added to the device's reported output latency (ms).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub latency_correction_ms: Option<f64>,
}

/// Which keys a calibration's taps were made with.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Group {
    Direction,
    Stomp,
}

impl Calibration {
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

    /// Records a tap-along's offset (ms) for `group` (`saveInputOffset`).
    pub fn set_offset(&mut self, group: Group, ms: f64) {
        match group {
            Group::Direction => self.input_offset_ms = Some(ms.round()),
            Group::Stomp => {
                self.stomp_extra_ms = Some((ms - self.input_offset_ms.unwrap_or(0.0)).round());
            }
        }
    }

    /// The display's offset (s).
    pub fn visual_s(&self) -> f64 {
        self.visual_offset_ms.unwrap_or(0.0) / 1000.0
    }

    /// The correction to the reported output latency (s).
    pub fn latency_correction_s(&self) -> f64 {
        self.latency_correction_ms.unwrap_or(0.0) / 1000.0
    }
}

/// The settings file: a calibration per output device, and a default.
#[derive(Clone, Debug, Default, PartialEq, Deserialize, Serialize)]
pub struct Settings {
    #[serde(flatten)]
    pub default: Calibration,
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub devices: BTreeMap<String, Calibration>,
    /// Global effects level, independent of output calibration and music volume.
    #[serde(default)]
    pub effects: Option<f32>,
    #[serde(default)]
    pub show_keys: Option<bool>,
}

impl Settings {
    pub fn keys_visible(&self) -> bool {
        self.show_keys.unwrap_or(true)
    }

    pub fn effects_level(&self) -> f32 {
        self.effects
            .filter(|v| v.is_finite())
            .unwrap_or(1.0)
            .clamp(0.0, 2.0)
    }

    pub fn adjust_effects(&mut self, delta: f32) -> f32 {
        let level = ((self.effects_level() + delta) * 10.0).round() / 10.0;
        self.effects = Some(level.clamp(0.0, 2.0));
        self.effects_level()
    }

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

    /// The saved settings (none when there is no file or it can't be read).
    pub fn load() -> Settings {
        Self::path()
            .and_then(|p| std::fs::read_to_string(p).ok())
            .and_then(|t| serde_json::from_str(&t).ok())
            .unwrap_or_default()
    }

    /// Writes the settings.
    pub fn save(&self) -> Result<(), String> {
        let path = Self::path().ok_or("no home directory")?;
        if let Some(dir) = path.parent() {
            std::fs::create_dir_all(dir).map_err(|e| format!("{}: {e}", dir.display()))?;
        }
        let text = serde_json::to_string_pretty(self).map_err(|e| e.to_string())?;
        std::fs::write(&path, text + "\n").map_err(|e| format!("{}: {e}", path.display()))
    }

    /// The calibration for the device `uid`: its own, else the default.
    pub fn for_device(&self, uid: &str) -> Calibration {
        self.devices.get(uid).copied().unwrap_or(self.default)
    }
}

/// The tap-along calibration (`calibrationOffset`): 8 clicks at 120 BPM.
pub const CLICKS: usize = 8;
pub const BPM: f64 = 120.0;

/// The offset (ms) from tap and heard click times (s): the median error after the first two taps,
/// counting only taps within a quarter beat of a click; none with fewer than three.
pub fn tap_offset(taps: &[f64], clicks: &[f64]) -> Option<f64> {
    let mut errs: Vec<f64> = taps
        .iter()
        .skip(2)
        .filter_map(|&t| {
            let best = clicks
                .iter()
                .map(|&c| t - c)
                .min_by(|a, b| a.abs().total_cmp(&b.abs()))?;
            (best.abs() <= 60.0 / BPM / 4.0).then_some(best * 1000.0)
        })
        .collect();
    if errs.len() < 3 {
        return None;
    }
    errs.sort_by(f64::total_cmp);
    let m = errs.len() / 2;
    Some(if errs.len() % 2 == 1 {
        errs[m]
    } else {
        f64::midpoint(errs[m - 1], errs[m])
    })
}

/// The group most taps came from (the directions on a tie).
pub fn tap_group(stomps: usize, taps: usize) -> Group {
    if stomps > taps - stomps.min(taps) {
        Group::Stomp
    } else {
        Group::Direction
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
        let mut c = Calibration::default();
        c.set_offset(Group::Direction, 30.0);
        assert!((c.offset_s(InputKey::Up) - 0.03).abs() < 1e-12);
        assert!((c.offset_s(InputKey::Space) - 0.03).abs() < 1e-12);
        // The stomp's own, saved as a difference: recalibrating the arrows moves it too.
        c.set_offset(Group::Stomp, 45.0);
        assert_eq!(c.stomp_extra_ms, Some(15.0));
        c.set_offset(Group::Direction, 40.0);
        assert!((c.offset_s(InputKey::Space) - 0.055).abs() < 1e-12);
    }

    #[test]
    fn settings_keep_a_calibration_per_device_and_read_the_old_file() {
        // (The flat file G5 wrote reads as the default.)
        let old: Settings =
            serde_json::from_str(r#"{"inputOffsetMs": 40, "stompExtraMs": 15}"#).unwrap();
        assert_eq!(old.default.input_offset_ms, Some(40.0));
        assert_eq!(old.effects_level(), 1.0);
        let mut s = old.clone();
        assert_eq!(s.adjust_effects(-0.1), 0.9);
        let mut airpods = Calibration::default();
        airpods.set_offset(Group::Direction, 120.0);
        airpods.latency_correction_ms = Some(30.0);
        s.devices.insert("AirPods-UID".into(), airpods);
        let back: Settings = serde_json::from_str(&serde_json::to_string(&s).unwrap()).unwrap();
        assert_eq!(back, s);
        assert_eq!(back.for_device("AirPods-UID").input_offset_ms, Some(120.0));
        assert_eq!(back.for_device("Speakers-UID"), old.default);
    }

    #[test]
    fn a_tap_along_takes_the_median_error_after_the_first_two_taps() {
        let clicks: Vec<f64> = (0..8).map(|k| 1.0 + f64::from(k) * 0.5).collect();
        // Late by 30 ms, the first two way off and one a stray between clicks.
        let mut taps: Vec<f64> = clicks.iter().map(|c| c + 0.03).collect();
        taps[0] = 1.2;
        taps[1] = 1.4;
        taps[5] = clicks[5] + 0.25;
        assert!((tap_offset(&taps, &clicks).unwrap() - 30.0).abs() < 1e-9);
        assert_eq!(tap_offset(&taps[..4], &clicks), None, "two counted taps");
        assert_eq!(tap_group(3, 8), Group::Direction);
        assert_eq!(tap_group(5, 8), Group::Stomp);
    }
}
