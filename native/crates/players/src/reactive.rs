//! The players for reactive tracks (`src/reactive/players.ts`): they press against the track's
//! reference beats, never against committed beats. On a track whose reference has free sections
//! (the Galop's prelude) they press only in its pulsed sections and roam elsewhere.

use serde_json::Value;
use sim::player::Player;

use crate::others::{HolderPlayer, MasherPlayer};
use crate::virtual_player::{PlayerSection, ProfileSpec, Slot, VirtualPlayer, profile};

/// A section of a track's reference.
#[derive(Clone, Debug, PartialEq)]
pub struct ReferenceSection {
    pub s0: f64,
    pub s1: f64,
    pub free: bool,
}

/// A track's reference: its beats as a person hears them, and its sections.
#[derive(Clone, Debug, PartialEq)]
pub struct Reference {
    pub beats: Vec<f64>,
    pub sections: Vec<ReferenceSection>,
}

impl Reference {
    /// From a stream file's `reference` (`stream::StreamFile::split`).
    pub fn from_json(v: &Value) -> Option<Reference> {
        let beats = v
            .get("beats")?
            .as_array()?
            .iter()
            .filter_map(Value::as_f64)
            .collect();
        let sections = v
            .get("sections")
            .and_then(Value::as_array)
            .map(|xs| {
                xs.iter()
                    .filter_map(|s| {
                        Some(ReferenceSection {
                            s0: s.get("s0")?.as_f64()?,
                            s1: s.get("s1")?.as_f64()?,
                            free: s.get("feel").and_then(Value::as_str) == Some("free"),
                        })
                    })
                    .collect()
            })
            .unwrap_or_default();
        Some(Reference { beats, sections })
    }
}

/// A reference player's profile and pattern.
struct Spec {
    profile: ProfileSpec,
    presses: &'static str,
    offbeat: bool,
}

fn spec(name: &str) -> Option<Spec> {
    let p = |n| profile(n).expect("a profile");
    Some(match name {
        "reactive-moderate" => Spec {
            profile: p("moderate"),
            presses: "x",
            offbeat: false,
        },
        "reactive-human" => Spec {
            profile: p("human"),
            presses: "x",
            offbeat: false,
        },
        "reactive-half" => Spec {
            profile: p("moderate"),
            presses: "x.",
            offbeat: false,
        },
        "reactive-offbeat" => Spec {
            profile: p("moderate"),
            presses: "x",
            offbeat: true,
        },
        // The reactive demo's player: every beat, a 20 ms spread.
        "reactive-skilled" => Spec {
            profile: ProfileSpec {
                sd: 0.02,
                clamp_s: 0.06,
                ..p("skilled")
            },
            presses: "x",
            offbeat: false,
        },
        _ => return None,
    })
}

/// The reference's sections as the players know them: pulsed ones press, free ones roam. (Live, the
/// last runs on with no end.)
fn reference_sections(r: &Reference, presses: &str, pace: f64) -> Vec<PlayerSection> {
    let all = [ReferenceSection {
        s0: 0.0,
        s1: f64::INFINITY,
        free: false,
    }];
    let secs = if r.sections.is_empty() {
        &all[..]
    } else {
        &r.sections[..]
    };
    let n = secs.len();
    secs.iter()
        .enumerate()
        .map(|(i, s)| PlayerSection {
            steps: !s.free,
            presses: presses.to_string(),
            s0: if i == 0 { 0.0 } else { s.s0 },
            s1: if i == n - 1 { f64::INFINITY } else { s.s1 },
            pace,
        })
        .collect()
}

/// Slots on the reference beats (or their off-beats) of the pulsed sections.
fn reference_slots(r: &Reference, sections: &[PlayerSection], offbeat: bool) -> Vec<Slot> {
    let b = &r.beats;
    let section_at = |t: f64| sections.iter().rposition(|x| t >= x.s0 - 1e-9).unwrap_or(0);
    let mut out = Vec::new();
    for i in 0..b.len() {
        let s = if offbeat {
            match b.get(i + 1) {
                Some(&n) => f64::midpoint(b[i], n),
                None => continue,
            }
        } else {
            b[i]
        };
        let section = section_at(s);
        if !sections[section].steps {
            continue;
        }
        out.push(Slot {
            s,
            bi: i as f64 + if offbeat { 0.5 } else { 0.0 },
            slot: i % 2,
            section,
        });
    }
    out
}

fn reference_player(
    step: (f64, f64),
    seed: f64,
    r: &Reference,
    s: &Spec,
    pace: f64,
) -> (VirtualPlayer, Vec<PlayerSection>) {
    let sections = reference_sections(r, s.presses, pace);
    let slots = reference_slots(r, &sections, s.offbeat);
    (
        VirtualPlayer::new(
            step.0,
            step.1,
            seed,
            &slots,
            sections.clone(),
            r.beats.clone(),
            s.profile,
        ),
        sections,
    )
}

/// The player a name stands for on a reactive track (`reactivePlayerFor`): a reference player, the
/// holder, or a masher (mash2, mash4, mash8). `step` is the hero's step (length px, duration s),
/// `pace` the level's roam pace.
pub fn reactive_player(
    step: (f64, f64),
    seed: f64,
    r: &Reference,
    pace: f64,
    name: &str,
) -> Option<Box<dyn Player>> {
    if let Some(s) = spec(name) {
        return Some(Box::new(reference_player(step, seed, r, &s, pace).0));
    }
    let (steer, sections) = reference_player(step, seed, r, &spec("reactive-moderate")?, pace);
    if let Some(rate) = name
        .strip_prefix("mash")
        .and_then(|n| n.parse::<f64>().ok())
    {
        return Some(Box::new(MasherPlayer::new(steer, sections, seed, rate)));
    }
    if name == "holder" {
        // (Live, the last bar runs to the end of the reference.)
        let bars: Vec<f64> = r.beats.iter().step_by(2).copied().collect();
        return Some(Box::new(HolderPlayer::new(steer, sections, seed, &bars)));
    }
    None
}
