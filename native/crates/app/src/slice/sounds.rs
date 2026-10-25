//! The game's sounds (docs/design/native-app.md, "Sound (G4)"; `planner/emit.ts` and
//! `live/sounds.ts` as a reference): what the simulation's sound causes play, from the content's
//! palette (`content/sounds.yaml`, samples from `public/assets/sfx`), placed on the committed beat
//! grid never late, faded by the evidence of the beat they belong to, panned by the camera and
//! scheduled on the output's timeline when they will be heard. The simulation runs at the time
//! being heard (G5 brings the runner ahead of it), so a sound known after its time plays as soon
//! as the output can.

use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;

use audio::mixer::{Command, Source, Voice};
use eval::live::{Cause, Caused};
use serde::Deserialize;
use sim::content::ContentLibrary;
use sim::content::types::{Sample, SoundPalette, WeaponKind};
use sim::core::rng::{Part, Rng};
use stream::Beat;

use super::live::decode;

/// The palette played (the TS director's loudness levels pick among three; one for now).
const PALETTE: &str = "crisp-steel";
/// Sounds that carry information play while the beat is unclear too (`GATE_EXEMPT_SOUNDS`).
const EXEMPT: [&str; 5] = ["hurt", "level-up", "tier-up", "tier-down", "orb"];
/// A sound plays on its slot only when known at least this long before it (`NEVER_LATE`); the
/// app raises it to cover the output's latency.
pub const NEVER_LATE: f64 = 0.06;
/// The most a sample starts ahead of its onset (`MAX_SOUND_LEAD`).
const MAX_LEAD: f64 = 0.25;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Entry {
    file: String,
    onset_s: f64,
}

#[derive(Deserialize)]
struct Catalogue {
    sounds: HashMap<String, Entry>,
}

/// Decoded samples, for the output's bank.
pub type Bank = Vec<Arc<[f32]>>;

/// The grid a sound keeps to.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Every {
    Sixteenth,
    Beat,
}

/// One sound to place: its palette key, song time, place, level and whether it ducks.
struct Call {
    key: String,
    s: f64,
    x: f64,
    gain: f64,
    duck: bool,
    every: Option<Every>,
    /// The song time whose beat it belongs to (the gate's), if not its own.
    owner: Option<f64>,
}

impl Call {
    fn new(key: impl Into<String>, s: f64, x: f64, gain: f64) -> Call {
        Call {
            key: key.into(),
            s,
            x,
            gain,
            duck: false,
            every: None,
            owner: None,
        }
    }
}

/// Where the run is: its seed and start, the stream's beats and gate, the camera, and the clock
/// mapping song time to when it is heard.
pub struct At<'a> {
    pub seed: f64,
    /// The song time the run began at (the simulation's 0).
    pub run_start: f64,
    pub beats: &'a [Beat],
    pub gate: f64,
    pub camera: (f64, f64, f64),
    /// Song time `song` is heard at host time `heard`.
    pub clock: (f64, f64),
    /// From the output rendering a sample to it being heard (s).
    pub l_out: f64,
    /// The least a sound is known before its slot (s).
    pub never_late: f64,
}

/// The palette, its samples in the bank, and the kill sounds waiting on their slots.
pub struct Sounds {
    palette: SoundPalette,
    /// Each sample id's bank index and onset (s).
    samples: HashMap<String, (usize, f64)>,
    weapons: HashMap<String, (String, bool)>,
    /// Kill sounds by slot (ms): the voice and how many kills it holds.
    kills: HashMap<i64, (u64, u32, f64)>,
    next_id: u64,
}

impl Sounds {
    /// Decodes the palette's samples from `dir` at `rate`: the sounds and the bank for the output.
    pub fn load(dir: &Path, rate: f64, lib: &ContentLibrary) -> Result<(Sounds, Bank), String> {
        let text = std::fs::read_to_string(dir.join("sounds.json"))
            .map_err(|e| format!("{}: {e}", dir.display()))?;
        let catalogue: Catalogue = serde_json::from_str(&text).map_err(|e| e.to_string())?;
        let palette = lib
            .sound_palettes
            .get(PALETTE)
            .ok_or_else(|| format!("no palette {PALETTE}"))?
            .clone();
        let mut bank: Bank = Vec::new();
        let mut samples = HashMap::new();
        for list in palette.sounds.values() {
            for sample in list {
                let id = sample_id(sample);
                if samples.contains_key(id) {
                    continue;
                }
                let entry = catalogue
                    .sounds
                    .get(id)
                    .ok_or_else(|| format!("no sound {id}"))?;
                bank.push(decode(&dir.join(&entry.file), rate)?.into());
                samples.insert(id.to_string(), (bank.len() - 1, entry.onset_s));
            }
        }
        let weapons = lib
            .weapons
            .iter()
            .map(|(id, w)| (id.clone(), (w.sound.clone(), w.kind == WeaponKind::Chain)))
            .collect();
        Ok((
            Sounds {
                palette,
                samples,
                weapons,
                kills: HashMap::new(),
                next_id: 1 << 40,
            },
            bank,
        ))
    }

