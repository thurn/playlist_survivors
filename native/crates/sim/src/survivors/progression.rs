//! Cumulative XP curve and Antonio's temporary Growth bonus (XP appendix).
use super::Stats;

pub fn total_xp(level: usize) -> f64 {
    let l = level as f64;
    (5.0 + 1.5 * (level / 20) as f64).min(8.0) * l * l
}

#[derive(Clone, Debug, PartialEq)]
pub struct Progress {
    pub stats: Stats,
    pub level: usize,
    pub xp: f64,
    /// SF7 consumes these one at a time while its card freeze stays open.
    pub pending_levels: usize,
}

impl Default for Progress {
    fn default() -> Self {
        Self {
            stats: Stats::default(),
            level: 1,
            xp: 0.0,
            pending_levels: 0,
        }
    }
}

impl Progress {
    pub fn growth(&self) -> f64 {
        self.stats.growth
            + if matches!(self.level, 20 | 40) {
                1.0
            } else {
                0.0
            }
    }

    /// Growth applies to the pickup at the current level, before resolving its thresholds.
    pub fn gain_xp(&mut self, value: f64) {
        self.xp += value * self.growth();
        while self.xp >= total_xp(self.level) {
            self.level += 1;
            self.pending_levels += 1;
        }
    }

    pub fn fill(&self) -> f64 {
        let previous = total_xp(self.level - 1);
        ((self.xp - previous) / (total_xp(self.level) - previous)).clamp(0.0, 1.0)
    }
}
