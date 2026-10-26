//! Short, bounded spin afterimages driven only by published shots, never by the simulation.
use render::{
    atlas::Atlases,
    post::Light,
    sprites::{Arc, Sprite},
};
use sim::survivors::Shot;
#[derive(Clone, Copy)]
struct Spin {
    shot: Shot,
    at: f64,
}
#[derive(Default)]
pub struct Sword {
    spins: std::collections::VecDeque<Spin>,
}
impl Sword {
    pub fn shot(&mut self, shot: Shot, now: f64) {
        self.spins.retain(|s| now - s.at < 0.45);
        if self.spins.len() == 16 {
            self.spins.pop_front();
        }
        self.spins.push_back(Spin { shot, at: now });
    }
    pub fn draw(
        &mut self,
        now: f64,
        art: &Atlases,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        out: &mut Vec<Sprite>,
        lights: &mut Vec<Light>,
    ) {
        self.spins.retain(|s| now - s.at < 0.45);
        for spin in &self.spins {
            let shot = spin.shot;
            let life = shot.period.clamp(0.16, 0.28);
            let age = (now - spin.at).max(0.0);
            let (x, y) = at(shot.x, shot.y);
            let radius = shot.radius as f32 * scale;
            let color = if shot.accent {
                [1.0, 0.75, 0.4]
            } else {
                [1.0, 0.28, 0.58]
            };
            if age < life {
                let p = (age / life) as f32;
                let fade = (1.0 - p).powf(0.7);
                let head = -std::f32::consts::FRAC_PI_2
                    + std::f32::consts::TAU * (1.0 - (1.0 - p).powi(2));
                let span = 1.3 + 2.0 * (std::f32::consts::PI * p).sin();
                let size = radius * if shot.accent { 1.2 } else { 1.0 };
                for i in (0..3).rev() {
                    let tail = i as f32;
                    out.push(Sprite::arc(
                        x,
                        y,
                        size * (1.0 - tail * 0.025),
                        Arc {
                            head: head - tail * 0.42,
                            span,
                            width: scale * (if shot.accent { 10.0 } else { 6.0 }),
                        },
                        [color[0], color[1], color[2], fade / (1.0 + tail * 2.0)],
                    ));
                }
                out.push(Sprite::arc(
                    x,
                    y,
                    size,
                    Arc {
                        head,
                        span: span * 0.4,
                        width: 2.0 * scale,
                    },
                    [1.0, 0.95, 1.0, fade * 0.9],
                ));
                if let Some(glow) = art.proc.get("glow") {
                    let tip = (x + head.cos() * size, y + head.sin() * size);
                    out.push(
                        Sprite::centred(*glow, tip.0, tip.1, 28.0 * scale, 28.0 * scale)
                            .tinted([color[0], color[1], color[2], fade * 0.6])
                            .additive(),
                    );
                }
                lights.push(Light {
                    x,
                    y,
                    radius: size * 1.5,
                    color,
                    strength: fade * if shot.accent { 0.7 } else { 0.35 },
                });
            }
            if shot.accent && age < 0.4 {
                let p = (age / 0.4) as f32;
                out.push(Sprite::arc(
                    x,
                    y,
                    radius * (0.85 + 0.65 * p),
                    Arc {
                        head: 0.0,
                        span: std::f32::consts::TAU,
                        width: 3.0 * scale,
                    },
                    [1.0, 0.75, 0.45, 0.6 * (1.0 - p).powi(2)],
                ));
            }
        }
    }
}