    /// Forgets the kill slots (a new run).
    pub fn reset(&mut self) {
        self.kills.clear();
    }

    /// A sound now: `key` centred, as soon as the output can play it (host `at`).
    pub fn now(&mut self, key: &str, at: f64, gain: f64, out: &mut Vec<Command>) {
        if let Some((sound, _, rate)) = self.pick(key, at, 0.0, 0.0) {
            self.next_id += 1;
            out.push(Command::Play(Voice {
                id: self.next_id,
                source: Source::Sample(sound),
                at,
                from: 0.0,
                lead: 0.0,
                gain: gain as f32,
                pan: 0.0,
                rate,
                duck: false,
            }));
        }
    }

    /// The commands that play `caused`.
    pub fn schedule(&mut self, caused: &[Caused], at: &At, out: &mut Vec<Command>) {
        for c in caused {
            for call in self.calls(&c.cause) {
                self.place(&call, c.known, at, out);
            }
        }
    }

    /// What a cause plays (`emit.ts`'s sound calls, their levels as it sets them).
    fn calls(&self, cause: &Cause) -> Vec<Call> {
        match cause {
            Cause::Swing { s, x, kind } => {
                let heavy = *kind == "heavy";
                let mut whoosh = Call::new("swing", s - 0.04, *x, 0.7);
                whoosh.owner = Some(*s);
                let mut strike = Call::new(
                    format!("hit-{kind}"),
                    *s,
                    *x,
                    if heavy { 1.1 } else { 0.95 },
                );
                strike.duck = heavy;
                vec![whoosh, strike]
            }
            Cause::Kill { s, x } => {
                let mut c = Call::new("kill", *s, *x, 0.6);
                c.every = Some(Every::Sixteenth);
                vec![c]
            }
            Cause::Weapon { s, x, what, caught } => {
                let Some((sound, chain)) = self.weapons.get(what) else {
                    return Vec::new();
                };
                // (A chain sounds as it fires; the others when they catch an enemy.)
                if *chain == *caught {
                    return Vec::new();
                }
                let mut c = Call::new(sound.clone(), *s, *x, if *chain { 0.7 } else { 0.5 });
                c.every = Some(Every::Sixteenth);
                vec![c]
            }
            Cause::Hurt { s } => {
                let mut c = Call::new("hurt", s + NEVER_LATE, f64::NAN, 0.8);
                c.every = Some(Every::Sixteenth);
                vec![c]
            }
            Cause::Tier { s, up } => {
                let mut c = if *up {
                    Call::new("tier-up", *s, f64::NAN, 0.8)
                } else {
                    Call::new("tier-down", *s, f64::NAN, 0.5)
                };
                c.every = Some(Every::Beat);
                vec![c]
            }
            Cause::Step { s, x } => vec![Call::new("step", *s, *x, 0.25)],
            Cause::LevelUp { s } => {
                let mut c = Call::new("level-up", *s, f64::NAN, 1.0);
                c.duck = true;
                c.every = Some(Every::Beat);
                vec![c]
            }
        }
    }

    /// A sample of `key` for a sound at song time `s` and place `x`: its bank index, onset and
    /// playback rate (a semitone either way).
    fn pick(&self, key: &str, s: f64, x: f64, seed: f64) -> Option<(usize, f64, f32)> {
        let list = self.palette.sounds.get(key).filter(|l| !l.is_empty())?;
        let mut r = Rng::new(&[
            Part::Num(seed),
            Part::Str("snd"),
            Part::Str(key),
            Part::Num((s * 1000.0).round()),
            Part::Num(if x.is_finite() {
                (x * 4.0).round()
            } else {
                0.0
            }),
        ]);
        let sample = r.pick(list);
        let &(sound, onset) = self.samples.get(sample_id(sample))?;
        let rate = 2f64.powf(r.range(-1.0, 1.0) / 12.0);
        Some((sound, onset, rate as f32))
    }

