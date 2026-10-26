//! A listening session's stream (docs/design/native-app.md, "Session recording (D3)"): every entry
//! as it arrived, in song time from the session's own start, `known_at` stamped with its arrival
//! (never earlier than the analyzer's own), one JSON line each in `stream.jsonl`; and the rule
//! that rebases them to a run's start `s0` ("Runs on live audio (D1)"): what the sim would have seen
//! had the song begun at `s0`, the tracker already warm. Every time is shifted by `-s0`; entries for
//! times before `s0` are dropped, those for later times but known before it are known at run time
//! 0; hindsight known from `s0` on is kept with its beat lists clipped to `s0`, and timbre known
//! from `s0` on as it is. A committed beat that arrives after its own time is dropped: the sim
//! can't act on a beat in its past (the analyzer can commit late while it starts up). The app and
//! `ps replay` share it, so a replay feeds the run what it saw live.

use serde::{Deserialize, Serialize};
use stream::{Beat, Change, End, Forecast, Hindsight, Jump, Onset, StreamLog, Tick, Timbre};

/// An entry of the stream, of any group.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "group", content = "entry", rename_all = "lowercase")]
pub enum Entry {
    Beat(Beat),
    Tick(Tick),
    Onset(Onset),
    Hindsight(Hindsight),
    Timbre(Timbre),
    Change(Change),
    Jump(Jump),
    End(End),
    Forecast(Forecast),
}

impl Entry {
    /// When it was known (its arrival stamp).
    pub fn known_at(&self) -> f64 {
        match self {
            Entry::Beat(e) => e.known_at,
            Entry::Tick(e) => e.known_at,
            Entry::Onset(e) => e.known_at,
            Entry::Hindsight(e) => e.known_at,
            Entry::Timbre(e) => e.known_at,
            Entry::Change(e) => e.known_at,
            Entry::Jump(e) => e.known_at,
            Entry::End(e) => e.known_at,
            Entry::Forecast(e) => e.known_at,
        }
    }

    /// Stamps it as arrived at song time `arrived`: never known earlier than that.
    #[must_use]
    pub fn stamped(mut self, arrived: f64) -> Entry {
        let k = match &mut self {
            Entry::Beat(e) => &mut e.known_at,
            Entry::Tick(e) => &mut e.known_at,
            Entry::Onset(e) => &mut e.known_at,
            Entry::Hindsight(e) => &mut e.known_at,
            Entry::Timbre(e) => &mut e.known_at,
            Entry::Change(e) => &mut e.known_at,
            Entry::Jump(e) => &mut e.known_at,
            Entry::End(e) => &mut e.known_at,
            Entry::Forecast(e) => &mut e.known_at,
        };
        *k = k.max(arrived);
        self
    }
}

/// An analyzer's entry as the session's stream has it (session song time, the analyzer's own
/// `known_at`; [`Entry::stamped`] adds the arrival).
pub fn from_analyzer(e: &analysis::analyzer::Entry) -> Entry {
    use analysis::analyzer::Entry as A;
    use analysis::ticks::Level;
    match e {
        A::Beat(c) => Entry::Beat(Beat {
            t: c.t,
            known_at: c.known_at,
            period: c.period,
            evidence: c.evidence,
        }),
        A::Tick(t) => Entry::Tick(Tick {
            t: t.t,
            known_at: t.t,
            evidence: t.evidence,
            energy: t.energy,
            level: Some(match t.level {
                Level::Quiet => stream::Level::Quiet,
                Level::Medium => stream::Level::Medium,
                Level::Loud => stream::Level::Loud,
            }),
            key: t.key.map(str::to_string),
            bands: Some(stream::Bands {
                kick: t.bands.kick,
                snare: t.bands.snare,
                hat: t.bands.hat,
                bass: t.bands.bass,
            }),
            brightness: Some(t.brightness),
        }),
        A::Onset(o) => Entry::Onset(Onset {
            t: o.t,
            known_at: o.known_at,
            strength: o.strength,
            strong: o.strong,
            band: match o.band {
                "kick" => Some(stream::Band::Kick),
                "snare" => Some(stream::Band::Snare),
                "hat" => Some(stream::Band::Hat),
                "bass" => Some(stream::Band::Bass),
                _ => None,
            },
        }),
        A::Hindsight(h) => Entry::Hindsight(Hindsight {
            known_at: h.known_at,
            offline: h.offline.clone(),
            online: h.online.clone(),
        }),
        A::Change(c) => Entry::Change(Change {
            t: c.t,
            known_at: c.known_at,
            strength: c.strength,
        }),
        A::Jump(j) => Entry::Jump(Jump {
            t: j.t,
            known_at: j.known_at,
            db: j.db,
        }),
        A::End(x) => Entry::End(End {
            t: x.t,
            known_at: x.known_at,
        }),
        A::Forecast(f) => Entry::Forecast(Forecast {
            t: f.t,
            known_at: f.known_at,
            cycle: f.cycle as f64,
            confidence: f.confidence,
        }),
        A::Timbre(t) => Entry::Timbre(Timbre {
            known_at: t.known_at,
            brightness: t.brightness,
            flatness: t.flatness,
            percussive: t.percussive,
            density: t.density,
        }),
    }
}

