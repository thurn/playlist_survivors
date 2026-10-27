//! The reactive fast lane (native/evidence/vfx/ts-spec.md, "Reactive fast lane"; the TS game's
//! `reactiveFx` and `reactive-look.ts`): what the music's stream and the player's presses show beside
//! the sim. The beat's pulse from the ticks' evidence, an accent flash and camera punch on strong
//! onsets, a burst of sparks and light at each onset in the song's key tint, the horde's flinch, a
//! ripple from each press and the ground's glow with the good ones; and the key's look on the grade.
//! Presentation only: each frame reads the stream known by then, never the sim.

use std::collections::VecDeque;

use stream::StreamLog;

/// The window the pulse's clarity and the loudness are smoothed over (s).
const SMOOTH_S: f64 = 0.5;
/// A press's pulse decays over this long (s).
const PRESS_PULSE_S: f64 = 0.25;
/// A press's ripple lasts this long, and the glow counts the presses this far back (s).
pub const RIPPLE_S: f64 = 0.45;
const GLOW_S: f64 = 2.0;
/// Accent flashes come at most this often (s).
const FLASH_GAP: f64 = 0.5;
/// An onset's strength is measured against a running peak halving over this long (s).
const ONSET_PEAK_HALF_S: f64 = 8.0;
/// An onset's burst lasts this long, the horde's flinch decays over this long (s), and a burst keeps
/// this share with the beat unclear.
pub const BURST_LIFE: f64 = 0.4;
const FLINCH_S: f64 = 0.07;
pub const BURST_FLOOR: f64 = 0.35;
/// A zone's key is the commonest of the ticks over this long before its wave (s); its look fades in
/// over this long.
const KEY_WINDOW_S: f64 = 4.0;
const LOOK_FADE_S: f64 = 1.0;

/// A grading tweak: multipliers and lift per channel, and a saturation factor.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Look {
    pub mul: [f32; 3],
    pub lift: [f32; 3],
    pub sat: f32,
}

impl Default for Look {
    fn default() -> Look {
        Look {
            mul: [1.0; 3],
            lift: [0.0; 3],
            sat: 1.0,
        }
    }
}

/// An onset's burst in flight: which (its index, for its seeded place), how old (s) and how strong.
#[derive(Clone, Copy, Debug)]
pub struct Burst {
    pub index: usize,
    pub age: f64,
    pub n: f64,
}

/// What the fast lane shows this frame.
#[derive(Clone, Debug, Default)]
pub struct Frame {
    /// The beat's evidence over the gate (0-1), the latest press's pulse, the share of the last
    /// 0.5 s with the beat clear, and the energy smoothed over it.
    pub amp: f64,
    pub press: f64,
    pub mix: f64,
    pub loudness: f64,
    /// A short pulse from an actual positive loudness jump already known at this frame.
    pub drop: f64,
    /// The accent flash's alpha and the camera's punch (a zoom factor over 1).
    pub flash: f64,
    pub punch: f64,
    /// The horde's flinch from the latest onset.
    pub flinch: f64,
    /// The share of the last 2 s of presses rated great or better.
    pub glow: f64,
    pub bursts: Vec<Burst>,
    /// Each press's ripple: its age (s) and tint.
    pub ripples: Vec<(f64, [f32; 4])>,
    /// The song's tint for effects, and the key's look on the grade.
    pub tint: [f32; 4],
    pub look: Look,
}

impl Frame {
    /// A beat-synced pulse (0-1) as the fast lane shows it: the beat's where it is clear, the
    /// presses' where it is not.
    pub fn pulsed(&self, v: f64) -> f64 {
        self.mix * self.amp * v + (1.0 - self.mix) * self.press
    }

    /// How strongly the horde dances: as the pulse follows the committed beats.
    pub fn groove(&self) -> f64 {
        self.mix * self.amp
    }
}

