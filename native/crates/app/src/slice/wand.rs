//! Glowing streaks use only the view's bounded, rollback-restored causal samples.
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
    for trail in view.missiles.values() {
        let Some(head) = trail.last() else {
            continue;
        };
        if !matches!(
            head.weapon,
            sim::survivors::WeaponId::MagicWand | sim::survivors::WeaponId::HolyWand
        ) {
            continue;
        }
        let color = if head.accent {
            [0.8, 0.65, 1.0]
        } else if head.weapon == sim::survivors::WeaponId::HolyWand {
            [1.0, 0.85, 0.4]
        } else {
            [0.35, 0.85, 1.0]
        };
        for (i, segment) in trail.windows(2).enumerate() {
            let a = at(segment[0].x, segment[0].y);
            let b = at(segment[1].x, segment[1].y);
            let dx = b.0 - a.0;
            let dy = b.1 - a.1;
            let length = dx.hypot(dy);
            if length < 0.01 {
                continue;
            }
            let alpha = (i + 1) as f32 / trail.len() as f32;
            for (width, tint) in [
                (6.0, [color[0], color[1], color[2], alpha * 0.35]),
                (1.5, [1.0, 1.0, 1.0, alpha * 0.8]),
            ] {
                out.push(
                    Sprite::solid(
                        f32::midpoint(a.0, b.0) - length / 2.0,
                        f32::midpoint(a.1, b.1) - width * scale / 2.0,
                        length,
                        width * scale,
                        tint,
                    )
                    .rotated(dy.atan2(dx))
                    .additive(),
                );
            }
        }
        let (x, y) = at(head.x, head.y);
        if let Some(glow) = art.proc.get("glow") {
            let size = if head.accent { 30.0 } else { 22.0 } * scale;
            out.push(
                Sprite::centred(*glow, x, y, size, size)
                    .tinted([color[0], color[1], color[2], 0.8])
                    .additive(),
            );
        }
        out.push(
            Sprite::solid(
                x - 1.5 * scale,
                y - 1.5 * scale,
                3.0 * scale,
                3.0 * scale,
                [1.0; 4],
            )
            .additive(),
        );
        lights.push(Light {
            x,
            y,
            radius: 20.0 * scale,
            color,
            strength: 0.35,
        });
    }
}
