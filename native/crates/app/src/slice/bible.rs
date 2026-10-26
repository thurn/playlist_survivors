//! Existing orbiting-blade art follows bounded causal projectile samples.
use eval::live::LiveView;
use render::{atlas::Atlases, post::Light, sprites::Sprite};
pub fn draw(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
    lights: &mut Vec<Light>,
) {
    let Some(blade) = art.proc.get("blade") else {
        return;
    };
    for trail in view.missiles.values() {
        let Some(head) = trail.last() else {
            continue;
        };
        if !matches!(
            head.weapon,
            sim::survivors::WeaponId::KingBible | sim::survivors::WeaponId::UnholyVespers
        ) {
            continue;
        }
        let color = if head.accent {
            [1.0, 0.7, 0.3]
        } else {
            [0.6, 0.8, 1.0]
        };
        let grow = (head.radius / (0.08 * sim::survivors::PX_PER_VS_UNIT)) as f32;
        if grow <= 0.001 {
            continue;
        }
        for (i, p) in trail.iter().rev().step_by(2).take(4).enumerate().rev() {
            let (x, y) = at(p.x, p.y);
            let alpha = if i == 0 { 1.0 } else { 0.22 / (i as f32) };
            let turn = (p.vy.atan2(p.vx) - std::f64::consts::FRAC_PI_2) * 3.0;
            out.push(
                Sprite::centred(
                    *blade,
                    x,
                    y,
                    blade.w as f32 * grow * scale,
                    blade.h as f32 * grow * scale,
                )
                .rotated(turn as f32)
                .tinted([color[0], color[1], color[2], alpha])
                .additive(),
            );
            if i == 0 {
                if let Some(glow) = art.proc.get("glow") {
                    out.push(
                        Sprite::centred(*glow, x, y, 40.0 * grow * scale, 40.0 * grow * scale)
                            .tinted([color[0], color[1], color[2], 0.3])
                            .additive(),
                    );
                }
                lights.push(Light {
                    x,
                    y,
                    radius: 35.0 * grow * scale,
                    color,
                    strength: 0.12,
                });
            }
        }
    }
}
