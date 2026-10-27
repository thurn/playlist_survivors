//! Causal, snapshot-owned presentation timing for scheduled bosses.
use stream::Beat;
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BossCueKind {
    Warning,
    Landed,
    Defeated,
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BossCue {
    pub kind: BossCueKind,
    pub pending: usize,
    pub song: f64,
    pub run: f64,
    pub id: usize,
    pub x: f64,
    pub y: f64,
}
#[derive(Clone, Debug, Default, PartialEq)]
pub struct BossPresentation {
    pub warned: Vec<usize>,
    pub pending: usize,
    pub warning_run: Option<f64>,
    pub landed_run: Option<f64>,
    /// Every actual landing, independent of the aggregate notice envelope.
    pub last_landed_run: Option<f64>,
}
impl BossPresentation {
    pub fn zoom(&self, run: f64) -> f64 {
        let Some(start) = self.warning_run else {
            return 1.0;
        };
        let ease = |x: f64| {
            let x = x.clamp(0.0, 1.0);
            x * x * (3.0 - 2.0 * x)
        };
        let enter = ease((run - start) / 0.6);
        let leave = self
            .landed_run
            .map_or(1.0, |at| 1.0 - ease((run - at) / 1.4));
        1.0 - 0.16 * enter * leave
    }
}
/// Native bars contain two beats. Extrapolate only from observations already known now;
/// unavailable grid falls back to the raw deadline. Future-tail edits cannot affect this.
pub fn boss_bar(due: f64, now: f64, beats: &[Beat]) -> (f64, f64) {
    if let Some((_, b)) = beats
        .iter()
        .enumerate()
        .filter(|(i, b)| i.is_multiple_of(2) && b.known_at <= now + 1e-9 && b.t >= due - 1e-9)
        .min_by(|a, b| a.1.t.total_cmp(&b.1.t))
    {
        return (b.t, b.period);
    }

    let Some((i, b)) = beats
        .iter()
        .enumerate()
        .rev()
        .find(|(_, b)| b.known_at <= now + 1e-9 && b.period.is_finite() && b.period > 0.0)
    else {
        return (due, 0.5);
    };
    let beat = i as f64 + (due - b.t) / b.period;
    let bar = ((beat - 1e-8) / 2.0).ceil() * 2.0;
    (b.t + (bar - i as f64) * b.period, b.period)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bars_use_known_prefix_and_two_native_bars() {
        let mut beats = vec![
            Beat {
                t: 0.0,
                known_at: 0.0,
                period: 0.5,
                evidence: 1.0,
            },
            Beat {
                t: 99.0,
                known_at: 100.0,
                period: 9.0,
                evidence: 1.0,
            },
        ];
        assert_eq!(boss_bar(60.2, 58.0, &beats), (61.0, 0.5));
        beats[1].t = 300.0;
        beats[1].known_at = 100.0;
        assert_eq!(boss_bar(60.2, 58.0, &beats), (61.0, 0.5));
        assert_eq!(boss_bar(60.0, 58.0, &beats).0 - 4.0 * 0.5, 58.0);
    }
    #[test]
    fn camera_eases_and_uses_held_run_clock() {
        let p = BossPresentation {
            warning_run: Some(58.0),
            landed_run: Some(60.0),
            ..Default::default()
        };
        assert_eq!(p.zoom(58.0), 1.0);
        assert_eq!(p.zoom(59.0), 0.84);
        assert!(p.zoom(60.7) > 0.84);
        assert_eq!(p.zoom(62.0), 1.0);
    }
    #[test]
    fn actual_committed_downbeats_win_over_period_extrapolation() {
        let beats: Vec<_> = [0.0, 0.4, 0.91, 1.3]
            .into_iter()
            .map(|t| Beat {
                t,
                known_at: 0.0,
                period: 0.5,
                evidence: 1.0,
            })
            .collect();
        assert_eq!(boss_bar(0.8, 0.8, &beats), (0.91, 0.5));
    }
}