    fn place(&mut self, call: &Call, known: f64, at: &At, out: &mut Vec<Command>) {
        // On its slot, or the first one far enough after it was known: never late.
        let s = match call.every {
            Some(every) => {
                let slot = slot_at(at.beats, every, call.s);
                if slot - known >= at.never_late - 1e-9 {
                    slot
                } else {
                    slot_at(at.beats, every, known + at.never_late)
                }
            }
            // (Until G5's runner simulates ahead, a strike is known only as it happens: it plays
            // as soon as the output can rather than not at all. A whoosh before it is dropped.)
            None if call.owner.is_none() => call.s.max(known + at.never_late),
            None if call.s - known < at.never_late => return,
            None => call.s,
        };
        let fade = if EXEMPT.contains(&call.key.as_str()) {
            1.0
        } else {
            evidence_fade(at.beats, at.gate, call.owner.unwrap_or(call.s))
        };
        if fade <= 0.0 {
            return;
        }
        let Some((sound, onset, rate)) = self.pick(&call.key, s, call.x, at.seed) else {
            return;
        };
        let sample_gain = self
            .palette
            .sounds
            .get(&call.key)
            .and_then(|l| {
                l.iter().find_map(|x| match x {
                    Sample::Detailed { gain, .. } => *gain,
                    Sample::Name(_) => None,
                })
            })
            .unwrap_or(1.0);
        let gain = call.gain * fade * sample_gain * self.palette.gain;
        let (cx, _, zoom) = at.camera;
        let pan = if call.x.is_finite() {
            ((call.x - cx) / (480.0 / zoom.max(1e-3))).clamp(-0.8, 0.8)
        } else {
            0.0
        };
        let lead = (onset / f64::from(rate)).min(MAX_LEAD);
        // Heard when the song reaches it; the output renders it `l_out` before, from `lead` before
        // its onset.
        let heard = at.clock.1 + (at.run_start + s - at.clock.0);
        let start = heard - at.l_out - lead;
        // Kills share their slot: a second one turns its sound into the multi-kill.
        if call.key == "kill" {
            let slot = (s * 1000.0).round() as i64;
            if let Some(&(id, n, x)) = self.kills.get(&slot) {
                out.push(Command::Cancel(id));
                let n = n + 1;
                let multi = Call::new("kill-multi", s, x, 0.5 + 0.22 * f64::from(n).log2());
                if let Some((sound, onset, rate)) = self.pick(&multi.key, s, x, at.seed) {
                    let lead = (onset / f64::from(rate)).min(MAX_LEAD);
                    self.next_id += 1;
                    self.kills.insert(slot, (self.next_id, n, x));
                    out.push(Command::Play(Voice {
                        id: self.next_id,
                        source: Source::Sample(sound),
                        at: heard - at.l_out - lead,
                        from: 0.0,
                        lead,
                        gain: (multi.gain * fade * self.palette.gain) as f32,
                        pan: pan as f32,
                        rate,
                        duck: false,
                    }));
                }
                return;
            }
            self.kills.retain(|&k, _| (k as f64) / 1000.0 > s - 2.0);
            self.kills.insert(slot, (self.next_id + 1, 1, call.x));
        }
        self.next_id += 1;
        out.push(Command::Play(Voice {
            id: self.next_id,
            source: Source::Sample(sound),
            at: start,
            from: 0.0,
            lead,
            gain: gain as f32,
            pan: pan as f32,
            rate,
            duck: call.duck,
        }));
    }
}

fn sample_id(s: &Sample) -> &str {
    match s {
        Sample::Name(id) | Sample::Detailed { id, .. } => id,
    }
}

/// The beat index at song time `t` (fractional), by the committed beats; past the last, at its
/// period. None before the first.
fn beat_index(beats: &[Beat], t: f64) -> Option<f64> {
    let first = beats.first()?;
    if t < first.t {
        return None;
    }
    let k = beats.partition_point(|b| b.t <= t) - 1;
    Some(match beats.get(k + 1) {
        Some(next) => k as f64 + (t - beats[k].t) / (next.t - beats[k].t).max(1e-6),
        None => k as f64 + (t - beats[k].t) / beats[k].period.max(1e-3),
    })
}

/// The song time of beat index `bi`.
fn time_of(beats: &[Beat], bi: f64) -> f64 {
    let k = (bi.floor().max(0.0) as usize).min(beats.len() - 1);
    let f = bi - k as f64;
    match beats.get(k + 1) {
        Some(next) => beats[k].t + f * (next.t - beats[k].t),
        None => beats[k].t + f * beats[k].period,
    }
}

