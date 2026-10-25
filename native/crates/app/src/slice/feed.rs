//! Analyzer entries as the session's stream has them (`eval::session`: stamped with their arrival
//! and rebased to each run there).

use analysis::analyzer::Entry;
use analysis::ticks::Level;
use eval::session::Entry as S;

/// `e` as the session's stream has it (session song time, the analyzer's own `known_at`; the
/// caller stamps its arrival).
pub fn convert(e: &Entry) -> S {
    match e {
        Entry::Beat(c) => S::Beat(stream::Beat {
            t: c.t,
            known_at: c.known_at,
            period: c.period,
            evidence: c.evidence,
        }),
        Entry::Tick(t) => S::Tick(stream::Tick {
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
        Entry::Onset(o) => S::Onset(stream::Onset {
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
        Entry::Hindsight(h) => S::Hindsight(stream::Hindsight {
            known_at: h.known_at,
            offline: h.offline.clone(),
            online: h.online.clone(),
        }),
        Entry::Change(c) => S::Change(stream::Change {
            t: c.t,
            known_at: c.known_at,
            strength: c.strength,
        }),
        Entry::Jump(j) => S::Jump(stream::Jump {
            t: j.t,
            known_at: j.known_at,
            db: j.db,
        }),
        Entry::End(x) => S::End(stream::End {
            t: x.t,
            known_at: x.known_at,
        }),
        Entry::Forecast(f) => S::Forecast(stream::Forecast {
            t: f.t,
            known_at: f.known_at,
            cycle: f.cycle as f64,
            confidence: f.confidence,
        }),
        Entry::Timbre(t) => S::Timbre(stream::Timbre {
            known_at: t.known_at,
            brightness: t.brightness,
            flatness: t.flatness,
            percussive: t.percussive,
            density: t.density,
        }),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use analysis::beats::Commit;

    #[test]
    fn a_beat_keeps_its_times() {
        let beat = Entry::Beat(Commit {
            t: 12.5,
            known_at: 12.0,
            period: 0.5,
            evidence: 0.9,
        });
        let S::Beat(b) = convert(&beat) else { panic!() };
        assert_eq!((b.t, b.known_at), (12.5, 12.0));
    }
}
