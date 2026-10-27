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
/// The music's envelopes: each drum band's hit decays over this long from when it sounds, and the
/// bass level releases over this long (s); none looks further back than the window.
const KICK_DECAY_S: f64 = 0.11;
const SNARE_DECAY_S: f64 = 0.09;
const HAT_DECAY_S: f64 = 0.045;
const BASS_RELEASE_S: f64 = 0.22;
const ENVELOPE_WINDOW_S: f64 = 0.6;
/// With lookahead (Delayed listening), a kick is anticipated over this long before it sounds, and a
/// loudness jump (a drop) builds over this long before it lands (s).
const ANTICIPATE_S: f64 = 0.09;
/// A tick's kick flux is anticipated only above this floor (its faint flux is nearly constant).
const KICK_PRE_FLOOR: f64 = 0.4;
const DROP_RISE_S: f64 = 1.6;

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

/// The music as continuous envelopes (each 0-1), for the layer under the beat game: each drum
/// band's latest hits (its labelled onsets and its ticks' flux, whichever is stronger) decaying
/// from when they sound, and the bass level with a fast attack and a slow release. They never wait
/// on a beat, so a frame of them can't be wrong the way a missed beat is. With lookahead they also
/// see what is coming: the next kick's anticipation, rising to it as it sounds, and a drop's
/// build-up, rising to it as it lands (both 0 in Direct listening).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Envelopes {
    pub kick: f64,
    pub snare: f64,
    pub hat: f64,
    pub bass: f64,
    pub kick_pre: f64,
    pub rise: f64,
    /// How strongly the layer shows them (1 as measured; E exaggerates it, to see what each does).
    pub gain: f64,
}

impl Envelopes {
    /// The bass above its usual floor (0-1, times the gain): the level sits near 0.5 through most
    /// music.
    pub fn bass_drive(&self) -> f64 {
        ((self.bass - 0.35) / 0.65).clamp(0.0, 1.0) * self.gain
    }

    /// The envelopes shown `g` times as strongly.
    #[must_use]
    pub fn scaled(self, g: f64) -> Envelopes {
        Envelopes {
            kick: self.kick * g,
            snare: self.snare * g,
            hat: self.hat * g,
            kick_pre: self.kick_pre * g,
            rise: self.rise * g,
            gain: self.gain * g,
            ..self
        }
    }
}

