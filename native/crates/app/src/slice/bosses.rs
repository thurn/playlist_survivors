//! Boss presentation reads authoritative cue times and positions; it never runs combat.
use eval::live::LiveView;
use render::{atlas::Atlases, font::Align, sprites::Sprite};
use sim::survivors::BossCueKind;
#[allow(clippy::too_many_arguments)]
pub fn draw(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    width: f32,
    height: f32,
    world: &mut Vec<Sprite>,
    overlay: &mut Vec<Sprite>,
) {
    let Some(run) = view.run else {
        return;
    };
    let unit = (width / 960.0).min(height / 540.0);
    for cue in view
        .boss_cues
        .iter()
        .filter(|c| c.kind != BossCueKind::Warning)
        .chain(view.boss_warning.iter())
    {
        let age = (run.elapsed - cue.run) as f32;
        if age < 0.0 {
            continue;
        }
        if cue.kind == BossCueKind::Warning {
            let pulse = 0.72 + 0.28 * (age * std::f32::consts::TAU * 2.0).cos();
            overlay.push(Sprite::solid(
                0.0,
                height * 0.24,
                width,
                42.0 * unit,
                [0.13, 0.015, 0.025, 0.85],
            ));
            overlay.push(Sprite::solid(
                0.0,
                height * 0.24,
                width,
                2.0 * unit,
                [1.0, 0.33, 0.16, pulse],
            ));
            art.fonts["pixel"].text(
                "BOSS APPROACHING",
                width * 0.5,
                height * 0.24 + 12.0 * unit,
                2.0 * unit,
                Align::Centre,
                [1.0, 0.78, 0.45, 1.0],
                overlay,
            );
            continue;
        }
        let duration = if cue.kind == BossCueKind::Defeated {
            1.3
        } else {
            0.8
        };
        if age > duration {
            continue;
        }
        let progress = age / duration;
        let (x, y) = at(cue.x, cue.y);
        // A moved camera keeps the short arrival/death cue visible at its nearest edge.
        let (x, y) = (
            x.clamp(28.0 * unit, width - 28.0 * unit),
            y.clamp(56.0 * unit, height - 40.0 * unit),
        );
        let color = if cue.kind == BossCueKind::Defeated {
            [1.0, 0.83, 0.25]
        } else {
            [1.0, 0.28, 0.12]
        };
        if let Some(glow) = art.proc.get("glow") {
            for k in 0..24 {
                let angle = k as f32 * std::f32::consts::TAU / 24.0;
                let radius = (20.0 + progress * 100.0) * scale;
                world.push(
                    Sprite::centred(
                        *glow,
                        x + angle.cos() * radius,
                        y + angle.sin() * radius * 0.55,
                        9.0 * scale,
                        20.0 * scale,
                    )
                    .rotated(angle)
                    .tinted([color[0], color[1], color[2], (1.0 - progress) * 0.85])
                    .additive(),
                );
            }
        }
        if cue.kind == BossCueKind::Defeated {
            art.fonts["pixel"].text(
                "BOSS DEFEATED",
                width * 0.5,
                height * 0.23,
                2.0 * unit,
                Align::Centre,
                [1.0, 0.85, 0.4, 1.0 - progress],
                overlay,
            );
        }
    }
}
