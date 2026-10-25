//! Analyzer entries into the sim's stream: each converted to the stream's type, stamped with its
//! arrival as `known_at` (never earlier than the analyzer's own), and rebased to the run's start
//! `s0` (docs/design/native-app.md, "Runs on live audio (D1)"): what the sim would have seen had the
//! song begun at `s0`, the tracker already warm. Every time is shifted by `-s0`; entries for times
//! before `s0` are dropped, those for later times but known before it are known at run time 0;
//! hindsight known from `s0` on is kept with its beat lists clipped to `s0`, and timbre known from
//! `s0` on as it is. A committed beat that arrives after its own time is dropped: the simulation
//! can't act on a beat in its past (the analyzer can commit late while it starts up).

use analysis::analyzer::Entry;
use analysis::ticks::Level;
use stream::StreamLog;

/// Appends `e`, arrived when song time `arrived` was heard, to a run that began at song time `s0`.
pub fn append(log: &mut StreamLog, e: &Entry, arrived: f64, s0: f64) {
    let at = |t: f64| t - s0;
    let stamp = |known: f64| (known.max(arrived) - s0).max(0.0);
    // Entries about a time before the run (the summaries, known before it) aren't the run's.
    let before = match e {
        Entry::Beat(c) => c.t < s0,
        Entry::Tick(t) => t.t < s0,
        Entry::Onset(o) => o.t < s0,
        Entry::Change(c) => c.t < s0,
        Entry::Jump(j) => j.t < s0,
        Entry::End(e) => e.t < s0,
        Entry::Forecast(f) => f.t < s0,
        Entry::Hindsight(h) => h.known_at < s0,
        Entry::Timbre(t) => t.known_at < s0,
    };
    if before {
        return;
    }
    match e {
        Entry::Beat(c) if at(c.t) < stamp(c.known_at) => {}
        Entry::Beat(c) => log.beats.push(stream::Beat {
            t: at(c.t),
            known_at: stamp(c.known_at),
            period: c.period,
            evidence: c.evidence,
        }),
        Entry::Tick(t) => log.ticks.push(stream::Tick {
            t: at(t.t),
            known_at: stamp(t.t),
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
        Entry::Onset(o) => log.onsets.push(stream::Onset {
            t: at(o.t),
            known_at: stamp(o.known_at),
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
        Entry::Hindsight(h) => {
            if let Some(list) = &mut log.hindsight {
                list.push(stream::Hindsight {
                    known_at: stamp(h.known_at),
                    offline: h
                        .offline
                        .iter()
                        .filter(|&&t| t >= s0)
                        .map(|&t| at(t))
                        .collect(),
                    online: h
                        .online
                        .iter()
                        .filter(|&&t| t >= s0)
                        .map(|&t| at(t))
                        .collect(),
                });
            }
        }
        Entry::Change(c) => {
            if let Some(list) = &mut log.changes {
                list.push(stream::Change {
                    t: at(c.t),
                    known_at: stamp(c.known_at),
                    strength: c.strength,
                });
            }
        }
        Entry::Jump(j) => {
            if let Some(list) = &mut log.jumps {
                list.push(stream::Jump {
                    t: at(j.t),
                    known_at: stamp(j.known_at),
                    db: j.db,
                });
            }
        }
        Entry::End(e) => {
            if let Some(list) = &mut log.ends {
                list.push(stream::End {
                    t: at(e.t),
                    known_at: stamp(e.known_at),
                });
            }
        }
        Entry::Forecast(f) => {
            if let Some(list) = &mut log.forecasts {
                list.push(stream::Forecast {
                    t: at(f.t),
                    known_at: stamp(f.known_at),
                    cycle: f.cycle as f64,
                    confidence: f.confidence,
                });
            }
        }
        Entry::Timbre(t) => {
            if let Some(list) = &mut log.timbre {
                list.push(stream::Timbre {
                    known_at: stamp(t.known_at),
                    brightness: t.brightness,
                    flatness: t.flatness,
                    percussive: t.percussive,
                    density: t.density,
                });
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use analysis::beats::Commit;

    #[test]
    fn entries_are_stamped_with_their_arrival_and_shifted_to_the_run() {
        let mut log = StreamLog::default();
        let beat = Entry::Beat(Commit {
            t: 12.5,
            known_at: 12.0,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &beat, 12.03, 10.0);
        assert_eq!(log.beats[0].t, 2.5);
        assert!((log.beats[0].known_at - 2.03).abs() < 1e-12);
        // Before the run began: dropped.
        let old = Entry::Beat(Commit {
            t: 9.8,
            known_at: 9.3,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &old, 9.35, 10.0);
        assert_eq!(log.beats.len(), 1);
        // Committed ahead, for a time in the run but known before it: known at run time 0.
        let ahead = Entry::Beat(Commit {
            t: 10.4,
            known_at: 9.6,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &ahead, 9.62, 10.0);
        assert_eq!(log.beats.len(), 2);
        assert!((log.beats[1].t - 0.4).abs() < 1e-12 && log.beats[1].known_at == 0.0);
        // Arrived after its own time: dropped.
        let late = Entry::Beat(Commit {
            t: 13.0,
            known_at: 12.6,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &late, 13.05, 10.0);
        assert_eq!(log.beats.len(), 2);
    }
}