/// The first slot of `every` at or after song time `t` (`t` itself before the first beat).
fn slot_at(beats: &[Beat], every: Every, t: f64) -> f64 {
    let Some(bi) = beat_index(beats, t) else {
        return t;
    };
    let per = match every {
        Every::Sixteenth => 4.0,
        Every::Beat => 1.0,
    };
    time_of(beats, (bi * per - 1e-6).ceil() / per)
}

/// A gameplay sound's level by the evidence of the latest beat at or before song time `s`: full
/// at twice the gate, silent at it (`soundFade`).
fn evidence_fade(beats: &[Beat], gate: f64, s: f64) -> f64 {
    let k = beats.partition_point(|b| b.t <= s);
    if k == 0 {
        return 0.0;
    }
    ((beats[k - 1].evidence - gate) / gate).clamp(0.0, 1.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn beats(n: usize, period: f64, evidence: f64) -> Vec<Beat> {
        (0..n)
            .map(|k| Beat {
                t: 1.0 + k as f64 * period,
                known_at: 1.0 + k as f64 * period,
                period,
                evidence,
            })
            .collect()
    }

    #[test]
    fn slots_fall_on_the_grid_and_past_the_last_beat() {
        let b = beats(4, 0.5, 1.0);
        assert_eq!(slot_at(&b, Every::Beat, 1.2), 1.5);
        assert!((slot_at(&b, Every::Sixteenth, 1.2) - 1.25).abs() < 1e-9);
        assert_eq!(slot_at(&b, Every::Sixteenth, 1.25), 1.25, "on a slot");
        // Past the last beat (2.5), at its period.
        assert!((slot_at(&b, Every::Beat, 2.6) - 3.0).abs() < 1e-9);
        assert_eq!(slot_at(&b, Every::Beat, 0.5), 0.5, "before the first");
    }

    #[test]
    fn the_evidence_fades_sounds_below_twice_the_gate() {
        let gate = 0.2;
        assert_eq!(evidence_fade(&beats(2, 0.5, 0.4), gate, 1.6), 1.0);
        assert!((evidence_fade(&beats(2, 0.5, 0.3), gate, 1.6) - 0.5).abs() < 1e-9);
        assert_eq!(evidence_fade(&beats(2, 0.5, 0.15), gate, 1.6), 0.0);
        assert_eq!(
            evidence_fade(&beats(2, 0.5, 1.0), gate, 0.5),
            0.0,
            "no beat yet"
        );
    }

    #[test]
    fn a_kill_known_late_moves_to_a_later_slot_and_a_second_makes_a_multi_kill() {
        let lib = super::super::World::load().unwrap().lib;
        let palette = lib.sound_palettes[PALETTE].clone();
        // Every sample at bank index 0 with no onset: placement is what's tested.
        let samples = palette
            .sounds
            .values()
            .flatten()
            .map(|s| (sample_id(s).to_string(), (0, 0.0)))
            .collect();
        let mut sounds = Sounds {
            palette,
            samples,
            weapons: HashMap::new(),
            kills: HashMap::new(),
            next_id: 0,
        };
        let b = beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
        };
        let kill = |s: f64, known: f64| Caused {
            cause: Cause::Kill { s, x: 0.0 },
            known,
        };
        let mut out = Vec::new();
        // At 1.2, known at once: its sixteenth is 1.25, 50 ms on, under NEVER_LATE: it moves to 1.375.
        sounds.schedule(&[kill(1.2, 1.2)], &at, &mut out);
        let Command::Play(v) = out[0] else { panic!() };
        assert!((v.at - (100.0 + 1.375 - 0.02)).abs() < 1e-9, "{}", v.at);
        // A second kill on that slot cancels it for a multi-kill.
        out.clear();
        sounds.schedule(&[kill(1.3, 1.3)], &at, &mut out);
        assert_eq!(out[0], Command::Cancel(v.id));
        assert!(matches!(out[1], Command::Play(m) if (m.at - v.at).abs() < 1e-9 && m.id != v.id));
        // A whoosh belongs to its strike's beat: below the gate there, both are silent.
        out.clear();
        let quiet = beats(8, 0.5, 0.1);
        let at = At {
            beats: &quiet,
            ..at
        };
        sounds.schedule(
            &[Caused {
                cause: Cause::Swing {
                    s: 2.0,
                    x: 0.0,
                    kind: "light",
                },
                known: 1.5,
            }],
            &at,
            &mut out,
        );
        assert!(out.is_empty());
    }
}