/// Appends `e` (in session time) to the log of a run that began at session time `s0`.
pub fn rebase_append(log: &mut StreamLog, e: &Entry, s0: f64) {
    let at = |t: f64| t - s0;
    let known = |k: f64| (k - s0).max(0.0);
    match e {
        // Entries about a time before the run, and summaries known before it, aren't the run's.
        Entry::Beat(b) if b.t < s0 => {}
        Entry::Tick(t) if t.t < s0 => {}
        Entry::Onset(o) if o.t < s0 => {}
        Entry::Change(c) if c.t < s0 => {}
        Entry::Jump(j) if j.t < s0 => {}
        Entry::End(x) if x.t < s0 => {}
        Entry::Forecast(f) if f.t < s0 => {}
        Entry::Hindsight(h) if h.known_at < s0 => {}
        Entry::Timbre(t) if t.known_at < s0 => {}
        // (Arrived after its own time.)
        Entry::Beat(b) if b.t < b.known_at => {}
        Entry::Beat(b) => log.beats.push(Beat {
            t: at(b.t),
            known_at: known(b.known_at),
            ..*b
        }),
        Entry::Tick(t) => log.ticks.push(Tick {
            t: at(t.t),
            known_at: known(t.known_at),
            ..t.clone()
        }),
        Entry::Onset(o) => log.onsets.push(Onset {
            t: at(o.t),
            known_at: known(o.known_at),
            ..*o
        }),
        Entry::Hindsight(h) => {
            let clip = |v: &[f64]| v.iter().filter(|&&t| t >= s0).map(|&t| at(t)).collect();
            if let Some(list) = &mut log.hindsight {
                list.push(Hindsight {
                    known_at: known(h.known_at),
                    offline: clip(&h.offline),
                    online: clip(&h.online),
                });
            }
        }
        Entry::Timbre(t) => {
            if let Some(list) = &mut log.timbre {
                list.push(Timbre {
                    known_at: known(t.known_at),
                    ..*t
                });
            }
        }
        Entry::Change(c) => {
            if let Some(list) = &mut log.changes {
                list.push(Change {
                    t: at(c.t),
                    known_at: known(c.known_at),
                    ..*c
                });
            }
        }
        Entry::Jump(j) => {
            if let Some(list) = &mut log.jumps {
                list.push(Jump {
                    t: at(j.t),
                    known_at: known(j.known_at),
                    ..*j
                });
            }
        }
        Entry::End(x) => {
            if let Some(list) = &mut log.ends {
                list.push(End {
                    t: at(x.t),
                    known_at: known(x.known_at),
                });
            }
        }
        Entry::Forecast(f) => {
            if let Some(list) = &mut log.forecasts {
                list.push(Forecast {
                    t: at(f.t),
                    known_at: known(f.known_at),
                    ..*f
                });
            }
        }
    }
}

/// One line of `stream.jsonl`: an entry and the session time it arrived at.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Arrival {
    pub arrived: f64,
    #[serde(flatten)]
    pub entry: Entry,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn beat(t: f64, known_at: f64) -> Entry {
        Entry::Beat(Beat {
            t,
            known_at,
            period: 0.5,
            evidence: 0.9,
        })
    }

    #[test]
    fn entries_are_rebased_to_the_run() {
        let mut log = StreamLog::default();
        // Arrived 30 ms after the analyzer knew it: known then.
        rebase_append(&mut log, &beat(12.5, 12.0).stamped(12.03), 10.0);
        assert_eq!(log.beats[0].t, 2.5);
        assert!((log.beats[0].known_at - 2.03).abs() < 1e-12);
        // Before the run: dropped. Committed ahead, known before the run: known at run time 0.
        rebase_append(&mut log, &beat(9.8, 9.3), 10.0);
        rebase_append(&mut log, &beat(10.4, 9.6), 10.0);
        assert_eq!(log.beats.len(), 2);
        assert!((log.beats[1].t - 0.4).abs() < 1e-12 && log.beats[1].known_at == 0.0);
        // Arrived after its own time: dropped.
        rebase_append(&mut log, &beat(13.0, 12.6).stamped(13.05), 10.0);
        assert_eq!(log.beats.len(), 2);
        // Hindsight known from the run on, its beats clipped to it.
        let mut log = StreamLog {
            hindsight: Some(Vec::new()),
            ..StreamLog::default()
        };
        let h = Entry::Hindsight(Hindsight {
            known_at: 11.0,
            offline: vec![9.5, 10.5],
            online: vec![10.2],
        });
        rebase_append(&mut log, &h, 10.0);
        let got = &log.hindsight.as_ref().unwrap()[0];
        assert_eq!(got.offline, vec![0.5]);
        assert!((got.online[0] - 0.2).abs() < 1e-12);
    }

    #[test]
    fn an_arrival_reads_back_as_written() {
        let a = Arrival {
            arrived: 3.25,
            entry: Entry::Tick(Tick {
                t: 3.2,
                known_at: 3.25,
                evidence: 0.4,
                energy: 0.1,
                level: Some(stream::Level::Loud),
                key: Some("A minor".into()),
                bands: None,
                brightness: Some(0.3),
            }),
        };
        let line = serde_json::to_string(&a).unwrap();
        assert_eq!(serde_json::from_str::<Arrival>(&line).unwrap(), a);
    }
}

// Replay is deliberately separate from the analyzer-to-stream conversion above. Analysis
// cache fingerprints cover this file, while replay-only changes preserve valid streams.
mod replay;
pub use replay::{AppliedKey, RecordedRun, Replayed, replay_run};