/// The fast lane's memory: each onset's strength share and the accent flashes as they become
/// known, the key at each zone wave, and the recent presses.
#[derive(Default)]
pub struct Lane {
    shares: Vec<f64>,
    flashes: Vec<f64>,
    peak: f64,
    peak_at: f64,
    /// The newest wave's start (s), its key and the one before.
    wave_s: Option<f64>,
    key: Option<u8>,
    prev_key: Option<u8>,
    /// Presses: when (host s), their rating, and whether rated great or better.
    presses: VecDeque<(f64, Option<&'static str>, bool)>,
}

impl Lane {
    /// A press heard at host time `now`, with its provisional rating.
    pub fn press(&mut self, now: f64, rating: Option<&'static str>, unjudged: bool) {
        let good = !unjudged && matches!(rating, Some("perfect" | "great"));
        self.presses.push_back((now, rating, good));
    }

    /// The frame at run time `s` (host time `now`) from the stream known by then, its evidence
    /// gate, and the newest zone wave's start.
    #[allow(clippy::many_single_char_names)]
    pub fn frame(
        &mut self,
        log: &StreamLog,
        gate: f64,
        s: f64,
        now: f64,
        wave_s: Option<f64>,
    ) -> Frame {
        self.learn(log, s, wave_s);
        let mut f = Frame::default();
        // The pulse: the latest tick's evidence, and the last 0.5 s's clarity and energy.
        let ticks = &log.ticks;
        let i = ticks.partition_point(|t| t.t <= s + 1e-9);
        if let Some(t) = ticks[..i].iter().rev().find(|t| t.known_at <= s + 1e-9) {
            f.amp = ((t.evidence - gate) / gate).clamp(0.0, 1.0);
        }
        if let Some(jump) = log
            .jumps
            .as_deref()
            .unwrap_or(&[])
            .iter()
            .rev()
            .find(|j| j.known_at <= s && j.t <= s && j.db >= 6.0)
        {
            let age = s - jump.known_at.max(jump.t);
            f.drop = (jump.db / 12.0).clamp(0.0, 1.0) * (-age / 0.3).exp();
        }
        let (mut n, mut clear, mut energy) = (0usize, 0usize, 0.0);
        for t in ticks[..i]
            .iter()
            .rev()
            .take_while(|t| t.t > s - SMOOTH_S + 1e-9)
        {
            if t.known_at > s + 1e-9 {
                continue;
            }
            n += 1;
            clear += usize::from(t.evidence >= gate);
            energy += t.energy;
        }
        if n > 0 {
            f.mix = clear as f64 / n as f64;
            f.loudness = energy / n as f64;
        }
        // The presses: the pulse, the ripples and the glow.
        while self.presses.front().is_some_and(|p| now - p.0 > GLOW_S) {
            self.presses.pop_front();
        }
        if let Some(&(last, ..)) = self.presses.back() {
            f.press = (-(now - last).max(0.0) / PRESS_PULSE_S).exp();
        }
        let good = self.presses.iter().filter(|p| p.2).count();
        if !self.presses.is_empty() {
            f.glow = good as f64 / self.presses.len() as f64;
        }
        f.ripples = self
            .presses
            .iter()
            .filter(|p| (0.0..RIPPLE_S).contains(&(now - p.0)))
            .map(|p| (now - p.0, ripple_tint(p.1)))
            .collect();
        // The latest accent flash.
        let known = self.flashes.partition_point(|&k| k <= s);
        if let Some(&k) = known.checked_sub(1).map(|j| &self.flashes[j]) {
            let age = s - k;
            if age <= 0.6 {
                f.flash = 0.22 * (-age / 0.07).exp();
                f.punch = 0.018 * (-age / 0.16).exp();
            }
        }
        // The onsets' bursts, newest first; the newest's flinch.
        let onsets = &log.onsets[..self.shares.len()];
        let latest = onsets.partition_point(|o| o.known_at <= s);
        for j in (0..latest).rev() {
            let age = s - onsets[j].known_at;
            if age >= BURST_LIFE {
                break;
            }
            let n = self.shares[j];
            if j + 1 == latest {
                f.flinch = n * (-age / FLINCH_S).exp();
            }
            f.bursts.push(Burst { index: j, age, n });
        }
        // The song's key: its tint, and its look fading in from the last zone's.
        f.tint = key_tint(self.key);
        f.look = match self.wave_s {
            Some(w) => blend(
                key_look(self.prev_key),
                key_look(self.key),
                ((s - w) / LOOK_FADE_S).clamp(0.0, 1.0) as f32,
            ),
            None => Look::default(),
        };
        f
    }

