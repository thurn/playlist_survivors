//! Short food feedback follows actual grid heal records, never the pickup's prediction.
pub mod check;
use render::{
    post::Light,
    sprites::{Arc, Sprite},
};
#[derive(Default)]
pub struct Pickups {
    heal: Option<(f64, f64)>,
}
impl Pickups {
    pub fn healed(&mut self, now: f64, amount: f64) {
        self.heal = Some((now, amount));
    }
    pub fn draw(
        &self,
        now: f64,
        at: (f32, f32),
        scale: f32,
        out: &mut Vec<Sprite>,
        lights: &mut Vec<Light>,
    ) {
        let Some((born, amount)) = self.heal else {
            return;
        };
        let age = now - born;
        if !(0.0..0.5).contains(&age) {
            return;
        }
        let p = (age / 0.5) as f32;
        let fade = (1.0 - p).powi(2) * if amount > 0.0 { 1.0 } else { 0.5 };
        out.push(Sprite::arc(
            at.0,
            at.1,
            (18.0 + 45.0 * p) * scale,
            Arc {
                head: 0.0,
                span: std::f32::consts::TAU,
                width: 3.0 * scale,
            },
            [0.4, 1.0, 0.55, 0.65 * fade],
        ));
        lights.push(Light {
            x: at.0,
            y: at.1,
            radius: 65.0 * scale,
            color: [0.3, 1.0, 0.5],
            strength: 0.25 * fade,
        });
    }
}
