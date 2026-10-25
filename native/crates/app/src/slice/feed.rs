//! Analyzer entries into the sim's stream: each converted to the stream's type, stamped with its
//! arrival as `known_at` (never earlier than the analyzer's own), and shifted so the run starts at
//! song time 0 (a restart begins a new run mid-song). A committed beat that arrives after its own
//! time is dropped: the simulation can't act on a beat in its past (the analyzer can commit late
//! while it starts up).

use analysis::analyzer::Entry;
use analysis::ticks::Level;
use stream::StreamLog;

/// Appends `e`, arrived when song time `arrived` was heard, to a run that began at `start`.
/// Entries known before the run began are skipped (a new run starts with an empty stream).
pub fn append(log: &mut StreamLog, e: &Entry, arrived: f64, start: f64) {
    let at = |t: f64| t - start;
    let stamp = |known: f64| known.max(arrived) - start;
    let known = match e {
        Entry::Beat(c) => c.known_at,
        Entry::Tick(t) => t.t,
        Entry::Onset(o) => o.known_at,
        Entry::Hindsight(h) => h.known_at,
        Entry::Change(c) => c.known_at,
        Entry::Jump(j) => j.known_at,
        Entry::End(e) => e.known_at,
        Entry::Forecast(f) => f.known_at,
        Entry::Timbre(t) => t.known_at,
    };
    if known < start {
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
                    offline: h.offline.iter().map(|&t| at(t)).collect(),
                    online: h.online.iter().map(|&t| at(t)).collect(),
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
        // Known before the run began: skipped.
        let old = Entry::Beat(Commit {
            t: 10.2,
            known_at: 9.5,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &old, 12.1, 10.0);
        assert_eq!(log.beats.len(), 1);
        // Arrived after its own time: dropped.
        let late = Entry::Beat(Commit {
            t: 13.0,
            known_at: 12.6,
            period: 0.5,
            evidence: 0.9,
        });
        append(&mut log, &late, 13.05, 10.0);
        assert_eq!(log.beats.len(), 1);
    }
}
