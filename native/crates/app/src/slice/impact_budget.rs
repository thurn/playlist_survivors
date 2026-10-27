//! Presentation-only density budget; applied damage and enemy reactions remain untouched.
use std::collections::{BTreeSet, VecDeque};
#[derive(Default)]
pub struct Budget {
    recent: VecDeque<(f64, usize)>,
    bucket: i64,
    cells: BTreeSet<(i32, i32, bool, bool)>,
    hero: Option<(f64, f64)>,
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
            && (id.starts_with("hit-")
                || id == "blades-hit"
                || id.starts_with("death-")
                || matches!(id, "blast-hit" | "lightning-hit" | "fire-hit"))
        {
            let density = self.admit(x, y, id.starts_with("death-"))?;
            start.density = Some(density);
            start.radiance = Some(self.density.max(0.2));
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
    pub fn dense(&self) -> bool {
        self.density < 0.99
    }
    pub fn focus(&mut self, hero: Option<(f64, f64)>) {
        self.hero = hero;
    }
    fn near(&self, x: f64, y: f64) -> bool {
        self.hero.is_some_and(|h| (x - h.0).hypot(y - h.1) <= 160.0)
    }
    pub fn flash_at(&self, x: f64, y: f64, protected: bool) -> f64 {
        if protected || self.near(x, y) {
            1.0
        } else {
            self.density.max(0.2)
        }
    }
    /// Separate hit/death cells preserve a death burst when a nearby hit already spent a cell.
    pub fn admit(&mut self, x: f64, y: f64, death: bool) -> Option<f64> {
        let near = self.near(x, y);
        let cell = (
            (x / 48.0).floor() as i32,
            (y / 48.0).floor() as i32,
            death,
            near,
        );
        if self
            .cells
            .iter()
            .filter(|cell| cell.2 == death && cell.3 == near)
            .count()
            >= if near { 8 } else { 24 }
            || !self.cells.insert(cell)
        {
            self.merged += 1;
            return None;
        }
        self.accepted += 1;
        Some(if near { 1.0 } else { self.density })
    }
}
#[cfg(test)]
mod tests {
    use super::super::fx::Start;
    use super::*;
    #[test]
    fn foreground_has_reserved_full_reactions_after_background_is_full() {
        let mut b = Budget::default();
        b.focus(Some((0.0, 0.0)));
        b.observe(1.0, 400);
        for i in 0..24 {
            assert!(
                b.start(
                    "hit-light",
                    1000.0 + f64::from(i) * 48.0,
                    0.0,
                    Start::default()
                )
                .is_some()
            );
        }
        let near = b.start("hit-light", 0.0, 0.0, Start::default()).unwrap();
        assert_eq!(near.density, Some(1.0));
        assert_eq!(near.scale, Some(1.0));
        assert_eq!(b.flash_at(0.0, 0.0, false), 1.0);
        assert!(b.flash_at(1000.0, 0.0, false) < 1.0);
        assert!(b.start("hit-light", 1.0, 1.0, Start::default()).is_none());
        assert_eq!(
            b.start("death-humanoid", 1.0, 1.0, Start::default())
                .unwrap()
                .density,
            Some(1.0)
        );
        assert!(
            near.radiance.unwrap() < 1.0,
            "background illumination cannot wash out foreground geometry"
        );
    }
    #[test]
    fn weapon_catches_cannot_spend_the_reserved_death_cell() {
        for id in ["lightning-hit", "fire-hit", "blast-hit"] {
            let mut b = Budget::default();
            b.focus(Some((0.0, 0.0)));
            b.observe(1.0, 400);
            assert!(b.start(id, 0.0, 0.0, Start::default()).is_some());
            assert!(
                b.start("death-skeleton", 0.0, 0.0, Start::default())
                    .is_some()
            );
        }
    }
    #[test]
    fn boss_showpiece_bypasses_an_exhausted_ordinary_death_budget() {
        let mut b = Budget::default();
        b.observe(1.0, 400);
        for i in 0..24 {
            assert!(
                b.start("death-humanoid", f64::from(i) * 48.0, 0.0, Start::default())
                    .is_some()
            );
        }
        assert!(
            b.start("death-humanoid", 0.0, 0.0, Start::default())
                .is_none()
        );
        let boss = b.start("death-boss", 0.0, 0.0, Start::default()).unwrap();
        assert!(boss.density.is_none());
        assert!(boss.scale.is_none());
        let other_boss = b
            .start(
                "death-elite",
                0.0,
                0.0,
                Start {
                    protected: true,
                    ..Start::default()
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
