//! The effects' level follower (`runtime/audio.ts`'s `followCurve`, made causal): when the music
//! is quieter than its loud passages, the effects come down with it so the music stays in front.
//! The TS game measured the whole song first; this hears it as it plays: the level over the last
//! 0.4 s, ten times a second, averaged over the last second, against the 90th percentile of the
//! last minute's levels.

/// Levels a second.
const RATE: f64 = 10.0;
/// The window each level measures (s).
const WINDOW_S: f64 = 0.4;
/// The levels averaged (one second's).
const SMOOTH: usize = 10;
/// The levels the reference is a percentile of (a minute's), and which.
const HISTORY: usize = 600;
const PERCENTILE: f64 = 0.9;
/// The effects move this fraction of the music's change in dB, and come down at most this far.
const FOLLOW: f64 = 0.75;
const MAX_CUT_DB: f64 = 10.0;

/// The follower's state: the hop being summed, the recent hops' energies and levels.
pub struct Follower {
    hop: usize,
    in_hop: usize,
    sum: f64,
    /// The last few hops' mean squares (the window).
    hops: Vec<f64>,
    window: usize,
    levels: Vec<f64>,
    history: Vec<f64>,
    scratch: Vec<f64>,
}

impl Follower {
    /// A follower of mono samples at `rate`.
    pub fn new(rate: f64) -> Follower {
        Follower {
            hop: (rate / RATE).round().max(1.0) as usize,
            in_hop: 0,
            sum: 0.0,
            hops: Vec::new(),
            window: (WINDOW_S * RATE).round() as usize,
            levels: Vec::new(),
            history: Vec::new(),
            scratch: Vec::with_capacity(HISTORY),
        }
    }

    /// Hears `samples`; returns the effects' new gain whenever a level completes (the last one if
    /// several do).
    pub fn push(&mut self, samples: &[f32]) -> Option<f32> {
        let mut gain = None;
        for &x in samples {
            self.sum += f64::from(x) * f64::from(x);
            self.in_hop += 1;
            if self.in_hop == self.hop {
                gain = Some(self.level(self.sum / self.hop as f64));
                self.sum = 0.0;
                self.in_hop = 0;
            }
        }
        gain
    }

    fn level(&mut self, mean_square: f64) -> f32 {
        push_bounded(&mut self.hops, mean_square, self.window);
        let ms = self.hops.iter().sum::<f64>() / self.hops.len() as f64;
        push_bounded(&mut self.levels, 10.0 * ms.max(1e-12).log10(), SMOOTH);
        let smooth = self.levels.iter().sum::<f64>() / self.levels.len() as f64;
        push_bounded(&mut self.history, smooth, HISTORY);
        self.scratch.clear();
        self.scratch.extend_from_slice(&self.history);
        self.scratch.sort_by(f64::total_cmp);
        let reference = self.scratch[(PERCENTILE * (self.scratch.len() - 1) as f64) as usize];
        let db = (FOLLOW * (smooth - reference)).clamp(-MAX_CUT_DB, 0.0);
        10f64.powf(db / 20.0) as f32
    }
}

fn push_bounded(v: &mut Vec<f64>, x: f64, n: usize) {
    if v.len() == n {
        v.remove(0);
    }
    v.push(x);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn tone(amplitude: f32, seconds: f64) -> Vec<f32> {
        (0..(seconds * 1000.0) as usize)
            .map(|i| amplitude * (i as f32 * 0.3).sin())
            .collect()
    }

    #[test]
    fn the_effects_follow_a_quiet_passage_down_and_come_back() {
        let mut f = Follower::new(1000.0);
        let loud = f.push(&tone(0.5, 20.0)).unwrap();
        assert!((loud - 1.0).abs() < 1e-3, "full in the loud part: {loud}");
        // 12 dB quieter for 5 s: down 9 dB (0.75 of it), under the 10 dB floor.
        let quiet = f.push(&tone(0.125, 5.0)).unwrap();
        let db = 20.0 * quiet.log10();
        assert!((db + 9.0).abs() < 0.5, "{db} dB");
        let back = f.push(&tone(0.5, 3.0)).unwrap();
        assert!((back - 1.0).abs() < 1e-3, "{back}");
        // Silence comes down at most 10 dB.
        let silent = f.push(&vec![0.0; 5000]).unwrap();
        assert!((20.0 * silent.log10() + MAX_CUT_DB as f32).abs() < 1e-3);
    }

    #[test]
    fn a_level_completes_ten_times_a_second() {
        let mut f = Follower::new(48_000.0);
        assert_eq!(f.push(&vec![0.1; 4799]), None);
        assert!(f.push(&[0.1]).is_some());
    }
}