/// What the fast lane shows this frame.
#[derive(Clone, Debug, Default)]
pub struct Frame {
    /// The beat's evidence over the gate (0-1), the latest press's pulse, the share of the last
    /// 0.5 s with the beat clear, and the energy smoothed over it.
    pub locked: bool,
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
    /// The music's envelopes.
    pub env: Envelopes,
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
    judging: Option<sim::reactive::judging::Judging>,
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
    /// gate, and the newest zone wave's start. `ahead` is the lookahead (s): Delayed listening's
    /// entries arrive `M - A` before they are heard, so the lane (presentation only, never the
    /// sim) may read an entry that long before its `known_at`, and shows each effect when it
    /// sounds; 0 in Direct listening, where everything shows at its `known_at`.
    #[allow(clippy::many_single_char_names, clippy::too_many_arguments)]
    pub fn frame(
        &mut self,
        log: &StreamLog,
        gate: f64,
        s: f64,
        now: f64,
        wave_s: Option<f64>,
        ahead: f64,
    ) -> Frame {
        self.learn(log, s, ahead, wave_s);
        let mut f = Frame::default();
        let known = log.ticks.partition_point(|t| t.known_at <= s + 1e-9);
        if self.judging.as_ref().is_none_or(|j| j.len() > known) {
            self.judging = Some(sim::reactive::judging::Judging::new(gate));
        }
        let judging = self.judging.as_mut().expect("judging");
        judging.extend(&log.ticks[..known]);
        let heard = log.ticks[..known].partition_point(|t| t.t <= s + 1e-9);
        f.locked = heard
            .checked_sub(1)
            .is_some_and(|i| judging.judged_at(i as isize) && s - log.ticks[i].t <= 0.25)
            && log
                .beats
                .iter()
                .any(|b| b.known_at <= s && b.t >= s && b.evidence >= gate);

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
            .find(|j| j.known_at <= s + ahead && j.t <= s && j.db >= 6.0)
        {
            let age = s - shown(jump.t, jump.known_at, ahead);
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
        let latest = onsets.partition_point(|o| shown(o.t, o.known_at, ahead) <= s);
        for j in (0..latest).rev() {
            let age = s - shown(onsets[j].t, onsets[j].known_at, ahead);
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
        f.env = envelopes(log, &self.shares, s, ahead);
        f
    }

    /// Takes in the onsets known since the last frame (`ahead` early), and the key at a new zone
    /// wave.
    fn learn(&mut self, log: &StreamLog, s: f64, ahead: f64, wave_s: Option<f64>) {
        // (A new run starts a new stream.)
        if log.onsets.len() < self.shares.len() {
            *self = Lane {
                presses: std::mem::take(&mut self.presses),
                ..Lane::default()
            };
        }
        for o in log.onsets[self.shares.len()..]
            .iter()
            .take_while(|o| o.known_at <= s + ahead + 1e-9)
        {
            let decay = 0.5f64.powf((o.known_at - self.peak_at).max(0.0) / ONSET_PEAK_HALF_S);
            self.peak = o.strength.max(self.peak * decay);
            self.peak_at = o.known_at;
            self.shares.push(if self.peak > 0.0 {
                (o.strength / self.peak).sqrt()
            } else {
                0.0
            });
            let at = shown(o.t, o.known_at, ahead);
            if o.strong
                && self
                    .flashes
                    .last()
                    .is_none_or(|&k| at - k >= FLASH_GAP - 1e-9)
            {
                self.flashes.push(at);
            }
        }
        if wave_s.is_some() && wave_s != self.wave_s {
            self.prev_key = self.key;
            self.key = dominant_key(log, wave_s.unwrap_or(s));
            self.wave_s = wave_s;
        }
    }
}

/// When an entry's effect shows: when it sounds, unless it can't be known by then (its
/// `known_at`, less the lookahead).
fn shown(t: f64, known_at: f64, ahead: f64) -> f64 {
    t.max(known_at - ahead)
}

/// The music's envelopes at run time `s`, from the ticks and onsets known by then, `ahead` early
/// (`shares` holds each learned onset's strength share). A hit's envelope peaks when it sounds, at
/// its `t`; a kick still to come is anticipated, and a drop still to come builds.
fn envelopes(log: &StreamLog, shares: &[f64], s: f64, ahead: f64) -> Envelopes {
    let mut e = Envelopes {
        gain: 1.0,
        ..Envelopes::default()
    };
    let hit = |v: f64, age: f64, decay: f64| v.clamp(0.0, 1.0) * (-age.max(0.0) / decay).exp();
    // (Rising to 1 as it sounds, `lead` s ahead.)
    let pre = |v: f64, lead: f64| v.clamp(0.0, 1.0) * (1.0 - lead / ANTICIPATE_S).max(0.0).powi(2);
    let known = |known_at: f64| known_at <= s + ahead + 1e-9;
    let ticks = &log.ticks[..log
        .ticks
        .partition_point(|t| t.t <= s + ANTICIPATE_S + 1e-9)];
    for t in ticks
        .iter()
        .rev()
        .take_while(|t| s - t.t < ENVELOPE_WINDOW_S)
    {
        let Some(b) = t.bands.filter(|_| known(t.known_at)) else {
            continue;
        };
        let age = s - t.t;
        if age < 0.0 {
            let strong = (b.kick - KICK_PRE_FLOOR) / (1.0 - KICK_PRE_FLOOR);
            e.kick_pre = e.kick_pre.max(pre(strong, -age));
            continue;
        }
        e.kick = e.kick.max(hit(b.kick, age, KICK_DECAY_S));
        e.snare = e.snare.max(hit(b.snare, age, SNARE_DECAY_S));
        e.hat = e.hat.max(hit(b.hat, age, HAT_DECAY_S));
        e.bass = e.bass.max(hit(b.bass, age, BASS_RELEASE_S));
    }
    let onsets = &log.onsets[..shares.len()];
    for (j, o) in onsets.iter().enumerate().rev() {
        let age = s - o.t;
        if age >= ENVELOPE_WINDOW_S {
            break;
        }
        if !known(o.known_at) || age < -ANTICIPATE_S {
            continue;
        }
        if age < 0.0 {
            if o.band == Some(stream::Band::Kick) {
                e.kick_pre = e.kick_pre.max(pre(shares[j], -age));
            }
            continue;
        }
        let (slot, decay) = match o.band {
            Some(stream::Band::Kick) => (&mut e.kick, KICK_DECAY_S),
            Some(stream::Band::Snare) => (&mut e.snare, SNARE_DECAY_S),
            Some(stream::Band::Hat) => (&mut e.hat, HAT_DECAY_S),
            _ => continue,
        };
        *slot = slot.max(hit(shares[j], age, decay));
    }
    // The next drop already known: its build, easing in, as strong as the jump.
    if let Some(j) = log
        .jumps
        .as_deref()
        .unwrap_or(&[])
        .iter()
        .find(|j| j.t > s && j.t - s < DROP_RISE_S && j.db >= 6.0 && known(j.known_at))
    {
        let u = 1.0 - (j.t - s) / DROP_RISE_S;
        e.rise = (j.db / 12.0).clamp(0.0, 1.0) * u * u * (3.0 - 2.0 * u);
    }
    e
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
    fn envelopes_peak_when_a_hit_sounds_and_wait_for_known_at_in_direct() {
        let mut l = log();
        // A labelled kick heard at 1.0, known 10 ms later; a hat known only at 2.0.
        l.onsets = vec![
            Onset {
                t: 1.0,
                known_at: 1.01,
                strength: 1.0,
                strong: false,
                band: Some(stream::Band::Kick),
            },
            Onset {
                t: 1.5,
                known_at: 2.0,
                strength: 1.0,
                strong: false,
                band: Some(stream::Band::Hat),
            },
        ];
        // The ticks' own flux: a snare at 1.05, the bass full at 1.0.
        for t in &mut l.ticks {
            t.bands = Some(stream::Bands {
                kick: 0.0,
                snare: if (t.t - 1.05).abs() < 1e-9 { 0.8 } else { 0.0 },
                hat: 0.0,
                bass: if (t.t - 1.0).abs() < 1e-9 { 1.0 } else { 0.3 },
            });
        }
        let mut lane = Lane::default();
        // Before the kick is known, nothing of it shows.
        assert_eq!(lane.frame(&l, 0.2, 1.005, 1.005, None, 0.0).env.kick, 0.0);
        let e = lane.frame(&l, 0.2, 1.1, 1.1, None, 0.0).env;
        // Its envelope decays from when it sounded, not from when it was known.
        assert!((e.kick - (-0.1 / KICK_DECAY_S).exp()).abs() < 1e-9);
        assert!((e.snare - 0.8 * (-0.05 / SNARE_DECAY_S).exp()).abs() < 1e-9);
        assert!((e.bass - (-0.1 / BASS_RELEASE_S).exp()).abs() < 1e-9);
        assert!(e.bass_drive() > 0.0 && e.bass_drive() < 1.0);
        // The hat sounded at 1.5 but isn't known until 2.0.
        assert_eq!(lane.frame(&l, 0.2, 1.6, 1.6, None, 0.0).env.hat, 0.0);
        // Long after, everything has released to the bass's floor.
        let e = lane.frame(&l, 0.2, 3.0, 3.0, None, 0.0).env;
        assert_eq!((e.kick, e.snare, e.hat), (0.0, 0.0, 0.0));
        assert!((e.bass - 0.3).abs() < 1e-9 && e.bass_drive() == 0.0);
    }

    #[test]
    fn lookahead_anticipates_kicks_builds_drops_and_shows_hits_as_they_sound() {
        let mut l = log();
        // Delayed listening: a strong kick heard at 2.0 and a 12 dB drop landing at 3.0, each
        // known (at its analysis position) well after it sounds, but arriving 2.95 s early.
        l.onsets = vec![Onset {
            t: 2.0,
            known_at: 2.01,
            strength: 1.0,
            strong: true,
            band: Some(stream::Band::Kick),
        }];
        l.jumps = Some(vec![stream::Jump {
            t: 3.0,
            known_at: 4.0,
            db: 12.0,
        }]);
        let ahead = 2.95;
        let at = |s: f64, ahead: f64| Lane::default().frame(&l, 0.2, s, s, None, ahead);
        // Direct listening sees neither coming.
        let direct = at(1.95, 0.0).env;
        assert_eq!((direct.kick_pre, direct.rise), (0.0, 0.0));
        // With lookahead the kick is anticipated, rising to it, and nothing before its window.
        let (early, near) = (at(1.95, ahead).env, at(1.99, ahead).env);
        assert!(early.kick_pre > 0.0 && near.kick_pre > early.kick_pre && near.kick_pre < 1.0);
        assert_eq!(at(1.9, ahead).env.kick_pre, 0.0);
        // It lands exactly when it sounds: the envelope full, the flash and burst at age 0.
        let hit = at(2.0, ahead);
        assert!((hit.env.kick - 1.0).abs() < 1e-9 && hit.env.kick_pre == 0.0);
        assert!((hit.flash - 0.22).abs() < 1e-9);
        assert_eq!(hit.bursts.len(), 1);
        assert!(hit.bursts[0].age.abs() < 1e-9);
        // Direct listening shows the same flash only once it is known.
        assert_eq!(at(2.0, 0.0).flash, 0.0);
        // The drop builds over its last 1.6 s, and releases as it lands, its pulse on time.
        assert_eq!(at(1.3, ahead).env.rise, 0.0);
        let (mid, late) = (at(2.2, ahead).env.rise, at(2.9, ahead).env.rise);
        assert!(mid > 0.0 && late > mid && late < 1.0);
        let landed = at(3.0, ahead);
        assert_eq!(landed.env.rise, 0.0);
        assert!((landed.drop - 1.0).abs() < 1e-9);
        assert_eq!(at(3.0, 0.0).drop, 0.0, "Direct waits for its known_at");
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
        let f = lane.frame(&l, 0.2, 1.25, 1.25, None, 0.0);
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
        lane.frame(&l, 0.2, 1.75, 1.75, None, 0.0);
        assert_eq!(lane.flashes, vec![1.0, 1.7]);
    }

    #[test]
    fn nothing_is_shown_before_it_is_known() {
        let mut lane = Lane::default();
        let f = lane.frame(&log(), 0.2, 0.9, 0.9, None, 0.0);
        assert!(f.bursts.is_empty() && f.flash == 0.0 && f.flinch == 0.0);
    }

    #[test]
    fn a_wave_takes_the_key_heard_before_it_and_fades_its_look_in() {
        let mut lane = Lane::default();
        let l = log();
        let before = lane.frame(&l, 0.2, 2.0, 2.0, None, 0.0);
        assert_eq!(before.look, Look::default());
        let start = lane.frame(&l, 0.2, 3.0, 3.0, Some(3.0), 0.0);
        assert_eq!(lane.key, Some(21));
        assert_eq!(start.look, Look::default());
        let after = lane.frame(&l, 0.2, 4.5, 4.5, Some(3.0), 0.0);
        assert_eq!(after.look, key_look(Some(21)));
        assert_ne!(after.tint, key_tint(None));
    }

    #[test]
    fn presses_ripple_and_the_good_ones_light_the_ground() {
        let mut lane = Lane::default();
        lane.press(10.0, Some("perfect"), false);
        lane.press(10.5, Some("good"), false);
        let f = lane.frame(&log(), 0.2, 0.0, 10.6, None, 0.0);
        // (The first press's ripple has gone.)
        assert_eq!(f.ripples.len(), 1);
        assert!((f.glow - 0.5).abs() < 1e-9);
        assert!((f.press - (-0.1f64 / 0.25).exp()).abs() < 1e-9);
        let later = lane.frame(&log(), 0.2, 0.0, 13.0, None, 0.0);
        assert!(later.ripples.is_empty() && later.glow == 0.0);
    }
    #[test]
    fn unknown_ticks_jumps_and_onsets_do_not_change_the_frame_or_zone_key() {
        let mut log = StreamLog::default();
        log.ticks.push(tick(0.0, 1.0, "C major"));
        let baseline = Lane::default().frame(&log, 0.2, 1.0, 1.0, Some(1.0), 0.0);
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
        let frame = Lane::default().frame(&log, 0.2, 1.0, 1.0, Some(1.0), 0.0);
        assert_eq!(frame.loudness, baseline.loudness);
        assert_eq!(frame.amp, baseline.amp);
        assert_eq!(frame.tint, baseline.tint);
        assert_eq!(frame.drop, 0.0);
        assert!(frame.bursts.is_empty());
        let after = Lane::default().frame(&log, 0.2, 2.0, 2.0, None, 0.0);
        assert_eq!(after.drop, 1.0);
        assert!(!after.bursts.is_empty());
    }
}

#[cfg(test)]
mod hud_lock_tests {
    use super::*;
    #[test]
    fn musical_lock_uses_heard_known_ticks_and_stops_after_a_gap() {
        let log = StreamLog {
            ticks: (0..=30)
                .map(|i| stream::Tick {
                    t: f64::from(i) * 0.05,
                    known_at: f64::from(i) * 0.05 + 0.2,
                    evidence: if i < 28 { 1.0 } else { 0.0 },
                    energy: 0.6,
                    level: None,
                    key: None,
                    bands: None,
                    brightness: None,
                })
                .collect(),
            beats: (0..10)
                .map(|i| stream::Beat {
                    t: f64::from(i) * 0.5,
                    known_at: (f64::from(i) * 0.5 - 0.5).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                })
                .collect(),
            ..StreamLog::default()
        };
        let mut lane = Lane::default();
        assert!(
            !lane.frame(&log, 0.2, 0.8, 0.8, None, 0.0).locked,
            "one-second admission gate"
        );
        assert!(
            lane.frame(&log, 0.2, 1.5, 1.5, None, 0.0).locked,
            "future low confidence is not heard yet"
        );
        assert!(!lane.frame(&log, 0.2, 1.61, 1.61, None, 0.0).locked);
        assert!(
            !lane.frame(&log, 0.2, 3.0, 3.0, None, 0.0).locked,
            "no stale metronome"
        );
    }
}
