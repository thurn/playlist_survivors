//! Sky bolts start only from actual simulation strikes, not an anticipated target.
use super::{fx::rand01, tier};
use render::{
    post::{Grade, Light},
    sprites::{Arc, Sprite},
};
use sim::survivors::LightningStrike;
#[derive(Default)]
pub struct Lightning {
    strikes: Vec<(LightningStrike, f64)>,
}
impl Lightning {
    pub fn struck(&mut self, strike: LightningStrike, now: f64) {
        self.strikes.retain(|(_, born)| now - born < 0.18);
        if self.strikes.len() == 64 {
            self.strikes.remove(0);
        }
        self.strikes.push((strike, now));
    }
    #[allow(clippy::too_many_arguments)]
    pub fn draw(
        &self,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        height: f32,
        out: &mut Vec<Sprite>,
        lights: &mut Vec<Light>,
        grade: &mut Grade,
    ) {
        for &(strike, born) in &self.strikes {
            let age = now - born;
            if !(0.0..0.18).contains(&age) {
                continue;
            }
            let fade = (1.0 - age as f32 / 0.18).powi(2);
            let (x, y) = at(strike.x, strike.y);
            let color = if strike.accent {
                [1.0, 0.8, 0.35]
            } else {
                [0.5, 0.75, 1.0]
            };
            let sky = (y - height * 0.9).min(-24.0 * scale);
            let offset = (rand01(strike.id as u64, 1, 0xB017) - 0.5) as f32 * 65.0 * scale;
            let points = [
                (x + offset, sky),
                (x - offset * 0.7, sky + (y - sky) * 0.45),
                (x + offset * 0.3, sky + (y - sky) * 0.75),
                (x, y),
            ];
            tier::bolt(
                &points,
                strike.id as u64,
                now,
                if strike.accent {
                    7.0 * scale
                } else {
                    4.5 * scale
                },
                [color[0], color[1], color[2], 0.75 * fade],
                fade,
                scale,
                out,
            );
            let r = strike.radius as f32 * scale;
            out.push(Sprite::arc(
                x,
                y,
                r * (1.0 + 0.2 * age as f32 / 0.18),
                Arc {
                    head: 0.0,
                    span: std::f32::consts::TAU,
                    width: 2.0 * scale,
                },
                [color[0], color[1], color[2], 0.6 * fade],
            ));
            lights.push(Light {
                x,
                y,
                radius: (r * 2.0).max(45.0 * scale),
                color,
                strength: 0.5 * fade,
            });
            let flash =
                (1.0 - age as f32 / 0.05).max(0.0) * if strike.accent { 0.08 } else { 0.04 };
            if flash > grade.flash[3] {
                grade.flash = [color[0], color[1], color[2], flash];
            }
        }
    }
}
