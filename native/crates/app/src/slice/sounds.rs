//! The game's sounds (docs/design/native-app.md, "Sound (G4)"; `planner/emit.ts` and
//! `live/sounds.ts` as a reference): what the simulation's sound causes play, from the content's
//! palette (`content/sounds.yaml`, samples from `public/assets/sfx`), placed on the committed beat
//! grid never late, faded by the evidence of the beat they belong to, panned by the camera and
//! scheduled on the output's timeline when they will be heard. The sounds come from the
//! simulation's speculation ahead of the time heard, and are brought in line with it each frame:
//! what a rollback undid is cancelled before it starts.

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
mod budget;
mod chain;
pub mod check;
use budget::{Budget, Scheduled};

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
    gem: Option<usize>,
    note: Option<f64>,
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
            gem: None,
            note: None,
        }
    }
}

/// Where the run is: its seed and start, the stream's beats and gate, the camera, and the clock
/// mapping song time to when it is heard.
pub struct At<'a> {
    pub seed: f64,
    pub final_before: f64,
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
    /// Host time now, and how far ahead of it the output is rendering (s).
    pub now: f64,
    pub ahead: f64,
}

/// A presentation cue at a sim-owned song time, with a stable screen identity.
pub struct Cue {
    pub key: &'static str,
    pub s: f64,
    pub id: u32,
    pub gain: f64,
}

/// The palette, its samples in the bank, and the kill sounds waiting on their slots.
#[derive(Clone)]
pub struct Sounds {
    palette: SoundPalette,
    survivors: bool,
    lengths: Vec<f64>,
    budget: Budget,
    chain: chain::Chain,
    /// Each sample id's bank index and onset (s).
    samples: HashMap<String, (usize, f64)>,
    weapons: HashMap<String, (String, bool)>,
    /// The voices scheduled, by identity: their id and when they start (host s).
    scheduled: HashMap<VoiceKey, Scheduled>,
    next_id: u64,
}

/// A voice's identity, so each frame's speculation can be compared with what is scheduled: what it
/// plays, the time of the cause it answers (0.1 ms), where (quarter pixels) and how many kills share
/// it. (Not the slot it was placed on: a beat arriving moves the slots past the last committed one,
/// and a started voice must not be scheduled again for it.)
#[derive(Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
struct VoiceKey {
    key: String,
    s: i64,
    x: i64,
    n: u32,
    gem: Option<usize>,
}

/// A sound placed: its palette key, the song time it plays at and its cause's, its place, level
/// and duck.
struct Placed {
    key: String,
    s: f64,
    cause_s: f64,
    x: f64,
    gain: f64,
    duck: bool,
    gem: Option<usize>,
    note: Option<f64>,
}

impl Placed {
    fn id(&self, n: u32) -> VoiceKey {
        VoiceKey {
            key: self.key.clone(),
            s: (self.cause_s * 1e4).round() as i64,
            x: if self.x.is_finite() {
                (self.x * 4.0).round() as i64
            } else {
                0
            },
            n,
            gem: self.gem,
        }
    }
}

