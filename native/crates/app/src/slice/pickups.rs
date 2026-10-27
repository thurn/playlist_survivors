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

/// Rollback-safe: the authoritative resume record supplies both origin and held age.
pub fn celebration(
    view: &eval::live::LiveView,
    art: &render::atlas::Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
    lights: &mut Vec<Light>,
) {
    let Some((run, pulse)) = view
        .run
        .and_then(|run| run.upgrade.map(|pulse| (run, pulse)))
    else {
        return;
    };
    let age = run.world - pulse.world;
    if !(0.0..0.7).contains(&age) {
        return;
    }
    let progress = (age / 0.7) as f32;
    let fade = (1.0 - progress).powi(2);
    let (x, y) = at(pulse.x, pulse.y);
    out.push(Sprite::arc(
        x,
        y,
        pulse.radius as f32 * scale * (1.0 - (1.0 - progress).powi(3)),
        Arc {
            head: 0.0,
            span: std::f32::consts::TAU,
            width: (2.0 + 3.0 * fade) * scale,
        },
        [1.0, 0.8, 0.35, 0.7 * fade],
    ));
    if let Some(&glow) = art.proc.get("glow") {
        for i in 0..12 {
            let angle = i as f32 * std::f32::consts::TAU / 12.0 + progress * 0.6;
            let radius = (20.0 + 85.0 * progress) * scale;
            let size = (5.0 + 12.0 * fade) * scale;
            out.push(
                Sprite::centred(
                    glow,
                    x + angle.cos() * radius,
                    y + angle.sin() * radius,
                    size,
                    size,
                )
                .tinted([1.0, 0.85, 0.4, 0.65 * fade])
                .additive(),
            );
        }
    }
    lights.push(Light {
        x,
        y,
        radius: 130.0 * scale,
        color: [1.0, 0.8, 0.35],
        strength: 0.22 * fade,
    });
}
