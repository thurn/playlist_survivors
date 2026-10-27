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
        let y = height * 0.47;
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
                    Sprite::centred(*glow, x, y - height * 0.2, 28.0 * unit, height * 0.8)
                        .tinted([1.0, 0.85, 0.3, 0.8])
                        .additive(),
                );
                for k in 0..12 {
                    let theta = k as f32 * std::f32::consts::TAU / 12.0;
                    overlay.push(
                        Sprite::centred(
                            *glow,
                            x + theta.cos() * 55.0 * unit,
                            y + theta.sin() * 55.0 * unit,
                            7.0 * unit,
                            16.0 * unit,
                        )
                        .rotated(theta)
                        .tinted([1.0, 0.9, 0.5, 0.8])
                        .additive(),
                    );
                }
            }
        }
        if let Some(icon) = art.icons.get(super::cards::icon(item)) {
            overlay.push(
                Sprite::centred(*icon, x, y, 58.0 * unit, 58.0 * unit)
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