impl Sounds {
    /// Decodes the palette's samples from `dir` at `rate`: the sounds and the bank for the output.
    pub fn load(
        dir: &Path,
        rate: f64,
        lib: &ContentLibrary,
        survivors: bool,
    ) -> Result<(Sounds, Bank), String> {
        let text = std::fs::read_to_string(dir.join("sounds.json"))
            .map_err(|e| format!("{}: {e}", dir.display()))?;
        let catalogue: Catalogue = serde_json::from_str(&text).map_err(|e| e.to_string())?;
        let id = if survivors { "survivors" } else { PALETTE };
        let palette = lib
            .sound_palettes
            .get(id)
            .ok_or_else(|| format!("no palette {id}"))?
            .clone();
        if survivors
            && palette.sounds.get("chime").is_none_or(|samples| {
                samples.is_empty()
                    || samples.iter().any(
                        |s| !matches!(s,Sample::Detailed{root_note:Some(note),..} if note=="G6"),
                    )
            })
        {
            return Err(
                "survivors chime requires a measured G6 sample for the C6-C8 gem chain".into(),
            );
        }
        let mut bank: Bank = Vec::new();
        let mut samples = HashMap::new();
        let mut lengths = Vec::new();
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
                let pcm = decode(&dir.join(&entry.file), rate)?;
                lengths.push(pcm.len() as f64 / rate);
                bank.push(pcm.into());
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
                survivors,
                lengths,
                budget: Budget::default(),
                chain: chain::Chain::default(),
                samples,
                weapons,
                scheduled: HashMap::new(),
                next_id: 1 << 40,
            },
            bank,
        ))
    }

    /// Forgets what is scheduled (a new run: the output cancels it).
    pub fn reset(&mut self, now: f64) {
        // CancelAll stops pending voices only. Keep already audible tails in the next
        // run's budget, under identities that cannot suppress new causes.
        self.scheduled = self
            .scheduled
            .drain()
            .filter_map(|(mut key, mut v)| {
                if v.start <= now && v.end > now {
                    key.key = format!("tail:{}", key.key);
                    v.reconcile = false;
                    Some((key, v))
                } else {
                    None
                }
            })
            .collect();
        self.budget = Budget::default();
        self.chain = chain::Chain::default();
    }

    /// A sound now: `key` centred, as soon as the output can play it (host `at`).
    pub fn now(&mut self, key: &str, at: f64, gain: f64, out: &mut Vec<Command>) {
        let p = Placed {
            key: key.into(),
            s: at,
            cause_s: at,
            x: f64::NAN,
            gain,
            duck: false,
            gem: None,
            note: None,
        };
        let Some((sound, _, rate, sample_gain)) = self.pick(key, at, 0.0, 0.0) else {
            return;
        };
        self.next_id += 1;
        let v = Voice {
            id: self.next_id,
            source: Source::Sample(sound),
            at,
            from: 0.0,
            lead: 0.0,
            gain: (gain * sample_gain * self.palette.gain) as f32,
            pan: 0.0,
            rate,
            duck: false,
        };
        self.scheduled
            .retain(|_, v| v.end > at || v.start > at - 2.0);
        let k = p.id(self.next_id as u32);
        let admitted = if self.survivors {
            self.budget
                .admit(&k, &v, self.lengths[sound], &self.scheduled, at)
        } else {
            Some(Scheduled {
                id: v.id,
                start: v.at,
                end: v.at + self.lengths[sound] / f64::from(v.rate),
                category: budget::Category::of(key),
                reconcile: false,
                note: None,
            })
        };
        if let Some(mut slot) = admitted {
            slot.reconcile = false;
            self.scheduled.insert(k, slot);
            out.push(Command::Play(v));
        }
    }

    pub fn log_budget(&self) {
        if self.survivors {
            eprintln!(
                "slice: sound budgets [weapons,hits,kills,pickups,ui] caps [4,6,2,3,3], peak {:?}, denied {:?}",
                self.budget.peaks, self.budget.dropped
            );
        }
    }

    /// Brings the scheduled voices in line with `caused`, the speculation's sounds now
    /// (`LiveSoundScheduler`): a voice it no longer has is cancelled if it hasn't started, and a new
    /// one is scheduled. Kills sharing a slot are one sound, the multi-kill from two on.
    pub fn reconcile(&mut self, caused: &[Caused], at: &At, out: &mut Vec<Command>) {
        self.reconcile_with_cues(caused, &[], at, out);
    }

    pub fn reconcile_with_cues(
        &mut self,
        caused: &[Caused],
        cues: &[Cue],
        at: &At,
        out: &mut Vec<Command>,
    ) {
        let notes = self.chain.notes(caused, at.final_before);
        let mut desired: HashMap<VoiceKey, Placed> = HashMap::new();
        let mut kills: Vec<(Placed, u32)> = Vec::new();
        for c in caused {
            for mut call in self.calls(&c.cause) {
                if let Some(gem) = call.gem {
                    call.note = notes
                        .iter()
                        .find(|(id, s, _)| *id == gem && (*s - call.s).abs() < 1e-9)
                        .map(|n| n.2);
                }
                let Some(p) = place(&call, c.known, at) else {
                    continue;
                };
                if p.key == "kill" {
                    match kills.iter_mut().find(|(k, _)| (k.s - p.s).abs() < 1e-6) {
                        Some((k, n)) => {
                            *n += 1;
                            k.cause_s = k.cause_s.min(p.cause_s);
                        }
                        None => kills.push((p, 1)),
                    }
                } else {
                    desired.insert(p.id(1), p);
                }
            }
        }
        for c in cues {
            let p = Placed {
                key: c.key.into(),
                s: c.s,
                cause_s: c.s,
                x: f64::NAN,
                gain: c.gain,
                duck: false,
                gem: None,
                note: None,
            };
            desired.insert(p.id(c.id), p);
        }
        for (mut p, n) in kills {
            if n > 1 {
                // (The kill's level is 0.6 of its fade; the multi-kill's grows with the count.)
                p.gain *= (0.5 + 0.22 * f64::from(n).log2()) / 0.6;
                p.key = "kill-multi".into();
                // A dense batch is one centred transient, independent of journal iteration order.
                if self.survivors {
                    p.x = f64::NAN;
                }
            }
            desired.insert(p.id(n), p);
        }
        // (The output may be rendering up to `ahead` past now: a voice due by then has started.)
        let started = at.now + at.ahead;
        self.scheduled.retain(|k, v| {
            let (id, start) = (v.id, v.start);
            if !v.reconcile {
                return v.end > at.now || start > at.now - 2.0;
            }
            if desired
                .get(k)
                .is_some_and(|p| p.note == v.note || start <= started)
            {
                true
            } else if start > started {
                out.push(Command::Cancel(id));
                false
            } else {
                v.end > at.now || start > at.now - 2.0
            }
        });
        let mut desired: Vec<_> = desired.into_iter().collect();
        desired.sort_by(|(ak, a), (bk, b)| a.s.total_cmp(&b.s).then(ak.cmp(bk)));
        for (k, p) in desired {
            if self.scheduled.contains_key(&k) {
                continue;
            }
            if let Some(mut v) = self.voice(&p, at) {
                // (Never a voice whose onset has passed: a cause re-identified after it played (a
                // kill joining or leaving its slot's group) would sound again, late.)
                if v.at + v.lead < at.now + at.ahead {
                    continue;
                }
                self.next_id += 1;
                v.id = self.next_id;
                let Source::Sample(sound) = v.source else {
                    continue;
                };
                let slot = if self.survivors {
                    self.budget
                        .admit(&k, &v, self.lengths[sound], &self.scheduled, at.now)
                } else {
                    Some(Scheduled {
                        id: v.id,
                        start: v.at,
                        end: v.at + self.lengths[sound] / f64::from(v.rate),
                        category: budget::Category::of(&p.key),
                        reconcile: true,
                        note: None,
                    })
                };
                if let Some(mut slot) = slot {
                    slot.note = p.note;
                    self.scheduled.insert(k, slot);
                    out.push(Command::Play(v));
                }
            }
        }
    }

    /// What a cause plays (`emit.ts`'s sound calls, their levels as it sets them).
    fn calls(&self, cause: &Cause) -> Vec<Call> {
        match cause {
            Cause::Swing { s, x, kind } => {
                let heavy = *kind == "heavy";
                if self.survivors {
                    let mut c = Call::new(
                        if heavy {
                            "sword-accent-fire"
                        } else {
                            "sword-fire"
                        },
                        *s,
                        *x,
                        if heavy { 0.85 } else { 0.65 },
                    );
                    c.owner = Some(*s);
                    return vec![c];
                }
                let mut whoosh = Call::new("swing", s - 0.04, *x, 0.7);
                whoosh.owner = Some(*s);
                let mut strike = Call::new(
                    if self.survivors {
                        if heavy {
                            "hit-heavy".into()
                        } else {
                            "hit-light".into()
                        }
                    } else {
                        format!("hit-{kind}")
                    },
                    *s,
                    *x,
                    if heavy { 1.1 } else { 0.95 },
                );
                strike.duck = heavy && !self.survivors;
                vec![whoosh, strike]
            }
            Cause::Kill { s, x } => {
                let mut c = Call::new("kill", *s, *x, 0.6);
                c.every = Some(Every::Sixteenth);
                vec![c]
            }
            Cause::Weapon { s, x, what, caught } => {
                if self.survivors {
                    let key = format!("{what}-{}", if *caught { "hit" } else { "fire" });
                    let mut c = Call::new(key, *s, *x, 0.5);
                    c.every = Some(Every::Sixteenth);
                    return vec![c];
                }
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
            Cause::WeaponPulse { s, x, what, accent } => {
                let key = format!(
                    "{}-{}fire",
                    what.as_str(),
                    if *accent { "accent-" } else { "" }
                );
                let mut c = Call::new(key, *s, *x, if *accent { 0.7 } else { 0.5 });
                c.owner = Some(*s);
                vec![c]
            }
            Cause::WeaponImpact { s, what, accent } => {
                let key = format!(
                    "{}-{}hit",
                    what.as_str(),
                    if *accent { "accent-" } else { "" }
                );
                let mut c = Call::new(key, *s, f64::NAN, if *accent { 0.8 } else { 0.55 });
                c.owner = Some(*s);
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
            Cause::Step { s, x } => {
                if self.survivors {
                    Vec::new()
                } else {
                    vec![Call::new("step", *s, *x, 0.25)]
                }
            }
            Cause::Chicken { s, .. } => {
                let mut c = Call::new("chicken", *s, f64::NAN, 0.65);
                c.owner = Some(*s);
                vec![c]
            }
            Cause::Gem { s, gem } => {
                let mut c = Call::new("chime", *s, f64::NAN, 0.55);
                c.every = Some(Every::Sixteenth);
                c.gem = Some(*gem);
                vec![c]
            }
            Cause::LevelUp { s } => {
                let mut c = Call::new("level-up", *s, f64::NAN, 1.0);
                c.duck = !self.survivors;
                c.every = Some(Every::Beat);
                vec![c]
            }
        }
    }

    /// A sample of `key` for a sound at song time `s` and place `x`: its bank index, onset and
    /// playback rate (a semitone either way).
    fn pick(&self, key: &str, s: f64, x: f64, seed: f64) -> Option<(usize, f64, f32, f64)> {
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
        let gain = match sample {
            Sample::Detailed { gain, .. } => gain.unwrap_or(1.0),
            Sample::Name(_) => 1.0,
        };
        Some((sound, onset, rate as f32, gain))
    }

    /// The voice that plays `p` (its id unset).
    fn voice(&self, p: &Placed, at: &At) -> Option<Voice> {
        let (sound, onset, mut rate, sample_gain) = self.pick(&p.key, p.s, p.x, at.seed)?;
        if let Some(note) = p.note {
            // The palette's chime is the existing measured G6 glass sample (MIDI 91).
            rate = 2f64.powf((note - 91.0) / 12.0) as f32;
        }
        let (cx, _, zoom) = at.camera;
        let pan = if p.x.is_finite() {
            ((p.x - cx) / (480.0 / zoom.max(1e-3))).clamp(-0.8, 0.8)
        } else {
            0.0
        };
        let lead = (onset / f64::from(rate)).min(MAX_LEAD);
        // Heard when the song reaches it; the output renders it `l_out` before, from `lead` before
        // its onset.
        let heard = at.clock.1 + (at.run_start + p.s - at.clock.0);
        Some(Voice {
            id: 0,
            source: Source::Sample(sound),
            at: heard - at.l_out - lead,
            from: 0.0,
            lead,
            gain: (p.gain * sample_gain * self.palette.gain) as f32,
            pan: pan as f32,
            rate,
            duck: p.duck,
        })
    }
}

/// Where and how loud a call plays, known at song time `known`: on its slot, or the first one far
/// enough after it was known (never late), faded by its beat's evidence; nothing when silent.
fn place(call: &Call, known: f64, at: &At) -> Option<Placed> {
    let s = match call.every {
        Some(every) => {
            let slot = slot_at(at.beats, every, call.s);
            if slot - known >= at.never_late - 1e-9 {
                slot
            } else {
                slot_at(at.beats, every, known + at.never_late)
            }
        }
        // (A strike known too late for its time, as after a stall, plays as soon as the output
        // can rather than not at all; a whoosh before it is dropped.)
        None if call.owner.is_none() => call.s.max(known + at.never_late),
        None if call.s - known < at.never_late => return None,
        None => call.s,
    };
    let fade = if EXEMPT.contains(&call.key.as_str()) {
        1.0
    } else {
        evidence_fade(at.beats, at.gate, call.owner.unwrap_or(call.s))
    };
    (fade > 0.0).then(|| Placed {
        key: call.key.clone(),
        s,
        cause_s: call.s,
        x: call.x,
        gain: call.gain * fade,
        duck: call.duck,
        gem: call.gem,
        note: call.note,
    })
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

    pub(super) fn beats(n: usize, period: f64, evidence: f64) -> Vec<Beat> {
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
    fn sounds_follow_the_speculation_and_kills_share_their_slot() {
        let lib = super::super::World::load(None).unwrap().lib;
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
            survivors: false,
            lengths: vec![0.3],
            budget: Budget::default(),
            chain: chain::Chain::default(),
            samples,
            weapons: HashMap::new(),
            scheduled: HashMap::new(),
            next_id: 0,
        };
        let b = beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.01,
        };
        let kill = |s: f64, known: f64| Caused {
            cause: Cause::Kill { s, x: 0.0 },
            known,
        };
        let mut out = Vec::new();
        // At 1.2, known at once: its sixteenth is 1.25, 50 ms on, under NEVER_LATE: it moves to 1.375.
        let first = [kill(1.2, 1.2)];
        sounds.reconcile(&first, &at, &mut out);
        let Command::Play(v) = out[0] else { panic!() };
        assert!((v.at - (100.0 + 1.375 - 0.02)).abs() < 1e-9, "{}", v.at);
        // The same speculation again: nothing to do.
        out.clear();
        sounds.reconcile(&first, &at, &mut out);
        assert!(out.is_empty(), "{out:?}");
        // A second kill on that slot: the kill gives way to a multi-kill there.
        let both = [kill(1.2, 1.2), kill(1.3, 1.3)];
        sounds.reconcile(&both, &at, &mut out);
        assert_eq!(out[0], Command::Cancel(v.id));
        let Command::Play(m) = out[1] else { panic!() };
        assert!((m.at - v.at).abs() < 1e-9 && m.id != v.id);
        // A rollback undoes both kills: the multi-kill, not yet started, is cancelled.
        out.clear();
        sounds.reconcile(&[], &at, &mut out);
        assert_eq!(out, [Command::Cancel(m.id)]);
        // A whoosh belongs to its strike's beat: below the gate there, both are silent.
        out.clear();
        let quiet = beats(8, 0.5, 0.1);
        let at = At {
            beats: &quiet,
            ..at
        };
        let swing = Caused {
            cause: Cause::Swing {
                s: 2.0,
                x: 0.0,
                kind: "light",
            },
            known: 1.5,
        };
        sounds.reconcile(&[swing], &at, &mut out);
        assert!(out.is_empty());
    }

    #[test]
    fn a_kill_group_changing_after_its_voice_played_sounds_nothing_more() {
        let lib = super::super::World::load(None).unwrap().lib;
        let palette = lib.sound_palettes[PALETTE].clone();
        let samples = palette
            .sounds
            .values()
            .flatten()
            .map(|s| (sample_id(s).to_string(), (0, 0.0)))
            .collect();
        let mut sounds = Sounds {
            palette,
            survivors: false,
            lengths: vec![0.3],
            budget: Budget::default(),
            chain: chain::Chain::default(),
            samples,
            weapons: HashMap::new(),
            scheduled: HashMap::new(),
            next_id: 0,
        };
        let b = beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.01,
        };
        let kill = |s: f64| Caused {
            cause: Cause::Kill { s, x: 0.0 },
            known: 1.0,
        };
        let mut out = Vec::new();
        sounds.reconcile(&[kill(1.3), kill(1.32)], &at, &mut out);
        assert!(matches!(out[..], [Command::Play(_)]), "{out:?}");
        // A second after the slot (1.375) the first kill leaves the speculation's window: the
        // group's count changes, but its voice has played.
        out.clear();
        let later = At {
            now: 100.0 + 2.4,
            ..at
        };
        sounds.reconcile(&[kill(1.32)], &later, &mut out);
        assert!(out.is_empty(), "sounded again: {out:?}");
    }

    #[test]
    fn a_started_voice_stays_when_a_new_beat_moves_its_slot() {
        let lib = super::super::World::load(None).unwrap().lib;
        let palette = lib.sound_palettes[PALETTE].clone();
        let samples = palette
            .sounds
            .values()
            .flatten()
            .map(|s| (sample_id(s).to_string(), (0, 0.0)))
            .collect();
        let mut sounds = Sounds {
            palette,
            survivors: false,
            lengths: vec![0.3],
            budget: Budget::default(),
            chain: chain::Chain::default(),
            samples,
            weapons: HashMap::new(),
            scheduled: HashMap::new(),
            next_id: 0,
        };
        // Beats to 2.5 s; a kill at 2.6 lands on an extrapolated sixteenth (2.625).
        let early = beats(4, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &early,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.01,
        };
        let kill = [Caused {
            cause: Cause::Kill { s: 2.6, x: 0.0 },
            known: 2.4,
        }];
        let mut out = Vec::new();
        sounds.reconcile(&kill, &at, &mut out);
        assert_eq!(out.len(), 1);
        // The next beat commits 20 ms early, moving the slot; the voice has started by then.
        let mut later = early.clone();
        later.push(Beat {
            t: 2.98,
            known_at: 2.6,
            period: 0.48,
            evidence: 1.0,
        });
        let at = At {
            beats: &later,
            now: 100.0 + 2.62,
            ..at
        };
        out.clear();
        sounds.reconcile(&kill, &at, &mut out);
        assert!(out.is_empty(), "scheduled again: {out:?}");
    }
}

