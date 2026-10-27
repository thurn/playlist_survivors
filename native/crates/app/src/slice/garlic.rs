//! A persistent hero-centred aura; published shots only refresh its presentation.
use eval::live::LiveView;
use render::{
    atlas::Atlases,
    post::{Grade, Light},
    sprites::{Arc, Sprite},
};
use sim::survivors::{Shot, WeaponId};
#[derive(Default)]
pub struct Garlic {
    shot: Option<(Shot, f64)>,
}
impl Garlic {
    pub fn shot(&mut self, shot: Shot, now: f64) {
        self.shot = Some((shot, now));
    }
    #[allow(clippy::too_many_arguments)]
    pub fn draw(
        &self,
        view: &LiveView,
        now: f64,
        beat: f64,
        art: &Atlases,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        out: &mut Vec<Sprite>,
        lights: &mut Vec<Light>,
        grade: &mut Grade,
    ) {
        let Some((shot, born)) = self.shot else {
            return;
        };
        if view
            .inventory
            .is_none_or(|i| i.weapons.rank(shot.id).is_none())
        {
            return;
        }
        let Some(hero) = view.hero else { return };
        let (x, y) = at(hero.x, hero.y);
        let kick = (-7.0 * beat.fract().max(0.0)).exp() as f32;
        let accent = if shot.accent {
            (1.0 - (now - born) as f32 / 0.45).clamp(0.0, 1.0)
        } else {
            0.0
        };
        let radius = shot.radius as f32 * scale;
        let color = if shot.id == WeaponId::SoulEater {
            [0.7 + 0.3 * accent, 0.4 + 0.4 * accent, 1.0]
        } else {
            [0.4 + 0.6 * accent, 1.0, 0.65 - 0.3 * accent]
        };
        if let Some(glow) = art.proc.get("glow") {
            out.push(
                Sprite::centred(*glow, x, y, 2.3 * radius, 2.3 * radius)
                    .tinted([color[0], color[1], color[2], 0.07 + 0.05 * kick])
                    .additive(),
            );
        }
        for (ratio, alpha, width) in [(1.0, 0.5, 2.0), (0.92, 0.18, 5.0), (0.76, 0.1, 2.0)] {
            out.push(Sprite::arc(
                x,
                y,
                radius * ratio * (1.0 + 0.025 * kick),
                Arc {
                    head: 0.0,
                    span: std::f32::consts::TAU,
                    width: width * scale,
                },
                [
                    color[0],
                    color[1],
                    color[2],
                    alpha * (0.6 + 0.4 * kick) + 0.15 * accent,
                ],
            ));
        }
        lights.push(Light {
            x,
            y,
            radius: radius * 1.4,
            color,
            strength: 0.1 + 0.08 * kick + 0.15 * accent,
        });
        grade.aura = [x, y, radius, 0.18 + 0.22 * kick + 0.25 * accent];
    }
}