    /// Takes in the onsets known since the last frame, and the key at a new zone wave.
    fn learn(&mut self, log: &StreamLog, s: f64, wave_s: Option<f64>) {
        // (A new run starts a new stream.)
        if log.onsets.len() < self.shares.len() {
            *self = Lane {
                presses: std::mem::take(&mut self.presses),
                ..Lane::default()
            };
        }
        for o in log.onsets[self.shares.len()..]
            .iter()
            .take_while(|o| o.known_at <= s + 1e-9)
        {
            let decay = 0.5f64.powf((o.known_at - self.peak_at).max(0.0) / ONSET_PEAK_HALF_S);
            self.peak = o.strength.max(self.peak * decay);
            self.peak_at = o.known_at;
            self.shares.push(if self.peak > 0.0 {
                (o.strength / self.peak).sqrt()
            } else {
                0.0
            });
            if o.strong
                && self
                    .flashes
                    .last()
                    .is_none_or(|&k| o.known_at - k >= FLASH_GAP - 1e-9)
            {
                self.flashes.push(o.known_at);
            }
        }
        if wave_s.is_some() && wave_s != self.wave_s {
            self.prev_key = self.key;
            self.key = dominant_key(log, wave_s.unwrap_or(s));
            self.wave_s = wave_s;
        }
    }
}

/// A press ripple's tint by its rating (`RIPPLE_TINTS`).
fn ripple_tint(rating: Option<&str>) -> [f32; 4] {
    super::fx::rgb(match rating {
        Some("perfect") => 0x00ff_d040,
        Some("great") => 0x00ff_ffff,
        Some("good") => 0x009f_d0ff,
        Some("loose") => 0x006a_7080,
        Some("stray") => 0x00e0_4848,
        _ => 0x008a_8a96,
    })
}

/// A key name ("F# minor") as its pitch class (0-11) for a major key, plus 12 for a minor one.
pub fn key_code(name: &str) -> Option<u8> {
    let (note, mode) = name.split_once(' ')?;
    let mut chars = note.chars();
    let pc: u8 = match chars.next()? {
        'C' => 0,
        'D' => 2,
        'E' => 4,
        'F' => 5,
        'G' => 7,
        'A' => 9,
        'B' => 11,
        _ => return None,
    };
    let pc = match chars.as_str() {
        "" => pc,
        "#" => (pc + 1) % 12,
        "b" => (pc + 11) % 12,
        _ => return None,
    };
    match mode {
        "major" => Some(pc),
        "minor" => Some(pc + 12),
        _ => None,
    }
}

/// The commonest key of the ticks in (s - 4, s], a tie going to the one heard last.
fn dominant_key(log: &StreamLog, s: f64) -> Option<u8> {
    let hi = log.ticks.partition_point(|t| t.t <= s + 1e-9);
    // (Each key's votes, in the order first met going back from s.)
    let mut votes: Vec<(u8, usize)> = Vec::new();
    for t in log.ticks[..hi]
        .iter()
        .rev()
        .take_while(|t| t.t > s - KEY_WINDOW_S)
    {
        if t.known_at <= s + 1e-9
            && let Some(c) = t.key.as_deref().and_then(key_code)
        {
            match votes.iter_mut().find(|v| v.0 == c) {
                Some(v) => v.1 += 1,
                None => votes.push((c, 1)),
            }
        }
    }
    let mut best: Option<(u8, usize)> = None;
    for v in votes {
        if best.is_none_or(|b| v.1 > b.1) {
            best = Some(v);
        }
    }
    best.map(|b| b.0)
}

/// A fully saturated colour at hue `h` (turns).
fn hue_rgb(h: f32) -> [f32; 3] {
    let f = |n: f32| {
        let k = (n + h * 6.0) % 6.0;
        1.0 - k.min(4.0 - k).clamp(0.0, 1.0)
    };
    [f(5.0), f(3.0), f(1.0)]
}

/// How strongly the key tints the grade, and how far a minor key cools toward blue and darkens.
const KEY_TINT: f32 = 0.1;
const MINOR_COOL: f32 = 0.4;
const MINOR_DARK: f32 = 0.88;
const COOL: [f32; 3] = [0.35, 0.55, 1.0];

/// A key's colour: the hue walks the circle of fifths; a minor key cooler.
fn key_colour(code: u8) -> [f32; 3] {
    let fifths = (u32::from(code % 12) * 7) % 12;
    let c = hue_rgb(fifths as f32 / 12.0);
    if code >= 12 {
        [0, 1, 2].map(|i| c[i] + (COOL[i] - c[i]) * MINOR_COOL)
    } else {
        c
    }
}

/// A key's look: its colour a little over the grade; a minor key darker and greyer.
fn key_look(code: Option<u8>) -> Look {
    let Some(code) = code else {
        return Look::default();
    };
    let c = key_colour(code);
    let dark = if code >= 12 { MINOR_DARK } else { 1.0 };
    Look {
        mul: c.map(|v| (1.0 + (v - 1.0) * KEY_TINT) * dark),
        lift: c.map(|v| 0.018 * v),
        sat: if code >= 12 { 0.9 } else { 1.0 },
    }
}

/// A key's tint for the effects that take the song's colour (lightened toward white).
fn key_tint(code: Option<u8>) -> [f32; 4] {
    match code {
        None => super::fx::rgb(0x00ff_f0c0),
        Some(code) => {
            let c = key_colour(code).map(|v| v + (1.0 - v) * 0.45);
            [c[0], c[1], c[2], 1.0]
        }
    }
}

fn blend(a: Look, b: Look, t: f32) -> Look {
    let mix = |x: f32, y: f32| x + (y - x) * t;
    Look {
        mul: [0, 1, 2].map(|i| mix(a.mul[i], b.mul[i])),
        lift: [0, 1, 2].map(|i| mix(a.lift[i], b.lift[i])),
        sat: mix(a.sat, b.sat),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use stream::{Onset, Tick};

    fn tick(t: f64, evidence: f64, key: &str) -> Tick {
        Tick {
            t,
            known_at: t,
            evidence,
            energy: 0.5,
            level: None,
            key: Some(key.to_string()),
            bands: None,
            brightness: None,
        }
    }

    fn onset(known_at: f64, strength: f64, strong: bool) -> Onset {
        Onset {
            t: known_at - 0.05,
            known_at,
            strength,
            strong,
            band: None,
        }
    }

    fn log() -> StreamLog {
        StreamLog {
            beats: Vec::new(),
            ticks: (0..100)
                .map(|k| tick(f64::from(k) * 0.05, 0.4, "A minor"))
                .collect(),
            onsets: vec![
                onset(1.0, 2.0, true),
                onset(1.2, 0.5, true),
                onset(1.7, 2.0, true),
            ],
            hindsight: None,
            timbre: None,
            changes: None,
            jumps: None,
            ends: None,
            forecasts: None,
        }
    }

    #[test]
    fn keys_read_as_their_pitch_class_and_mode() {
        assert_eq!(key_code("C major"), Some(0));
        assert_eq!(key_code("F# minor"), Some(18));
        assert_eq!(key_code("Bb major"), Some(10));
        assert_eq!(key_code("H major"), None);
    }

    #[test]
    fn the_pulse_follows_the_evidence_and_the_onsets_flash_once_a_gap() {
        let mut lane = Lane::default();
        let l = log();
        let f = lane.frame(&l, 0.2, 1.25, 1.25, None);
        // (Evidence twice the gate: a full pulse, the beat clear throughout.)
        assert!((f.amp - 1.0).abs() < 1e-9 && (f.mix - 1.0).abs() < 1e-9);
        // The second onset is too soon for a flash of its own: the flash is the first's.
        assert_eq!(
            lane.flashes,
            vec![1.0],
            "the future onset is not learned yet"
        );
        assert!((f.flash - 0.22 * (-0.25f64 / 0.07).exp()).abs() < 1e-9);
        // A quieter onset is a smaller share of the peak, and flinches the horde less.
        assert!((lane.shares[1] - 0.5).abs() < 0.01);
        assert_eq!(f.bursts.len(), 2);
        assert!(f.flinch < 0.5 && f.flinch > 0.0);
        lane.frame(&l, 0.2, 1.75, 1.75, None);
        assert_eq!(lane.flashes, vec![1.0, 1.7]);
    }

    #[test]
    fn nothing_is_shown_before_it_is_known() {
        let mut lane = Lane::default();
        let f = lane.frame(&log(), 0.2, 0.9, 0.9, None);
        assert!(f.bursts.is_empty() && f.flash == 0.0 && f.flinch == 0.0);
    }

    #[test]
    fn a_wave_takes_the_key_heard_before_it_and_fades_its_look_in() {
        let mut lane = Lane::default();
        let l = log();
        let before = lane.frame(&l, 0.2, 2.0, 2.0, None);
        assert_eq!(before.look, Look::default());
        let start = lane.frame(&l, 0.2, 3.0, 3.0, Some(3.0));
        assert_eq!(lane.key, Some(21));
        assert_eq!(start.look, Look::default());
        let after = lane.frame(&l, 0.2, 4.5, 4.5, Some(3.0));
        assert_eq!(after.look, key_look(Some(21)));
        assert_ne!(after.tint, key_tint(None));
    }

    #[test]
    fn presses_ripple_and_the_good_ones_light_the_ground() {
        let mut lane = Lane::default();
        lane.press(10.0, Some("perfect"), false);
        lane.press(10.5, Some("good"), false);
        let f = lane.frame(&log(), 0.2, 0.0, 10.6, None);
        // (The first press's ripple has gone.)
        assert_eq!(f.ripples.len(), 1);
        assert!((f.glow - 0.5).abs() < 1e-9);
        assert!((f.press - (-0.1f64 / 0.25).exp()).abs() < 1e-9);
        let later = lane.frame(&log(), 0.2, 0.0, 13.0, None);
        assert!(later.ripples.is_empty() && later.glow == 0.0);
    }
    #[test]
    fn unknown_ticks_jumps_and_onsets_do_not_change_the_frame_or_zone_key() {
        let mut log = StreamLog::default();
        log.ticks.push(tick(0.0, 1.0, "C major"));
        let baseline = Lane::default().frame(&log, 0.2, 1.0, 1.0, Some(1.0));
        let mut late = tick(0.9, 0.0, "F# minor");
        late.energy = 1.0;
        late.known_at = 2.0;
        log.ticks.push(late);
        log.jumps = Some(vec![stream::Jump {
            t: 0.9,
            known_at: 2.0,
            db: 12.0,
        }]);
        log.onsets.push(onset(2.0, 1.0, true));
        let frame = Lane::default().frame(&log, 0.2, 1.0, 1.0, Some(1.0));
        assert_eq!(frame.loudness, baseline.loudness);
        assert_eq!(frame.amp, baseline.amp);
        assert_eq!(frame.tint, baseline.tint);
        assert_eq!(frame.drop, 0.0);
        assert!(frame.bursts.is_empty());
        let after = Lane::default().frame(&log, 0.2, 2.0, 2.0, None);
        assert_eq!(after.drop, 1.0);
        assert!(!after.bursts.is_empty());
    }
}