#[cfg(test)]
mod survivor_tests {
    use super::*;
    fn decoded() -> (Sounds, Bank) {
        static PALETTE: std::sync::OnceLock<(Sounds, Bank)> = std::sync::OnceLock::new();
        PALETTE
            .get_or_init(|| {
                let lib = crate::slice::World::load(None).unwrap().lib;
                let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets/sfx");
                Sounds::load(&dir, 8000.0, &lib, true).unwrap()
            })
            .clone()
    }
    #[test]
    fn sword_fire_keeps_its_actual_subdivision_instead_of_snapping_again() {
        let (mut sounds, _) = decoded();
        let b = tests::beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.0,
        };
        let cause = Caused {
            cause: Cause::Swing {
                s: 1.0 + 1.0 / 6.0,
                x: 0.0,
                kind: "spin",
            },
            known: 0.5,
        };
        let mut out = Vec::new();
        sounds.reconcile(&[cause], &at, &mut out);
        assert_eq!(
            out.len(),
            1,
            "fire only; impacts get their own actual reaction cause"
        );
        let Command::Play(v) = out[0] else { panic!() };
        assert!((v.at + v.lead + at.l_out - (101.0 + 1.0 / 6.0)).abs() < 1e-6);
    }
    #[test]
    fn weapon_pulse_and_contact_cues_use_real_palette_and_cancel_on_retraction() {
        for what in [
            sim::survivors::WeaponId::Garlic,
            sim::survivors::WeaponId::MagicWand,
            sim::survivors::WeaponId::KingBible,
            sim::survivors::WeaponId::LightningRing,
        ] {
            let (mut sounds, _) = decoded();
            let b = tests::beats(8, 0.5, 1.0);
            let at = At {
                seed: 1.0,
                final_before: -1.0,
                run_start: 0.0,
                beats: &b,
                gate: 0.2,
                camera: (0.0, 0.0, 1.0),
                clock: (0.0, 100.0),
                l_out: 0.02,
                never_late: NEVER_LATE,
                now: 100.0,
                ahead: 0.0,
            };
            let mut causes = Vec::new();
            for accent in [false, true] {
                let s = if accent { 2.0 } else { 1.0 }
                    + if what == sim::survivors::WeaponId::MagicWand {
                        0.1
                    } else {
                        0.0
                    };
                causes.push(Caused {
                    cause: Cause::WeaponPulse {
                        s,
                        x: 0.0,
                        what,
                        accent,
                    },
                    known: 0.5,
                });
                causes.push(Caused {
                    cause: Cause::WeaponImpact {
                        s: s + 0.125,
                        what,
                        accent,
                    },
                    known: 0.5,
                });
            }
            let mut out = Vec::new();
            sounds.reconcile(&causes, &at, &mut out);
            assert_eq!(
                out.len(),
                4,
                "normal/accent fire and actual impact clips decode"
            );
            let offset = if what == sim::survivors::WeaponId::MagicWand {
                0.1
            } else {
                0.0
            };
            for (command, s) in out.iter().zip([1.0, 1.125, 2.0, 2.125].map(|s| s + offset)) {
                let Command::Play(v) = command else { panic!() };
                assert!((v.at + v.lead + at.l_out - (100.0 + s)).abs() < 1e-6);
                assert!(!v.duck);
            }
            out.clear();
            sounds.reconcile(&[], &at, &mut out);
            assert_eq!(out.len(), 4);
            assert!(out.iter().all(|c| matches!(c, Command::Cancel(_))));
        }
    }
    #[test]
    fn screen_cues_land_on_their_onset_and_retract_before_start() {
        let (mut sounds, _) = decoded();
        let b = tests::beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.02,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.0,
        };
        let cue = Cue {
            key: "card-reveal",
            s: 1.0,
            id: 123,
            gain: 0.7,
        };
        let mut out = Vec::new();
        sounds.reconcile_with_cues(&[], &[cue], &at, &mut out);
        let Command::Play(v) = out[0] else {
            panic!("no reveal sound")
        };
        assert!((v.at + v.lead + at.l_out - 101.0).abs() < 1e-6);
        assert!(!v.duck);
        out.clear();
        sounds.reconcile_with_cues(&[], &[], &at, &mut out);
        assert_eq!(out, vec![Command::Cancel(v.id)]);
    }
    #[test]
    fn a_restart_keeps_audible_tails_in_the_budget() {
        let (mut sounds, _) = decoded();
        let mut out = Vec::new();
        sounds.now("level-up", 100.0, 1.0, &mut out);
        sounds.now("level-up", 101.0, 1.0, &mut out);
        sounds.reset(100.01);
        assert_eq!(
            sounds.scheduled.len(),
            1,
            "only the already audible tail survives CancelAll"
        );
        let (key, tail) = sounds.scheduled.iter().next().unwrap();
        assert!(key.key.starts_with("tail:"));
        assert!(!tail.reconcile);
        sounds.reset(1000.0);
        assert!(sounds.scheduled.is_empty());
    }
    #[test]
    fn rollback_repitches_only_a_gem_that_has_not_started() {
        let (mut sounds, _) = decoded();
        let b = tests::beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.0,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.0,
        };
        let gem = |s, gem| Caused {
            cause: Cause::Gem { s, gem },
            known: 0.0,
        };
        let mut out = Vec::new();
        sounds.reconcile(&[gem(1.0, 0), gem(1.1, 1)], &at, &mut out);
        let original = sounds
            .scheduled
            .iter()
            .find(|(k, _)| k.gem == Some(1))
            .unwrap()
            .1
            .id;
        out.clear();
        sounds.reconcile(&[gem(1.1, 1)], &at, &mut out);
        assert!(out.contains(&Command::Cancel(original)));
        let Command::Play(v) = out.iter().find(|c| matches!(c, Command::Play(_))).unwrap() else {
            unreachable!()
        };
        assert!(
            (v.rate - 2f32.powf(-7.0 / 12.0)).abs() < 1e-6,
            "the remaining gem returns to C6"
        );
        let played_id = v.id;
        let started = At { now: 101.2, ..at };
        out.clear();
        sounds.reconcile(&[gem(1.0, 0), gem(1.1, 1)], &started, &mut out);
        assert!(
            !out.iter()
                .any(|c| matches!(c,Command::Cancel(id) if *id==played_id)),
            "a started note remains untouched"
        );
    }
    #[test]
    fn actual_palette_decodes_and_dense_causes_obey_all_caps() {
        let (mut sounds, bank) = decoded();
        assert!(!bank.is_empty());
        let b = tests::beats(8, 0.5, 1.0);
        let at = At {
            seed: 1.0,
            final_before: -1.0,
            run_start: 0.0,
            beats: &b,
            gate: 0.2,
            camera: (0.0, 0.0, 1.0),
            clock: (0.0, 100.0),
            l_out: 0.0,
            never_late: NEVER_LATE,
            now: 100.0,
            ahead: 0.0,
        };
        let mut causes = Vec::new();
        for i in 0..200 {
            causes.push(Caused {
                cause: Cause::Swing {
                    s: 1.0,
                    x: i as f64,
                    kind: "heavy",
                },
                known: 0.0,
            });
            causes.push(Caused {
                cause: Cause::WeaponImpact {
                    s: 1.0 + i as f64 * 0.01,
                    what: sim::survivors::WeaponId::Sword,
                    accent: true,
                },
                known: 0.0,
            });
            causes.push(Caused {
                cause: Cause::Kill {
                    s: 1.0,
                    x: i as f64,
                },
                known: 0.0,
            });
            causes.push(Caused {
                cause: Cause::Gem { s: 1.0, gem: i },
                known: 0.0,
            });
            causes.push(Caused {
                cause: Cause::LevelUp {
                    s: 1.0 + i as f64 * 0.001,
                },
                known: 0.0,
            });
        }
        let mut out = Vec::new();
        sounds.reconcile(&causes, &at, &mut out);
        assert_eq!(sounds.budget.peaks, [4, 6, 1, 3, 3]);
        assert!(sounds.budget.dropped.iter().sum::<u64>() > 500);
        assert!(
            out.iter()
                .all(|c| !matches!(c, Command::Play(Voice { duck: true, .. })))
        );
        // Same speculative set in another input order chooses the same admitted voices.
        let admitted = out.len();
        let first = out.clone();
        sounds.reset(0.0);
        causes.reverse();
        out.clear();
        sounds.reconcile(&causes, &at, &mut out);
        let projected = |cmds: Vec<Command>| {
            cmds.into_iter()
                .filter_map(|c| match c {
                    Command::Play(v) => Some((v.source, v.at, v.rate, v.pan)),
                    _ => None,
                })
                .collect::<Vec<_>>()
        };
        assert_eq!(projected(first), projected(out.clone()));
        // Rollback removes all future voices before they start.
        out.clear();
        sounds.reconcile(&[], &at, &mut out);
        assert_eq!(out.len(), admitted);
        assert!(out.iter().all(|c| matches!(c, Command::Cancel(_))));
    }
}
