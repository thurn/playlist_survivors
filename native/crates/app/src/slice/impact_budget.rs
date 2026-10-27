//! Presentation-only density budget; applied damage and enemy reactions remain untouched.
use std::collections::{BTreeSet, VecDeque};
#[derive(Default)]
pub struct Budget {
    recent: VecDeque<(f64, usize)>,
    bucket: i64,
    cells: BTreeSet<(i32, i32, bool)>,
    density: f64,
    pub requested: usize,
    pub accepted: usize,
    pub merged: usize,
}
impl Budget {
    pub fn start(
        &mut self,
        id: &str,
        x: f64,
        y: f64,
        mut start: super::fx::Start,
    ) -> Option<super::fx::Start> {
        if !start.protected
            && id != "death-boss"
            && (id.starts_with("hit-") || id == "blades-hit" || id.starts_with("death-"))
        {
            let density = self.admit(x, y, id.starts_with("death-"))?;
            start.density = Some(density);
            start.scale = Some(start.scale.unwrap_or(1.0) * density.sqrt());
        }
        Some(start)
    }

    pub fn observe(&mut self, now: f64, hits: usize) {
        self.requested += hits;
        self.recent.retain(|(t, _)| now - t < 0.15);
        if hits > 0 {
            self.recent.push_back((now, hits));
        }
        let n = self.recent.iter().map(|(_, n)| n).sum::<usize>();
        self.density = (8.0 / n.max(8) as f64).sqrt().clamp(0.2, 1.0);
        let bucket = (now / 0.05).floor() as i64;
        if self.bucket != bucket {
            self.cells.clear();
            self.bucket = bucket;
        }
    }
    pub fn flash(&self) -> f64 {
        self.density.max(0.2)
    }
    /// Separate hit/death cells preserve a death burst when a nearby hit already spent a cell.
    pub fn admit(&mut self, x: f64, y: f64, death: bool) -> Option<f64> {
        let cell = ((x / 48.0).floor() as i32, (y / 48.0).floor() as i32, death);
        if self.cells.iter().filter(|cell| cell.2 == death).count() >= 24
            || !self.cells.insert(cell)
        {
            self.merged += 1;
            return None;
        }
        self.accepted += 1;
        Some(self.density)
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn boss_showpiece_bypasses_an_exhausted_ordinary_death_budget() {
        let mut b = Budget::default();
        b.observe(1.0, 400);
        for i in 0..24 {
            assert!(
                b.start(
                    "death-humanoid",
                    f64::from(i) * 48.0,
                    0.0,
                    super::super::fx::Start::default()
                )
                .is_some()
            );
        }
        assert!(
            b.start(
                "death-humanoid",
                0.0,
                0.0,
                super::super::fx::Start::default()
            )
            .is_none()
        );
        let boss = b
            .start("death-boss", 0.0, 0.0, super::super::fx::Start::default())
            .unwrap();
        assert!(boss.density.is_none());
        assert!(boss.scale.is_none());
        let other_boss = b
            .start(
                "death-elite",
                0.0,
                0.0,
                super::super::fx::Start {
                    protected: true,
                    ..super::super::fx::Start::default()
                },
            )
            .unwrap();
        assert!(other_boss.density.is_none());
    }
    #[test]
    fn dense_hits_merge_shrink_and_recover_without_suppressing_death() {
        let mut b = Budget::default();
        b.observe(1.0, 200);
        assert_eq!(b.admit(0.0, 0.0, false), Some(0.2));
        assert_eq!(b.admit(1.0, 1.0, false), None);
        assert_eq!(b.admit(1.0, 1.0, true), Some(0.2));
        for i in 1..=23 {
            assert!(b.admit(f64::from(i) * 48.0, 0.0, false).is_some());
        }
        assert!(b.admit(2000.0, 0.0, false).is_none());
        b.observe(1.2, 1);
        assert_eq!(b.admit(0.0, 0.0, false), Some(1.0));
    }
}
