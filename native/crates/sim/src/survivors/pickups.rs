//! Source pickup geometry and the 60Hz outward-kick/accelerated pull (appendix-feel).
use super::PX_PER_VS_UNIT;
pub const GEM_BODY_RADIUS: f64 = 0.1 * PX_PER_VS_UNIT;
pub const GEM_TARGET_LIFT: f64 = 0.08 * PX_PER_VS_UNIT;
pub const CHICKEN_IMMUNITY_S: f64 = 0.3;
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GemPose {
    pub id: usize,
    pub w: f64,
    pub x: f64,
    pub y: f64,
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum FloorChicken {
    Drop { id: usize, x: f64, y: f64 },
    Taken { id: usize },
    Healed { id: usize, amount: f64 },
}
#[derive(Clone, Copy, Debug)]
pub(crate) struct Attraction {
    pub x: f64,
    pub y: f64,
    pub speed: f64,
    pub start_w: f64,
    pub next_w: f64,
}
impl Attraction {
    pub fn new(w: f64, x: f64, y: f64) -> Self {
        Self {
            x,
            y,
            speed: 250.0,
            start_w: w,
            next_w: w + 1.0 / 60.0,
        }
    }
    pub fn update(&mut self, hero: (f64, f64), hero_radius: f64) -> bool {
        let target = (hero.0, hero.1 - GEM_TARGET_LIFT);
        let dx = target.0 - self.x;
        let dy = target.1 - self.y;
        let d = dx.hypot(dy);
        let radius = hero_radius + GEM_BODY_RADIUS;
        let time = ((self.next_w - self.start_w) / 0.5 * 2.0 - 1.0).clamp(-1.0, 1.0);
        let step = self.speed * 0.01 * PX_PER_VS_UNIT * time / 60.0;
        let old = (self.x, self.y);
        if d > 1e-9 {
            self.x += dx / d * step;
            self.y += dy / d * step;
        }
        let seg = (self.x - old.0, self.y - old.1);
        let len2 = seg.0 * seg.0 + seg.1 * seg.1;
        let t = if len2 > 1e-12 {
            ((hero.0 - old.0) * seg.0 + (hero.1 - old.1) * seg.1) / len2
        } else {
            0.0
        };
        let closest = (
            old.0 + seg.0 * t.clamp(0.0, 1.0),
            old.1 + seg.1 * t.clamp(0.0, 1.0),
        );
        let collected =
            (hero.0 - closest.0).hypot(hero.1 - closest.1) <= radius || self.speed > 65536.0;
        if collected {
            self.x = target.0;
            self.y = target.1;
        }
        self.speed += 1.0;
        self.next_w += 1.0 / 60.0;
        collected
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pickup_source_attraction_updates_at_60hz_and_increments_legacy_speed_once() {
        let mut a = Attraction::new(0.0, 100.0, 0.0);
        assert!(!a.update((0.0, 0.0), 10.0));
        assert!(a.x > 100.0);
        assert_eq!(a.speed, 251.0);
        for _ in 1..30 {
            a.update((0.0, 0.0), 10.0);
        }
        assert_eq!(a.speed, 280.0);
        assert!((a.next_w - 31.0 / 60.0).abs() < 1e-9);
    }
}
