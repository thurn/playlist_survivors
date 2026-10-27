//! Chest state and roulette progress come only from the simulation, including after rollback.
pub mod check;
use eval::live::LiveView;
use render::{atlas::Atlases, font::Align, sprites::Sprite};
use sim::survivors::{Item, WeaponId};
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
    for c in view.chests.values() {
        let (x, y) = at(c.x, c.y);
        let pulse = (0.5 + 0.5 * (view.s * 4.0 + c.id as f64).sin()) as f32;
        if let Some(glow) = art.proc.get("glow") {
            world.push(
                Sprite::centred(*glow, x, y - 105.0 * scale, 18.0 * scale, 220.0 * scale)
                    .tinted([1.0, 0.72, 0.18, 0.22 + 0.12 * pulse])
                    .additive(),
            );
            for k in 0..5 {
                let phase = (view.s as f32 * 0.6 + k as f32 / 5.0).fract();
                world.push(
                    Sprite::centred(
                        *glow,
                        x + (k as f32 * 2.4).sin() * 15.0 * scale,
                        y - phase * 100.0 * scale,
                        4.0 * scale,
                        7.0 * scale,
                    )
                    .tinted([1.0, 0.85, 0.35, (1.0 - phase) * 0.7])
                    .additive(),
                );
            }
        }
        if let Some(glow) = art.proc.get("glow") {
            world.push(
                Sprite::centred(*glow, x, y, 65.0 * scale, 45.0 * scale)
                    .tinted([1.0, 0.65, 0.12, 0.5])
                    .additive(),
            );
        }
        world.push(Sprite::solid(
            x - 12.0 * scale,
            y - 16.0 * scale,
            24.0 * scale,
            16.0 * scale,
            [0.7, 0.32, 0.06, 1.0],
        ));
        world.push(Sprite::solid(
            x - 12.0 * scale,
            y - 14.0 * scale,
            24.0 * scale,
            3.0 * scale,
            [1.0, 0.8, 0.25, 1.0],
        ));
        world.push(Sprite::solid(
            x - 2.0 * scale,
            y - 12.0 * scale,
            4.0 * scale,
            6.0 * scale,
            [1.0, 0.9, 0.4, 1.0],
        ));
    }
    let Some(roulette) = view.run.and_then(|roulette| roulette.roulette) else {
        return;
    };
    let unit = (width / 960.0).min(height / 540.0);
    let age = roulette.landed.map_or(0.0, |s| (view.s - s).max(0.0)) as f32;
    let burst = (-age * 3.0).exp();
    let face = &art.fonts["pixel"];
    overlay.push(Sprite::solid(
        0.0,
        0.0,
        width,
        height,
        [0.008, 0.005, 0.02, 0.58],
    ));
    face.text(
        if roulette.landed.is_some() {
            "TREASURE FOUND"
        } else {
            "TREASURE"
        },
        width / 2.0,
        height * 0.27,
        3.0 * unit,
        Align::Centre,
        [1.0, 0.8, 0.3, 1.0],
        overlay,
    );
    let item_count = sim::survivors::chest_items(roulette.tier);
    for index in 0..item_count {
        let x = width / 2.0 + (index as f32 - (item_count - 1) as f32 / 2.0) * 126.0 * unit;
        let spin = if roulette.landed.is_none() {
            (view.s as f32 * 18.0).sin() * (1.0 - f32::from(roulette.tick) / 9.0) * 8.0 * unit
        } else {
            0.0
        };
        let y = height * 0.47 + spin;
        let item = roulette.rewards[index].unwrap_or(Item::Weapon(
            [
                WeaponId::Sword,
                WeaponId::Garlic,
                WeaponId::MagicWand,
                WeaponId::KingBible,
                WeaponId::LightningRing,
            ][(usize::from(roulette.tick) + index) % 5],
        ));
        if let Some(glow) = art.proc.get("glow") {
            overlay.push(
                Sprite::centred(*glow, x, y, 108.0 * unit, 108.0 * unit)
                    .tinted([1.0, 0.6, 0.12, 0.5])
                    .additive(),
            );
            if roulette.landed.is_some() {
                overlay.push(
                    Sprite::centred(
                        *glow,
                        x,
                        y - height * 0.2,
                        (18.0 + 30.0 * burst) * unit,
                        height * 0.8,
                    )
                    .tinted([1.0, 0.85, 0.3, 0.8])
                    .additive(),
                );
                for k in 0..12 {
                    let theta = k as f32 * std::f32::consts::TAU / 12.0 + age * 0.35;
                    let radius = 55.0 + age.min(0.8) * 75.0;
                    overlay.push(
                        Sprite::centred(
                            *glow,
                            x + theta.cos() * radius * unit,
                            y + theta.sin() * radius * unit,
                            7.0 * unit,
                            16.0 * unit,
                        )
                        .rotated(theta)
                        .tinted([1.0, 0.9, 0.5, 0.25 + 0.55 * burst])
                        .additive(),
                    );
                }
            }
        }
        if let Some(icon) = art.icons.get(super::cards::icon(item)) {
            overlay.push(
                Sprite::centred(
                    *icon,
                    x,
                    y,
                    (58.0 + 10.0 * burst) * unit,
                    (58.0 + 10.0 * burst) * unit,
                )
                .tinted([1.0, 0.9, 0.55, 1.0]),
            );
        }
        if roulette.landed.is_some() {
            face.text(
                item.name(),
                x,
                y + 65.0 * unit,
                1.0 * unit,
                Align::Centre,
                [1.0; 4],
                overlay,
            );
        }
    }
    face.text(
        if roulette.landed.is_none() {
            "TWO BARS  /  LAND ON THE DOWNBEAT"
        } else {
            "REWARDS RECEIVED"
        },
        width / 2.0,
        height * 0.72,
        1.3 * unit,
        Align::Centre,
        [0.85, 0.9, 1.0, 1.0],
        overlay,
    );
}
